using System.Collections.ObjectModel;
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
    private const string HistoryRestartedNotice = "O histórico carregado foi reiniciado porque muitos itens foram encerrados desde a última leitura. Use Carregar mais histórico para ver os mais antigos.";
    // Histórico paginado (P4): a API devolve todos os itens ativos e as encerradas mais recentes; o resto vem por "Carregar mais histórico". O ciclo de 20 s relê só a primeira página e preserva o que já foi carregado (ver HistoryPager).
    private readonly HistoryPager<UnitWaitingEntry, long> queuePager = new(entry => entry.Id, entry => entry.IsActive);
    private readonly HistoryPager<UnitLightningSlot, Guid> slotsPager = new(slot => slot.Id, slot => slot.IsActive);
    private string queueHistoryNotice = "", slotsHistoryNotice = "";
    public UnitPatientRecord? WaitingPatient { get => waitingPatient; set { waitingPatient = value; Notify(); } }
    public UnitWaitingEntry? SelectedWaitingEntry { get => selectedWaitingEntry; set { selectedWaitingEntry = value; Notify(); Notify(nameof(CanCancelWaitingEntry)); } }
    public UnitLightningSlot? SelectedUnitSlot { get => selectedUnitSlot; set { selectedUnitSlot = value; Notify(); } }
    public string WaitingSpecialty { get => waitingSpecialty; set { waitingSpecialty = value; Notify(); } }
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
        await LoadUnitScopeAsync(unit, UnitScope.Queue);
        if (ActiveObsUnit?.Id == unit.Id) Say(MessageKind.Success, "Fila da UBS atualizada.");
    });

    public Task AddUnitWaitingEntryAsync() => RunAsync(async () =>
    {
        var unit = ActiveObsUnit ?? throw new NoraApiException("Escolha uma UBS no Painel da UBS antes de incluir na fila.");
        var patient = WaitingPatient ?? throw new NoraApiException("Selecione o paciente cadastrado na UBS.");
        if (!patient.IsActive) throw new NoraApiException("O paciente precisa ter um cadastro ativo nesta UBS.");
        var specialty = WaitingSpecialty.Trim();
        if (specialty.Length == 0) throw new NoraApiException("Informe a especialidade.");
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
        ShowQueueList(queuePager.ApplyOlder(page.Items, page.HistoryCursor));
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
        ShowSlotList(slotsPager.ApplyOlder(page.Items, page.HistoryCursor));
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
        AwaitingSlots.Clear(); SelectedAwaitingSlot = null; SlotSteps.Clear();
        NotifyUnitCounts();
        foreach (var name in new[] { nameof(CanLoadMoreQueueHistory), nameof(CanLoadMoreSlotsHistory), nameof(QueueHistoryState), nameof(SlotsHistoryState) }) Notify(name);
    }
}
