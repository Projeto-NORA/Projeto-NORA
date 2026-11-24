using System.Security.Cryptography;
using System.Text;
using System.Net.Mail;
using Microsoft.AspNetCore.Identity;
using Nora.Api.DAOs;
using Nora.Api.Models;

namespace Nora.Api.Services;

public sealed class AuthService(AccountDao accounts, SessionDao sessions, IPasswordHasher<Account> passwords, TimeProvider time, ObsDeviceOptions devices)
{
    public static readonly TimeSpan PresenceTimeout = TimeSpan.FromSeconds(90);
    private readonly Account _dummy = new(new(Guid.Empty, "", "", ""), "", "", "");
    private string? _dummyHash;
    public async Task<LoginResponse?> LoginAsync(LoginRequest request, CancellationToken ct, DeviceCredential? device = null)
    {
        var account = await VerifyPasswordAsync(request, ct);
        return account is null ? null : await StartSessionCoreAsync(account.User, ct, device, request.KeepSignedIn == true, account.PasswordHash);
    }

    public async Task<DesktopLoginResult> LoginDesktopAsync(LoginRequest request, CancellationToken ct, DeviceCredential? device)
    {
        if (request.Scope is not ("obs" or "municipal") || request.UnitId == Guid.Empty) throw new ApiInputException();
        var account = await VerifyPasswordAsync(request, ct);
        if (account is null) return new(null);

        string? deviceName = null;
        if (account.User.Scope == "obs" && devices.Enabled)
        {
            if (device is null) throw new ApiCodedException(StatusCodes.Status400BadRequest, "device-credential-required");
            deviceName = ObsDeviceCredentials.ValidName(request.DeviceName ?? "Computador OBS");
        }

        var (token, creation) = await CreateSessionAsync(account.User, device, request.KeepSignedIn == true, ct,
            desktop: true, request.UnitId, deviceName, account.PasswordHash);
        return creation.Outcome switch
        {
            SessionOutcome.AccountInactive => new(null),
            SessionOutcome.DeviceUnitSelectionRequired => new(null, creation.Units),
            SessionOutcome.DeviceRevoked => throw new ApiCodedException(StatusCodes.Status403Forbidden, "device-revoked"),
            SessionOutcome.DeviceUnitMismatch => throw new ApiCodedException(StatusCodes.Status403Forbidden, "device-unit-mismatch"),
            SessionOutcome.DeviceNoAuthorizedUnits => throw new ApiCodedException(StatusCodes.Status403Forbidden, "device-no-authorized-units"),
            SessionOutcome.DeviceRequired => throw new ApiCodedException(StatusCodes.Status403Forbidden, "device-required"),
            SessionOutcome.Created => new(new(token, creation.ExpiresAt, account.User, creation.KeepSignedIn, creation.Device)),
            _ => throw new InvalidOperationException("Resultado inesperado ao abrir sessão.")
        };
    }

    private async Task<Account?> VerifyPasswordAsync(LoginRequest request, CancellationToken ct)
    {
        if (request.Scope is not ("obs" or "municipal" or "patient") || string.IsNullOrWhiteSpace(request.Identifier) || request.Identifier.Length > 254 || request.Password is null || request.Password.Length > 256) throw new ApiInputException();
        var identifier = AccountService.NormalizeIdentifier(request.Identifier);
        if (request.Identifier.Contains('@'))
        {
            if (!MailAddress.TryCreate(identifier, out var email) || email.Address != identifier) throw new ApiInputException();
        }
        else if (!AccountService.ValidCpf(identifier)) throw new ApiInputException();
        var account = await accounts.FindAsync(request.Scope, identifier, ct);
        var result = passwords.VerifyHashedPassword(account ?? _dummy, account?.PasswordHash ?? (_dummyHash ??= passwords.HashPassword(_dummy, Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)))), request.Password);
        if (account is null || result == PasswordVerificationResult.Failed) return null;
        if (result != PasswordVerificationResult.SuccessRehashNeeded) return account;

        var updatedHash = passwords.HashPassword(account, request.Password);
        if (await accounts.RehashAsync(account.User.Id, account.PasswordHash, updatedHash, ct) == 1)
            return account with { PasswordHash = updatedHash };

        // Outro login pode ter atualizado apenas o hash. Uma senha redefinida invalida a tentativa.
        var current = await accounts.FindAsync(request.Scope, identifier, ct);
        return current is not null && current.User.Id == account.User.Id
            && passwords.VerifyHashedPassword(current, current.PasswordHash, request.Password) != PasswordVerificationResult.Failed
            ? current : null;
    }
    /// <summary>
    /// Abre uma sessão de 8 h para a conta já identificada (senha ou Google); nulo se a conta deixou de estar ativa (conferido sob lock). No OBS, o computador do cabeçalho liga a sessão a ele;
    /// "manter conectado" só vale com computador (sessão de <see cref="ObsDeviceOptions.KeepSignedInDays"/> dias, sem renovação); atendente sem computador válido (exigência ligada) → 403 device-required.
    /// </summary>
    public Task<LoginResponse?> StartSessionAsync(UserInfo user, CancellationToken ct, DeviceCredential? device = null, bool keepSignedIn = false) =>
        StartSessionCoreAsync(user, ct, device, keepSignedIn, null);

    private async Task<LoginResponse?> StartSessionCoreAsync(UserInfo user, CancellationToken ct, DeviceCredential? device, bool keepSignedIn, string? verifiedPasswordHash)
    {
        var (token, creation) = await CreateSessionAsync(user, device, keepSignedIn, ct, verifiedPasswordHash: verifiedPasswordHash);
        return creation.Outcome switch
        {
            SessionOutcome.AccountInactive => null,
            SessionOutcome.DeviceRequired => throw new ApiCodedException(StatusCodes.Status403Forbidden, "device-required"),
            _ => new(token, creation.ExpiresAt, user, creation.KeepSignedIn, creation.Device)
        };
    }

    private async Task<(string Token, SessionCreation Creation)> CreateSessionAsync(UserInfo user, DeviceCredential? device,
        bool keepSignedIn, CancellationToken ct, bool desktop = false, Guid? unitId = null, string? deviceName = null,
        string? verifiedPasswordHash = null)
    {
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var now = time.GetUtcNow();
        // Com os computadores desligados, o cabeçalho de um OBS antigo é ignorado: a sessão vale só pelo login.
        var request = new NewSession(user.Id, user.Scope, user.Role, HashToken(token), now.AddHours(desktop ? 24 : 8),
            now.AddDays(desktop ? 30 : devices.KeepSignedInDays), keepSignedIn, devices.Enabled ? device : null, devices.RequireForAttendants, now,
            desktop, unitId, deviceName, verifiedPasswordHash, devices.Enabled);
        var creation = await ObsDeviceService.RetryOnDeadlockAsync(() => sessions.CreateAsync(request, ct));
        return (token, creation);
    }
    /// <summary>
    /// Sessão válida da requisição. Sessão ligada a um computador exige o mesmo cabeçalho de computador ativo; com a exigência ligada, sessão de atendente sem computador (as anteriores à ativação) não vale mais.
    /// </summary>
    public async Task<SessionInfo?> AuthenticateAsync(string token, CancellationToken ct, DeviceCredential? device = null)
    {
        if (token.Length != 64 || !token.All(Uri.IsHexDigit)) return null;
        var session = await sessions.FindAsync(HashToken(token), time.GetUtcNow(), device, ct);
        return session is { User: { Scope: "obs", Role: "attendant" }, DeviceId: null } && devices.RequireForAttendants ? null : session;
    }
    public Task<int> LogoutAsync(SessionInfo session, CancellationToken ct) => sessions.RevokeAsync(session.TokenHash, time.GetUtcNow(), ct);
    public Task<int> SetPresenceAsync(SessionInfo session, bool online, CancellationToken ct) => sessions.SetPresenceAsync(session.TokenHash, time.GetUtcNow(), online, ct);
    /// <summary>
    /// Troca a senha da própria conta. Senha atual incorreta → 400 invalid-current-password (nunca 401, que encerraria a sessão nos clientes);
    /// nova igual à atual → 400. Limpa a troca obrigatória e revoga as outras sessões; a atual continua.
    /// </summary>
    public async Task ChangePasswordAsync(SessionInfo session, ChangePasswordRequest request, CancellationToken ct)
    {
        if (request.CurrentPassword is null || request.CurrentPassword.Length > 256 || request.NewPassword is null || request.NewPassword.Length is < 8 or > 256) throw new ApiInputException();
        var hash = await accounts.FindPasswordHashAsync(session.User.Id, ct) ?? throw new ApiUnauthorizedException();
        var account = new Account(session.User, "", "", hash);
        if (passwords.VerifyHashedPassword(account, hash, request.CurrentPassword) == PasswordVerificationResult.Failed) throw new ApiCodedException(StatusCodes.Status400BadRequest, "invalid-current-password");
        if (string.Equals(request.CurrentPassword, request.NewPassword, StringComparison.Ordinal)) throw new ApiInputException();
        if (!await accounts.ChangePasswordAsync(session.User.Id, session.TokenHash, hash, passwords.HashPassword(account, request.NewPassword), time.GetUtcNow(), ct)) throw new ApiConflictException();
    }
    private static string HashToken(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
