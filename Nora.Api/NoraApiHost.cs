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
        builder.Services.AddSingleton<UnitDatabaseRouter>();
        builder.Services.AddSingleton<UnitMigrationDao>();
        builder.Services.AddSingleton<UnitPatientRegistryDao>();
        builder.Services.AddSingleton<AppointmentDao>();
        builder.Services.AddSingleton<UnitWaitingListDao>();
        builder.Services.AddSingleton<UnitLightningDao>();
        builder.Services.AddSingleton<UnitLightningDispatcher>();
        builder.Services.AddSingleton<UnitLightningService>();
        builder.Services.AddSingleton<UnitPatientStatusService>();
        builder.Services.AddSingleton<UnitRegistryReadService>();
        builder.Services.AddSingleton<UnitNetworkSummaryDao>();
        builder.Services.AddSingleton<UnitMetricsDao>();
        builder.Services.AddSingleton<UnitPatientUnlinkDao>();
        builder.Services.AddSingleton<HistoryDao>();
        builder.Services.AddSingleton<StaffAccountDao>();
        builder.Services.AddSingleton<StaffAccountService>();
        builder.Services.AddSingleton<MetricsService>();
        builder.Services.AddSingleton<UnitDatabaseProvisioner>();
        builder.Services.AddSingleton<UnitAccessService>();
        builder.Services.AddSingleton<PatientRegistrationService>();
        builder.Services.AddSingleton<AccountDao>();
        builder.Services.AddSingleton<SessionDao>();
        builder.Services.AddSingleton<WorkflowDao>();
        builder.Services.AddSingleton<HealthUnitDao>();
        builder.Services.AddSingleton<HealthUnitService>();
        builder.Services.AddSingleton<MigrationDao>();
        builder.Services.AddSingleton<PasswordResetDao>();
        builder.Services.AddSingleton<IPasswordHasher<Account>,PasswordHasher<Account>>();
        builder.Services.AddSingleton<IEmailSender,SmtpEmailSender>();
        builder.Services.AddMemoryCache();
        builder.Services.AddHttpClient<CepLookupService>(client =>
        {
            client.BaseAddress = new Uri("https://viacep.com.br/");
            client.Timeout = TimeSpan.FromSeconds(5);
        });
        builder.Services.AddSingleton<AccountService>();
        builder.Services.AddSingleton<AuthService>();
        builder.Services.AddSingleton<PasswordResetService>();
        builder.Services.AddSingleton<WorkflowService>();
        builder.Services.AddHostedService<OfferExpirationWorker>();
        builder.Services.AddHostedService<UnitLightningWorker>();
        builder.Services.AddHostedService<UnitSchemaCheckService>();
        builder.Services.AddRateLimiter(options=>
        {
            options.RejectionStatusCode=429;
            options.AddPolicy("login",context=>RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString()??"unknown",_=>new FixedWindowRateLimiterOptions {PermitLimit=30,Window=TimeSpan.FromMinutes(1),QueueLimit=0}));
            options.AddPolicy("password-reset-request",context=>RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString()??"unknown",_=>new FixedWindowRateLimiterOptions {PermitLimit=5,Window=TimeSpan.FromMinutes(10),QueueLimit=0}));
            options.AddPolicy("password-reset-confirm",context=>RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString()??"unknown",_=>new FixedWindowRateLimiterOptions {PermitLimit=20,Window=TimeSpan.FromMinutes(10),QueueLimit=0}));
            options.AddPolicy("registration-start",context=>RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString()??"unknown",_=>new FixedWindowRateLimiterOptions {PermitLimit=10,Window=TimeSpan.FromMinutes(10),QueueLimit=0}));
            options.AddPolicy("registration-send",context=>RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString()??"unknown",_=>new FixedWindowRateLimiterOptions {PermitLimit=10,Window=TimeSpan.FromMinutes(10),QueueLimit=0}));
            options.AddPolicy("registration-confirm",context=>RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString()??"unknown",_=>new FixedWindowRateLimiterOptions {PermitLimit=20,Window=TimeSpan.FromMinutes(10),QueueLimit=0}));
            options.AddPolicy("registration-units",context=>RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString()??"unknown",_=>new FixedWindowRateLimiterOptions {PermitLimit=30,Window=TimeSpan.FromMinutes(1),QueueLimit=0}));
            options.AddPolicy("patient-link-units",context=>RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString()??"unknown",_=>new FixedWindowRateLimiterOptions {PermitLimit=20,Window=TimeSpan.FromMinutes(10),QueueLimit=0}));
            options.AddPolicy("password-change",context=>RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString()??"unknown",_=>new FixedWindowRateLimiterOptions {PermitLimit=10,Window=TimeSpan.FromMinutes(10),QueueLimit=0}));
            options.AddPolicy("staff-account",context=>RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString()??"unknown",_=>new FixedWindowRateLimiterOptions {PermitLimit=30,Window=TimeSpan.FromMinutes(10),QueueLimit=0}));
            options.AddPolicy("unit-database",context=>RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString()??"unknown",_=>new FixedWindowRateLimiterOptions {PermitLimit=20,Window=TimeSpan.FromMinutes(10),QueueLimit=0}));
            options.AddPolicy("patient-unlink",context=>RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString()??"unknown",_=>new FixedWindowRateLimiterOptions {PermitLimit=10,Window=TimeSpan.FromMinutes(10),QueueLimit=0}));
            options.AddPolicy("cep",context=>RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString()??"unknown",_=>new FixedWindowRateLimiterOptions {PermitLimit=30,Window=TimeSpan.FromMinutes(1),QueueLimit=0}));
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
                ApiForbiddenException=>403,
                ApiRateLimitException=>429,
                ApiCodedException coded=>coded.Status,
                EmailDeliveryUnavailableException=>503,
                CepUnavailableException=>503,
                PostgresException {SqlState:"23505"}=>409,
                DatabaseUnavailableException or NpgsqlException=>503,
                _=>500
            };
            return ApiProblems.WriteAsync(context,status,(exception as ApiCodedException)?.Code);
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
