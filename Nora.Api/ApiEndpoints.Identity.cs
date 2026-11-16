using Nora.Api.DAOs;
using Nora.Api.Models;
using Nora.Api.Services;

namespace Nora.Api;

/// <summary>
/// Entrar com Google e push pelo servidor (P-042). Só pacientes. O token do aparelho só trafega no corpo, nunca na URL. Tudo desligado e inofensivo sem configuração:
/// o Google responde 503 <c>google-disabled</c> e o push só aceita e guarda os aparelhos.
/// </summary>
internal static partial class ApiEndpoints
{
    private static void MapIdentityEndpoints(this WebApplication app)
    {
        // Mesmo limitador (e contador) por IP do login com senha; a recusa é o mesmo 401 genérico.
        app.MapPost("/api/auth/google", async (GoogleTokenRequest request, GoogleAccountService google, CancellationToken ct) =>
        {
            var response = await google.LoginAsync(request, ct);
            return response is null ? Results.Unauthorized() : Results.Ok(response);
        }).RequireRateLimiting("login");

        var patient = app.MapGroup("/api/patient").AddEndpointFilter(new SessionFilter("patient"));
        patient.MapGet("/google", async (HttpContext context, GoogleAccountService google, CancellationToken ct) => Results.Ok(await google.StatusAsync(Session(context), ct)));
        patient.MapPost("/google/link", async (GoogleTokenRequest request, HttpContext context, GoogleAccountService google, CancellationToken ct) =>
        {
            await google.LinkAsync(Session(context), request, ct);
            return Results.NoContent();
        }).AddEndpointFilter(new AccountRateLimitFilter(AccountRateLimiter.GoogleLink));
        patient.MapPost("/google/unlink", async (HttpContext context, GoogleAccountService google, CancellationToken ct) =>
        {
            await google.UnlinkAsync(Session(context), ct);
            return Results.NoContent();
        }).AddEndpointFilter(new AccountRateLimitFilter(AccountRateLimiter.GoogleLink));

        patient.MapGet("/devices/push", (PushOptions options) => Results.Ok(new PushStatus(options.Configured)));
        patient.MapPost("/devices", async (PushDeviceRequest request, HttpContext context, PushDao devices, TimeProvider time, CancellationToken ct) =>
        {
            if (!ValidDeviceToken(request.Token) || request.Platform != "android") throw new ApiInputException();
            await devices.RegisterDeviceAsync(Session(context).User.Id, request.Token!, "android", time.GetUtcNow(), ct);
            return Results.NoContent();
        }).AddEndpointFilter(new AccountRateLimitFilter(AccountRateLimiter.PushDevice));
        patient.MapPost("/devices/remove", async (PushDeviceRemoveRequest request, HttpContext context, PushDao devices, CancellationToken ct) =>
        {
            if (!ValidDeviceToken(request.Token)) throw new ApiInputException();
            await devices.RemoveDeviceAsync(Session(context).User.Id, request.Token!, ct);
            return Results.NoContent();
        }).AddEndpointFilter(new AccountRateLimitFilter(AccountRateLimiter.PushDevice));
    }

    /// <summary>Token do FCM: de 1 a 4096 caracteres ASCII visíveis, sem espaço nem controle.</summary>
    private static bool ValidDeviceToken(string? token) => token is { Length: > 0 and <= 4096 } && token.All(c => c > ' ' && c < (char)0x7f);
}
