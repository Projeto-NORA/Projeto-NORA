using System.Net.Mail;
using Npgsql;
using Nora.Api.Database;
using Nora.Api.Models;
using Nora.Api.Services;

namespace Nora.Api.DAOs;

/// <summary>Internal database boundary. Callers must authorize staff and the selected UBS before use.</summary>
public sealed class UnitPatientRegistryDao(UnitDatabaseRouter databases)
{
    public async Task UpdateEmailAsync(Guid unitId, Guid actorId, string value, string? newEmail, CancellationToken ct = default)
    {
        var cpf = AccountService.NormalizeCpf(value);
        var email = newEmail?.Trim().ToLowerInvariant() ?? "";
        if (unitId == Guid.Empty || actorId == Guid.Empty || !AccountService.ValidCpf(cpf) || email.Length > 254 ||
            !MailAddress.TryCreate(email, out var address) || address.Address != email) throw new ApiInputException();
        await using var connection = await databases.OpenAsync(unitId, ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await using (var update = new NpgsqlCommand("UPDATE nora_unit.patient_registry SET email=$2 WHERE cpf=$1", connection))
        {
            update.Parameters.AddWithValue(cpf); update.Parameters.AddWithValue(email);
            if (await update.ExecuteNonQueryAsync(ct) != 1) throw new ApiNotFoundException();
        }
        await using (var audit = new NpgsqlCommand("INSERT INTO nora_unit.patient_registry_events(cpf,actor_id,action) VALUES($1,$2,'email_updated')", connection))
        {
            audit.Parameters.AddWithValue(cpf); audit.Parameters.AddWithValue(actorId);
            await audit.ExecuteNonQueryAsync(ct);
        }
        await transaction.CommitAsync(ct);
    }

    public async Task CreateAsync(Guid unitId, Guid actorId, UnitPatientRecord patient, CancellationToken ct = default)
    {
        var normalized = Normalize(unitId, actorId, patient);
        var cpf = normalized.Cpf; var name = normalized.FullName; var email = normalized.Email; var phone = normalized.Phone; var patientAddress = normalized.Address;
        await using var connection = await databases.OpenAsync(unitId, ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await using (var insert = new NpgsqlCommand("INSERT INTO nora_unit.patient_registry(cpf,full_name,email,created_by,phone,cep,street,address_number,complement,neighborhood,city,state,ibge) VALUES($1,$2,$3,$4,NULLIF($5,''),NULLIF($6,''),NULLIF($7,''),NULLIF($8,''),NULLIF($9,''),NULLIF($10,''),NULLIF($11,''),NULLIF($12,''),NULLIF($13,'')) ON CONFLICT(cpf) DO NOTHING", connection))
        {
            foreach (var value in new object[] { cpf, name, email, actorId, phone ?? "", patientAddress?.Cep ?? "", patientAddress?.Street ?? "", patientAddress?.Number ?? "", patientAddress?.Complement ?? "", patientAddress?.Neighborhood ?? "", patientAddress?.City ?? "", patientAddress?.State ?? "", patientAddress?.Ibge ?? "" })
                insert.Parameters.Add(new NpgsqlParameter { Value = value });
            if (await insert.ExecuteNonQueryAsync(ct) != 1) throw new ApiConflictException();
        }
        await using (var audit = new NpgsqlCommand("INSERT INTO nora_unit.patient_registry_events(cpf,actor_id,action) VALUES($1,$2,'created')", connection))
        {
            audit.Parameters.AddWithValue(cpf); audit.Parameters.AddWithValue(actorId);
            await audit.ExecuteNonQueryAsync(ct);
        }
        await transaction.CommitAsync(ct);
    }

    public async Task UpdateAsync(Guid unitId, Guid actorId, UnitPatientRecord patient, CancellationToken ct = default)
    {
        var normalized = Normalize(unitId, actorId, patient); var address = normalized.Address;
        await using var connection = await databases.OpenAsync(unitId, ct); await using var transaction = await connection.BeginTransactionAsync(ct);
        await using (var update = new NpgsqlCommand("UPDATE nora_unit.patient_registry SET full_name=$2,email=$3,phone=NULLIF($4,''),cep=NULLIF($5,''),street=NULLIF($6,''),address_number=NULLIF($7,''),complement=NULLIF($8,''),neighborhood=NULLIF($9,''),city=NULLIF($10,''),state=NULLIF($11,''),ibge=NULLIF($12,'') WHERE cpf=$1", connection))
        {
            foreach (var value in new object[] { normalized.Cpf, normalized.FullName, normalized.Email, normalized.Phone ?? "", address?.Cep ?? "", address?.Street ?? "", address?.Number ?? "", address?.Complement ?? "", address?.Neighborhood ?? "", address?.City ?? "", address?.State ?? "", address?.Ibge ?? "" })
                update.Parameters.AddWithValue(value);
            if (await update.ExecuteNonQueryAsync(ct) != 1) throw new ApiNotFoundException();
        }
        await using (var audit = new NpgsqlCommand("INSERT INTO nora_unit.patient_registry_events(cpf,actor_id,action) VALUES($1,$2,'updated')", connection))
        { audit.Parameters.AddWithValue(normalized.Cpf); audit.Parameters.AddWithValue(actorId); await audit.ExecuteNonQueryAsync(ct); }
        await transaction.CommitAsync(ct);
    }

    private static UnitPatientRecord Normalize(Guid unitId, Guid actorId, UnitPatientRecord patient)
    {
        var cpf = AccountService.NormalizeCpf(patient.Cpf); var name = patient.FullName?.Trim() ?? ""; var email = patient.Email?.Trim().ToLowerInvariant() ?? "";
        if (unitId == Guid.Empty || actorId == Guid.Empty || !AccountService.ValidCpf(cpf) || name.Length is < 2 or > 160 ||
            name.Any(char.IsControl) || email.Length > 254 || !MailAddress.TryCreate(email, out var parsed) || parsed.Address != email) throw new ApiInputException();
        return new(cpf, name, email, AccountService.NormalizePhone(patient.Phone), AccountService.NormalizeAddress(patient.Address));
    }

    public async Task<List<UnitPatientRecord>> ListAsync(Guid unitId, CancellationToken ct = default)
    {
        await using var connection = await databases.OpenAsync(unitId, ct);
        await using var query = new NpgsqlCommand("SELECT cpf,full_name,email,phone,cep,street,address_number,complement,neighborhood,city,state,ibge,is_active,created_at FROM nora_unit.patient_registry ORDER BY full_name,cpf", connection);
        await using var reader = await query.ExecuteReaderAsync(ct);
        var result = new List<UnitPatientRecord>();
        while (await reader.ReadAsync(ct)) result.Add(Read(reader));
        return result;
    }

    public async Task<UnitPatientRecord?> FindByCpfAsync(Guid unitId, string? value, CancellationToken ct = default)
    {
        var cpf = AccountService.NormalizeCpf(value);
        if (!AccountService.ValidCpf(cpf)) throw new ApiInputException();
        await using var connection = await databases.OpenAsync(unitId, ct);
        await using var query = new NpgsqlCommand("SELECT cpf,full_name,email,phone,cep,street,address_number,complement,neighborhood,city,state,ibge,is_active,created_at FROM nora_unit.patient_registry WHERE cpf=$1 AND is_active", connection);
        query.Parameters.AddWithValue(cpf);
        await using var reader = await query.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Read(reader) : null;
    }

    private static UnitPatientRecord Read(NpgsqlDataReader reader)
    {
        string? Optional(int index) => reader.IsDBNull(index) ? null : reader.GetString(index);
        AddressInfo? address = reader.IsDBNull(4) ? null : new(reader.GetString(4), reader.GetString(5), Optional(6), Optional(7), Optional(8), reader.GetString(9), reader.GetString(10), Optional(11));
        return new(reader.GetString(0), reader.GetString(1), reader.GetString(2), Optional(3), address, reader.GetBoolean(12), false, reader.GetFieldValue<DateTimeOffset>(13));
    }

    public async Task<bool> IsActiveAsync(Guid unitId, string cpf, CancellationToken ct)
    {
        await using var connection = await databases.OpenAsync(unitId, ct);
        await using var query = new NpgsqlCommand("SELECT EXISTS(SELECT 1 FROM nora_unit.patient_registry WHERE cpf=$1 AND is_active)", connection);
        query.Parameters.AddWithValue(cpf); return await query.ExecuteScalarAsync(ct) is true;
    }
}
