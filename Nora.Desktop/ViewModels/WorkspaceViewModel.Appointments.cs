using System.Collections.ObjectModel;
using System.Globalization;
using Nora.Desktop.Models;
using Nora.Desktop.Services;

namespace Nora.Desktop.ViewModels;

/// <summary>Agenda: uma leitura por mês visível; dia, semana e marcações do calendário são derivados dela, sem novas requisições.</summary>
public sealed partial class WorkspaceViewModel
{
    private UnitPatientRecord? appointmentPatient;
    private AppointmentInfo? selectedAppointment;
    private string appointmentSpecialty = "", appointmentProfessional = "", appointmentStart = "09:00", appointmentEnd = "09:30";
    private IReadOnlyList<AppointmentInfo>? todayOverview;
    private DateTime todayOverviewDate;
    private string agendaViewMode = "Semana";
    private DateTime selectedAgendaDate = DateTime.Today;
    private IReadOnlyList<AppointmentInfo>? monthAppointments;
    private (int Year, int Month)? monthKey;
    private bool agendaLoadPending;
    private static readonly string[] AgendaActionNames =
        [nameof(CanCallAppointment), nameof(CanCompleteAppointment), nameof(CanMissAppointment), nameof(CanCancelAppointment), nameof(CanCreateLightning)];

    private string TodayCount(params string[] statuses) => HasActiveObsUnit && todayOverview is not null && todayOverviewDate == DateTime.Today
        ? todayOverview.Count(item => statuses.Contains(item.Status, StringComparer.Ordinal)).ToString() : "—";
    public string TodayCompletedCount => TodayCount("completed");
    public string TodayScheduledCount => TodayCount("scheduled", "called");
    public string TodayMissedCount => TodayCount("missed");
    public string TodayCancelledCount => TodayCount("cancelled");
    private void SetTodayOverview(DateTime date, IReadOnlyList<AppointmentInfo>? records)
    {
        todayOverviewDate = date; todayOverview = records;
        Notify(nameof(TodayCompletedCount)); Notify(nameof(TodayScheduledCount)); Notify(nameof(TodayMissedCount)); Notify(nameof(TodayCancelledCount));
    }

    public string AgendaViewMode
    {
        get => agendaViewMode;
        private set { if (agendaViewMode == value) return; agendaViewMode = value; RebuildAgendaView(); Notify(); Notify(nameof(IsAgendaDay)); Notify(nameof(IsAgendaWeek)); Notify(nameof(AgendaRangeLabel)); }
    }
    public bool IsAgendaDay => AgendaViewMode == "Dia";
    public bool IsAgendaWeek => AgendaViewMode == "Semana";
    public string AgendaRangeLabel => IsAgendaDay ? selectedAgendaDate.ToString("dddd, dd 'de' MMMM") : $"Semana de {StartOfWeek(selectedAgendaDate):dd/MM} a {StartOfWeek(selectedAgendaDate).AddDays(6):dd/MM}";
    public string SelectedAgendaDateLabel => selectedAgendaDate.ToString("dddd, dd 'de' MMMM 'de' yyyy");
    public ObservableCollection<AgendaCalendarDay> AgendaDays { get; } = [];
    public ObservableCollection<AppointmentInfo> Appointments { get; } = [];
    /// <summary>Texto de estado da lista: ainda carregando, sem consultas no período ou vazio quando há consultas.</summary>
    public string AgendaStateText => !HasActiveObsUnit ? "Escolha uma UBS no Painel da UBS para ver a agenda."
        : monthKey != CurrentMonthKey ? "Carregando a agenda do mês…"
        : Appointments.Count == 0 ? (IsAgendaDay ? "Nenhuma consulta neste dia." : "Nenhuma consulta nesta semana.") : "";

    public UnitPatientRecord? AppointmentPatient { get => appointmentPatient; set { appointmentPatient = value; Notify(); } }
    public AppointmentInfo? SelectedAppointment { get => selectedAppointment; set { selectedAppointment = value; Notify(); foreach (var name in AgendaActionNames) Notify(name); } }
    public string AppointmentSpecialty { get => appointmentSpecialty; set { appointmentSpecialty = value; Notify(); } }
    public string AppointmentProfessional { get => appointmentProfessional; set { appointmentProfessional = value; Notify(); } }
    public string AppointmentStart { get => appointmentStart; set { appointmentStart = value; Notify(); } }
    public string AppointmentEnd { get => appointmentEnd; set { appointmentEnd = value; Notify(); } }

    // Botões de situação seguem as transições aceitas pela API; sem seleção, tudo fica desabilitado.
    public bool CanCallAppointment => CanOperate && SelectedAppointment?.Status == "scheduled";
    public bool CanCompleteAppointment => CanOperate && SelectedAppointment?.Status == "called";
    public bool CanMissAppointment => CanOperate && SelectedAppointment?.Status is "scheduled" or "called";
    public bool CanCancelAppointment => CanOperate && SelectedAppointment?.Status is "scheduled" or "called";
    public bool CanCreateLightning => CanOperate && SelectedAppointment is { Status: "cancelled" } appointment && appointment.StartsAt > DateTimeOffset.Now;

    private (int Year, int Month) CurrentMonthKey => (selectedAgendaDate.Year, selectedAgendaDate.Month);
    private static DateTime StartOfWeek(DateTime date)
    {
        var offset = ((int)date.DayOfWeek + 6) % 7;
        return date.Date.AddDays(-offset);
    }
    /// <summary>Intervalo exibido pelo calendário (semanas inteiras do mês): no máximo 42 dias, uma só requisição.</summary>
    private static (DateTimeOffset From, DateTimeOffset To) AgendaMonthRange(DateTime date)
    {
        var first = new DateTime(date.Year, date.Month, 1);
        return (new DateTimeOffset(StartOfWeek(first)), new DateTimeOffset(StartOfWeek(first.AddMonths(1).AddDays(-1)).AddDays(7)));
    }
    private (DateTime From, DateTime To) AgendaRange()
    {
        var first = IsAgendaWeek ? StartOfWeek(selectedAgendaDate) : selectedAgendaDate.Date;
        return (first, first.AddDays(IsAgendaWeek ? 7 : 1));
    }

    private void BuildAgendaDays()
    {
        AgendaDays.Clear();
        var first = new DateTime(selectedAgendaDate.Year, selectedAgendaDate.Month, 1);
        var start = StartOfWeek(first);
        var last = first.AddMonths(1).AddDays(-1);
        var daysInGrid = (StartOfWeek(last).AddDays(6) - start).Days + 1;
        for (var offset = 0; offset < daysInGrid; offset++)
        {
            var date = start.AddDays(offset);
            AgendaDays.Add(new AgendaCalendarDay(date, date.Month == first.Month) { IsSelected = date == selectedAgendaDate });
        }
        ApplyCalendarMarks();
    }

    /// <summary>Marca no calendário a quantidade de consultas de cada dia, derivada da leitura do mês.</summary>
    private void ApplyCalendarMarks()
    {
        Dictionary<DateTime, int>? counts = monthKey == CurrentMonthKey && monthAppointments is not null
            ? monthAppointments.GroupBy(item => item.StartsAt.ToLocalTime().Date).ToDictionary(group => group.Key, group => group.Count()) : null;
        foreach (var day in AgendaDays) day.AppointmentCount = counts is not null && counts.TryGetValue(day.Date, out var count) ? count : 0;
    }

    /// <summary>Recalcula a lista de dia/semana a partir da leitura do mês; mantém a consulta selecionada quando ela continua visível.</summary>
    private void RebuildAgendaView()
    {
        var selectedId = SelectedAppointment?.Id;
        if (monthKey == CurrentMonthKey && monthAppointments is not null)
        {
            var (from, to) = AgendaRange();
            Replace(Appointments, monthAppointments.Where(item => item.StartsAt.ToLocalTime() >= from && item.StartsAt.ToLocalTime() < to).OrderBy(item => item.StartsAt).ThenBy(item => item.Id).ToArray());
        }
        else Appointments.Clear();
        SelectedAppointment = selectedId is null ? null : Appointments.FirstOrDefault(item => item.Id == selectedId);
        ApplyCalendarMarks();
        Notify(nameof(AgendaStateText));
    }

    private void SetMonthCache((int Year, int Month) key, IReadOnlyList<AppointmentInfo> records)
    {
        monthKey = key; monthAppointments = records;
        RebuildAgendaView();
    }

    private void SelectCalendarDay(AgendaCalendarDay? day)
    {
        if (day == null) return;
        ChangeAgendaDate(day.Date, "Dia");
    }

    public void MoveAgenda(int direction) => ChangeAgendaDate(selectedAgendaDate.AddDays(direction * (IsAgendaWeek ? 7 : 1)), null);

    private void ChangeAgendaDate(DateTime date, string? mode)
    {
        var monthChanged = date.Year != selectedAgendaDate.Year || date.Month != selectedAgendaDate.Month;
        var dayChanged = date.Date != selectedAgendaDate;
        selectedAgendaDate = date.Date;
        if (mode is not null) agendaViewMode = mode;
        if (monthChanged) BuildAgendaDays();
        else foreach (var day in AgendaDays) day.IsSelected = day.Date == selectedAgendaDate;
        RebuildAgendaView();
        Notify(nameof(AgendaViewMode)); Notify(nameof(IsAgendaDay)); Notify(nameof(IsAgendaWeek));
        Notify(nameof(AgendaRangeLabel)); Notify(nameof(SelectedAgendaDateLabel));
        // Só um mês novo exige leitura; trocar dia, semana ou modo dentro do mês usa o que já está na memória.
        if (monthChanged && HasActiveObsUnit) RequestAgendaLoad();
        // Os horários livres partem do dia escolhido: uma leitura por mudança de dia, só com especialidade e profissional escolhidos.
        if (dayChanged && UsesCatalogBooking) { Notify(nameof(BookingWindowLabel)); ReloadBookingSlots(); }
    }

    private void RequestAgendaLoad()
    {
        agendaLoadPending = true;
        StartBackground(() =>
        {
            if (!agendaLoadPending) return Task.CompletedTask;
            agendaLoadPending = false;
            return ActiveObsUnit is { } unit ? LoadUnitScopeAsync(unit, UnitScope.Agenda) : Task.CompletedTask;
        });
    }

    public Task LoadAppointmentsAsync() => RunAsync(async () =>
    {
        var unit = ActiveObsUnit ?? throw new NoraApiException("Escolha uma UBS no Painel da UBS antes de consultar a agenda.");
        // As mensagens do agendamento mandam usar "Atualizar agenda": ela também relê o cadastro (profissionais e especialidades) e os horários livres, senão o botão não resolveria
        // um cadastro que falhou, um profissional novo cadastrado em outro computador nem uma busca de horários que falhou.
        await LoadUnitScopeAsync(unit, UnitScope.Agenda | UnitScope.Catalog);
        if (ActiveObsUnit?.Id != unit.Id) return;
        ReloadBookingSlots();
        if (catalogFailed) Say(MessageKind.Error, "A agenda foi atualizada, mas o cadastro de profissionais não pôde ser lido agora: o agendamento segue indisponível. Tente de novo.");
        else Say(MessageKind.Success, "Agenda atualizada.");
    });

    /// <summary>Confere o agendamento antes de enviar; a API repete as mesmas regras.</summary>
    private (DateTimeOffset Start, DateTimeOffset End, UnitPatientRecord Patient) ValidateAppointmentDraft()
    {
        var patient = AppointmentPatient ?? throw new NoraApiException("Selecione o paciente cadastrado na UBS.");
        if (ObsRegistry.FirstOrDefault(item => item.Cpf == patient.Cpf) is not { IsActive: true })
            throw new NoraApiException("O paciente precisa ter um cadastro ativo nesta UBS.");
        var specialty = AppointmentSpecialty.Trim(); var professional = AppointmentProfessional.Trim();
        if (specialty.Length == 0 || professional.Length == 0) throw new NoraApiException("Informe a especialidade e o profissional.");
        if (specialty.Length > 120) throw new NoraApiException("A especialidade pode ter até 120 caracteres.");
        if (professional.Length > 160) throw new NoraApiException("O profissional pode ter até 160 caracteres.");
        if (!TimeSpan.TryParseExact(AppointmentStart.Trim(), @"hh\:mm", CultureInfo.InvariantCulture, out var start) ||
            !TimeSpan.TryParseExact(AppointmentEnd.Trim(), @"hh\:mm", CultureInfo.InvariantCulture, out var end))
            throw new NoraApiException("Informe os horários no formato HH:mm.");
        var startsAt = new DateTimeOffset(selectedAgendaDate.Date.Add(start)); var endsAt = new DateTimeOffset(selectedAgendaDate.Date.Add(end));
        if (startsAt <= DateTimeOffset.Now) throw new NoraApiException("O início da consulta precisa ser no futuro.");
        if (endsAt <= startsAt) throw new NoraApiException("O horário final precisa ser depois do inicial.");
        if (endsAt - startsAt > TimeSpan.FromHours(12)) throw new NoraApiException("A consulta pode durar no máximo 12 horas.");
        return (startsAt, endsAt, patient);
    }

    public Task CreateAppointmentAsync() => RunAsync(async () =>
    {
        var unit = ActiveObsUnit ?? throw new NoraApiException("Escolha uma UBS no Painel da UBS antes de agendar.");
        if (catalogFailed) throw new NoraApiException("Não foi possível ler o cadastro de profissionais desta UBS, por isso o agendamento está indisponível. Atualize a agenda e tente de novo.");
        if (UsesCatalogBooking) { await CreateCatalogAppointmentAsync(unit); return; }
        var (startsAt, endsAt, patient) = ValidateAppointmentDraft();
        // Outro computador cadastrou o primeiro profissional ativo: a API passa a exigir o catálogo (400 catalog-required) e a tela ainda mostra o texto livre.
        // O cadastro é relido para a lista aparecer e a mensagem do erro continua.
        try { await api.CreateAppointmentAsync(unit.Id, patient.Cpf, AppointmentSpecialty.Trim(), AppointmentProfessional.Trim(), startsAt, endsAt); }
        catch (NoraApiException error) when (error.Code == "catalog-required")
        {
            await RecoverBookingAsync(unit, error.Code);
            throw;
        }
        SetTodayOverview(default, null);
        AppointmentPatient = null;
        await ReloadAfterChangeAsync(unit, UnitScope.Agenda, "Consulta agendada.");
    });

    /// <summary>Marcação pelo catálogo: só um horário livre da lista. Se o horário foi ocupado no meio do caminho ou o catálogo mudou, a leitura é refeita e a mensagem diz o que aconteceu.</summary>
    private async Task CreateCatalogAppointmentAsync(HealthUnit unit)
    {
        var patient = AppointmentPatient ?? throw new NoraApiException("Selecione o paciente cadastrado na UBS.");
        if (ObsRegistry.FirstOrDefault(item => item.Cpf == patient.Cpf) is not { IsActive: true }) throw new NoraApiException("O paciente precisa ter um cadastro ativo nesta UBS.");
        if (BookingSpecialty is null) throw new NoraApiException("Escolha a especialidade.");
        if (BookingProfessional is not { } professional) throw new NoraApiException("Escolha o profissional.");
        var slot = BookingSlot ?? throw new NoraApiException("Escolha um horário livre da lista.");
        if (slot.ProfessionalId != professional.Id || slot.SpecialtyId != BookingSpecialty.Id) throw new NoraApiException("O horário escolhido é de outro profissional ou especialidade. Escolha um horário livre da lista atual.");
        try { await api.CreateAppointmentAsync(unit.Id, patient.Cpf, slot.SpecialtyName, slot.ProfessionalName, slot.StartsAt, slot.EndsAt, slot.ProfessionalId, slot.SpecialtyId); }
        catch (NoraApiException error) when (error.Code is "slot-unavailable" or "catalog-required" or "patient-overlap")
        {
            await RecoverBookingAsync(unit, error.Code);
            throw;
        }
        SetTodayOverview(default, null);
        AppointmentPatient = null; BookingSlot = null;
        ReloadBookingSlots();
        await ReloadAfterChangeAsync(unit, UnitScope.Agenda, $"Consulta agendada para {slot.Display}.");
    }

    public Task ChangeAppointmentStatusAsync(string status, bool confirmed) => RunAsync(async () =>
    {
        var unit = ActiveObsUnit ?? throw new NoraApiException("Escolha uma UBS antes de alterar a consulta.");
        var appointment = SelectedAppointment ?? throw new NoraApiException("Selecione uma consulta.");
        var updated = await api.ChangeAppointmentStatusAsync(unit.Id, appointment.Id, status, confirmed);
        SetTodayOverview(default, null);
        if (disposed || !IsAuthenticated || ActiveObsUnit?.Id != unit.Id) return;
        if (monthAppointments is not null) SetMonthCache(monthKey ?? CurrentMonthKey, monthAppointments.Select(item => item.Id == updated.Id ? updated : item).ToArray());
        SelectedAppointment = Appointments.FirstOrDefault(item => item.Id == updated.Id);
        Say(MessageKind.Success, "Situação da consulta atualizada.");
    });

    private void ClearAppointmentData()
    {
        monthAppointments = null; monthKey = null; agendaLoadPending = false;
        Appointments.Clear(); SelectedAppointment = null; ApplyCalendarMarks(); Notify(nameof(AgendaStateText));
        SetTodayOverview(default, null); AppointmentPatient = null; AppointmentSpecialty = ""; AppointmentProfessional = "";
        ClearBooking();
    }
}
