using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Nora.Desktop.Models;

namespace Nora.Desktop.Checks;

internal static partial class Program
{
    /// <summary>
    /// Agenda por catálogo, capacidade, metas e alertas da fixture (docs/api.md, "Capacidade, agendamento e metas"). Sem profissional ativo a UBS aceita texto livre, como o servidor;
    /// com um, a marcação só aceita um horário livre da lista de disponibilidade. Escrita do catálogo é do administrador (atendente → 403).
    /// </summary>
    private sealed partial class ApiFixture
    {
        private sealed class SchedulingState
        {
            public List<Professional> Professionals { get; } = [];
            public List<Room> Rooms { get; } = [];
            public List<ScheduleTemplate> Schedule { get; } = [];
            public List<ScheduleBlock> Blocks { get; } = [];
            public List<AvailableSlot> Slots { get; } = [];
            public List<AlertItem> Alerts { get; } = [];
            public ActiveRooms ActiveRooms { get; set; } = new(0, 0);
            public CapacityMetrics? Capacity { get; set; }
        }

        private readonly Dictionary<Guid, SchedulingState> schedulingStates = [];
        public List<Specialty> Specialties { get; } = [];
        public List<Goal> Goals { get; } = [];
        public List<(Guid Unit, System.Collections.Specialized.NameValueCollection Query)> AvailabilityQueries { get; } = [];
        public List<System.Collections.Specialized.NameValueCollection> NetworkAlertQueries { get; } = [];
        /// <summary>Leituras de indicadores com a janela da capacidade do Painel (hoje à meia-noite local + 7 dias).</summary>
        public List<(Guid Unit, System.Collections.Specialized.NameValueCollection Query)> CapacityQueries { get; } = [];
        public List<JsonElement> AppointmentBodies { get; } = [];
        /// <summary>Todas as leituras de especialidades da rede ficam registradas em <see cref="Requests"/>; estas flags fazem uma rota específica falhar (500) sem derrubar o resto.</summary>
        public bool FailAvailability { get; set; }
        public bool FailAlerts { get; set; }
        public bool FailActiveRooms { get; set; }
        public HashSet<Guid> FailCatalogUnits { get; } = [];
        /// <summary>Código 409 que a próxima marcação pelo catálogo devolve (por exemplo, o horário ocupado por outra pessoa no meio do caminho).</summary>
        public string? NextBookingConflict { get; set; }
        public int ScheduleVersion { get; private set; }

        private SchedulingState Sched(Guid unitId) => schedulingStates.TryGetValue(unitId, out var state) ? state : schedulingStates[unitId] = new();
        public Specialty AddSpecialty(string name, bool active = true) { var item = new Specialty(Guid.NewGuid(), name, active); Specialties.Add(item); return item; }
        public Professional AddProfessional(Guid unitId, string name, Guid[] specialtyIds, bool active = true, string? acronym = null, string? number = null, string? state = null)
        { var item = new Professional(Guid.NewGuid(), name, acronym, number, state, active, specialtyIds); Sched(unitId).Professionals.Add(item); return item; }
        public Room AddRoom(Guid unitId, string name, bool active = true) { var item = new Room(Guid.NewGuid(), name, active); Sched(unitId).Rooms.Add(item); return item; }
        public ScheduleTemplate AddScheduleRow(Guid unitId, Professional professional, Room room, Guid specialtyId, int weekday = 1, string start = "08:00", string end = "12:00", int minutes = 30, string? validFrom = null, string? validUntil = null)
        { var item = new ScheduleTemplate(Guid.NewGuid(), professional.Id, weekday, start, end, minutes, room.Id, specialtyId, validFrom, validUntil); Sched(unitId).Schedule.Add(item); return item; }
        public AvailableSlot AddAvailableSlot(Guid unitId, Professional professional, Specialty specialty, Room room, DateTimeOffset start, int minutes = 30)
        { var item = new AvailableSlot(start, start.AddMinutes(minutes), professional.Id, professional.FullName, specialty.Id, specialty.Name, room.Id, room.Name); Sched(unitId).Slots.Add(item); return item; }
        public AlertItem AddAlert(Guid unitId, AlertItem item) { Sched(unitId).Alerts.Add(item); return item; }
        public ScheduleBlock AddBlockFor(Guid unitId, Guid? professionalId, DateTime day, string reason) { var item = new ScheduleBlock(Guid.NewGuid(), professionalId, new DateTimeOffset(day.Date), new DateTimeOffset(day.Date.AddDays(1)), reason); Sched(unitId).Blocks.Add(item); return item; }
        public void Reset(Guid unitId, bool alerts = false) { if (alerts) Sched(unitId).Alerts.Clear(); }
        public void SetActiveRooms(Guid unitId, int inUse, int planned) => Sched(unitId).ActiveRooms = new(inUse, planned);
        public void SetCapacity(Guid unitId, CapacityMetrics? capacity) => Sched(unitId).Capacity = capacity;
        /// <summary>Outra pessoa muda a situação de um cadastro por fora do cliente: a tela só descobre quando relê.</summary>
        public void SetProfessionalActive(Guid unitId, Guid id, bool active) { var list = Sched(unitId).Professionals; var index = list.FindIndex(item => item.Id == id); list[index] = list[index] with { IsActive = active }; }
        public void SetRoomActive(Guid unitId, Guid id, bool active) { var list = Sched(unitId).Rooms; var index = list.FindIndex(item => item.Id == id); list[index] = list[index] with { IsActive = active }; }
        public void SetRegistryActive(Guid unitId, string cpf, bool active) { var list = State(unitId).Registry; var index = list.FindIndex(item => item.Cpf == cpf); list[index] = list[index] with { IsActive = active }; }
        public IReadOnlyList<Professional> ProfessionalsOf(Guid unitId) => Sched(unitId).Professionals;
        public IReadOnlyList<Room> RoomsOf(Guid unitId) => Sched(unitId).Rooms;
        public IReadOnlyList<ScheduleTemplate> ScheduleOf(Guid unitId) => Sched(unitId).Schedule;
        public IReadOnlyList<ScheduleBlock> BlocksOf(Guid unitId) => Sched(unitId).Blocks;
        public IReadOnlyList<AvailableSlot> FreeSlotsOf(Guid unitId) => Sched(unitId).Slots;

        private static string NameKey(string text)
        {
            var decomposed = text.Normalize(NormalizationForm.FormD);
            var builder = new StringBuilder();
            foreach (var character in decomposed) if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark) builder.Append(character);
            return string.Join(' ', builder.ToString().ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }
        private static HttpResponseMessage Created<T>(T payload) => new(HttpStatusCode.Created) { Content = JsonContent.Create(payload) };
        private HttpResponseMessage? AdministratorOnly() => ObsRole == "administrator" ? null : Problem(HttpStatusCode.Forbidden, "forbidden");
        private static string? Text(JsonElement root, string name) => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        private static Guid? GuidOf(JsonElement root, string name) => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && Guid.TryParse(value.GetString(), out var id) ? id : null;

        /// <summary>Rotas globais do OBS (lista de especialidades da rede).</summary>
        private HttpResponseMessage? HandleGlobalScheduling(HttpRequestMessage request, string path)
            => path == "/api/obs/specialties" && request.Method == HttpMethod.Get ? Reply(new { specialties = Specialties.OrderBy(item => NameKey(item.Name), StringComparer.Ordinal).ToArray() }) : null;

        /// <summary>Rotas do OBS por UBS ligadas ao catálogo; devolve nulo para o que a fixture antiga já trata.</summary>
        private HttpResponseMessage? HandleUnitScheduling(HttpRequestMessage request, Guid unitId, string[] rest, JsonDocument? payload)
        {
            var state = Sched(unitId); var method = request.Method; var route = string.Join('/', rest);
            var query = System.Web.HttpUtility.ParseQueryString(request.RequestUri!.Query);
            JsonElement body = payload?.RootElement ?? default;
            switch (rest)
            {
                case ["professionals"] when method == HttpMethod.Get:
                    return FailCatalogUnits.Contains(unitId) ? Problem(HttpStatusCode.InternalServerError, "internal-error") : Reply(new { professionals = state.Professionals.ToArray() });
                case ["professionals"] when method == HttpMethod.Post:
                case ["professionals", _] when method == HttpMethod.Put:
                {
                    if (AdministratorOnly() is { } forbidden) return forbidden;
                    var name = Text(body, "fullName")?.Trim() ?? "";
                    var ids = body.TryGetProperty("specialtyIds", out var list) && list.ValueKind == JsonValueKind.Array ? list.EnumerateArray().Select(item => Guid.Parse(item.GetString()!)).ToArray() : [];
                    var council = new[] { Text(body, "councilAcronym"), Text(body, "councilNumber"), Text(body, "councilState") };
                    var editing = method == HttpMethod.Put ? Guid.Parse(rest[1]) : (Guid?)null;
                    // Especialidade desativada pela Secretaria só é aceita se já estava vinculada a este profissional (a edição a mantém); vínculo novo exige especialidade ativa.
                    var linked = editing is { } editingId ? state.Professionals.FirstOrDefault(item => item.Id == editingId)?.SpecialtyIds ?? Array.Empty<Guid>() : Array.Empty<Guid>();
                    if (name.Length is < 2 or > 160 || ids.Length == 0 || ids.Any(id => Specialties.FirstOrDefault(s => s.Id == id) is not { } specialty || (!specialty.IsActive && !linked.Contains(id))) || council.Any(item => item is null) != council.All(item => item is null)) return Problem(HttpStatusCode.BadRequest, "invalid-input");
                    if (state.Professionals.Any(item => item.Id != editing && string.Equals(item.FullName, name, StringComparison.OrdinalIgnoreCase))) return Problem(HttpStatusCode.Conflict, "conflict");
                    if (editing is { } id1)
                    {
                        var index = state.Professionals.FindIndex(item => item.Id == id1); if (index < 0) return Problem(HttpStatusCode.NotFound, "not-found");
                        if (state.Schedule.Any(row => row.ProfessionalId == id1 && !ids.Contains(row.SpecialtyId))) return Problem(HttpStatusCode.Conflict, "conflict");
                        state.Professionals[index] = state.Professionals[index] with { FullName = name, SpecialtyIds = ids, CouncilAcronym = council[0]?.ToUpperInvariant(), CouncilNumber = council[1], CouncilState = council[2]?.ToUpperInvariant() };
                        return Reply(state.Professionals[index]);
                    }
                    var created = new Professional(Guid.NewGuid(), name, council[0]?.ToUpperInvariant(), council[1], council[2]?.ToUpperInvariant(), true, ids);
                    state.Professionals.Add(created); return Created(created);
                }
                case ["professionals", var professionalText, "active"] when method == HttpMethod.Patch:
                {
                    if (AdministratorOnly() is { } forbidden) return forbidden;
                    var index = state.Professionals.FindIndex(item => item.Id == Guid.Parse(professionalText)); if (index < 0) return Problem(HttpStatusCode.NotFound, "not-found");
                    state.Professionals[index] = state.Professionals[index] with { IsActive = body.GetProperty("isActive").GetBoolean() };
                    return Reply(state.Professionals[index]);
                }
                case ["rooms"] when method == HttpMethod.Get: return Reply(new { rooms = state.Rooms.ToArray() });
                case ["rooms"] when method == HttpMethod.Post:
                case ["rooms", _] when method == HttpMethod.Put:
                {
                    if (AdministratorOnly() is { } forbidden) return forbidden;
                    var name = Text(body, "name")?.Trim() ?? ""; var editing = method == HttpMethod.Put ? Guid.Parse(rest[1]) : (Guid?)null;
                    if (name.Length is < 1 or > 80) return Problem(HttpStatusCode.BadRequest, "invalid-input");
                    if (state.Rooms.Any(item => item.Id != editing && string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase))) return Problem(HttpStatusCode.Conflict, "conflict");
                    if (editing is { } id2)
                    {
                        var index = state.Rooms.FindIndex(item => item.Id == id2); if (index < 0) return Problem(HttpStatusCode.NotFound, "not-found");
                        state.Rooms[index] = state.Rooms[index] with { Name = name }; return Reply(state.Rooms[index]);
                    }
                    var created = new Room(Guid.NewGuid(), name, true); state.Rooms.Add(created); return Created(created);
                }
                case ["rooms", var roomText, "active"] when method == HttpMethod.Patch:
                {
                    if (AdministratorOnly() is { } forbidden) return forbidden;
                    var index = state.Rooms.FindIndex(item => item.Id == Guid.Parse(roomText)); if (index < 0) return Problem(HttpStatusCode.NotFound, "not-found");
                    state.Rooms[index] = state.Rooms[index] with { IsActive = body.GetProperty("isActive").GetBoolean() }; return Reply(state.Rooms[index]);
                }
                case ["schedule"] when method == HttpMethod.Get:
                    return Reply(new { schedule = state.Schedule.Where(row => query["professionalId"] is not { } filter || row.ProfessionalId == Guid.Parse(filter)).ToArray() });
                case ["schedule"] when method == HttpMethod.Post:
                case ["schedule", _] when method == HttpMethod.Put:
                {
                    if (AdministratorOnly() is { } forbidden) return forbidden;
                    var editing = method == HttpMethod.Put ? Guid.Parse(rest[1]) : (Guid?)null;
                    var professionalId = GuidOf(body, "professionalId"); var roomId = GuidOf(body, "roomId"); var specialtyId = GuidOf(body, "specialtyId");
                    var start = Text(body, "startTime"); var end = Text(body, "endTime");
                    if (professionalId is null || roomId is null || specialtyId is null || start is null || end is null || !body.TryGetProperty("weekday", out var weekdayElement) || !body.TryGetProperty("slotMinutes", out var minutesElement)) return Problem(HttpStatusCode.BadRequest, "invalid-input");
                    var professional = state.Professionals.FirstOrDefault(item => item.Id == professionalId); var room = state.Rooms.FirstOrDefault(item => item.Id == roomId);
                    if (professional is null || room is null || !professional.SpecialtyIds.Contains(specialtyId.Value) || string.CompareOrdinal(end, start) <= 0) return Problem(HttpStatusCode.BadRequest, "invalid-input");
                    if (!professional.IsActive || !room.IsActive) return Problem(HttpStatusCode.Conflict, "conflict");
                    var weekday = weekdayElement.GetInt32(); var validFrom = Text(body, "validFrom"); var validUntil = Text(body, "validUntil");
                    // Sobreposição: mesmo dia da semana, horários que se cruzam (encostar não é cruzar) e vigências que se cruzam (nula = ilimitada), para o mesmo profissional ou o mesmo consultório.
                    bool Crosses(ScheduleTemplate row) => row.Id != editing && row.Weekday == weekday && (row.ProfessionalId == professionalId || row.RoomId == roomId)
                        && string.CompareOrdinal(row.StartTime, end) < 0 && string.CompareOrdinal(start, row.EndTime) < 0
                        && (validUntil is null || row.ValidFrom is null || string.CompareOrdinal(row.ValidFrom, validUntil) <= 0) && (validFrom is null || row.ValidUntil is null || string.CompareOrdinal(validFrom, row.ValidUntil) <= 0);
                    if (state.Schedule.Any(Crosses)) return Problem(HttpStatusCode.Conflict, "conflict");
                    var row = new ScheduleTemplate(editing ?? Guid.NewGuid(), professionalId.Value, weekday, start, end, minutesElement.GetInt32(), roomId.Value, specialtyId.Value, validFrom, validUntil);
                    if (editing is { } id3) { var index = state.Schedule.FindIndex(item => item.Id == id3); if (index < 0) return Problem(HttpStatusCode.NotFound, "not-found"); state.Schedule[index] = row; ScheduleVersion++; return Reply(row); }
                    state.Schedule.Add(row); ScheduleVersion++; return Created(row);
                }
                case ["schedule", var scheduleText] when method == HttpMethod.Delete:
                {
                    if (AdministratorOnly() is { } forbidden) return forbidden;
                    if (state.Schedule.RemoveAll(item => item.Id == Guid.Parse(scheduleText)) == 0) return Problem(HttpStatusCode.NotFound, "not-found");
                    ScheduleVersion++; return new(HttpStatusCode.NoContent);
                }
                case ["schedule-blocks"] when method == HttpMethod.Get: return Reply(new { blocks = state.Blocks.ToArray() });
                case ["schedule-blocks"] when method == HttpMethod.Post:
                {
                    if (AdministratorOnly() is { } forbidden) return forbidden;
                    var reason = Text(body, "reason")?.Trim() ?? ""; var startsAt = body.GetProperty("startsAt").GetDateTimeOffset(); var endsAt = body.GetProperty("endsAt").GetDateTimeOffset();
                    if (reason.Length is < 1 or > 120 || endsAt <= startsAt) return Problem(HttpStatusCode.BadRequest, "invalid-input");
                    var created = new ScheduleBlock(Guid.NewGuid(), GuidOf(body, "professionalId"), startsAt, endsAt, reason); state.Blocks.Add(created); return Created(created);
                }
                case ["schedule-blocks", var blockText] when method == HttpMethod.Delete:
                {
                    if (AdministratorOnly() is { } forbidden) return forbidden;
                    if (state.Blocks.RemoveAll(item => item.Id == Guid.Parse(blockText)) == 0) return Problem(HttpStatusCode.NotFound, "not-found"); return new(HttpStatusCode.NoContent);
                }
                case ["availability"] when method == HttpMethod.Get:
                {
                    AvailabilityQueries.Add((unitId, query));
                    if (FailAvailability) return Problem(HttpStatusCode.InternalServerError, "internal-error");
                    var from = DateTimeOffset.Parse(query["from"]!, CultureInfo.InvariantCulture); var to = DateTimeOffset.Parse(query["to"]!, CultureInfo.InvariantCulture);
                    if (query["specialtyId"] is not { } specialty || to <= from || to - from > TimeSpan.FromDays(92)) return Problem(HttpStatusCode.BadRequest, "invalid-input");
                    var professionalFilter = query["professionalId"] is { } professionalText ? Guid.Parse(professionalText) : (Guid?)null;
                    return Reply(new { slots = state.Slots.Where(slot => slot.SpecialtyId == Guid.Parse(specialty) && (professionalFilter is null || slot.ProfessionalId == professionalFilter) && slot.StartsAt >= from && slot.StartsAt < to && slot.StartsAt > DateTimeOffset.UtcNow).OrderBy(slot => slot.StartsAt).ToArray() });
                }
                case ["active-rooms"] when method == HttpMethod.Get: return FailActiveRooms ? Problem(HttpStatusCode.InternalServerError, "internal-error") : Reply(state.ActiveRooms);
                case ["alerts"] when method == HttpMethod.Get: return FailAlerts ? Problem(HttpStatusCode.InternalServerError, "internal-error") : Reply(new { alerts = state.Alerts.OrderBy(item => item.Kind, StringComparer.Ordinal).ToArray() });
                case ["appointments"] when method == HttpMethod.Post && state.Professionals.Any(item => item.IsActive):
                    return BookForStaff(unitId, state, body);
            }
            return null;
        }

        /// <summary>Marcação da equipe numa UBS que usa o catálogo (docs/api.md, "Agendamento pela equipe").</summary>
        private HttpResponseMessage BookForStaff(Guid unitId, SchedulingState state, JsonElement body)
        {
            AppointmentBodies.Add(body.Clone());
            if (GuidOf(body, "professionalId") is not { } professionalId || GuidOf(body, "specialtyId") is not { } specialtyId) return Problem(HttpStatusCode.BadRequest, "catalog-required");
            if (NextBookingConflict is { } code) { NextBookingConflict = null; return Problem(HttpStatusCode.Conflict, code); }
            var cpf = body.GetProperty("cpf").GetString()!; var startsAt = body.GetProperty("startsAt").GetDateTimeOffset();
            if (startsAt <= DateTimeOffset.UtcNow) return Problem(HttpStatusCode.BadRequest, "invalid-input");
            var slot = state.Slots.FirstOrDefault(item => item.ProfessionalId == professionalId && item.SpecialtyId == specialtyId && item.StartsAt == startsAt);
            if (slot is null) return Problem(HttpStatusCode.Conflict, "slot-unavailable");
            var appointments = State(unitId).Appointments;
            if (appointments.Any(item => item.Cpf == cpf && item.Status is "scheduled" or "called" && item.StartsAt < slot.EndsAt && slot.StartsAt < item.EndsAt)) return Problem(HttpStatusCode.Conflict, "patient-overlap");
            var patient = State(unitId).Registry.FirstOrDefault(item => item.Cpf == cpf && item.IsActive);
            if (patient is null) return Problem(HttpStatusCode.NotFound, "not-found");
            var created = new AppointmentInfo(Guid.NewGuid(), cpf, patient.FullName, slot.SpecialtyName, slot.ProfessionalName, slot.StartsAt, slot.EndsAt, "scheduled", null, "staff", slot.ProfessionalId, slot.SpecialtyId);
            appointments.Add(created); state.Slots.Remove(slot);
            return Created(created);
        }

        /// <summary>Rotas da Secretaria: especialidades da rede, metas, alertas da rede e leitura do catálogo de uma UBS.</summary>
        private HttpResponseMessage? HandleMunicipalScheduling(HttpRequestMessage request, string path, System.Collections.Specialized.NameValueCollection query, JsonDocument? payload)
        {
            var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries); var method = request.Method; JsonElement body = payload?.RootElement ?? default;
            switch (parts)
            {
                case ["api", "municipal", "specialties"] when method == HttpMethod.Get:
                    return Reply(new { specialties = Specialties.OrderBy(item => NameKey(item.Name), StringComparer.Ordinal).ToArray() });
                case ["api", "municipal", "specialties"] when method == HttpMethod.Post:
                case ["api", "municipal", "specialties", _] when method == HttpMethod.Put:
                {
                    var name = string.Join(' ', (Text(body, "name") ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
                    var editing = method == HttpMethod.Put ? Guid.Parse(parts[3]) : (Guid?)null;
                    if (name.Length is < 2 or > 120) return Problem(HttpStatusCode.BadRequest, "invalid-input");
                    if (Specialties.Any(item => item.Id != editing && NameKey(item.Name) == NameKey(name))) return Problem(HttpStatusCode.Conflict, "conflict");
                    if (editing is { } id)
                    {
                        var index = Specialties.FindIndex(item => item.Id == id); if (index < 0) return Problem(HttpStatusCode.NotFound, "not-found");
                        Specialties[index] = Specialties[index] with { Name = name }; return Reply(Specialties[index]);
                    }
                    return Created(AddSpecialty(name));
                }
                case ["api", "municipal", "specialties", var specialtyText, "active"] when method == HttpMethod.Patch:
                {
                    var index = Specialties.FindIndex(item => item.Id == Guid.Parse(specialtyText)); if (index < 0) return Problem(HttpStatusCode.NotFound, "not-found");
                    Specialties[index] = Specialties[index] with { IsActive = body.GetProperty("isActive").GetBoolean() }; return Reply(Specialties[index]);
                }
                case ["api", "municipal", "goals"] when method == HttpMethod.Get: return Reply(new { goals = Goals.ToArray() });
                case ["api", "municipal", "goals", var metric] when method == HttpMethod.Put:
                {
                    if (!GoalMetric.All.Contains(metric) || !body.TryGetProperty("target", out var targetElement)) return Problem(HttpStatusCode.BadRequest, "invalid-input");
                    var target = targetElement.GetDecimal(); var unit = GuidOf(body, "unitId");
                    if (unit is { } known && units.All(item => item.Id != known)) return Problem(HttpStatusCode.NotFound, "not-found");
                    if ((metric != GoalMetric.AverageWait && target is < 0 or > 1) || (metric == GoalMetric.AverageWait && target <= 0)) return Problem(HttpStatusCode.BadRequest, "invalid-input");
                    Goals.RemoveAll(goal => goal.Metric == metric && goal.UnitId == unit);
                    var saved = new Goal(metric, unit, target); Goals.Add(saved); return Reply(saved);
                }
                case ["api", "municipal", "goals", var metric] when method == HttpMethod.Delete:
                {
                    var unit = query["unitId"] is { } unitText ? Guid.Parse(unitText) : (Guid?)null;
                    return Goals.RemoveAll(goal => goal.Metric == metric && goal.UnitId == unit) == 0 ? Problem(HttpStatusCode.NotFound, "not-found") : new(HttpStatusCode.NoContent);
                }
                case ["api", "municipal", "alerts"] when method == HttpMethod.Get:
                {
                    NetworkAlertQueries.Add(query);
                    if (FailAlerts) return Problem(HttpStatusCode.InternalServerError, "internal-error");
                    var unit = query["unitId"] is { } unitText ? Guid.Parse(unitText) : (Guid?)null; var region = query["region"];
                    if (unit is { } id && units.All(item => item.Id != id)) return Problem(HttpStatusCode.NotFound, "not-found");
                    var scoped = units.Where(item => !item.DatabasePending && (unit is null || item.Id == unit) && (string.IsNullOrEmpty(region) || string.Equals(item.Region, region, StringComparison.OrdinalIgnoreCase)));
                    return Reply(new { alerts = scoped.SelectMany(item => Sched(item.Id).Alerts).OrderBy(item => item.UnitName, StringComparer.Ordinal).ThenBy(item => item.Kind, StringComparer.Ordinal).ToArray() });
                }
                case ["api", "municipal", "units", var unitText, "professionals" or "rooms" or "schedule"] when method == HttpMethod.Get:
                {
                    var id = Guid.Parse(unitText); if (units.All(item => item.Id != id)) return Problem(HttpStatusCode.NotFound, "not-found");
                    if (FailCatalogUnits.Contains(id)) return Problem(HttpStatusCode.InternalServerError, "internal-error");
                    var state = Sched(id);
                    return parts[4] switch { "professionals" => Reply(new { professionals = state.Professionals.ToArray() }), "rooms" => Reply(new { rooms = state.Rooms.ToArray() }), _ => Reply(new { schedule = state.Schedule.ToArray() }) };
                }
            }
            return null;
        }

        // ---- Metas e capacidade dos indicadores ----

        /// <summary>Situação de cada meta aplicável, como a API: meta da UBS ou, sem ela, a da rede; "perto" = até 10% da meta.</summary>
        private GoalStatus[] GoalStatuses(Guid? unitId, double? attendanceScore, double? missedRate, double? averageWaitMinutes)
        {
            var result = new List<GoalStatus>();
            foreach (var metric in GoalMetric.All)
            {
                var goal = (unitId is null ? null : Goals.FirstOrDefault(item => item.Metric == metric && item.UnitId == unitId)) ?? Goals.FirstOrDefault(item => item.Metric == metric && item.UnitId is null);
                if (goal is null) continue;
                var value = metric switch { GoalMetric.Score => attendanceScore, GoalMetric.MissedRate => missedRate, _ => averageWaitMinutes };
                string status;
                if (value is null) status = "no-data";
                else if (metric == GoalMetric.Score) { var v = (decimal)value.Value; status = v >= goal.Target ? "met" : v >= goal.Target * 0.9m ? "near" : "off"; }
                else { var v = (decimal)value.Value; status = v <= goal.Target ? "met" : v <= goal.Target * 1.1m ? "near" : "off"; }
                result.Add(new(metric, goal.Target, value, status));
            }
            return [.. result];
        }
        private static double? MissedRate(AppointmentMetrics appointments) => appointments.Completed + appointments.Missed == 0 ? null : Math.Round((double)appointments.Missed / (appointments.Completed + appointments.Missed), 4);
        private static readonly CapacityMetrics DefaultCapacity = new(40, 30, 0.75);
        private CapacityMetrics UnitCapacityOf(Guid unitId) => Sched(unitId).Capacity ?? DefaultCapacity;
        private static double? OccupancyOf(long offered, long booked) => offered == 0 ? null : Math.Round((double)booked / offered, 4);
    }
}
