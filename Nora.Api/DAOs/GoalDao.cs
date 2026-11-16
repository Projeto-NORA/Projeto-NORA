using Nora.Api.Database;
using Nora.Api.Models;
using Nora.Api.Services;

namespace Nora.Api.DAOs;

/// <summary>Metas dos indicadores (banco central): uma da rede por métrica e, opcionalmente, uma sobrescrita por UBS.</summary>
public sealed class GoalDao(DatabaseHelper database)
{
    public async Task<List<Goal>> ListAsync(CancellationToken ct)
    {
        await using var connection = await database.OpenAsync(ct);
        await using var command = database.Command(connection, "SELECT metric,unit_id,target FROM nora.goals ORDER BY metric,unit_id NULLS FIRST");
        await using var reader = await command.ExecuteReaderAsync(ct);
        var goals = new List<Goal>();
        while (await reader.ReadAsync(ct)) goals.Add(new(reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetGuid(1), reader.GetDecimal(2)));
        return goals;
    }

    public async Task<Goal> UpsertAsync(string metric, Guid? unitId, decimal target, Guid actorId, CancellationToken ct)
    {
        await using var connection = await database.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        if (unitId is { } unit)
        {
            await using var exists = database.Command(connection, "SELECT 1 FROM nora.health_units WHERE id=$1 FOR KEY SHARE", unit);
            if (await exists.ExecuteScalarAsync(ct) is null) throw new ApiNotFoundException();
        }
        var sql = unitId is null
            ? "INSERT INTO nora.goals(id,metric,unit_id,target,updated_by) VALUES($1,$2,NULL,$3,$4) ON CONFLICT (metric) WHERE unit_id IS NULL DO UPDATE SET target=EXCLUDED.target,updated_at=CURRENT_TIMESTAMP,updated_by=EXCLUDED.updated_by"
            : "INSERT INTO nora.goals(id,metric,unit_id,target,updated_by) VALUES($1,$2,$5,$3,$4) ON CONFLICT (metric,unit_id) WHERE unit_id IS NOT NULL DO UPDATE SET target=EXCLUDED.target,updated_at=CURRENT_TIMESTAMP,updated_by=EXCLUDED.updated_by";
        await using (var command = unitId is null ? database.Command(connection, sql, Guid.NewGuid(), metric, target, actorId) : database.Command(connection, sql, Guid.NewGuid(), metric, target, actorId, unitId.Value))
            await command.ExecuteNonQueryAsync(ct);
        await transaction.CommitAsync(ct);
        return new(metric, unitId, target);
    }

    public async Task DeleteAsync(string metric, Guid? unitId, CancellationToken ct)
    {
        await using var connection = await database.OpenAsync(ct);
        var removed = await database.ExecuteAsync(connection, "DELETE FROM nora.goals WHERE metric=$1 AND unit_id IS NOT DISTINCT FROM $2::uuid", ct, metric, unitId is { } unit ? unit : DBNull.Value);
        if (removed == 0) throw new ApiNotFoundException();
    }
}
