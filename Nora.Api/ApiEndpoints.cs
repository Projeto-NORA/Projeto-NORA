using Nora.Api.Database;
using Nora.Api.Models;
using Nora.Api.Services;

namespace Nora.Api;
internal static class ApiEndpoints
{
    public static void MapNoraEndpoints(this WebApplication app)
    {
        app.MapGet("/health/ready", async (DatabaseHelper database,CancellationToken ct) =>
        {
            await using var c=await database.OpenAsync(ct);
            await using var command=database.Command(c,"SELECT 1 FROM nora.accounts LIMIT 1");
            await command.ExecuteScalarAsync(ct);
            return Results.Ok(new {status="ready",scope="database"});
        });
        app.MapPost("/api/auth/login",async (LoginRequest request,AuthService auth,CancellationToken ct) =>
        {
            var response=await auth.LoginAsync(request,ct);
            return response is null ? Results.Unauthorized() : Results.Ok(response);
        }).RequireRateLimiting("login");
        var auth=app.MapGroup("/api/auth").AddEndpointFilter(new SessionFilter(null));
        auth.MapGet("/me",(HttpContext context)=>Results.Ok(Session(context).User));
        auth.MapPost("/logout",async(HttpContext context,AuthService service,CancellationToken ct)=>
        {
            await service.LogoutAsync(Session(context),ct);
            return Results.NoContent();
        });
        var obs=app.MapGroup("/api").AddEndpointFilter(new SessionFilter("obs"));
        obs.MapGet("/patients",async(AccountService service,CancellationToken ct)=>Results.Ok(await service.PatientsAsync(ct)));
        obs.MapPost("/patients",async(CreatePatientRequest request,AccountService service,CancellationToken ct)=>Results.Json(await service.CreatePatientAsync(request,ct),statusCode:StatusCodes.Status201Created));
        obs.MapGet("/queue",async(WorkflowService service,CancellationToken ct)=>Results.Ok(await service.QueueAsync(ct)));
        obs.MapPost("/queue",async(AddQueueRequest request,WorkflowService service,CancellationToken ct)=>Results.Json(await service.AddQueueAsync(request,ct),statusCode:StatusCodes.Status201Created));
        obs.MapPost("/queue/{id:long}/cancel",async(long id,WorkflowService service,CancellationToken ct)=>
        {
            await service.CancelQueueAsync(id,ct);
            return Results.NoContent();
        });
        obs.MapGet("/slots",async(WorkflowService service,CancellationToken ct)=>Results.Ok(await service.SlotsAsync(ct)));
        obs.MapPost("/slots",async(CreateSlotRequest request,WorkflowService service,CancellationToken ct)=>Results.Json(await service.AddSlotAsync(request,ct),statusCode:StatusCodes.Status201Created));
        var patient=app.MapGroup("/api").AddEndpointFilter(new SessionFilter("patient"));
        patient.MapGet("/patient/overview",async(HttpContext context,WorkflowService service,CancellationToken ct)=>Results.Ok(await service.OverviewAsync(Session(context).User,ct)));
        patient.MapPost("/offers/{id:guid}/respond",async(Guid id,RespondOfferRequest request,HttpContext context,WorkflowService service,CancellationToken ct)=>Results.Ok(await service.RespondAsync(id,Session(context).User.Id,request,ct)));
        app.MapGet("/api/municipal/summary",async(WorkflowService service,CancellationToken ct)=>Results.Ok(await service.SummaryAsync(ct))).AddEndpointFilter(new SessionFilter("municipal"));
    }
    private static SessionInfo Session(HttpContext context)=>(SessionInfo)context.Items[typeof(SessionInfo)]!;
    private sealed class SessionFilter(string? scope):IEndpointFilter
    {
        public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext invocation,EndpointFilterDelegate next)
        {
            var context=invocation.HttpContext;
            var authorization=context.Request.Headers.Authorization.ToString();
            if(!authorization.StartsWith("Bearer ",StringComparison.OrdinalIgnoreCase)) return Results.Unauthorized();
            var session=await context.RequestServices.GetRequiredService<AuthService>().AuthenticateAsync(authorization[7..],context.RequestAborted);
            if(session is null) return Results.Unauthorized();
            if(scope is not null && session.User.Scope!=scope) return Results.StatusCode(403);
            if(scope=="obs" && session.User.Role is not("administrator" or "attendant")) return Results.StatusCode(403);
            context.Items[typeof(SessionInfo)]=session;
            return await next(invocation);
        }
    }
}
