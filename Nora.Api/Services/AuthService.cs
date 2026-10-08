using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Nora.Api.DAOs;
using Nora.Api.Models;

namespace Nora.Api.Services;

public sealed class AuthService(AccountDao accounts, SessionDao sessions, IPasswordHasher<Account> passwords, TimeProvider time)
{
    private readonly Account _dummy = new(new(Guid.Empty, "", "", ""), "", "", "");
    private string? _dummyHash;
    public async Task<LoginResponse?> LoginAsync(LoginRequest request, CancellationToken ct)
    {
        if (request.Scope is not ("obs" or "municipal" or "patient") || string.IsNullOrWhiteSpace(request.Identifier) || request.Identifier.Length > 254 || request.Password is null || request.Password.Length > 256) throw new ApiInputException();
        var account = await accounts.FindAsync(request.Scope, AccountService.NormalizeIdentifier(request.Identifier), ct);
        var result = passwords.VerifyHashedPassword(account ?? _dummy, account?.PasswordHash ?? (_dummyHash ??= passwords.HashPassword(_dummy, Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)))), request.Password);
        if (account is null || result == PasswordVerificationResult.Failed) return null;
        if (result == PasswordVerificationResult.SuccessRehashNeeded) await accounts.RehashAsync(account.User.Id, passwords.HashPassword(account, request.Password), ct);
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var expiresAt = time.GetUtcNow().AddHours(8);
        await sessions.CreateAsync(account.User.Id, HashToken(token), expiresAt, ct);
        return new(token, expiresAt, account.User);
    }
    public Task<SessionInfo?> AuthenticateAsync(string token, CancellationToken ct) => token.Length == 64 && token.All(Uri.IsHexDigit)
        ? sessions.FindAsync(HashToken(token), time.GetUtcNow(), ct) : Task.FromResult<SessionInfo?>(null);
    public Task<int> LogoutAsync(SessionInfo session, CancellationToken ct) => sessions.RevokeAsync(session.TokenHash, time.GetUtcNow(), ct);
    private static string HashToken(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
