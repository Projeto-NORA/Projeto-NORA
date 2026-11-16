using Nora.Api.DAOs;
using Nora.Api.Database;
using Nora.Api.Models;
using Nora.Api.Services;

namespace Nora.Api;
internal static partial class ApiEndpoints
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
        app.MapPost("/api/auth/password-reset/request", async (PasswordResetStartRequest request,
            PasswordResetService service, CancellationToken ct) =>
            Results.Accepted(value: await service.StartAsync(request, ct)))
            .RequireRateLimiting("password-reset-request");
        app.MapPost("/api/auth/password-reset/confirm", async (PasswordResetFinishRequest request,
            PasswordResetService service, CancellationToken ct) =>
            Results.Ok(await service.FinishAsync(request, ct)))
            .RequireRateLimiting("password-reset-confirm");
        app.MapGet("/api/address/cep/{cep}", async (string cep, CepLookupService service, CancellationToken ct) =>
        {
            var address = await service.LookupAsync(cep, ct);
            return address is null ? Results.NotFound() : Results.Ok(address);
        }).RequireRateLimiting("cep");
        app.MapGet("/api/registration/units", async (HealthUnitService service, CancellationToken ct) =>
            Results.Ok(new { units = (await service.ListReadyAsync(ct)).Select(unit => new { unit.Id, unit.Name, unit.Region }) }))
            .RequireRateLimiting("registration-units");
        app.MapPost("/api/registration/start", async (RegistrationStartRequest request, PatientRegistrationService service, CancellationToken ct) =>
            Results.Ok(await service.StartAsync(request, ct))).RequireRateLimiting("registration-start");
        app.MapPost("/api/registration/send-code", async (RegistrationSendRequest request, PatientRegistrationService service, CancellationToken ct) =>
        {
            await service.SendCodeAsync(request, ct);
            return Results.NoContent();
        }).RequireRateLimiting("registration-send");
        app.MapPost("/api/registration/confirm", async (RegistrationConfirmRequest request, PatientRegistrationService service, CancellationToken ct) =>
            Results.Ok(await service.ConfirmAsync(request, ct))).RequireRateLimiting("registration-confirm");
        var auth=app.MapGroup("/api/auth").AddEndpointFilter(new SessionFilter(null,allowPasswordChangePending:true));
        auth.MapGet("/me",(HttpContext context)=>Results.Ok(Session(context).User));
        auth.MapPost("/logout",async(HttpContext context,AuthService service,CancellationToken ct)=>
        {
            await service.LogoutAsync(Session(context),ct);
            return Results.NoContent();
        });
        auth.MapPost("/password",async(ChangePasswordRequest request,HttpContext context,AuthService service,CancellationToken ct)=>
        {
            await service.ChangePasswordAsync(Session(context),request,ct);
            return Results.NoContent();
        }).AddEndpointFilter(new AccountRateLimitFilter(AccountRateLimiter.PasswordChange));
        var obs=app.MapGroup("/api").AddEndpointFilter(new SessionFilter("obs"));
        obs.MapGet("/obs/units", async (HttpContext context, HealthUnitService service, CancellationToken ct) => Results.Ok(await service.ListForStaffAsync(Session(context).User.Id, ct)));
        obs.MapGet("/obs/units/{unitId:guid}/registry", async (Guid unitId, HttpContext context, UnitAccessService access, UnitRegistryReadService registry, CancellationToken ct) =>
            Results.Ok(await access.ExecuteAsync(Session(context), unitId, () => registry.ListAsync(unitId, true, ct), ct)));
        obs.MapGet("/obs/units/{unitId:guid}/metrics", async (Guid unitId, DateTimeOffset from, DateTimeOffset to, HttpContext context, UnitAccessService access, MetricsService metrics, CancellationToken ct) =>
            Results.Ok(await access.ExecuteAsync(Session(context), unitId, () => metrics.ReadUnitAsync(unitId, from, to, ct), ct)));
        obs.MapPost("/obs/units/{unitId:guid}/registry", async (Guid unitId, UnitPatientRecord request, HttpContext context, UnitAccessService access, UnitPatientRegistryDao registry, CancellationToken ct) =>
        {
            var session = Session(context);
            await access.ExecuteAsync(session, unitId, async () => { await registry.CreateAsync(unitId, session.User.Id, request, ct); return true; }, ct);
            return Results.Json(new { created = true }, statusCode: StatusCodes.Status201Created);
        });
        obs.MapPut("/obs/units/{unitId:guid}/registry", async (Guid unitId, UnitPatientRecord request, HttpContext context, UnitAccessService access, UnitPatientStatusService service, CancellationToken ct) =>
        {
            var session = Session(context);
            await access.ExecuteAsync(session, unitId, async () => { await service.UpdateDetailsAsync(unitId, session.User.Id, request, ct); return true; }, ct);
            return Results.NoContent();
        });
        obs.MapPatch("/obs/units/{unitId:guid}/registry/email", async (Guid unitId, UnitPatientEmailRequest request, HttpContext context, UnitAccessService access, UnitPatientStatusService status, CancellationToken ct) =>
        {
            var session = Session(context);
            await access.ExecuteAsync(session, unitId, async () => { await status.UpdateEmailAsync(unitId, session.User.Id, request.Cpf, request.Email, ct); return true; }, ct);
            return Results.NoContent();
        });
        obs.MapPatch("/obs/units/{unitId:guid}/registry/active", async (Guid unitId, UnitPatientActiveRequest request, HttpContext context, UnitAccessService access, UnitPatientStatusService status, UnitLightningService lightning, CancellationToken ct) =>
        {
            var session = Session(context);
            await access.ExecuteAsync(session, unitId, async () => { await status.SetActiveAsync(unitId, session.User.Id, request, ct); return true; }, ct);
            await lightning.TryAdvanceAsync(unitId, ct);
            return Results.NoContent();
        });
        obs.MapGet("/obs/units/{unitId:guid}/appointments", async (Guid unitId, DateTimeOffset from, DateTimeOffset to, HttpContext context, UnitAccessService access, AppointmentDao appointments, CancellationToken ct) =>
            Results.Ok(await access.ExecuteAsync(Session(context), unitId, () => appointments.ListAsync(unitId, from, to, null, ct), ct)));
        obs.MapPost("/obs/units/{unitId:guid}/appointments", async (Guid unitId, CreateAppointmentRequest request, HttpContext context, UnitAccessService access, AppointmentDao appointments, CancellationToken ct) =>
        {
            var session = Session(context);
            var result = await access.ExecuteAsync(session, unitId, () => appointments.CreateAsync(unitId, session.User.Id, request, ct), ct);
            return Results.Json(result, statusCode: StatusCodes.Status201Created);
        });
        obs.MapPatch("/obs/units/{unitId:guid}/appointments/{id:guid}/status", async (Guid unitId, Guid id, AppointmentStatusRequest request, HttpContext context, UnitAccessService access, AppointmentDao appointments, CancellationToken ct) =>
        {
            var session = Session(context);
            return Results.Ok(await access.ExecuteAsync(session, unitId, () => appointments.ChangeStatusAsync(unitId, session.User.Id, id, request, ct), ct));
        });
        obs.MapGet("/obs/units/{unitId:guid}/waiting-list", async (Guid unitId, HttpContext context, UnitAccessService access, UnitWaitingListDao waiting, CancellationToken ct) =>
            Results.Ok(await access.ExecuteAsync(Session(context), unitId, () => waiting.ListAsync(unitId, ct), ct)));
        obs.MapPost("/obs/units/{unitId:guid}/waiting-list", async (Guid unitId, AddUnitWaitingEntryRequest request, HttpContext context, UnitAccessService access, UnitWaitingListDao waiting, UnitLightningService lightningService, CancellationToken ct) =>
        {
            var session = Session(context);
            var entry = await access.ExecuteAsync(session, unitId, () => waiting.AddAsync(unitId, session.User.Id, request, ct), ct);
            await lightningService.TryAdvanceAsync(unitId, ct);
            return Results.Json(entry, statusCode: StatusCodes.Status201Created);
        });
        obs.MapPost("/obs/units/{unitId:guid}/waiting-list/{id:long}/cancel", async (Guid unitId, long id, CancelUnitWaitingEntryRequest request, HttpContext context, UnitAccessService access, UnitWaitingListDao waiting, UnitLightningService lightningService, CancellationToken ct) =>
        {
            var session = Session(context);
            await access.ExecuteAsync(session, unitId, async () => { await waiting.CancelAsync(unitId, session.User.Id, id, request.Confirmed, ct); return true; }, ct);
            await lightningService.TryAdvanceAsync(unitId, ct);
            return Results.NoContent();
        });
        obs.MapGet("/obs/units/{unitId:guid}/lightning-slots", async (Guid unitId, HttpContext context, UnitAccessService access, UnitLightningDao lightning, CancellationToken ct) =>
            Results.Ok(await access.ExecuteAsync(Session(context), unitId, () => lightning.ListAsync(unitId, ct), ct)));
        obs.MapPost("/obs/units/{unitId:guid}/lightning-slots", async (Guid unitId, CreateUnitLightningSlotRequest request, HttpContext context, UnitAccessService access, UnitLightningDao lightning, UnitLightningService lightningService, CancellationToken ct) =>
        {
            var session = Session(context);
            var slot = await access.ExecuteAsync(session, unitId, () => lightning.CreateAsync(unitId, session.User.Id, request, ct), ct);
            await lightningService.TryAdvanceAsync(unitId, ct);
            return Results.Json(slot, statusCode: StatusCodes.Status201Created);
        });
        obs.MapGet("/obs/units/{unitId:guid}/lightning-slots/{slotId:guid}/offers", async (Guid unitId, Guid slotId, HttpContext context, UnitAccessService access, UnitLightningDao lightning, CancellationToken ct) =>
            Results.Ok(await access.ExecuteAsync(Session(context), unitId, () => lightning.ListOffersAsync(unitId, slotId, ct), ct)));
        var legacyObs = app.MapGroup("/api").AddEndpointFilter(new SessionFilter("obs")).AddEndpointFilter(new LegacyWorkflowFilter());
        legacyObs.MapGet("/patients",async(AccountService service,CancellationToken ct)=>Results.Ok(await service.PatientsAsync(ct)));
        legacyObs.MapPost("/patients",async(CreatePatientRequest request,AccountService service,CancellationToken ct)=>Results.Json(await service.CreatePatientAsync(request,ct),statusCode:StatusCodes.Status201Created));
        legacyObs.MapPatch("/patients/{id:guid}/active",async(Guid id,SetPatientActiveRequest request,WorkflowService service,CancellationToken ct)=>
        {
            await service.SetPatientActiveAsync(id,request,ct);
            return Results.NoContent();
        });
        legacyObs.MapGet("/queue",async(WorkflowService service,CancellationToken ct)=>Results.Ok(await service.QueueAsync(ct)));
        legacyObs.MapPost("/queue",async(AddQueueRequest request,WorkflowService service,CancellationToken ct)=>Results.Json(await service.AddQueueAsync(request,ct),statusCode:StatusCodes.Status201Created));
        legacyObs.MapPost("/queue/{id:long}/cancel",async(long id,WorkflowService service,CancellationToken ct)=>
        {
            await service.CancelQueueAsync(id,ct);
            return Results.NoContent();
        });
        legacyObs.MapGet("/slots",async(WorkflowService service,CancellationToken ct)=>Results.Ok(await service.SlotsAsync(ct)));
        legacyObs.MapPost("/slots",async(CreateSlotRequest request,WorkflowService service,CancellationToken ct)=>Results.Json(await service.AddSlotAsync(request,ct),statusCode:StatusCodes.Status201Created));
        var patient=app.MapGroup("/api").AddEndpointFilter(new SessionFilter("patient"));
        patient.MapPost("/patient/presence",async(HttpContext context,AuthService service,CancellationToken ct)=>
        {
            return await service.SetPresenceAsync(Session(context),true,ct)>0 ? Results.NoContent() : Results.Unauthorized();
        });
        patient.MapPost("/patient/presence/offline",async(HttpContext context,AuthService service,CancellationToken ct)=>
        {
            return await service.SetPresenceAsync(Session(context),false,ct)>0 ? Results.NoContent() : Results.Unauthorized();
        });
        patient.MapGet("/patient/units", async (HttpContext context, HealthUnitService service, UnitAccessService access, CancellationToken ct) =>
        {
            var session = Session(context); var visible = new List<HealthUnit>();
            foreach (var unit in await service.ListForPatientAsync(session.User.Id, ct))
            {
                // UBS ainda sem banco não tem cadastro a conferir: fica fora da lista em vez de derrubar a lista inteira com 503.
                // Banco pronto e fora do ar continua respondendo 503. Banco desatualizado também fica de fora (a equipe é avisada no log).
                if (unit.DatabaseState != "ready") continue;
                try { await access.ExecutePatientAsync(session, unit.Id, _ => Task.FromResult(true), ct); visible.Add(unit); }
                catch (ApiForbiddenException) { }
                catch (ApiCodedException outdated) when (outdated.Code == "unit-database-outdated") { }
            }
            return Results.Ok(new { units = visible });
        });
        patient.MapPost("/patient/units/link", async (HttpContext context, PatientUnitLinkRequest request, PatientRegistrationService service, CancellationToken ct) =>
            Results.Ok(await service.LinkUnitsAsync(Session(context), request, ct))).RequireRateLimiting("patient-link-units");
        patient.MapGet("/patient/units/{unitId:guid}/appointments", async (Guid unitId, DateTimeOffset from, DateTimeOffset to, HttpContext context, UnitAccessService access, AppointmentDao appointments, CancellationToken ct) =>
            Results.Ok(new { appointments = await access.ExecutePatientAsync(Session(context), unitId, cpf => appointments.ListAsync(unitId, from, to, cpf, ct), ct) }));
        patient.MapPost("/patient/units/{unitId:guid}/appointments/{id:guid}/cancel", async (Guid unitId, Guid id, CancelAppointmentRequest request, HttpContext context, UnitAccessService access, AppointmentDao appointments, CancellationToken ct) =>
        {
            var session = Session(context);
            return Results.Ok(await access.ExecutePatientAsync(session, unitId, cpf => appointments.ChangeStatusAsync(unitId, session.User.Id, id, new("cancelled", request.Confirmed), ct, cpf), ct));
        });
        patient.MapGet("/patient/units/{unitId:guid}/lightning-offers", async (Guid unitId, HttpContext context, UnitAccessService access, UnitLightningDao lightning, UnitLightningService lightningService, CancellationToken ct) =>
        {
            var session = Session(context);
            await access.ExecutePatientAsync(session, unitId, _ => Task.FromResult(true), ct);
            await lightningService.TryAdvanceAsync(unitId, ct);
            return Results.Ok(new { offers = await access.ExecutePatientAsync(session, unitId, cpf => lightning.ListForPatientAsync(unitId, cpf, ct), ct) });
        });
        patient.MapGet("/patient/units/{unitId:guid}/waiting-list", async (Guid unitId, HttpContext context, UnitAccessService access, UnitWaitingListDao waiting, CancellationToken ct) =>
            Results.Ok(new { entries = await access.ExecutePatientAsync(Session(context), unitId, cpf => waiting.ListAsync(unitId, ct, cpf), ct) }));
        patient.MapPost("/patient/units/{unitId:guid}/lightning-offers/{id:guid}/respond", async (Guid unitId, Guid id, UnitLightningResponse request, HttpContext context, UnitAccessService access, UnitLightningDao lightning, UnitLightningService lightningService, CancellationToken ct) =>
        {
            var session = Session(context);
            var result = await access.ExecutePatientAsync(session, unitId, cpf => lightning.RespondAsync(unitId, session.User.Id, cpf, id, request.Accept, ct), ct);
            if (!result.Accepted) await lightningService.TryAdvanceAsync(unitId, ct);
            return Results.Ok(result);
        });
        patient.MapPost("/patient/units/{unitId:guid}/waiting-list/{id:long}/cancel", async (Guid unitId, long id, CancelUnitWaitingEntryRequest request, HttpContext context, UnitAccessService access, UnitWaitingListDao waiting, UnitLightningService lightningService, CancellationToken ct) =>
        {
            var session = Session(context);
            await access.ExecutePatientAsync(session, unitId, async cpf => { await waiting.CancelByPatientAsync(unitId, session.User.Id, cpf, id, request.Confirmed, ct); return true; }, ct);
            await lightningService.TryAdvanceAsync(unitId, ct);
            return Results.NoContent();
        });
        patient.MapPost("/patient/units/{unitId:guid}/unlink", async (Guid unitId, UnlinkUnitRequest request, HttpContext context, UnitAccessService access, UnitPatientUnlinkDao unlink, UnitLightningService lightningService, CancellationToken ct) =>
        {
            if (!request.Confirmed) throw new ApiInputException();
            var session = Session(context);
            var result = await access.ExecutePatientUnlinkAsync(session, unitId, cpf => unlink.CancelPendingAsync(unitId, session.User.Id, cpf, ct), ct);
            await lightningService.TryAdvanceAsync(unitId, ct);
            return Results.Ok(result);
        }).AddEndpointFilter(new AccountRateLimitFilter(AccountRateLimiter.PatientUnlink));
        patient.MapGet("/patient/units/{unitId:guid}/attendance", async (Guid unitId, HttpContext context, UnitAccessService access, UnitRegistryReadService registry, CancellationToken ct) =>
            Results.Ok(await access.ExecutePatientAsync(Session(context), unitId, cpf => registry.ReadOwnAttendanceAsync(unitId, cpf, ct), ct)));
        var legacyPatient = app.MapGroup("/api").AddEndpointFilter(new SessionFilter("patient")).AddEndpointFilter(new LegacyWorkflowFilter());
        legacyPatient.MapGet("/patient/overview",async(HttpContext context,WorkflowService service,CancellationToken ct)=>Results.Ok(await service.OverviewAsync(Session(context),ct)));
        legacyPatient.MapPost("/offers/{id:guid}/respond",async(Guid id,RespondOfferRequest request,HttpContext context,WorkflowService service,CancellationToken ct)=>Results.Ok(await service.RespondAsync(id,Session(context),request,ct)));
        app.MapCommunityEndpoints(scope => new SessionFilter(scope)); // Fase 3 — comunicação e jornada (ApiEndpoints.Community.cs)
        var municipal = app.MapGroup("/api/municipal").AddEndpointFilter(new SessionFilter("municipal"));
        municipal.MapGet("/staff", async (HealthUnitService service, CancellationToken ct) => Results.Ok(await service.ListStaffAsync(ct)));
        municipal.MapPost("/staff", async (CreateStaffRequest request, HttpContext context, StaffAccountService service, CancellationToken ct) =>
            Results.Json(await service.CreateAsync(Session(context).User.Id, request, ct), statusCode: StatusCodes.Status201Created)).RequireRateLimiting("staff-account");
        municipal.MapPatch("/staff/{id:guid}/active", async (Guid id, SetStaffActiveRequest request, HttpContext context, StaffAccountService service, CancellationToken ct) =>
        {
            await service.SetActiveAsync(Session(context).User.Id, id, request, ct);
            return Results.NoContent();
        }).RequireRateLimiting("staff-account");
        municipal.MapPost("/staff/{id:guid}/password", async (Guid id, ResetStaffPasswordRequest request, HttpContext context, StaffAccountService service, CancellationToken ct) =>
        {
            await service.ResetPasswordAsync(Session(context).User.Id, id, request, ct);
            return Results.NoContent();
        }).RequireRateLimiting("staff-account");
        municipal.MapGet("/staff/{id:guid}/units", async (Guid id, HealthUnitService service, CancellationToken ct) => Results.Ok(await service.ListForStaffAsync(id, ct)));
        municipal.MapPut("/staff/{id:guid}/units/{unitId:guid}", async (Guid id, Guid unitId, HttpContext context, HealthUnitService service, CancellationToken ct) =>
        {
            await service.SetStaffAccessAsync(id, unitId, Session(context).User.Id, true, ct);
            return Results.NoContent();
        });
        municipal.MapDelete("/staff/{id:guid}/units/{unitId:guid}", async (Guid id, Guid unitId, HttpContext context, HealthUnitService service, CancellationToken ct) =>
        {
            await service.SetStaffAccessAsync(id, unitId, Session(context).User.Id, false, ct);
            return Results.NoContent();
        });
        municipal.MapGet("/units", async (HealthUnitService service, CancellationToken ct) => Results.Ok(await service.ListAsync(ct)));
        municipal.MapGet("/units/{unitId:guid}/registry", async (Guid unitId, HttpContext context, UnitAccessService access, UnitRegistryReadService registry, CancellationToken ct) =>
            Results.Ok(await access.ExecuteManagementReadAsync(Session(context), unitId, () => registry.ListAsync(unitId, false, ct), ct)));
        municipal.MapGet("/units/{unitId:guid}/appointments", async (Guid unitId, DateTimeOffset from, DateTimeOffset to, HttpContext context, UnitAccessService access, AppointmentDao appointments, CancellationToken ct) =>
            Results.Ok(await access.ExecuteManagementReadAsync(Session(context), unitId, () => appointments.ListAsync(unitId, from, to, null, ct), ct, "appointment_read")));
        // Cada cadastro cria um banco: mesmo limitador da nova tentativa (compartilhado por IP).
        municipal.MapPost("/units", async (HealthUnitRequest request, HttpContext context, HealthUnitService service, CancellationToken ct) => Results.Json(await service.CreateAsync(request, Session(context).User.Id, ct), statusCode: StatusCodes.Status201Created)).RequireRateLimiting("unit-database");
        municipal.MapPost("/units/{id:guid}/database", async (Guid id, HttpContext context, HealthUnitService service, CancellationToken ct) => Results.Ok(await service.ProvisionDatabaseAsync(id, Session(context).User.Id, ct))).RequireRateLimiting("unit-database");
        municipal.MapGet("/history", async (DateTimeOffset? from, DateTimeOffset? to, Guid? unitId, int? limit, string? before, HistoryDao history, CancellationToken ct) =>
            Results.Ok(await history.ReadAsync(from, to, unitId, limit, before, ct)));
        municipal.MapGet("/metrics", async (DateTimeOffset from, DateTimeOffset to, Guid? unitId, string? region, string? specialty, MetricsService metrics, CancellationToken ct) =>
            Results.Ok(await metrics.ReadNetworkAsync(from, to, unitId, region, specialty, ct)));
        municipal.MapPut("/units/{id:guid}", async (Guid id, HealthUnitRequest request, HealthUnitService service, CancellationToken ct) => Results.Ok(await service.UpdateAsync(id, request, ct)));
        app.MapSchedulingEndpoints();
        app.MapGet("/api/municipal/summary",async(Guid? unitId, IConfiguration config, IHostEnvironment environment, UnitNetworkSummaryDao network, WorkflowService legacy, CancellationToken ct) =>
            Results.Ok(LegacyEnabled(config, environment) ? await legacy.SummaryAsync(ct) : await network.ReadAsync(unitId, ct))).AddEndpointFilter(new SessionFilter("municipal"));
    }
    private static SessionInfo Session(HttpContext context)=>(SessionInfo)context.Items[typeof(SessionInfo)]!;
    private static bool LegacyEnabled(IConfiguration config, IHostEnvironment environment) => environment.IsDevelopment() && config.GetValue("Nora:LegacyGlobalWorkflow:Enabled", false);
    private sealed class LegacyWorkflowFilter : IEndpointFilter
    {
        public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext invocation, EndpointFilterDelegate next)
            => LegacyEnabled(invocation.HttpContext.RequestServices.GetRequiredService<IConfiguration>(), invocation.HttpContext.RequestServices.GetRequiredService<IHostEnvironment>())
                ? next(invocation) : ValueTask.FromResult<object?>(Results.StatusCode(StatusCodes.Status403Forbidden));
    }
    /// <summary>Limite por conta autenticada; precisa vir depois do SessionFilter (filtros do grupo rodam antes dos do endpoint).</summary>
    private sealed class AccountRateLimitFilter(string policy):IEndpointFilter
    {
        public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext invocation,EndpointFilterDelegate next)
        {
            invocation.HttpContext.RequestServices.GetRequiredService<AccountRateLimiter>().Require(policy,Session(invocation.HttpContext).User.Id);
            return next(invocation);
        }
    }
    private sealed class SessionFilter(string? scope,bool allowPasswordChangePending=false):IEndpointFilter
    {
        public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext invocation,EndpointFilterDelegate next)
        {
            var context=invocation.HttpContext;
            var authorization=context.Request.Headers.Authorization.ToString();
            if(!authorization.StartsWith("Bearer ",StringComparison.OrdinalIgnoreCase)) return Results.Unauthorized();
            var session=await context.RequestServices.GetRequiredService<AuthService>().AuthenticateAsync(authorization[7..],context.RequestAborted);
            if(session is null) return Results.Unauthorized();
            // Senha provisória: só /api/auth/me, /logout e /password respondem até a troca (o grupo auth é o único que passa allowPasswordChangePending).
            if(session.User.MustChangePassword && !allowPasswordChangePending) throw new ApiCodedException(StatusCodes.Status403Forbidden,"password-change-required");
            if(scope is not null && session.User.Scope!=scope) return Results.StatusCode(403);
            if(scope=="obs" && session.User.Role is not("administrator" or "attendant")) return Results.StatusCode(403);
            context.Items[typeof(SessionInfo)]=session;
            return await next(invocation);
        }
    }
}
