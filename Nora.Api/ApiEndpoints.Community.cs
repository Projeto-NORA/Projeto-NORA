using Nora.Api.DAOs;
using Nora.Api.Models;
using Nora.Api.Services;

namespace Nora.Api;

/// <summary>Rotas de comunicação (comunicados) e da Jornada (exames e vacinas). Chamado por uma linha em <see cref="ApiEndpoints"/>.</summary>
internal static class CommunityEndpoints
{
    /// <param name="sessionFilter">Fábrica do filtro de sessão do projeto, por escopo (obs, municipal, patient).</param>
    public static void MapCommunityEndpoints(this WebApplication app, Func<string, IEndpointFilter> sessionFilter)
    {
        // ---- Secretaria: comunicados ----
        var municipal = app.MapGroup("/api/municipal/announcements").AddEndpointFilter(sessionFilter("municipal"));
        municipal.MapPost("", async (CreateAnnouncementRequest request, HttpContext context, AnnouncementService service, CancellationToken ct) =>
            Results.Json(await service.CreateAsync(Session(context).User.Id, request, ct), statusCode: StatusCodes.Status201Created))
            .AddEndpointFilter(new AccountLimitFilter(CommunityRateLimiter.AnnouncementPublish));
        municipal.MapGet("", async (long? before, int? limit, HttpContext context, AnnouncementService service, CancellationToken ct) =>
            Results.Ok(await service.ListOwnAsync(Session(context).User.Id, before, limit, ct)));
        municipal.MapPost("/{id:guid}/close", async (Guid id, CloseAnnouncementRequest request, HttpContext context, AnnouncementService service, CancellationToken ct) =>
        {
            await service.CloseAsync(Session(context).User.Id, id, request, ct);
            return Results.NoContent();
        }).AddEndpointFilter(new AccountLimitFilter(CommunityRateLimiter.AnnouncementPublish));
        municipal.MapGet("/{id:guid}/reads", async (Guid id, HttpContext context, AnnouncementService service, CancellationToken ct) =>
            Results.Ok(await service.ReadsAsync(Session(context).User.Id, id, ct)));

        // ---- Equipe (OBS): comunicados ----
        var obsAnnouncements = app.MapGroup("/api/obs/announcements").AddEndpointFilter(sessionFilter("obs"));
        obsAnnouncements.MapGet("", async (long? after, int? limit, HttpContext context, AnnouncementService service, CancellationToken ct) =>
            Results.Ok(await service.ListForAccountAsync(Session(context).User.Id, true, after, limit, ct)))
            .AddEndpointFilter(new AccountLimitFilter(CommunityRateLimiter.AnnouncementPoll));
        obsAnnouncements.MapPost("/{id:guid}/read", async (Guid id, HttpContext context, AnnouncementService service, CancellationToken ct) =>
        {
            await service.MarkReadAsync(Session(context).User.Id, true, id, ct);
            return Results.NoContent();
        }).AddEndpointFilter(new AccountLimitFilter(CommunityRateLimiter.AnnouncementRead));

        // ---- Equipe (OBS): Jornada do paciente. O CPF vai sempre no corpo, nunca na URL. ----
        var records = app.MapGroup("/api/obs/units/{unitId:guid}/health-records").AddEndpointFilter(sessionFilter("obs"));
        records.MapPost("/query", async (Guid unitId, HealthRecordsQueryRequest request, HttpContext context, UnitAccessService access, HealthRecordDao dao, CancellationToken ct) =>
            Results.Ok(await access.ExecuteAsync(Session(context), unitId, () => dao.ListAsync(unitId, HealthRecordDao.Cpf(request.Cpf), request.IncludeVoided, true, true, ct), ct)));
        records.MapPost("/exams", async (Guid unitId, CreateExamRequest request, HttpContext context, UnitAccessService access, HealthRecordDao dao, CancellationToken ct) =>
        {
            var session = Session(context);
            return Results.Json(await access.ExecuteAsync(session, unitId, () => dao.CreateExamAsync(unitId, session.User.Id, request, ct), ct), statusCode: StatusCodes.Status201Created);
        });
        records.MapPatch("/exams/{id:guid}/status", async (Guid unitId, Guid id, ExamStatusRequest request, HttpContext context, UnitAccessService access, HealthRecordDao dao, CancellationToken ct) =>
        {
            var session = Session(context);
            return Results.Ok(await access.ExecuteAsync(session, unitId, () => dao.ChangeExamStatusAsync(unitId, session.User.Id, id, request, ct), ct));
        });
        records.MapPost("/exams/{id:guid}/void", async (Guid unitId, Guid id, VoidHealthRecordRequest request, HttpContext context, UnitAccessService access, HealthRecordDao dao, CancellationToken ct) =>
        {
            var session = Session(context);
            return Results.Ok(await access.ExecuteAsync(session, unitId, () => dao.VoidExamAsync(unitId, session.User.Id, id, request, ct), ct));
        });
        records.MapPost("/vaccines", async (Guid unitId, CreateVaccineRequest request, HttpContext context, UnitAccessService access, HealthRecordDao dao, CancellationToken ct) =>
        {
            var session = Session(context);
            return Results.Json(await access.ExecuteAsync(session, unitId, () => dao.CreateVaccineAsync(unitId, session.User.Id, request, ct), ct), statusCode: StatusCodes.Status201Created);
        });
        records.MapPost("/vaccines/{id:guid}/void", async (Guid unitId, Guid id, VoidHealthRecordRequest request, HttpContext context, UnitAccessService access, HealthRecordDao dao, CancellationToken ct) =>
        {
            var session = Session(context);
            return Results.Ok(await access.ExecuteAsync(session, unitId, () => dao.VoidVaccineAsync(unitId, session.User.Id, id, request, ct), ct));
        });

        // ---- Paciente: comunicados e Jornada (somente leitura) ----
        var patient = app.MapGroup("/api/patient").AddEndpointFilter(sessionFilter("patient"));
        patient.MapGet("/announcements", async (long? after, int? limit, HttpContext context, AnnouncementService service, CancellationToken ct) =>
            Results.Ok(await service.ListForAccountAsync(Session(context).User.Id, false, after, limit, ct)))
            .AddEndpointFilter(new AccountLimitFilter(CommunityRateLimiter.AnnouncementPoll));
        patient.MapPost("/announcements/{id:guid}/read", async (Guid id, HttpContext context, AnnouncementService service, CancellationToken ct) =>
        {
            await service.MarkReadAsync(Session(context).User.Id, false, id, ct);
            return Results.NoContent();
        }).AddEndpointFilter(new AccountLimitFilter(CommunityRateLimiter.AnnouncementRead));
        patient.MapGet("/units/{unitId:guid}/health-records", async (Guid unitId, bool? includeVoided, HttpContext context, HealthRecordService service, CancellationToken ct) =>
            Results.Ok(await service.ListOwnAsync(Session(context), unitId, includeVoided ?? false, ct)))
            .AddEndpointFilter(new AccountLimitFilter(CommunityRateLimiter.HealthRecordsView));
        patient.MapGet("/health-records", async (bool? includeVoided, HttpContext context, HealthRecordService service, CancellationToken ct) =>
            Results.Ok(new { units = await service.ListOwnAllAsync(Session(context), includeVoided ?? false, ct) }))
            .AddEndpointFilter(new AccountLimitFilter(CommunityRateLimiter.HealthRecordsView));
    }

    private static SessionInfo Session(HttpContext context) => (SessionInfo)context.Items[typeof(SessionInfo)]!;

    /// <summary>Limite por conta autenticada; precisa vir depois do filtro de sessão (os do grupo rodam antes dos do endpoint).</summary>
    private sealed class AccountLimitFilter(string policy) : IEndpointFilter
    {
        public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext invocation, EndpointFilterDelegate next)
        {
            invocation.HttpContext.RequestServices.GetRequiredService<CommunityRateLimiter>().Require(policy, Session(invocation.HttpContext).User.Id);
            return next(invocation);
        }
    }
}
