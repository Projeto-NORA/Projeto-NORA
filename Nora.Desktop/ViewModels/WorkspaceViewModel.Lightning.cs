using System.Collections.ObjectModel;
using System.Windows.Threading;
using Nora.Desktop.Generics;
using Nora.Desktop.Models;
using Nora.Desktop.Services;

namespace Nora.Desktop.ViewModels;

/// <summary>
/// Vaga Relâmpago da UBS ativa como painel automático de acompanhamento: a API oferece a vaga sozinha a quem tem consulta marcada
/// depois, na mesma especialidade. O OBS só lê as vagas (GET .../lightning-slots) e as ofertas de cada vaga (GET .../offers).
/// 
/// </summary>
public sealed partial class WorkspaceViewModel
{
    private UnitLightningSlot? selectedUnitSlot;
    private SlotPanelRow? selectedSlotRow;
    public ObservableCollection<UnitLightningSlot> UnitLightningSlots { get; } = [];
    /// <summary>Linhas do painel: uma por vaga, com o paciente e a contagem regressiva da oferta atual.</summary>
    public ObservableCollection<SlotPanelRow> SlotPanelRows { get; } = [];
    public ObservableCollection<SlotPanelRow> FilteredSlotPanelRows { get; } = [];
    private bool unitSlotsLoaded;
    private const string HistoryRestartedNotice = "O histórico carregado foi reiniciado porque muitos itens foram encerrados desde a última leitura. Use Carregar mais histórico para ver os mais antigos.";
    private readonly HistoryPager<UnitLightningSlot, Guid> slotsPager = new(slot => slot.Id, slot => slot.IsActive, slot => slot with { Status = "closed", ClosedReason = null });
    private string slotsHistoryNotice = "";
    // Ofertas das vagas encerradas não mudam mais: ficam guardadas para não reler a cada ciclo.
    private readonly Dictionary<Guid, UnitSlotOffer[]> closedOffers = [];
    private readonly Dictionary<Guid, UnitSlotOffer[]> activeOffers = [];
    private DispatcherTimer? countdownTimer;
    private int offersVersion;
    private string slotSpecialtyFilter = AllSpecialties, slotStatusFilter = "Todas", slotSearch = "";
    public const string AllSpecialties = "Todas as especialidades";
    public static string[] SlotStatusOptions { get; } = ["Todas", "Abertas", "Em oferta", "Aceitas", "Recusadas", "Vencidas", "Encerradas sem aceite"];
    public ObservableCollection<string> SlotSpecialtyOptions { get; } = [AllSpecialties];

    public UnitLightningSlot? SelectedUnitSlot { get => selectedUnitSlot; set { selectedUnitSlot = value; Notify(); } }
    public SlotPanelRow? SelectedSlotRow { get => selectedSlotRow; set { selectedSlotRow = value; Notify(); SelectedUnitSlot = value?.Slot; } }
    public string SlotSpecialtyFilter { get => slotSpecialtyFilter; set { slotSpecialtyFilter = value ?? AllSpecialties; Notify(); ApplySlotFilter(); } }
    public string SlotStatusFilter { get => slotStatusFilter; set { slotStatusFilter = value ?? "Todas"; Notify(); ApplySlotFilter(); } }
    public string SlotSearch { get => slotSearch; set { slotSearch = value ?? ""; Notify(); ApplySlotFilter(); } }

    public bool CanLoadMoreSlotsHistory => CanOperate && HasActiveObsUnit && slotsPager.HasMore;
    public string SlotsHistoryState => !HasActiveObsUnit || !unitSlotsLoaded ? "" : ((slotsHistoryNotice.Length > 0 ? slotsHistoryNotice + " " : "") + (slotsPager.HasMore ? "Há vagas encerradas mais antigas: use Carregar mais histórico." : "")).Trim();
    public bool HasNoUnitSlots => HasActiveObsUnit && unitSlotsLoaded && UnitLightningSlots.Count == 0;
    public bool HasNoFilteredSlots => unitSlotsLoaded && SlotPanelRows.Count > 0 && FilteredSlotPanelRows.Count == 0;

    // Contadores do painel (as vagas carregadas e as ofertas lidas de cada uma).
    public string PanelOpenCount => unitSlotsLoaded ? SlotPanelRows.Count(row => row.Slot.Status == "open").ToString() : "—";
    public string PanelOfferedCount => unitSlotsLoaded ? SlotPanelRows.Count(row => row.Slot.Status == "offered").ToString() : "—";
    public string PanelAcceptedCount => unitSlotsLoaded ? SlotPanelRows.Count(row => row.Slot.Status == "accepted").ToString() : "—";
    public string PanelDeclinedCount => unitSlotsLoaded ? SlotPanelRows.Sum(row => row.Declined).ToString() : "—";
    public string PanelExpiredCount => unitSlotsLoaded ? SlotPanelRows.Sum(row => row.Expired).ToString() : "—";

    private void ReselectSlot()
    {
        if (selectedSlotRow is { } row) SelectedSlotRow = FilteredSlotPanelRows.FirstOrDefault(item => item.Slot.Id == row.Slot.Id);
        else if (selectedUnitSlot is { } slot) SelectedUnitSlot = UnitLightningSlots.FirstOrDefault(item => item.Id == slot.Id);
    }

    public Task LoadUnitLightningSlotsAsync() => RunAsync(async () =>
    {
        var unit = ActiveObsUnit ?? throw new NoraApiException("Escolha uma UBS no Painel da UBS antes de consultar vagas.");
        await LoadUnitScopeAsync(unit, UnitScope.Slots);
        if (ActiveObsUnit?.Id == unit.Id) Say(MessageKind.Success, "Painel da Vaga Relâmpago atualizado.");
    });

    /// <summary>Acrescenta as vagas encerradas mais antigas às já carregadas, sem repetir nenhuma.</summary>
    public Task LoadMoreSlotsHistoryAsync() => RunAsync(async () =>
    {
        var unit = ActiveObsUnit ?? throw new NoraApiException("Escolha uma UBS no Painel da UBS antes de consultar vagas.");
        if (slotsPager.Cursor is not { } cursor) return;
        var page = await api.UnitLightningSlotsAsync(unit.Id, cursor);
        if (disposed || !IsAuthenticated || ActiveObsUnit?.Id != unit.Id) return;
        slotsHistoryNotice = "";
        ShowSlotList(slotsPager.ApplyOlder(page.Items, page.HistoryCursor), appended: true);
        Say(MessageKind.Success, slotsPager.HasMore ? "Histórico das vagas ampliado." : "Todo o histórico das vagas está na lista.");
    });

    /// <summary>Depois de uma alteração confirmada pela API: relê as partes afetadas. Se a releitura falhar, a alteração continua confirmada e os dados ficam marcados como possivelmente desatualizados.</summary>
    private async Task ReloadAfterChangeAsync(HealthUnit unit, UnitScope scope, string success)
    {
        try
        {
            await LoadUnitScopeAsync(unit, scope);
            MarkFresh();
            Say(MessageKind.Success, success);
        }
        catch (NoraApiException error) when (!error.Unauthorized)
        {
            MarkStale();
            Say(MessageKind.Success, success + " Não foi possível atualizar a tela; aguarde a atualização automática antes de repetir a operação.");
        }
    }

    private void ApplySlots(LightningSlotPage page)
    {
        var merged = slotsPager.ApplyPage(page.Items, page.HistoryCursor, out var restarted);
        slotsHistoryNotice = restarted ? HistoryRestartedNotice : "";
        ShowSlotList(merged);
    }

    private void ShowSlotList(IReadOnlyList<UnitLightningSlot> slots, bool appended = false)
    {
        if (appended) Grow(UnitLightningSlots, slots); else Replace(UnitLightningSlots, slots);
        unitSlotsLoaded = true;
        RebuildSlotRows();
        Notify(nameof(CanLoadMoreSlotsHistory)); Notify(nameof(SlotsHistoryState));
        if (ActiveObsUnit is { } unit) _ = LoadSlotOffersAsync(unit);
    }

    /// <summary>Lê as ofertas das vagas em andamento (a cada ciclo) e das encerradas ainda não lidas (uma vez). Falha de uma vaga não derruba o painel.</summary>
    private async Task LoadSlotOffersAsync(HealthUnit unit)
    {
        var version = ++offersVersion;
        // Vaga "aberta" ainda não tem oferta: só as ofertadas (a cada ciclo) e as encerradas ainda não lidas (uma vez) pedem as ofertas.
        var pending = UnitLightningSlots.Where(slot => slot.Status != "open" && (slot.IsActive || !closedOffers.ContainsKey(slot.Id))).Take(60).ToArray();
        foreach (var slot in pending)
        {
            UnitSlotOffer[] offers;
            try { offers = await api.UnitSlotOffersAsync(unit.Id, slot.Id); }
            catch (NoraApiException) { continue; }
            if (disposed || version != offersVersion || ActiveObsUnit?.Id != unit.Id) return;
            if (slot.IsActive) activeOffers[slot.Id] = offers; else closedOffers[slot.Id] = offers;
            SlotPanelRows.FirstOrDefault(row => row.Slot.Id == slot.Id)?.SetOffers(offers);
        }
        if (version == offersVersion) { NotifyPanelCounts(); ApplySlotFilter(); }
    }

    private void RebuildSlotRows()
    {
        var rows = UnitLightningSlots.Select(slot =>
        {
            var row = new SlotPanelRow(slot);
            if ((slot.IsActive ? activeOffers : closedOffers).TryGetValue(slot.Id, out var offers)) row.SetOffers(offers);
            return row;
        }).ToList();
        SlotPanelRows.Clear(); foreach (var row in rows) SlotPanelRows.Add(row);
        var specialties = UnitLightningSlots.Select(slot => slot.Specialty).Distinct(StringComparer.CurrentCultureIgnoreCase).Order(StringComparer.CurrentCultureIgnoreCase).ToList();
        if (!SlotSpecialtyOptions.Skip(1).SequenceEqual(specialties))
        {
            var keep = slotSpecialtyFilter;
            SlotSpecialtyOptions.Clear(); SlotSpecialtyOptions.Add(AllSpecialties); foreach (var name in specialties) SlotSpecialtyOptions.Add(name);
            slotSpecialtyFilter = SlotSpecialtyOptions.Contains(keep) ? keep : AllSpecialties; Notify(nameof(SlotSpecialtyFilter));
        }
        EnsureCountdown();
        NotifyPanelCounts();
        ApplySlotFilter();
    }

    /// <summary>Subaba "Em andamento" (live) mostra vagas abertas e ofertadas; "Encerradas" (closed) mostra o resto. Os filtros valem nas duas.</summary>
    public string SlotPanelScope { get => slotPanelScope; set { slotPanelScope = value ?? ""; Notify(); ApplySlotFilter(); } }
    private string slotPanelScope = "";

    private void ApplySlotFilter()
    {
        var search = SlotSearch.Trim();
        var filtered = SlotPanelRows.Where(row =>
                (SlotPanelScope != "live" || row.Slot.IsActive) && (SlotPanelScope != "closed" || !row.Slot.IsActive)
                && (SlotSpecialtyFilter == AllSpecialties || string.Equals(row.Slot.Specialty, SlotSpecialtyFilter, StringComparison.CurrentCultureIgnoreCase))
                && SlotStatusFilter switch
                {
                    "Abertas" => row.Slot.Status == "open",
                    "Em oferta" => row.Slot.Status == "offered",
                    "Aceitas" => row.Slot.Status == "accepted",
                    "Recusadas" => row.Declined > 0,
                    "Vencidas" => row.Expired > 0,
                    "Encerradas sem aceite" => row.Slot.Status == "closed",
                    _ => true
                }
                && (search.Length == 0 || row.PatientName.Contains(search, StringComparison.CurrentCultureIgnoreCase) || row.Slot.Professional.Contains(search, StringComparison.CurrentCultureIgnoreCase) || row.Slot.Specialty.Contains(search, StringComparison.CurrentCultureIgnoreCase)))
            // Em andamento: quem vence primeiro no topo; encerradas: as mais recentes primeiro (lista cronológica).
            .OrderBy(row => row.Slot.IsActive ? 0 : 1).ThenBy(row => row.Slot.IsActive ? row.Slot.StartsAt : DateTimeOffset.MaxValue - (row.Slot.StartsAt - DateTimeOffset.MinValue))
            .ToList();
        var selected = selectedSlotRow?.Slot.Id;
        FilteredSlotPanelRows.Clear(); foreach (var row in filtered) FilteredSlotPanelRows.Add(row);
        selectedSlotRow = selected is null ? null : FilteredSlotPanelRows.FirstOrDefault(row => row.Slot.Id == selected);
        Notify(nameof(SelectedSlotRow)); Notify(nameof(HasNoFilteredSlots)); Notify(nameof(HasNoUnitSlots));
    }

    private void NotifyPanelCounts()
    {
        foreach (var name in new[] { nameof(PanelOpenCount), nameof(PanelOfferedCount), nameof(PanelAcceptedCount), nameof(PanelDeclinedCount), nameof(PanelExpiredCount), nameof(SlotOpenCount), nameof(OffersRunningCount) }) Notify(name);
    }

    /// <summary>Contagem regressiva do prazo da oferta atual, atualizada a cada segundo enquanto há oferta em andamento.</summary>
    private void EnsureCountdown()
    {
        var needed = SlotPanelRows.Any(row => row.HasDeadline);
        if (!needed) { countdownTimer?.Stop(); return; }
        if (countdownTimer is null) { countdownTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) }; countdownTimer.Tick += (_, _) => { foreach (var row in SlotPanelRows) row.Tick(); }; }
        countdownTimer.Start();
    }

    private void ClearUnitLightningData()
    {
        UnitLightningSlots.Clear(); unitSlotsLoaded = false;
        SlotPanelRows.Clear(); FilteredSlotPanelRows.Clear(); closedOffers.Clear(); activeOffers.Clear(); offersVersion++; countdownTimer?.Stop();
        slotsPager.Clear(); slotsHistoryNotice = "";
        SelectedUnitSlot = null; selectedSlotRow = null;
        NotifyUnitCounts(); NotifyPanelCounts();
        foreach (var name in new[] { nameof(CanLoadMoreSlotsHistory), nameof(SlotsHistoryState), nameof(HasNoFilteredSlots), nameof(SelectedSlotRow) }) Notify(name);
    }
}

/// <summary>Linha do painel da Vaga Relâmpago: vaga, paciente da oferta atual (ou de quem aceitou) e o prazo correndo.</summary>
public sealed class SlotPanelRow(UnitLightningSlot slot) : ObservableObject
{
    private UnitSlotOffer[] offers = [];
    public UnitLightningSlot Slot { get; } = slot;
    public string Specialty => Slot.Specialty;
    public string Professional => Slot.Professional;
    public string TimeDisplay => Slot.TimeDisplay;
    public int Declined => offers.Count(offer => offer.Status == "declined");
    public int Expired => offers.Count(offer => offer.Status == "expired");
    private UnitSlotOffer? Current => offers.LastOrDefault(offer => offer.Status == "pending");
    private UnitSlotOffer? Accepted => offers.LastOrDefault(offer => offer.Status == "accepted");
    public string PatientName => (Accepted ?? Current ?? offers.LastOrDefault())?.PatientName ?? (Slot.Status == "open" ? "Procurando paciente…" : "—");
    public bool HasDeadline => Current is not null;
    public string StatusDisplay => Slot.Status switch
    {
        "open" => "Aberta",
        "offered" => "Em oferta",
        "accepted" => "Aceita",
        _ => Slot.StatusDisplay
    };
    public string OffersSummary => offers.Length == 0 ? "Nenhuma oferta ainda" : $"{offers.Length} oferta(s) · {Declined} recusada(s) · {Expired} vencida(s)";
    /// <summary>Tempo que falta para a oferta atual vencer ("04:32"), ou o desfecho quando não há oferta correndo.</summary>
    public string Countdown
    {
        get
        {
            if (Current is { } current)
            {
                var left = current.ExpiresAt - DateTimeOffset.Now;
                return left <= TimeSpan.Zero ? "Vencendo…" : $"{(int)left.TotalMinutes:00}:{left.Seconds:00}";
            }
            return Slot.Status switch { "accepted" => "Aceita", "open" => "Aguardando oferta", _ => "—" };
        }
    }
    public string CountdownTip => Current is { } current ? $"A oferta vence às {current.ExpiresAt.ToLocalTime():HH:mm:ss}. Sem resposta, a vaga vai para a próxima pessoa automaticamente." : "Sem oferta correndo agora.";
    /// <summary>Faltam menos de 2 minutos: a contagem fica em destaque.</summary>
    public bool IsUrgent => Current is { } current && current.ExpiresAt - DateTimeOffset.Now < TimeSpan.FromMinutes(2);
    public void SetOffers(UnitSlotOffer[] value)
    {
        offers = value;
        foreach (var name in new[] { nameof(PatientName), nameof(HasDeadline), nameof(OffersSummary), nameof(Countdown), nameof(CountdownTip), nameof(IsUrgent), nameof(Declined), nameof(Expired) }) Notify(name);
    }
    public void Tick() { if (HasDeadline) { Notify(nameof(Countdown)); Notify(nameof(IsUrgent)); } }
}
