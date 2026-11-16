using System.Globalization;
using System.Text.RegularExpressions;
using Nora.Api.DAOs;
using Nora.Api.Models;

namespace Nora.Api.Services;

/// <summary>
/// Regras da agenda da Fase 3: valida os dados do catálogo (profissionais, consultórios, grade, bloqueios), calcula a disponibilidade e a capacidade,
/// marca consultas pelo catálogo (equipe e paciente) e confirma presença. A especialidade vem da lista central; tudo o mais vive no banco da UBS.
/// </summary>
public sealed partial class SchedulingService
{
    /// <summary>Janela do paciente: início do atendimento de 24 h até 90 dias à frente (limites incluídos).</summary>
    public static readonly TimeSpan PatientMinimumLead = TimeSpan.FromHours(24);
    public static readonly TimeSpan PatientMaximumLead = TimeSpan.FromDays(90);
    /// <summary>Intervalo máximo de uma consulta de horários livres.</summary>
    public const int MaxAvailabilityDays = 92;
    private static readonly HashSet<string> States = ["AC", "AL", "AP", "AM", "BA", "CE", "DF", "ES", "GO", "MA", "MT", "MS", "MG", "PA", "PB", "PR", "PE", "PI", "RJ", "RN", "RS", "RO", "RR", "SC", "SP", "SE", "TO"];
    [GeneratedRegex("^[A-Za-z]{2,10}$")] private static partial Regex AcronymPattern();
    [GeneratedRegex("^[A-Za-z0-9./-]{1,20}$")] private static partial Regex CouncilNumberPattern();

    private readonly SchedulingDao dao;
    private readonly SpecialtyDao specialties;
    private readonly SchedulingAccessService access;
    private readonly HealthUnitDao units;
    private readonly TimeProvider time;
    private readonly SlotEngine engine;

    public SchedulingService(SchedulingDao dao, SpecialtyDao specialties, SchedulingAccessService access, HealthUnitDao units, TimeProvider time, IConfiguration configuration)
    {
        this.dao = dao; this.specialties = specialties; this.access = access; this.units = units; this.time = time;
        var zone = UnitMetricsDao.ConfiguredZone(configuration);
        try { engine = new(TimeZoneInfo.FindSystemTimeZoneById(zone)); }
        catch (Exception error) when (error is TimeZoneNotFoundException or InvalidTimeZoneException) { throw new InvalidOperationException($"O fuso horário '{zone}' de Nora:TimeZone não está disponível no sistema operacional da API.", error); }
    }

    // ---- Validação do catálogo ----

    private static string Name(string? value, int minimum, int maximum)
    {
        var text = string.Join(' ', (value ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (text.Length < minimum || text.Length > maximum || text.Any(char.IsControl)) throw new ApiInputException();
        return text;
    }

    public async Task<ProfessionalRequest> ValidateProfessionalAsync(ProfessionalRequest request, CancellationToken ct)
    {
        var name = Name(request.FullName, 2, 160);
        var ids = request.SpecialtyIds?.Distinct().ToList() ?? [];
        if (ids.Count is < 1 or > 50 || ids.Contains(Guid.Empty)) throw new ApiInputException();
        var found = await specialties.FindAsync(ids, ct);
        if (ids.Any(id => !found.TryGetValue(id, out var item) || !item.IsActive)) throw new ApiInputException();
        var acronym = request.CouncilAcronym?.Trim() ?? ""; var number = request.CouncilNumber?.Trim() ?? ""; var state = request.CouncilState?.Trim() ?? "";
        if (acronym.Length + number.Length + state.Length == 0) return new(name, ids, null, null, null);
        if (!AcronymPattern().IsMatch(acronym) || !CouncilNumberPattern().IsMatch(number) || !States.Contains(state.ToUpperInvariant())) throw new ApiInputException();
        return new(name, ids, acronym.ToUpperInvariant(), number, state.ToUpperInvariant());
    }

    public static string ValidateRoomName(RoomRequest request) => Name(request.Name, 1, 80);

    public async Task<(ScheduleTemplateDraft Draft, Specialty Specialty)> ValidateTemplateAsync(ScheduleTemplateRequest request, CancellationToken ct)
    {
        if (request.ProfessionalId is not { } professional || professional == Guid.Empty || request.RoomId is not { } room || room == Guid.Empty || request.SpecialtyId is not { } specialtyId || specialtyId == Guid.Empty ||
            request.Weekday is not (>= 0 and <= 6) || request.SlotMinutes is not (>= 5 and <= 240) ||
            !TimeOnly.TryParseExact(request.StartTime, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var start) || !TimeOnly.TryParseExact(request.EndTime, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var end) ||
            end <= start || (end - start).TotalMinutes < request.SlotMinutes) throw new ApiInputException();
        var from = Date(request.ValidFrom); var until = Date(request.ValidUntil);
        if (from is { } first && until is { } last && last < first) throw new ApiInputException();
        var found = await specialties.FindAsync([specialtyId], ct);
        if (!found.TryGetValue(specialtyId, out var specialty) || !specialty.IsActive) throw new ApiInputException();
        return (new(professional, request.Weekday.Value, start, end, request.SlotMinutes.Value, room, specialtyId, from, until), specialty);
    }

    private static DateOnly? Date(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : throw new ApiInputException();
    }

    public static (Guid? ProfessionalId, DateTimeOffset StartsAt, DateTimeOffset EndsAt, string Reason) ValidateBlock(ScheduleBlockRequest request)
    {
        if (request.StartsAt is not { } start || request.EndsAt is not { } end || end <= start || end - start > TimeSpan.FromDays(366) || request.ProfessionalId == Guid.Empty) throw new ApiInputException();
        return (request.ProfessionalId, start, end, Name(request.Reason, 1, 120));
    }

    // ---- Disponibilidade e capacidade ----

    /// <summary>Com ao menos um profissional ativo, a UBS marca só pelo catálogo.</summary>
    public Task<bool> UsesCatalogAsync(Guid unitId, CancellationToken ct) => dao.HasActiveProfessionalAsync(unitId, ct);

    /// <summary>
    /// Atendimentos livres da especialidade (e do profissional, se informado) com início em [from, to): grade − bloqueios − consultas ativas − vagas relâmpago abertas ou ofertadas.
    /// Equipe: só no futuro. Paciente: só de 24 h a 90 dias à frente. Especialidade inativa → lista vazia; inexistente → 400.
    /// </summary>
    public async Task<List<AvailableSlot>> AvailabilityAsync(Guid unitId, DateTimeOffset from, DateTimeOffset to, Guid? specialtyId, Guid? professionalId, bool forPatient, CancellationToken ct)
    {
        if (specialtyId is not { } id || id == Guid.Empty || professionalId == Guid.Empty || to <= from || to - from > TimeSpan.FromDays(MaxAvailabilityDays)) throw new ApiInputException();
        var specialty = (await specialties.FindAsync([id], ct)).GetValueOrDefault(id) ?? throw new ApiInputException();
        var now = time.GetUtcNow();
        var lower = forPatient ? now + PatientMinimumLead : now.AddTicks(1);
        var effectiveFrom = from > lower ? from : lower;
        var effectiveTo = forPatient && to > now + PatientMaximumLead ? now + PatientMaximumLead + TimeSpan.FromTicks(1) : to;
        if (!specialty.IsActive || effectiveTo <= effectiveFrom) return [];
        var data = await dao.LoadAsync(unitId, effectiveFrom, effectiveTo, professionalId, ct);
        return engine.Generate(data.Templates.Where(template => template.SpecialtyId == id).ToList(), effectiveFrom, effectiveTo)
            .Where(slot => SlotEngine.StateOf(data, slot) == SlotState.Free)
            .OrderBy(slot => slot.Start).ThenBy(slot => slot.Template.ProfessionalName, StringComparer.OrdinalIgnoreCase)
            .Select(slot => new AvailableSlot(new DateTimeOffset(slot.Start, TimeSpan.Zero), new DateTimeOffset(slot.End, TimeSpan.Zero), slot.Template.ProfessionalId, slot.Template.ProfessionalName, specialty.Id, specialty.Name, slot.Template.RoomId, slot.Template.RoomName)).ToList();
    }

    /// <summary>
    /// Atendimentos ofertados pela grade com início em [from, to), menos os bloqueados (UBS ou profissional), e quantos deles têm consulta ocupando o horário.
    /// Só profissionais e consultórios ativos ofertam. <paramref name="specialtyIds"/> restringe a grade a essas especialidades (vazio = nenhuma; nulo = todas).
    /// </summary>
    public async Task<CapacityMetrics> ReadCapacityAsync(Guid unitId, DateTimeOffset from, DateTimeOffset to, IReadOnlyCollection<Guid>? specialtyIds, CancellationToken ct)
    {
        if (specialtyIds is { Count: 0 }) return new(0, 0, null);
        var data = await dao.LoadAsync(unitId, from, to, null, ct);
        var templates = specialtyIds is null ? data.Templates : data.Templates.Where(template => specialtyIds.Contains(template.SpecialtyId)).ToList();
        long offered = 0, booked = 0;
        foreach (var slot in engine.Generate(templates, from, to))
        {
            var state = SlotEngine.StateOf(data, slot);
            if (state == SlotState.Blocked) continue;
            offered++;
            if (state == SlotState.Booked) booked++;
        }
        return new(offered, booked, Occupancy(offered, booked));
    }

    public static double? Occupancy(long offered, long booked) => offered == 0 ? null : MetricsService.Round((double)booked / offered);

    /// <summary>Especialidade da lista central a partir do texto livre do filtro de indicadores (mesma comparação do nome único). Nulo se não existir.</summary>
    public async Task<Guid?> FindSpecialtyIdAsync(string text, CancellationToken ct) => (await specialties.FindByNameAsync(text, ct))?.Id;

    public async Task<ActiveRooms> ReadActiveRoomsAsync(Guid unitId, CancellationToken ct)
    {
        var zone = engine.Zone;
        var local = TimeZoneInfo.ConvertTime(time.GetUtcNow(), zone);
        var today = DateOnly.FromDateTime(local.DateTime);
        var start = today.ToDateTime(TimeOnly.MinValue); var next = start.AddDays(1);
        return await dao.ReadActiveRoomsAsync(unitId, new(start, zone.GetUtcOffset(start)), new(next, zone.GetUtcOffset(next)), today, ct);
    }

    // ---- Marcação ----

    /// <summary>Marcação da equipe numa UBS que já usa o catálogo (chamada por <c>AppointmentDao.CreateAsync</c>). Sem profissional e especialidade do catálogo → 400 catalog-required.</summary>
    internal async Task<AppointmentInfo> BookForStaffAsync(Guid unitId, Guid actorId, CreateAppointmentRequest request, CancellationToken ct)
    {
        if (request.ProfessionalId is not { } professional || professional == Guid.Empty || request.SpecialtyId is not { } specialtyId || specialtyId == Guid.Empty) throw new ApiCodedException(StatusCodes.Status400BadRequest, "catalog-required");
        var cpf = AccountService.NormalizeCpf(request.Cpf);
        var start = request.StartsAt.ToUniversalTime();
        if (!AccountService.ValidCpf(cpf) || start <= time.GetUtcNow()) throw new ApiInputException();
        var specialty = (await specialties.FindAsync([specialtyId], ct)).GetValueOrDefault(specialtyId);
        if (specialty is not { IsActive: true }) throw new ApiInputException();
        return await dao.BookAsync(unitId, actorId, cpf, professional, specialty, start, request.EndsAt == default ? null : request.EndsAt, "staff", engine, ct);
    }

    /// <summary>
    /// Marcação pelo próprio paciente: vale na hora, só num atendimento livre da grade de 24 h a 90 dias à frente, e uma consulta futura por especialidade marcada pelo app em toda a rede
    /// (UBS vinculadas ao paciente). Ordem das recusas (todas 409): janela (<c>booking-window</c>), limite (<c>specialty-limit</c>), horário (<c>slot-unavailable</c>).
    /// </summary>
    public async Task<AppointmentInfo> PatientBookAsync(SessionInfo session, Guid unitId, PatientBookingRequest request, CancellationToken ct)
    {
        if (request.ProfessionalId is not { } professional || professional == Guid.Empty || request.SpecialtyId is not { } specialtyId || specialtyId == Guid.Empty || request.StartsAt is not { } requested) throw new ApiInputException();
        var specialty = (await specialties.FindAsync([specialtyId], ct)).GetValueOrDefault(specialtyId);
        if (specialty is not { IsActive: true }) throw new ApiInputException();
        var start = requested.ToUniversalTime();
        return await access.ExecutePatientNetworkAsync(session, unitId, async cpf =>
        {
            var now = time.GetUtcNow();
            if (start < now + PatientMinimumLead || start > now + PatientMaximumLead) throw new ApiCodedException(StatusCodes.Status409Conflict, "booking-window");
            foreach (var unit in await units.ListForPatientAsync(session.User.Id, ct))
                if (unit.DatabaseState == "ready" && await dao.CountPatientBookingsAsync(unit.Id, cpf, specialtyId, now, ct) > 0) throw new ApiCodedException(StatusCodes.Status409Conflict, "specialty-limit");
            return await dao.BookAsync(unitId, session.User.Id, cpf, professional, specialty, start, null, "patient", engine, ct);
        }, ct);
    }
}
