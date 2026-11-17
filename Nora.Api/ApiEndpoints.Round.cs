using Nora.Api.DAOs;
using Nora.Api.Models;
using Nora.Api.Services;

namespace Nora.Api;

/// <summary>
/// Rodada final: computadores do OBS (P-014), "chamar próximo" da fila, atendimento ao público da UBS, aprovação das vagas reaproveitadas e a leitura do perfil da UBS pelo app.
/// Chamado por uma linha em <see cref="ApiEndpoints"/>.
/// </summary>
internal static partial class ApiEndpoints
{
    private static void MapRoundEndpoints(this WebApplication app)
    {
        // ---- Computadores do OBS: as duas primeiras rotas são públicas (o computador ainda não tem sessão) e limitadas por IP ----
        app.MapGet("/api/obs/devices/current", async (HttpContext context, ObsDeviceService devices, CancellationToken ct) =>
            Results.Ok(await devices.CurrentAsync(ObsDeviceCredentials.FromRequest(context.Request), ct))).RequireRateLimiting("obs-device-current");
        app.MapPost("/api/obs/devices/activate", async (ActivateObsDeviceRequest request, ObsDeviceService devices, CancellationToken ct) =>
            Results.Ok(await devices.ActivateAsync(request, ct))).RequireRateLimiting("obs-device-activate");

        var obs = app.MapGroup("/api").AddEndpointFilter(new SessionFilter("obs"));
        obs.MapGet("/obs/units/{unitId:guid}/devices", async (Guid unitId, HttpContext context, SchedulingAccessService access, ObsDeviceService devices, CancellationToken ct) =>
            Results.Ok(await access.ExecuteAdministratorAsync(Session(context), unitId, connection => devices.ListAsync(connection, unitId, ObsDeviceCredentials.FromRequest(context.Request), ct), ct)));
        obs.MapPost("/obs/units/{unitId:guid}/devices", async (Guid unitId, CreateObsDeviceRequest request, HttpContext context, SchedulingAccessService access, ObsDeviceService devices, CancellationToken ct) =>
        {
            var session = Session(context);
            var (device, credential, code) = await ObsDeviceService.RetryOnDeadlockAsync(() => access.ExecuteAdministratorAsync(session, unitId, connection => devices.CreateAsync(connection, session, unitId, request, ObsDeviceCredentials.FromRequest(context.Request), ct), ct));
            return credential is not null
                ? Results.Json(new ObsDeviceWithCredential(device, credential), statusCode: StatusCodes.Status201Created)
                : Results.Json(new ObsDeviceWithCode(device, code!.ActivationCode, code.CodeExpiresAt), statusCode: StatusCodes.Status201Created);
        }).AddEndpointFilter(new AccountRateLimitFilter(AccountRateLimiter.ObsDevices));
        obs.MapPost("/obs/units/{unitId:guid}/devices/{id:guid}/activation-code", async (Guid unitId, Guid id, HttpContext context, SchedulingAccessService access, ObsDeviceService devices, CancellationToken ct) =>
        {
            var session = Session(context);
            return Results.Ok(await ObsDeviceService.RetryOnDeadlockAsync(() => access.ExecuteAdministratorAsync(session, unitId, connection => devices.IssueCodeAsync(connection, session, unitId, id, ct), ct)));
        }).AddEndpointFilter(new AccountRateLimitFilter(AccountRateLimiter.ObsDevices));
        obs.MapPost("/obs/units/{unitId:guid}/devices/{id:guid}/revoke", async (Guid unitId, Guid id, RevokeObsDeviceRequest request, HttpContext context, SchedulingAccessService access, ObsDeviceService devices, CancellationToken ct) =>
        {
            var session = Session(context);
            return Results.Ok(new ObsDeviceEnvelope(await ObsDeviceService.RetryOnDeadlockAsync(() => access.ExecuteAdministratorAsync(session, unitId, connection => devices.RevokeAsync(connection, session, unitId, id, request, ObsDeviceCredentials.FromRequest(context.Request), ct), ct))));
        }).AddEndpointFilter(new AccountRateLimitFilter(AccountRateLimiter.ObsDevices));

        // ---- Fila: chamar próximo (oferece um horário livre da grade ao primeiro elegível da especialidade) ----
        obs.MapGet("/obs/units/{unitId:guid}/waiting-list/next", async (Guid unitId, Guid? specialtyId, DateTimeOffset? startsAt, Guid? professionalId, HttpContext context, CallNextService service, CancellationToken ct) =>
            Results.Ok(await service.PreviewAsync(Session(context), unitId, specialtyId, startsAt, professionalId, ct)));
        obs.MapPost("/obs/units/{unitId:guid}/waiting-list/call-next", async (Guid unitId, CallNextRequest request, HttpContext context, CallNextService service, CancellationToken ct) =>
            Results.Json(await service.CallAsync(Session(context), unitId, request, ct), statusCode: StatusCodes.Status201Created));

        // ---- Atendimento ao público da UBS (o administrador grava; a equipe lê) ----
        obs.MapGet("/obs/units/{unitId:guid}/profile", async (Guid unitId, HttpContext context, UnitAccessService access, UnitProfileService profile, CancellationToken ct) =>
            Results.Ok(await access.ExecuteAsync(Session(context), unitId, () => profile.ReadForObsAsync(unitId, ct), ct)));
        obs.MapPut("/obs/units/{unitId:guid}/profile", async (Guid unitId, UnitProfileRequest request, HttpContext context, SchedulingAccessService access, UnitProfileService profile, CancellationToken ct) =>
        {
            var session = Session(context);
            return Results.Ok(await access.ExecuteAdministratorAsync(session, unitId, connection => profile.ReplaceAsync(connection, session, unitId, request, ct), ct));
        });

        // ---- Aprovação das vagas reaproveitadas ----
        obs.MapPut("/obs/units/{unitId:guid}/slot-approval", async (Guid unitId, SlotApprovalRequest request, HttpContext context, SchedulingAccessService access, SlotApprovalService approval, CancellationToken ct) =>
        {
            var session = Session(context);
            return Results.Ok(await access.ExecuteAdministratorAsync(session, unitId, connection => approval.SetAsync(connection, session, unitId, request, ct), ct));
        });
        obs.MapPost("/obs/units/{unitId:guid}/lightning-slots/{slotId:guid}/approve", async (Guid unitId, Guid slotId, ConfirmedRequest request, HttpContext context, UnitAccessService access, UnitLightningDao lightning, UnitLightningService lightningService, CancellationToken ct) =>
        {
            var session = Session(context);
            var slot = await access.ExecuteAsync(session, unitId, () => lightning.ApproveAsync(unitId, session.User.Id, slotId, request.Confirmed, ct), ct);
            await lightningService.TryAdvanceAsync(unitId, ct);
            return Results.Ok(slot);
        });
        obs.MapPost("/obs/units/{unitId:guid}/lightning-slots/{slotId:guid}/discard", async (Guid unitId, Guid slotId, ConfirmedRequest request, HttpContext context, UnitAccessService access, UnitLightningDao lightning, CancellationToken ct) =>
        {
            var session = Session(context);
            return Results.Ok(await access.ExecuteAsync(session, unitId, () => lightning.DiscardAsync(unitId, session.User.Id, slotId, request.Confirmed, ct), ct));
        });

        // ---- App: atendimento ao público da UBS vinculada (paciente) e da UBS do dependente (responsável) ----
        var patient = app.MapGroup("/api").AddEndpointFilter(new SessionFilter("patient"));
        patient.MapGet("/patient/units/{unitId:guid}/about", async (Guid unitId, HttpContext context, UnitAccessService access, UnitProfileService profile, CancellationToken ct) =>
            Results.Ok(await access.ExecutePatientAsync(Session(context), unitId, _ => profile.ReadAsync(unitId, ct), ct)));
        patient.MapGet("/patient/dependents/{dependentId:guid}/units/{unitId:guid}/about", async (Guid dependentId, Guid unitId, HttpContext context, UnitAccessService access, UnitProfileService profile, CancellationToken ct) =>
            Results.Ok(await access.ExecuteGuardianAsync(Session(context), dependentId, unitId, _ => profile.ReadAsync(unitId, ct), ct)));
    }
}
