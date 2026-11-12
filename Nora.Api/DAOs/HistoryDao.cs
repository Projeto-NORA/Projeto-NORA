using Nora.Api.Database;
using Nora.Api.Models;
using Nora.Api.Services;

namespace Nora.Api.DAOs;

/// <summary>Histórico de ações da gestão: une concessões, consultas nominais, ações de conta e criação de bancos. Sem CPF nem e-mail.</summary>
public sealed class HistoryDao(DatabaseHelper database)
{
    public const int DefaultLimit = 100;
    public const int MaxLimit = 200;

    /// <param name="before">Cursor por instante: devolve somente eventos estritamente anteriores (use o occurredAt do último item da página).</param>
    public async Task<List<HistoryEvent>> ReadAsync(DateTimeOffset? from, DateTimeOffset? to, Guid? unitId, int? limit, DateTimeOffset? before, CancellationToken ct)
    {
        var size = limit ?? DefaultLimit;
        if (size is < 1 or > MaxLimit || (from is { } start && to is { } end && end <= start)) throw new ApiInputException();
        // Cada ramo aplica os filtros e o limite antes da união: o custo não depende do tamanho total do histórico.
        const string filter = "($1::timestamptz IS NULL OR e.occurred_at >= $1) AND ($2::timestamptz IS NULL OR e.occurred_at < $2) AND ($4::timestamptz IS NULL OR e.occurred_at < $4)";
        const string unitFilter = " AND ($3::uuid IS NULL OR e.unit_id = $3)";
        var sql = $"""
            SELECT occurred_at, kind, actor_name, unit_id, unit_name, account_name FROM (
              (SELECT e.occurred_at, CASE e.action WHEN 'granted' THEN 'staff-granted' ELSE 'staff-revoked' END AS kind, actor.full_name AS actor_name, e.unit_id, u.name AS unit_name, account.full_name AS account_name
                 FROM nora.staff_unit_events e LEFT JOIN nora.accounts actor ON actor.id=e.actor_id LEFT JOIN nora.accounts account ON account.id=e.account_id LEFT JOIN nora.health_units u ON u.id=e.unit_id
                 WHERE {filter}{unitFilter} ORDER BY e.occurred_at DESC LIMIT $5)
              UNION ALL
              (SELECT e.occurred_at, CASE e.action WHEN 'registry_read' THEN 'registry-read' ELSE 'appointment-read' END, actor.full_name, e.unit_id, u.name, NULL
                 FROM nora.unit_access_events e LEFT JOIN nora.accounts actor ON actor.id=e.actor_id LEFT JOIN nora.health_units u ON u.id=e.unit_id
                 WHERE {filter}{unitFilter} ORDER BY e.occurred_at DESC LIMIT $5)
              UNION ALL
              (SELECT e.occurred_at, CASE e.action WHEN 'created' THEN 'staff-created' WHEN 'deactivated' THEN 'staff-deactivated' WHEN 'reactivated' THEN 'staff-reactivated' ELSE 'staff-password-reset' END, actor.full_name, NULL::uuid, NULL, account.full_name
                 FROM nora.staff_account_events e LEFT JOIN nora.accounts actor ON actor.id=e.actor_id LEFT JOIN nora.accounts account ON account.id=e.account_id
                 WHERE {filter} AND $3::uuid IS NULL ORDER BY e.occurred_at DESC LIMIT $5)
              UNION ALL
              (SELECT e.occurred_at, 'unit-database-created', actor.full_name, e.unit_id, u.name, NULL
                 FROM nora.unit_database_events e LEFT JOIN nora.accounts actor ON actor.id=e.actor_id LEFT JOIN nora.health_units u ON u.id=e.unit_id
                 WHERE {filter}{unitFilter} ORDER BY e.occurred_at DESC LIMIT $5)
            ) history ORDER BY occurred_at DESC, kind LIMIT $5
            """;
        await using var connection = await database.OpenAsync(ct);
        await using var command = database.Command(connection, sql,
            from is { } f ? f.ToUniversalTime() : (object)DBNull.Value, to is { } t ? t.ToUniversalTime() : (object)DBNull.Value, unitId is { } u ? u : (object)DBNull.Value,
            before is { } b ? b.ToUniversalTime() : (object)DBNull.Value, size);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var events = new List<HistoryEvent>();
        while (await reader.ReadAsync(ct))
            events.Add(new(reader.GetFieldValue<DateTimeOffset>(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetGuid(3), reader.IsDBNull(4) ? null : reader.GetString(4), reader.IsDBNull(5) ? null : reader.GetString(5)));
        return events;
    }
}
