using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using Nora.Desktop.Models;

namespace Nora.Desktop.Checks;

internal static partial class Program
{
    /// <summary>
    /// Comunicados, Jornada (exames e vacinas), Família e prévia da desativação da fixture (docs/api.md, "Comunicação e Jornada" e "Família, e-mail da conta e manutenção").
    /// Repete as regras do servidor que a tela precisa respeitar: texto puro, cursor incremental, transições de exame, anulação com motivo, decisão de família com 409 por estado.
    /// </summary>
    private sealed partial class ApiFixture
    {
        /// <summary>Comunicado como o servidor o guarda; a leitura da conta de teste (OBS) e as leituras de outras contas são separadas.</summary>
        public sealed class FixtureAnnouncement
        {
            public Guid Id { get; init; } = Guid.NewGuid();
            public long Sequence { get; init; }
            public string Title { get; set; } = "";
            public string Body { get; set; } = "";
            public string Audience { get; set; } = "staff";
            public string Scope { get; set; } = "all";
            public string[] Regions { get; set; } = [];
            public Guid[] UnitIds { get; set; } = [];
            public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
            public DateTimeOffset? ExpiresAt { get; set; }
            public DateTimeOffset? ClosedAt { get; set; }
            public DateTimeOffset? OperatorReadAt { get; set; }
            public List<AnnouncementReader> OtherStaff { get; } = [];
            public long PatientReads { get; set; }
        }

        private long announcementSequence;
        public List<FixtureAnnouncement> Announcements { get; } = [];
        /// <summary>Consulta (query string) de cada leitura de comunicados da equipe; serve para contar chamadas completas e incrementais.</summary>
        public List<System.Collections.Specialized.NameValueCollection> AnnouncementFeedQueries { get; } = [];
        public bool FailAnnouncementFeed { get; set; }
        public bool FailDeactivationPreview { get; set; }
        public bool FailHealthRecords { get; set; }
        /// <summary>Leituras dos pedidos de família respondem 500 (uma base sem a migração, por exemplo).</summary>
        public bool FailFamily { get; set; }
        public int HoldNextHealthQueries { get; set; }
        public int HoldNextHealthWrites { get; set; }
        public List<TaskCompletionSource<bool>> HeldHealthWrites { get; } = [];
        public List<TaskCompletionSource<bool>> HeldHealthQueries { get; } = [];

        public FixtureAnnouncement AddAnnouncement(string title, string body, string audience = "staff", string scope = "all", string[]? regions = null, Guid[]? unitIds = null, DateTimeOffset? expiresAt = null)
        {
            var item = new FixtureAnnouncement { Sequence = ++announcementSequence, Title = title, Body = body, Audience = audience, Scope = scope, Regions = regions ?? [], UnitIds = unitIds ?? [], ExpiresAt = expiresAt };
            Announcements.Add(item); return item;
        }

        private bool VisibleToOperator(FixtureAnnouncement item, DateTimeOffset now)
        {
            if (item.Audience == "patients" || item.ClosedAt is not null || (item.ExpiresAt is { } expires && expires <= now)) return false;
            var granted = units.Where(unit => Granted is null || Granted.Contains(unit.Id)).ToArray();
            return item.Scope switch
            {
                "all" => granted.Length > 0,
                "region" => granted.Any(unit => item.Regions.Contains(unit.Region, StringComparer.OrdinalIgnoreCase)),
                "units" => granted.Any(unit => item.UnitIds.Contains(unit.Id)),
                _ => false
            };
        }

        private static AnnouncementSummary Summary(FixtureAnnouncement item, DateTimeOffset now)
        {
            var status = item.ClosedAt is not null ? "closed" : item.ExpiresAt is { } expires && expires <= now ? "expired" : "active";
            var staffReads = (item.OperatorReadAt is null ? 0 : 1) + item.OtherStaff.Count;
            return new(item.Id, item.Sequence, item.Title, item.Body, item.Audience, item.Scope, item.Regions, item.UnitIds, item.CreatedAt, item.ExpiresAt, item.ClosedAt, status, item.Audience == "patients" ? 0 : staffReads, item.Audience == "staff" ? 0 : item.PatientReads);
        }

        private static bool PlainLine(string? value, int min, int max) => value is not null && value.Trim().Length >= min && value.Length <= max && !value.Any(char.IsControl);

        /// <summary>Rotas globais de comunicados: a equipe (OBS) lê e marca como lido; a Secretaria publica, lista, vê leituras e encerra.</summary>
        private async Task<HttpResponseMessage?> HandleGlobalCommunityAsync(HttpRequestMessage request, string path, CancellationToken cancellationToken)
        {
            var method = request.Method; var now = DateTimeOffset.UtcNow;
            var query = System.Web.HttpUtility.ParseQueryString(request.RequestUri!.Query);
            if (path == "/api/obs/announcements" && method == HttpMethod.Get)
            {
                AnnouncementFeedQueries.Add(query);
                if (FailAnnouncementFeed) return Problem(HttpStatusCode.InternalServerError, "internal-error");
                var after = query["after"] is { } afterText ? long.Parse(afterText, CultureInfo.InvariantCulture) : 0;
                var limit = query["limit"] is { } limitText ? int.Parse(limitText, CultureInfo.InvariantCulture) : 50;
                if (after < 0 || limit is < 1 or > 100) return Problem(HttpStatusCode.BadRequest, "invalid-input");
                var visible = Announcements.Where(item => item.Sequence > after && VisibleToOperator(item, now)).OrderBy(item => item.Sequence).ToList();
                var page = visible.Take(limit).ToList();
                var hasMore = visible.Count > page.Count;
                var maxSequence = Announcements.Count == 0 ? 0 : Announcements.Max(item => item.Sequence);
                var cursor = hasMore ? page[^1].Sequence : maxSequence;
                return Reply(new ReceivedAnnouncementPage([.. page.Select(item => new ReceivedAnnouncement(item.Id, item.Sequence, item.Title, item.Body, item.CreatedAt, item.ExpiresAt, item.OperatorReadAt is not null, item.OperatorReadAt))], cursor, hasMore));
            }
            var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts is ["api", "obs", "announcements", var readText, "read"] && method == HttpMethod.Post && Guid.TryParse(readText, out var readId))
            {
                var item = Announcements.FirstOrDefault(entry => entry.Id == readId);
                if (item is null || !VisibleToOperator(item, now)) return Problem(HttpStatusCode.NotFound, "not-found");
                item.OperatorReadAt ??= now;
                return new(HttpStatusCode.NoContent);
            }
            if (path == "/api/municipal/announcements" && method == HttpMethod.Get)
            {
                var before = query["before"] is { } beforeText ? long.Parse(beforeText, CultureInfo.InvariantCulture) : long.MaxValue;
                var limit = query["limit"] is { } limitText ? int.Parse(limitText, CultureInfo.InvariantCulture) : 50;
                var ordered = Announcements.Where(item => item.Sequence < before).OrderByDescending(item => item.Sequence).ToList();
                var page = ordered.Take(limit).ToList();
                return Reply(new AnnouncementSummaryPage([.. page.Select(item => Summary(item, now))], ordered.Count > page.Count ? page[^1].Sequence : null));
            }
            if (path == "/api/municipal/announcements" && method == HttpMethod.Post)
            {
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
                var root = body.RootElement;
                var title = Text(root, "title") ?? ""; var text = Text(root, "body") ?? ""; var audience = Text(root, "audience"); var scope = Text(root, "scope");
                DateTimeOffset? expires = root.TryGetProperty("expiresAt", out var expiresElement) && expiresElement.ValueKind == JsonValueKind.String ? expiresElement.GetDateTimeOffset() : null;
                string[] regions = root.TryGetProperty("regions", out var regionElement) && regionElement.ValueKind == JsonValueKind.Array ? [.. regionElement.EnumerateArray().Select(item => item.GetString() ?? "")] : [];
                Guid[] unitIds = root.TryGetProperty("unitIds", out var unitElement) && unitElement.ValueKind == JsonValueKind.Array ? [.. unitElement.EnumerateArray().Select(item => item.GetGuid())] : [];
                var valid = title.Trim().Length > 0 && title.Length <= 120 && !title.Any(char.IsControl) && text.Trim().Length > 0 && text.Length <= 2000 && !text.Any(c => char.IsControl(c) && c is not ('\n' or '\r' or '\t'))
                    && audience is "staff" or "patients" or "both" && scope is "all" or "region" or "units" && (expires is null || expires > now)
                    && (scope != "all" || (regions.Length == 0 && unitIds.Length == 0)) && (scope != "region" || (regions.Length > 0 && regions.Length <= 50 && unitIds.Length == 0 && regions.All(region => units.Any(unit => string.Equals(unit.Region, region, StringComparison.OrdinalIgnoreCase)))))
                    && (scope != "units" || (unitIds.Length > 0 && regions.Length == 0 && unitIds.All(id => units.Any(unit => unit.Id == id))));
                if (!valid) return Problem(HttpStatusCode.BadRequest, "invalid-input");
                AnnouncementBodies.Add(root.Clone());
                var item = AddAnnouncement(title, text, audience!, scope!, [.. regions.Select(region => units.First(unit => string.Equals(unit.Region, region, StringComparison.OrdinalIgnoreCase)).Region)], unitIds, expires);
                return Created(Summary(item, now));
            }
            if (parts is ["api", "municipal", "announcements", var idText, var action] && Guid.TryParse(idText, out var id))
            {
                var item = Announcements.FirstOrDefault(entry => entry.Id == id);
                if (item is null) return Problem(HttpStatusCode.NotFound, "not-found");
                if (action == "reads" && method == HttpMethod.Get)
                {
                    var staff = new List<AnnouncementReader>(item.OtherStaff);
                    if (item.OperatorReadAt is { } at) staff.Add(new(UseWorkspaceIdentity ? "Operador Fixture" : "Paciente Fixture", at));
                    return Reply(new AnnouncementReads(item.Audience == "patients" ? 0 : staff.Count, item.Audience == "staff" ? 0 : item.PatientReads, item.Audience == "patients" ? [] : [.. staff.OrderBy(reader => reader.ReadAt)]));
                }
                if (action == "close" && method == HttpMethod.Post)
                {
                    using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
                    if (!body.RootElement.TryGetProperty("confirmed", out var confirmed) || confirmed.ValueKind != JsonValueKind.True) return Problem(HttpStatusCode.BadRequest, "invalid-input");
                    if (item.ClosedAt is not null) return Problem(HttpStatusCode.Conflict, "conflict");
                    item.ClosedAt = now;
                    return new(HttpStatusCode.NoContent);
                }
            }
            return null;
        }

        /// <summary>Corpos (JSON) dos comunicados criados pela Secretaria: o teste confere que o texto foi enviado exatamente como digitado.</summary>
        public List<JsonElement> AnnouncementBodies { get; } = [];

        // ---- Jornada ----

        private sealed class HealthState
        {
            public List<ExamInfo> Exams { get; } = [];
            public List<VaccineInfo> Vaccines { get; } = [];
        }
        private readonly Dictionary<(Guid Unit, string Cpf), HealthState> healthStates = [];
        private HealthState Health(Guid unitId, string cpf) => healthStates.TryGetValue((unitId, cpf), out var state) ? state : healthStates[(unitId, cpf)] = new();
        public IReadOnlyList<ExamInfo> ExamsOf(Guid unitId, string cpf) => Health(unitId, cpf).Exams;
        public IReadOnlyList<VaccineInfo> VaccinesOf(Guid unitId, string cpf) => Health(unitId, cpf).Vaccines;
        public ExamInfo AddExam(Guid unitId, string cpf, string name, string status = "requested", DateOnly? performedOn = null, string? voidReason = null)
        {
            var item = new ExamInfo(Guid.NewGuid(), name, status, DateTimeOffset.UtcNow, performedOn, null, status == "voided" ? DateTimeOffset.UtcNow : null, voidReason);
            Health(unitId, cpf).Exams.Add(item); return item;
        }
        /// <summary>Muda um exame no "servidor", como outra pessoa faria em outro computador.</summary>
        public void ChangeExam(Guid unitId, string cpf, Guid id, Func<ExamInfo, ExamInfo> change)
        {
            var list = Health(unitId, cpf).Exams; var index = list.FindIndex(item => item.Id == id); list[index] = change(list[index]);
        }
        public VaccineInfo AddVaccine(Guid unitId, string cpf, string name, string dose, DateOnly appliedOn, string status = "applied", string? voidReason = null)
        {
            var item = new VaccineInfo(Guid.NewGuid(), name, dose, appliedOn, status, null, status == "voided" ? DateTimeOffset.UtcNow : null, voidReason);
            Health(unitId, cpf).Vaccines.Add(item); return item;
        }

        // ---- Família ----

        public List<(Guid Unit, FamilyLink Link)> FamilyLinks { get; } = [];
        public List<(string Action, JsonElement Body)> FamilyBodies { get; } = [];
        public FamilyLink AddFamilyRequest(Guid unitId, string guardianName, string guardianCpf, string dependentCpf, string status = "pending", DateTimeOffset? requestedAt = null)
        {
            var name = State(unitId).Registry.FirstOrDefault(record => record.Cpf == dependentCpf)?.FullName;
            var link = new FamilyLink(Guid.NewGuid(), status, guardianName, guardianCpf, dependentCpf, name, requestedAt ?? DateTimeOffset.UtcNow, null, null, null);
            FamilyLinks.Add((unitId, link)); return link;
        }
        public FamilyLink? FamilyLinkOf(Guid id) => FamilyLinks.Where(entry => entry.Link.Id == id).Select(entry => entry.Link).FirstOrDefault();

        /// <summary>Rotas por UBS da Jornada, da Família e da prévia da desativação; nulo para o que a fixture antiga já trata.</summary>
        private HttpResponseMessage? HandleUnitCommunity(HttpRequestMessage request, Guid unitId, string[] rest, JsonDocument? payload)
        {
            var method = request.Method; var now = DateTimeOffset.UtcNow;
            var query = System.Web.HttpUtility.ParseQueryString(request.RequestUri!.Query);
            JsonElement body = payload?.RootElement ?? default;
            var state = State(unitId);
            if (rest is ["registry", "deactivation-preview"] && method == HttpMethod.Post)
            {
                if (FailDeactivationPreview) return Problem(HttpStatusCode.InternalServerError, "internal-error");
                var cpf = Text(body, "cpf") ?? "";
                if (!Nora.Desktop.Services.BrazilianDataValidator.IsValidCpf(cpf)) return Problem(HttpStatusCode.BadRequest, "invalid-input");
                if (state.Registry.All(record => record.Cpf != cpf)) return Problem(HttpStatusCode.NotFound, "not-found");
                return Reply(new DeactivationPreview(state.Appointments.Count(item => item.Cpf == cpf && item.Status is "scheduled" or "called" && item.StartsAt > now), state.Waiting.Count(item => item.Cpf == cpf && item.IsActive)));
            }
            if (rest is ["family-links"] && method == HttpMethod.Get)
            {
                if (FailFamily) return Problem(HttpStatusCode.InternalServerError, "internal-error");
                var status = query["status"] ?? "pending";
                if (status is not ("pending" or "approved")) return Problem(HttpStatusCode.BadRequest, "invalid-input");
                return Reply(new FamilyLinkList([.. FamilyLinks.Where(entry => entry.Unit == unitId && entry.Link.Status == status).Select(entry => entry.Link).OrderBy(link => link.RequestedAt)]));
            }
            if (rest is ["family-links", var linkText, var action] && method == HttpMethod.Post && Guid.TryParse(linkText, out var linkId) && action is "approve" or "reject" or "end")
            {
                var index = FamilyLinks.FindIndex(entry => entry.Unit == unitId && entry.Link.Id == linkId);
                if (index < 0) return Problem(HttpStatusCode.NotFound, "not-found");
                if (!body.TryGetProperty("confirmed", out var confirmed) || confirmed.ValueKind != JsonValueKind.True) return Problem(HttpStatusCode.BadRequest, "invalid-input");
                FamilyBodies.Add((action, body.Clone()));
                var link = FamilyLinks[index].Link;
                var reason = action == "reject" ? Text(body, "reason") : null;
                if (action == "reject" && reason is not null && (reason.Trim().Length is < 1 or > 280 || reason.Any(char.IsControl))) return Problem(HttpStatusCode.BadRequest, "invalid-input");
                if (action == "end" ? link.Status != "approved" : link.Status != "pending") return Problem(HttpStatusCode.Conflict, "family-link-closed");
                if (action == "approve" && state.Registry.FirstOrDefault(record => record.Cpf == link.DependentCpf) is not { IsActive: true }) return Problem(HttpStatusCode.Conflict, "dependent-inactive");
                var updated = action switch
                {
                    "approve" => link with { Status = "approved", DecidedByName = "Operador Fixture", DecidedAt = now },
                    "reject" => link with { Status = "rejected", DecidedByName = "Operador Fixture", DecidedAt = now, DecisionReason = reason },
                    _ => link with { Status = "ended" }
                };
                FamilyLinks[index] = (unitId, updated);
                return Reply(updated);
            }
            if (rest is not ["health-records", ..]) return null;
            if (FailHealthRecords && method == HttpMethod.Post && rest is ["health-records", "query"]) return Problem(HttpStatusCode.InternalServerError, "internal-error");
            if (UnauthorizedReads && rest is ["health-records", "query"]) return new(HttpStatusCode.Unauthorized);
            if (rest is ["health-records", "query"] && method == HttpMethod.Post)
            {
                var cpf = Text(body, "cpf") ?? "";
                if (!Nora.Desktop.Services.BrazilianDataValidator.IsValidCpf(cpf)) return Problem(HttpStatusCode.BadRequest, "invalid-input");
                if (state.Registry.All(record => record.Cpf != cpf)) return Problem(HttpStatusCode.NotFound, "not-found");
                var includeVoided = body.TryGetProperty("includeVoided", out var include) && include.ValueKind == JsonValueKind.True;
                var health = Health(unitId, cpf);
                return Reply(new HealthRecords(
                    [.. health.Exams.Where(item => includeVoided || !item.IsVoided).OrderByDescending(item => item.PerformedOn?.ToDateTime(TimeOnly.MinValue) ?? item.RequestedAt.UtcDateTime).ThenByDescending(item => item.RequestedAt)],
                    [.. health.Vaccines.Where(item => includeVoided || !item.IsVoided).OrderByDescending(item => item.AppliedOn)]));
            }
            if (rest is ["health-records", "exams"] && method == HttpMethod.Post)
            {
                var cpf = Text(body, "cpf") ?? ""; var name = Text(body, "name"); var note = Text(body, "note");
                if (!Nora.Desktop.Services.BrazilianDataValidator.IsValidCpf(cpf) || !PlainLine(name, 1, 160) || (note is not null && note.Trim().Length > 0 && !PlainLine(note, 1, 280))) return Problem(HttpStatusCode.BadRequest, "invalid-input");
                if (state.Registry.FirstOrDefault(record => record.Cpf == cpf) is not { IsActive: true }) return Problem(HttpStatusCode.NotFound, "not-found");
                var item = new ExamInfo(Guid.NewGuid(), name!.Trim(), "requested", now, null, string.IsNullOrWhiteSpace(note) ? null : note.Trim(), null, null);
                Health(unitId, cpf).Exams.Add(item); HealthBodies.Add(("exam-create", body.Clone()));
                return Created(item);
            }
            if (rest is ["health-records", "exams", var examText, "status"] && method == HttpMethod.Patch && Guid.TryParse(examText, out var examId))
            {
                var found = healthStates.Where(entry => entry.Key.Unit == unitId).Select(entry => (entry.Key.Cpf, State: entry.Value)).FirstOrDefault(entry => entry.State.Exams.Any(exam => exam.Id == examId));
                if (found.State is null) return Problem(HttpStatusCode.NotFound, "not-found");
                var status = Text(body, "status"); var performed = Text(body, "performedOn");
                DateOnly? performedOn = performed is not null && DateOnly.TryParseExact(performed, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed) ? parsed : null;
                if (status is not ("done" or "cancelled") || (status == "done") != (performedOn is not null) || (performed is not null && performedOn is null)
                    || (performedOn is { } date && (date < new DateOnly(1900, 1, 1) || date > DateOnly.FromDateTime(now.UtcDateTime).AddDays(1)))) return Problem(HttpStatusCode.BadRequest, "invalid-input");
                var index = found.State.Exams.FindIndex(exam => exam.Id == examId);
                if (found.State.Exams[index].Status != "requested") return Problem(HttpStatusCode.Conflict, "conflict");
                found.State.Exams[index] = found.State.Exams[index] with { Status = status, PerformedOn = performedOn }; HealthBodies.Add(("exam-status", body.Clone()));
                return Reply(found.State.Exams[index]);
            }
            if (rest is ["health-records", "vaccines"] && method == HttpMethod.Post)
            {
                var cpf = Text(body, "cpf") ?? ""; var name = Text(body, "name"); var note = Text(body, "note"); var dose = Text(body, "dose"); var applied = Text(body, "appliedOn");
                if (!Nora.Desktop.Services.BrazilianDataValidator.IsValidCpf(cpf) || !PlainLine(name, 1, 160) || (note is not null && note.Trim().Length > 0 && !PlainLine(note, 1, 280)) || dose is not ("1" or "2" or "3" or "booster" or "single")
                    || applied is null || !DateOnly.TryParseExact(applied, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var appliedOn) || appliedOn < new DateOnly(1900, 1, 1) || appliedOn > DateOnly.FromDateTime(now.UtcDateTime).AddDays(1))
                    return Problem(HttpStatusCode.BadRequest, "invalid-input");
                if (state.Registry.FirstOrDefault(record => record.Cpf == cpf) is not { IsActive: true }) return Problem(HttpStatusCode.NotFound, "not-found");
                var item = new VaccineInfo(Guid.NewGuid(), name!.Trim(), dose, appliedOn, "applied", string.IsNullOrWhiteSpace(note) ? null : note.Trim(), null, null);
                Health(unitId, cpf).Vaccines.Add(item); HealthBodies.Add(("vaccine-create", body.Clone()));
                return Created(item);
            }
            if (rest is ["health-records", var kind, var recordText, "void"] && kind is "exams" or "vaccines" && method == HttpMethod.Post && Guid.TryParse(recordText, out var recordId))
            {
                var reason = Text(body, "reason");
                var confirmedVoid = body.TryGetProperty("confirmed", out var confirmed) && confirmed.ValueKind == JsonValueKind.True;
                foreach (var health in healthStates.Where(entry => entry.Key.Unit == unitId).Select(entry => entry.Value))
                {
                    if (kind == "exams" && health.Exams.FindIndex(exam => exam.Id == recordId) is var examIndex and >= 0)
                    {
                        if (!confirmedVoid || !PlainLine(reason, 3, 280)) return Problem(HttpStatusCode.BadRequest, "invalid-input");
                        if (health.Exams[examIndex].IsVoided) return Problem(HttpStatusCode.Conflict, "conflict");
                        health.Exams[examIndex] = health.Exams[examIndex] with { Status = "voided", VoidedAt = now, VoidReason = reason!.Trim() }; HealthBodies.Add(("exam-void", body.Clone()));
                        return Reply(health.Exams[examIndex]);
                    }
                    if (kind == "vaccines" && health.Vaccines.FindIndex(vaccine => vaccine.Id == recordId) is var vaccineIndex and >= 0)
                    {
                        if (!confirmedVoid || !PlainLine(reason, 3, 280)) return Problem(HttpStatusCode.BadRequest, "invalid-input");
                        if (health.Vaccines[vaccineIndex].IsVoided) return Problem(HttpStatusCode.Conflict, "conflict");
                        health.Vaccines[vaccineIndex] = health.Vaccines[vaccineIndex] with { Status = "voided", VoidedAt = now, VoidReason = reason!.Trim() }; HealthBodies.Add(("vaccine-void", body.Clone()));
                        return Reply(health.Vaccines[vaccineIndex]);
                    }
                }
                return Problem(HttpStatusCode.NotFound, "not-found");
            }
            return null;
        }

        /// <summary>Corpos (JSON) das escritas da Jornada, na ordem em que chegaram.</summary>
        public List<(string Action, JsonElement Body)> HealthBodies { get; } = [];
    }
}
