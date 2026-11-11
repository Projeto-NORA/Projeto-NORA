using Npgsql;
using Nora.Api.DAOs;
using Nora.Api.Database;
using Nora.Api.Models;

namespace Nora.Api.Services;

public sealed class UnitPatientStatusService(DatabaseHelper central, UnitDatabaseRouter databases, TimeProvider time, UnitPatientRegistryDao registry)
{
    public async Task UpdateEmailAsync(Guid unitId, Guid actorId, string cpfValue, string? email, CancellationToken ct)
    {
        var cpf = AccountService.NormalizeCpf(cpfValue);
        if (!AccountService.ValidCpf(cpf)) throw new ApiInputException();
        await using var connection = await central.OpenAsync(ct); await using var transaction = await connection.BeginTransactionAsync(ct);
        await central.ExecuteAsync(connection, "SELECT pg_advisory_xact_lock(hashtextextended($1,62419805))", ct, cpf);
        await registry.UpdateEmailAsync(unitId, actorId, cpf, email, ct);
        await transaction.CommitAsync(ct);
    }
    public async Task UpdateDetailsAsync(Guid unitId, Guid actorId, UnitPatientRecord patient, CancellationToken ct)
    {
        var cpf = AccountService.NormalizeCpf(patient.Cpf);
        if (!AccountService.ValidCpf(cpf)) throw new ApiInputException();
        await using var connection = await central.OpenAsync(ct); await using var transaction = await connection.BeginTransactionAsync(ct);
        await central.ExecuteAsync(connection, "SELECT pg_advisory_xact_lock(hashtextextended($1,62419805))", ct, cpf);
        await registry.UpdateAsync(unitId, actorId, patient, ct);
        await transaction.CommitAsync(ct);
    }
    public async Task SetActiveAsync(Guid unitId, Guid actorId, UnitPatientActiveRequest request, CancellationToken ct)
    {
        var cpf = AccountService.NormalizeCpf(request.Cpf);
        if (unitId == Guid.Empty || actorId == Guid.Empty || !AccountService.ValidCpf(cpf) || !request.Confirmed) throw new ApiInputException();
        await using var centralConnection = await central.OpenAsync(ct);
        await using var centralTransaction = await centralConnection.BeginTransactionAsync(ct);
        await central.ExecuteAsync(centralConnection, "SELECT pg_advisory_xact_lock(hashtextextended($1,62419805))", ct, cpf);
        await using (var link = central.Command(centralConnection, "SELECT p.patient_id FROM nora.patient_units p JOIN nora.accounts a ON a.id=p.patient_id WHERE p.unit_id=$1 AND a.scope='patient' AND a.cpf=$2 FOR UPDATE OF p", unitId, cpf))
        { await using var reader = await link.ExecuteReaderAsync(ct); while (await reader.ReadAsync(ct)) { } }
        await using var connection = await databases.OpenAsync(unitId, ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await UnitWaitingListDao.LockAsync(connection, ct);
        bool previous;
        await using (var current = new NpgsqlCommand("SELECT is_active FROM nora_unit.patient_registry WHERE cpf=$1 FOR UPDATE", connection))
        { current.Parameters.AddWithValue(cpf); previous = await current.ExecuteScalarAsync(ct) is bool value ? value : throw new ApiNotFoundException(); }
        if (previous != request.IsActive)
        {
            await using (var update = new NpgsqlCommand("UPDATE nora_unit.patient_registry SET is_active=$2 WHERE cpf=$1", connection))
            { update.Parameters.AddWithValue(cpf); update.Parameters.AddWithValue(request.IsActive); await update.ExecuteNonQueryAsync(ct); }
            var now = time.GetUtcNow();
            if (!request.IsActive)
            {
                await using (var reopen = new NpgsqlCommand("UPDATE nora_unit.lightning_slots SET status='open' WHERE id IN (SELECT o.slot_id FROM nora_unit.lightning_offers o JOIN nora_unit.waiting_list q ON q.id=o.entry_id WHERE q.cpf=$1 AND o.status='pending')", connection))
                { reopen.Parameters.AddWithValue(cpf); await reopen.ExecuteNonQueryAsync(ct); }
                await using (var cancel = new NpgsqlCommand("UPDATE nora_unit.lightning_offers SET status='cancelled' WHERE status='pending' AND entry_id IN (SELECT id FROM nora_unit.waiting_list WHERE cpf=$1)", connection))
                { cancel.Parameters.AddWithValue(cpf); await cancel.ExecuteNonQueryAsync(ct); }
                await using (var cancelQueue = new NpgsqlCommand("WITH changed AS (UPDATE nora_unit.waiting_list SET status='cancelled' WHERE cpf=$1 AND status IN ('waiting','offered') RETURNING id) INSERT INTO nora_unit.waiting_list_events(entry_id,actor_id,status,occurred_at,source) SELECT id,$2,'cancelled',$3,'staff' FROM changed", connection))
                { cancelQueue.Parameters.AddWithValue(cpf); cancelQueue.Parameters.AddWithValue(actorId); cancelQueue.Parameters.AddWithValue(now); await cancelQueue.ExecuteNonQueryAsync(ct); }
            }
            await using var audit = new NpgsqlCommand("INSERT INTO nora_unit.patient_registry_events(cpf,actor_id,action,occurred_at) VALUES($1,$2,$3,$4)", connection);
            audit.Parameters.AddWithValue(cpf); audit.Parameters.AddWithValue(actorId); audit.Parameters.AddWithValue(request.IsActive ? "reactivated" : "deactivated"); audit.Parameters.AddWithValue(now); await audit.ExecuteNonQueryAsync(ct);
        }
        await transaction.CommitAsync(ct); await centralTransaction.CommitAsync(ct);
    }
}
