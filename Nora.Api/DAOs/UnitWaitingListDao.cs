using Npgsql;
using Nora.Api.Database;
using Nora.Api.Models;
using Nora.Api.Services;

namespace Nora.Api.DAOs;

/// <summary>Internal unit boundary. Callers must authorize the unit before invoking this DAO.</summary>
public sealed class UnitWaitingListDao(UnitDatabaseRouter databases, TimeProvider time)
{
    public async Task<List<UnitWaitingEntry>> ListAsync(Guid unitId, CancellationToken ct, string? cpf = null)
    {
        await using var connection = await databases.OpenAsync(unitId, ct);
        await using var query = new NpgsqlCommand("SELECT q.id,q.cpf,p.full_name,q.specialty,q.entered_at,q.status FROM nora_unit.waiting_list q JOIN nora_unit.patient_registry p ON p.cpf=q.cpf WHERE ($1='' OR q.cpf=$1) ORDER BY q.entered_at,q.id", connection);
        query.Parameters.AddWithValue(cpf ?? "");
        await using var reader = await query.ExecuteReaderAsync(ct); var entries = new List<UnitWaitingEntry>();
        while (await reader.ReadAsync(ct)) entries.Add(Read(reader));
        return entries;
    }

    public async Task<UnitWaitingEntry> AddAsync(Guid unitId, Guid actorId, AddUnitWaitingEntryRequest request, CancellationToken ct)
    {
        var cpf = AccountService.NormalizeCpf(request.Cpf); var specialty = request.Specialty?.Trim() ?? "";
        if (unitId == Guid.Empty || actorId == Guid.Empty || !AccountService.ValidCpf(cpf) || specialty.Length is < 1 or > 120 || specialty.Any(char.IsControl)) throw new ApiInputException();
        await using var connection = await databases.OpenAsync(unitId, ct); await using var transaction = await connection.BeginTransactionAsync(ct);
        await LockAsync(connection, ct);
        await using (var patient = new NpgsqlCommand("SELECT is_active FROM nora_unit.patient_registry WHERE cpf=$1 FOR SHARE", connection))
        { patient.Parameters.AddWithValue(cpf); if (await patient.ExecuteScalarAsync(ct) is not true) throw new ApiNotFoundException(); }
        var now = time.GetUtcNow(); long id;
        await using (var insert = new NpgsqlCommand("INSERT INTO nora_unit.waiting_list(cpf,specialty,entered_at,status,created_by) VALUES($1,$2,$3,'waiting',$4) ON CONFLICT DO NOTHING RETURNING id", connection))
        {
            insert.Parameters.AddWithValue(cpf); insert.Parameters.AddWithValue(specialty); insert.Parameters.AddWithValue(now); insert.Parameters.AddWithValue(actorId);
            id = await insert.ExecuteScalarAsync(ct) is long created ? created : throw new ApiConflictException();
        }
        await RecordAsync(connection, id, actorId, "waiting", now, ct);
        var item = await GetAsync(connection, id, ct); await transaction.CommitAsync(ct); return item;
    }

    public async Task CancelAsync(Guid unitId, Guid actorId, long id, bool confirmed, CancellationToken ct)
    {
        if (id <= 0 || !confirmed) throw new ApiInputException();
        await using var connection = await databases.OpenAsync(unitId, ct); await using var transaction = await connection.BeginTransactionAsync(ct);
        await LockAsync(connection, ct);
        await using (var update = new NpgsqlCommand("UPDATE nora_unit.waiting_list SET status='cancelled' WHERE id=$1 AND status IN ('waiting','offered') RETURNING id", connection))
        { update.Parameters.AddWithValue(id); if (await update.ExecuteScalarAsync(ct) is not long) throw new ApiConflictException(); }
        await using (var reopen = new NpgsqlCommand("UPDATE nora_unit.lightning_slots SET status='open' WHERE id IN (SELECT slot_id FROM nora_unit.lightning_offers WHERE entry_id=$1 AND status='pending')", connection))
        { reopen.Parameters.AddWithValue(id); await reopen.ExecuteNonQueryAsync(ct); }
        await using (var cancel = new NpgsqlCommand("UPDATE nora_unit.lightning_offers SET status='cancelled' WHERE entry_id=$1 AND status='pending'", connection))
        { cancel.Parameters.AddWithValue(id); await cancel.ExecuteNonQueryAsync(ct); }
        await RecordAsync(connection, id, actorId, "cancelled", time.GetUtcNow(), ct); await transaction.CommitAsync(ct);
    }

    private static async Task RecordAsync(NpgsqlConnection connection, long id, Guid actor, string status, DateTimeOffset now, CancellationToken ct)
    {
        await using var insert = new NpgsqlCommand("INSERT INTO nora_unit.waiting_list_events(entry_id,actor_id,status,occurred_at) VALUES($1,$2,$3,$4)", connection);
        insert.Parameters.AddWithValue(id); insert.Parameters.AddWithValue(actor); insert.Parameters.AddWithValue(status); insert.Parameters.AddWithValue(now); await insert.ExecuteNonQueryAsync(ct);
    }
    internal static async Task LockAsync(NpgsqlConnection connection, CancellationToken ct)
    { await using var command = new NpgsqlCommand("SELECT pg_advisory_xact_lock(62419811)", connection); await command.ExecuteNonQueryAsync(ct); }
    private static async Task<UnitWaitingEntry> GetAsync(NpgsqlConnection connection, long id, CancellationToken ct)
    {
        await using var query = new NpgsqlCommand("SELECT q.id,q.cpf,p.full_name,q.specialty,q.entered_at,q.status FROM nora_unit.waiting_list q JOIN nora_unit.patient_registry p ON p.cpf=q.cpf WHERE q.id=$1", connection);
        query.Parameters.AddWithValue(id); await using var reader = await query.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) throw new ApiNotFoundException(); return Read(reader);
    }
    private static UnitWaitingEntry Read(NpgsqlDataReader reader) => new(reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetFieldValue<DateTimeOffset>(4), reader.GetString(5));
}
