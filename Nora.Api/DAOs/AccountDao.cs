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
            "SELECT id,full_name,role,scope,must_change_password,cpf,email,password_hash FROM nora.accounts WHERE scope=$1 AND is_active AND " + (scope == "patient" ? "(cpf=$2 OR email=$2)" : "email=$2"), scope, identifier);
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadAccount(reader) : null;
    }, ct);

    public Task<PatientInfo> CreateAsync(string scope, string role, CreatePatientRequest request, string passwordHash, CancellationToken ct, string? municipalityName = null) => database.UseAsync(async connection =>
    {
        var id = Guid.NewGuid();
        await using var command = database.Command(connection,
            "INSERT INTO nora.accounts(id,full_name,cpf,email,password_hash,scope,role,phone,cep,street,address_number,complement,neighborhood,city,state,ibge,municipality_name) VALUES($1,$2,NULLIF($3,''),$4,$5,$6,$7,NULLIF($8,''),NULLIF($9,''),NULLIF($10,''),NULLIF($11,''),NULLIF($12,''),NULLIF($13,''),NULLIF($14,''),NULLIF($15,''),NULLIF($16,''),NULLIF($17,'')) RETURNING created_at",
            id, request.FullName, request.Cpf, request.Email, passwordHash, scope, role, request.Phone ?? "", request.Address?.Cep ?? "", request.Address?.Street ?? "", request.Address?.Number ?? "", request.Address?.Complement ?? "", request.Address?.Neighborhood ?? "", request.Address?.City ?? "", request.Address?.State ?? "", request.Address?.Ibge ?? "", municipalityName ?? "");
        await using var reader = await command.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        return new PatientInfo(id, request.FullName, request.Cpf, request.Email, request.Phone, CreatedAt: reader.GetFieldValue<DateTimeOffset>(0), Address: request.Address);
    }, ct);

    public Task<List<PatientInfo>> PatientsAsync(DateTimeOffset now, CancellationToken ct) => database.UseAsync(async connection =>
    {
        await using var command = database.Command(connection,
            "SELECT a.id,a.full_name,a.cpf,a.email,a.phone,a.is_active AND EXISTS(SELECT 1 FROM nora.sessions s WHERE s.account_id=a.id AND s.revoked_at IS NULL AND s.expires_at>$1 AND s.presence_seen_at>$2),a.is_active,a.created_at,a.cep,a.street,a.address_number,a.complement,a.neighborhood,a.city,a.state,a.ibge FROM nora.accounts a WHERE a.scope='patient' ORDER BY lower(a.full_name),a.id", now, now - AuthService.PresenceTimeout);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var patients = new List<PatientInfo>();
        while (await reader.ReadAsync(ct))
        {
            AddressInfo? address = reader.IsDBNull(8) ? null : new(reader.GetString(8), reader.GetString(9), ReadOptional(reader, 10), ReadOptional(reader, 11), ReadOptional(reader, 12), reader.GetString(13), reader.GetString(14), ReadOptional(reader, 15));
            patients.Add(new(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), ReadOptional(reader, 4), reader.GetBoolean(5), reader.GetBoolean(6), reader.GetFieldValue<DateTimeOffset>(7), address));
        }
        return patients;
    }, ct);

    /// <summary>Troca o hash só se ainda é o que foi lido no login: uma troca de senha concorrente não é desfeita pelo rehash (0 linhas → nada a fazer).</summary>
    public Task<int> RehashAsync(Guid id, string expectedHash, string hash, CancellationToken ct) => database.UseAsync(connection =>
        database.ExecuteAsync(connection, "UPDATE nora.accounts SET password_hash=$3 WHERE id=$1 AND password_hash=$2", ct, id, expectedHash, hash), ct);

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

    /// <summary>
    /// Redefinição feita pelo terminal da equipe técnica (<c>--reset-password</c>): troca o hash de uma conta de equipe ativa, exige nova troca no próximo login, revoga todas as sessões
    /// e registra o evento sem autor (o histórico mostra "Sistema"). Retorna falso se a conta não existe, está inativa ou é de outro escopo; nada é alterado nesse caso.
    /// </summary>
    public Task<bool> ResetStaffPasswordAsync(string scope, string email, string newHash, DateTimeOffset now, CancellationToken ct) => database.UseAsync(async connection =>
    {
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await using var update = database.Command(connection,
            "UPDATE nora.accounts SET password_hash=$3,must_change_password=true WHERE scope=$1 AND email=$2 AND is_active RETURNING id", scope, email, newHash);
        if (await update.ExecuteScalarAsync(ct) is not Guid id) return false;
        await database.ExecuteAsync(connection, "UPDATE nora.sessions SET revoked_at=$2,presence_seen_at=NULL WHERE account_id=$1 AND revoked_at IS NULL", ct, id, now);
        await database.ExecuteAsync(connection, "INSERT INTO nora.staff_account_events(account_id,actor_id,action) VALUES($1,NULL,'password_reset')", ct, id);
        await transaction.CommitAsync(ct);
        return true;
    }, ct);

    private static Account ReadAccount(NpgsqlDataReader reader) => new(
        new UserInfo(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetBoolean(4)).WithContact(reader.IsDBNull(5) ? null : reader.GetString(5), reader.GetString(6)),
        reader.IsDBNull(5) ? "" : reader.GetString(5), reader.GetString(6), reader.GetString(7));
    private static string? ReadOptional(NpgsqlDataReader reader, int index) => reader.IsDBNull(index) ? null : reader.GetString(index);
}
