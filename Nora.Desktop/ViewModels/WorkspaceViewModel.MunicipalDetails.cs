using Nora.Desktop.Models;
using Nora.Desktop.Services;

namespace Nora.Desktop.ViewModels;

/// <summary>
/// Fichas em linhas (duplo clique, segundo clique e Enter) das listagens da Secretaria: UBS, cadastros de referência, agenda da UBS, especialidades, comunicados,
/// contas e histórico. A ficha do comunicado lê, sob demanda, quem da equipe leu (uma requisição) e mostra cada leitor como uma linha.
/// </summary>
public sealed partial class WorkspaceViewModel
{
    private const string ReadersGroup = "Quem da equipe leu";

    public async Task<ItemDetails?> BuildMunicipalDetailsAsync(object item)
    {
        if (!IsMunicipal || !IsAuthenticated) return null;
        if (item is not AnnouncementSummary announcement) return BuildMunicipalDetails(item);
        // As leituras só são lidas sob demanda e só para comunicados com público de equipe.
        SelectedMyAnnouncement = MyAnnouncements.FirstOrDefault(entry => entry.Id == announcement.Id) ?? announcement;
        if (announcement.Audience != "patients") await LoadAnnouncementReadersAsync();
        if (disposed || !IsAuthenticated) return null;
        return BuildAnnouncementDetails(announcement);
    }

    public ItemDetails? BuildMunicipalDetails(object item) => !IsMunicipal || !IsAuthenticated ? null : item switch
    {
        HealthUnit unit => BuildUnitDetails(unit),
        UnitPatientRecord patient => BuildReferencePatientDetails(patient),
        AppointmentInfo appointment => BuildReferenceAppointmentDetails(appointment),
        Specialty specialty => new(specialty.Name, "Especialidade da rede", [new("Nome", specialty.Name, true, "Especialidade"), new("Situação", specialty.StatusDisplay, Group: "Especialidade")]),
        StaffAccount account => BuildStaffDetails(account),
        HistoryEvent entry => new(entry.ActionDisplay, "Ação registrada", [
            new("Ação", entry.ActionDisplay, Group: "Ação"), new("Data e hora", entry.OccurredAtDisplay, Group: "Ação"),
            new("Quem fez", entry.ActorDisplay, Group: "Envolvidos"), new("Conta", entry.AccountDisplay, Group: "Envolvidos"), new("UBS", entry.UnitDisplay, Group: "Envolvidos")]),
        AnnouncementSummary announcement => BuildAnnouncementDetails(announcement),
        _ => null
    };

    private static ItemDetails BuildUnitDetails(HealthUnit unit) => new(unit.Name, "UBS da rede", [
        new("Nome", unit.Name, true, "Identificação"), new("Região", unit.Region, Group: "Identificação"), new("Endereço", unit.Address, true, "Identificação"),
        new("Base de dados", unit.DatabaseLabel.Length > 0 ? unit.DatabaseLabel : "Não informada", Group: "Base"),
        new("Localização cadastrada", $"{unit.Latitude.ToString("0.#####", DisplayFormat.Culture)}, {unit.Longitude.ToString("0.#####", DisplayFormat.Culture)}", Group: "Localização")]);

    /// <summary>Cadastro nominal de referência: sem score nem dados de participação (a Secretaria só lê o cadastro).</summary>
    private static ItemDetails BuildReferencePatientDetails(UnitPatientRecord patient)
    {
        var fields = new List<DetailField>
        {
            new("Nome completo", patient.FullName, true, "Identificação"), new("CPF", patient.FormattedCpf, true, "Contato"), new("E-mail", patient.Email, true, "Contato"),
            new("Telefone", patient.PhoneDisplay, !string.IsNullOrWhiteSpace(patient.Phone), "Contato"), new("Situação na UBS", patient.ActiveDisplay, Group: "Situação"),
            new("Presença no aplicativo", patient.PresenceDisplay, Group: "Situação"), new("Cadastrado em", patient.CreatedAtDisplay, Group: "Cadastro")
        };
        if (patient.Address is { } address) fields.AddRange([new("CEP", address.Cep, Group: "Endereço"), new("Endereço", address.Display, Group: "Endereço"), new("Complemento", address.Complement ?? "Não informado", Group: "Endereço")]);
        return new(patient.FullName, "Cadastro de referência da UBS", fields);
    }

    private static ItemDetails BuildReferenceAppointmentDetails(AppointmentInfo appointment) => new(appointment.PatientName, "Consulta da UBS", [
        new("Paciente", appointment.PatientName, true, "Paciente"), new("CPF", appointment.FormattedCpf, true, "Paciente"),
        new("Especialidade", appointment.Specialty, Group: "Consulta"), new("Profissional", appointment.Professional, Group: "Consulta"), new("Horário", appointment.TimeDisplay, Group: "Consulta"),
        new("Situação", appointment.StatusDisplay, Group: "Consulta"), new("Marcada por", appointment.BookedByDisplay, Group: "Consulta")]);

    private static ItemDetails BuildStaffDetails(StaffAccount account) => new(account.FullName, "Conta de equipe (OBS)", [
        new("Nome completo", account.FullName, true, "Conta"), new("E-mail", account.EmailDisplay, account.Email is { Length: > 0 }, "Conta"), new("Perfil", account.RoleDisplay, Group: "Conta"),
        new("Situação", account.StatusDisplay, Group: "Situação"), new("Troca de senha pendente", account.PendingDisplay, Group: "Situação"),
        new("UBS com acesso", account.UnitsDisplay.Length > 0 ? account.UnitsDisplay : "Ainda não lido", Group: "Acesso")]);

    private ItemDetails BuildAnnouncementDetails(AnnouncementSummary item)
    {
        var fields = new List<DetailField>
        {
            new("Título", item.Title, true, "Comunicado"), new("Texto", item.Body, true, "Comunicado"),
            new("Público", item.AudienceDisplay, Group: "Alcance"), new("Alcance", item.ScopeDisplay, Group: "Alcance"),
            new("Situação", item.StatusDetail.Length > 0 ? $"{item.StatusDisplay} {item.StatusDetail}" : item.StatusDisplay, Group: "Publicação"),
            new("Publicado em", item.CreatedAtDisplay, Group: "Publicação"), new("Validade", item.ValidityDisplay, Group: "Publicação"),
            new("Equipes leram", item.StaffReadsDisplay, Group: "Leituras"), new("Pacientes leram", item.PatientReadsDisplay, Group: "Leituras"),
            new("Como ler a contagem", "A contagem de equipes é a de contas que marcaram o comunicado como lido; a de pacientes não mostra quem leu.", Group: "Leituras")
        };
        if (item.Audience == "patients") fields.Add(new("Equipes", "Este comunicado é só para pacientes: o aplicativo informa a contagem de leituras, sem nomes.", Group: ReadersGroup));
        else if (readers is not null && readersFor == item.Id)
        {
            if (!string.IsNullOrEmpty(AnnouncementReadersSummary)) fields.Add(new("Resumo", AnnouncementReadersSummary, Group: ReadersGroup));
            if (AnnouncementReaders.Count == 0) fields.Add(new("Equipes", AnnouncementReadersState, Group: ReadersGroup));
            fields.AddRange(AnnouncementReaders.Select(reader => new DetailField(reader.FullName, reader.ReadAtDisplay, Group: ReadersGroup)));
        }
        else fields.Add(new("Equipes", "Não foi possível carregar as leituras agora. Feche a ficha e abra de novo.", Group: ReadersGroup));
        return new(item.Title, "Comunicado da Secretaria", fields);
    }
}
