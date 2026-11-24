using Nora.Api.Database;
using Nora.Api.Models;
using Nora.Api.Services;
using Npgsql;

namespace Nora.Api.DAOs;

public enum SessionOutcome
{
    Created, AccountInactive, DeviceRequired, DeviceUnitSelectionRequired, DeviceRevoked, DeviceUnitMismatch, DeviceNoAuthorizedUnits
}

/// <param name="Device">Computador reconhecido (cabeçalho válido) ou nulo; só vale no escopo <c>obs</c>.</param>
/// <param name="RequireDevice">Atendente só entra com computador ativado (e com concessão na UBS dele).</param>
/// <param name="DevicesEnabled">Recurso dos computadores ligado (<c>Nora:ObsDevices:Enabled</c>). Desligado, o login de desktop do OBS não exige computador e pode ser mantido como o municipal.</param>
public sealed record NewSession(Guid AccountId, string Scope, string Role, string TokenHash, DateTimeOffset DefaultExpiry,
    DateTimeOffset KeepSignedInExpiry, bool KeepSignedIn, DeviceCredential? Device, bool RequireDevice, DateTimeOffset Now,
    bool Desktop = false, Guid? UnitId = null, string? DeviceName = null, string? VerifiedPasswordHash = null, bool DevicesEnabled = true);
public sealed record SessionCreation(SessionOutcome Outcome, DateTimeOffset ExpiresAt, bool KeepSignedIn, ObsDeviceRef? Device,
    IReadOnlyList<LoginUnitOption>? Units = null);

public sealed class SessionDao(DatabaseHelper database, ObsDeviceDao devices)
{
    /// <summary>
    /// Abre a sessão numa transação: conta <c>FOR UPDATE</c> (como sempre) e, no OBS, o computador <c>FOR NO KEY UPDATE</c> se estiver ativo com aquele segredo (o mesmo lock do <c>UPDATE</c> de <c>last_login_at</c>, sem promoção de lock compartilhado, então dois logins no mesmo computador
    /// se enfileiram em vez de entrar em impasse), de modo que a revogação (que o bloqueia para escrita) espera o login e o login nunca deixa sessão ativa ligada a computador revogado. Computador que sumiu no meio do caminho vale como "sem computador". Administrador nunca fica trancado;
    /// atendente com exigência ligada sem computador válido (ou de UBS em que não tem concessão) → <see cref="SessionOutcome.DeviceRequired"/>. "Manter conectado" só vale com computador.
    /// </summary>
    public Task<SessionCreation> CreateAsync(NewSession request, CancellationToken ct) => database.UseAsync(async connection =>
    {
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await using (var check = database.Command(connection,
            "SELECT is_active AND ($2::text IS NULL OR password_hash=$2) FROM nora.accounts WHERE id=$1 FOR UPDATE",
            request.AccountId, (object?)request.VerifiedPasswordHash ?? DBNull.Value))
            if (await check.ExecuteScalarAsync(ct) is not true) return new SessionCreation(SessionOutcome.AccountInactive, request.DefaultExpiry, false, null);
        ObsDeviceRef? device = null;
        if (request.Scope == "obs" && request.Device is { } credential)
        {
            if (request.Desktop)
            {
                var enrollment = await EnrollDesktopAsync(connection, request, credential, ct);
                if (enrollment.Outcome != SessionOutcome.Created) return enrollment;
                device = enrollment.Device;
            }
            else device = await FindLegacyDeviceAsync(connection, request, credential, ct);
        }
        if (request.Scope == "obs" && device is null && ((request.Desktop && request.DevicesEnabled) || (request.Role == "attendant" && request.RequireDevice)))
            return new SessionCreation(SessionOutcome.DeviceRequired, request.DefaultExpiry, false, null);
        var keep = request.KeepSignedIn && (device is not null || (request.Desktop && (request.Scope == "municipal" || !request.DevicesEnabled)));
        var expires = keep ? request.KeepSignedInExpiry : request.DefaultExpiry;
        await using (var insert = database.Command(connection, "INSERT INTO nora.sessions(token_hash,account_id,expires_at,device_id) VALUES($1,$2,$3,$4::uuid)", request.TokenHash, request.AccountId, expires, (object?)device?.Id ?? DBNull.Value))
            await insert.ExecuteNonQueryAsync(ct);
        if (device is not null)
            await database.ExecuteAsync(connection, "UPDATE nora.obs_devices SET last_login_at=$2 WHERE id=$1", ct, device.Id, request.Now);
        await transaction.CommitAsync(ct);
        return new SessionCreation(SessionOutcome.Created, expires, keep, device);
    }, ct);

    private async Task<ObsDeviceRef?> FindLegacyDeviceAsync(NpgsqlConnection connection, NewSession request, DeviceCredential credential, CancellationToken ct)
    {
        var device = await ObsDeviceDao.FindActiveAsync(database, connection, credential, lockRow: true, ct);
        if (device is null || request.Role != "attendant") return device;

        await using var grant = database.Command(connection, "SELECT 1 FROM nora.staff_units WHERE account_id=$1 AND unit_id=$2", request.AccountId, device.UnitId);
        return await grant.ExecuteScalarAsync(ct) is null ? null : device;
    }

    private async Task<SessionCreation> EnrollDesktopAsync(NpgsqlConnection connection, NewSession request, DeviceCredential credential, CancellationToken ct)
    {
        var units = await ObsDeviceDao.ListAuthorizedUnitsAsync(database, connection, request.AccountId, unitId: null, lockRows: true, ct);
        var existing = await devices.FindEnrollmentAsync(connection, credential, ct);
        if (existing is not null) return CheckEnrollment(existing, units, request);
        if (units.Count == 0) return new(SessionOutcome.DeviceNoAuthorizedUnits, request.DefaultExpiry, false, null);
        if (request.UnitId is null && units.Count > 1) return new(SessionOutcome.DeviceUnitSelectionRequired, request.DefaultExpiry, false, null, units);

        var unit = request.UnitId is null ? units[0] : units.Find(item => item.Id == request.UnitId);
        if (unit is null) return new(SessionOutcome.DeviceUnitMismatch, request.DefaultExpiry, false, null);

        // Outro login com a mesma instalação pode registrar o id entre a leitura e a inserção. Só reutilize se o segredo
        // e a unidade conferirem (e a concessão ainda estiver protegida pela transação).
        var name = request.DeviceName ?? "Computador OBS";
        if (!await devices.InsertAsync(connection, credential.Id, unit.Id, name, "active", ObsDeviceCredentials.HashSecret(credential.Secret),
            codeHash: null, codeExpiresAt: null, request.AccountId, request.Now, ct))
        {
            existing = await devices.FindEnrollmentAsync(connection, credential, ct);
            return existing is null
                ? new(SessionOutcome.DeviceRevoked, request.DefaultExpiry, false, null)
                : CheckEnrollment(existing, units, request with { UnitId = unit.Id });
        }

        await devices.AddEventAsync(connection, credential.Id, unit.Id, request.AccountId, "created", request.Now, ct);
        await devices.AddEventAsync(connection, credential.Id, unit.Id, request.AccountId, "activated", request.Now, ct);
        return new(SessionOutcome.Created, request.DefaultExpiry, false, new(credential.Id, name, unit.Id, unit.Name));
    }

    private static SessionCreation CheckEnrollment(ObsDeviceEnrollment enrollment, List<LoginUnitOption> units, NewSession request)
    {
        if (!enrollment.CanSignIn) return new(SessionOutcome.DeviceRevoked, request.DefaultExpiry, false, null);
        if (units.Count == 0) return new(SessionOutcome.DeviceNoAuthorizedUnits, request.DefaultExpiry, false, null);
        if ((request.UnitId is { } requested && requested != enrollment.Device.UnitId) ||
            !units.Exists(unit => unit.Id == enrollment.Device.UnitId))
            return new(SessionOutcome.DeviceUnitMismatch, request.DefaultExpiry, false, null);
        return new(SessionOutcome.Created, request.DefaultExpiry, false, enrollment.Device);
    }

    /// <summary>
    /// Sessão válida. Sessão ligada a um computador só vale com o computador ativo e o mesmo cabeçalho (id e segredo) na requisição: numa consulta só, sem custo extra.
    /// </summary>
    public Task<SessionInfo?> FindAsync(string hash, DateTimeOffset now, DeviceCredential? device, CancellationToken ct) => database.UseAsync(async connection =>
    {
        await using var command = database.Command(connection,
            "SELECT a.id,a.full_name,a.role,a.scope,a.must_change_password,s.device_id,d.unit_id FROM nora.sessions s JOIN nora.accounts a ON a.id=s.account_id " +
            "LEFT JOIN nora.obs_devices d ON d.id=s.device_id " +
            "WHERE s.token_hash=$1 AND a.is_active AND s.revoked_at IS NULL AND s.expires_at>$2 " +
            "AND (s.device_id IS NULL OR (d.status='active' AND d.id=$3::uuid AND d.secret_hash=$4::text))", hash, now,
            (object?)device?.Id ?? DBNull.Value, (object?)(device is null ? null : ObsDeviceCredentials.HashSecret(device.Secret)) ?? DBNull.Value);
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct)
            ? new SessionInfo(new(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetBoolean(4)), hash,
                reader.IsDBNull(5) ? null : reader.GetGuid(5), reader.IsDBNull(6) ? null : reader.GetGuid(6))
            : null;
    }, ct);

    public Task<int> RevokeAsync(string hash, DateTimeOffset now, CancellationToken ct) => database.UseAsync(connection =>
        database.ExecuteAsync(connection, "UPDATE nora.sessions SET revoked_at=$2 WHERE token_hash=$1 AND revoked_at IS NULL", ct, hash, now), ct);

    public Task<int> SetPresenceAsync(string hash, DateTimeOffset now, bool online, CancellationToken ct) => database.UseAsync(connection =>
        database.ExecuteAsync(connection, "UPDATE nora.sessions s SET presence_seen_at=CASE WHEN $3 THEN $2 ELSE NULL END FROM nora.accounts a WHERE s.token_hash=$1 AND a.id=s.account_id AND a.is_active AND s.revoked_at IS NULL AND s.expires_at>$2", ct, hash, now, online), ct);
}
