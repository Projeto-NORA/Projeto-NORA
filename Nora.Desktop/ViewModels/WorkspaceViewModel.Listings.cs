using Nora.Desktop.Models;
using Nora.Desktop.Services;

namespace Nora.Desktop.ViewModels;

/// <summary>Fichas de detalhe dos itens das listagens da UBS ativa (duplo clique e Enter).</summary>
public sealed partial class WorkspaceViewModel
{
    /// <summary>
    /// Ficha de um item. A da Vaga Relâmpago lê antes as ofertas da vaga (uma requisição) e as mostra em ordem cronológica;
    /// as demais fichas usam só os dados já carregados. Devolve nulo se a sessão ou a UBS mudou durante a leitura.
    /// </summary>
    public async Task<ItemDetails?> BuildDetailsAsync(object item)
    {
        if (item is not UnitLightningSlot slot || BuildDetails(slot) is not { } details || ActiveObsUnit is not { } unit) return BuildDetails(item);
        UnitSlotOffer[]? offers = null;
        await RunAsync(async () => offers = await api.UnitSlotOffersAsync(unit.Id, slot.Id));
        if (disposed || !IsAuthenticated || ActiveObsUnit?.Id != unit.Id) return null;
        return details with { Fields = [.. details.Fields, .. OfferFields(offers)] };
    }

    private const string OffersGroup = "Ofertas da vaga";
    private static IEnumerable<DetailField> OfferFields(UnitSlotOffer[]? offers)
    {
        if (offers is null) return [new("Ofertas", "Não foi possível carregar as ofertas agora. Feche a ficha e abra de novo.", Group: OffersGroup)];
        if (offers.Length == 0) return [new("Ofertas", "Nenhuma oferta foi feita para esta vaga até agora.", Group: OffersGroup)];
        return offers.Select((offer, index) => new DetailField($"{index + 1}ª oferta", $"{offer.PatientName} · {offer.StatusDisplay} · prazo {offer.ExpiresAtDisplay}", Group: OffersGroup));
    }

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
        fields.AddRange(AttendanceFields(patient.Attendance));
        return new(patient.FullName, "Paciente da UBS", fields);
    }

    private const string AttendanceGroup = "Participação nos últimos 12 meses";
    private const string AttendanceRule = "Concluídas ÷ (concluídas + faltas + cancelamentos feitos pelo paciente, ponderados pela antecedência). Consultas ainda agendadas ou chamadas e cancelamentos feitos pela unidade não entram.";
    private static IEnumerable<DetailField> AttendanceFields(UnitAttendance? attendance)
    {
        if (attendance is null) return [new("Participação", "—", Group: AttendanceGroup), new("Como é calculada", "A API não enviou a participação deste cadastro. Atualize a lista da UBS.", Group: AttendanceGroup)];
        return
        [
            new("Participação", attendance.Score is null ? "— (nenhuma consulta encerrada nos últimos 12 meses)" : attendance.ScoreDisplay, Group: AttendanceGroup),
            new("Concluídas", DisplayFormat.Count(attendance.Completed), Group: AttendanceGroup),
            new("Faltas", DisplayFormat.Count(attendance.Missed), Group: AttendanceGroup),
            new("Cancelamentos", attendance.CancelledDisplay, Group: AttendanceGroup),
            new("Como é calculada", AttendanceRule, Group: AttendanceGroup)
        ];
    }
}
