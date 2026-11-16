using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Nora.Api.DAOs;
using Nora.Api.Database;
using Nora.Api.Models;

namespace Nora.Api.Services;

/// <summary>
/// Troca do e-mail da conta guiada pela correção do e-mail de referência da UBS: o app lê a sugestão pendente, pede um código ao endereço novo e só troca depois de confirmá-lo.
/// Mesmo padrão do cadastro e da recuperação de senha: código de seis dígitos guardado só como HMAC, uso único, 10 minutos, 5 tentativas, no máximo 3 envios por sugestão.
/// Sem SMTP ou sem chave de assinatura, 503.
/// </summary>
public sealed class EmailChangeService(DatabaseHelper central, EmailChangeDao suggestions, IEmailSender email, IConfiguration configuration, TimeProvider time)
{
    private static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(10);
    public const int MaxSends = 3;
    public const int MaxAttempts = 5;

    /// <summary>Sugestão pendente da conta (e-mail mascarado e UBS) ou nulo. Só conta de paciente.</summary>
    public async Task<EmailSuggestion?> ReadAsync(SessionInfo session, CancellationToken ct)
    {
        RequirePatient(session);
        return await suggestions.ReadAsync(session.User.Id, ct) is { } found ? new(found.Id, PatientRegistrationService.Mask(found.Email), found.UnitId, found.UnitName, found.CreatedAt) : null;
    }

    /// <summary>Gera o código, guarda o HMAC e o envia ao endereço sugerido. Sem sugestão pendente → 404; terceiro envio já feito → 429.</summary>
    public async Task SendCodeAsync(SessionInfo session, CancellationToken ct)
    {
        RequirePatient(session);
        var key = SigningKey();
        if (!email.IsConfigured) throw new EmailDeliveryUnavailableException();
        var now = time.GetUtcNow();
        string destination, code, codeHash; Guid id;
        await using (var connection = await central.OpenAsync(ct))
        await using (var transaction = await connection.BeginTransactionAsync(ct))
        {
            if (!await suggestions.LockAccountAsync(connection, session.User.Id, session.TokenHash, now, ct)) throw new ApiUnauthorizedException();
            var pending = await suggestions.LockPendingAsync(connection, session.User.Id, ct) ?? throw new ApiNotFoundException();
            if (pending.Sends >= MaxSends) throw new ApiRateLimitException();
            id = pending.Id; destination = pending.SuggestedEmail;
            code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", CultureInfo.InvariantCulture);
            codeHash = Hash(key, id, code);
            await suggestions.StoreCodeAsync(connection, id, codeHash, now + CodeLifetime, ct);
            await transaction.CommitAsync(ct);
        }
        try
        {
            await email.SendAsync(destination, "Código para trocar o e-mail da sua conta NORA", $"Seu código de confirmação é: {code}\n\nEle vale por 10 minutos e pode ser usado uma única vez. Se você não pediu esta troca, ignore esta mensagem.", ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { await suggestions.InvalidateCodeAsync(id, codeHash); throw; }
        catch (Exception) { await suggestions.InvalidateCodeAsync(id, codeHash); throw new EmailDeliveryUnavailableException(); }
    }

    /// <summary>
    /// Confere o código e troca o e-mail da conta. <c>codeValid=false</c> cobre código errado, vencido, já usado, sem envio ou com as cinco tentativas gastas. E-mail já usado por outra conta
    /// de paciente → 409 (a unicidade é do banco); nada é trocado e a sugestão continua pendente. Sem sugestão pendente → 404.
    /// </summary>
    public async Task<EmailSuggestionConfirmResponse> ConfirmAsync(SessionInfo session, EmailSuggestionConfirmRequest request, CancellationToken ct)
    {
        RequirePatient(session);
        if (request.Code is null || request.Code.Length != 6 || request.Code.Any(c => !char.IsAsciiDigit(c))) return new(false);
        var key = SigningKey(); var now = time.GetUtcNow();
        await using var connection = await central.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        if (!await suggestions.LockAccountAsync(connection, session.User.Id, session.TokenHash, now, ct)) throw new ApiUnauthorizedException();
        var pending = await suggestions.LockPendingAsync(connection, session.User.Id, ct) ?? throw new ApiNotFoundException();
        if (pending.CodeHash is null || pending.CodeExpiresAt is null || pending.CodeExpiresAt <= now || pending.Attempts >= MaxAttempts) { await transaction.CommitAsync(ct); return new(false); }
        if (!EqualHash(pending.CodeHash, Hash(key, pending.Id, request.Code)))
        {
            await suggestions.CountAttemptAsync(connection, pending.Id, ct);
            await transaction.CommitAsync(ct);
            return new(false);
        }
        // Código certo: a unicidade de e-mail do banco decide; a violação (23505) vira 409 e desfaz tudo, inclusive o uso do código.
        if (await suggestions.ChangeAccountEmailAsync(connection, session.User.Id, pending.SuggestedEmail, ct) != 1) throw new ApiUnauthorizedException();
        await suggestions.CloseAsync(connection, pending.Id, "confirmed", now, ct);
        await suggestions.ConsumeResetCodesAsync(connection, session.User.Id, now, ct);
        await transaction.CommitAsync(ct);
        return new(true);
    }

    /// <summary>Dispensa a sugestão pendente (o e-mail da conta não muda). Sem sugestão pendente → 404.</summary>
    public async Task DismissAsync(SessionInfo session, CancellationToken ct)
    {
        RequirePatient(session);
        await using var connection = await central.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        var pending = await suggestions.LockPendingAsync(connection, session.User.Id, ct) ?? throw new ApiNotFoundException();
        await suggestions.CloseAsync(connection, pending.Id, "dismissed", time.GetUtcNow(), ct);
        await transaction.CommitAsync(ct);
    }

    private static void RequirePatient(SessionInfo session)
    {
        if (session.User.Scope != "patient" || session.User.Role != "patient") throw new ApiForbiddenException();
    }

    /// <summary>Chave do HMAC do código: <c>Nora:EmailChange:HashKey</c> ou, na falta dela, a do cadastro ou a da recuperação de senha (a primeira com ao menos 32 bytes). Nenhuma → 503.</summary>
    private byte[] SigningKey()
    {
        foreach (var name in new[] { "Nora:EmailChange:HashKey", "Nora:Registration:HashKey", "Nora:PasswordReset:HashKey" })
        {
            var value = configuration[name];
            if (!string.IsNullOrWhiteSpace(value) && Encoding.UTF8.GetByteCount(value) >= 32) return Encoding.UTF8.GetBytes(value);
        }
        throw new EmailDeliveryUnavailableException();
    }

    private static string Hash(byte[] key, Guid suggestionId, string code) => Convert.ToHexString(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes($"email-change:{suggestionId:N}:{code}")));

    private static bool EqualHash(string first, string second) => first.Length == 64 && second.Length == 64 && first.All(Uri.IsHexDigit) && second.All(Uri.IsHexDigit) &&
        CryptographicOperations.FixedTimeEquals(Convert.FromHexString(first), Convert.FromHexString(second));
}
