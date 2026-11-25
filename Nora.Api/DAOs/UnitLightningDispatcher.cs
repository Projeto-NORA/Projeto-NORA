using Npgsql;
using Nora.Api.Database;

namespace Nora.Api.DAOs;

/// <summary>
/// Distribuição automática da Vaga Relâmpago (2026-11-24). A cada passe (worker de 15 s e depois das ações), na transação da UBS e sob o lock da fila:
/// (1) vence as ofertas pendentes fora do prazo e cancela as que perderam a consulta a antecipar; (2) fecha as vagas abertas perto demais do horário (<c>too-late</c>);
/// (3) transforma em vaga aberta cada consulta futura cancelada que ainda não tem vaga, com o horário livre (sem consulta ativa, vaga aberta/ofertada ou bloqueio do profissional ou da UBS) e a mais de
/// <see cref="CreationLead"/> do início; o cancelamento por antecipação gera a vaga com origem <c>rescheduled</c> (efeito cascata); (4) oferece cada vaga aberta ao primeiro de <see cref="LightningEligibility"/>.
/// Sem laço infinito: quem aceita sempre vai para um horário anterior ao da sua consulta, então a cascata só anda para frente no tempo; cada consulta gera no máximo uma vaga (<c>appointment_id</c> único) e para quando o horário
/// liberado fica a menos de <see cref="CreationLead"/>.
/// </summary>
public sealed class UnitLightningDispatcher(DatabaseHelper central, UnitDatabaseRouter databases, TimeProvider time)
{
    /// <summary>Prazo de resposta da oferta.</summary>
    public static readonly TimeSpan OfferLifetime = TimeSpan.FromMinutes(10);
    /// <summary>Antecedência mínima do horário para uma oferta começar (a oferta precisa de 10 minutos completos antes dele); vaga aberta com menos é fechada.</summary>
    public static readonly TimeSpan MinimumLead = TimeSpan.FromMinutes(10);
    /// <summary>Uma consulta cancelada só vira vaga (inclusive na cascata) se faltar mais que isto: o prazo de uma oferta mais a antecedência mínima.</summary>
    public static readonly TimeSpan CreationLead = OfferLifetime + MinimumLead;
    /// <summary>Teto de vagas criadas por passe; o resto vem no passe seguinte.</summary>
    private const int CreationsPerPass = 200;

    public async Task AdvanceAsync(Guid unitId, CancellationToken ct)
    {
        // P1 (docs/desempenho.md): sem nada a fazer o passe não abre transação central nem trava vínculos.
        if (!await HasWorkAsync(unitId, ct)) return;
        // Vínculos e contas travados na central antes da transação da UBS: ninguém recebe oferta com vínculo ou conta mudando no meio. O commit da UBS vem antes do central.
        await using var centralConnection = await central.OpenAsync(ct);
        await using var centralTransaction = await centralConnection.BeginTransactionAsync(ct);
        var eligible = await LightningEligibility.ReadAsync(central, centralConnection, unitId, ct);
        await using var connection = await databases.OpenAsync(unitId, ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await UnitWaitingListDao.LockAsync(connection, ct);
        var now = time.GetUtcNow();
        // (1) Vencimento: prazo de 10 minutos ou o horário já começou. A vaga volta a 'open' (e a etapa 2 fecha a que ficou tarde demais).
        await ExecuteAsync(connection, "UPDATE nora_unit.lightning_slots SET status='open' WHERE id IN (SELECT o.slot_id FROM nora_unit.lightning_offers o JOIN nora_unit.lightning_slots s ON s.id=o.slot_id WHERE o.status='pending' AND (o.expires_at<=$1 OR s.starts_at<=$1))", ct, now);
        await ExecuteAsync(connection, "UPDATE nora_unit.lightning_offers SET status='expired' WHERE status='pending' AND (expires_at<=$1 OR slot_id IN (SELECT id FROM nora_unit.lightning_slots WHERE starts_at<=$1))", ct, now);
        // A consulta que seria antecipada deixou de estar marcada (cancelada, chamada, encerrada): a oferta não vale mais.
        await ExecuteAsync(connection, "UPDATE nora_unit.lightning_slots SET status='open' WHERE id IN (SELECT o.slot_id FROM nora_unit.lightning_offers o JOIN nora_unit.appointments a ON a.id=o.appointment_id WHERE o.status='pending' AND a.status<>'scheduled')", ct);
        await ExecuteAsync(connection, "UPDATE nora_unit.lightning_offers o SET status='cancelled' FROM nora_unit.appointments a WHERE a.id=o.appointment_id AND o.status='pending' AND a.status<>'scheduled'", ct);
        // (2) A oferta precisa de 10 minutos completos antes do horário.
        await ExecuteAsync(connection, "UPDATE nora_unit.lightning_slots SET status='closed',closed_reason='too-late' WHERE status='open' AND starts_at<$1", ct, now + MinimumLead);
        // (3) Consultas canceladas viram vagas abertas.
        await CreateFromCancellationsAsync(connection, now, ct);
        // (4) Oferta automática.
        var slots = new List<(Guid Id, string Specialty, DateTimeOffset Start, DateTimeOffset End)>();
        await using (var query = new NpgsqlCommand("SELECT id,specialty,starts_at,ends_at FROM nora_unit.lightning_slots WHERE status='open' ORDER BY starts_at,created_at,id", connection))
        await using (var reader = await query.ExecuteReaderAsync(ct))
            while (await reader.ReadAsync(ct)) slots.Add((reader.GetGuid(0), reader.GetString(1), reader.GetFieldValue<DateTimeOffset>(2), reader.GetFieldValue<DateTimeOffset>(3)));
        foreach (var slot in slots)
        {
            if (await LightningEligibility.PickAsync(connection, slot.Id, slot.Specialty, eligible, slot.Start, slot.End, now, ct) is not { } chosen) continue;
            await ExecuteAsync(connection, "INSERT INTO nora_unit.lightning_offers(id,slot_id,entry_id,cpf,appointment_id,expires_at,status) VALUES($1,$2,NULL,$3,$4,$5,'pending')", ct, Guid.NewGuid(), slot.Id, chosen.Cpf, chosen.AppointmentId, now + OfferLifetime);
            await ExecuteAsync(connection, "UPDATE nora_unit.lightning_slots SET status='offered' WHERE id=$1", ct, slot.Id);
        }
        await transaction.CommitAsync(ct); await centralTransaction.CommitAsync(ct);
    }

    /// <summary>Consulta cancelada (alias <c>a</c>) que vira vaga: $1 = agora + <see cref="CreationLead"/>. Sem vaga ainda e com o horário livre para o profissional (consulta ativa, vaga aberta/ofertada e bloqueio).</summary>
    private const string CreationWhere =
        "WHERE a.status='cancelled' AND a.starts_at>$1 AND NOT EXISTS(SELECT 1 FROM nora_unit.lightning_slots x WHERE x.appointment_id=a.id) " +
        "AND NOT EXISTS(SELECT 1 FROM nora_unit.appointments b WHERE lower(btrim(b.professional))=lower(btrim(a.professional)) AND b.status IN ('scheduled','called','completed','missed') AND b.starts_at<a.ends_at AND b.ends_at>a.starts_at) " +
        "AND NOT EXISTS(SELECT 1 FROM nora_unit.lightning_slots y WHERE lower(btrim(y.professional))=lower(btrim(a.professional)) AND y.status IN ('open','offered') AND y.starts_at<a.ends_at AND y.ends_at>a.starts_at) " +
        "AND NOT EXISTS(SELECT 1 FROM nora_unit.schedule_blocks k WHERE (k.professional_id IS NULL OR k.professional_id=a.professional_id) AND k.starts_at<a.ends_at AND k.ends_at>a.starts_at) ";

    /// <summary>
    /// Cria as vagas das consultas canceladas, uma a uma sob o lock do profissional (o mesmo da agenda: <c>62419809</c>), para não cruzar com uma marcação simultânea no mesmo horário. Origem <c>rescheduled</c> quando o
    /// último cancelamento foi a antecipação relâmpago; senão <c>cancellation</c>. Sem autor.
    /// </summary>
    private static async Task CreateFromCancellationsAsync(NpgsqlConnection connection, DateTimeOffset now, CancellationToken ct)
    {
        var candidates = new List<(Guid Id, string Professional)>();
        await using (var query = new NpgsqlCommand($"SELECT a.id,a.professional FROM nora_unit.appointments a {CreationWhere}ORDER BY a.starts_at,a.id LIMIT {CreationsPerPass}", connection))
        {
            query.Parameters.AddWithValue(now + CreationLead);
            await using var reader = await query.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct)) candidates.Add((reader.GetGuid(0), reader.GetString(1)));
        }
        foreach (var (id, professional) in candidates)
        {
            await ExecuteAsync(connection, "SELECT pg_advisory_xact_lock(hashtextextended(lower($1), 62419809))", ct, professional);
            await ExecuteAsync(connection,
                "INSERT INTO nora_unit.lightning_slots(id,appointment_id,status,created_at,created_by,origin,specialty,specialty_id,professional,professional_id,room_id,starts_at,ends_at) " +
                "SELECT gen_random_uuid(),a.id,'open',$2,NULL,CASE WHEN (SELECT e.source FROM nora_unit.appointment_events e WHERE e.appointment_id=a.id AND e.status='cancelled' ORDER BY e.id DESC LIMIT 1)='rescheduled-lightning' THEN 'rescheduled' ELSE 'cancellation' END," +
                "a.specialty,a.specialty_id,a.professional,a.professional_id,a.room_id,a.starts_at,a.ends_at FROM nora_unit.appointments a " + CreationWhere + "AND a.id=$3 ON CONFLICT (appointment_id) DO NOTHING", ct, now + CreationLead, now, id);
        }
    }

    /// <summary>O passe tem o que fazer? Vaga aberta ou ofertada, oferta pendente ou consulta cancelada que vira vaga (mesmo critério de <see cref="CreationWhere"/>). Só leitura, sem lock.</summary>
    private async Task<bool> HasWorkAsync(Guid unitId, CancellationToken ct)
    {
        await using var unit = await databases.OpenAsync(unitId, ct);
        await using var query = new NpgsqlCommand(
            "SELECT EXISTS(SELECT 1 FROM nora_unit.lightning_slots WHERE status IN ('open','offered')) OR EXISTS(SELECT 1 FROM nora_unit.lightning_offers WHERE status='pending') " +
            "OR EXISTS(SELECT 1 FROM nora_unit.appointments a " + CreationWhere + ")", unit);
        query.Parameters.AddWithValue(time.GetUtcNow() + CreationLead);
        return await query.ExecuteScalarAsync(ct) is true;
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql, CancellationToken ct, params object[] values)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var value in values) command.Parameters.AddWithValue(value);
        await command.ExecuteNonQueryAsync(ct);
    }
}
