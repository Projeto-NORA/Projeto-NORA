using Nora.Api.Database;

namespace Nora.Api.DAOs;

public sealed class MigrationDao(DatabaseHelper database)
{
    public async Task MigrateAsync(CancellationToken ct = default)
    {
        await using var connection = await database.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await database.ExecuteAsync(connection, "SELECT pg_advisory_xact_lock(62419802)", ct);
        var assembly = typeof(MigrationDao).Assembly;
        foreach (var name in assembly.GetManifestResourceNames().Where(n => n.EndsWith(".sql", StringComparison.Ordinal)).Order())
        {
            await using var stream = assembly.GetManifestResourceStream(name)!;
            using var reader = new StreamReader(stream);
            await database.ExecuteAsync(connection, (await reader.ReadToEndAsync(ct)).Replace("nora.", database.Schema + ".").Replace("SCHEMA IF NOT EXISTS nora;", "SCHEMA IF NOT EXISTS " + database.Schema + ";"), ct);
        }
        await transaction.CommitAsync(ct);
    }
}
