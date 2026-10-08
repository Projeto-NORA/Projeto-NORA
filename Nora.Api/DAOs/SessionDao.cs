using Nora.Api.Database;
using Nora.Api.Models;

namespace Nora.Api.DAOs;

public sealed class SessionDao(DatabaseHelper database)
{
    public Task<int> CreateAsync(Guid id, string tokenHash, DateTimeOffset expiresAt, CancellationToken ct) => database.UseAsync(connection =>
        database.ExecuteAsync(connection, "INSERT INTO nora.sessions(token_hash,account_id,expires_at) VALUES($1,$2,$3)", ct, tokenHash, id, expiresAt), ct);

    public Task<SessionInfo?> FindAsync(string hash, DateTimeOffset now, CancellationToken ct) => database.UseAsync(async connection =>
    {
        await using var command = database.Command(connection,
            "SELECT a.id,a.full_name,a.role,a.scope FROM nora.sessions s JOIN nora.accounts a ON a.id=s.account_id WHERE s.token_hash=$1 AND s.revoked_at IS NULL AND s.expires_at>$2", hash, now);
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? new SessionInfo(new(reader.GetGuid(0),reader.GetString(1),reader.GetString(2),reader.GetString(3)), hash) : null;
    }, ct);

    public Task<int> RevokeAsync(string hash, DateTimeOffset now, CancellationToken ct) => database.UseAsync(connection =>
        database.ExecuteAsync(connection, "UPDATE nora.sessions SET revoked_at=$2 WHERE token_hash=$1 AND revoked_at IS NULL", ct, hash, now), ct);
}
