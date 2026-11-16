using Npgsql;
using Nora.Api.Database;

namespace Nora.Api.DAOs;

public sealed class UnitMigrationDao(UnitDatabaseRouter databases, DatabaseHelper central)
{
    public async Task MigrateAsync(Guid unitId, CancellationToken ct = default)
    {
        if (unitId == Guid.Empty) throw new ArgumentException("UBS inválida.", nameof(unitId));
        await using var connection = await databases.OpenForMigrationAsync(unitId, ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await using (var checkCentral = new NpgsqlCommand("SELECT to_regclass($1) IS NOT NULL", connection))
        {
            checkCentral.Parameters.AddWithValue(central.Schema + ".accounts");
            if (await checkCentral.ExecuteScalarAsync(ct) is true) throw new DatabaseUnavailableException();
        }
        await using (var lockCommand = new NpgsqlCommand("SELECT pg_advisory_xact_lock(62419804)", connection))
            await lockCommand.ExecuteNonQueryAsync(ct);
        var assembly = typeof(UnitMigrationDao).Assembly;
        foreach (var name in UnitSchema.ResourceNames)
        {
            await using var stream = assembly.GetManifestResourceStream(name)!;
            using var reader = new StreamReader(stream);
            await using var migration = new NpgsqlCommand(await reader.ReadToEndAsync(ct), connection);
            await migration.ExecuteNonQueryAsync(ct);
        }
        await using (var insert = new NpgsqlCommand("INSERT INTO nora_unit.identity(singleton,unit_id) VALUES(true,$1) ON CONFLICT(singleton) DO NOTHING", connection))
        {
            insert.Parameters.AddWithValue(unitId);
            await insert.ExecuteNonQueryAsync(ct);
        }
        await using (var check = new NpgsqlCommand("SELECT unit_id FROM nora_unit.identity WHERE singleton", connection))
            if (await check.ExecuteScalarAsync(ct) is not Guid actual || actual != unitId) throw new DatabaseUnavailableException();
        // Carimbo da versão: a API só abre o banco de uma UBS que o tenha na versão esperada (UnitDatabaseRouter.OpenAsync).
        await using (var stamp = new NpgsqlCommand("INSERT INTO nora_unit.schema_version(singleton,version) VALUES(true,$1) ON CONFLICT(singleton) DO UPDATE SET version=GREATEST(nora_unit.schema_version.version,EXCLUDED.version)", connection))
        {
            stamp.Parameters.AddWithValue(UnitSchema.ExpectedVersion);
            await stamp.ExecuteNonQueryAsync(ct);
        }
        await transaction.CommitAsync(ct);
        databases.MarkSchemaCurrent(unitId);
    }
}
