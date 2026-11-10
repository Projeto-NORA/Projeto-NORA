using Nora.Api.Database;
using Nora.Api.Models;
using Nora.Api.Services;

namespace Nora.Api.DAOs;

public sealed class HealthUnitDao(DatabaseHelper database)
{
    public async Task<List<HealthUnit>> ListAsync(CancellationToken ct)
    {
        await using var connection = await database.OpenAsync(ct);
        await using var command = database.Command(connection, "SELECT id,name,region,address,latitude,longitude FROM nora.health_units ORDER BY name,id");
        await using var reader = await command.ExecuteReaderAsync(ct);
        var units = new List<HealthUnit>();
        while (await reader.ReadAsync(ct)) units.Add(Read(reader));
        return units;
    }
    public async Task<List<HealthUnit>> ListForPatientAsync(Guid patientId, CancellationToken ct)
    {
        await using var connection = await database.OpenAsync(ct);
        await using var command = database.Command(connection, "SELECT u.id,u.name,u.region,u.address,u.latitude,u.longitude FROM nora.health_units u JOIN nora.patient_units p ON p.unit_id=u.id WHERE p.patient_id=$1 ORDER BY u.name,u.id", patientId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var units = new List<HealthUnit>();
        while (await reader.ReadAsync(ct)) units.Add(Read(reader));
        return units;
    }
    public async Task<List<HealthUnit>> ListForStaffAsync(Guid accountId, CancellationToken ct)
    {
        await using var connection = await database.OpenAsync(ct);
        await using var command = database.Command(connection, "SELECT u.id,u.name,u.region,u.address,u.latitude,u.longitude FROM nora.health_units u JOIN nora.staff_units s ON s.unit_id=u.id WHERE s.account_id=$1 ORDER BY u.name,u.id", accountId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var units = new List<HealthUnit>();
        while (await reader.ReadAsync(ct)) units.Add(Read(reader));
        return units;
    }
    public async Task<List<StaffAccount>> ListStaffAsync(CancellationToken ct)
    {
        await using var connection = await database.OpenAsync(ct);
        await using var command = database.Command(connection, "SELECT id,full_name,role FROM nora.accounts WHERE scope='obs' AND is_active ORDER BY full_name,id");
        await using var reader = await command.ExecuteReaderAsync(ct);
        var staff = new List<StaffAccount>();
        while (await reader.ReadAsync(ct)) staff.Add(new(reader.GetGuid(0), reader.GetString(1), reader.GetString(2)));
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
        await using var command = database.Command(connection, "INSERT INTO nora.health_units(id,name,region,address,latitude,longitude) VALUES($1,$2,$3,$4,$5,$6) RETURNING id,name,region,address,latitude,longitude", unit.Id, unit.Name, unit.Region, unit.Address, unit.Latitude, unit.Longitude);
        await using var reader = await command.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        return Read(reader);
    }
    public async Task<HealthUnit> UpdateAsync(HealthUnit unit, CancellationToken ct)
    {
        await using var connection = await database.OpenAsync(ct);
        await using var command = database.Command(connection, "UPDATE nora.health_units SET name=$2,region=$3,address=$4,latitude=$5,longitude=$6 WHERE id=$1 RETURNING id,name,region,address,latitude,longitude", unit.Id, unit.Name, unit.Region, unit.Address, unit.Latitude, unit.Longitude);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) throw new ApiNotFoundException();
        return Read(reader);
    }
    private static HealthUnit Read(Npgsql.NpgsqlDataReader reader) => new(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetDouble(4), reader.GetDouble(5));
}
