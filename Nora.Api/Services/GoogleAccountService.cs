using Nora.Api.DAOs;
using Nora.Api.Models;

namespace Nora.Api.Services;

/// <summary>
/// Entrar com Google (P-042, decisão do usuário de 2026-11-16): só pacientes e só pelo app. A conta Google é vinculada com a sessão já aberta (CPF e senha), pode ser
/// desvinculada, e o Google sozinho nunca cria conta. Desligado sem <c>Nora:Google:ClientIds</c>: vínculo e login respondem 503 <c>google-disabled</c>.
/// </summary>
public sealed class GoogleAccountService(GoogleSignInOptions options, GoogleIdTokenValidator validator, GoogleIdentityDao identities, AuthService auth, TimeProvider time)
{
    public async Task<GoogleLinkStatus> StatusAsync(SessionInfo session, CancellationToken ct)
    {
        var (linked, email) = await identities.FindAsync(session.User.Id, ct);
        return new(options.Enabled, linked, email);
    }

    /// <summary>Token inválido, vencido, de outro cliente ou com e-mail não verificado → 400; sub de outra conta → 409.</summary>
    public async Task LinkAsync(SessionInfo session, GoogleTokenRequest request, CancellationToken ct)
    {
        RequireEnabled();
        if (string.IsNullOrWhiteSpace(request.IdToken)) throw new ApiInputException();
        var identity = await validator.ValidateAsync(request.IdToken, ct) ?? throw new ApiInputException();
        await identities.LinkAsync(session.User.Id, identity.Subject, identity.Email, time.GetUtcNow(), ct);
    }

    public Task UnlinkAsync(SessionInfo session, CancellationToken ct) => identities.UnlinkAsync(session.User.Id, ct);

    /// <summary>
    /// Mesma resposta do login com senha (scope <c>patient</c>). Nulo (o endpoint devolve o 401 genérico do login) quando o token é inválido, o sub não está vinculado
    /// ou a conta está inativa, sem dizer qual. <c>deviceName</c> é aceito e não é guardado. <c>keepSignedIn</c> segue a regra do login com senha: sem ele a sessão vale 24 h, com ele
    /// <see cref="ObsDeviceOptions.KeepSignedInDays"/> dias (30 por padrão), e a resposta informa o que foi aplicado.
    /// </summary>
    public async Task<LoginResponse?> LoginAsync(GoogleTokenRequest request, CancellationToken ct)
    {
        RequireEnabled();
        if (string.IsNullOrWhiteSpace(request.IdToken)) throw new ApiInputException();
        var identity = await validator.ValidateAsync(request.IdToken, ct);
        if (identity is null || await identities.FindPatientAsync(identity.Subject, ct) is not { } user) return null;
        return await auth.StartSessionAsync(user, ct, keepSignedIn: request.KeepSignedIn == true);
    }

    private void RequireEnabled() { if (!options.Enabled) throw new ApiCodedException(StatusCodes.Status503ServiceUnavailable, "google-disabled"); }
}
