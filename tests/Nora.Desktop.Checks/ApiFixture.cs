using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using Nora.Desktop.Models;

namespace Nora.Desktop.Checks;

/// <summary>Entrada antiga de fila, só como dado de teste: a API ainda conta entradas nas métricas e na prévia de desativação, mas o OBS não a lê mais.</summary>
internal sealed record WaitingSeed(long Id, string Cpf, string PatientName, string Specialty, DateTimeOffset EnteredAt, string Status, int? Points = null)
{
    public bool IsActive => Status is "waiting" or "offered";
    public string StatusDisplay => Status == "closed" ? "Encerrada" : Status;
}

internal static partial class Program
{
    /// <summary>
    /// Transporte HTTP sintético do harness: responde só as rotas por UBS e da Secretaria. Não há banco nem rede; as rotas globais
    /// antigas (/api/patients, /api/queue, /api/slots) deliberadamente não existem, como no servidor fora de Development.
    /// </summary>
    private sealed partial class ApiFixture : HttpMessageHandler
    {
        private sealed class UnitState
        {
            public List<UnitPatientRecord> Registry { get; } = [];
            public List<WaitingSeed> Waiting { get; } = [];
            public List<UnitLightningSlot> Slots { get; } = [];
            public List<AppointmentInfo> Appointments { get; } = [];
        }

        private static readonly Guid AccountId = Guid.Parse("0fc6ec25-fb80-4cdd-881b-000000000001");
        private static readonly Guid StaffId = Guid.Parse("0fc6ec25-fb80-4cdd-881b-000000000003");
        private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
        private readonly List<HealthUnit> units = [];
        private readonly Dictionary<Guid, UnitState> states = [];
        private readonly HashSet<Guid> staffUnitIds = [];
        public List<string> Requests { get; } = [];
        public string? LoginIdentifier { get; private set; }
        public string Scope { get; private set; } = "obs";
        public string? LastBearer { get; private set; }
        public bool FailLogout { get; init; }
        public string ObsRole { get; set; } = "administrator";
        public bool LogoutCalled { get; private set; }
        public bool UseWorkspaceIdentity { get; init; }
        public int CepLookups { get; private set; }
        public TaskCompletionSource? PauseReads { get; set; }
        public (DateTimeOffset From, DateTimeOffset To) LastAppointmentRange { get; private set; }
        public bool FailReads { get; set; }
        public bool UnauthorizedReads { get; set; }
        /// <summary>Quando definida, /api/obs/units devolve só estas UBS (concessões da conta); nula devolve todas as cadastradas.</summary>
        public List<Guid>? Granted { get; set; }
        /// <summary>UBS cujo banco "está fora do ar": qualquer rota dela responde 503 com problem+json.</summary>
        public HashSet<Guid> UnavailableUnits { get; } = [];
        /// <summary>UBS cuja base "está desatualizada": qualquer rota dela responde 503 com o código unit-database-outdated.</summary>
        public HashSet<Guid> OutdatedUnits { get; } = [];
        /// <summary>Só a rota de indicadores da UBS falha (500), como uma base sem a migração mais nova; o resto da UBS continua respondendo.</summary>
        public HashSet<Guid> FailingUnitMetrics { get; } = [];
        /// <summary>Cada requisição de indicadores da UBS fica retida até o teste liberá-la (ignora o cancelamento, como um servidor lento).</summary>
        public bool HoldUnitMetrics { get; set; }
        public List<TaskCompletionSource<bool>> HeldUnitMetrics { get; } = [];
        public List<(Guid Unit, System.Collections.Specialized.NameValueCollection Query)> UnitMetricsQueries { get; } = [];
        /// <summary>Ofertas por vaga, na ordem devolvida pelo servidor (cronológica).</summary>
        public Dictionary<Guid, List<UnitSlotOffer>> Offers { get; } = [];
        /// <summary>A conta entra com senha provisória: toda rota (exceto me, logout e troca de senha) responde 403 password-change-required até a troca.</summary>
        public bool MustChangePassword { get; set; }
        public string CurrentPassword { get; set; } = "isolated-test-password";
        public List<(string Current, string New)> PasswordChanges { get; } = [];
        public bool FailProvisioning { get; set; }
        public bool NewUnitsPending { get; set; }
        /// <summary>Resumo e indicadores respondem 503 service-unavailable (uma base de UBS fora do ar).</summary>
        public bool FailNetwork503 { get; set; }
        /// <summary>Sem consulta encerrada no período: o score devolvido é nulo.</summary>
        public bool NoClosedAppointments { get; set; }
        /// <summary>Cada requisição de indicadores fica retida até o teste liberá-la (ignora o cancelamento, como um servidor lento).</summary>
        public bool HoldMetrics { get; set; }
        public List<TaskCompletionSource<bool>> HeldMetrics { get; } = [];
        public List<System.Collections.Specialized.NameValueCollection> MetricsQueries { get; } = [];
        public List<System.Collections.Specialized.NameValueCollection> HistoryQueries { get; } = [];
        public List<System.Collections.Specialized.NameValueCollection> SummaryQueries { get; } = [];
        public List<HistoryEvent> History { get; } = [];
        public List<StaffAccount> StaffAccounts { get; } = [new(StaffId, "Equipe Fixture", "attendant", "equipe@example.test", true, false)];
        public (string FullName, string Email, string Role, string Password)? LastStaffCreate { get; private set; }
        /// <summary>Corpo cru do último POST /api/municipal/staff: os testes conferem que nenhum CPF foi enviado.</summary>
        public string? LastStaffCreateBody { get; private set; }
        /// <summary>Tamanho da página de encerrados de GET .../lightning-slots (o servidor usa 100 por padrão).</summary>
        public int HistoryPageSize { get; set; } = 100;
        /// <summary>Leituras de GET .../lightning-slots (com a query), na ordem em que chegaram.</summary>
        public List<string> HistoryReads { get; } = [];
        public string? LastStaffPassword { get; private set; }

        public HealthUnit AddUnit(HealthUnit unit) { units.Add(unit); State(unit.Id); return unit; }
        public HealthUnit AddUnit(string name = "UBS de teste", string region = "Centro", double latitude = -23.5, double longitude = -46.6, string? databaseState = null)
            => AddUnit(new HealthUnit(Guid.NewGuid(), name, region, "Rua de teste, 10", latitude, longitude, databaseState ?? "ready"));
        private UnitState State(Guid unitId) => states.TryGetValue(unitId, out var state) ? state : states[unitId] = new();
        public UnitPatientRecord AddRegistry(Guid unitId, UnitPatientRecord record) { State(unitId).Registry.Add(record); return record; }
        public UnitPatientRecord AddRegistry(Guid unitId, string name, string cpf = "52998224725", string? email = null, string? phone = null, bool active = true)
            => AddRegistry(unitId, new UnitPatientRecord(cpf, name, email ?? $"{name.Split(' ')[0].ToLowerInvariant()}@example.test", phone, null, active, false, DateTimeOffset.UtcNow));
        public WaitingSeed AddWaiting(Guid unitId, string cpf, string name, string specialty, DateTimeOffset enteredAt, string status = "waiting", int? points = null)
        {
            var state = State(unitId);
            var entry = new WaitingSeed(state.Waiting.Count + 1, cpf, name, specialty, enteredAt, status, points);
            state.Waiting.Add(entry); return entry;
        }
        public AppointmentInfo AddAppointment(Guid unitId, string status, DateTimeOffset startsAt, string cpf = "52998224725", string name = "Paciente Fixture", string specialty = "Clínica", string professional = "Profissional")
        {
            var item = new AppointmentInfo(Guid.NewGuid(), cpf, name, specialty, professional, startsAt, startsAt.AddMinutes(30), status);
            State(unitId).Appointments.Add(item); return item;
        }
        public UnitLightningSlot AddSlot(Guid unitId, UnitLightningSlot slot) { State(unitId).Slots.Add(slot); return slot; }
        public void ReplaceWaiting(Guid unitId, long id, Func<WaitingSeed, WaitingSeed> change) { var list = State(unitId).Waiting; var index = list.FindIndex(entry => entry.Id == id); list[index] = change(list[index]); }
        public IReadOnlyList<WaitingSeed> WaitingOf(Guid unitId) => State(unitId).Waiting;
        public IReadOnlyList<UnitPatientRecord> RegistryOf(Guid unitId) => State(unitId).Registry;
        public IReadOnlyList<AppointmentInfo> AppointmentsOf(Guid unitId) => State(unitId).Appointments;
        public IReadOnlyList<UnitLightningSlot> SlotsOf(Guid unitId) => State(unitId).Slots;
        public int Count(string method, string pathSuffix) => Requests.Count(request => request.StartsWith(method + " ", StringComparison.Ordinal) && request.EndsWith(pathSuffix, StringComparison.Ordinal));

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastBearer = request.Headers.Authorization?.Parameter;
            var path = request.RequestUri!.AbsolutePath;
            Requests.Add(request.Method + " " + path);
            var deviceHeader = request.Headers.TryGetValues("X-Nora-Device", out var headerValues) ? headerValues.Single() : null;
            DeviceHeaders.Add(deviceHeader);
            if (deviceHeader is not null) DeviceHeaderHosts.Add(request.RequestUri.Host);
            if (path == "/api/auth/login")
            {
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
                LoginIdentifier = body.RootElement.GetProperty("identifier").GetString();
                Scope = body.RootElement.GetProperty("scope").GetString()!;
                Require(!string.IsNullOrEmpty(body.RootElement.GetProperty("password").GetString()), "Senha não enviada.");
                var accountName = UseWorkspaceIdentity ? Scope == "obs" ? "Operador Fixture" : "Gestor Fixture" : "Paciente Fixture";
                return BuildLogin(request, body.RootElement, accountName);
            }
            if (request.Method == HttpMethod.Get && path == "/api/address/cep/01001000")
            {
                CepLookups++;
                return Reply(new AddressInfo("01001000", "Praça da Sé", null, null, "Sé", "São Paulo", "SP", "3550308"));
            }
            if (await HandleDevicePublicAsync(request, path, cancellationToken) is { } devicePublic) return devicePublic;
            Require(LastBearer == "fixture-token", "Rota autenticada sem bearer.");
            if (RejectDeviceSession(request) is { } deviceRejected) return deviceRejected;
            if (path == "/api/auth/password" && request.Method == HttpMethod.Post)
            {
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
                var current = body.RootElement.GetProperty("currentPassword").GetString()!; var next = body.RootElement.GetProperty("newPassword").GetString()!;
                if (current != CurrentPassword) return Problem(HttpStatusCode.BadRequest, "invalid-current-password");
                if (next.Length is < 8 or > 256 || next == current) return Problem(HttpStatusCode.BadRequest, "invalid-input");
                PasswordChanges.Add((current, next)); CurrentPassword = next; MustChangePassword = false;
                return new(HttpStatusCode.NoContent);
            }
            if (MustChangePassword && path != "/api/auth/logout" && path != "/api/auth/me") return Problem(HttpStatusCode.Forbidden, "password-change-required");
            if (request.Method == HttpMethod.Get && PauseReads != null) await PauseReads.Task.WaitAsync(cancellationToken);
            if (request.Method == HttpMethod.Get && (FailReads || UnauthorizedReads))
                return new(UnauthorizedReads ? HttpStatusCode.Unauthorized : HttpStatusCode.InternalServerError);
            if (path == "/api/auth/me" && request.Method == HttpMethod.Get)
                return Reply(new AccountUser(AccountId, UseWorkspaceIdentity ? Scope == "obs" ? "Operador Fixture" : "Gestor Fixture" : "Paciente Fixture", Scope == "obs" ? ObsRole : "manager", Scope, MustChangePassword));
            if (path == "/api/auth/profile")
            {
                var name = Scope == "obs" ? "Operador Fixture" : "Gestor Fixture";
                if (request.Method == HttpMethod.Patch)
                {
                    using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
                    name = body.RootElement.GetProperty("fullName").GetString()!;
                }
                return Reply(new StaffSelfProfile(name, "equipe@example.test", Scope == "obs" ? ObsRole : "manager", Scope,
                    Scope == "municipal" ? "São José dos Campos" : null, units.Select(unit => new StaffUnitOption(unit.Id, unit.Name)).ToArray()));
            }
            if (await HandleRetentionAsync(request, path, cancellationToken) is { } retentionResponse) return retentionResponse;
            if (path == "/api/auth/logout") { LogoutCalled = true; return FailLogout ? new(HttpStatusCode.InternalServerError) { Content = new StringContent("private-server-secret") } : new(HttpStatusCode.NoContent); }
            // As próximas leituras da Jornada ficam retidas até o teste liberá-las (ignoram o cancelamento, como um servidor lento).
            if (HoldNextHealthQueries > 0 && request.Method == HttpMethod.Post && path.EndsWith("/health-records/query", StringComparison.Ordinal))
            { HoldNextHealthQueries--; var heldQuery = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously); HeldHealthQueries.Add(heldQuery); await heldQuery.Task; }
            if (HoldNextHealthWrites > 0 && request.Method == HttpMethod.Post && path.EndsWith("/health-records/exams", StringComparison.Ordinal))
            { HoldNextHealthWrites--; var heldWrite = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously); HeldHealthWrites.Add(heldWrite); await heldWrite.Task; }
            if (HandleGlobalScheduling(request, path) is { } globalResponse) return globalResponse;
            if (await HandleGlobalCommunityAsync(request, path, cancellationToken) is { } communityResponse) return communityResponse;
            if (path == "/api/obs/units" && request.Method == HttpMethod.Get)
                return Reply(units.Where(unit => Granted is null || Granted.Contains(unit.Id)).ToArray());
            var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts is ["api", "obs", "units", var unitText, .. var rest] && Guid.TryParse(unitText, out var unitId))
                return await HandleUnitAsync(request, unitId, rest, cancellationToken);
            return await HandleMunicipalAsync(request, path, cancellationToken);
        }

        private async Task<HttpResponseMessage> HandleUnitAsync(HttpRequestMessage request, Guid unitId, string[] rest, CancellationToken cancellationToken)
        {
            Require(units.Any(unit => unit.Id == unitId), "OBS usou UBS inexistente.");
            Require(Granted is null || Granted.Contains(unitId), "OBS usou UBS sem concessão.");
            if (UnavailableUnits.Contains(unitId)) return Problem(HttpStatusCode.ServiceUnavailable, "service-unavailable");
            if (OutdatedUnits.Contains(unitId)) return Problem(HttpStatusCode.ServiceUnavailable, "unit-database-outdated");
            var state = State(unitId);
            var route = string.Join('/', rest);
            JsonDocument? payload = null;
            if (request.Content != null) payload = JsonDocument.Parse(await request.Content.ReadAsStringAsync(cancellationToken));
            using (payload)
            {
                var method = request.Method;
                if (HandleUnitScheduling(request, unitId, rest, payload) is { } scheduled) return scheduled;
                if (HandleUnitCommunity(request, unitId, rest, payload) is { } community) return community;
                if (HandleUnitRound(request, unitId, rest, payload) is { } round) return round;
                if (route == "registry")
                {
                    if (method == HttpMethod.Get) return Reply(state.Registry.ToArray());
                    var root = payload!.RootElement;
                    var cpf = root.GetProperty("cpf").GetString()!;
                    var phone = root.GetProperty("phone").ValueKind == JsonValueKind.Null ? null : root.GetProperty("phone").GetString();
                    var address = root.GetProperty("address").ValueKind == JsonValueKind.Null ? null : JsonSerializer.Deserialize<AddressInfo>(root.GetProperty("address").GetRawText(), Web);
                    if (method == HttpMethod.Post)
                    {
                        if (state.Registry.Any(item => item.Cpf == cpf)) return Problem(HttpStatusCode.Conflict, "conflict");
                        state.Registry.Add(new UnitPatientRecord(cpf, root.GetProperty("fullName").GetString()!, root.GetProperty("email").GetString()!, phone, address, true, false, DateTimeOffset.UtcNow));
                        return Reply(new { created = true });
                    }
                    if (method == HttpMethod.Put)
                    {
                        var index = state.Registry.FindIndex(item => item.Cpf == cpf); Require(index >= 0, "Edição solicitou paciente inexistente.");
                        state.Registry[index] = state.Registry[index] with { FullName = root.GetProperty("fullName").GetString()!, Email = root.GetProperty("email").GetString()!, Phone = phone, Address = address };
                        return new(HttpStatusCode.NoContent);
                    }
                }
                if (route == "registry/email" && method == HttpMethod.Patch)
                {
                    var cpf = payload!.RootElement.GetProperty("cpf").GetString(); var index = state.Registry.FindIndex(item => item.Cpf == cpf);
                    Require(index >= 0, "Correção solicitou paciente inexistente.");
                    state.Registry[index] = state.Registry[index] with { Email = payload.RootElement.GetProperty("email").GetString()! };
                    return new(HttpStatusCode.NoContent);
                }
                if (route == "registry/active" && method == HttpMethod.Patch)
                {
                    Require(payload!.RootElement.GetProperty("confirmed").GetBoolean(), "Alteração de situação sem confirmação.");
                    var cpf = payload.RootElement.GetProperty("cpf").GetString(); var index = state.Registry.FindIndex(item => item.Cpf == cpf);
                    Require(index >= 0, "Alteração solicitou paciente inexistente.");
                    var active = payload.RootElement.GetProperty("isActive").GetBoolean();
                    state.Registry[index] = state.Registry[index] with { IsActive = active };
                    if (!active)
                    {
                        for (var i = 0; i < state.Waiting.Count; i++) if (state.Waiting[i].Cpf == cpf && state.Waiting[i].IsActive) state.Waiting[i] = state.Waiting[i] with { Status = "cancelled" };
                        // Decisão de 2026-11-16: desativar também cancela as consultas futuras da UBS (cancelamento da unidade, que não pesa no score).
                        for (var i = 0; i < state.Appointments.Count; i++)
                            if (state.Appointments[i].Cpf == cpf && state.Appointments[i].Status is "scheduled" or "called" && state.Appointments[i].StartsAt > DateTimeOffset.UtcNow) state.Appointments[i] = state.Appointments[i] with { Status = "cancelled" };
                    }
                    return new(HttpStatusCode.NoContent);
                }
                if (route == "metrics" && method == HttpMethod.Get)
                {
                    var query = System.Web.HttpUtility.ParseQueryString(request.RequestUri!.Query);
                    var windowFrom = DateTimeOffset.Parse(query["from"]!, CultureInfo.InvariantCulture); var windowTo = DateTimeOffset.Parse(query["to"]!, CultureInfo.InvariantCulture);
                    // A capacidade dos próximos 7 dias do Painel usa a mesma rota com uma janela que começa hoje à meia-noite local; as leituras dela ficam em CapacityQueries.
                    var isCapacityRead = windowFrom.LocalDateTime == DateTime.Today && windowTo - windowFrom == TimeSpan.FromDays(7);
                    if (isCapacityRead) CapacityQueries.Add((unitId, query)); else UnitMetricsQueries.Add((unitId, query));
                    if (FailingUnitMetrics.Contains(unitId)) return Problem(HttpStatusCode.InternalServerError, "internal-error");
                    if (HoldUnitMetrics && !isCapacityRead) { var held = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously); HeldUnitMetrics.Add(held); await held.Task; }
                    return Reply(BuildUnitMetrics(unitId, state, DateTimeOffset.Parse(query["from"]!, CultureInfo.InvariantCulture), DateTimeOffset.Parse(query["to"]!, CultureInfo.InvariantCulture)));
                }
                if (rest is ["lightning-slots", var slotText, "offers"] && method == HttpMethod.Get)
                {
                    var slotId = Guid.Parse(slotText);
                    if (state.Slots.All(slot => slot.Id != slotId)) return Problem(HttpStatusCode.NotFound, "not-found");
                    return Reply(Offers.TryGetValue(slotId, out var offers) ? offers.ToArray() : Array.Empty<UnitSlotOffer>());
                }
                if (route == "lightning-slots")
                {
                    if (method == HttpMethod.Get) return SlotsPage(state, request);
                    var appointment = state.Appointments.Single(item => item.Id == payload!.RootElement.GetProperty("appointmentId").GetGuid());
                    if (state.Slots.Any(item => item.AppointmentId == appointment.Id)) return Problem(HttpStatusCode.Conflict, "conflict");
                    var slot = new UnitLightningSlot(Guid.NewGuid(), appointment.Id, appointment.Specialty, appointment.Professional, appointment.StartsAt, appointment.EndsAt, "open", DateTimeOffset.UtcNow);
                    state.Slots.Add(slot); return Reply(slot);
                }
                if (route == "appointments")
                {
                    if (method == HttpMethod.Get)
                    {
                        var query = System.Web.HttpUtility.ParseQueryString(request.RequestUri!.Query);
                        var from = DateTimeOffset.Parse(query["from"]!, CultureInfo.InvariantCulture); var to = DateTimeOffset.Parse(query["to"]!, CultureInfo.InvariantCulture);
                        Require(to - from <= TimeSpan.FromDays(366), "Intervalo acima de 366 dias.");
                        LastAppointmentRange = (from, to);
                        return Reply(state.Appointments.Where(item => item.StartsAt >= from && item.StartsAt < to).ToArray());
                    }
                    var root = payload!.RootElement; var cpf = root.GetProperty("cpf").GetString()!;
                    var created = new AppointmentInfo(Guid.NewGuid(), cpf, state.Registry.Single(item => item.Cpf == cpf).FullName, root.GetProperty("specialty").GetString()!, root.GetProperty("professional").GetString()!, root.GetProperty("startsAt").GetDateTimeOffset(), root.GetProperty("endsAt").GetDateTimeOffset(), "scheduled");
                    state.Appointments.Add(created); return Reply(created);
                }
                if (rest is ["appointments", var appointmentText, "status"] && method == HttpMethod.Patch)
                {
                    var id = Guid.Parse(appointmentText); var index = state.Appointments.FindIndex(item => item.Id == id); Require(index >= 0, "Consulta inexistente.");
                    var status = payload!.RootElement.GetProperty("status").GetString()!;
                    if (status == "cancelled") Require(payload.RootElement.GetProperty("confirmed").GetBoolean(), "Cancelamento sem confirmação.");
                    state.Appointments[index] = state.Appointments[index] with { Status = status };
                    return Reply(state.Appointments[index]);
                }
            }
            throw new InvalidOperationException("Rota de UBS inesperada da fixture: " + request.Method + " " + route);
        }

        private HttpResponseMessage SlotsPage(UnitState state, HttpRequestMessage request)
        {
            HistoryReads.Add("lightning-slots" + request.RequestUri!.Query);
            var (indexes, cursor) = HistorySlice(state.Slots.Select((slot, index) => (slot.IsActive, index)).ToArray(), request);
            return Reply(new { items = indexes.Select(index => state.Slots[index]).ToArray(), historyCursor = cursor });
        }

        /// <summary>Escolhe os índices da página: ativos (só na primeira página) e os encerrados mais recentes (maior índice primeiro) até <see cref="HistoryPageSize"/>; cursor = "h" + índice do mais antigo devolvido, se sobrarem mais.</summary>
        private (int[] Indexes, string? Cursor) HistorySlice((bool Active, int Index)[] all, HttpRequestMessage request)
        {
            var query = System.Web.HttpUtility.ParseQueryString(request.RequestUri!.Query);
            var before = query["historyBefore"] is { } text ? int.Parse(text[1..], CultureInfo.InvariantCulture) : int.MaxValue;
            var limit = int.TryParse(query["historyLimit"], out var requested) ? requested : HistoryPageSize;
            var closed = all.Where(item => !item.Active && item.Index < before).OrderByDescending(item => item.Index).ToArray();
            var taken = closed.Take(limit).Select(item => item.Index).ToArray();
            var indexes = (query["historyBefore"] is null ? all.Where(item => item.Active).Select(item => item.Index) : []).Concat(taken).OrderBy(index => index).ToArray();
            return (indexes, closed.Length > taken.Length ? "h" + taken[^1].ToString(CultureInfo.InvariantCulture) : null);
        }

        private async Task<HttpResponseMessage> HandleMunicipalAsync(HttpRequestMessage request, string path, CancellationToken cancellationToken)
        {
            var query = System.Web.HttpUtility.ParseQueryString(request.RequestUri!.Query);
            if (path == "/api/municipal/staff" && request.Method == HttpMethod.Get) return Reply(StaffAccounts.ToArray());
            if (path == "/api/municipal/staff" && request.Method == HttpMethod.Post)
            {
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
                var root = body.RootElement;
                LastStaffCreateBody = root.GetRawText();
                var fullName = root.GetProperty("fullName").GetString()!; var email = root.GetProperty("email").GetString()!;
                var role = root.GetProperty("role").GetString()!; var password = root.GetProperty("password").GetString()!;
                LastStaffCreate = (fullName, email, role, password);
                if (role is not ("attendant" or "administrator") || fullName.Trim().Length < 2 || !Nora.Desktop.Services.BrazilianDataValidator.IsValidEmail(email) || password.Length is < 8 or > 256)
                    return Problem(HttpStatusCode.BadRequest, "invalid-input");
                if (StaffAccounts.Any(item => item.Email == email)) return Problem(HttpStatusCode.Conflict, "conflict");
                var created = new StaffAccount(Guid.NewGuid(), fullName, role, email, true, true);
                StaffAccounts.Add(created); History.Add(new(DateTimeOffset.UtcNow, "staff-created", "Gestor Fixture", null, null, fullName));
                return new(HttpStatusCode.Created) { Content = JsonContent.Create(created) };
            }
            var staffParts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (staffParts is ["api", "municipal", "staff", var staffText, "active"] && request.Method == HttpMethod.Patch)
            {
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
                Require(body.RootElement.GetProperty("confirmed").GetBoolean(), "Alteração de conta sem confirmação.");
                var index = StaffAccounts.FindIndex(item => item.Id == Guid.Parse(staffText)); if (index < 0) return Problem(HttpStatusCode.NotFound, "not-found");
                var active = body.RootElement.GetProperty("isActive").GetBoolean();
                StaffAccounts[index] = StaffAccounts[index] with { IsActive = active };
                History.Add(new(DateTimeOffset.UtcNow, active ? "staff-reactivated" : "staff-deactivated", "Gestor Fixture", null, null, StaffAccounts[index].FullName));
                return new(HttpStatusCode.NoContent);
            }
            if (staffParts is ["api", "municipal", "staff", var resetText, "password"] && request.Method == HttpMethod.Post)
            {
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
                Require(body.RootElement.GetProperty("confirmed").GetBoolean(), "Redefinição sem confirmação.");
                var index = StaffAccounts.FindIndex(item => item.Id == Guid.Parse(resetText)); if (index < 0) return Problem(HttpStatusCode.NotFound, "not-found");
                LastStaffPassword = body.RootElement.GetProperty("password").GetString();
                StaffAccounts[index] = StaffAccounts[index] with { MustChangePassword = true };
                History.Add(new(DateTimeOffset.UtcNow, "staff-password-reset", "Gestor Fixture", null, null, StaffAccounts[index].FullName));
                return new(HttpStatusCode.NoContent);
            }
            if (path.StartsWith("/api/municipal/staff/", StringComparison.Ordinal) && path.EndsWith("/units", StringComparison.Ordinal) && request.Method == HttpMethod.Get)
                return Reply(units.Where(unit => staffUnitIds.Contains(unit.Id)).ToArray());
            if (path.StartsWith("/api/municipal/staff/", StringComparison.Ordinal) && path.Contains("/units/", StringComparison.Ordinal))
            {
                var id = Guid.Parse(path.Split('/').Last());
                if (request.Method == HttpMethod.Put) staffUnitIds.Add(id);
                else if (request.Method == HttpMethod.Delete) staffUnitIds.Remove(id);
                else throw new InvalidOperationException("Método não previsto para vínculo.");
                return new(HttpStatusCode.NoContent);
            }
            if (path == "/api/municipal/history" && request.Method == HttpMethod.Get)
            {
                HistoryQueries.Add(query);
                var limit = query["limit"] is { } limitText ? int.Parse(limitText, CultureInfo.InvariantCulture) : 100;
                DateTimeOffset? from = query["from"] is { } f ? DateTimeOffset.Parse(f, CultureInfo.InvariantCulture) : null, to = query["to"] is { } t ? DateTimeOffset.Parse(t, CultureInfo.InvariantCulture) : null;
                Guid? unit = query["unitId"] is { } u ? Guid.Parse(u) : null;
                // Como a API: ordem (instante, posição) decrescente; `before` é o cursor opaco do último item (c1_ + instante + posição). O instante puro continua aceito (só eventos estritamente anteriores).
                var ordered = History.Select((item, position) => (Item: item with { Cursor = $"c1_{item.OccurredAt.UtcTicks}_{position}" }, Position: position)).OrderByDescending(entry => entry.Item.OccurredAt).ThenByDescending(entry => entry.Position).ToArray();
                var before = query["before"];
                bool After(HistoryEvent item, int position)
                {
                    if (before is null) return true;
                    if (before.StartsWith("c1_", StringComparison.Ordinal)) { var pieces = before[3..].Split('_'); var ticks = long.Parse(pieces[0], CultureInfo.InvariantCulture); var seq = int.Parse(pieces[1], CultureInfo.InvariantCulture); return item.OccurredAt.UtcTicks < ticks || (item.OccurredAt.UtcTicks == ticks && position < seq); }
                    return item.OccurredAt < DateTimeOffset.Parse(before, CultureInfo.InvariantCulture);
                }
                return Reply(ordered.Where(entry => (from is null || entry.Item.OccurredAt >= from) && (to is null || entry.Item.OccurredAt < to) && (unit is null || entry.Item.UnitId == unit) && After(entry.Item, entry.Position)).Select(entry => entry.Item).Take(limit).ToArray());
            }
            if (path == "/api/municipal/metrics" && request.Method == HttpMethod.Get)
            {
                MetricsQueries.Add(query);
                if (FailNetwork503) return Problem(HttpStatusCode.ServiceUnavailable, "service-unavailable");
                if (HoldMetrics) { var held = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously); HeldMetrics.Add(held); await held.Task; }
                return Reply(BuildMetrics(DateTimeOffset.Parse(query["from"]!, CultureInfo.InvariantCulture), DateTimeOffset.Parse(query["to"]!, CultureInfo.InvariantCulture), query["unitId"] is { } unitText ? Guid.Parse(unitText) : null, query["region"], query["specialty"]));
            }
            if (path.StartsWith("/api/municipal/units/", StringComparison.Ordinal) && path.EndsWith("/database", StringComparison.Ordinal) && request.Method == HttpMethod.Post)
            {
                var id = Guid.Parse(path.Split('/')[4]); var index = units.FindIndex(unit => unit.Id == id); if (index < 0) return Problem(HttpStatusCode.NotFound, "not-found");
                if (FailProvisioning) return Problem(HttpStatusCode.ServiceUnavailable, "database-provisioning-failed");
                units[index] = units[index] with { DatabaseState = "ready" };
                History.Add(new(DateTimeOffset.UtcNow, "unit-database-created", "Gestor Fixture", id, units[index].Name, null));
                return Reply(units[index]);
            }
            if (path.StartsWith("/api/municipal/units/", StringComparison.Ordinal) && path.EndsWith("/registry", StringComparison.Ordinal) && request.Method == HttpMethod.Get)
            {
                var id = Guid.Parse(path.Split('/')[4]);
                Require(units.Any(unit => unit.Id == id), "Consulta solicitou uma UBS inexistente.");
                return Reply(new[] { new UnitPatientRecord("52998224725", "Paciente Sintético", "registered@example.test", "+5511999990000", new AddressInfo("01001000", "Praça da Sé", "10", null, "Sé", "São Paulo", "SP"), true, true, new DateTimeOffset(2026, 3, 12, 15, 30, 0, TimeSpan.Zero)), new UnitPatientRecord("11144477735", "Outro Paciente", "outro@example.test", null, null, false, false, new DateTimeOffset(2026, 4, 2, 9, 0, 0, TimeSpan.Zero)) });
            }
            if (path.StartsWith("/api/municipal/units/", StringComparison.Ordinal) && path.EndsWith("/appointments", StringComparison.Ordinal) && request.Method == HttpMethod.Get)
            {
                var id = Guid.Parse(path.Split('/')[4]); Require(units.Any(unit => unit.Id == id), "Agenda solicitou UBS inexistente.");
                var from = DateTimeOffset.Parse(query["from"]!, CultureInfo.InvariantCulture); var to = DateTimeOffset.Parse(query["to"]!, CultureInfo.InvariantCulture);
                Require(to - from <= TimeSpan.FromDays(366), "Intervalo acima de 366 dias.");
                LastAppointmentRange = (from, to);
                if (State(id).Appointments.Count > 0) return Reply(State(id).Appointments.Where(item => item.StartsAt >= from && item.StartsAt < to).ToArray());
                var start = DateTimeOffset.UtcNow.AddDays(1);
                return Reply(new[] { new AppointmentInfo(Guid.NewGuid(), "52998224725", "Paciente Sintético", "Clínica", "Profissional", start, start.AddMinutes(30), "scheduled") });
            }
            if (path == "/api/municipal/units" && request.Method == HttpMethod.Get) return Reply(units.ToArray());
            if ((path == "/api/municipal/units" && request.Method == HttpMethod.Post) || (path.StartsWith("/api/municipal/units/", StringComparison.Ordinal) && request.Method == HttpMethod.Put))
            {
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
                var id = request.Method == HttpMethod.Post ? Guid.NewGuid() : Guid.Parse(path.Split('/').Last());
                var state = request.Method == HttpMethod.Post ? (NewUnitsPending ? "pending" : "ready") : units.FirstOrDefault(existing => existing.Id == id)?.DatabaseState ?? "ready";
                var unit = new HealthUnit(id, body.RootElement.GetProperty("name").GetString()!, body.RootElement.GetProperty("region").GetString()!, body.RootElement.GetProperty("address").GetString()!, body.RootElement.GetProperty("latitude").GetDouble(), body.RootElement.GetProperty("longitude").GetDouble(), state);
                units.RemoveAll(existing => existing.Id == id); AddUnit(unit); return Reply(unit);
            }
            if (path == "/api/municipal/summary")
            {
                SummaryQueries.Add(query);
                if (FailNetwork503) return Problem(HttpStatusCode.ServiceUnavailable, "service-unavailable");
                var pending = units.Count(unit => unit.DatabasePending);
                return Reply(new MunicipalSummary(1, 1, 0, 3, 5, 2, 1, 2, 0, [new(7, 1, 0, 1, 2, 1, 0, 1, 0), new(30, 1, 0, 2, 3, 1, 1, 1, 0), new(90, 1, 0, 3, 5, 2, 1, 2, 0)], 2, pending));
            }
            using (var schedulingBody = request.Content is null ? null : JsonDocument.Parse(await request.Content.ReadAsStringAsync(cancellationToken)))
                if (HandleMunicipalScheduling(request, path, query, schedulingBody) is { } municipalScheduled) return municipalScheduled;
            throw new InvalidOperationException("Rota inesperada da fixture: " + request.Method + " " + path);
        }

        /// <summary>
        /// Indicadores de uma UBS, determinísticos: 12 concluídas, 3 faltas, 2 cancelamentos (1 do paciente, peso 0,4), fila e vagas reais da fixture,
        /// espera média de 95 min e série esparsa com só dois dias (o último da janela e quatro dias antes), como a API.
        /// </summary>
        private UnitMetrics BuildUnitMetrics(Guid unitId, UnitState state, DateTimeOffset from, DateTimeOffset to)
        {
            var empty = NoClosedAppointments;
            var completed = empty ? 0 : 12; var missed = empty ? 0 : 3; var weight = empty ? 0 : 0.4;
            var appointments = new AppointmentMetrics(1, 0, completed, missed, empty ? 0 : 2, empty ? 0 : 1, weight, empty ? null : Math.Round(completed / (completed + missed + weight), 4));
            var active = state.Waiting.Count(entry => entry.IsActive);
            var waiting = new WaitingMetrics(active, active, empty ? 0 : 4, empty ? null : 95, active == 0 ? null : 1500);
            var lightning = new LightningMetrics(state.Slots.Count, empty ? 0 : 1, empty ? 0 : 2, empty ? 0 : 1);
            var specialties = empty ? Array.Empty<SpecialtyMetrics>() : new[] { new SpecialtyMetrics("Clínica geral", 9, 2, 1, 0.4, 0.8257, active, 120), new SpecialtyMetrics("Pediatria", 3, 1, 1, 0, 0.75, 0, null) };
            var last = DateOnly.FromDateTime(to.LocalDateTime.AddDays(-1)); var first = DateOnly.FromDateTime(from.LocalDateTime);
            var daily = empty ? Array.Empty<DailyMetrics>() : new[] { new DailyMetrics(last.AddDays(-4).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), 0, 3, 1, 0), new DailyMetrics(last.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), 1, 2, 0, 1) }
                .Where(day => DateOnly.ParseExact(day.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture) >= first).ToArray();
            return new(from, to, appointments, waiting, lightning, specialties, daily, UnitCapacityOf(unitId), GoalStatuses(unitId, appointments.AttendanceScore, MissedRate(appointments), waiting.AverageWaitMinutes));
        }

        /// <summary>Indicadores sintéticos e determinísticos: cada UBS da fixture tem números próprios e os filtros restringem o resultado como no servidor.</summary>
        private NetworkMetrics BuildMetrics(DateTimeOffset from, DateTimeOffset to, Guid? unitId, string? region, string? specialty)
        {
            var ready = units.Where(unit => !unit.DatabasePending).ToList();
            var scoped = ready.Where(unit => (unitId is null || unit.Id == unitId) && (string.IsNullOrEmpty(region) || string.Equals(unit.Region, region, StringComparison.OrdinalIgnoreCase))).ToList();
            AppointmentMetrics Appointments(int index)
            {
                var completed = NoClosedAppointments ? 0 : 10 * (index + 1); var missed = NoClosedAppointments ? 0 : 2 * (index + 1);
                var weight = NoClosedAppointments ? 0 : 0.5 * index;
                return new(1, 0, completed, missed, NoClosedAppointments ? 0 : index + 1, NoClosedAppointments ? 0 : index, weight, completed + missed + weight == 0 ? null : Math.Round(completed / (completed + missed + weight), 4));
            }
            WaitingMetrics Waiting(int index) => new(3 * (index + 1), 2 * (index + 1), index + 1, 90 * (index + 1), 1500);
            LightningMetrics Lightning(int index) => new(index + 1, index, 1, 0);
            var byUnit = scoped.Select(unit => { var index = ready.IndexOf(unit); var appointments = Appointments(index); var waiting = Waiting(index); return new UnitMetricsSummary(unit.Id, unit.Name, unit.Region, appointments, waiting, Lightning(index), UnitCapacityOf(unit.Id), GoalStatuses(unit.Id, appointments.AttendanceScore, MissedRate(appointments), waiting.AverageWaitMinutes)); }).ToArray();
            var byRegion = byUnit.GroupBy(item => item.Region, StringComparer.OrdinalIgnoreCase).Select(group =>
            {
                var completed = group.Sum(item => item.Appointments.Completed); var missed = group.Sum(item => item.Appointments.Missed); var weight = group.Sum(item => item.Appointments.CancellationWeight);
                return new RegionMetricsSummary(group.Key, new(0, 0, completed, missed, group.Sum(item => item.Appointments.Cancelled), group.Sum(item => item.Appointments.CancelledByPatient), weight, completed + missed + weight == 0 ? null : Math.Round(completed / (completed + missed + weight), 4)),
                    new(group.Sum(item => item.Waiting.ActiveEntries), group.Sum(item => item.Waiting.WaitingPatients), group.Sum(item => item.Waiting.AcceptedEntries), group.Average(item => item.Waiting.AverageWaitMinutes), 1500), new(0, 0, 0, 0),
                    new CapacityMetrics(group.Sum(item => item.Capacity?.OfferedSlots ?? 0), group.Sum(item => item.Capacity?.BookedSlots ?? 0), OccupancyOf(group.Sum(item => item.Capacity?.OfferedSlots ?? 0), group.Sum(item => item.Capacity?.BookedSlots ?? 0))));
            }).ToArray();
            var totalCompleted = byUnit.Sum(item => item.Appointments.Completed); var totalMissed = byUnit.Sum(item => item.Appointments.Missed); var totalWeight = byUnit.Sum(item => item.Appointments.CancellationWeight);
            var totals = new MetricsSummary(
                new(byUnit.Sum(item => item.Appointments.Scheduled), 0, totalCompleted, totalMissed, byUnit.Sum(item => item.Appointments.Cancelled), byUnit.Sum(item => item.Appointments.CancelledByPatient), totalWeight, totalCompleted + totalMissed + totalWeight == 0 ? null : Math.Round(totalCompleted / (totalCompleted + totalMissed + totalWeight), 4)),
                byUnit.Length == 0 ? new(0, 0, 0, null, null) : new(byUnit.Sum(item => item.Waiting.ActiveEntries), byUnit.Sum(item => item.Waiting.WaitingPatients), byUnit.Sum(item => item.Waiting.AcceptedEntries), byUnit.Average(item => item.Waiting.AverageWaitMinutes), 1500),
                new(byUnit.Sum(item => item.Lightning.RegisteredSlots), byUnit.Sum(item => item.Lightning.AcceptedOffers), byUnit.Sum(item => item.Lightning.DeclinedOffers), 0));
            var specialties = new[]
            {
                new SpecialtyMetrics("Clínica geral", 30, 6, 3, 1.2, NoClosedAppointments ? null : 0.8, 5, 120),
                new SpecialtyMetrics("Pediatria", 12, 2, 1, 0.4, NoClosedAppointments ? null : 0.8571, 2, null)
            }.Where(item => string.IsNullOrEmpty(specialty) || string.Equals(item.Specialty, specialty, StringComparison.OrdinalIgnoreCase)).ToArray();
            var firstDay = DateOnly.FromDateTime(from.LocalDateTime);
            var daily = byUnit.Length == 0 || NoClosedAppointments ? [] : Enumerable.Range(0, Math.Min(5, Math.Max(1, (int)(to - from).TotalDays))).Select(offset => new DailyMetrics(firstDay.AddDays(offset).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), 1, 3 * byUnit.Length + offset, offset % 2, 1)).ToArray();
            var offeredTotal = byUnit.Sum(item => item.Capacity?.OfferedSlots ?? 0); var bookedTotal = byUnit.Sum(item => item.Capacity?.BookedSlots ?? 0);
            return new(from, to, totals, byUnit, byRegion, specialties, daily, units.Count(unit => unit.DatabasePending), new CapacityMetrics(offeredTotal, bookedTotal, OccupancyOf(offeredTotal, bookedTotal)),
                GoalStatuses(null, totals.Appointments.AttendanceScore, MissedRate(totals.Appointments), totals.Waiting.AverageWaitMinutes));
        }

        private static HttpResponseMessage Problem(HttpStatusCode status, string code) => new(status)
        {
            Content = new StringContent($"{{\"type\":\"private-server-secret\",\"title\":\"private-server-secret\",\"status\":{(int)status},\"detail\":\"private-server-secret\",\"code\":\"{code}\",\"traceId\":\"x\"}}", System.Text.Encoding.UTF8, "application/problem+json")
        };
        private static HttpResponseMessage Reply<T>(T payload) => new(HttpStatusCode.OK) { Content = JsonContent.Create(payload) };
    }

    /// <summary>Transporte mínimo para testar o cliente HTTP isoladamente: devolve a resposta que o teste escolheu.</summary>
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.Method + " " + request.RequestUri!.AbsolutePath);
            return Task.FromResult(respond(request));
        }
    }
}
