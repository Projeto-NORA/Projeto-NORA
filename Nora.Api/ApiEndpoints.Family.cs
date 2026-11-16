using Nora.Api.DAOs;
using Nora.Api.Models;
using Nora.Api.Services;

namespace Nora.Api;

/// <summary>
/// Família (o responsável age pelo dependente) e troca do e-mail da conta. As rotas delegadas espelham as do paciente sob
/// <c>/api/patient/dependents/{dependentId}/units/{unitId}/...</c>: <c>dependentId</c> é o id do vínculo aprovado, nunca o CPF. Todas passam por <see cref="UnitAccessService.ExecuteGuardianAsync{T}"/>.
/// </summary>
internal static partial class ApiEndpoints
{
    private static void MapFamilyEndpoints(this WebApplication app)
    {
        // ---- Responsável: pedidos e vínculos ----
        var patient = app.MapGroup("/api/patient").AddEndpointFilter(new SessionFilter("patient"));
        patient.MapGet("/dependents", async (HttpContext context, FamilyService family, CancellationToken ct) =>
            Results.Ok(new { dependents = await family.ListAsync(Session(context), ct) }))
            .AddEndpointFilter(new FamilyRateLimitFilter(FamilyRateLimiter.Read));
        patient.MapPost("/dependents", async (FamilyRequest request, HttpContext context, FamilyService family, CancellationToken ct) =>
            Results.Json(await family.RequestAsync(Session(context), request, ct), statusCode: StatusCodes.Status201Created))
            .AddEndpointFilter(new FamilyRateLimitFilter(FamilyRateLimiter.Request));
        patient.MapGet("/dependents/lightning-offers", async (HttpContext context, FamilyService family, CancellationToken ct) =>
            Results.Ok(new { dependents = await family.PendingOffersAsync(Session(context), ct) }))
            .AddEndpointFilter(new FamilyRateLimitFilter(FamilyRateLimiter.Read));
        patient.MapPost("/dependents/{id:guid}/cancel", async (Guid id, FamilyConfirmRequest request, HttpContext context, FamilyService family, CancellationToken ct) =>
            Results.Ok(await family.CancelAsync(Session(context), id, request, ct)))
            .AddEndpointFilter(new FamilyRateLimitFilter(FamilyRateLimiter.Write));
        patient.MapPost("/dependents/{id:guid}/end", async (Guid id, FamilyConfirmRequest request, HttpContext context, FamilyService family, CancellationToken ct) =>
            Results.Ok(await family.EndAsync(Session(context), id, request, ct)))
            .AddEndpointFilter(new FamilyRateLimitFilter(FamilyRateLimiter.Write));

        // ---- Responsável pelo dependente (rotas espelhadas das do paciente) ----
        const string d = "/dependents/{dependentId:guid}/units/{unitId:guid}";
        patient.MapGet(d + "/appointments", async (Guid dependentId, Guid unitId, DateTimeOffset from, DateTimeOffset to, HttpContext context, UnitAccessService access, AppointmentDao appointments, CancellationToken ct) =>
        {
            var session = Session(context);
            return Results.Ok(new { appointments = await access.ExecuteGuardianAsync(session, dependentId, unitId, cpf => appointments.ListForPatientAsync(unitId, from, to, cpf, session.User.Id, ct), ct) });
        }).AddEndpointFilter(new FamilyRateLimitFilter(FamilyRateLimiter.Read));
        patient.MapPost(d + "/appointments", async (Guid dependentId, Guid unitId, PatientBookingRequest request, HttpContext context, SchedulingService service, CancellationToken ct) =>
            Results.Json(await service.GuardianBookAsync(Session(context), dependentId, unitId, request, ct), statusCode: StatusCodes.Status201Created))
            .AddEndpointFilter(new SchedulingRateLimitFilter(SchedulingRateLimiter.Booking));
        patient.MapPost(d + "/appointments/{id:guid}/cancel", async (Guid dependentId, Guid unitId, Guid id, CancelAppointmentRequest request, HttpContext context, UnitAccessService access, AppointmentDao appointments, CancellationToken ct) =>
        {
            var session = Session(context);
            return Results.Ok(await access.ExecuteGuardianAsync(session, dependentId, unitId, cpf => appointments.ChangeStatusAsync(unitId, session.User.Id, id, new("cancelled", request.Confirmed), ct, cpf, session.User.Id), ct));
        }).AddEndpointFilter(new FamilyRateLimitFilter(FamilyRateLimiter.Write));
        patient.MapPost(d + "/appointments/{id:guid}/confirm", async (Guid dependentId, Guid unitId, Guid id, HttpContext context, UnitAccessService access, SchedulingDao dao, CancellationToken ct) =>
        {
            var session = Session(context);
            return Results.Ok(await access.ExecuteGuardianAsync(session, dependentId, unitId, cpf => dao.ConfirmAsync(unitId, cpf, id, ct, session.User.Id), ct));
        }).AddEndpointFilter(new SchedulingRateLimitFilter(SchedulingRateLimiter.Confirmation));
        patient.MapGet(d + "/availability", async (Guid dependentId, Guid unitId, DateTimeOffset from, DateTimeOffset to, Guid? specialtyId, Guid? professionalId, HttpContext context, UnitAccessService access, SchedulingService service, CancellationToken ct) =>
            Results.Ok(new { slots = await access.ExecuteGuardianAsync(Session(context), dependentId, unitId, _ => service.AvailabilityAsync(unitId, from, to, specialtyId, professionalId, true, ct), ct) }))
            .AddEndpointFilter(new FamilyRateLimitFilter(FamilyRateLimiter.Read));
        patient.MapGet(d + "/waiting-list", async (Guid dependentId, Guid unitId, HttpContext context, UnitAccessService access, UnitWaitingListDao waiting, CancellationToken ct) =>
            Results.Ok(new { entries = await access.ExecuteGuardianAsync(Session(context), dependentId, unitId, cpf => waiting.ListAsync(unitId, ct, cpf), ct) }))
            .AddEndpointFilter(new FamilyRateLimitFilter(FamilyRateLimiter.Read));
        patient.MapPost(d + "/waiting-list/{id:long}/cancel", async (Guid dependentId, Guid unitId, long id, CancelUnitWaitingEntryRequest request, HttpContext context, UnitAccessService access, UnitWaitingListDao waiting, UnitLightningService lightningService, CancellationToken ct) =>
        {
            var session = Session(context);
            await access.ExecuteGuardianAsync(session, dependentId, unitId, async cpf => { await waiting.CancelByPatientAsync(unitId, session.User.Id, cpf, id, request.Confirmed, ct, session.User.Id); return true; }, ct);
            await lightningService.TryAdvanceAsync(unitId, ct);
            return Results.NoContent();
        }).AddEndpointFilter(new FamilyRateLimitFilter(FamilyRateLimiter.Write));
        // Só leitura: quem redistribui as ofertas são as ações e o worker periódico.
        patient.MapGet(d + "/lightning-offers", async (Guid dependentId, Guid unitId, HttpContext context, UnitAccessService access, UnitLightningDao lightning, CancellationToken ct) =>
            Results.Ok(new { offers = await access.ExecuteGuardianAsync(Session(context), dependentId, unitId, cpf => lightning.ListForPatientAsync(unitId, cpf, ct), ct) }))
            .AddEndpointFilter(new FamilyRateLimitFilter(FamilyRateLimiter.Read));
        patient.MapPost(d + "/lightning-offers/{id:guid}/respond", async (Guid dependentId, Guid unitId, Guid id, UnitLightningResponse request, HttpContext context, UnitAccessService access, UnitLightningDao lightning, UnitLightningService lightningService, CancellationToken ct) =>
        {
            var session = Session(context);
            var result = await access.ExecuteGuardianAsync(session, dependentId, unitId, cpf => lightning.RespondAsync(unitId, session.User.Id, cpf, id, request.Accept, ct, session.User.Id), ct);
            if (!result.Accepted) await lightningService.TryAdvanceAsync(unitId, ct);
            return Results.Ok(result);
        }).AddEndpointFilter(new FamilyRateLimitFilter(FamilyRateLimiter.Write));
        patient.MapGet(d + "/health-records", async (Guid dependentId, Guid unitId, bool? includeVoided, HttpContext context, UnitAccessService access, HealthRecordDao records, CancellationToken ct) =>
            Results.Ok(await access.ExecuteGuardianAsync(Session(context), dependentId, unitId, cpf => records.ListAsync(unitId, cpf, includeVoided ?? false, false, false, ct), ct)))
            .AddEndpointFilter(new FamilyRateLimitFilter(FamilyRateLimiter.Read));
        patient.MapGet(d + "/attendance", async (Guid dependentId, Guid unitId, HttpContext context, UnitAccessService access, UnitRegistryReadService registry, CancellationToken ct) =>
            Results.Ok(await access.ExecuteGuardianAsync(Session(context), dependentId, unitId, cpf => registry.ReadOwnAttendanceAsync(unitId, cpf, ct), ct)))
            .AddEndpointFilter(new FamilyRateLimitFilter(FamilyRateLimiter.Read));

        // ---- Equipe da UBS: conferência dos pedidos ----
        var obs = app.MapGroup("/api/obs/units/{unitId:guid}/family-links").AddEndpointFilter(new SessionFilter("obs"));
        obs.MapGet("", async (Guid unitId, string? status, int? limit, string? after, HttpContext context, UnitAccessService access, FamilyService family, CancellationToken ct) =>
            Results.Ok(await access.ExecuteAsync(Session(context), unitId, () => family.ListForUnitAsync(unitId, status, limit, after, ct), ct)));
        obs.MapPost("/{id:guid}/approve", async (Guid unitId, Guid id, FamilyConfirmRequest request, HttpContext context, UnitAccessService access, FamilyService family, CancellationToken ct) =>
        {
            var session = Session(context);
            return Results.Ok(await access.ExecuteAsync(session, unitId, () => family.ApproveAsync(unitId, session.User.Id, id, request, ct), ct));
        });
        obs.MapPost("/{id:guid}/reject", async (Guid unitId, Guid id, FamilyRejectRequest request, HttpContext context, UnitAccessService access, FamilyService family, CancellationToken ct) =>
        {
            var session = Session(context);
            return Results.Ok(await access.ExecuteAsync(session, unitId, () => family.RejectAsync(unitId, session.User.Id, id, request, ct), ct));
        });
        obs.MapPost("/{id:guid}/end", async (Guid unitId, Guid id, FamilyConfirmRequest request, HttpContext context, UnitAccessService access, FamilyService family, CancellationToken ct) =>
        {
            var session = Session(context);
            return Results.Ok(await access.ExecuteAsync(session, unitId, () => family.EndAsync(unitId, session.User.Id, id, request, ct), ct));
        });

        // ---- Desativação do paciente na UBS: prévia do que será cancelado (o CPF vai no corpo, nunca na URL) ----
        app.MapGroup("/api").AddEndpointFilter(new SessionFilter("obs")).MapPost("/obs/units/{unitId:guid}/registry/deactivation-preview",
            async (Guid unitId, DeactivationPreviewRequest request, HttpContext context, UnitAccessService access, UnitPatientStatusService status, CancellationToken ct) =>
                Results.Ok(await access.ExecuteAsync(Session(context), unitId, () => status.PreviewDeactivationAsync(unitId, request.Cpf, ct), ct)));

        // ---- Troca do e-mail da conta guiada pela UBS ----
        patient.MapGet("/email-suggestion", async (HttpContext context, EmailChangeService service, CancellationToken ct) =>
            Results.Ok(new { suggestion = await service.ReadAsync(Session(context), ct) }))
            .AddEndpointFilter(new FamilyRateLimitFilter(FamilyRateLimiter.EmailRead));
        patient.MapPost("/email-suggestion/send-code", async (HttpContext context, EmailChangeService service, CancellationToken ct) =>
        {
            await service.SendCodeAsync(Session(context), ct);
            return Results.NoContent();
        }).AddEndpointFilter(new FamilyRateLimitFilter(FamilyRateLimiter.EmailCode));
        patient.MapPost("/email-suggestion/confirm", async (EmailSuggestionConfirmRequest request, HttpContext context, EmailChangeService service, CancellationToken ct) =>
            Results.Ok(await service.ConfirmAsync(Session(context), request, ct)))
            .AddEndpointFilter(new FamilyRateLimitFilter(FamilyRateLimiter.EmailConfirm));
        patient.MapPost("/email-suggestion/dismiss", async (HttpContext context, EmailChangeService service, CancellationToken ct) =>
        {
            await service.DismissAsync(Session(context), ct);
            return Results.NoContent();
        }).AddEndpointFilter(new FamilyRateLimitFilter(FamilyRateLimiter.EmailRead));
    }

    /// <summary>Limite por conta autenticada das rotas da Família e da troca de e-mail; vem depois do SessionFilter do grupo.</summary>
    private sealed class FamilyRateLimitFilter(string policy) : IEndpointFilter
    {
        public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext invocation, EndpointFilterDelegate next)
        {
            invocation.HttpContext.RequestServices.GetRequiredService<FamilyRateLimiter>().Require(policy, Session(invocation.HttpContext).User.Id);
            return next(invocation);
        }
    }
}
