using Nora.Desktop.Models;

namespace Nora.Desktop.ViewModels;

/// <summary>Fichas de detalhe dos itens das listagens da UBS ativa (duplo clique e Enter).</summary>
public sealed partial class WorkspaceViewModel
{
    public ItemDetails? BuildDetails(object item)
    {
        if (IsMunicipal || !IsAuthenticated || !HasActiveObsUnit) return null;
        return item switch
        {
            UnitPatientRecord patient when ObsRegistry.Contains(patient) => BuildUnitPatientDetails(patient),
            UnitWaitingEntry entry when UnitWaitingList.Contains(entry) => BuildWaitingDetails(entry),
            UnitLightningSlot slot when UnitLightningSlots.Contains(slot) => new(slot.Specialty, "Vaga Relâmpago da UBS", [
                new("Especialidade", slot.Specialty), new("Profissional", slot.Professional), new("Horário", slot.TimeDisplay),
                new("Situação", slot.StatusDisplay), new("Criada em", slot.CreatedAtDisplay)]),
            _ => null
        };
    }

    private static ItemDetails BuildWaitingDetails(UnitWaitingEntry entry) => new(entry.PatientName, "Entrada na fila da UBS", [
        new("Paciente", entry.PatientName, true), new("CPF", entry.FormattedCpf, true), new("Especialidade", entry.Specialty),
        new("Posição na especialidade", entry.PositionDisplay), new("Tempo de espera", entry.WaitDisplay),
        new("Entrada", entry.EnteredAtDisplay), new("Situação", entry.StatusDisplay)]);

    private static ItemDetails BuildUnitPatientDetails(UnitPatientRecord patient)
    {
        var fields = new List<DetailField> { new("Nome completo", patient.FullName, true, "Identificação"), new("CPF", patient.FormattedCpf, true, "Contato"),
            new("E-mail", patient.Email, true, "Contato"), new("Telefone", patient.PhoneDisplay, !string.IsNullOrWhiteSpace(patient.Phone), "Contato"), new("Situação na UBS", patient.ActiveDisplay, Group: "Situação"), new("Presença no aplicativo", patient.PresenceDisplay, Group: "Situação"), new("Cadastrado em", patient.CreatedAtDisplay, Group: "Cadastro") };
        if (patient.Address is { } address) fields.AddRange([new("CEP", address.Cep, Group: "Endereço"), new("Endereço", address.Display, Group: "Endereço"), new("Complemento", address.Complement ?? "Não informado", Group: "Endereço")]);
        return new(patient.FullName, "Paciente da UBS", fields);
    }
}
