using System.Windows.Input;
using Nora.Desktop.Generics;
using Nora.Desktop.Models;
using Nora.Desktop.Services;

namespace Nora.Desktop.ViewModels;

public sealed partial class WorkspaceViewModel
{
    private object? actionSelection;
    private LightningSlot? selectedSlot;
    private string patientStatusFilter = "Todos";

    public object? ActionSelection => actionSelection;
    public PatientAccount? ActionSelectedPatient => actionSelection as PatientAccount;
    public bool HasPatientActionSelection => actionSelection is PatientAccount && ShowPatients;
    public bool HasQueueActionSelection => actionSelection is QueueEntry && ShowQueue;
    public bool HasSlotActionSelection => actionSelection is LightningSlot && ShowSlots;
    public string ActionSelectionLabel => actionSelection switch
    {
        PatientAccount patient => patient.FullName,
        QueueEntry entry => entry.PatientName,
        LightningSlot slot => slot.Label,
        _ => ""
    };
    public bool CanSetPatientActive => CanOperate && IsOperational && ActionSelectedPatient != null && user?.Role is "administrator" or "attendant";
    public string PatientActiveActionLabel => ActionSelectedPatient?.IsActive == false ? "Reativar paciente" : "Desativar paciente";
    public ICommand ClearSelectionCommand { get; }
    public LightningSlot? SelectedSlot { get => selectedSlot; set { selectedSlot = value; Notify(); } }
    public string PatientStatusFilter { get => patientStatusFilter; set { patientStatusFilter = value; Notify(); NotifyPatientFilters(); } }

    public void SelectListItem(object? item)
    {
        if (!IsAuthenticated || !IsOperational) item = null;
        actionSelection = item;
        NotifyActionSelection();
    }

    private void NotifyActionSelection()
    {
        foreach (var name in new[] { nameof(ActionSelection), nameof(ActionSelectedPatient), nameof(HasPatientActionSelection), nameof(HasQueueActionSelection), nameof(HasSlotActionSelection), nameof(ActionSelectionLabel), nameof(CanSetPatientActive), nameof(PatientActiveActionLabel) }) Notify(name);
        CommandManager.InvalidateRequerySuggested();
    }

    private void ClearActionSelection()
    {
        var selected = actionSelection;
        actionSelection = null;
        if (selected is PatientAccount) SelectedPatient = null;
        else if (selected is QueueEntry) SelectedQueue = null;
        else if (selected is LightningSlot) SelectedSlot = null;
        NotifyActionSelection();
    }

    private object? CurrentActionItem(object? previous) => previous switch
    {
        PatientAccount patient => FilteredPatients.FirstOrDefault(item => item.Id == patient.Id),
        QueueEntry entry => Queue.FirstOrDefault(item => item.Id == entry.Id),
        LightningSlot slot => Slots.FirstOrDefault(item => item.Id == slot.Id),
        _ => null
    };

    public Task SetPatientActiveAsync(PatientAccount patient) => RunAsync(async () =>
    {
        if (!IsOperational || !IsAuthenticated || user?.Role is not ("administrator" or "attendant"))
            throw new NoraApiException("Esta conta não pode alterar a situação do paciente.");
        var active = !patient.IsActive;
        await api.SetPatientActiveAsync(patient.Id, active);
        await RefreshAfterChangeAsync(active ? "Paciente reativado. Ele pode entrar novamente no APPNORA; a fila não é restaurada." : "Paciente desativado. Acesso encerrado e pendências da fila/ofertas canceladas.");
    });

    public ItemDetails? BuildDetails(object item)
    {
        if (IsMunicipal && item is not OperationalRow) return null;
        if (!IsAuthenticated && item is PatientAccount or QueueEntry or LightningSlot) return null;
        if (item is UnitPatientRecord patientGuard && (!IsAuthenticated || !HasActiveObsUnit || !ObsRegistry.Contains(patientGuard))) return null;
        if (item is UnitWaitingEntry entryGuard && (!IsAuthenticated || !HasActiveObsUnit || !UnitWaitingList.Contains(entryGuard))) return null;
        if (item is UnitLightningSlot slotGuard && (!IsAuthenticated || !HasActiveObsUnit || !UnitLightningSlots.Contains(slotGuard))) return null;
        return item switch
        {
            UnitPatientRecord unitPatient => BuildUnitPatientDetails(unitPatient),
            UnitWaitingEntry unitEntry => new(unitEntry.PatientName, "Entrada na fila da UBS", [new("Paciente", unitEntry.PatientName, true), new("CPF", unitEntry.Cpf, true), new("Especialidade", unitEntry.Specialty), new("Entrada", unitEntry.EnteredAtDisplay), new("Situação", unitEntry.StatusDisplay)]),
            UnitLightningSlot unitSlot => new(unitSlot.Specialty, "Vaga Relâmpago da UBS", [new("Especialidade", unitSlot.Specialty), new("Profissional", unitSlot.Professional), new("Horário", unitSlot.TimeDisplay), new("Situação", unitSlot.StatusDisplay)]),
            PatientAccount patient => BuildPatientDetails(patient),
            QueueEntry entry => BuildQueueDetails(entry),
            LightningSlot slot => new(slot.Label, "Vaga Relâmpago", [
                new("Descrição", slot.Label), new("Situação", slot.StatusDisplay),
                new("Criada em", slot.CreatedAt.ToLocalTime().ToString("dd/MM/yyyy 'às' HH:mm"))]),
            OperationalRow row => new(row.Description, "Atendimento", [
                new("Referência", row.Reference), new("Descrição", row.Description), new("Situação", row.Status)]),
            _ => null
        };
    }

    private static ItemDetails BuildUnitPatientDetails(UnitPatientRecord patient)
    {
        var fields = new List<DetailField> { new("Nome completo", patient.FullName, true, "Identificação"), new("CPF", patient.FormattedCpf, true, "Contato"),
            new("E-mail", patient.Email, true, "Contato"), new("Telefone", patient.PhoneDisplay, !string.IsNullOrWhiteSpace(patient.Phone), "Contato"), new("Situação na UBS", patient.ActiveDisplay, Group: "Situação"), new("Presença no aplicativo", patient.PresenceDisplay, Group: "Situação"), new("Cadastrado em", patient.CreatedAtDisplay, Group: "Cadastro") };
        if (patient.Address is { } address) fields.AddRange([new("CEP", address.Cep, Group: "Endereço"), new("Endereço", address.Display, Group: "Endereço"), new("Complemento", address.Complement ?? "Não informado", Group: "Endereço")]);
        return new(patient.FullName, "Paciente da UBS", fields);
    }

    private ItemDetails BuildQueueDetails(QueueEntry entry)
    {
        var fields = new List<DetailField> { new("Nome completo", entry.PatientName, true, "Identificação") };
        var patient = Patients.FirstOrDefault(item => item.Id == entry.PatientId);
        if (patient != null)
            fields.AddRange([
                new("E-mail", patient.Email, true, "Contato"), new("CPF", patient.FormattedCpf, true, "Contato"),
                new("Telefone", patient.PhoneDisplay, !string.IsNullOrWhiteSpace(patient.Phone), "Contato"),
                new("Presença no APPNORA", patient.PresenceDisplay, Group: "Situação")]);
        fields.AddRange([new("Entrada", entry.EnteredAtDisplay, Group: "Fila"), new("Situação", entry.StatusDisplay, Group: "Fila"), new("Número da entrada", entry.Id.ToString(), Group: "Fila")]);
        return new(entry.PatientName, "Entrada na fila de Vaga Relâmpago", fields);
    }

    private static ItemDetails BuildPatientDetails(PatientAccount patient)
    {
        var fields = new List<DetailField> {
            new("Nome completo", patient.FullName, true, "Identificação"),
            new("E-mail", patient.Email, true, "Contato"),
            new("CPF", patient.FormattedCpf, true, "Contato"),
            new("Telefone", patient.PhoneDisplay, !string.IsNullOrWhiteSpace(patient.Phone), "Contato"),
            new("Cadastro", patient.ActiveDisplay, Group: "Situação"),
            new("Presença no APPNORA", patient.PresenceDisplay, Group: "Situação"),
            new("Cadastrado em", patient.CreatedAt?.ToLocalTime().ToString("dd/MM/yyyy 'às' HH:mm") ?? "Data não informada", Group: "Cadastro") };
        if (patient.Address is { } address)
            fields.AddRange([new("CEP", address.Cep, Group: "Endereço"), new("Endereço", address.Display, Group: "Endereço"), new("Complemento", address.Complement ?? "Não informado", Group: "Endereço")]);
        return new(patient.FullName, "Paciente", fields);
    }
}
