using Npgsql;
using Nora.Api.Database;

namespace Nora.Api.DAOs;

/// <summary>Internal scheduler; callers must provide a verified UBS identifier.</summary>
public sealed class UnitLightningDispatcher(DatabaseHelper central, UnitDatabaseRouter databases, TimeProvider time)
{
    /// <summary>A consulta precisa estar a mais que isto para a vaga ainda poder ser aprovada (e a proposta ser criada).</summary>
    public static readonly TimeSpan ApprovalLead = TimeSpan.FromHours(1);
    /// <summary>Teto de propostas criadas por passe; o resto vem no passe seguinte.</summary>
    private const int ProposalsPerPass = 200;

    public async Task AdvanceAsync(Guid unitId, CancellationToken ct)
    {
        // Lock central grants and account state before the unit transaction. Never offer to a
        // patient whose unit link or active account can change during this operation.
        await using var centralConnection = await central.OpenAsync(ct);
        await using var centralTransaction = await centralConnection.BeginTransactionAsync(ct);
        // Família: o dependente com vínculo aprovado nesta UBS e responsável ativo também recebe ofertas, tenha conta própria ou não (quem responde é o responsável).
        var eligible = await LightningEligibility.ReadAsync(central, centralConnection, unitId, ct);
        DateTimeOffset? approvalSince;
        await using (var read = central.Command(centralConnection, "SELECT slot_approval_since FROM nora.health_units WHERE id=$1", unitId))
        await using (var reader = await read.ExecuteReaderAsync(ct))
            approvalSince = await reader.ReadAsync(ct) && !reader.IsDBNull(0) ? reader.GetFieldValue<DateTimeOffset>(0) : null;
        await using var connection = await databases.OpenAsync(unitId, ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await UnitWaitingListDao.LockAsync(connection, ct);
        var now = time.GetUtcNow();
        // Aprovação das vagas reaproveitadas: o prazo fecha as propostas que ninguém aprovou até faltar 1 h e, com a chave ligada, os cancelamentos novos viram propostas (nunca ofertadas antes de aprovadas).
        await ExecuteAsync(connection, "UPDATE nora_unit.lightning_slots SET status='closed',closed_reason='approval-timeout' WHERE status='awaiting_approval' AND starts_at<=$1", ct, now + ApprovalLead);
        if (approvalSince is { } since) await ProposeAsync(connection, since, now, ct);
        await ExecuteAsync(connection, "UPDATE nora_unit.waiting_list SET status='completed' WHERE id IN (SELECT o.entry_id FROM nora_unit.lightning_offers o JOIN nora_unit.lightning_slots s ON s.id=o.slot_id WHERE o.status='pending' AND (o.expires_at<=$1 OR s.starts_at<=$1))", ct, now);
        await ExecuteAsync(connection, "INSERT INTO nora_unit.waiting_list_events(entry_id,actor_id,status,occurred_at,source) SELECT o.entry_id,NULL,'completed',$1,'system' FROM nora_unit.lightning_offers o JOIN nora_unit.lightning_slots s ON s.id=o.slot_id WHERE o.status='pending' AND (o.expires_at<=$1 OR s.starts_at<=$1)", ct, now);
        await ExecuteAsync(connection, "UPDATE nora_unit.lightning_slots SET status='open' WHERE id IN (SELECT o.slot_id FROM nora_unit.lightning_offers o JOIN nora_unit.lightning_slots s ON s.id=o.slot_id WHERE o.status='pending' AND (o.expires_at<=$1 OR s.starts_at<=$1))", ct, now);
        await ExecuteAsync(connection, "UPDATE nora_unit.lightning_offers SET status='expired' WHERE status='pending' AND (expires_at<=$1 OR slot_id IN (SELECT id FROM nora_unit.lightning_slots WHERE starts_at<=$1))", ct, now);
        // A full ten-minute response window must remain before the scheduled start.
        await ExecuteAsync(connection, "UPDATE nora_unit.lightning_slots SET status='closed',closed_reason='too-late' WHERE status='open' AND starts_at<$1", ct, now.AddMinutes(10));
        var slots = new List<(Guid Id, string Specialty, DateTimeOffset Start, DateTimeOffset End)>();
        await using (var query = new NpgsqlCommand("SELECT id,specialty,starts_at,ends_at FROM nora_unit.lightning_slots WHERE status='open' ORDER BY created_at,id", connection))
        await using (var reader = await query.ExecuteReaderAsync(ct))
            while (await reader.ReadAsync(ct)) slots.Add((reader.GetGuid(0), reader.GetString(1), reader.GetFieldValue<DateTimeOffset>(2), reader.GetFieldValue<DateTimeOffset>(3)));
        foreach (var slot in slots)
        {
            if (await LightningEligibility.PickAsync(connection, slot.Specialty, eligible, slot.Start, slot.End, ct) is not { } chosen) continue;
            await ExecuteAsync(connection, "INSERT INTO nora_unit.lightning_offers(id,slot_id,entry_id,expires_at,status) VALUES($1,$2,$3,$4,'pending')", ct, Guid.NewGuid(), slot.Id, chosen, now.AddMinutes(10));
            await ExecuteAsync(connection, "UPDATE nora_unit.waiting_list SET status='offered' WHERE id=$1", ct, chosen);
            await ExecuteAsync(connection, "UPDATE nora_unit.lightning_slots SET status='offered' WHERE id=$1", ct, slot.Id);
            await ExecuteAsync(connection, "INSERT INTO nora_unit.waiting_list_events(entry_id,actor_id,status,occurred_at,source) VALUES($1,NULL,'offered',$2,'system')", ct, chosen, now);
        }
        await transaction.CommitAsync(ct); await centralTransaction.CommitAsync(ct);
    }

    /// <summary>
    /// Cria a proposta (<c>awaiting_approval</c>, origem <c>cancellation</c>, sem autor) de cada consulta cancelada no futuro (a mais de 1 h), cancelada depois de a chave ser ligada, que ainda não tem vaga e cujo horário o profissional
    /// não ocupou de novo (consulta ativa ou vaga aberta/ofertada). Até 200 por passe. A proposta não segura o horário.
    /// </summary>
    private static Task ProposeAsync(NpgsqlConnection connection, DateTimeOffset since, DateTimeOffset now, CancellationToken ct) => ExecuteAsync(connection,
        "INSERT INTO nora_unit.lightning_slots(id,appointment_id,status,created_at,created_by,origin,specialty,specialty_id,professional,professional_id,room_id,starts_at,ends_at) " +
        "SELECT gen_random_uuid(),a.id,'awaiting_approval',$1,NULL,'cancellation',a.specialty,a.specialty_id,a.professional,a.professional_id,a.room_id,a.starts_at,a.ends_at FROM nora_unit.appointments a " +
        "WHERE a.status='cancelled' AND a.starts_at>$3 AND NOT EXISTS(SELECT 1 FROM nora_unit.lightning_slots x WHERE x.appointment_id=a.id) " +
        "AND EXISTS(SELECT 1 FROM nora_unit.appointment_events e WHERE e.appointment_id=a.id AND e.status='cancelled' AND e.occurred_at>=$2) " +
        "AND NOT EXISTS(SELECT 1 FROM nora_unit.appointments b WHERE lower(btrim(b.professional))=lower(btrim(a.professional)) AND b.status IN ('scheduled','called','completed','missed') AND b.starts_at<a.ends_at AND b.ends_at>a.starts_at) " +
        "AND NOT EXISTS(SELECT 1 FROM nora_unit.lightning_slots y WHERE lower(btrim(y.professional))=lower(btrim(a.professional)) AND y.status IN ('open','offered') AND y.starts_at<a.ends_at AND y.ends_at>a.starts_at) " +
        $"ORDER BY a.starts_at,a.id LIMIT {ProposalsPerPass} ON CONFLICT (appointment_id) DO NOTHING", ct, now, since, now + ApprovalLead);

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql, CancellationToken ct, params object[] values)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var value in values) command.Parameters.AddWithValue(value);
        await command.ExecuteNonQueryAsync(ct);
    }
}
