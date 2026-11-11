using Npgsql;
using Nora.Api.Database;
using Nora.Api.Models;
using Nora.Api.Services;

namespace Nora.Api.DAOs;

public sealed class UnitNetworkSummaryDao(HealthUnitDao units, UnitDatabaseRouter databases, TimeProvider time)
{
    public async Task<MunicipalSummary> ReadAsync(Guid? selectedUnitId, CancellationToken ct)
    {
        var all = await units.ListAsync(ct);
        if (selectedUnitId is { } selected && all.All(unit => unit.Id != selected)) throw new ApiNotFoundException();
        var chosen = selectedUnitId is { } id ? all.Where(unit => unit.Id == id) : all;
        var cpfs = new HashSet<string>(StringComparer.Ordinal); var totals = new long[9];
        var cohorts = new long[3, 8]; var now = time.GetUtcNow();
        foreach (var unit in chosen)
        {
            await using var connection = await databases.OpenAsync(unit.Id, ct);
            await using (var query = new NpgsqlCommand("SELECT cpf FROM nora_unit.patient_registry", connection))
            await using (var reader = await query.ExecuteReaderAsync(ct)) while (await reader.ReadAsync(ct)) cpfs.Add(reader.GetString(0));
            const string totalSql = "SELECT (SELECT count(*) FROM nora_unit.waiting_list WHERE status IN ('waiting','offered')),(SELECT count(*) FROM nora_unit.lightning_slots WHERE status IN ('open','offered')),(SELECT count(*) FROM nora_unit.lightning_offers WHERE status='accepted'),(SELECT count(*) FROM nora_unit.lightning_slots),(SELECT count(*) FROM nora_unit.lightning_offers),(SELECT count(*) FROM nora_unit.lightning_offers WHERE status='declined'),(SELECT count(*) FROM nora_unit.lightning_offers WHERE status='expired'),(SELECT count(*) FROM nora_unit.lightning_offers WHERE status='pending'),(SELECT count(*) FROM nora_unit.lightning_offers WHERE status='cancelled')";
            await using (var query = new NpgsqlCommand(totalSql, connection))
            await using (var reader = await query.ExecuteReaderAsync(ct))
            { await reader.ReadAsync(ct); for (var i = 0; i < totals.Length; i++) totals[i] += reader.GetInt64(i); }
            const string cohortSql = """
                SELECT d.days, count(DISTINCT s.id) FILTER (WHERE s.status IN ('open','offered')),
                  count(o.id) FILTER (WHERE o.status='accepted'), count(DISTINCT s.id), count(o.id),
                  count(o.id) FILTER (WHERE o.status='declined'), count(o.id) FILTER (WHERE o.status='expired'),
                  count(o.id) FILTER (WHERE o.status='pending'), count(o.id) FILTER (WHERE o.status='cancelled')
                FROM (VALUES (7),(30),(90)) AS d(days)
                LEFT JOIN nora_unit.lightning_slots s ON s.created_at >= $1 - d.days * interval '1 day' AND s.created_at <= $1
                LEFT JOIN nora_unit.lightning_offers o ON o.slot_id=s.id GROUP BY d.days ORDER BY d.days
                """;
            await using (var query = new NpgsqlCommand(cohortSql, connection))
            {
                query.Parameters.AddWithValue(now); await using var reader = await query.ExecuteReaderAsync(ct); var row = 0;
                while (await reader.ReadAsync(ct)) { for (var i = 0; i < 8; i++) cohorts[row, i] += reader.GetInt64(i + 1); row++; }
            }
        }
        var windows = new List<LightningCohort>(); var days = new[] { 7, 30, 90 };
        for (var i = 0; i < days.Length; i++) windows.Add(new(days[i], cohorts[i, 0], cohorts[i, 1], cohorts[i, 2], cohorts[i, 3], cohorts[i, 4], cohorts[i, 5], cohorts[i, 6], cohorts[i, 7]));
        return new(cpfs.Count, totals[0], totals[1], totals[2], totals[3], totals[4], totals[5], totals[6], totals[7], totals[8], windows);
    }
}
