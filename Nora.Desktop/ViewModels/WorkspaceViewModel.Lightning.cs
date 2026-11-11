using System.Collections.ObjectModel;
using Nora.Desktop.Models;
using Nora.Desktop.Services;

namespace Nora.Desktop.ViewModels;

public sealed partial class WorkspaceViewModel
{
    private UnitPatientRecord? waitingPatient;
    private UnitWaitingEntry? selectedWaitingEntry;
    private string waitingSpecialty = "";
    public ObservableCollection<UnitWaitingEntry> UnitWaitingList { get; } = [];
    public ObservableCollection<UnitLightningSlot> UnitLightningSlots { get; } = [];
    private bool unitQueueLoaded, unitSlotsLoaded;
    public UnitPatientRecord? WaitingPatient { get => waitingPatient; set { waitingPatient = value; Notify(); } }
    public UnitWaitingEntry? SelectedWaitingEntry { get => selectedWaitingEntry; set { selectedWaitingEntry = value; Notify(); } }
    public string WaitingSpecialty { get => waitingSpecialty; set { waitingSpecialty = value; Notify(); } }

    public Task LoadUnitWaitingListAsync() => RunAsync(async () =>
    {
        var unit = ActiveObsUnit ?? throw new NoraApiException("Confirme uma UBS em Pacientes antes de consultar a fila.");
        var entries = await api.UnitWaitingListAsync(unit.Id);
        if (disposed || !IsAuthenticated || ActiveObsUnit?.Id != unit.Id) return;
        SelectedWaitingEntry = null; Replace(UnitWaitingList, entries); unitQueueLoaded = true; Notify(nameof(QueueWaitingCount)); Message = "Fila da UBS atualizada.";
    });
    public Task AddUnitWaitingEntryAsync() => RunAsync(async () =>
    {
        var unit = ActiveObsUnit ?? throw new NoraApiException("Confirme uma UBS em Pacientes antes de incluir na fila.");
        var patient = WaitingPatient ?? throw new NoraApiException("Selecione o paciente cadastrado na UBS.");
        if (string.IsNullOrWhiteSpace(WaitingSpecialty)) throw new NoraApiException("Informe a especialidade.");
        await api.AddUnitWaitingEntryAsync(unit.Id, patient.Cpf, WaitingSpecialty.Trim()); WaitingPatient = null;
        try
        {
            var entries = await api.UnitWaitingListAsync(unit.Id);
            if (disposed || !IsAuthenticated || ActiveObsUnit?.Id != unit.Id) return;
            Replace(UnitWaitingList, entries); unitQueueLoaded = true; Notify(nameof(QueueWaitingCount)); Message = "Paciente incluído na fila da UBS.";
        }
        catch (NoraApiException error) when (!error.Unauthorized) { Message = "Paciente incluído. Atualize a fila para conferir."; }
    });
    public Task CancelUnitWaitingEntryAsync() => RunAsync(async () =>
    {
        var unit = ActiveObsUnit ?? throw new NoraApiException("Confirme uma UBS antes de cancelar a entrada.");
        var entry = SelectedWaitingEntry ?? throw new NoraApiException("Selecione uma entrada da fila.");
        await api.CancelUnitWaitingEntryAsync(unit.Id, entry.Id);
        try
        {
            var entries = await api.UnitWaitingListAsync(unit.Id);
            if (disposed || !IsAuthenticated || ActiveObsUnit?.Id != unit.Id) return;
            SelectedWaitingEntry = null; Replace(UnitWaitingList, entries); unitQueueLoaded = true; Notify(nameof(QueueWaitingCount)); Message = "Entrada cancelada.";
        }
        catch (NoraApiException error) when (!error.Unauthorized) { Message = "Entrada cancelada. Atualize a fila para conferir."; }
    });
    public Task LoadUnitLightningSlotsAsync() => RunAsync(async () =>
    {
        var unit = ActiveObsUnit ?? throw new NoraApiException("Confirme uma UBS em Pacientes antes de consultar vagas.");
        var slots = await api.UnitLightningSlotsAsync(unit.Id);
        if (disposed || !IsAuthenticated || ActiveObsUnit?.Id != unit.Id) return;
        Replace(UnitLightningSlots, slots); unitSlotsLoaded = true; Notify(nameof(SlotOpenCount)); Message = "Vagas da UBS atualizadas.";
    });
    public Task CreateUnitLightningSlotAsync() => RunAsync(async () =>
    {
        var unit = ActiveObsUnit ?? throw new NoraApiException("Confirme uma UBS antes de criar a vaga.");
        var appointment = SelectedAppointment ?? throw new NoraApiException("Selecione uma consulta cancelada.");
        if (appointment.Status != "cancelled") throw new NoraApiException("Somente consultas canceladas podem gerar uma vaga.");
        await api.CreateUnitLightningSlotAsync(unit.Id, appointment.Id);
        try
        {
            var slots = await api.UnitLightningSlotsAsync(unit.Id);
            if (disposed || !IsAuthenticated || ActiveObsUnit?.Id != unit.Id) return;
            Replace(UnitLightningSlots, slots); unitSlotsLoaded = true; Notify(nameof(SlotOpenCount)); Message = "Vaga criada. Consulte a situação para acompanhar a oferta.";
        }
        catch (NoraApiException error) when (!error.Unauthorized) { Message = "Vaga criada. Atualize a lista de vagas para conferir."; }
    });
    private void ClearUnitLightningData()
    { UnitWaitingList.Clear(); UnitLightningSlots.Clear(); unitQueueLoaded = unitSlotsLoaded = false; WaitingPatient = null; SelectedWaitingEntry = null; WaitingSpecialty = ""; Notify(nameof(QueueWaitingCount)); Notify(nameof(SlotOpenCount)); }
}
