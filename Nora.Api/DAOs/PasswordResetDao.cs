using System.Security.Cryptography;
using Nora.Api.Database;

namespace Nora.Api.DAOs;

public sealed class PasswordResetDao(DatabaseHelper database)
{
    public async Task<Guid?> TryCreateAsync(Guid challengeId, Guid accountId, string codeHash,
        DateTimeOffset now, DateTimeOffset expiresAt, CancellationToken ct)
    {
        await using var connection = await database.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await using (var account = database.Command(connection,
            "SELECT id FROM nora.accounts WHERE id=$1 AND scope='patient' AND is_active FOR UPDATE", accountId))
            if (await account.ExecuteScalarAsync(ct) is null) { await transaction.CommitAsync(ct); return null; }

        await using (var limits = database.Command(connection,
            "SELECT count(*) FROM nora.patient_password_reset_codes WHERE account_id=$1 AND created_at>$2",
            accountId, now.AddHours(-1)))
        await using (var reader = await limits.ExecuteReaderAsync(ct))
        {
            await reader.ReadAsync(ct);
            if (reader.GetInt64(0) >= 5)
            {
                await reader.DisposeAsync();
                Guid? activeChallenge;
                await using (var existing = database.Command(connection,
                    "SELECT challenge_id FROM nora.patient_password_reset_codes WHERE account_id=$1 AND consumed_at IS NULL AND expires_at>$2 ORDER BY created_at DESC LIMIT 1",
                    accountId, now))
                {
                    var value = await existing.ExecuteScalarAsync(ct);
                    activeChallenge = value is Guid id ? id : null;
                }
                await transaction.CommitAsync(ct);
                return activeChallenge;
            }
        }

        await database.ExecuteAsync(connection,
            "UPDATE nora.patient_password_reset_codes SET consumed_at=$2 WHERE account_id=$1 AND consumed_at IS NULL",
            ct, accountId, now);
        await database.ExecuteAsync(connection,
            "INSERT INTO nora.patient_password_reset_codes(challenge_id,account_id,code_hash,created_at,expires_at) VALUES($1,$2,$3,$4,$5)",
            ct, challengeId, accountId, codeHash, now, expiresAt);
        await transaction.CommitAsync(ct);
        return challengeId;
    }

    public async Task DeleteAsync(Guid challengeId, CancellationToken ct)
    {
        await using var connection = await database.OpenAsync(ct);
        await database.ExecuteAsync(connection,
            "DELETE FROM nora.patient_password_reset_codes WHERE challenge_id=$1", ct, challengeId);
    }

    public async Task<bool> TryCompleteAsync(Guid challengeId, string submittedCodeHash,
        string newPasswordHash, DateTimeOffset now, CancellationToken ct)
    {
        await using var connection = await database.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);

        Guid accountId;
        await using (var owner = database.Command(connection,
            "SELECT account_id FROM nora.patient_password_reset_codes WHERE challenge_id=$1", challengeId))
        {
            var value = await owner.ExecuteScalarAsync(ct);
            if (value is not Guid id) { await transaction.CommitAsync(ct); return false; }
            accountId = id;
        }

        bool accountActive;
        await using (var account = database.Command(connection,
            "SELECT is_active FROM nora.accounts WHERE id=$1 AND scope='patient' FOR UPDATE", accountId))
        {
            var value = await account.ExecuteScalarAsync(ct);
            accountActive = value is true;
        }

        string codeHash;
        DateTimeOffset expiresAt;
        short attempts;
        bool consumed;
        await using (var reset = database.Command(connection,
            "SELECT code_hash,expires_at,attempts,consumed_at IS NOT NULL FROM nora.patient_password_reset_codes WHERE challenge_id=$1 FOR UPDATE",
            challengeId))
        await using (var reader = await reset.ExecuteReaderAsync(ct))
        {
            if (!await reader.ReadAsync(ct)) { await reader.DisposeAsync(); await transaction.CommitAsync(ct); return false; }
            codeHash = reader.GetString(0);
            expiresAt = reader.GetFieldValue<DateTimeOffset>(1);
            attempts = reader.GetInt16(2);
            consumed = reader.GetBoolean(3);
        }

        if (!accountActive || consumed || expiresAt <= now || attempts >= 5)
        { await transaction.CommitAsync(ct); return false; }

        var expected = Convert.FromHexString(codeHash);
        var submitted = Convert.FromHexString(submittedCodeHash);
        if (!CryptographicOperations.FixedTimeEquals(expected, submitted))
        {
            await database.ExecuteAsync(connection,
                "UPDATE nora.patient_password_reset_codes SET attempts=LEAST(attempts+1,5) WHERE challenge_id=$1",
                ct, challengeId);
            await transaction.CommitAsync(ct);
            return false;
        }

        if (await database.ExecuteAsync(connection,
            "UPDATE nora.accounts SET password_hash=$2 WHERE id=$1 AND scope='patient' AND is_active",
            ct, accountId, newPasswordHash) != 1)
        { await transaction.CommitAsync(ct); return false; }

        await database.ExecuteAsync(connection,
            "UPDATE nora.patient_password_reset_codes SET consumed_at=$2 WHERE account_id=$1 AND consumed_at IS NULL",
            ct, accountId, now);
        await database.ExecuteAsync(connection,
            "UPDATE nora.sessions SET revoked_at=$2 WHERE account_id=$1 AND revoked_at IS NULL",
            ct, accountId, now);
        await transaction.CommitAsync(ct);
        return true;
    }
}
