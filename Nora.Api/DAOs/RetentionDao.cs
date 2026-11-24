using Nora.Api.Database;
using Nora.Api.Models;
using Npgsql;

namespace Nora.Api.DAOs;

public sealed class RetentionDao(DatabaseHelper database, UnitDatabaseRouter unitDatabases, TimeProvider time)
{
    private sealed record Catalog(string Table, string Policy, string Events, string Name, string Disabled, string Unlinked);

    private static readonly Catalog Staff = new("nora.accounts", "nora.staff_retention_policy", "nora.staff_retention_events", "full_name",
        "i.scope='obs' AND NOT i.is_active",
        """
        NOT i.retention_protected
        AND NOT EXISTS (SELECT 1 FROM nora.sessions s WHERE s.account_id=i.id)
        AND NOT EXISTS (SELECT 1 FROM nora.staff_units s WHERE s.account_id=i.id OR s.granted_by=i.id)
        AND NOT EXISTS (SELECT 1 FROM nora.staff_unit_events e WHERE e.account_id=i.id OR e.actor_id=i.id)
        AND NOT EXISTS (SELECT 1 FROM nora.staff_account_events e WHERE e.actor_id=i.id)
        """);
    private static readonly Catalog Rooms = new("nora_unit.rooms", "nora_unit.room_retention_policy", "nora_unit.room_retention_events", "name",
        "NOT i.is_active",
        """
        NOT EXISTS (SELECT 1 FROM nora_unit.schedule_templates s WHERE s.room_id=i.id)
        AND NOT EXISTS (SELECT 1 FROM nora_unit.appointments a WHERE a.room_id=i.id)
        AND NOT EXISTS (SELECT 1 FROM nora_unit.lightning_slots l WHERE l.room_id=i.id)
        """);

    private const string DueAt = """
        CASE WHEN i.retention_cancelled THEN NULL ELSE COALESCE(i.retention_at,
          GREATEST(i.inactive_since, p.effective_at) + p.days * interval '1 day') END
        """;

    public async Task<RetentionOverview> ReadAsync(Guid? unitId, CancellationToken ct)
    {
        var catalog = unitId is null ? Staff : Rooms;
        await using var connection = await OpenAsync(unitId, ct);
        var policy = await ReadPolicyAsync(connection, catalog, ct);
        await using var command = database.Command(connection, $"""
            SELECT i.id, i.{catalog.Name}, ({catalog.Unlinked}), ({DueAt}), i.retention_cancelled
              FROM {catalog.Table} i CROSS JOIN {catalog.Policy} p WHERE {catalog.Disabled}
              ORDER BY 3 DESC, i.{catalog.Name}, i.id LIMIT 200
            """);
        var items = new List<RetentionItem>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            items.Add(new(reader.GetGuid(0), reader.GetString(1), reader.GetBoolean(2),
                reader.IsDBNull(3) ? null : reader.GetFieldValue<DateTimeOffset>(3), reader.GetBoolean(4)));
        return new(policy, items);
    }

    public async Task<RetentionPolicy> ChangePolicyAsync(Guid? unitId, int? days, CancellationToken ct)
    {
        var catalog = unitId is null ? Staff : Rooms;
        await using var connection = await OpenAsync(unitId, ct);
        // Ligar o prazo ou diminuí-lo recomeça a contagem agora (decisão de 24/11/2026): nada vence antes de passar o prazo novo inteiro.
        // Aumentar mantém o início, porque só dá mais tempo. No SET, "days" ainda é o valor anterior.
        await using var command = database.Command(connection, $"""
            UPDATE {catalog.Policy} SET
              effective_at=CASE WHEN $1::int IS NULL THEN NULL WHEN days IS NULL OR $1::int < days THEN $2 ELSE effective_at END,
              days=$1::int RETURNING days,effective_at
            """, days is { } count ? count : DBNull.Value, time.GetUtcNow());
        await using var reader = await command.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        return ReadPolicy(reader);
    }

    public async Task<bool> ChangeScheduleAsync(Guid? unitId, Guid id, int? days, CancellationToken ct)
    {
        var catalog = unitId is null ? Staff : Rooms;
        await using var connection = await OpenAsync(unitId, ct);
        var now = time.GetUtcNow();
        object due = days is { } count ? now.AddDays(count) : DBNull.Value;
        return await database.ExecuteAsync(connection, $"""
            UPDATE {catalog.Table} i SET retention_at=$2::timestamptz, retention_cancelled=($2::timestamptz IS NULL)
            WHERE i.id=$1 AND {catalog.Disabled} AND ($2::timestamptz IS NULL OR ({catalog.Unlinked}))
            """, ct, id, due) == 1;
    }

    public async Task<int> PurgeDueAsync(Guid? unitId, CancellationToken ct)
    {
        var catalog = unitId is null ? Staff : Rooms;
        await using var connection = await OpenAsync(unitId, ct);
        await using var command = database.Command(connection, $"""
            WITH due AS (
                SELECT i.id FROM {catalog.Table} i CROSS JOIN {catalog.Policy} p
                WHERE {catalog.Disabled} AND ({DueAt}) <= $1 AND ({catalog.Unlinked})
                ORDER BY i.id LIMIT 50 FOR UPDATE OF i SKIP LOCKED FOR SHARE OF p
            ), removed AS (
                DELETE FROM {catalog.Table} i USING due WHERE i.id=due.id AND {catalog.Disabled} RETURNING i.id
            )
            INSERT INTO {catalog.Events}(item_id,removed_at) SELECT id,$1 FROM removed ON CONFLICT DO NOTHING
            """, time.GetUtcNow());
        return await command.ExecuteNonQueryAsync(ct);
    }

    private Task<NpgsqlConnection> OpenAsync(Guid? unitId, CancellationToken ct) =>
        unitId is { } id ? unitDatabases.OpenAsync(id, ct) : database.OpenAsync(ct);

    private async Task<RetentionPolicy> ReadPolicyAsync(NpgsqlConnection connection, Catalog catalog, CancellationToken ct)
    {
        await using var command = database.Command(connection, $"SELECT days,effective_at FROM {catalog.Policy} WHERE singleton");
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadPolicy(reader) : new(null, null);
    }

    private static RetentionPolicy ReadPolicy(NpgsqlDataReader reader) =>
        new(reader.IsDBNull(0) ? null : reader.GetInt32(0), reader.IsDBNull(1) ? null : reader.GetFieldValue<DateTimeOffset>(1));
}
