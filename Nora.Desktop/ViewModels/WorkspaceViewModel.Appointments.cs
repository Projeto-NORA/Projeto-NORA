using System.Collections.ObjectModel;
using System.Globalization;
using Nora.Desktop.Models;
using Nora.Desktop.Services;

namespace Nora.Desktop.ViewModels;

/// <summary>
/// Agenda: quatro visões (Dia, Semana, Mês e Ano) sobre um mesmo cache de consultas por dia. Cada leitura da API grava no cache os dias que cobriu; a visão e as marcações
/// do calendário são derivadas dele, sem novas requisições. Dia, Semana e Mês leem o calendário do mês visível (no máximo 42 dias, uma requisição). O Ano lê os 12 meses
/// em blocos de até 6 meses (a API aceita no máximo 366 dias por pedido) e, nos ciclos seguintes, só o mês escolhido, o mês de hoje e o que ficou velho (5 minutos).
/// </summary>
public sealed partial class WorkspaceViewModel
{
    private static readonly TimeSpan AgendaYearFreshness = TimeSpan.FromMinutes(5);
    /// <summary>Quantos meses cabem num pedido do ano: 6 meses ficam abaixo dos 366 dias aceitos pela API.</summary>
    private const int AgendaYearChunkMonths = 6;

    private UnitPatientRecord? appointmentPatient;
    private AppointmentInfo? selectedAppointment;
    private IReadOnlyList<AppointmentInfo> appointmentSelection = [];
    private string appointmentSpecialty = "", appointmentProfessional = "", appointmentStart = "09:00", appointmentEnd = "09:30";
    private IReadOnlyList<AppointmentInfo>? todayOverview;
    private DateTime todayOverviewDate;
    private string agendaViewMode = "Mês";
    private DateTime selectedAgendaDate = DateTime.Today;
    /// <summary>Consultas lidas, por dia local; um dia lido sem consulta não tem entrada aqui, mas tem entrada em <see cref="agendaReadAt"/>.</summary>
    private readonly Dictionary<DateTime, AppointmentInfo[]> agendaByDay = [];
    /// <summary>Quando cada dia foi lido pela última vez: é o que diz se um dia já foi coberto por alguma leitura.</summary>
    private readonly Dictionary<DateTime, DateTimeOffset> agendaReadAt = [];
    private bool agendaLoadPending;
    private static readonly string[] AgendaActionNames =
        [nameof(CanCallAppointment), nameof(CanCompleteAppointment), nameof(CanMissAppointment), nameof(CanCancelAppointment), nameof(SelectedAppointmentCount)];

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

    /// <summary>As visões da agenda, na ordem do alternador do cabeçalho.</summary>
    public IReadOnlyList<string> AgendaPeriods { get; } = ["Dia", "Semana", "Mês", "Ano"];
    /// <summary>"Dia", "Semana", "Mês" ou "Ano". Trocar a visão mantém a data escolhida; um valor fora da lista é ignorado.</summary>
    public string AgendaViewMode
    {
        get => agendaViewMode;
        set { if (value is not null && value != agendaViewMode && AgendaPeriods.Contains(value)) ChangeAgendaDate(selectedAgendaDate, value); }
    }
    public bool IsAgendaDay => AgendaViewMode == "Dia";
    public bool IsAgendaWeek => AgendaViewMode == "Semana";
    public bool IsAgendaMonth => AgendaViewMode == "Mês";
    public bool IsAgendaYear => AgendaViewMode == "Ano";
    /// <summary>Dia e Mês mostram a lista das consultas do dia escolhido (no Mês, abaixo do calendário).</summary>
    public bool ShowsAgendaDayList => IsAgendaDay || IsAgendaMonth;
    /// <summary>As visões em que a cor da situação aparece (a legenda só vale nelas); o Ano marca os dias pela quantidade.</summary>
    public bool ShowsAgendaLegend => !IsAgendaYear;
    public string AgendaRangeLabel => AgendaViewMode switch
    {
        "Dia" => Capitalize(selectedAgendaDate.ToString("dddd, dd 'de' MMMM")),
        "Semana" => $"Semana de {StartOfWeek(selectedAgendaDate):dd/MM} a {StartOfWeek(selectedAgendaDate).AddDays(6):dd/MM}",
        "Mês" => Capitalize(selectedAgendaDate.ToString("MMMM 'de' yyyy")),
        _ => selectedAgendaDate.Year.ToString(CultureInfo.InvariantCulture)
    };
    public string SelectedAgendaDateLabel => selectedAgendaDate.ToString("dddd, dd 'de' MMMM 'de' yyyy");
    private static string Capitalize(string text) => text.Length == 0 ? text : char.ToUpper(text[0]) + text[1..];
    /// <summary>Os dias do calendário do mês escolhido, em semanas inteiras.</summary>
    public ObservableCollection<AgendaCalendarDay> AgendaDays { get; } = [];
    /// <summary>Os sete dias da semana escolhida (cabeçalho da grade da Semana).</summary>
    public ObservableCollection<AgendaCalendarDay> AgendaWeekDays { get; } = [];
    /// <summary>Os 12 meses do ano escolhido (visão Ano); só são montados enquanto o Ano está aberto.</summary>
    public ObservableCollection<AgendaYearMonth> AgendaMonths { get; } = [];
    /// <summary>Faixa de horas da grade da Semana: as horas com consulta e o horário de atendimento da UBS.</summary>
    public int AgendaWeekFirstHour { get; private set; } = 8;
    public int AgendaWeekLastHour { get; private set; } = 18;
    public DateTime AgendaWeekStart => StartOfWeek(selectedAgendaDate);
    /// <summary>As consultas mostradas pela visão: as do dia (Dia e Mês), as da semana (Semana) ou nenhuma (Ano, que só marca os dias).</summary>
    public ObservableCollection<AppointmentInfo> Appointments { get; } = [];
    /// <summary>Texto de estado da lista: sem UBS, lendo a agenda ou sem consultas no período. Vazio quando a lista tem o que mostrar.</summary>
    public string AgendaStateText => !HasActiveObsUnit ? "Escolha a UBS em que você vai trabalhar para ver a agenda."
        : AgendaIsLoading ? (IsAgendaYear ? "Carregando a agenda do ano…" : "Carregando a agenda do mês…")
        : IsAgendaYear ? ""
        : Appointments.Count == 0 ? (IsAgendaWeek ? "Nenhuma consulta nesta semana." : "Nenhuma consulta neste dia.") : "";
    /// <summary>A agenda da visão ainda não foi lida (troca de mês, de ano ou de UBS): a tela mostra o indicador junto do texto de estado.</summary>
    public bool AgendaIsLoading => HasActiveObsUnit && !AgendaCovered();

    public UnitPatientRecord? AppointmentPatient { get => appointmentPatient; set { appointmentPatient = value; Notify(); } }
    /// <summary>A consulta principal da seleção (a primeira, se houver várias). A tela escolhe a seleção; atribuir aqui seleciona só esta.</summary>
    public AppointmentInfo? SelectedAppointment { get => selectedAppointment; set => SetAppointmentSelection(value is null ? [] : [value]); }
    public int SelectedAppointmentCount => appointmentSelection.Count;
    /// <summary>Escolhida na lista de especialidades cadastradas; nulo vindo do seletor (valor fora da lista) não apaga a escolha.</summary>
    public string AppointmentSpecialty { get => appointmentSpecialty; set { if (value is null) return; appointmentSpecialty = value; Notify(); } }
    public string AppointmentProfessional { get => appointmentProfessional; set { appointmentProfessional = value; Notify(); } }
    public string AppointmentStart { get => appointmentStart; set { appointmentStart = value; Notify(); } }
    public string AppointmentEnd { get => appointmentEnd; set { appointmentEnd = value; Notify(); } }

    /// <summary>A seleção da lista (ou da grade da semana) mudou: guarda as consultas selecionadas, na ordem em que a tela as mostra.</summary>
    public void SetAppointmentSelection(IReadOnlyList<AppointmentInfo> items)
    {
        if (appointmentSelection.SequenceEqual(items)) return;
        appointmentSelection = items;
        selectedAppointment = items.FirstOrDefault();
        Notify(nameof(SelectedAppointment));
        foreach (var name in AgendaActionNames) Notify(name);
    }

    /// <summary>
    /// Regra das situações aceitas pela API: chamar só quem está agendado, concluir só quem foi chamado, registrar falta e cancelar valem para agendada ou chamada.
    /// Com várias consultas só vale a ação que serve a todas.
    /// </summary>
    public static bool AllowsAppointmentStatus(string status, IReadOnlyCollection<AppointmentInfo> items) => items.Count > 0 && items.All(item => status switch
    {
        "called" => item.Status == "scheduled",
        "completed" => item.Status == "called",
        "missed" or "cancelled" => item.Status is "scheduled" or "called",
        _ => false
    });
    // Botões de situação seguem as transições aceitas pela API; sem seleção, tudo fica desabilitado.
    public bool CanCallAppointment => CanOperate && AllowsAppointmentStatus("called", appointmentSelection);
    public bool CanCompleteAppointment => CanOperate && AllowsAppointmentStatus("completed", appointmentSelection);
    public bool CanMissAppointment => CanOperate && AllowsAppointmentStatus("missed", appointmentSelection);
    public bool CanCancelAppointment => CanOperate && AllowsAppointmentStatus("cancelled", appointmentSelection);

    private static DateTime StartOfWeek(DateTime date)
    {
        var offset = ((int)date.DayOfWeek + 6) % 7;
        return date.Date.AddDays(-offset);
    }
    /// <summary>Intervalo exibido pelo calendário (semanas inteiras do mês): no máximo 42 dias, uma só requisição. O fim é exclusivo.</summary>
    private static (DateTime From, DateTime To) AgendaGridRange(DateTime date)
    {
        var first = new DateTime(date.Year, date.Month, 1);
        return (StartOfWeek(first), StartOfWeek(first.AddMonths(1).AddDays(-1)).AddDays(7));
    }
    /// <summary>Os dias que a visão atual precisa ter lidos: o ano inteiro no Ano; nas demais, o calendário do mês (a semana e o dia sempre estão nele).</summary>
    private (DateTime From, DateTime To) AgendaNeededRange() => IsAgendaYear
        ? (new DateTime(selectedAgendaDate.Year, 1, 1), new DateTime(selectedAgendaDate.Year + 1, 1, 1))
        : AgendaGridRange(selectedAgendaDate);
    private bool AgendaCovered() => AgendaCovered(AgendaNeededRange());
    private bool AgendaCovered((DateTime From, DateTime To) range)
    {
        for (var day = range.From; day < range.To; day = day.AddDays(1))
            if (!agendaReadAt.ContainsKey(day)) return false;
        return true;
    }

    private void BuildAgendaDays()
    {
        AgendaDays.Clear();
        var first = new DateTime(selectedAgendaDate.Year, selectedAgendaDate.Month, 1);
        var (start, end) = AgendaGridRange(first);
        for (var date = start; date < end; date = date.AddDays(1))
            AgendaDays.Add(new AgendaCalendarDay(date, date.Month == first.Month) { IsSelected = date == selectedAgendaDate });
        ApplyCalendarMarks();
    }

    private void BuildAgendaMonths()
    {
        AgendaMonths.Clear();
        foreach (var month in Enumerable.Range(1, 12)) AgendaMonths.Add(new AgendaYearMonth(selectedAgendaDate.Year, month));
        ApplyCalendarMarks();
    }

    /// <summary>Marca quantidade e situações de cada dia exibido (mês, semana e ano), derivadas do cache; um dia ainda não lido aparece sem marcas.</summary>
    private void ApplyCalendarMarks()
    {
        foreach (var day in AgendaDays) MarkDay(day);
        foreach (var day in AgendaWeekDays) MarkDay(day);
        foreach (var month in AgendaMonths)
        {
            foreach (var day in month.Days) { MarkDay(day); day.IsSelected = day.Date == selectedAgendaDate; }
            month.AppointmentCount = month.Days.Where(day => day.IsCurrentMonth).Sum(day => day.AppointmentCount);
        }
    }

    private void MarkDay(AgendaCalendarDay day)
    {
        var items = agendaByDay.TryGetValue(day.Date, out var found) ? found : [];
        day.AppointmentCount = items.Length;
        day.SetStatuses(items.Select(item => item.Status));
    }

    private IEnumerable<AppointmentInfo> AgendaRecords(DateTime from, int days) =>
        Enumerable.Range(0, days).SelectMany(offset => agendaByDay.TryGetValue(from.AddDays(offset), out var found) ? found : []);

    /// <summary>Recalcula a lista da visão (dia, semana ou nenhuma) a partir do cache; mantém selecionadas as consultas que continuam visíveis.</summary>
    private void RebuildAgendaView()
    {
        var selectedIds = appointmentSelection.Select(item => item.Id).ToHashSet();
        var shown = IsAgendaYear ? Array.Empty<AppointmentInfo>() : IsAgendaWeek ? AgendaRecords(StartOfWeek(selectedAgendaDate), 7).ToArray() : AgendaRecords(selectedAgendaDate, 1).ToArray();
        Replace(Appointments, shown.OrderBy(item => item.StartsAt).ThenBy(item => item.Id).ToArray());
        SetAppointmentSelection(selectedIds.Count == 0 ? Array.Empty<AppointmentInfo>() : Appointments.Where(item => selectedIds.Contains(item.Id)).ToArray());
        if (IsAgendaYear && (AgendaMonths.Count != 12 || AgendaMonths[0].FirstDay.Year != selectedAgendaDate.Year)) BuildAgendaMonths();
        // Fora do Ano os 12 mini-calendários (504 dias) saem da memória e deixam de ser remarcados a cada leitura.
        else if (!IsAgendaYear && AgendaMonths.Count > 0) AgendaMonths.Clear();
        if (IsAgendaWeek) BuildAgendaWeek();
        ApplyCalendarMarks();
        Notify(nameof(AgendaStateText)); Notify(nameof(AgendaIsLoading));
    }

    /// <summary>A grade da Semana: os sete dias e a faixa de horas (consultas da semana e horário de atendimento da UBS, sempre com pelo menos 6 horas).</summary>
    private void BuildAgendaWeek()
    {
        var start = StartOfWeek(selectedAgendaDate);
        if (AgendaWeekDays.Count != 7 || AgendaWeekDays[0].Date != start)
        {
            AgendaWeekDays.Clear();
            foreach (var offset in Enumerable.Range(0, 7)) AgendaWeekDays.Add(new AgendaCalendarDay(start.AddDays(offset), true));
        }
        foreach (var day in AgendaWeekDays) day.IsSelected = day.Date == selectedAgendaDate;
        int first = 24, last = 0;
        foreach (var item in Appointments)
        {
            var begins = item.StartsAt.ToLocalTime(); var ends = item.EndsAt.ToLocalTime();
            first = Math.Min(first, begins.Hour);
            last = Math.Max(last, ends.Date > begins.Date ? 24 : ends.Minute > 0 ? ends.Hour + 1 : ends.Hour);
        }
        foreach (var interval in unitProfile?.OpeningHours ?? [])
        {
            if (TimeSpan.TryParseExact(interval.Opens, @"hh\:mm", CultureInfo.InvariantCulture, out var opens)) first = Math.Min(first, opens.Hours);
            if (TimeSpan.TryParseExact(interval.Closes, @"hh\:mm", CultureInfo.InvariantCulture, out var closes)) last = Math.Max(last, closes.Minutes > 0 ? closes.Hours + 1 : closes.Hours);
        }
        if (last <= first) (first, last) = (8, 18);
        if (last - first < 6) { last = Math.Min(24, first + 6); first = last - 6; }
        var changed = first != AgendaWeekFirstHour || last != AgendaWeekLastHour;
        AgendaWeekFirstHour = first; AgendaWeekLastHour = last;
        if (changed) { Notify(nameof(AgendaWeekFirstHour)); Notify(nameof(AgendaWeekLastHour)); }
        Notify(nameof(AgendaWeekStart));
    }

    /// <summary>Um dia clicado: no Ano e na Semana abre a visão Dia nele; no Mês só o escolhe (a lista do dia aparece abaixo).</summary>
    private void SelectCalendarDay(AgendaCalendarDay? day)
    {
        if (day == null) return;
        ChangeAgendaDate(day.Date, IsAgendaMonth ? null : "Dia");
    }

    /// <summary>Um mês clicado na visão Ano: abre a visão Mês nele (no dia de hoje, se for o mês de hoje; senão no dia 1).</summary>
    public void OpenAgendaMonth(DateTime month) =>
        ChangeAgendaDate(month.Year == DateTime.Today.Year && month.Month == DateTime.Today.Month ? DateTime.Today : new DateTime(month.Year, month.Month, 1), "Mês");

    public void GoToAgendaToday() => ChangeAgendaDate(DateTime.Today, null);

    /// <summary>Anda um período para trás ou para a frente: um dia, uma semana, um mês ou um ano, conforme a visão.</summary>
    public void MoveAgenda(int direction) => ChangeAgendaDate(AgendaViewMode switch
    {
        "Dia" => selectedAgendaDate.AddDays(direction),
        "Semana" => selectedAgendaDate.AddDays(direction * 7),
        "Mês" => selectedAgendaDate.AddMonths(direction),
        _ => selectedAgendaDate.AddYears(direction)
    }, null);

    private void ChangeAgendaDate(DateTime date, string? mode)
    {
        var previous = selectedAgendaDate;
        var monthChanged = date.Year != previous.Year || date.Month != previous.Month;
        var yearChanged = date.Year != previous.Year;
        var dayChanged = date.Date != previous;
        var modeChanged = mode is not null && mode != agendaViewMode;
        selectedAgendaDate = date.Date;
        if (mode is not null) agendaViewMode = mode;
        if (monthChanged) BuildAgendaDays();
        else foreach (var day in AgendaDays) day.IsSelected = day.Date == selectedAgendaDate;
        RebuildAgendaView();
        foreach (var name in new[] { nameof(AgendaViewMode), nameof(IsAgendaDay), nameof(IsAgendaWeek), nameof(IsAgendaMonth), nameof(IsAgendaYear), nameof(ShowsAgendaDayList), nameof(ShowsAgendaLegend), nameof(AgendaRangeLabel), nameof(SelectedAgendaDateLabel) })
            Notify(name);
        // Dia, Semana e Mês: mudar de mês sempre relê o calendário e trocar de visão só lê se faltar algum dia. Ano: lê só se faltar algum dia do ano (ao trocar de visão ou de ano);
        // voltar a um ano já lido usa o que está guardado, e o ciclo de 20 s atualiza o resto.
        var needsRead = IsAgendaYear ? (modeChanged || yearChanged) && !AgendaCovered() : monthChanged || (modeChanged && !AgendaCovered());
        if (HasActiveObsUnit && needsRead) RequestAgendaLoad();
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

    /// <summary>Um pedaço lido da agenda: os dias de <see cref="From"/> (inclusive) a <see cref="To"/> (exclusivo) e as consultas que a API devolveu para eles.</summary>
    private sealed record AgendaChunk(DateTime From, DateTime To, AppointmentInfo[] Records, DateTimeOffset At);
    private sealed record AgendaRead(IReadOnlyList<AgendaChunk> Chunks);

    /// <summary>
    /// O que ler agora. Dia, Semana e Mês: o calendário do mês, sempre (uma requisição). Ano: os meses ainda não lidos ou com mais de 5 minutos, mais o mês escolhido e o
    /// mês de hoje, juntando meses vizinhos em pedidos de até 6 meses.
    /// </summary>
    private List<(DateTime From, DateTime To)> PlanAgendaReads()
    {
        if (!IsAgendaYear) return [AgendaGridRange(selectedAgendaDate)];
        var year = selectedAgendaDate.Year; var now = DateTimeOffset.Now;
        var plan = new List<(DateTime From, DateTime To)>();
        int? chunkStart = null;
        for (var month = 1; month <= 12; month++)
        {
            var hot = month == selectedAgendaDate.Month || (year == DateTime.Today.Year && month == DateTime.Today.Month);
            var needed = hot || !AgendaMonthFresh(year, month, now);
            if (needed && chunkStart is null) chunkStart = month;
            if (chunkStart is { } begin && (!needed || month - begin + 1 == AgendaYearChunkMonths || month == 12))
            {
                var lastMonth = needed ? month : month - 1;
                plan.Add((new DateTime(year, begin, 1), new DateTime(year, lastMonth, 1).AddMonths(1)));
                chunkStart = null;
            }
        }
        return plan;
    }

    private bool AgendaMonthFresh(int year, int month, DateTimeOffset now)
    {
        var first = new DateTime(year, month, 1);
        for (var day = first; day.Month == month; day = day.AddDays(1))
            if (!agendaReadAt.TryGetValue(day, out var at) || now - at >= AgendaYearFreshness) return false;
        return true;
    }

    private async Task<AgendaRead> ReadAgendaAsync(HealthUnit unit)
    {
        var ranges = PlanAgendaReads();
        var stamp = DateTimeOffset.Now;
        var reads = ranges.Select(range => api.AppointmentsAsync(unit.Id, new DateTimeOffset(range.From), new DateTimeOffset(range.To))).ToArray();
        var results = await Task.WhenAll(reads);
        return new([.. ranges.Select((range, index) => new AgendaChunk(range.From, range.To, results[index], stamp))]);
    }

    /// <summary>Grava as leituras no cache (um dia relido troca o que havia) e refaz a visão. O cache só guarda o que está a menos de dois anos da data escolhida.</summary>
    private void ApplyAgenda(AgendaRead read)
    {
        foreach (var chunk in read.Chunks) ApplyAgendaChunk(chunk);
        var limit = TimeSpan.FromDays(730);
        foreach (var far in agendaReadAt.Keys.Where(day => (day - selectedAgendaDate).Duration() > limit).ToArray()) { agendaReadAt.Remove(far); agendaByDay.Remove(far); }
        RebuildAgendaView();
        // Sem botão de atualizar: uma busca de horários livres que falhou é repetida a cada leitura da agenda.
        if (bookingSlotsFailed) ReloadBookingSlots();
    }

    private void ApplyAgendaChunk(AgendaChunk chunk)
    {
        for (var day = chunk.From; day < chunk.To; day = day.AddDays(1)) { agendaByDay.Remove(day); agendaReadAt[day] = chunk.At; }
        foreach (var group in chunk.Records.GroupBy(item => item.StartsAt.ToLocalTime().Date).Where(group => group.Key >= chunk.From && group.Key < chunk.To))
            agendaByDay[group.Key] = [.. group.OrderBy(item => item.StartsAt).ThenBy(item => item.Id)];
    }

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

    /// <summary>Agenda a consulta do pop-up. Devolve se a API aceitou; o erro fica em <c>Message</c> e o pop-up o mostra dentro dele.</summary>
    public async Task<bool> CreateAppointmentAsync()
    {
        var created = false;
        await RunAsync(async () =>
        {
            var unit = ActiveObsUnit ?? throw new NoraApiException("Escolha a UBS em que você vai trabalhar antes de agendar.");
            if (catalogFailed) throw new NoraApiException("Não foi possível ler o cadastro de profissionais desta UBS, por isso o agendamento está indisponível. A agenda tenta de novo sozinha; aguarde alguns instantes e repita.");
            if (UsesCatalogBooking) { await CreateCatalogAppointmentAsync(unit); created = true; return; }
            var (startsAt, endsAt, patient) = ValidateAppointmentDraft();
            // Outro computador cadastrou o primeiro profissional ativo: a API passa a exigir o catálogo (400 catalog-required) e a tela ainda mostra o texto livre.
            // O cadastro é relido para a lista aparecer e a mensagem do erro continua.
            try { await api.CreateAppointmentAsync(unit.Id, patient.Cpf, AppointmentSpecialty.Trim(), AppointmentProfessional.Trim(), startsAt, endsAt); }
            catch (NoraApiException error) when (error.Code == "catalog-required")
            {
                await RecoverBookingAsync(unit, error.Code);
                throw;
            }
            created = true;
            SetTodayOverview(default, null);
            AppointmentPatient = null;
            await ReloadAfterChangeAsync(unit, UnitScope.Agenda, "Consulta agendada.");
        });
        return created;
    }

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

    /// <summary>
    /// Muda a situação das consultas selecionadas, uma chamada da API por consulta e uma só mensagem no fim. Se uma falhar no meio, as anteriores continuam valendo e a
    /// mensagem diz quantas foram atualizadas.
    /// </summary>
    public Task ChangeAppointmentStatusAsync(string status, bool confirmed) => RunAsync(async () =>
    {
        var unit = ActiveObsUnit ?? throw new NoraApiException("Escolha uma UBS antes de alterar a consulta.");
        var targets = appointmentSelection.Count > 0 ? appointmentSelection.ToArray() : throw new NoraApiException("Selecione uma consulta.");
        var updated = new List<AppointmentInfo>();
        try { foreach (var appointment in targets) updated.Add(await api.ChangeAppointmentStatusAsync(unit.Id, appointment.Id, status, confirmed)); }
        catch (NoraApiException error) when (updated.Count > 0 && !error.Unauthorized && error.Code != "password-change-required")
        {
            ApplyUpdatedAppointments(unit, updated);
            throw new NoraApiException($"{updated.Count} de {targets.Length} consultas foram atualizadas; a seguinte não: {error.Message}", false, error.Code, error.Status);
        }
        ApplyUpdatedAppointments(unit, updated);
        if (disposed || !IsAuthenticated || ActiveObsUnit?.Id != unit.Id) return;
        await ReloadAfterChangeAsync(unit, UnitScope.Agenda, targets.Length == 1 ? "Situação da consulta atualizada." : $"Situação de {targets.Length} consultas atualizada.");
    });

    /// <summary>A resposta da API vale na hora; a releitura que vem depois confirma o resto da agenda.</summary>
    private void ApplyUpdatedAppointments(HealthUnit unit, IReadOnlyList<AppointmentInfo> updated)
    {
        SetTodayOverview(default, null);
        if (disposed || !IsAuthenticated || ActiveObsUnit?.Id != unit.Id) return;
        foreach (var record in updated)
            foreach (var day in agendaByDay.Keys.ToArray())
                if (agendaByDay[day].Any(item => item.Id == record.Id))
                    agendaByDay[day] = [.. agendaByDay[day].Select(item => item.Id == record.Id ? record : item)];
        RebuildAgendaView();
    }

    private void ClearAppointmentData()
    {
        agendaByDay.Clear(); agendaReadAt.Clear(); agendaLoadPending = false;
        Appointments.Clear(); SetAppointmentSelection([]); ApplyCalendarMarks(); Notify(nameof(AgendaStateText)); Notify(nameof(AgendaIsLoading));
        SetTodayOverview(default, null); AppointmentPatient = null; AppointmentSpecialty = ""; AppointmentProfessional = "";
        ClearBooking();
    }
}
