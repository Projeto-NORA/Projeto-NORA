using System.Globalization;
using System.Text;
using Nora.Api.Database;
using Nora.Api.Models;
using Nora.Api.Services;

namespace Nora.Api.DAOs;

/// <summary>
/// Cursor opaco do histórico: instante (ticks UTC) + origem do evento (1 a 4, uma por tabela) + id da linha, a ordem total
/// (occurred_at DESC, origem DESC, id DESC) usada na listagem. Eventos que compartilham o instante não se perdem entre páginas.
/// Texto <c>c1_</c> + base64url; o cliente trata como caixa-preta. O instante ISO do formato anterior continua aceito em <c>before</c>
/// (devolve só eventos estritamente anteriores a ele).
/// </summary>
internal readonly record struct HistoryCursor(DateTimeOffset At, short Source, long Id)
{
    private const string Prefix = "c1_";

    public string Encode() => Prefix + Convert.ToBase64String(Encoding.UTF8.GetBytes(string.Create(CultureInfo.InvariantCulture, $"{At.UtcTicks}.{Source}.{Id}"))).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>Aceita o cursor opaco ou um instante ISO (legado); qualquer outra coisa → 400.</summary>
    public static HistoryCursor Parse(string value)
    {
        if (value.StartsWith(Prefix, StringComparison.Ordinal)) return ParseOpaque(value[Prefix.Length..]);
        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var instant))
            return new(instant.ToUniversalTime(), -1, 0); // origem -1: nenhuma linha do mesmo instante é "anterior"
        throw new ApiInputException();
    }

    private static HistoryCursor ParseOpaque(string encoded)
    {
        try
        {
            var padded = encoded.Replace('-', '+').Replace('_', '/');
            padded = padded.PadRight(padded.Length + (4 - padded.Length % 4) % 4, '=');
            var parts = Encoding.UTF8.GetString(Convert.FromBase64String(padded)).Split('.');
            if (parts.Length == 3 && long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks) && short.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var source)
                && long.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var id) && ticks <= DateTimeOffset.MaxValue.Ticks && source is >= 1 and <= 4)
                return new(new DateTimeOffset(ticks, TimeSpan.Zero), source, id);
        }
        catch (Exception error) when (error is FormatException or ArgumentException or DecoderFallbackException) { }
        throw new ApiInputException();
    }
}

/// <summary>Histórico de ações da gestão: une concessões, consultas nominais, ações de conta e criação de bancos. Sem CPF nem e-mail.</summary>
public sealed class HistoryDao(DatabaseHelper database)
{
    public const int DefaultLimit = 100;
    public const int MaxLimit = 200;

    /// <param name="before">Cursor opaco devolvido em <c>cursor</c> de cada item (ou, por compatibilidade, o instante ISO): só eventos estritamente anteriores na ordem total.</param>
    public async Task<List<HistoryEvent>> ReadAsync(DateTimeOffset? from, DateTimeOffset? to, Guid? unitId, int? limit, string? before, CancellationToken ct)
    {
        var size = limit ?? DefaultLimit;
        if (size is < 1 or > MaxLimit || (from is { } start && to is { } end && end <= start)) throw new ApiInputException();
        var cursor = before is null ? (HistoryCursor?)null : HistoryCursor.Parse(before);
        // Cada ramo aplica os filtros e o limite antes da união, na mesma ordem total (instante, origem constante, id): o custo não depende do
        // tamanho do histórico e o corte de cada ramo é coerente com o corte global. O cursor compara a tupla (instante, origem, id).
        string Filter(int source) => $"($1::timestamptz IS NULL OR e.occurred_at >= $1) AND ($2::timestamptz IS NULL OR e.occurred_at < $2) AND ($4::timestamptz IS NULL OR (e.occurred_at, {source}::smallint, e.id) < ($4::timestamptz, $5::smallint, $6::bigint))";
        const string unitFilter = " AND ($3::uuid IS NULL OR e.unit_id = $3)";
        var sql = $"""
            SELECT occurred_at, kind, actor_name, unit_id, unit_name, account_name, src, id FROM (
              (SELECT e.occurred_at, CASE e.action WHEN 'granted' THEN 'staff-granted' ELSE 'staff-revoked' END AS kind, actor.full_name AS actor_name, e.unit_id, u.name AS unit_name, account.full_name AS account_name, 1::smallint AS src, e.id
                 FROM nora.staff_unit_events e LEFT JOIN nora.accounts actor ON actor.id=e.actor_id LEFT JOIN nora.accounts account ON account.id=e.account_id LEFT JOIN nora.health_units u ON u.id=e.unit_id
                 WHERE {Filter(1)}{unitFilter} ORDER BY e.occurred_at DESC, e.id DESC LIMIT $7)
              UNION ALL
              (SELECT e.occurred_at, CASE e.action WHEN 'registry_read' THEN 'registry-read' ELSE 'appointment-read' END, actor.full_name, e.unit_id, u.name, NULL, 2::smallint, e.id
                 FROM nora.unit_access_events e LEFT JOIN nora.accounts actor ON actor.id=e.actor_id LEFT JOIN nora.health_units u ON u.id=e.unit_id
                 WHERE {Filter(2)}{unitFilter} ORDER BY e.occurred_at DESC, e.id DESC LIMIT $7)
              UNION ALL
              (SELECT e.occurred_at, CASE e.action WHEN 'created' THEN 'staff-created' WHEN 'deactivated' THEN 'staff-deactivated' WHEN 'reactivated' THEN 'staff-reactivated' ELSE 'staff-password-reset' END, actor.full_name, NULL::uuid, NULL, account.full_name, 3::smallint, e.id
                 FROM nora.staff_account_events e LEFT JOIN nora.accounts actor ON actor.id=e.actor_id LEFT JOIN nora.accounts account ON account.id=e.account_id
                 WHERE {Filter(3)} AND $3::uuid IS NULL ORDER BY e.occurred_at DESC, e.id DESC LIMIT $7)
              UNION ALL
              (SELECT e.occurred_at, 'unit-database-created', actor.full_name, e.unit_id, u.name, NULL, 4::smallint, e.id
                 FROM nora.unit_database_events e LEFT JOIN nora.accounts actor ON actor.id=e.actor_id LEFT JOIN nora.health_units u ON u.id=e.unit_id
                 WHERE {Filter(4)}{unitFilter} ORDER BY e.occurred_at DESC, e.id DESC LIMIT $7)
            ) history ORDER BY occurred_at DESC, src DESC, id DESC LIMIT $7
            """;
        await using var connection = await database.OpenAsync(ct);
        await using var command = database.Command(connection, sql,
            from is { } f ? f.ToUniversalTime() : (object)DBNull.Value, to is { } t ? t.ToUniversalTime() : (object)DBNull.Value, unitId is { } u ? u : (object)DBNull.Value,
            cursor is { } c ? c.At : (object)DBNull.Value, cursor is { } cs ? cs.Source : (short)0, cursor is { } ci ? ci.Id : 0L, size);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var events = new List<HistoryEvent>();
        while (await reader.ReadAsync(ct))
        {
            var at = reader.GetFieldValue<DateTimeOffset>(0);
            events.Add(new(at, reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetGuid(3), reader.IsDBNull(4) ? null : reader.GetString(4), reader.IsDBNull(5) ? null : reader.GetString(5),
                new HistoryCursor(at, reader.GetInt16(6), reader.GetInt64(7)).Encode()));
        }
        return events;
    }
}
