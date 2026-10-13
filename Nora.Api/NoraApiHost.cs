using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;
using Npgsql;
using Nora.Api.DAOs;
using Nora.Api.Database;
using Nora.Api.Models;
using Nora.Api.Services;

namespace Nora.Api;

public static class NoraApiHost
{
    public static WebApplication Create(WebApplicationOptions options,Action<IServiceCollection>? configureServices=null)
    {
        var builder=WebApplication.CreateBuilder(options);
        builder.Services.AddProblemDetails();
        builder.WebHost.ConfigureKestrel(server=>server.AddServerHeader=false);
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<DatabaseHelper>();
        builder.Services.AddSingleton<AccountDao>();
        builder.Services.AddSingleton<SessionDao>();
        builder.Services.AddSingleton<WorkflowDao>();
        builder.Services.AddSingleton<MigrationDao>();
        builder.Services.AddSingleton<IPasswordHasher<Account>,PasswordHasher<Account>>();
        builder.Services.AddSingleton<AccountService>();
        builder.Services.AddSingleton<AuthService>();
        builder.Services.AddSingleton<WorkflowService>();
        builder.Services.AddHostedService<OfferExpirationWorker>();
        builder.Services.AddRateLimiter(options=>
        {
            options.RejectionStatusCode=429;
            options.AddPolicy("login",context=>RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString()??"unknown",_=>new FixedWindowRateLimiterOptions {PermitLimit=30,Window=TimeSpan.FromMinutes(1),QueueLimit=0}));
        });
        configureServices?.Invoke(builder.Services);
        var app=builder.Build();
        app.UseExceptionHandler(errors=>errors.Run(context=>
        {
            var exception=context.Features.Get<IExceptionHandlerFeature>()?.Error;
            var status=exception switch
            {
                ApiInputException or BadHttpRequestException=>400,
                ApiConflictException=>409,
                ApiNotFoundException=>404,
                ApiUnauthorizedException=>401,
                PostgresException {SqlState:"23505"}=>409,
                DatabaseUnavailableException or NpgsqlException=>503,
                _=>500
            };
            return ApiProblems.WriteAsync(context,status);
        }));
        app.UseStatusCodePages(context=>ApiProblems.WriteAsync(context.HttpContext,context.HttpContext.Response.StatusCode));
        app.Use(async(context,next)=>
        {
            context.Response.Headers.CacheControl="no-store";
            await next(context);
        });
        app.UseRateLimiter();
        app.MapMethods("/health/live",[HttpMethods.Get,HttpMethods.Head],(HttpContext context)=>TypedResults.Ok(new LivenessResponse("alive","process")));
        app.MapNoraEndpoints();
        return app;
    }
    public sealed record LivenessResponse(string Status,string Scope);
}
