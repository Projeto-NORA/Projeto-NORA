using System.Collections.ObjectModel;
using Nora.Desktop.Models;

namespace Nora.Desktop.ViewModels;

/// <summary>Partes dos dados de uma UBS que cada seção precisa.</summary>
[Flags]
public enum UnitScope
{
    None = 0, Registry = 1, Queue = 2, Slots = 4, Today = 8, Agenda = 16,
    /// <summary>Últimos 30 dias e série de 7 dias do Painel; nunca faz parte do ciclo de 20 s fora da regra dos 5 minutos.</summary>
    PanelMetrics = 32,
    /// <summary>Indicadores com o filtro de período da aba Indicadores.</summary>
    Metrics = 64,
    Everything = Registry | Queue | Slots | Today
}

/// <summary>Leitura por UBS: busca em paralelo, só o que a seção pede, e aplica sem recriar coleções que não mudaram.</summary>
public sealed partial class WorkspaceViewModel
{
    private sealed class UnitSnapshot
    {
        public UnitPatientRecord[]? Registry { get; init; }
        public UnitWaitingEntry[]? Queue { get; init; }
        public UnitLightningSlot[]? Slots { get; init; }
        public AppointmentInfo[]? Today { get; init; }
        public DateTime TodayDate { get; init; }
        public AppointmentInfo[]? Month { get; init; }
        public (int Year, int Month) MonthKey { get; init; }
        public UnitMetricsRead? Metrics { get; init; }
        public UnitMetricsRead? Panel { get; init; }
        /// <summary>Quando cada leitura de indicadores foi pedida; a troca de UBS limpa o estado depois da leitura, então a hora é regravada ao aplicar.</summary>
        public DateTimeOffset MetricsAt { get; init; }
        public DateTimeOffset PanelAt { get; init; }
    }

    private static UnitScope ScopeFor(string section) => section switch
    {
        "home" => UnitScope.Everything,
        "patients" => UnitScope.Registry,
        "queue" => UnitScope.Queue,
        "slots" => UnitScope.Slots,
        "agenda" => UnitScope.Agenda,
        "indicators" => UnitScope.Metrics,
        _ => UnitScope.None
    };

    private async Task<UnitSnapshot> FetchUnitDataAsync(HealthUnit unit, UnitScope scope)
    {
        var today = DateTime.Today;
        var monthKey = (selectedAgendaDate.Year, selectedAgendaDate.Month);
        var (gridStart, gridEnd) = AgendaMonthRange(selectedAgendaDate);
        var registry = scope.HasFlag(UnitScope.Registry) ? api.ObsRegistryAsync(unit.Id) : null;
        var queue = scope.HasFlag(UnitScope.Queue) ? api.UnitWaitingListAsync(unit.Id) : null;
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
        await Task.WhenAll(new Task?[] { registry, queue, slots, todayList, month, metricsRead, panelRead }.OfType<Task>());
        return new UnitSnapshot
        {
            Registry = registry is null ? null : await registry,
            Queue = queue is null ? null : await queue,
            Slots = slots is null ? null : await slots,
            Today = todayList is null ? null : await todayList,
            TodayDate = today,
            Month = month is null ? null : await month,
            MonthKey = monthKey,
            Metrics = metricsRead is null ? null : await metricsRead,
            Panel = panelRead is null ? null : await panelRead,
            MetricsAt = unitMetricsAttemptedAt ?? DateTimeOffset.Now,
            PanelAt = panelAttemptedAt ?? DateTimeOffset.Now
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
        if (data.Queue is { } queue) ApplyQueue(queue);
        if (data.Slots is { } slots) { Replace(UnitLightningSlots, slots); unitSlotsLoaded = true; ReselectSlot(); }
        if (data.Today is { } today) SetTodayOverview(data.TodayDate, today);
        if (data.Month is { } month && data.MonthKey == (selectedAgendaDate.Year, selectedAgendaDate.Month)) SetMonthCache(data.MonthKey, month);
        // Uma resposta de um filtro que já mudou não altera a tela.
        if (data.Metrics is { Data: { } read } && TryBuildUnitRequest(out var current) && current == data.Metrics.Request) { unitMetricsAttemptedAt = data.MetricsAt; ApplyUnitMetrics(read, current); }
        if (data.Panel is { } panel) { panelAttemptedAt = data.PanelAt; ApplyPanelMetrics(panel); }
        NotifyUnitCounts();
    }

    private void ApplyRegistry(UnitPatientRecord[] records)
    {
        var selectedCpf = SelectedObsRegistry?.Cpf; var correctedEmail = CorrectedObsEmail; var waitingCpf = WaitingPatient?.Cpf; var appointmentCpf = AppointmentPatient?.Cpf;
        if (!Replace(ObsRegistry, records)) return;
        Notify(nameof(FilteredObsRegistry)); Notify(nameof(ActiveObsRegistry));
        SelectedObsRegistry = selectedCpf is null ? null : ObsRegistry.FirstOrDefault(p => p.Cpf == selectedCpf);
        if (SelectedObsRegistry is not null) CorrectedObsEmail = correctedEmail;
        WaitingPatient = waitingCpf is null ? null : ObsRegistry.FirstOrDefault(p => p.Cpf == waitingCpf);
        AppointmentPatient = appointmentCpf is null ? null : ObsRegistry.FirstOrDefault(p => p.Cpf == appointmentCpf);
    }

    private void ApplyQueue(UnitWaitingEntry[] entries)
    {
        var selectedId = SelectedWaitingEntry?.Id;
        var changed = Replace(UnitWaitingList, DecorateQueue(entries, DateTimeOffset.Now));
        unitQueueLoaded = true;
        if (changed) SelectedWaitingEntry = selectedId is null ? null : UnitWaitingList.FirstOrDefault(entry => entry.Id == selectedId);
    }

    /// <summary>Posição = ordem de entrada entre as entradas ativas da mesma especialidade (como o servidor distribui); espera = tempo desde a entrada.</summary>
    private static UnitWaitingEntry[] DecorateQueue(UnitWaitingEntry[] entries, DateTimeOffset now)
    {
        var next = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var positions = new Dictionary<long, int>();
        foreach (var entry in entries.Where(entry => entry.IsActive).OrderBy(entry => entry.EnteredAt).ThenBy(entry => entry.Id))
        {
            var key = entry.Specialty.Trim();
            positions[entry.Id] = next[key] = next.GetValueOrDefault(key) + 1;
        }
        var result = new UnitWaitingEntry[entries.Length];
        for (var index = 0; index < entries.Length; index++)
        {
            var entry = entries[index];
            result[index] = positions.TryGetValue(entry.Id, out var position) ? entry with { Position = position, WaitDisplay = FormatWait(now - entry.EnteredAt) } : entry;
        }
        return result;
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
        Notify(nameof(PatientTotalCount)); Notify(nameof(QueueWaitingCount)); Notify(nameof(SlotOpenCount));
        Notify(nameof(HasNoWaitingEntries)); Notify(nameof(HasNoUnitSlots));
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
    private static bool Replace<T>(ObservableCollection<T> destination, IEnumerable<T> source) => Replace(destination, source as IReadOnlyList<T> ?? source.ToArray());
}
