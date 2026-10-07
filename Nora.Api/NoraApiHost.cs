namespace Nora.Api;

public static class NoraApiHost
{
    public static WebApplication Create(WebApplicationOptions options)
    {
        var builder = WebApplication.CreateBuilder(options);
        builder.Services.AddProblemDetails();
        builder.WebHost.ConfigureKestrel(server => server.AddServerHeader = false);

        var app = builder.Build();
        // O mesmo contrato seguro é usado em Development e Production.
        app.UseExceptionHandler(errors => errors.Run(context => ApiProblems.WriteAsync(context, 500)));
        app.UseStatusCodePages(context => ApiProblems.WriteAsync(context.HttpContext, context.HttpContext.Response.StatusCode));

        app.MapMethods("/health/live", [HttpMethods.Get, HttpMethods.Head], (HttpContext context) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            return TypedResults.Ok(new LivenessResponse("alive", "process"));
        });
        return app;
    }

    public sealed record LivenessResponse(string Status, string Scope);
}
