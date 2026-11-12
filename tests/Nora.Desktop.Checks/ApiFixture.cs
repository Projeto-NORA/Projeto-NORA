using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using Nora.Desktop.Models;

namespace Nora.Desktop.Checks;

internal static partial class Program
{
    /// <summary>
    /// Transporte HTTP sintético do harness: responde só as rotas por UBS e da Secretaria. Não há banco nem rede; as rotas globais
    /// antigas (/api/patients, /api/queue, /api/slots) deliberadamente não existem, como no servidor fora de Development.
    /// </summary>
    private sealed class ApiFixture : HttpMessageHandler
    {
        private sealed class UnitState
        {
            public List<UnitPatientRecord> Registry { get; } = [];
            public List<UnitWaitingEntry> Waiting { get; } = [];
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

        public HealthUnit AddUnit(HealthUnit unit) { units.Add(unit); State(unit.Id); return unit; }
        public HealthUnit AddUnit(string name = "UBS de teste", string region = "Centro", double latitude = -23.5, double longitude = -46.6)
            => AddUnit(new HealthUnit(Guid.NewGuid(), name, region, "Rua de teste, 10", latitude, longitude));
        private UnitState State(Guid unitId) => states.TryGetValue(unitId, out var state) ? state : states[unitId] = new();
        public UnitPatientRecord AddRegistry(Guid unitId, UnitPatientRecord record) { State(unitId).Registry.Add(record); return record; }
        public UnitPatientRecord AddRegistry(Guid unitId, string name, string cpf = "52998224725", string? email = null, string? phone = null, bool active = true)
            => AddRegistry(unitId, new UnitPatientRecord(cpf, name, email ?? $"{name.Split(' ')[0].ToLowerInvariant()}@example.test", phone, null, active, false, DateTimeOffset.UtcNow));
        public UnitWaitingEntry AddWaiting(Guid unitId, string cpf, string name, string specialty, DateTimeOffset enteredAt, string status = "waiting")
        {
            var state = State(unitId);
            var entry = new UnitWaitingEntry(state.Waiting.Count + 1, cpf, name, specialty, enteredAt, status);
            state.Waiting.Add(entry); return entry;
        }
        public AppointmentInfo AddAppointment(Guid unitId, string status, DateTimeOffset startsAt, string cpf = "52998224725", string name = "Paciente Fixture", string specialty = "Clínica", string professional = "Profissional")
        {
            var item = new AppointmentInfo(Guid.NewGuid(), cpf, name, specialty, professional, startsAt, startsAt.AddMinutes(30), status);
            State(unitId).Appointments.Add(item); return item;
        }
        public UnitLightningSlot AddSlot(Guid unitId, UnitLightningSlot slot) { State(unitId).Slots.Add(slot); return slot; }
        public void ReplaceWaiting(Guid unitId, long id, Func<UnitWaitingEntry, UnitWaitingEntry> change) { var list = State(unitId).Waiting; var index = list.FindIndex(entry => entry.Id == id); list[index] = change(list[index]); }
        public IReadOnlyList<UnitWaitingEntry> WaitingOf(Guid unitId) => State(unitId).Waiting;
        public IReadOnlyList<UnitPatientRecord> RegistryOf(Guid unitId) => State(unitId).Registry;
        public IReadOnlyList<AppointmentInfo> AppointmentsOf(Guid unitId) => State(unitId).Appointments;
        public IReadOnlyList<UnitLightningSlot> SlotsOf(Guid unitId) => State(unitId).Slots;
        public int Count(string method, string pathSuffix) => Requests.Count(request => request.StartsWith(method + " ", StringComparison.Ordinal) && request.EndsWith(pathSuffix, StringComparison.Ordinal));

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastBearer = request.Headers.Authorization?.Parameter;
            var path = request.RequestUri!.AbsolutePath;
            Requests.Add(request.Method + " " + path);
            if (path == "/api/auth/login")
            {
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
                LoginIdentifier = body.RootElement.GetProperty("identifier").GetString();
                Scope = body.RootElement.GetProperty("scope").GetString()!;
                Require(!string.IsNullOrEmpty(body.RootElement.GetProperty("password").GetString()), "Senha não enviada.");
                var accountName = UseWorkspaceIdentity ? Scope == "obs" ? "Operador Fixture" : "Gestor Fixture" : "Paciente Fixture";
                return Reply(new LoginSession("fixture-token", DateTimeOffset.UtcNow.AddHours(1), new(AccountId, accountName, Scope == "obs" ? ObsRole : "manager", Scope)));
            }
            if (request.Method == HttpMethod.Get && path == "/api/address/cep/01001000")
            {
                CepLookups++;
                return Reply(new AddressInfo("01001000", "Praça da Sé", null, null, "Sé", "São Paulo", "SP", "3550308"));
            }
            Require(LastBearer == "fixture-token", "Rota autenticada sem bearer.");
            if (request.Method == HttpMethod.Get && PauseReads != null) await PauseReads.Task.WaitAsync(cancellationToken);
            if (request.Method == HttpMethod.Get && (FailReads || UnauthorizedReads))
                return new(UnauthorizedReads ? HttpStatusCode.Unauthorized : HttpStatusCode.InternalServerError);
            if (path == "/api/auth/logout") { LogoutCalled = true; return FailLogout ? new(HttpStatusCode.InternalServerError) { Content = new StringContent("private-server-secret") } : new(HttpStatusCode.NoContent); }
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
            var state = State(unitId);
            var route = string.Join('/', rest);
            JsonDocument? payload = null;
            if (request.Content != null) payload = JsonDocument.Parse(await request.Content.ReadAsStringAsync(cancellationToken));
            using (payload)
            {
                var method = request.Method;
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
                    if (!active) for (var i = 0; i < state.Waiting.Count; i++) if (state.Waiting[i].Cpf == cpf && state.Waiting[i].IsActive) state.Waiting[i] = state.Waiting[i] with { Status = "cancelled" };
                    return new(HttpStatusCode.NoContent);
                }
                if (route == "waiting-list")
                {
                    if (method == HttpMethod.Get) return Reply(state.Waiting.ToArray());
                    var cpf = payload!.RootElement.GetProperty("cpf").GetString()!; var specialty = payload.RootElement.GetProperty("specialty").GetString()!;
                    if (state.Waiting.Any(item => item.Cpf == cpf && item.IsActive && string.Equals(item.Specialty, specialty, StringComparison.OrdinalIgnoreCase))) return Problem(HttpStatusCode.Conflict, "conflict");
                    var name = state.Registry.Single(item => item.Cpf == cpf).FullName;
                    var entry = new UnitWaitingEntry(state.Waiting.Count + 1, cpf, name, specialty, DateTimeOffset.UtcNow, "waiting");
                    state.Waiting.Add(entry); return Reply(entry);
                }
                if (rest is ["waiting-list", var entryText, "cancel"])
                {
                    var id = long.Parse(entryText, CultureInfo.InvariantCulture); var index = state.Waiting.FindIndex(entry => entry.Id == id);
                    Require(index >= 0, "Entrada de fila inexistente.");
                    if (!state.Waiting[index].IsActive) return Problem(HttpStatusCode.Conflict, "conflict");
                    state.Waiting[index] = state.Waiting[index] with { Status = "cancelled" }; return new(HttpStatusCode.NoContent);
                }
                if (route == "lightning-slots")
                {
                    if (method == HttpMethod.Get) return Reply(state.Slots.ToArray());
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

        private async Task<HttpResponseMessage> HandleMunicipalAsync(HttpRequestMessage request, string path, CancellationToken cancellationToken)
        {
            if (path == "/api/municipal/staff" && request.Method == HttpMethod.Get) return Reply(new[] { new StaffAccount(StaffId, "Equipe Fixture", "attendant") });
            if (path == $"/api/municipal/staff/{StaffId}/units" && request.Method == HttpMethod.Get) return Reply(units.Where(unit => staffUnitIds.Contains(unit.Id)).ToArray());
            if (path.StartsWith($"/api/municipal/staff/{StaffId}/units/", StringComparison.Ordinal))
            {
                var id = Guid.Parse(path.Split('/').Last());
                if (request.Method == HttpMethod.Put) staffUnitIds.Add(id);
                else if (request.Method == HttpMethod.Delete) staffUnitIds.Remove(id);
                else throw new InvalidOperationException("Método não previsto para vínculo.");
                return new(HttpStatusCode.NoContent);
            }
            if (path.StartsWith("/api/municipal/units/", StringComparison.Ordinal) && path.EndsWith("/registry", StringComparison.Ordinal) && request.Method == HttpMethod.Get)
            {
                var id = Guid.Parse(path.Split('/')[4]);
                Require(units.Any(unit => unit.Id == id), "Consulta solicitou uma UBS inexistente.");
                return Reply(new[] { new UnitPatientRecord("52998224725", "Paciente Sintético", "registered@example.test") });
            }
            if (path.StartsWith("/api/municipal/units/", StringComparison.Ordinal) && path.EndsWith("/appointments", StringComparison.Ordinal) && request.Method == HttpMethod.Get)
            {
                var id = Guid.Parse(path.Split('/')[4]); Require(units.Any(unit => unit.Id == id), "Agenda solicitou UBS inexistente.");
                var start = DateTimeOffset.UtcNow.AddDays(1);
                return Reply(new[] { new AppointmentInfo(Guid.NewGuid(), "52998224725", "Paciente Sintético", "Clínica", "Profissional", start, start.AddMinutes(30), "scheduled") });
            }
            if (path == "/api/municipal/units" && request.Method == HttpMethod.Get) return Reply(units.ToArray());
            if ((path == "/api/municipal/units" && request.Method == HttpMethod.Post) || (path.StartsWith("/api/municipal/units/", StringComparison.Ordinal) && request.Method == HttpMethod.Put))
            {
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
                var id = request.Method == HttpMethod.Post ? Guid.NewGuid() : Guid.Parse(path.Split('/').Last());
                var unit = new HealthUnit(id, body.RootElement.GetProperty("name").GetString()!, body.RootElement.GetProperty("region").GetString()!, body.RootElement.GetProperty("address").GetString()!, body.RootElement.GetProperty("latitude").GetDouble(), body.RootElement.GetProperty("longitude").GetDouble());
                units.RemoveAll(existing => existing.Id == id); AddUnit(unit); return Reply(unit);
            }
            if (path == "/api/municipal/summary")
                return Reply(new MunicipalSummary(1, 1, 1, 0, 3, 5, 2, 1, 2, 0, [new(7, 1, 0, 1, 2, 1, 0, 1, 0), new(30, 1, 0, 2, 3, 1, 1, 1, 0), new(90, 1, 0, 3, 5, 2, 1, 2, 0)]));
            throw new InvalidOperationException("Rota inesperada da fixture: " + request.Method + " " + path);
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
