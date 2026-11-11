using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Nora.Api.DAOs;
using Nora.Api.Models;

namespace Nora.Api.Services;

public sealed class PasswordResetService(AccountDao accounts, PasswordResetDao resets,
    IPasswordHasher<Account> passwords, IEmailSender email, IConfiguration configuration,
    TimeProvider time, ILogger<PasswordResetService> logger)
{
    private static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(10);
    private static readonly Account HashUser = new(new(Guid.Empty, "", "patient", "patient"), "", "", "");

    public async Task<PasswordResetChallengeResponse> StartAsync(PasswordResetStartRequest request, CancellationToken ct)
    {
        var cpf = AccountService.NormalizeCpf(request.Cpf);
        if (!AccountService.ValidCpf(cpf)) throw new ApiInputException();
        var key = SigningKey();
        if (!email.IsConfigured) throw new EmailDeliveryUnavailableException();

        var challengeId = Guid.NewGuid();
        var now = time.GetUtcNow();
        var expiresAt = now.Add(CodeLifetime);
        var target = await accounts.FindPatientResetTargetAsync(cpf, ct);
        if (target is null) return new(challengeId, (int)CodeLifetime.TotalSeconds);

        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
        var codeHash = HashCode(key, challengeId, code);
        var usableChallenge = await resets.TryCreateAsync(challengeId, target.Id, codeHash, now, expiresAt, ct);
        if (usableChallenge is null || usableChallenge != challengeId)
            return new(usableChallenge ?? challengeId, (int)CodeLifetime.TotalSeconds);

        try
        {
            await email.SendAsync(target.Email, "Código para redefinir sua senha NORA",
                $"Olá, {target.FullName}.\n\nSeu código de verificação é: {code}\n\nEle vale por 10 minutos e pode ser usado uma única vez. Se você não pediu a alteração, ignore esta mensagem.", ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            await resets.DeleteAsync(challengeId, CancellationToken.None);
            throw;
        }
        catch (Exception)
        {
            await resets.DeleteAsync(challengeId, CancellationToken.None);
            logger.LogWarning("Não foi possível entregar um código de recuperação por e-mail.");
        }

        // Identical response for unknown, throttled and known CPF values avoids account discovery.
        return new(challengeId, (int)CodeLifetime.TotalSeconds);
    }

    public async Task<PasswordResetFinishResponse> FinishAsync(PasswordResetFinishRequest request, CancellationToken ct)
    {
        if (request.ChallengeId == Guid.Empty || request.NewPassword is null || request.NewPassword.Length is < 8 or > 256)
            throw new ApiInputException();
        if (request.Code is null || request.Code.Length != 6 || request.Code.Any(c => !char.IsAsciiDigit(c)))
            return new(false);

        var key = SigningKey();
        var codeHash = HashCode(key, request.ChallengeId, request.Code);
        var newHash = passwords.HashPassword(HashUser, request.NewPassword);
        var completed = await resets.TryCompleteAsync(request.ChallengeId, codeHash, newHash, time.GetUtcNow(), ct);
        return new(completed);
    }

    private byte[] SigningKey()
    {
        var value = configuration["Nora:PasswordReset:HashKey"];
        if (string.IsNullOrWhiteSpace(value)) throw new EmailDeliveryUnavailableException();
        var key = Encoding.UTF8.GetBytes(value);
        if (key.Length < 32) throw new EmailDeliveryUnavailableException();
        return key;
    }

    private static string HashCode(byte[] key, Guid challengeId, string code) =>
        Convert.ToHexString(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes($"{challengeId:N}:{code}")));
}
