using Npgsql;
using Nora.Api.Database;
using Nora.Api.Models;

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
            "INSERT INTO nora.accounts(id,full_name,cpf,email,password_hash,scope,role) VALUES($1,$2,$3,$4,$5,$6,$7)", ct,
            id, request.FullName, request.Cpf, request.Email, passwordHash, scope, role);
        return new PatientInfo(id, request.FullName, request.Cpf, request.Email);
    }, ct);

    public Task<List<PatientInfo>> PatientsAsync(CancellationToken ct) => database.UseAsync(async connection =>
    {
        await using var command = database.Command(connection,
            "SELECT id,full_name,cpf,email FROM nora.accounts WHERE scope='patient' ORDER BY full_name,id");
        await using var reader = await command.ExecuteReaderAsync(ct);
        var patients = new List<PatientInfo>();
        while (await reader.ReadAsync(ct)) patients.Add(new(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3)));
        return patients;
    }, ct);

    public Task<int> RehashAsync(Guid id, string hash, CancellationToken ct) => database.UseAsync(connection =>
        database.ExecuteAsync(connection, "UPDATE nora.accounts SET password_hash=$2 WHERE id=$1", ct, id, hash), ct);

    private static Account ReadAccount(NpgsqlDataReader reader) => new(
        new(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3)),
        reader.GetString(4), reader.GetString(5), reader.GetString(6));
}
