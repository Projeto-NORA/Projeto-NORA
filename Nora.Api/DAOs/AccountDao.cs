using Npgsql;
using Nora.Api.Database;
using Nora.Api.Models;
using Nora.Api.Services;

namespace Nora.Api.DAOs;

public sealed class AccountDao(DatabaseHelper database)
{
    public Task<Account?> FindAsync(string scope, string identifier, CancellationToken ct) => database.UseAsync(async connection =>
    {
        await using var command = database.Command(connection,
            "SELECT id,full_name,role,scope,cpf,email,password_hash FROM nora.accounts WHERE scope=$1 AND (cpf=$2 OR email=$2)", scope, identifier);
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadAccount(reader) : null;
    }, ct);

    public Task<PatientInfo> CreateAsync(string scope, string role, CreatePatientRequest request, string passwordHash, CancellationToken ct) => database.UseAsync(async connection =>
    {
        var id = Guid.NewGuid();
        await database.ExecuteAsync(connection,
            "INSERT INTO nora.accounts(id,full_name,cpf,email,password_hash,scope,role,phone) VALUES($1,$2,$3,$4,$5,$6,$7,NULLIF($8,''))", ct,
            id, request.FullName, request.Cpf, request.Email, passwordHash, scope, role, request.Phone ?? "");
        return new PatientInfo(id, request.FullName, request.Cpf, request.Email, request.Phone);
    }, ct);

    public Task<List<PatientInfo>> PatientsAsync(DateTimeOffset now, CancellationToken ct) => database.UseAsync(async connection =>
    {
        await using var command = database.Command(connection,
            "SELECT a.id,a.full_name,a.cpf,a.email,a.phone,EXISTS(SELECT 1 FROM nora.sessions s WHERE s.account_id=a.id AND s.revoked_at IS NULL AND s.expires_at>$1 AND s.presence_seen_at>$2) FROM nora.accounts a WHERE a.scope='patient' ORDER BY a.created_at DESC,a.id", now, now - AuthService.PresenceTimeout);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var patients = new List<PatientInfo>();
        while (await reader.ReadAsync(ct)) patients.Add(new(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetString(4), reader.GetBoolean(5)));
        return patients;
    }, ct);

    public Task<int> RehashAsync(Guid id, string hash, CancellationToken ct) => database.UseAsync(connection =>
        database.ExecuteAsync(connection, "UPDATE nora.accounts SET password_hash=$2 WHERE id=$1", ct, id, hash), ct);

    private static Account ReadAccount(NpgsqlDataReader reader) => new(
        new(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3)),
        reader.GetString(4), reader.GetString(5), reader.GetString(6));
}
