using Npgsql;
using Nora.Api.Database;
using Nora.Api.Models;
using Nora.Api.Services;

namespace Nora.Api.DAOs;

public sealed class AccountDao(DatabaseHelper database)
{
    public Task<PasswordResetTarget?> FindPatientResetTargetAsync(string cpf, CancellationToken ct) => database.UseAsync(async connection =>
    {
        await using var command = database.Command(connection,
            "SELECT id,full_name,email FROM nora.accounts WHERE scope='patient' AND is_active AND cpf=$1", cpf);
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? new PasswordResetTarget(reader.GetGuid(0), reader.GetString(1), reader.GetString(2)) : null;
    }, ct);

    public Task<Account?> FindAsync(string scope, string identifier, CancellationToken ct) => database.UseAsync(async connection =>
    {
        await using var command = database.Command(connection,
            "SELECT id,full_name,role,scope,must_change_password,cpf,email,password_hash FROM nora.accounts WHERE scope=$1 AND is_active AND (cpf=$2 OR email=$2)", scope, identifier);
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadAccount(reader) : null;
    }, ct);

    public Task<PatientInfo> CreateAsync(string scope, string role, CreatePatientRequest request, string passwordHash, CancellationToken ct) => database.UseAsync(async connection =>
    {
        var id = Guid.NewGuid();
        await using var command = database.Command(connection,
            "INSERT INTO nora.accounts(id,full_name,cpf,email,password_hash,scope,role,phone,cep,street,address_number,complement,neighborhood,city,state,ibge) VALUES($1,$2,$3,$4,$5,$6,$7,NULLIF($8,''),NULLIF($9,''),NULLIF($10,''),NULLIF($11,''),NULLIF($12,''),NULLIF($13,''),NULLIF($14,''),NULLIF($15,''),NULLIF($16,'')) RETURNING created_at",
            id, request.FullName, request.Cpf, request.Email, passwordHash, scope, role, request.Phone ?? "", request.Address?.Cep ?? "", request.Address?.Street ?? "", request.Address?.Number ?? "", request.Address?.Complement ?? "", request.Address?.Neighborhood ?? "", request.Address?.City ?? "", request.Address?.State ?? "", request.Address?.Ibge ?? "");
        await using var reader = await command.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        return new PatientInfo(id, request.FullName, request.Cpf, request.Email, request.Phone, CreatedAt: reader.GetFieldValue<DateTimeOffset>(0), Address: request.Address);
    }, ct);

    public Task<List<PatientInfo>> PatientsAsync(DateTimeOffset now, CancellationToken ct) => database.UseAsync(async connection =>
    {
        await using var command = database.Command(connection,
            "SELECT a.id,a.full_name,a.cpf,a.email,a.phone,a.is_active AND EXISTS(SELECT 1 FROM nora.sessions s WHERE s.account_id=a.id AND s.revoked_at IS NULL AND s.expires_at>$1 AND s.presence_seen_at>$2),a.is_active,a.created_at,a.cep,a.street,a.address_number,a.complement,a.neighborhood,a.city,a.state,a.ibge FROM nora.accounts a WHERE a.scope='patient' ORDER BY a.created_at DESC,a.id", now, now - AuthService.PresenceTimeout);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var patients = new List<PatientInfo>();
        while (await reader.ReadAsync(ct))
        {
            AddressInfo? address = reader.IsDBNull(8) ? null : new(reader.GetString(8), reader.GetString(9), ReadOptional(reader, 10), ReadOptional(reader, 11), ReadOptional(reader, 12), reader.GetString(13), reader.GetString(14), ReadOptional(reader, 15));
            patients.Add(new(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), ReadOptional(reader, 4), reader.GetBoolean(5), reader.GetBoolean(6), reader.GetFieldValue<DateTimeOffset>(7), address));
        }
        return patients;
    }, ct);

    public Task<int> RehashAsync(Guid id, string hash, CancellationToken ct) => database.UseAsync(connection =>
        database.ExecuteAsync(connection, "UPDATE nora.accounts SET password_hash=$2 WHERE id=$1", ct, id, hash), ct);

    public Task<string?> FindPasswordHashAsync(Guid id, CancellationToken ct) => database.UseAsync(async connection =>
    {
        await using var command = database.Command(connection, "SELECT password_hash FROM nora.accounts WHERE id=$1 AND is_active", id);
        return await command.ExecuteScalarAsync(ct) as string;
    }, ct);

    /// <summary>Troca a senha só se o hash lido ainda é o atual (concorrência otimista), limpa a troca obrigatória e revoga as outras sessões.</summary>
    public Task<bool> ChangePasswordAsync(Guid id, string currentTokenHash, string expectedHash, string newHash, DateTimeOffset now, CancellationToken ct) => database.UseAsync(async connection =>
    {
        await using var transaction = await connection.BeginTransactionAsync(ct);
        if (await database.ExecuteAsync(connection, "UPDATE nora.accounts SET password_hash=$3,must_change_password=false WHERE id=$1 AND is_active AND password_hash=$2", ct, id, expectedHash, newHash) != 1) return false;
        await database.ExecuteAsync(connection, "UPDATE nora.sessions SET revoked_at=$3,presence_seen_at=NULL WHERE account_id=$1 AND revoked_at IS NULL AND token_hash<>$2", ct, id, currentTokenHash, now);
        await transaction.CommitAsync(ct);
        return true;
    }, ct);

    private static Account ReadAccount(NpgsqlDataReader reader) => new(
        new(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetBoolean(4)),
        reader.GetString(5), reader.GetString(6), reader.GetString(7));
    private static string? ReadOptional(NpgsqlDataReader reader, int index) => reader.IsDBNull(index) ? null : reader.GetString(index);
}
