using System.Collections.ObjectModel;
using Nora.Desktop.Models;
using Nora.Desktop.Services;

namespace Nora.Desktop.ViewModels;

/// <summary>Partes dos dados de uma UBS que cada seção precisa.</summary>
[Flags]
public enum UnitScope
{
    None = 0, Registry = 1, Slots = 4, Today = 8, Agenda = 16,
    /// <summary>Últimos 30 dias e série de 7 dias do Painel; nunca faz parte do ciclo de 20 s fora da regra dos 5 minutos.</summary>
    PanelMetrics = 32,
    /// <summary>Indicadores com o filtro de período da aba Indicadores.</summary>
    Metrics = 64,
    /// <summary>Consultórios em atendimento agora e com grade hoje (leitura leve, junto do resto do Painel).</summary>
    ActiveRooms = 128,
    /// <summary>Especialidades da rede e profissionais: base do agendamento por catálogo. Lido na troca de UBS, na atualização manual e, nas abas que o usam, quando a última leitura tem mais de 5 minutos.</summary>
    Catalog = 256,
    /// <summary>Consultórios, grade semanal e bloqueios, só para a aba Configurações da UBS.</summary>
    Setup = 512,
    /// <summary>Alertas avaliados pela API (aba Alertas); o Painel os lê junto com PanelMetrics.</summary>
    Alerts = 1024,
    /// <summary>Pedidos de família pendentes e vínculos aprovados da UBS (aba Família); só ela os lê.</summary>
    Family = 2048,
    /// <summary>Atendimento ao público (horário e serviços); lido nas Configurações, nas Vagas e na troca de UBS.</summary>
    Profile = 4096,
    Everything = Registry | Slots | Today | ActiveRooms
}

/// <summary>Leitura que pode falhar sem impedir o resto da UBS (rotas novas, base sem a migração mais nova); sessão encerrada e troca de senha continuam subindo como erro.</summary>
public readonly record struct Tolerant<T>(T? Value, bool Failed);

/// <summary>Leitura por UBS: busca em paralelo, só o que a seção pede, e aplica sem recriar coleções que não mudaram.</summary>
public sealed partial class WorkspaceViewModel
{
    private sealed class UnitSnapshot
    {
        public UnitPatientRecord[]? Registry { get; init; }
        public LightningSlotPage? Slots { get; init; }
        public AppointmentInfo[]? Today { get; init; }
        public DateTime TodayDate { get; init; }
        public AppointmentInfo[]? Month { get; init; }
        public (int Year, int Month) MonthKey { get; init; }
        public UnitMetricsRead? Metrics { get; init; }
        public UnitMetricsRead? Panel { get; init; }
        /// <summary>Quando cada leitura de indicadores foi pedida; a troca de UBS limpa o estado depois da leitura, então a hora é regravada ao aplicar.</summary>
        public DateTimeOffset MetricsAt { get; init; }
        public DateTimeOffset PanelAt { get; init; }
        public Tolerant<Specialty[]>? Specialties { get; init; }
        public Tolerant<Professional[]>? Professionals { get; init; }
        public DateTimeOffset CatalogAt { get; init; }
        public Tolerant<Room[]>? Rooms { get; init; }
        public Tolerant<ScheduleTemplate[]>? Schedule { get; init; }
        public Tolerant<ScheduleBlock[]>? Blocks { get; init; }
        public Tolerant<ActiveRooms>? ActiveRooms { get; init; }
        public Tolerant<UnitMetrics>? Capacity { get; init; }
        public Tolerant<AlertItem[]>? Alerts { get; init; }
        public DateTimeOffset AlertsAt { get; init; }
        public Tolerant<FamilyLinkPages>? FamilyPending { get; init; }
        public Tolerant<FamilyLinkPages>? FamilyApproved { get; init; }
        public Tolerant<UnitProfile>? Profile { get; init; }
    }

    private static async Task<Tolerant<T>> ReadTolerantAsync<T>(Func<Task<T>> read)
    {
        try { return new(await read(), false); }
        catch (NoraApiException error) when (!error.Unauthorized && error.Code != "password-change-required") { return new(default, true); }
    }

    private static UnitScope ScopeFor(string section) => section switch
    {
        "home" => UnitScope.Everything,
        "patients" => UnitScope.Registry,
        "slots" => UnitScope.Slots | UnitScope.Profile,
        "agenda" => UnitScope.Agenda | UnitScope.Catalog,
        "indicators" => UnitScope.Metrics,
        "alerts" => UnitScope.Alerts,
        "unit-settings" => UnitScope.Catalog | UnitScope.Setup | UnitScope.Profile,
        "family" => UnitScope.Family,
        _ => UnitScope.None
    };

    private async Task<UnitSnapshot> FetchUnitDataAsync(HealthUnit unit, UnitScope scope)
    {
        var today = DateTime.Today;
        var monthKey = (selectedAgendaDate.Year, selectedAgendaDate.Month);
        var (gridStart, gridEnd) = AgendaMonthRange(selectedAgendaDate);
        var registry = scope.HasFlag(UnitScope.Registry) ? api.ObsRegistryAsync(unit.Id) : null;
        var slots = scope.HasFlag(UnitScope.Slots) ? api.UnitLightningSlotsAsync(unit.Id) : null;
        var todayList = scope.HasFlag(UnitScope.Today) ? api.AppointmentsAsync(unit.Id, new DateTimeOffset(today), new DateTimeOffset(today.AddDays(1))) : null;
        // Uma única requisição cobre o mês inteiro do calendário; dia, semana e marcações saem dela.
        var month = scope.HasFlag(UnitScope.Agenda) ? api.AppointmentsAsync(unit.Id, gridStart, gridEnd) : null;
        // Indicadores: o do filtro e o do Painel viram uma só requisição quando pedem a mesma janela.
        Task<UnitMetricsRead>? metricsRead = null, panelRead = null;
        if (scope.HasFlag(UnitScope.Metrics) && TryBuildUnitRequest(out var filterRequest))
        {
            unitMetricsAttemptedAt = DateTimeOffset.Now;
            metricsRead = ShareAsync(api.UnitMetricsAsync(unit.Id, filterRequest.From, filterRequest.To), filterRequest);
        }
        if (scope.HasFlag(UnitScope.PanelMetrics))
        {
            var panelRequestNow = BuildPanelRequest(); panelAttemptedAt = DateTimeOffset.Now;
            panelRead = metricsRead is not null && TryBuildUnitRequest(out var sameRequest) && sameRequest == panelRequestNow
                ? SharePanelAsync(metricsRead, panelRequestNow) : ReadPanelMetricsAsync(unit, panelRequestNow);
        }
        // Leituras tolerantes (rotas da agenda por catálogo): cada uma falha sozinha, sem derrubar a abertura da UBS.
        Task<Tolerant<Specialty[]>>? specialties = null; Task<Tolerant<Professional[]>>? professionals = null; Task<Tolerant<Room[]>>? rooms = null;
        Task<Tolerant<ScheduleTemplate[]>>? schedule = null; Task<Tolerant<ScheduleBlock[]>>? blocks = null; Task<Tolerant<ActiveRooms>>? activeRooms = null;
        Task<Tolerant<UnitMetrics>>? capacity = null; Task<Tolerant<AlertItem[]>>? alerts = null;
        var catalogAt = DateTimeOffset.Now; var alertsAt = DateTimeOffset.Now;
        if (scope.HasFlag(UnitScope.Catalog)) { specialties = ReadTolerantAsync(() => api.ObsSpecialtiesAsync()); professionals = ReadTolerantAsync(() => api.ProfessionalsAsync(unit.Id)); }
        if (scope.HasFlag(UnitScope.Setup))
        {
            rooms = ReadTolerantAsync(() => api.RoomsAsync(unit.Id)); schedule = ReadTolerantAsync(() => api.ScheduleAsync(unit.Id));
            var blocksFrom = new DateTimeOffset(today); blocks = ReadTolerantAsync(() => api.ScheduleBlocksAsync(unit.Id, blocksFrom, blocksFrom.AddDays(366)));
        }
        if (scope.HasFlag(UnitScope.ActiveRooms)) activeRooms = ReadTolerantAsync(() => api.ActiveRoomsAsync(unit.Id));
        // O Painel e a aba Alertas leem a avaliação da API; a capacidade dos próximos 7 dias acompanha a leitura de indicadores do Painel (no máximo a cada 5 minutos).
        if (scope.HasFlag(UnitScope.Alerts) || scope.HasFlag(UnitScope.PanelMetrics)) alerts = ReadTolerantAsync(() => api.UnitAlertsAsync(unit.Id));
        if (scope.HasFlag(UnitScope.PanelMetrics)) { var capacityFrom = new DateTimeOffset(today); capacity = ReadTolerantAsync(() => api.UnitMetricsAsync(unit.Id, capacityFrom, capacityFrom.AddDays(CapacityDays))); }
        // Leitura tolerante: se falhar, a aba diz que não conseguiu ler em vez de ficar em "Carregando…".
        var familyPending = scope.HasFlag(UnitScope.Family) ? ReadTolerantAsync(() => api.FamilyLinksAsync(unit.Id, "pending")) : null;
        var familyApproved = scope.HasFlag(UnitScope.Family) ? ReadTolerantAsync(() => api.FamilyLinksAsync(unit.Id, "approved")) : null;
        // Atendimento ao público (todos da equipe leem): leitura tolerante, uma falha não derruba a UBS.
        var profileRead = scope.HasFlag(UnitScope.Profile) ? ReadTolerantAsync(() => api.UnitProfileAsync(unit.Id)) : null;
        await Task.WhenAll(new Task?[] { registry, slots, todayList, month, metricsRead, panelRead, specialties, professionals, rooms, schedule, blocks, activeRooms, capacity, alerts, familyPending, familyApproved, profileRead }.OfType<Task>());
        return new UnitSnapshot
        {
            Registry = registry is null ? null : await registry,
            Slots = slots is null ? null : await slots,
            Today = todayList is null ? null : await todayList,
            TodayDate = today,
            Month = month is null ? null : await month,
            MonthKey = monthKey,
            Metrics = metricsRead is null ? null : await metricsRead,
            Panel = panelRead is null ? null : await panelRead,
            MetricsAt = unitMetricsAttemptedAt ?? DateTimeOffset.Now,
            PanelAt = panelAttemptedAt ?? DateTimeOffset.Now,
            Specialties = specialties is null ? null : await specialties,
            Professionals = professionals is null ? null : await professionals,
            CatalogAt = catalogAt,
            Rooms = rooms is null ? null : await rooms,
            Schedule = schedule is null ? null : await schedule,
            Blocks = blocks is null ? null : await blocks,
            ActiveRooms = activeRooms is null ? null : await activeRooms,
            Capacity = capacity is null ? null : await capacity,
            Alerts = alerts is null ? null : await alerts,
            AlertsAt = alertsAt,
            FamilyPending = familyPending is null ? null : await familyPending,
            FamilyApproved = familyApproved is null ? null : await familyApproved,
            Profile = profileRead is null ? null : await profileRead
        };
    }

    /// <summary>Lê e aplica as partes pedidas. Nada muda se a leitura falhar ou se a UBS ativa trocou no meio do caminho.</summary>
    private async Task LoadUnitScopeAsync(HealthUnit unit, UnitScope scope)
    {
        var snapshot = await FetchUnitDataAsync(unit, scope);
        if (disposed || !IsAuthenticated || ActiveObsUnit?.Id != unit.Id) return;
        ApplyUnitData(snapshot);
    }

    private void ApplyUnitData(UnitSnapshot data)
    {
        if (data.Registry is { } registry) ApplyRegistry(registry);

        if (data.Slots is { } slots) ApplySlots(slots);
        if (data.Profile is { } unitProfileRead) ApplyProfile(unitProfileRead);
        if (data.Today is { } today) SetTodayOverview(data.TodayDate, today);
        if (data.Month is { } month && data.MonthKey == (selectedAgendaDate.Year, selectedAgendaDate.Month)) SetMonthCache(data.MonthKey, month);
        // Uma resposta de um filtro que já mudou não altera a tela.
        if (data.Metrics is { Data: { } read } && TryBuildUnitRequest(out var current) && current == data.Metrics.Request) { unitMetricsAttemptedAt = data.MetricsAt; ApplyUnitMetrics(read, current); }
        if (data.Panel is { } panel) { panelAttemptedAt = data.PanelAt; ApplyPanelMetrics(panel); }
        if (data.Specialties is not null || data.Professionals is not null || data.Rooms is not null || data.Schedule is not null || data.Blocks is not null) ApplyCatalog(data);
        if (data.ActiveRooms is { } activeRooms) ApplyActiveRooms(activeRooms);
        if (data.Capacity is { } capacity) ApplyPanelCapacity(capacity);
        if (data.Alerts is { } alerts) ApplyUnitAlerts(alerts, data.AlertsAt);
        if (data.FamilyPending is not null || data.FamilyApproved is not null) ApplyFamily(data.FamilyPending, data.FamilyApproved);

        NotifyUnitCounts();
    }

    private void ApplyRegistry(UnitPatientRecord[] records)
    {
        var selectedCpf = SelectedObsRegistry?.Cpf; var correctedEmail = CorrectedObsEmail; var appointmentCpf = AppointmentPatient?.Cpf;
        if (!Replace(ObsRegistry, records)) return;
        Notify(nameof(FilteredObsRegistry)); Notify(nameof(ActiveObsRegistry));
        SelectedObsRegistry = selectedCpf is null ? null : ObsRegistry.FirstOrDefault(p => p.Cpf == selectedCpf);
        if (SelectedObsRegistry is not null) CorrectedObsEmail = correctedEmail;

        AppointmentPatient = appointmentCpf is null ? null : ObsRegistry.FirstOrDefault(p => p.Cpf == appointmentCpf);
    }

    private static string FormatWait(TimeSpan elapsed)
    {
        if (elapsed < TimeSpan.FromMinutes(1)) return "Agora";
        if (elapsed.TotalDays >= 1) return $"{(int)elapsed.TotalDays}d {elapsed.Hours}h";
        if (elapsed.TotalHours >= 1) return $"{(int)elapsed.TotalHours}h {elapsed.Minutes}min";
        return $"{elapsed.Minutes} min";
    }

    private void NotifyUnitCounts()
    {
        Notify(nameof(PatientTotalCount)); Notify(nameof(OffersRunningCount)); Notify(nameof(SlotOpenCount));
        Notify(nameof(HasNoUnitSlots));
    }

    /// <summary>
    /// Atualiza a coleção no lugar: só as linhas que mudaram são substituídas e, se nada mudou, nenhum evento é gerado.
    /// Preserva rolagem e foco; devolve se houve alteração.
    /// </summary>
    private static bool Replace<T>(ObservableCollection<T> destination, IReadOnlyList<T> source)
    {
        if (destination.Count == source.Count)
        {
            var changed = false;
            for (var index = 0; index < source.Count; index++)
            {
                if (EqualityComparer<T>.Default.Equals(destination[index], source[index])) continue;
                destination[index] = source[index]; changed = true;
            }
            return changed;
        }
        destination.Clear();
        foreach (var item in source) destination.Add(item);
        return true;
    }

    /// <summary>
    /// Como <see cref="Replace{T}(ObservableCollection{T}, IReadOnlyList{T})"/>, para quando a origem é a lista atual com itens a mais no fim: as linhas existentes só são trocadas se mudaram e o resto é
    /// acrescentado com Add, sem Clear. O DataGrid não recebe Reset, então rolagem, foco e seleção ficam onde estavam. Se a origem for menor, cai no <c>Replace</c> comum.
    /// </summary>
    private static bool Grow<T>(ObservableCollection<T> destination, IReadOnlyList<T> source)
    {
        if (source.Count < destination.Count) return Replace(destination, source);
        var changed = false;
        for (var index = 0; index < destination.Count; index++)
        {
            if (EqualityComparer<T>.Default.Equals(destination[index], source[index])) continue;
            destination[index] = source[index]; changed = true;
        }
        for (var index = destination.Count; index < source.Count; index++) { destination.Add(source[index]); changed = true; }
        return changed;
    }
    private static bool Replace<T>(ObservableCollection<T> destination, IEnumerable<T> source) => Replace(destination, source as IReadOnlyList<T> ?? source.ToArray());
}
