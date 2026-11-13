using System.Security.Cryptography;
using System.Text;
using System.Net.Mail;
using Microsoft.AspNetCore.Identity;
using Nora.Api.DAOs;
using Nora.Api.Models;

namespace Nora.Api.Services;

public sealed class AuthService(AccountDao accounts, SessionDao sessions, IPasswordHasher<Account> passwords, TimeProvider time)
{
    public static readonly TimeSpan PresenceTimeout = TimeSpan.FromSeconds(90);
    private readonly Account _dummy = new(new(Guid.Empty, "", "", ""), "", "", "");
    private string? _dummyHash;
    public async Task<LoginResponse?> LoginAsync(LoginRequest request, CancellationToken ct)
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
        if (result == PasswordVerificationResult.SuccessRehashNeeded) await accounts.RehashAsync(account.User.Id, account.PasswordHash, passwords.HashPassword(account, request.Password), ct);
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var expiresAt = time.GetUtcNow().AddHours(8);
        if (await sessions.CreateAsync(account.User.Id, HashToken(token), expiresAt, ct) == 0) return null;
        return new(token, expiresAt, account.User);
    }
    public Task<SessionInfo?> AuthenticateAsync(string token, CancellationToken ct) => token.Length == 64 && token.All(Uri.IsHexDigit)
        ? sessions.FindAsync(HashToken(token), time.GetUtcNow(), ct) : Task.FromResult<SessionInfo?>(null);
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
