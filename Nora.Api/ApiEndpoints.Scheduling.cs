using Nora.Api.DAOs;
using Nora.Api.Models;
using Nora.Api.Services;

namespace Nora.Api;

/// <summary>Fase 3 — agenda e metas: especialidades, profissionais, consultórios, grade, bloqueios, disponibilidade, agendamento do paciente, confirmação, metas e alertas.</summary>
internal static partial class ApiEndpoints
{
    private static void MapSchedulingEndpoints(this WebApplication app)
    {
        var obs = app.MapGroup("/api").AddEndpointFilter(new SessionFilter("obs"));
        obs.MapGet("/obs/specialties", async (SpecialtyDao specialties, CancellationToken ct) => Results.Ok(new { specialties = await specialties.ListAsync(false, ct) }));
        obs.MapGet("/obs/units/{unitId:guid}/professionals", async (Guid unitId, HttpContext context, UnitAccessService access, SchedulingDao dao, CancellationToken ct) =>
            Results.Ok(new { professionals = await access.ExecuteAsync(Session(context), unitId, () => dao.ListProfessionalsAsync(unitId, ct), ct) }));
        obs.MapPost("/obs/units/{unitId:guid}/professionals", async (Guid unitId, ProfessionalRequest request, HttpContext context, SchedulingAccessService access, SchedulingService service, SchedulingDao dao, CancellationToken ct) =>
            Results.Json(await access.ExecuteAdministratorAsync(Session(context), unitId, async () => await dao.CreateProfessionalAsync(unitId, await service.ValidateProfessionalAsync(request, ct), ct), ct), statusCode: StatusCodes.Status201Created));
        obs.MapPut("/obs/units/{unitId:guid}/professionals/{id:guid}", async (Guid unitId, Guid id, ProfessionalRequest request, HttpContext context, SchedulingAccessService access, SchedulingService service, SchedulingDao dao, CancellationToken ct) =>
            Results.Ok(await access.ExecuteAdministratorAsync(Session(context), unitId, async () => await dao.UpdateProfessionalAsync(unitId, id, await service.ValidateProfessionalAsync(request, ct), ct), ct)));
        obs.MapPatch("/obs/units/{unitId:guid}/professionals/{id:guid}/active", async (Guid unitId, Guid id, SetActiveRequest request, HttpContext context, SchedulingAccessService access, SchedulingDao dao, CancellationToken ct) =>
            Results.Ok(await access.ExecuteAdministratorAsync(Session(context), unitId, () => dao.SetProfessionalActiveAsync(unitId, id, request.IsActive, ct), ct)));

        obs.MapGet("/obs/units/{unitId:guid}/rooms", async (Guid unitId, HttpContext context, UnitAccessService access, SchedulingDao dao, CancellationToken ct) =>
            Results.Ok(new { rooms = await access.ExecuteAsync(Session(context), unitId, () => dao.ListRoomsAsync(unitId, ct), ct) }));
        obs.MapPost("/obs/units/{unitId:guid}/rooms", async (Guid unitId, RoomRequest request, HttpContext context, SchedulingAccessService access, SchedulingDao dao, CancellationToken ct) =>
            Results.Json(await access.ExecuteAdministratorAsync(Session(context), unitId, () => dao.CreateRoomAsync(unitId, SchedulingService.ValidateRoomName(request), ct), ct), statusCode: StatusCodes.Status201Created));
        obs.MapPut("/obs/units/{unitId:guid}/rooms/{id:guid}", async (Guid unitId, Guid id, RoomRequest request, HttpContext context, SchedulingAccessService access, SchedulingDao dao, CancellationToken ct) =>
            Results.Ok(await access.ExecuteAdministratorAsync(Session(context), unitId, () => dao.RenameRoomAsync(unitId, id, SchedulingService.ValidateRoomName(request), ct), ct)));
        obs.MapPatch("/obs/units/{unitId:guid}/rooms/{id:guid}/active", async (Guid unitId, Guid id, SetActiveRequest request, HttpContext context, SchedulingAccessService access, SchedulingDao dao, CancellationToken ct) =>
            Results.Ok(await access.ExecuteAdministratorAsync(Session(context), unitId, () => dao.SetRoomActiveAsync(unitId, id, request.IsActive, ct), ct)));

        obs.MapGet("/obs/units/{unitId:guid}/schedule", async (Guid unitId, Guid? professionalId, HttpContext context, UnitAccessService access, SchedulingDao dao, CancellationToken ct) =>
            Results.Ok(new { schedule = await access.ExecuteAsync(Session(context), unitId, () => dao.ListTemplatesAsync(unitId, professionalId, ct), ct) }));
        obs.MapPost("/obs/units/{unitId:guid}/schedule", async (Guid unitId, ScheduleTemplateRequest request, HttpContext context, SchedulingAccessService access, SchedulingService service, SchedulingDao dao, CancellationToken ct) =>
            Results.Json(await access.ExecuteAdministratorAsync(Session(context), unitId, async () => await dao.CreateTemplateAsync(unitId, (await service.ValidateTemplateAsync(request, ct)).Draft, ct), ct), statusCode: StatusCodes.Status201Created));
        obs.MapPut("/obs/units/{unitId:guid}/schedule/{id:guid}", async (Guid unitId, Guid id, ScheduleTemplateRequest request, HttpContext context, SchedulingAccessService access, SchedulingService service, SchedulingDao dao, CancellationToken ct) =>
            Results.Ok(await access.ExecuteAdministratorAsync(Session(context), unitId, async () => await dao.UpdateTemplateAsync(unitId, id, (await service.ValidateTemplateAsync(request, ct)).Draft, ct), ct)));
        obs.MapDelete("/obs/units/{unitId:guid}/schedule/{id:guid}", async (Guid unitId, Guid id, HttpContext context, SchedulingAccessService access, SchedulingDao dao, CancellationToken ct) =>
        {
            await access.ExecuteAdministratorAsync(Session(context), unitId, async () => { await dao.DeleteTemplateAsync(unitId, id, ct); return true; }, ct);
            return Results.NoContent();
        });

        obs.MapGet("/obs/units/{unitId:guid}/schedule-blocks", async (Guid unitId, DateTimeOffset from, DateTimeOffset to, HttpContext context, UnitAccessService access, SchedulingDao dao, CancellationToken ct) =>
            Results.Ok(new { blocks = await access.ExecuteAsync(Session(context), unitId, () => dao.ListBlocksAsync(unitId, from, ValidBlockWindow(from, to), ct), ct) }));
        obs.MapPost("/obs/units/{unitId:guid}/schedule-blocks", async (Guid unitId, ScheduleBlockRequest request, HttpContext context, SchedulingAccessService access, SchedulingDao dao, CancellationToken ct) =>
        {
            var session = Session(context);
            return Results.Json(await access.ExecuteAdministratorAsync(session, unitId, () =>
            {
                var block = SchedulingService.ValidateBlock(request);
                return dao.CreateBlockAsync(unitId, session.User.Id, block.ProfessionalId, block.StartsAt, block.EndsAt, block.Reason, ct);
            }, ct), statusCode: StatusCodes.Status201Created);
        });
        obs.MapDelete("/obs/units/{unitId:guid}/schedule-blocks/{id:guid}", async (Guid unitId, Guid id, HttpContext context, SchedulingAccessService access, SchedulingDao dao, CancellationToken ct) =>
        {
            await access.ExecuteAdministratorAsync(Session(context), unitId, async () => { await dao.DeleteBlockAsync(unitId, id, ct); return true; }, ct);
            return Results.NoContent();
        });

        obs.MapGet("/obs/units/{unitId:guid}/availability", async (Guid unitId, DateTimeOffset from, DateTimeOffset to, Guid? specialtyId, Guid? professionalId, HttpContext context, UnitAccessService access, SchedulingService service, CancellationToken ct) =>
            Results.Ok(new { slots = await access.ExecuteAsync(Session(context), unitId, () => service.AvailabilityAsync(unitId, from, to, specialtyId, professionalId, false, ct), ct) }));
        obs.MapGet("/obs/units/{unitId:guid}/active-rooms", async (Guid unitId, HttpContext context, UnitAccessService access, SchedulingService service, CancellationToken ct) =>
            Results.Ok(await access.ExecuteAsync(Session(context), unitId, () => service.ReadActiveRoomsAsync(unitId, ct), ct)));
        obs.MapGet("/obs/units/{unitId:guid}/alerts", async (Guid unitId, HttpContext context, UnitAccessService access, AlertService alerts, CancellationToken ct) =>
            Results.Ok(new { alerts = await access.ExecuteAsync(Session(context), unitId, () => alerts.ReadUnitAsync(unitId, ct), ct) }));

        var municipal = app.MapGroup("/api/municipal").AddEndpointFilter(new SessionFilter("municipal"));
        municipal.MapGet("/specialties", async (SpecialtyDao specialties, CancellationToken ct) => Results.Ok(new { specialties = await specialties.ListAsync(false, ct) }));
        municipal.MapPost("/specialties", async (SpecialtyRequest request, HttpContext context, SpecialtyDao specialties, CancellationToken ct) =>
            Results.Json(await specialties.CreateAsync(SpecialtyDao.ValidName(request.Name), Session(context).User.Id, ct), statusCode: StatusCodes.Status201Created));
        municipal.MapPut("/specialties/{id:guid}", async (Guid id, SpecialtyRequest request, HttpContext context, SpecialtyDao specialties, CancellationToken ct) =>
            Results.Ok(await specialties.RenameAsync(id, SpecialtyDao.ValidName(request.Name), Session(context).User.Id, ct)));
        municipal.MapPatch("/specialties/{id:guid}/active", async (Guid id, SetActiveRequest request, HttpContext context, SpecialtyDao specialties, CancellationToken ct) =>
            Results.Ok(await specialties.SetActiveAsync(id, request.IsActive, Session(context).User.Id, ct)));
        municipal.MapGet("/units/{unitId:guid}/professionals", async (Guid unitId, HttpContext context, SchedulingAccessService access, SchedulingDao dao, CancellationToken ct) =>
            Results.Ok(new { professionals = await access.ExecuteManagementCatalogReadAsync(Session(context), unitId, () => dao.ListProfessionalsAsync(unitId, ct), ct) }));
        municipal.MapGet("/units/{unitId:guid}/rooms", async (Guid unitId, HttpContext context, SchedulingAccessService access, SchedulingDao dao, CancellationToken ct) =>
            Results.Ok(new { rooms = await access.ExecuteManagementCatalogReadAsync(Session(context), unitId, () => dao.ListRoomsAsync(unitId, ct), ct) }));
        municipal.MapGet("/units/{unitId:guid}/schedule", async (Guid unitId, Guid? professionalId, HttpContext context, SchedulingAccessService access, SchedulingDao dao, CancellationToken ct) =>
            Results.Ok(new { schedule = await access.ExecuteManagementCatalogReadAsync(Session(context), unitId, () => dao.ListTemplatesAsync(unitId, professionalId, ct), ct) }));
        municipal.MapGet("/units/{unitId:guid}/schedule-blocks", async (Guid unitId, DateTimeOffset from, DateTimeOffset to, HttpContext context, SchedulingAccessService access, SchedulingDao dao, CancellationToken ct) =>
            Results.Ok(new { blocks = await access.ExecuteManagementCatalogReadAsync(Session(context), unitId, () => dao.ListBlocksAsync(unitId, from, ValidBlockWindow(from, to), ct), ct) }));
        municipal.MapGet("/goals", async (GoalService goals, CancellationToken ct) => Results.Ok(new { goals = await goals.ListAsync(ct) }));
        municipal.MapPut("/goals/{metric}", async (string metric, GoalRequest request, HttpContext context, GoalService goals, CancellationToken ct) =>
        {
            var valid = GoalService.ValidMetric(metric);
            return Results.Ok(await goals.SetAsync(valid, request.UnitId == Guid.Empty ? throw new ApiInputException() : request.UnitId, GoalService.ValidTarget(valid, request.Target), Session(context).User.Id, ct));
        });
        municipal.MapDelete("/goals/{metric}", async (string metric, Guid? unitId, GoalService goals, CancellationToken ct) =>
        {
            await goals.RemoveAsync(GoalService.ValidMetric(metric), unitId == Guid.Empty ? throw new ApiInputException() : unitId, ct);
            return Results.NoContent();
        });
        municipal.MapGet("/alerts", async (Guid? unitId, string? region, AlertService alerts, CancellationToken ct) => Results.Ok(new { alerts = await alerts.ReadNetworkAsync(unitId, region, ct) }));

        var patient = app.MapGroup("/api").AddEndpointFilter(new SessionFilter("patient"));
        patient.MapGet("/patient/specialties", async (SpecialtyDao specialties, CancellationToken ct) => Results.Ok(new { specialties = await specialties.ListAsync(true, ct) }));
        patient.MapGet("/patient/units/{unitId:guid}/availability", async (Guid unitId, DateTimeOffset from, DateTimeOffset to, Guid? specialtyId, Guid? professionalId, HttpContext context, UnitAccessService access, SchedulingService service, CancellationToken ct) =>
            Results.Ok(new { slots = await access.ExecutePatientAsync(Session(context), unitId, _ => service.AvailabilityAsync(unitId, from, to, specialtyId, professionalId, true, ct), ct) }));
        patient.MapPost("/patient/units/{unitId:guid}/appointments", async (Guid unitId, PatientBookingRequest request, HttpContext context, SchedulingService service, CancellationToken ct) =>
            Results.Json(await service.PatientBookAsync(Session(context), unitId, request, ct), statusCode: StatusCodes.Status201Created))
            .AddEndpointFilter(new SchedulingRateLimitFilter(SchedulingRateLimiter.Booking));
        patient.MapPost("/patient/units/{unitId:guid}/appointments/{id:guid}/confirm", async (Guid unitId, Guid id, HttpContext context, UnitAccessService access, SchedulingDao dao, CancellationToken ct) =>
            Results.Ok(await access.ExecutePatientAsync(Session(context), unitId, cpf => dao.ConfirmAsync(unitId, cpf, id, ct, Session(context).User.Id), ct)))
            .AddEndpointFilter(new SchedulingRateLimitFilter(SchedulingRateLimiter.Confirmation));
    }

    /// <summary>Janela das listas de bloqueios: <c>to</c> depois de <c>from</c> e no máximo 366 dias (como as demais listas por período).</summary>
    private static DateTimeOffset ValidBlockWindow(DateTimeOffset from, DateTimeOffset to) => to > from && to - from <= TimeSpan.FromDays(366) ? to : throw new ApiInputException();

    /// <summary>Limite por conta autenticada das escritas do paciente na agenda; vem depois do SessionFilter do grupo.</summary>
    private sealed class SchedulingRateLimitFilter(string policy) : IEndpointFilter
    {
        public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext invocation, EndpointFilterDelegate next)
        {
            invocation.HttpContext.RequestServices.GetRequiredService<SchedulingRateLimiter>().Require(policy, Session(invocation.HttpContext).User.Id);
            return next(invocation);
        }
    }
}
