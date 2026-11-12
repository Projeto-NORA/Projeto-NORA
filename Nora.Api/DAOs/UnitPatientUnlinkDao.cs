using Npgsql;
using Nora.Api.Database;
using Nora.Api.Models;

namespace Nora.Api.DAOs;

/// <summary>Parte da remoção de vínculo que acontece no banco da UBS. O chamador já autorizou o paciente e segura o vínculo central.</summary>
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
        await using (var reopen = new NpgsqlCommand("UPDATE nora_unit.lightning_slots SET status='open' WHERE id IN (SELECT o.slot_id FROM nora_unit.lightning_offers o JOIN nora_unit.waiting_list q ON q.id=o.entry_id WHERE q.cpf=$1 AND o.status='pending')", connection))
        { reopen.Parameters.AddWithValue(cpf); await reopen.ExecuteNonQueryAsync(ct); }
        await using (var cancelOffers = new NpgsqlCommand("UPDATE nora_unit.lightning_offers SET status='cancelled' WHERE status='pending' AND entry_id IN (SELECT id FROM nora_unit.waiting_list WHERE cpf=$1)", connection))
        { cancelOffers.Parameters.AddWithValue(cpf); await cancelOffers.ExecuteNonQueryAsync(ct); }
        await using (var cancelQueue = new NpgsqlCommand("WITH changed AS (UPDATE nora_unit.waiting_list SET status='cancelled' WHERE cpf=$1 AND status IN ('waiting','offered') RETURNING id), logged AS (INSERT INTO nora_unit.waiting_list_events(entry_id,actor_id,status,occurred_at,source) SELECT id,$2,'cancelled',$3,'patient' FROM changed RETURNING 1) SELECT count(*) FROM logged", connection))
        {
            cancelQueue.Parameters.AddWithValue(cpf); cancelQueue.Parameters.AddWithValue(actorId); cancelQueue.Parameters.AddWithValue(now);
            entries = (int)(long)(await cancelQueue.ExecuteScalarAsync(ct))!;
        }
        await transaction.CommitAsync(ct);
        return new(appointments, entries);
    }
}
