using Nora.Api.Database;
using Nora.Api.Models;
using Nora.Api.Services;

namespace Nora.Api.DAOs;

public sealed class StaffProfileDao(DatabaseHelper database, TimeProvider clock)
{
    public async Task<StaffSelfProfile> ReadAsync(SessionInfo session, string? updatedName, CancellationToken ct)
    {
        await using var connection = await database.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        StaffSelfProfile profile;
        Guid? deviceUnit;
        // Revalida e bloqueia a conta e a sessão após o filtro de autenticação. A edição já segura a conta
        // para escrita, evitando que duas edições tentem promover ao mesmo tempo seus bloqueios de leitura.
        var rowLocks = updatedName is null ? " FOR SHARE OF a,s" : " FOR NO KEY UPDATE OF a FOR SHARE OF s";
        await using (var query = database.Command(connection,
            "SELECT a.full_name,a.email,a.role,a.scope,a.municipality_name,a.must_change_password,d.unit_id " +
            "FROM nora.accounts a JOIN nora.sessions s ON s.account_id=a.id LEFT JOIN nora.obs_devices d ON d.id=s.device_id " +
            "WHERE a.id=$1 AND a.scope IN ('obs','municipal') AND a.is_active AND s.token_hash=$2 " +
            "AND s.revoked_at IS NULL AND s.expires_at>$3 AND (s.device_id IS NULL OR d.status='active')" + rowLocks,
            session.User.Id, session.TokenHash, clock.GetUtcNow()))
        await using (var reader = await query.ExecuteReaderAsync(ct))
        {
            if (!await reader.ReadAsync(ct)) throw new ApiUnauthorizedException();
            if (reader.GetBoolean(5)) throw new ApiCodedException(StatusCodes.Status403Forbidden, "password-change-required");
            profile = new(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4), []);
            deviceUnit = reader.IsDBNull(6) ? null : reader.GetGuid(6);
        }

        if (updatedName is not null && profile.FullName != updatedName)
        {
            await database.ExecuteAsync(connection, "UPDATE nora.accounts SET full_name=$2 WHERE id=$1", ct, session.User.Id, updatedName);
            profile = profile with { FullName = updatedName };
        }
        if (profile.Scope == "obs")
        {
            var allowedUnit = profile.Role == "attendant" ? deviceUnit : null;
            var units = await ObsDeviceDao.ListAuthorizedUnitsAsync(database, connection, session.User.Id, allowedUnit, lockRows: false, ct);
            profile = profile with { Units = units };
        }
        await transaction.CommitAsync(ct);
        return profile;
    }
}
