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

    /// <summary>
    /// Aba "Exames e vacinas" da ficha de um paciente da UBS ativa. Nulo fora de uma UBS ativa ou para um item que não está mais na lista. A ficha é uma janela própria: o retorno
    /// das ações aparece nela, e uma sessão encerrada ou troca obrigatória de senha continua sendo tratada aqui.
    /// </summary>
    public HealthRecordsViewModel? CreateHealthRecords(UnitPatientRecord patient)
    {
        if (IsMunicipal || !IsAuthenticated || ActiveObsUnit is not { } unit || !ObsRegistry.Contains(patient)) return null;
        return new HealthRecordsViewModel(api, unit.Id, patient.Cpf, patient.FullName, patient.IsActive, HandleHealthRecordsError);
    }

    private bool HandleHealthRecordsError(NoraApiException error)
    {
        if (disposed || !IsAuthenticated) return false;
        if (error.Code == "password-change-required") { EnterPasswordChange(); return true; }
        if (error.Unauthorized) { SignOutRejected(error.Message); return true; }
        return false;
    }

    public ItemDetails? BuildDetails(object item)
    {
        if (IsMunicipal || !IsAuthenticated || !HasActiveObsUnit) return null;
        return item switch
        {
            UnitPatientRecord patient when ObsRegistry.Contains(patient) => BuildUnitPatientDetails(patient),
            AppointmentInfo appointment when Appointments.Contains(appointment) => BuildAppointmentDetails(appointment),
            UnitLightningSlot slot when UnitLightningSlots.Contains(slot) => new(slot.Specialty, "Vaga Relâmpago da UBS", BuildSlotFields(slot)),
            FamilyLink link when FamilyPending.Contains(link) || FamilyApproved.Contains(link) => BuildFamilyDetails(link),
            _ => null
        };
    }

    /// <summary>Ficha de um pedido ou vínculo de família: o que a equipe confere nos documentos (nome e CPF de cada um, com cópia) e quando e por quem foi decidido.</summary>
    private static ItemDetails BuildFamilyDetails(FamilyLink link)
    {
        var approved = link.Status == "approved";
        var fields = new List<DetailField>
        {
            new("Responsável", link.GuardianName, true, "Quem pediu"), new("CPF do responsável", link.GuardianCpfDisplay, true, "Quem pediu"),
            new("Dependente", link.DependentDisplay, !string.IsNullOrWhiteSpace(link.DependentName), "Para quem"), new("CPF do dependente", link.DependentCpfDisplay, true, "Para quem"),
            new("Pedido em", link.RequestedAtDisplay, Group: "Pedido")
        };
        if (approved) fields.Add(new("Aprovado em", link.DecidedDisplay, Group: "Pedido"));
        return new($"{link.GuardianName} → {link.DependentDisplay}", approved ? "Vínculo de família aprovado" : "Pedido de vínculo de família", fields);
    }

    private static IReadOnlyList<DetailField> BuildSlotFields(UnitLightningSlot slot)
    {
        var fields = new List<DetailField>
        {
            new("Especialidade", slot.Specialty), new("Profissional", slot.Professional), new("Horário", slot.TimeDisplay),
            new("Situação", slot.StatusDisplay), new("Origem", slot.OriginDisplay), new("Criada em", slot.CreatedAtDisplay)
        };
        if (slot.Source is not null) fields.Add(new("Consulta de origem", slot.SourceDisplay));
        return fields;
    }

    private static ItemDetails BuildAppointmentDetails(AppointmentInfo appointment) => new(appointment.PatientName, "Consulta da UBS", [
        new("Paciente", appointment.PatientName, true), new("CPF", appointment.FormattedCpf, true), new("Especialidade", appointment.Specialty), new("Profissional", appointment.Professional),
        new("Horário", appointment.TimeDisplay), new("Situação", appointment.StatusDisplay), new("Marcada por", appointment.BookedByDisplay),
        new("Confirmação de presença", appointment.HasConfirmationInfo ? appointment.ConfirmationDisplay : "Sem informação: a confirmação só vale para consulta agendada, de 48 h até 2 h antes.")]);

    private static ItemDetails BuildUnitPatientDetails(UnitPatientRecord patient)
    {
        var fields = new List<DetailField> { new("Nome completo", patient.FullName, true, "Identificação"), new("CPF", patient.FormattedCpf, true, "Identificação"),
            new("E-mail", patient.Email, true, "Contato"), new("Telefone", patient.PhoneDisplay, !string.IsNullOrWhiteSpace(patient.Phone), "Contato"), new("Situação na UBS", patient.ActiveDisplay, Group: "Situação"), new("Presença no aplicativo", patient.PresenceDisplay, Group: "Situação"), new("Cadastrado em", patient.CreatedAtDisplay, Group: "Cadastro") };
        if (patient.Address is { } address) fields.AddRange([new("CEP", address.Cep, Group: "Endereço"), new("Endereço", address.Display, Group: "Endereço"), new("Complemento", address.Complement ?? "Não informado", Group: "Endereço")]);
        fields.AddRange(AttendanceFields(patient.Attendance));
        return new(patient.FullName, "Paciente da UBS", fields);
    }

    private const string AttendanceGroup = "Score nos últimos 12 meses";
    private const string AttendanceRule = "Pontos de 0 a 1000 = comparecimento × 1000, em que comparecimento = concluídas ÷ (concluídas + faltas + cancelamentos feitos pelo paciente, ponderados pela antecedência). Consultas ainda agendadas ou chamadas e cancelamentos feitos pela unidade não entram. Quem ainda não teve consulta encerrada começa em 1000. Faixas: Baixo 0–299, Regular 300–599, Bom 600–799, Excelente 800–1000.";
    /// <summary>Efeito do score na Vaga Relâmpago, repetido nas telas do OBS que mostram os pontos.</summary>
    public const string ScorePriorityText = "Quem tem score maior recebe antes as ofertas de Vaga Relâmpago. O score não muda a prioridade clínica nem o direito ao atendimento.";
    private static IEnumerable<DetailField> AttendanceFields(UnitAttendance? attendance)
    {
        if (attendance is null) return [new("Score", "—", Group: AttendanceGroup), new("Como é calculado", "A API não enviou o score deste cadastro. A lista da UBS se atualiza sozinha.", Group: AttendanceGroup)];
        return
        [
            new("Score", attendance.ScoreDisplay, Group: AttendanceGroup),
            new("Faixa", attendance.BandDisplay, Group: AttendanceGroup),
            new("Concluídas", DisplayFormat.Count(attendance.Completed), Group: AttendanceGroup),
            new("Faltas", DisplayFormat.Count(attendance.Missed), Group: AttendanceGroup),
            new("Cancelamentos", attendance.CancelledDisplay, Group: AttendanceGroup),
            new("Como é calculado", AttendanceRule, Group: AttendanceGroup),
            new("Efeito na Vaga Relâmpago", ScorePriorityText, Group: AttendanceGroup)
        ];
    }
}
