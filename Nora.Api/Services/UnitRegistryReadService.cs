using Nora.Api.DAOs;
using Nora.Api.Database;
using Nora.Api.Models;

namespace Nora.Api.Services;

public sealed class UnitRegistryReadService(UnitPatientRegistryDao registry, DatabaseHelper central, TimeProvider time)
{
    public async Task<List<UnitPatientRecord>> ListAsync(Guid unitId, CancellationToken ct)
    {
        var records = await registry.ListAsync(unitId, ct);
        if (records.Count == 0) return records;
        var online = new HashSet<string>(StringComparer.Ordinal);
        var now = time.GetUtcNow();
        await using var connection = await central.OpenAsync(ct);
        await using var query = central.Command(connection, "SELECT a.cpf FROM nora.accounts a WHERE a.scope='patient' AND a.is_active AND a.cpf=ANY($1) AND EXISTS(SELECT 1 FROM nora.sessions s WHERE s.account_id=a.id AND s.revoked_at IS NULL AND s.expires_at>$2 AND s.presence_seen_at>$3)", records.Select(record => record.Cpf).ToArray(), now, now - AuthService.PresenceTimeout);
        await using var reader = await query.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) online.Add(reader.GetString(0));
        return records.Select(record => record with { IsOnline = online.Contains(record.Cpf) }).ToList();
    }
}
