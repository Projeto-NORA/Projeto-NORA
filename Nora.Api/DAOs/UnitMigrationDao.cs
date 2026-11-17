using Npgsql;
using Nora.Api.Database;

namespace Nora.Api.DAOs;

public sealed class UnitMigrationDao(UnitDatabaseRouter databases, DatabaseHelper central)
{
    public async Task MigrateAsync(Guid unitId, CancellationToken ct = default)
    {
        if (unitId == Guid.Empty) throw new ArgumentException("UBS inválida.", nameof(unitId));
        // A conexão da migração é devolvida ao pool antes da carga do índice, que reaproveita a mesma conexão: o cluster de teste tem poucas conexões.
        await ApplyAsync(unitId, ct);
        await IndexRegistryAsync(unitId, ct);
    }

    private async Task ApplyAsync(Guid unitId, CancellationToken ct)
    {
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

    private const int IndexBatch = 2000;

    /// <summary>
    /// Carga inicial e reparo do índice central de cadastros (<c>nora.unit_registry_index</c>, usado na conferência de consulta em outra UBS): copia todos os CPFs do cadastro da UBS, em lotes, sem duplicar. Roda ao fim de toda
    /// migração (<c>--migrate-unit</c>, <c>--migrate-all-units</c>, criação e nova tentativa do banco). UBS que não está cadastrada no banco central (ou API sem banco central) não tem o que indexar.
    /// </summary>
    private async Task IndexRegistryAsync(Guid unitId, CancellationToken ct)
    {
        if (!central.IsConfigured) return;
        await using var centralConnection = await central.OpenAsync(ct);
        // Sem o cadastro de UBS no central (teste de um banco de UBS isolado) não há o que indexar; central na versão antiga (sem o índice) é erro de ordem de implantação.
        await using (var tables = central.Command(centralConnection, "SELECT to_regclass($1) IS NOT NULL,to_regclass($2) IS NOT NULL", central.Schema + ".health_units", central.Schema + ".unit_registry_index"))
        await using (var reader = await tables.ExecuteReaderAsync(ct))
        {
            await reader.ReadAsync(ct);
            if (!reader.GetBoolean(0)) return;
            if (!reader.GetBoolean(1)) throw new InvalidOperationException("O banco central ainda não tem nora.unit_registry_index: rode Nora.Api --migrate (central até a 021) antes de migrar as UBS.");
        }
        await using (var exists = central.Command(centralConnection, "SELECT EXISTS(SELECT 1 FROM nora.health_units WHERE id=$1)", unitId))
            if (await exists.ExecuteScalarAsync(ct) is not true) return;
        await using var connection = await databases.OpenAsync(unitId, ct);
        var after = "";
        while (true)
        {
            var batch = new List<string>(IndexBatch);
            await using (var query = new NpgsqlCommand("SELECT cpf FROM nora_unit.patient_registry WHERE cpf>$1 ORDER BY cpf LIMIT $2", connection))
            {
                query.Parameters.AddWithValue(after); query.Parameters.AddWithValue(IndexBatch);
                await using var reader = await query.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct)) batch.Add(reader.GetString(0));
            }
            if (batch.Count == 0) return;
            await central.ExecuteAsync(centralConnection, "INSERT INTO nora.unit_registry_index(cpf,unit_id) SELECT unnest($1::varchar[]),$2 ON CONFLICT DO NOTHING", ct, (object)batch.ToArray(), unitId);
            if (batch.Count < IndexBatch) return;
            after = batch[^1];
        }
    }
}
