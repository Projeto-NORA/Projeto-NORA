using Npgsql;
using Nora.Api.Database;
using Nora.Api.Models;
using Nora.Api.Services;

namespace Nora.Api.DAOs;

/// <summary>SQL do vínculo entre contas de paciente e contas Google (banco central, migração 018). Um sub por conta e uma conta por sub.</summary>
public sealed class GoogleIdentityDao(DatabaseHelper database)
{
    public Task<(bool Linked, string? Email)> FindAsync(Guid accountId, CancellationToken ct) => database.UseAsync(async connection =>
    {
        await using var command = database.Command(connection, "SELECT email FROM nora.google_identities WHERE account_id=$1", accountId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? (true, reader.IsDBNull(0) ? null : reader.GetString(0)) : (false, (string?)null);
    }, ct);

    /// <summary>Vincula (ou troca) o sub da conta. Sub já vinculado a outra conta → 409 <c>google-already-linked</c> (a unicidade é do banco, sem corrida).</summary>
    public Task LinkAsync(Guid accountId, string subject, string? email, DateTimeOffset now, CancellationToken ct) => database.UseAsync(async connection =>
    {
        try
        {
            return await database.ExecuteAsync(connection,
                "INSERT INTO nora.google_identities(account_id,subject,email,linked_at) VALUES($1,$2,$3,$4) ON CONFLICT (account_id) DO UPDATE SET subject=EXCLUDED.subject,email=EXCLUDED.email," +
                "linked_at=CASE WHEN google_identities.subject=EXCLUDED.subject THEN google_identities.linked_at ELSE EXCLUDED.linked_at END", ct, accountId, subject, (object?)email ?? DBNull.Value, now);
        }
        catch (PostgresException error) when (error.SqlState == PostgresErrorCodes.UniqueViolation) { throw new ApiCodedException(StatusCodes.Status409Conflict, "google-already-linked"); }
    }, ct);

    public Task<int> UnlinkAsync(Guid accountId, CancellationToken ct) => database.UseAsync(connection =>
        database.ExecuteAsync(connection, "DELETE FROM nora.google_identities WHERE account_id=$1", ct, accountId), ct);

    /// <summary>Conta de paciente ativa vinculada ao sub; nula se não há vínculo ou a conta está inativa.</summary>
    public Task<UserInfo?> FindPatientAsync(string subject, CancellationToken ct) => database.UseAsync(async connection =>
    {
        await using var command = database.Command(connection,
            "SELECT a.id,a.full_name,a.role,a.scope,a.must_change_password,a.cpf,a.email FROM nora.google_identities g JOIN nora.accounts a ON a.id=g.account_id WHERE g.subject=$1 AND a.scope='patient' AND a.is_active", subject);
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? new UserInfo(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetBoolean(4)).WithContact(reader.IsDBNull(5) ? null : reader.GetString(5), reader.IsDBNull(6) ? null : reader.GetString(6)) : null;
    }, ct);
}
