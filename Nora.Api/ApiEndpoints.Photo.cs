using Microsoft.Net.Http.Headers;
using Nora.Api.Services;

namespace Nora.Api;

internal static partial class ApiEndpoints
{
    /// <summary>
    /// Foto de perfil de qualquer conta autenticada (paciente, OBS ou Secretaria). Grupo próprio, sem <c>allowPasswordChangePending</c>: com troca de senha pendente
    /// só me/logout/password respondem, então a foto recebe 403 <c>password-change-required</c>. PUT e DELETE limitados por conta; GET sem limite extra.
    /// </summary>
    private static void MapPhotoEndpoints(this WebApplication app)
    {
        var photo = app.MapGroup("/api/auth/photo").AddEndpointFilter(new SessionFilter(null));
        photo.MapGet("", async (HttpContext context, AccountPhotoService service, CancellationToken ct) =>
        {
            var current = await service.GetAsync(Session(context).User.Id, ct);
            var etag = new EntityTagHeaderValue($"\"{current.ETag}\"");
            // Sobrescreve o no-store do middleware: o cliente pode guardar, mas revalida sempre (If-None-Match igual → 304 sem corpo).
            context.Response.Headers.CacheControl = "private, no-cache";
            context.Response.Headers.ETag = etag.ToString();
            if (EntityTagHeaderValue.TryParseList(context.Request.Headers.IfNoneMatch.ToArray(), out var sent)
                && sent.Any(item => item.Equals(EntityTagHeaderValue.Any) || item.Compare(etag, useStrongComparison: false)))
                return Results.StatusCode(StatusCodes.Status304NotModified);
            return Results.Bytes(current.Content, current.ContentType);
        });
        photo.MapPut("", async (HttpContext context, AccountPhotoService service, CancellationToken ct) =>
        {
            await service.SaveAsync(Session(context).User.Id, context.Request.ContentType, context.Request.Body, ct);
            return Results.NoContent();
        }).AddEndpointFilter(new AccountRateLimitFilter(AccountRateLimiter.ProfilePhoto));
        photo.MapDelete("", async (HttpContext context, AccountPhotoService service, CancellationToken ct) =>
        {
            await service.DeleteAsync(Session(context).User.Id, ct);
            return Results.NoContent();
        }).AddEndpointFilter(new AccountRateLimitFilter(AccountRateLimiter.ProfilePhoto));
    }
}
