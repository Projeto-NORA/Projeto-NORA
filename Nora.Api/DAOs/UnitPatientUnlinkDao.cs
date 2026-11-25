using Npgsql;
using Nora.Api.Database;
using Nora.Api.Models;

namespace Nora.Api.DAOs;

/// <summary>Parte da remoção de vínculo (do paciente com a UBS, ou do responsável pela Família) que acontece no banco da UBS. O chamador já autorizou e segura o vínculo central.</summary>
public sealed class UnitPatientUnlinkDao(UnitDatabaseRouter databases, TimeProvider time)
{
    /// <summary>
    /// Numa única transação da UBS: cancela as consultas futuras scheduled/called do CPF (evento de origem 'patient', que pesa no
    /// score pela tabela normal) e as entradas ativas da fila, com as ofertas pendentes delas. Vagas ofertadas voltam a 'open'
    /// para serem redistribuídas pelo chamador.
    /// </summary>
    public async Task<UnlinkUnitResult> CancelPendingAsync(Guid unitId, Guid actorId, string cpf, CancellationToken ct)
    {
        await using var connection = await databases.OpenAsync(unitId, ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await UnitWaitingListDao.LockAsync(connection, ct);
        var now = time.GetUtcNow();
        int appointments, entries;
        await using (var cancelAppointments = new NpgsqlCommand("WITH changed AS (UPDATE nora_unit.appointments SET status='cancelled' WHERE cpf=$1 AND status IN ('scheduled','called') AND starts_at>$3 RETURNING id), logged AS (INSERT INTO nora_unit.appointment_events(appointment_id,actor_id,status,occurred_at,source) SELECT id,$2,'cancelled',$3,'patient' FROM changed RETURNING 1) SELECT count(*) FROM logged", connection))
        {
            cancelAppointments.Parameters.AddWithValue(cpf); cancelAppointments.Parameters.AddWithValue(actorId); cancelAppointments.Parameters.AddWithValue(now);
            appointments = (int)(long)(await cancelAppointments.ExecuteScalarAsync(ct))!;
        }
        await using (var reopen = new NpgsqlCommand("UPDATE nora_unit.lightning_slots SET status='open' WHERE id IN (SELECT o.slot_id FROM nora_unit.lightning_offers o WHERE o.cpf=$1 AND o.status='pending')", connection))
        { reopen.Parameters.AddWithValue(cpf); await reopen.ExecuteNonQueryAsync(ct); }
        await using (var cancelOffers = new NpgsqlCommand("UPDATE nora_unit.lightning_offers SET status='cancelled' WHERE status='pending' AND cpf=$1", connection))
        { cancelOffers.Parameters.AddWithValue(cpf); await cancelOffers.ExecuteNonQueryAsync(ct); }
        await using (var cancelQueue = new NpgsqlCommand("WITH changed AS (UPDATE nora_unit.waiting_list SET status='cancelled' WHERE cpf=$1 AND status IN ('waiting','offered') RETURNING id), logged AS (INSERT INTO nora_unit.waiting_list_events(entry_id,actor_id,status,occurred_at,source) SELECT id,$2,'cancelled',$3,'patient' FROM changed RETURNING 1) SELECT count(*) FROM logged", connection))
        {
            cancelQueue.Parameters.AddWithValue(cpf); cancelQueue.Parameters.AddWithValue(actorId); cancelQueue.Parameters.AddWithValue(now);
            entries = (int)(long)(await cancelQueue.ExecuteScalarAsync(ct))!;
        }
        await transaction.CommitAsync(ct);
        return new(appointments, entries);
    }

    /// <summary>Prévia de <see cref="CancelPendingAsync"/>: conta, com os mesmos critérios e sem limite de data, o que a remoção cancelaria agora. Não grava nada.</summary>
    public async Task<UnlinkPreview> PreviewAsync(Guid unitId, string cpf, CancellationToken ct)
    {
        await using var connection = await databases.OpenAsync(unitId, ct);
        await using var query = new NpgsqlCommand("SELECT (SELECT count(*) FROM nora_unit.appointments WHERE cpf=$1 AND status IN ('scheduled','called') AND starts_at>$2),(SELECT count(*) FROM nora_unit.waiting_list WHERE cpf=$1 AND status IN ('waiting','offered'))", connection);
        query.Parameters.AddWithValue(cpf); query.Parameters.AddWithValue(time.GetUtcNow());
        await using var reader = await query.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        return new((int)reader.GetInt64(0), (int)reader.GetInt64(1));
    }

    /// <summary>
    /// Fim do vínculo de Família que deixou o dependente sem ninguém que responda por ele na UBS: cancela as consultas futuras (<c>scheduled</c>/<c>called</c>, início depois de agora) do CPF
    /// que foram marcadas por um responsável (evento <c>scheduled</c> com <c>guardian_id</c>), numa transação da UBS. O evento de cancelamento tem origem <c>system</c>, que não pesa no score,
    /// autor <paramref name="actorId"/> (quem encerrou) e <paramref name="guardianId"/> quando foi o responsável. O horário volta à disponibilidade; a Vaga Relâmpago continua manual.
    /// </summary>
    public async Task<int> CancelGuardianBookingsAsync(Guid unitId, string cpf, Guid actorId, Guid? guardianId, CancellationToken ct)
    {
        await using var connection = await databases.OpenAsync(unitId, ct);
        await using var query = new NpgsqlCommand("WITH changed AS (UPDATE nora_unit.appointments a SET status='cancelled' WHERE a.cpf=$1 AND a.status IN ('scheduled','called') AND a.starts_at>$3 AND EXISTS(SELECT 1 FROM nora_unit.appointment_events e WHERE e.appointment_id=a.id AND e.status='scheduled' AND e.guardian_id IS NOT NULL) RETURNING a.id), logged AS (INSERT INTO nora_unit.appointment_events(appointment_id,actor_id,status,occurred_at,source,guardian_id) SELECT id,$2,'cancelled',$3,'system',$4 FROM changed RETURNING 1) SELECT count(*) FROM logged", connection);
        query.Parameters.AddWithValue(cpf); query.Parameters.AddWithValue(actorId); query.Parameters.AddWithValue(time.GetUtcNow()); query.Parameters.Add(new NpgsqlParameter { Value = (object?)guardianId ?? DBNull.Value });
        return (int)(long)(await query.ExecuteScalarAsync(ct))!;
    }
}
