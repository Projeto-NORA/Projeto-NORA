using Nora.Api.Models;
using Nora.Api.Services;

namespace Nora.Api;

internal static partial class ApiEndpoints
{
    private static void MapRetentionEndpoints(this WebApplication app)
    {
        var municipal = app.MapGroup("/api/municipal/retention").AddEndpointFilter(new SessionFilter("municipal"));
        municipal.MapGet("", async (HttpContext context, RetentionService service, CancellationToken ct) =>
            Results.Ok(await service.ReadAsync(Session(context), null, ct)));
        municipal.MapPut("", async (RetentionChange change, HttpContext context, RetentionService service, CancellationToken ct) =>
            Results.Ok(await service.ChangePolicyAsync(Session(context), null, change, ct)));
        municipal.MapPut("/{id:guid}", async (Guid id, RetentionChange change, HttpContext context, RetentionService service, CancellationToken ct) =>
        {
            await service.ChangeScheduleAsync(Session(context), null, id, change, ct);
            return Results.NoContent();
        });

        var obs = app.MapGroup("/api/obs/units/{unitId:guid}/retention").AddEndpointFilter(new SessionFilter("obs"));
        obs.MapGet("", async (Guid unitId, HttpContext context, RetentionService service, CancellationToken ct) =>
            Results.Ok(await service.ReadAsync(Session(context), unitId, ct)));
        obs.MapPut("", async (Guid unitId, RetentionChange change, HttpContext context, RetentionService service, CancellationToken ct) =>
            Results.Ok(await service.ChangePolicyAsync(Session(context), unitId, change, ct)));
        obs.MapPut("/{id:guid}", async (Guid unitId, Guid id, RetentionChange change, HttpContext context, RetentionService service, CancellationToken ct) =>
        {
            await service.ChangeScheduleAsync(Session(context), unitId, id, change, ct);
            return Results.NoContent();
        });
    }
}
