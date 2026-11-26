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
    public ObservableCollection<UnitLightningSlot> UnitLightningSlots { get; } = [];
    /// <summary>Linhas do painel: uma por vaga, com o paciente e a contagem regressiva da oferta atual. A linha de uma vaga que não mudou é a mesma a cada leitura: a lista da tela não se recria e a seleção fica.</summary>
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

    public string SlotSpecialtyFilter { get => slotSpecialtyFilter; set { slotSpecialtyFilter = value ?? AllSpecialties; Notify(); ApplySlotFilter(); } }
    public string SlotStatusFilter { get => slotStatusFilter; set { slotStatusFilter = value ?? "Todas"; Notify(); ApplySlotFilter(); } }
    public string SlotSearch { get => slotSearch; set { slotSearch = value ?? ""; Notify(); ApplySlotFilter(); } }

    public bool CanLoadMoreSlotsHistory => CanOperate && HasActiveObsUnit && slotsPager.HasMore;
    public string SlotsHistoryState => !HasActiveObsUnit || !unitSlotsLoaded ? "" : ((slotsHistoryNotice.Length > 0 ? slotsHistoryNotice + " " : "") + (slotsPager.HasMore ? "Há vagas encerradas mais antigas: use Carregar mais histórico." : "")).Trim();
    public bool HasNoUnitSlots => HasActiveObsUnit && unitSlotsLoaded && UnitLightningSlots.Count == 0;
    /// <summary>A primeira leitura das vagas da UBS ainda não chegou: a tela mostra o indicador e o passo escrito.</summary>
    public bool IsSlotsLoading => HasActiveObsUnit && !unitSlotsLoaded;
    public bool HasSlotsFilter => SlotSpecialtyFilter != AllSpecialties || SlotStatusFilter != "Todas" || SlotSearch.Trim().Length > 0;
    /// <summary>
    /// Texto da lista vazia, sempre um dos dois estados distintos: "nenhuma vaga cadastrada" (a UBS não tem vaga na subtela) ou "nenhuma vaga encontrada para esta busca" (os filtros esconderam tudo).
    /// Vazio quando há vagas para mostrar ou a primeira leitura ainda não chegou.
    /// </summary>
    public string SlotsEmptyText => !HasActiveObsUnit || !unitSlotsLoaded || FilteredSlotPanelRows.Count > 0 ? ""
        : HasNoUnitSlots ? "Nenhuma vaga cadastrada nesta UBS até agora."
        : HasSlotsFilter && SlotPanelRows.Any(InSlotScope) ? "Nenhuma vaga encontrada para esta busca."
        : SlotPanelScope == "closed" ? "Nenhuma vaga encerrada." : "Nenhuma vaga em andamento.";

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
        // A linha de uma vaga que não mudou continua a mesma (com as ofertas já lidas): a lista da tela só troca o que mudou, e a seleção e o balão não piscam.
        var existing = SlotPanelRows.ToDictionary(row => row.Slot.Id);
        var rows = UnitLightningSlots.Select(slot =>
        {
            if (existing.TryGetValue(slot.Id, out var kept) && kept.Slot == slot) return kept;
            var row = new SlotPanelRow(slot);
            if ((slot.IsActive ? activeOffers : closedOffers).TryGetValue(slot.Id, out var offers)) row.SetOffers(offers);
            return row;
        }).ToList();
        Replace(SlotPanelRows, rows);
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

    /// <summary>A linha pertence à subtela aberta: "Em andamento" mostra as vagas abertas e ofertadas; "Encerradas", o resto.</summary>
    private bool InSlotScope(SlotPanelRow row) => (SlotPanelScope != "live" || row.Slot.IsActive) && (SlotPanelScope != "closed" || !row.Slot.IsActive);

    private void ApplySlotFilter()
    {
        var search = SlotSearch.Trim();
        var filtered = SlotPanelRows.Where(row =>
                InSlotScope(row)
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
        // No lugar: sem mudança nenhum evento é gerado e a lista da tela (seleção, rolagem e balão) não se mexe.
        Replace(FilteredSlotPanelRows, filtered);
        foreach (var name in new[] { nameof(SlotsEmptyText), nameof(HasSlotsFilter), nameof(IsSlotsLoading) }) Notify(name);
    }

    private void NotifyPanelCounts()
    {
        foreach (var name in new[] { nameof(SlotOpenCount), nameof(OffersRunningCount) }) Notify(name);
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
        NotifyUnitCounts(); NotifyPanelCounts();
        foreach (var name in new[] { nameof(CanLoadMoreSlotsHistory), nameof(SlotsHistoryState), nameof(SlotsEmptyText), nameof(IsSlotsLoading) }) Notify(name);
    }
}

/// <summary>
/// Linha do painel da Vaga Relâmpago: vaga, paciente da oferta atual (ou de quem aceitou) e o prazo correndo. Os textos da listagem (título, linha de dados, etiqueta) saem prontos daqui;
/// só o prazo muda a cada segundo, e ele muda na própria linha, sem recriar a lista.
/// </summary>
public sealed class SlotPanelRow(UnitLightningSlot slot) : ObservableObject
{
    private UnitSlotOffer[] offers = [];
    public UnitLightningSlot Slot { get; } = slot;
    /// <summary>Identidade da linha: a seleção da lista é guardada por ela quando a lista é lida de novo.</summary>
    public Guid Id => Slot.Id;
    public string Specialty => Slot.Specialty;
    public string Professional => Slot.Professional;
    public int Declined => offers.Count(offer => offer.Status == "declined");
    public int Expired => offers.Count(offer => offer.Status == "expired");
    private UnitSlotOffer? Current => offers.LastOrDefault(offer => offer.Status == "pending");
    private UnitSlotOffer? Accepted => offers.LastOrDefault(offer => offer.Status == "accepted");
    public string PatientName => (Accepted ?? Current ?? offers.LastOrDefault())?.PatientName ?? (Slot.Status == "open" ? "Procurando paciente…" : "—");
    public bool HasDeadline => Current is not null;

    /// <summary>Primeira linha do item: o horário da vaga e a especialidade.</summary>
    public string ItemTitle => $"{Slot.TimeDisplay} · {Slot.Specialty}";
    /// <summary>Profissional e paciente, só o que está informado, separados por " | " (o prazo vem logo depois, no próprio item, porque muda a cada segundo).</summary>
    public string MetaLead => string.Join(" | ", new[] { Slot.Professional, PatientName }.Where(text => !string.IsNullOrWhiteSpace(text) && text != "—"));
    public string SeparatorBeforeDeadline => MetaLead.Length > 0 && HasDeadline ? " | " : "";
    /// <summary>Situação da vaga na etiqueta à direita (curta); a ficha traz o texto completo.</summary>
    public string StatusDisplay => Slot.Status switch
    {
        "open" => "Aberta",
        "offered" => "Em oferta",
        "accepted" => "Aceita",
        _ => Slot.ClosedReason switch { "conflict" => "Horário ocupado", "too-late" => "Menos de 10 min", _ => "Encerrada" }
    };
    public Tone StatusTone => Slot.Status switch
    {
        "open" => Tone.Blue,
        "offered" => Tone.Teal,
        "accepted" => Tone.Green,
        _ => Slot.ClosedReason is "conflict" or "too-late" ? Tone.Yellow : Tone.Neutral
    };
    public string AutomationLabel => $"{ItemTitle}. {StatusDisplay}. {MetaLead}.";
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
    /// <summary>"prazo 04:32": só existe com oferta correndo.</summary>
    public string DeadlineText => Current is null ? "" : "prazo " + Countdown;
    public string CountdownTip => Current is { } current ? $"A oferta vence às {current.ExpiresAt.ToLocalTime():HH:mm:ss}. Sem resposta, a vaga vai para a próxima pessoa automaticamente." : "Sem oferta correndo agora.";
    /// <summary>Faltam menos de 2 minutos: a contagem fica em destaque.</summary>
    public bool IsUrgent => Current is { } current && current.ExpiresAt - DateTimeOffset.Now < TimeSpan.FromMinutes(2);
    public void SetOffers(UnitSlotOffer[] value)
    {
        offers = value;
        foreach (var name in new[] { nameof(PatientName), nameof(MetaLead), nameof(SeparatorBeforeDeadline), nameof(AutomationLabel), nameof(HasDeadline), nameof(DeadlineText), nameof(Countdown), nameof(CountdownTip), nameof(IsUrgent), nameof(Declined), nameof(Expired) }) Notify(name);
    }
    public void Tick() { if (HasDeadline) { Notify(nameof(DeadlineText)); Notify(nameof(IsUrgent)); } }
}
