using Npgsql;
using Nora.Api.Database;

namespace Nora.Api.DAOs;

/// <summary>Internal scheduler; callers must provide a verified UBS identifier.</summary>
public sealed class UnitLightningDispatcher(DatabaseHelper central, UnitDatabaseRouter databases, TimeProvider time)
{
    public async Task AdvanceAsync(Guid unitId, CancellationToken ct)
    {
        // Lock central grants and account state before the unit transaction. Never offer to a
        // patient whose unit link or active account can change during this operation.
        await using var centralConnection = await central.OpenAsync(ct);
        await using var centralTransaction = await centralConnection.BeginTransactionAsync(ct);
        var eligible = new List<string>();
        await using (var query = central.Command(centralConnection, "SELECT a.cpf FROM nora.patient_units p JOIN nora.accounts a ON a.id=p.patient_id WHERE p.unit_id=$1 AND a.scope='patient' AND a.is_active ORDER BY a.id FOR SHARE OF a,p", unitId))
        await using (var reader = await query.ExecuteReaderAsync(ct))
            while (await reader.ReadAsync(ct)) eligible.Add(reader.GetString(0));
        await using var connection = await databases.OpenAsync(unitId, ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await UnitWaitingListDao.LockAsync(connection, ct);
        var now = time.GetUtcNow();
        await ExecuteAsync(connection, "UPDATE nora_unit.waiting_list SET status='completed' WHERE id IN (SELECT o.entry_id FROM nora_unit.lightning_offers o JOIN nora_unit.lightning_slots s ON s.id=o.slot_id JOIN nora_unit.appointments a ON a.id=s.appointment_id WHERE o.status='pending' AND (o.expires_at<=$1 OR a.starts_at<=$1))", ct, now);
        await ExecuteAsync(connection, "INSERT INTO nora_unit.waiting_list_events(entry_id,actor_id,status,occurred_at,source) SELECT o.entry_id,NULL,'completed',$1,'system' FROM nora_unit.lightning_offers o JOIN nora_unit.lightning_slots s ON s.id=o.slot_id JOIN nora_unit.appointments a ON a.id=s.appointment_id WHERE o.status='pending' AND (o.expires_at<=$1 OR a.starts_at<=$1)", ct, now);
        await ExecuteAsync(connection, "UPDATE nora_unit.lightning_slots SET status='open' WHERE id IN (SELECT o.slot_id FROM nora_unit.lightning_offers o JOIN nora_unit.lightning_slots s ON s.id=o.slot_id JOIN nora_unit.appointments a ON a.id=s.appointment_id WHERE o.status='pending' AND (o.expires_at<=$1 OR a.starts_at<=$1))", ct, now);
        await ExecuteAsync(connection, "UPDATE nora_unit.lightning_offers SET status='expired' WHERE status='pending' AND (expires_at<=$1 OR slot_id IN (SELECT s.id FROM nora_unit.lightning_slots s JOIN nora_unit.appointments a ON a.id=s.appointment_id WHERE a.starts_at<=$1))", ct, now);
        // A full ten-minute response window must remain before the scheduled start.
        await ExecuteAsync(connection, "UPDATE nora_unit.lightning_slots SET status='closed' WHERE status='open' AND appointment_id IN (SELECT id FROM nora_unit.appointments WHERE starts_at<$1)", ct, now.AddMinutes(10));
        var slots = new List<(Guid Id, string Specialty, DateTimeOffset Start, DateTimeOffset End)>();
        await using (var query = new NpgsqlCommand("SELECT s.id,a.specialty,a.starts_at,a.ends_at FROM nora_unit.lightning_slots s JOIN nora_unit.appointments a ON a.id=s.appointment_id WHERE s.status='open' ORDER BY s.created_at,s.id", connection))
        await using (var reader = await query.ExecuteReaderAsync(ct))
            while (await reader.ReadAsync(ct)) slots.Add((reader.GetGuid(0), reader.GetString(1), reader.GetFieldValue<DateTimeOffset>(2), reader.GetFieldValue<DateTimeOffset>(3)));
        foreach (var slot in slots)
        {
            long? entryId;
            await using (var query = new NpgsqlCommand("SELECT q.id FROM nora_unit.waiting_list q WHERE q.status='waiting' AND lower(q.specialty)=lower($1) AND q.cpf=ANY($2) AND EXISTS(SELECT 1 FROM nora_unit.patient_registry p WHERE p.cpf=q.cpf AND p.is_active) AND NOT EXISTS(SELECT 1 FROM nora_unit.lightning_offers o JOIN nora_unit.waiting_list other ON other.id=o.entry_id WHERE o.status='pending' AND other.cpf=q.cpf) AND NOT EXISTS(SELECT 1 FROM nora_unit.appointments a WHERE a.cpf=q.cpf AND a.status IN ('scheduled','called','completed','missed') AND a.starts_at<$4 AND a.ends_at>$3) ORDER BY q.entered_at,q.id LIMIT 1 FOR UPDATE OF q", connection))
            {
                query.Parameters.AddWithValue(slot.Specialty); query.Parameters.AddWithValue(eligible.ToArray()); query.Parameters.AddWithValue(slot.Start); query.Parameters.AddWithValue(slot.End);
                entryId = await query.ExecuteScalarAsync(ct) is long id ? id : null;
            }
            if (entryId is not { } chosen) continue;
            await ExecuteAsync(connection, "INSERT INTO nora_unit.lightning_offers(id,slot_id,entry_id,expires_at,status) VALUES($1,$2,$3,$4,'pending')", ct, Guid.NewGuid(), slot.Id, chosen, now.AddMinutes(10));
            await ExecuteAsync(connection, "UPDATE nora_unit.waiting_list SET status='offered' WHERE id=$1", ct, chosen);
            await ExecuteAsync(connection, "UPDATE nora_unit.lightning_slots SET status='offered' WHERE id=$1", ct, slot.Id);
            await ExecuteAsync(connection, "INSERT INTO nora_unit.waiting_list_events(entry_id,actor_id,status,occurred_at,source) VALUES($1,NULL,'offered',$2,'system')", ct, chosen, now);
        }
        await transaction.CommitAsync(ct); await centralTransaction.CommitAsync(ct);
    }
    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql, CancellationToken ct, params object[] values)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var value in values) command.Parameters.AddWithValue(value);
        await command.ExecuteNonQueryAsync(ct);
    }
}
