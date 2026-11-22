using System.Collections.ObjectModel;
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
    private string queuePatientFilter = "", queueSpecialtyFilter = "", queueSort = "Mais antigos";
    public ObservableCollection<UnitWaitingEntry> UnitWaitingList { get; } = [];
    public ObservableCollection<UnitLightningSlot> UnitLightningSlots { get; } = [];
    private bool unitQueueLoaded, unitSlotsLoaded;
    public UnitPatientRecord? WaitingPatient { get => waitingPatient; set { waitingPatient = value; Notify(); } }
    public UnitWaitingEntry? SelectedWaitingEntry { get => selectedWaitingEntry; set { selectedWaitingEntry = value; Notify(); Notify(nameof(CanCancelWaitingEntry)); } }
    public UnitLightningSlot? SelectedUnitSlot { get => selectedUnitSlot; set { selectedUnitSlot = value; Notify(); } }
    public string WaitingSpecialty { get => waitingSpecialty; set { waitingSpecialty = value; Notify(); } }
    public static string[] QueueSortOptions { get; } = ["Mais antigos", "Mais recentes", "Paciente A–Z", "Paciente Z–A"];
    public string QueuePatientFilter { get => queuePatientFilter; set { queuePatientFilter = value ?? ""; Notify(); NotifyQueueFilter(); } }
    public string QueueSpecialtyFilter { get => queueSpecialtyFilter; set { queueSpecialtyFilter = value ?? ""; Notify(); NotifyQueueFilter(); } }
    public string QueueSort { get => queueSort; set { queueSort = value ?? "Mais antigos"; Notify(); NotifyQueueFilter(); } }
    public IEnumerable<UnitWaitingEntry> FilteredUnitWaitingList
    {
        get
        {
            var filtered = UnitWaitingList.Where(entry => entry.PatientName.Contains(QueuePatientFilter.Trim(), StringComparison.CurrentCultureIgnoreCase) && entry.Specialty.Contains(QueueSpecialtyFilter.Trim(), StringComparison.CurrentCultureIgnoreCase));
            return QueueSort switch
            {
                "Paciente A–Z" => filtered.OrderBy(entry => entry.PatientName, StringComparer.CurrentCultureIgnoreCase).ThenBy(entry => entry.EnteredAt),
                "Paciente Z–A" => filtered.OrderByDescending(entry => entry.PatientName, StringComparer.CurrentCultureIgnoreCase).ThenBy(entry => entry.EnteredAt),
                "Mais recentes" => filtered.OrderByDescending(entry => entry.EnteredAt).ThenByDescending(entry => entry.Id),
                _ => filtered.OrderBy(entry => entry.EnteredAt).ThenBy(entry => entry.Id)
            };
        }
    }
    public bool HasNoFilteredWaitingEntries => unitQueueLoaded && UnitWaitingList.Count > 0 && !FilteredUnitWaitingList.Any();
    private void NotifyQueueFilter() { Notify(nameof(FilteredUnitWaitingList)); Notify(nameof(HasNoFilteredWaitingEntries)); }
    /// <summary>Cancelar só vale para entrada ainda ativa (aguardando ou com oferta em andamento).</summary>
    public bool CanCancelWaitingEntry => CanOperate && SelectedWaitingEntry?.IsActive == true;
    public bool HasNoWaitingEntries => HasActiveObsUnit && unitQueueLoaded && UnitWaitingList.Count == 0;
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
        UnitWaitingList.Clear(); UnitLightningSlots.Clear(); unitQueueLoaded = unitSlotsLoaded = false; NotifyQueueFilter();
        WaitingPatient = null; SelectedWaitingEntry = null; SelectedUnitSlot = null; WaitingSpecialty = "";
        AwaitingSlots.Clear(); SelectedAwaitingSlot = null; SlotSteps.Clear();
        NotifyUnitCounts();
    }
}
