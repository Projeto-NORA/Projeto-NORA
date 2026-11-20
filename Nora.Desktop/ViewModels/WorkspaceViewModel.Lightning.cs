using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using Nora.Desktop.Generics;
using Nora.Desktop.Models;
using Nora.Desktop.Services;

namespace Nora.Desktop.ViewModels;

/// <summary>Fila por especialidade e Vagas Relâmpago da UBS ativa.</summary>
public sealed partial class WorkspaceViewModel
{
    private UnitPatientRecord? waitingPatient;
    private UnitWaitingEntry? selectedWaitingEntry;
    private UnitLightningSlot? selectedUnitSlot;
    private string waitingSpecialty = "";
    public ObservableCollection<UnitWaitingEntry> UnitWaitingList { get; } = [];
    public ObservableCollection<UnitLightningSlot> UnitLightningSlots { get; } = [];
    private bool unitQueueLoaded, unitSlotsLoaded;
    private const string HistoryRestartedNotice = "O histórico carregado foi reiniciado porque muitos itens foram encerrados desde a última leitura. Use Carregar mais histórico.";
    // Histórico paginado (P4): a API devolve todos os itens ativos e as encerradas mais recentes; o resto vem por "Carregar mais histórico". O ciclo de 20 s relê só a primeira página e preserva o que já foi carregado (ver HistoryPager).
    private readonly HistoryPager<UnitWaitingEntry, long> queuePager = new(entry => entry.Id, entry => entry.IsActive, entry => entry with { Status = "closed", Position = 0, WaitDisplay = "—" });
    private readonly HistoryPager<UnitLightningSlot, Guid> slotsPager = new(slot => slot.Id, slot => slot.IsActive, slot => slot with { Status = "closed", ClosedReason = null });
    private string queueHistoryNotice = "", slotsHistoryNotice = "";
    public UnitPatientRecord? WaitingPatient { get => waitingPatient; set { waitingPatient = value; Notify(); } }
    public UnitWaitingEntry? SelectedWaitingEntry { get => selectedWaitingEntry; set { selectedWaitingEntry = value; Notify(); Notify(nameof(CanCancelWaitingEntry)); } }
    public UnitLightningSlot? SelectedUnitSlot { get => selectedUnitSlot; set { selectedUnitSlot = value; Notify(); } }
    // Filtro da tabela da fila: só esconde linhas da vista (a ordem de oferta e o Chamar próximo seguem a lista inteira). Linha escondida perde a seleção e ela não volta ao limpar o filtro.
    private string queueSearch = "", queueSpecialtyFilter = "";
    private ICollectionView? queueView;
    public ObservableCollection<FilterOption> QueueSpecialtyOptions { get; } = [];
    public ICollectionView QueueView => queueView ??= CreateQueueView();
    public string QueueSearch { get => queueSearch; set { queueSearch = value ?? ""; Notify(); RefreshQueueView(); } }
    public string QueueSpecialtyFilter { get => queueSpecialtyFilter; set { queueSpecialtyFilter = value ?? ""; Notify(); RefreshQueueView(); } }
    public bool HasQueueFilter => queueSearch.Trim().Length > 0 || queueSpecialtyFilter.Length > 0;
    /// <summary>Fila com entradas, mas nenhuma passa pelo filtro: a tela diz isso em vez de mostrar uma tabela vazia.</summary>
    public bool QueueFilterHidesAll => UnitWaitingList.Count > 0 && QueueView.IsEmpty;
    private ICollectionView CreateQueueView()
    {
        var view = new ListCollectionView(UnitWaitingList);
        view.Filter = item => item is UnitWaitingEntry entry && QueueMatches(entry);
        return view;
    }
    private bool QueueMatches(UnitWaitingEntry entry) =>
        (queueSpecialtyFilter.Length == 0 || string.Equals(entry.Specialty.Trim(), queueSpecialtyFilter, StringComparison.CurrentCultureIgnoreCase))
        && (queueSearch.Trim().Length == 0 || entry.PatientName.Contains(queueSearch.Trim(), StringComparison.CurrentCultureIgnoreCase));
    private void RefreshQueueView() { QueueView.Refresh(); if (selectedWaitingEntry is { } hidden && !QueueMatches(hidden)) SelectedWaitingEntry = null; Notify(nameof(HasQueueFilter)); Notify(nameof(QueueFilterHidesAll)); }
    /// <summary>Opções do filtro = especialidades presentes na fila; o filtro escolhido que sumiu da fila volta para "todas".</summary>
    private void RebuildQueueSpecialtyOptions()
    {
        var names = UnitWaitingList.Select(entry => entry.Specialty.Trim()).Where(name => name.Length > 0).Distinct(StringComparer.CurrentCultureIgnoreCase).OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase);
        var wanted = new[] { new FilterOption("", "Todas as especialidades") }.Concat(names.Select(name => new FilterOption(name, name))).ToArray();
        // Por remoção e inserção, sem Clear: o Clear faria a ComboBox perder o item escolhido e gravar null no filtro.
        for (var index = QueueSpecialtyOptions.Count - 1; index >= 0; index--)
            if (!wanted.Any(option => string.Equals(option.Value, QueueSpecialtyOptions[index].Value, StringComparison.CurrentCultureIgnoreCase))) QueueSpecialtyOptions.RemoveAt(index);
        for (var index = 0; index < wanted.Length; index++)
            if (index >= QueueSpecialtyOptions.Count || !string.Equals(QueueSpecialtyOptions[index].Value, wanted[index].Value, StringComparison.CurrentCultureIgnoreCase)) QueueSpecialtyOptions.Insert(index, wanted[index]);
        if (queueSpecialtyFilter.Length > 0 && QueueSpecialtyOptions.All(option => !string.Equals(option.Value, queueSpecialtyFilter, StringComparison.CurrentCultureIgnoreCase))) QueueSpecialtyFilter = "";
        // A vista aplica o Filter sozinha a cada Add/Replace da lista; Refresh aqui mandaria um Reset à tabela (rolagem e foco voltariam ao topo).
        else Notify(nameof(QueueFilterHidesAll));
    }
    /// <summary>Estado da caixa de especialidade da fila: o catálogo da rede é a única origem das opções.</summary>
    public string QueueSpecialtyState => !HasActiveObsUnit ? ""
        : specialtiesFailed ? "Não foi possível ler as especialidades. Use Atualizar fila da UBS."
        : !specialtiesLoaded ? "Carregando as especialidades…"
        : !ObsSpecialties.Any(item => item.IsActive) ? "Nenhuma especialidade ativa no catálogo da rede." : "";
    public string WaitingSpecialty { get => waitingSpecialty; set { waitingSpecialty = value ?? ""; Notify(); } }
    /// <summary>Cancelar só vale para entrada ainda ativa (aguardando ou com oferta em andamento).</summary>
    public bool CanCancelWaitingEntry => CanOperate && SelectedWaitingEntry?.IsActive == true;
    public bool HasNoWaitingEntries => HasActiveObsUnit && unitQueueLoaded && UnitWaitingList.Count == 0;
    public bool CanLoadMoreQueueHistory => CanOperate && HasActiveObsUnit && queuePager.HasMore;
    public bool CanLoadMoreSlotsHistory => CanOperate && HasActiveObsUnit && slotsPager.HasMore;
    public string QueueHistoryState => HistoryText(unitQueueLoaded, queuePager.HasMore, queueHistoryNotice, "entradas encerradas");
    public string SlotsHistoryState => HistoryText(unitSlotsLoaded, slotsPager.HasMore, slotsHistoryNotice, "vagas encerradas");
    private string HistoryText(bool loaded, bool hasMore, string notice, string what) =>
        !HasActiveObsUnit || !loaded ? "" : ((notice.Length > 0 ? notice + " " : "") + (hasMore ? $"Há {what} mais antigas: use Carregar mais histórico." : "")).Trim();
    public bool HasNoUnitSlots => HasActiveObsUnit && unitSlotsLoaded && UnitLightningSlots.Count == 0;

    private void ReselectSlot() { if (selectedUnitSlot is { } slot) SelectedUnitSlot = UnitLightningSlots.FirstOrDefault(item => item.Id == slot.Id); }

    public Task LoadUnitWaitingListAsync() => RunAsync(async () =>
    {
        var unit = ActiveObsUnit ?? throw new NoraApiException("Escolha uma UBS no Painel da UBS antes de consultar a fila.");
        await LoadUnitScopeAsync(unit, catalogFailed || !catalogLoaded ? UnitScope.Queue | UnitScope.Catalog : UnitScope.Queue);
        if (ActiveObsUnit?.Id == unit.Id) Say(MessageKind.Success, "Fila da UBS atualizada.");
    });

    public Task AddUnitWaitingEntryAsync() => RunAsync(async () =>
    {
        var unit = ActiveObsUnit ?? throw new NoraApiException("Escolha uma UBS no Painel da UBS antes de incluir na fila.");
        var patient = WaitingPatient ?? throw new NoraApiException("Selecione o paciente cadastrado na UBS.");
        if (!patient.IsActive) throw new NoraApiException("O paciente precisa ter um cadastro ativo nesta UBS.");
        var specialty = WaitingSpecialty.Trim();
        if (specialty.Length == 0) throw new NoraApiException("Escolha a especialidade.");
        if (specialty.Length > 120) throw new NoraApiException("A especialidade pode ter até 120 caracteres.");
        await api.AddUnitWaitingEntryAsync(unit.Id, patient.Cpf, specialty);
        WaitingPatient = null;
        await ReloadAfterChangeAsync(unit, UnitScope.Queue, "Paciente incluído na fila da UBS.");
    });

    public Task CancelUnitWaitingEntryAsync() => RunAsync(async () =>
    {
        var unit = ActiveObsUnit ?? throw new NoraApiException("Escolha uma UBS antes de cancelar a entrada.");
        var entry = SelectedWaitingEntry ?? throw new NoraApiException("Selecione uma entrada da fila.");
        if (!entry.IsActive) throw new NoraApiException("Esta entrada já está encerrada.");
        await api.CancelUnitWaitingEntryAsync(unit.Id, entry.Id);
        SelectedWaitingEntry = null;
        await ReloadAfterChangeAsync(unit, UnitScope.Queue, "Entrada cancelada.");
    });

    public Task LoadUnitLightningSlotsAsync() => RunAsync(async () =>
    {
        var unit = ActiveObsUnit ?? throw new NoraApiException("Escolha uma UBS no Painel da UBS antes de consultar vagas.");
        await LoadUnitScopeAsync(unit, UnitScope.Slots);
        if (ActiveObsUnit?.Id == unit.Id) Say(MessageKind.Success, "Vagas da UBS atualizadas.");
    });

    /// <summary>Pede à API as entradas encerradas mais antigas que as já carregadas e as acrescenta ao fim da lista, sem repetir nenhuma.</summary>
    public Task LoadMoreQueueHistoryAsync() => RunAsync(async () =>
    {
        var unit = ActiveObsUnit ?? throw new NoraApiException("Escolha uma UBS no Painel da UBS antes de consultar a fila.");
        if (queuePager.Cursor is not { } cursor) return;
        var page = await api.UnitWaitingListAsync(unit.Id, cursor);
        if (disposed || !IsAuthenticated || ActiveObsUnit?.Id != unit.Id) return;
        queueHistoryNotice = "";
        ShowQueueList(queuePager.ApplyOlder(page.Items, page.HistoryCursor), appended: true);
        Say(MessageKind.Success, queuePager.HasMore ? "Histórico da fila ampliado." : "Todo o histórico da fila está na lista.");
    });

    /// <summary>Igual à fila: acrescenta as vagas encerradas mais antigas às já carregadas.</summary>
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

    public Task CreateUnitLightningSlotAsync() => RunAsync(async () =>
    {
        var unit = ActiveObsUnit ?? throw new NoraApiException("Escolha uma UBS antes de criar a vaga.");
        var appointment = SelectedAppointment ?? throw new NoraApiException("Selecione uma consulta cancelada.");
        if (appointment.Status != "cancelled") throw new NoraApiException("Somente consultas canceladas podem gerar uma vaga.");
        if (appointment.StartsAt <= DateTimeOffset.Now) throw new NoraApiException("A consulta cancelada já começou; só horários futuros podem virar Vaga Relâmpago.");
        await api.CreateUnitLightningSlotAsync(unit.Id, appointment.Id);
        await ReloadAfterChangeAsync(unit, UnitScope.Slots | UnitScope.Queue, "Vaga criada. Consulte a situação para acompanhar a oferta.");
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

    private void ClearUnitLightningData()
    {
        UnitWaitingList.Clear(); UnitLightningSlots.Clear(); unitQueueLoaded = unitSlotsLoaded = false;
        queuePager.Clear(); slotsPager.Clear(); queueHistoryNotice = slotsHistoryNotice = "";
        WaitingPatient = null; SelectedWaitingEntry = null; SelectedUnitSlot = null; WaitingSpecialty = "";
        queueSearch = queueSpecialtyFilter = ""; Notify(nameof(QueueSearch)); Notify(nameof(QueueSpecialtyFilter)); RebuildQueueSpecialtyOptions();
        AwaitingSlots.Clear(); SelectedAwaitingSlot = null; SlotSteps.Clear();
        NotifyUnitCounts();
        foreach (var name in new[] { nameof(CanLoadMoreQueueHistory), nameof(CanLoadMoreSlotsHistory), nameof(QueueHistoryState), nameof(SlotsHistoryState) }) Notify(name);
    }
}
