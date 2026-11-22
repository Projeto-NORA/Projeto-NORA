using System.Collections.ObjectModel;
using Nora.Desktop.Models;
using Nora.Desktop.Services;

namespace Nora.Desktop.ViewModels;

/// <summary>
/// OBS: consultórios ativos e capacidade dos próximos 7 dias (Painel), capacidade e metas do período (Indicadores) e alertas avaliados pela API.
/// Tudo vem de leituras tolerantes: se uma falhar, o cartão avisa e o resto da UBS continua funcionando.
/// </summary>
public sealed partial class WorkspaceViewModel
{
    private const int CapacityDays = 7;
    private static readonly TimeSpan AlertsRefreshAge = TimeSpan.FromMinutes(5);
    private ActiveRooms? activeRooms;
    private bool activeRoomsFailed, capacityFailed, unitAlertsFailed;
    private CapacityMetrics? panelCapacity;
    private DateTimeOffset? alertsAttemptedAt, alertsReadAt;
    private readonly HashSet<(Guid Unit, string Kind, string Specialty, string Metric)> activeAlertKeys = [];
    private readonly HashSet<(Guid Unit, string Kind, string Specialty, string Metric)> viewedAlertKeys = [];
    private int alertBadgeCount;
    public int AlertBadgeCount { get => alertBadgeCount; private set { if (alertBadgeCount == value) return; alertBadgeCount = value; Notify(); } }
    private void MarkAlertsViewed() { viewedAlertKeys.UnionWith(activeAlertKeys); AlertBadgeCount = 0; }

    public ObservableCollection<MetricCard> UnitCapacityCards { get; } = [];
    public ObservableCollection<GoalCell> UnitGoalCells { get; } = [];
    public ObservableCollection<AlertRow> UnitAlerts { get; } = [];
    /// <summary>Os primeiros alertas, para o cartão "Atenção da equipe" do Painel.</summary>
    public ObservableCollection<AlertRow> UnitAlertPreview { get; } = [];
    private bool AlertsDue => alertsAttemptedAt is null || DateTimeOffset.Now - alertsAttemptedAt >= AlertsRefreshAge;

    /// <summary>Avalia os alertas da UBS ativa de novo (botão Atualizar alertas).</summary>
    public Task RefreshAlertsAsync() => RunAsync(async () =>
    {
        var unit = ActiveObsUnit ?? throw new NoraApiException("Escolha uma UBS no Painel da UBS antes de consultar os alertas.");
        await LoadUnitScopeAsync(unit, UnitScope.Alerts);
        if (ActiveObsUnit?.Id != unit.Id) return;
        if (unitAlertsFailed) Say(MessageKind.Error, "Não foi possível avaliar os alertas agora. A lista mostra a última avaliação concluída, se houver.");
        else Say(MessageKind.Success, UnitAlerts.Count == 0 ? "Alertas avaliados: nenhum alerta." : $"Alertas avaliados: {UnitAlerts.Count}.");
    });

    // ---- Painel: consultórios ativos ----

    public string ActiveRoomsInUseValue => activeRooms is { } rooms ? rooms.RoomsInUse.ToString() : "—";
    public bool ActiveRoomsHasError => activeRoomsFailed;
    public string ActiveRoomsPlannedValue => activeRooms is { } rooms ? rooms.RoomsPlannedToday.ToString() : "—";
    public string ActiveRoomsHint => !HasActiveObsUnit ? "Aguardando a escolha da UBS"
        : activeRooms is null ? (activeRoomsFailed ? "Não foi possível ler os consultórios agora." : "Aguardando a leitura dos consultórios")
        : activeRoomsFailed ? "A última leitura não concluiu; estes são os números da leitura anterior."
        : "Em atendimento: consulta chamada hoje. Com grade: horário previsto hoje.";

    // ---- Painel: capacidade dos próximos 7 dias ----

    public string PanelOfferedValue => panelCapacity is { } capacity ? DisplayFormat.Count(capacity.OfferedSlots) : "—";
    public string PanelBookedValue => panelCapacity is { } capacity ? DisplayFormat.Count(capacity.BookedSlots) : "—";
    public string PanelOccupancyValue => panelCapacity is { } capacity ? DisplayFormat.Percent(capacity.Occupancy) : "—";
    public string PanelCapacityHint => !HasActiveObsUnit ? "Aguardando a escolha da UBS"
        : panelCapacity is null ? (capacityFailed ? "Não foi possível ler a capacidade agora." : "Aguardando a leitura da capacidade")
        : capacityFailed ? "A última leitura não concluiu; estes são os números da leitura anterior."
        : panelCapacity.OfferedSlots == 0 ? "Nenhum horário ofertado nos próximos 7 dias: a grade da UBS está vazia ou tudo está bloqueado."
        : "Horários da grade de hoje até daqui a 7 dias. Ocupado é o horário com consulta marcada.";

    // ---- Alertas ----

    public string UnitAlertsState => !IsAuthenticated ? "Entre na conta para consultar os alertas."
        : !HasActiveObsUnit ? "Escolha uma UBS no Painel da UBS para ver os alertas."
        : unitAlertsFailed && alertsReadAt is null ? "Não foi possível avaliar os alertas agora. Use Atualizar alertas para tentar de novo."
        : alertsReadAt is null ? "Avaliando os alertas…"
        : UnitAlerts.Count == 0 && unitAlertsFailed ? $"A última avaliação não concluiu. Na avaliação anterior, das {alertsReadAt:HH:mm}, não havia alerta. Use Atualizar alertas para tentar de novo."
        : UnitAlerts.Count > 0 && unitAlertsFailed ? $"A última avaliação não concluiu; estes são os alertas da avaliação anterior, das {alertsReadAt:HH:mm}."
        : UnitAlerts.Count == 0 ? $"Nenhum alerta. Nenhum critério (fila alta, espera longa, faltas, ocupação da grade e metas) passou do limite na leitura das {alertsReadAt:HH:mm}."
        : "";
    /// <summary>Só uma avaliação concluída e sem alerta mostra o ícone verde; falha ou carregamento nunca viram "tudo certo".</summary>
    public bool UnitAlertsAllClear => IsAuthenticated && HasActiveObsUnit && alertsReadAt is not null && UnitAlerts.Count == 0 && !unitAlertsFailed;
    public string UnitAlertsFootnote => UnitAlerts.Count == 0 ? "" : $"Avaliado às {alertsReadAt:HH:mm}. O \"desde\" só existe para a espera longa; os demais alertas mostram a situação atual.";
    public bool HasUnitAlerts => UnitAlerts.Count > 0;
    public string PanelAlertsTitle => !HasActiveObsUnit ? "Aguardando a escolha da UBS"
        : alertsReadAt is null ? (unitAlertsFailed ? "Alertas indisponíveis agora" : "Avaliando os alertas…")
        : UnitAlerts.Count == 0 ? (unitAlertsFailed ? "Última avaliação não concluiu" : "Nenhum alerta") : UnitAlerts.Count == 1 ? "1 alerta ativo" : $"{UnitAlerts.Count} alertas ativos";
    public string PanelAlertsText => !HasActiveObsUnit ? "Escolha uma UBS para avaliar fila, espera, faltas, ocupação e metas."
        : alertsReadAt is null ? (unitAlertsFailed ? "Não foi possível avaliar os alertas agora." : "")
        : UnitAlerts.Count == 0 ? (unitAlertsFailed ? $"A última avaliação não concluiu; na avaliação anterior, das {alertsReadAt:HH:mm}, não havia alerta." : $"Nenhum critério passou do limite na leitura das {alertsReadAt:HH:mm}.")
        : unitAlertsFailed ? "A última avaliação não concluiu; estes são os alertas da avaliação anterior."
        : UnitAlerts.Count > UnitAlertPreview.Count ? "Os primeiros alertas da avaliação; veja todos na central de alertas:" : "Alertas da avaliação da API:";

    private static readonly string[] SchedulingNames =
    [
        nameof(ActiveRoomsInUseValue), nameof(ActiveRoomsPlannedValue), nameof(ActiveRoomsHint), nameof(ActiveRoomsHasError), nameof(PanelOfferedValue), nameof(PanelBookedValue), nameof(PanelOccupancyValue), nameof(PanelCapacityHint),
        nameof(UnitAlertsState), nameof(UnitAlertsFootnote), nameof(HasUnitAlerts), nameof(UnitAlertsAllClear), nameof(PanelAlertsTitle), nameof(PanelAlertsText), nameof(UnitGoalsState)
    ];
    private void NotifyScheduling() { foreach (var name in SchedulingNames) Notify(name); }

    private void ApplyActiveRooms(Tolerant<ActiveRooms> read)
    {
        activeRoomsFailed = read.Failed;
        if (read.Value is { } value) activeRooms = value;
        NotifyScheduling();
    }

    private void ApplyPanelCapacity(Tolerant<UnitMetrics> read)
    {
        capacityFailed = read.Failed || read.Value?.Capacity is null;
        if (read.Value?.Capacity is { } capacity) panelCapacity = capacity;
        NotifyScheduling();
    }

    private void ApplyUnitAlerts(Tolerant<AlertItem[]> read, DateTimeOffset readAt)
    {
        alertsAttemptedAt = readAt;
        unitAlertsFailed = read.Failed;
        if (read.Value is { } alerts)
        {
            alertsReadAt = readAt;
            activeAlertKeys.Clear();
            foreach (var alert in alerts) activeAlertKeys.Add((alert.UnitId, alert.Kind, (alert.Specialty ?? "").Trim().ToUpperInvariant(), alert.Metric ?? ""));
            viewedAlertKeys.IntersectWith(activeAlertKeys);
            if (Section == "alerts") viewedAlertKeys.UnionWith(activeAlertKeys);
            AlertBadgeCount = activeAlertKeys.Count(key => !viewedAlertKeys.Contains(key));
            var rows = alerts.Select(AlertRow.From).ToArray();
            Replace(UnitAlerts, rows);
            Replace(UnitAlertPreview, rows.Take(3).ToArray());
        }
        NotifyScheduling();
    }

    // ---- Indicadores: capacidade e metas do período ----

    public string UnitGoalsState => unitMetrics is null ? "" : UnitGoalCells.Count == 0 ? "Nenhuma meta definida pela Secretaria para esta UBS nem para a rede." : "";

    /// <summary>Três cartões da capacidade da janela: ofertados, ocupados e ocupação; sem o campo na resposta nada é afirmado.</summary>
    private static MetricCard[] BuildCapacityCards(CapacityMetrics? capacity, string windowHint)
    {
        if (capacity is null) return [new("Horários ofertados", "—", "Aguardando os indicadores"), new("Horários ocupados", "—", "Aguardando os indicadores"), new("Ocupação da grade", "—", "Aguardando os indicadores")];
        var none = capacity.OfferedSlots == 0;
        return
        [
            new("Horários ofertados", DisplayFormat.Count(capacity.OfferedSlots), none ? "Nenhum horário na grade neste período" : windowHint),
            new("Horários ocupados", DisplayFormat.Count(capacity.BookedSlots), "Horários da grade com consulta marcada"),
            new("Ocupação da grade", DisplayFormat.Percent(capacity.Occupancy), capacity.Occupancy is null ? "Sem horários ofertados para calcular" : "Ocupados ÷ ofertados")
        ];
    }

    private void ApplyUnitCapacityAndGoals(UnitMetrics result)
    {
        Replace(UnitCapacityCards, BuildCapacityCards(result.Capacity, "Horários da grade com início no período"));
        Replace(UnitGoalCells, (result.Goals ?? []).Select(GoalCell.From).ToArray());
        NotifyScheduling();
    }

    private void ClearUnitScheduling()
    {
        activeRooms = null; activeRoomsFailed = false; panelCapacity = null; capacityFailed = false;
        unitAlertsFailed = false; alertsAttemptedAt = alertsReadAt = null;
        Replace(UnitCapacityCards, BuildCapacityCards(null, "")); Replace(UnitGoalCells, Array.Empty<GoalCell>());
        UnitAlerts.Clear(); UnitAlertPreview.Clear();
        activeAlertKeys.Clear(); viewedAlertKeys.Clear(); AlertBadgeCount = 0;
        ClearCatalog();
        NotifyScheduling();
    }
}
