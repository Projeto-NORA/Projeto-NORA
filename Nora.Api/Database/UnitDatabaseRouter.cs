using Npgsql;

namespace Nora.Api.Database;

/// <summary>Database connections are chosen only from server configuration, never from a client-supplied URL.</summary>
public sealed class UnitDatabaseRouter(IConfiguration configuration) : IAsyncDisposable
{
    private readonly Dictionary<Guid, NpgsqlDataSource> sources = [];
    private readonly object sync = new();
    public bool IsConfigured(Guid unitId) => unitId != Guid.Empty && !string.IsNullOrWhiteSpace(configuration[$"Nora:UnitDatabases:{unitId:N}"]);

    private NpgsqlDataSource Source(Guid unitId)
    {
        if (unitId == Guid.Empty) throw new DatabaseUnavailableException();
        lock (sync)
        {
            if (sources.TryGetValue(unitId, out var existing)) return existing;
            var value = configuration[$"Nora:UnitDatabases:{unitId:N}"];
            if (string.IsNullOrWhiteSpace(value)) throw new DatabaseUnavailableException();
            var safe = new NpgsqlConnectionStringBuilder(value)
            {
                IncludeErrorDetail = false,
                LogParameters = false,
                MaxPoolSize = Math.Min(new NpgsqlConnectionStringBuilder(value).MaxPoolSize, 10)
            };
            var source = NpgsqlDataSource.Create(safe.ConnectionString);
            sources.Add(unitId, source);
            return source;
        }
    }

    internal Task<NpgsqlConnection> OpenForMigrationAsync(Guid unitId, CancellationToken ct) => Source(unitId).OpenConnectionAsync(ct).AsTask();

    public async Task<NpgsqlConnection> OpenAsync(Guid unitId, CancellationToken ct = default)
    {
        var connection = await Source(unitId).OpenConnectionAsync(ct);
        try
        {
            await using var command = new NpgsqlCommand("SELECT unit_id FROM nora_unit.identity WHERE singleton", connection);
            if (await command.ExecuteScalarAsync(ct) is not Guid actual || actual != unitId) throw new DatabaseUnavailableException();
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        List<NpgsqlDataSource> captured;
        lock (sync) { captured = sources.Values.ToList(); sources.Clear(); }
        foreach (var source in captured) await source.DisposeAsync();
    }
}
