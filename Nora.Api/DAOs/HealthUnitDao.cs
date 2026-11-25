using Nora.Api.Database;
using Nora.Api.Models;
using Nora.Api.Services;

namespace Nora.Api.DAOs;

public sealed class HealthUnitDao(DatabaseHelper database, UnitDatabaseRouter router)
{
    private const string UnitColumns = "u.id,u.name,u.region,u.address,u.latitude,u.longitude,u.database_ready_at";

    public async Task<List<HealthUnit>> ListAsync(CancellationToken ct)
    {
        await using var connection = await database.OpenAsync(ct);
        await using var command = database.Command(connection, $"SELECT {UnitColumns} FROM nora.health_units u ORDER BY lower(u.name),u.id");
        await using var reader = await command.ExecuteReaderAsync(ct);
        var units = new List<HealthUnit>();
        while (await reader.ReadAsync(ct)) units.Add(Read(reader));
        return units;
    }
    public async Task<HealthUnit?> FindAsync(Guid id, CancellationToken ct)
    {
        await using var connection = await database.OpenAsync(ct);
        await using var command = database.Command(connection, $"SELECT {UnitColumns} FROM nora.health_units u WHERE u.id=$1", id);
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Read(reader) : null;
    }
    public async Task<List<HealthUnit>> ListForPatientAsync(Guid patientId, CancellationToken ct)
    {
        await using var connection = await database.OpenAsync(ct);
        await using var command = database.Command(connection, $"SELECT {UnitColumns} FROM nora.health_units u JOIN nora.patient_units p ON p.unit_id=u.id WHERE p.patient_id=$1 ORDER BY lower(u.name),u.id", patientId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var units = new List<HealthUnit>();
        while (await reader.ReadAsync(ct)) units.Add(Read(reader));
        return units;
    }
    public async Task<List<HealthUnit>> ListForStaffAsync(Guid accountId, CancellationToken ct)
    {
        await using var connection = await database.OpenAsync(ct);
        await using var command = database.Command(connection, $"SELECT {UnitColumns} FROM nora.health_units u JOIN nora.staff_units s ON s.unit_id=u.id WHERE s.account_id=$1 ORDER BY lower(u.name),u.id", accountId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var units = new List<HealthUnit>();
        while (await reader.ReadAsync(ct)) units.Add(Read(reader));
        return units;
    }
    /// <summary>Identificadores que existem e têm o banco pronto, numa única consulta.</summary>
    public async Task<HashSet<Guid>> ReadyIdsAsync(Guid[] ids, CancellationToken ct)
    {
        await using var connection = await database.OpenAsync(ct);
        await using var command = database.Command(connection, "SELECT id,database_ready_at IS NOT NULL FROM nora.health_units WHERE id=ANY($1)", ids);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var ready = new HashSet<Guid>();
        while (await reader.ReadAsync(ct)) if (reader.GetBoolean(1) || router.HasExplicitConnection(reader.GetGuid(0))) ready.Add(reader.GetGuid(0));
        return ready;
    }
    public async Task<List<StaffAccount>> ListStaffAsync(CancellationToken ct)
    {
        await using var connection = await database.OpenAsync(ct);
        await using var command = database.Command(connection, "SELECT id,full_name,role,email,is_active,must_change_password FROM nora.accounts WHERE scope='obs' ORDER BY lower(full_name),id");
        await using var reader = await command.ExecuteReaderAsync(ct);
        var staff = new List<StaffAccount>();
        while (await reader.ReadAsync(ct)) staff.Add(new(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetBoolean(4), reader.GetBoolean(5)));
        return staff;
    }
    public async Task SetStaffAccessAsync(Guid accountId, Guid unitId, Guid actorId, bool grant, CancellationToken ct)
    {
        await using var connection = await database.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await using (var validate = database.Command(connection, "SELECT EXISTS(SELECT 1 FROM nora.accounts WHERE id=$1 AND scope='obs' AND (NOT $3 OR is_active)) AND EXISTS(SELECT 1 FROM nora.health_units WHERE id=$2)", accountId, unitId, grant))
            if ((bool)(await validate.ExecuteScalarAsync(ct))! == false) throw new ApiNotFoundException();
        var count = grant
            ? await database.ExecuteAsync(connection, "INSERT INTO nora.staff_units(account_id,unit_id,granted_by) VALUES($1,$2,$3) ON CONFLICT DO NOTHING", ct, accountId, unitId, actorId)
            : await database.ExecuteAsync(connection, "DELETE FROM nora.staff_units WHERE account_id=$1 AND unit_id=$2", ct, accountId, unitId);
        if (count > 0)
            await database.ExecuteAsync(connection, "INSERT INTO nora.staff_unit_events(account_id,unit_id,actor_id,action) VALUES($1,$2,$3,$4)", ct, accountId, unitId, actorId, grant ? "granted" : "revoked");
        await transaction.CommitAsync(ct);
    }
    public async Task<HealthUnit> CreateAsync(HealthUnit unit, CancellationToken ct)
    {
        await using var connection = await database.OpenAsync(ct);
        await using var command = database.Command(connection, "INSERT INTO nora.health_units AS u(id,name,region,address,latitude,longitude) VALUES($1,$2,$3,$4,$5,$6) RETURNING " + UnitColumns, unit.Id, unit.Name, unit.Region, unit.Address, unit.Latitude, unit.Longitude);
        await using var reader = await command.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        return Read(reader);
    }
    public async Task<HealthUnit> UpdateAsync(HealthUnit unit, CancellationToken ct)
    {
        await using var connection = await database.OpenAsync(ct);
        await using var command = database.Command(connection, "UPDATE nora.health_units AS u SET name=$2,region=$3,address=$4,latitude=$5,longitude=$6 WHERE u.id=$1 RETURNING " + UnitColumns, unit.Id, unit.Name, unit.Region, unit.Address, unit.Latitude, unit.Longitude);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) throw new ApiNotFoundException();
        return Read(reader);
    }
    /// <summary>
    /// Marca o banco como pronto uma única vez. Com <paramref name="recordEvent"/> (criação/preparação pela API) registra também o evento
    /// unit-database-created; os comandos de migração (--migrate-unit, --migrate-all-units) só marcam, sem evento, pois o banco não foi criado por eles.
    /// </summary>
    public async Task<bool> MarkDatabaseReadyAsync(Guid unitId, Guid? actorId, CancellationToken ct, bool recordEvent = true)
    {
        await using var connection = await database.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        var changed = await database.ExecuteAsync(connection, "UPDATE nora.health_units SET database_ready_at=CURRENT_TIMESTAMP WHERE id=$1 AND database_ready_at IS NULL", ct, unitId);
        if (changed == 0)
        {
            await using var exists = database.Command(connection, "SELECT EXISTS(SELECT 1 FROM nora.health_units WHERE id=$1)", unitId);
            if ((bool)(await exists.ExecuteScalarAsync(ct))! == false) throw new ApiNotFoundException();
        }
        else if (recordEvent) await database.ExecuteAsync(connection, "INSERT INTO nora.unit_database_events(unit_id,actor_id) VALUES($1,$2)", ct, unitId, actorId is { } id ? (object)id : DBNull.Value);
        await transaction.CommitAsync(ct);
        return changed > 0;
    }
    private HealthUnit Read(Npgsql.NpgsqlDataReader reader) => new(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetDouble(4), reader.GetDouble(5),
        reader.IsDBNull(6) && !router.HasExplicitConnection(reader.GetGuid(0)) ? "pending" : "ready");
}
