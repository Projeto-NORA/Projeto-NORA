using System.Collections.ObjectModel;
using Nora.Desktop.Models;
using Nora.Desktop.Services;

namespace Nora.Desktop.ViewModels;

public sealed partial class WorkspaceViewModel
{
    private HealthUnit? candidateObsUnit, activeObsUnit;
    private UnitPatientRecord? selectedObsRegistry;
    private string correctedObsEmail = "";
    private string obsRegistryName = "", obsRegistryCpf = "", obsRegistryEmail = "", obsRegistryPhone = "";
    private string obsRegistrySearch = "";
    private string? editingObsCpf, obsRegistryIbge;
    private bool obsRegistryDraftOpen, obsUnitSelectionOpen = true, obsUnitsLoaded;
    private string obsRegistryStatusFilter = "Todos", obsRegistrySort = "Mais recentes";
    private string obsRegistryPresenceFilter = "Todos";
    private string obsRegistryCep = "", obsRegistryStreet = "", obsRegistryNumber = "", obsRegistryComplement = "", obsRegistryNeighborhood = "", obsRegistryCity = "", obsRegistryState = "";
    public ObservableCollection<HealthUnit> ObsUnits { get; } = [];
    public bool HasAvailableObsUnits => ObsUnits.Count > 0;
    public bool ObsUnitsLoaded => obsUnitsLoaded;
    public bool ObsUnitSelectionOpen { get => obsUnitSelectionOpen; set { obsUnitSelectionOpen = value; Notify(); } }
    /// <summary>Orientação do seletor: explica o que falta para ter uma UBS ativa (nenhuma concedida, várias a escolher, lista ainda não lida).</summary>
    public string ObsUnitPrompt => !IsAuthenticated || !IsOperational || HasActiveObsUnit ? ""
        : !obsUnitsLoaded ? "As UBS autorizadas ainda não foram carregadas. Use Atualizar lista de UBS."
        : ObsUnits.Count == 0 ? "Nenhuma UBS autorizada para esta conta. A Secretaria precisa conceder acesso a uma UBS (aba Usuários e histórico da gestão)."
        : ObsUnits.Count == 1 ? "Confirme a UBS para carregar os dados da operação."
        : $"Esta conta tem acesso a {ObsUnits.Count} UBS. Escolha a unidade e confirme para carregar os dados da operação.";
    public bool NeedsObsUnitChoice => IsAuthenticated && IsOperational && !HasActiveObsUnit;
    public bool CanConfirmObsUnit => CanOperate && CandidateObsUnit is not null;
    public void SuggestClosestObsUnit(double latitude, double longitude)
    {
        if (!IsAuthenticated || !double.IsFinite(latitude) || !double.IsFinite(longitude) || latitude is < -90 or > 90 || longitude is < -180 or > 180 || ObsUnits.Count == 0) return;
        static double Radians(double degrees) => degrees * Math.PI / 180;
        static double Distance(HealthUnit unit, double latitude, double longitude)
        {
            var dLat = Radians(unit.Latitude - latitude); var dLon = Radians(unit.Longitude - longitude);
            var a = Math.Pow(Math.Sin(dLat / 2), 2) + Math.Cos(Radians(latitude)) * Math.Cos(Radians(unit.Latitude)) * Math.Pow(Math.Sin(dLon / 2), 2);
            return Math.Asin(Math.Sqrt(Math.Clamp(a, 0, 1)));
        }
        CandidateObsUnit = ObsUnits.OrderBy(unit => Distance(unit, latitude, longitude)).ThenBy(unit => unit.Name, StringComparer.CurrentCultureIgnoreCase).First();
        Say(MessageKind.Info, "UBS autorizada mais próxima sugerida. Confira e confirme a escolha.");
    }
    public ObservableCollection<UnitPatientRecord> ObsRegistry { get; } = [];
    public string ObsRegistrySearch { get => obsRegistrySearch; set { obsRegistrySearch = value; Notify(); Notify(nameof(FilteredObsRegistry)); Notify(nameof(ActiveObsRegistry)); } }
    public string ObsRegistryStatusFilter { get => obsRegistryStatusFilter; set { obsRegistryStatusFilter = value; Notify(); Notify(nameof(FilteredObsRegistry)); Notify(nameof(ActiveObsRegistry)); } }
    public string ObsRegistryPresenceFilter { get => obsRegistryPresenceFilter; set { obsRegistryPresenceFilter = value; Notify(); Notify(nameof(FilteredObsRegistry)); } }
    public string ObsRegistrySort { get => obsRegistrySort; set { obsRegistrySort = value; Notify(); Notify(nameof(FilteredObsRegistry)); Notify(nameof(ActiveObsRegistry)); } }
    public IEnumerable<UnitPatientRecord> ActiveObsRegistry => ObsRegistry.Where(patient => patient.IsActive);
    public IEnumerable<UnitPatientRecord> FilteredObsRegistry => ObsRegistrySort switch
    {
        "Nome Z–A" => MatchingObsRegistry().OrderByDescending(patient => patient.FullName, StringComparer.CurrentCultureIgnoreCase),
        "Nome A–Z" => MatchingObsRegistry().OrderBy(patient => patient.FullName, StringComparer.CurrentCultureIgnoreCase),
        // Quem ainda não tem participação calculável vai para o fim nas duas direções.
        "Maior participação" => MatchingObsRegistry().OrderBy(patient => patient.Attendance?.Score is null).ThenByDescending(patient => patient.Attendance?.Score).ThenBy(patient => patient.FullName, StringComparer.CurrentCultureIgnoreCase),
        "Menor participação" => MatchingObsRegistry().OrderBy(patient => patient.Attendance?.Score is null).ThenBy(patient => patient.Attendance?.Score).ThenBy(patient => patient.FullName, StringComparer.CurrentCultureIgnoreCase),
        _ => MatchingObsRegistry().OrderByDescending(patient => patient.CreatedAt).ThenBy(patient => patient.Cpf, StringComparer.Ordinal)
    };
    private bool MatchesObsSearch(UnitPatientRecord patient)
    {
        var search = ObsRegistrySearch.Trim();
        if (patient.FullName.Contains(search, StringComparison.CurrentCultureIgnoreCase) || patient.Email.Contains(search, StringComparison.OrdinalIgnoreCase) ||
            patient.Cpf.Contains(search, StringComparison.OrdinalIgnoreCase) || (patient.Phone?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false)) return true;
        if (!search.Any(char.IsAsciiDigit) || search.Any(c => !char.IsAsciiDigit(c) && !char.IsWhiteSpace(c) && c is not ('.' or '-' or '(' or ')' or '+'))) return false;
        var digits = new string(search.Where(char.IsAsciiDigit).ToArray());
        return patient.Cpf.Contains(digits, StringComparison.Ordinal) || (patient.Phone?.Contains(digits, StringComparison.Ordinal) ?? false);
    }
    private IEnumerable<UnitPatientRecord> MatchingObsRegistry() => ObsRegistry.Where(patient =>
        (ObsRegistryStatusFilter == "Todos" || (ObsRegistryStatusFilter == "Ativos" && patient.IsActive) || (ObsRegistryStatusFilter == "Desativados" && !patient.IsActive)) &&
        (ObsRegistryPresenceFilter == "Todos" || (ObsRegistryPresenceFilter == "Online" && patient.IsOnline) || (ObsRegistryPresenceFilter == "Offline" && !patient.IsOnline)) &&
        MatchesObsSearch(patient));
    public UnitPatientRecord? SelectedObsRegistry { get => selectedObsRegistry; set { selectedObsRegistry = value; CorrectedObsEmail = value?.Email ?? ""; Notify(); Notify(nameof(ObsRegistryActiveAction)); Notify(nameof(HasSelectedObsRegistry)); } }
    public bool HasSelectedObsRegistry => SelectedObsRegistry is not null;
    public string ObsRegistryActiveAction => SelectedObsRegistry?.IsActive == false ? "Reativar nesta UBS" : "Desativar nesta UBS";
    public string CorrectedObsEmail { get => correctedObsEmail; set { correctedObsEmail = value; Notify(); Notify(nameof(CorrectedObsEmailError)); } }
    public HealthUnit? CandidateObsUnit { get => candidateObsUnit; set { candidateObsUnit = value; Notify(); Notify(nameof(CanConfirmObsUnit)); } }
    public HealthUnit? ActiveObsUnit
    {
        get => activeObsUnit;
        private set
        {
            activeObsUnit = value;
            Notify(); Notify(nameof(ActiveObsUnitLabel)); Notify(nameof(ActiveObsUnitRegion)); Notify(nameof(HasActiveObsUnit)); Notify(nameof(NeedsObsUnitChoice)); Notify(nameof(ObsUnitPrompt)); Notify(nameof(UnitLabel)); Notify(nameof(AgendaStateText));
            NotifyUnitMetrics(); Notify(nameof(UnitMetricsScopeLabel));
            NotifyUnitCounts();
        }
    }
    public string ActiveObsUnitLabel => ActiveObsUnit?.Name ?? "Nenhuma UBS confirmada";
    public string ActiveObsUnitRegion => ActiveObsUnit is { } unit && !string.IsNullOrWhiteSpace(unit.Region) ? unit.Region : "";
    public bool HasActiveObsUnit => ActiveObsUnit != null;
    public string ObsRegistryName { get => obsRegistryName; set { obsRegistryName = value; Notify(); } }
    public string ObsRegistryCpf { get => obsRegistryCpf; set { obsRegistryCpf = value; Notify(); } }
    public string ObsRegistryEmail { get => obsRegistryEmail; set { obsRegistryEmail = value; Notify(); Notify(nameof(ObsRegistryEmailError)); } }
    public string ObsRegistryPhone { get => obsRegistryPhone; set { obsRegistryPhone = value; Notify(); Notify(nameof(ObsRegistryPhoneError)); } }
    /// <summary>Mensagens por campo: aparecem conforme a pessoa digita e voltam a ser conferidas ao salvar.</summary>
    public string ObsRegistryEmailError => EmailError(ObsRegistryEmail);
    public string ObsRegistryPhoneError => PhoneError(ObsRegistryPhone);
    public string CorrectedObsEmailError => EmailError(CorrectedObsEmail);
    private static string EmailError(string value) => string.IsNullOrWhiteSpace(value) || BrazilianDataValidator.IsValidEmail(value) ? "" : "E-mail inválido. Use o formato nome@dominio.com.";
    private static string PhoneError(string value) => string.IsNullOrWhiteSpace(value) || BrazilianDataValidator.IsValidPhone(value) ? "" : "Telefone inválido. Informe de 8 a 15 dígitos, com + opcional no início.";
    public string ObsRegistryCep { get => obsRegistryCep; set { if (BrazilianDataValidator.NormalizeCep(obsRegistryCep) != BrazilianDataValidator.NormalizeCep(value)) obsRegistryIbge = null; obsRegistryCep = value; Notify(); } }
    public string ObsRegistryStreet { get => obsRegistryStreet; set { obsRegistryStreet = value; Notify(); } }
    public string ObsRegistryNumber { get => obsRegistryNumber; set { obsRegistryNumber = value; Notify(); } }
    public string ObsRegistryComplement { get => obsRegistryComplement; set { obsRegistryComplement = value; Notify(); } }
    public string ObsRegistryNeighborhood { get => obsRegistryNeighborhood; set { obsRegistryNeighborhood = value; Notify(); } }
    public string ObsRegistryCity { get => obsRegistryCity; set { obsRegistryCity = value; Notify(); } }
    public string ObsRegistryState { get => obsRegistryState; set { obsRegistryState = value; Notify(); } }
    public bool IsEditingObsRegistry => editingObsCpf is not null;
    public bool ObsRegistryDraftOpen { get => obsRegistryDraftOpen; set { obsRegistryDraftOpen = value; Notify(); } }
    public string ObsRegistryDraftTitle => IsEditingObsRegistry ? "Editar cadastro da UBS" : "Cadastrar referência na UBS";
    public string ObsRegistrySaveLabel => IsEditingObsRegistry ? "Salvar alterações" : "Salvar cadastro da UBS";
    public void EditSelectedObsRegistry()
    {
        if (ActiveObsUnit is null || SelectedObsRegistry is not { } patient) return;
        editingObsCpf = patient.Cpf; ObsRegistryName = patient.FullName; ObsRegistryCpf = patient.Cpf; ObsRegistryEmail = patient.Email; ObsRegistryPhone = patient.Phone ?? "";
        var address = patient.Address; ObsRegistryCep = address?.Cep ?? ""; obsRegistryIbge = address?.Ibge; ObsRegistryStreet = address?.Street ?? "";
        ObsRegistryNumber = address?.Number ?? ""; ObsRegistryComplement = address?.Complement ?? ""; ObsRegistryNeighborhood = address?.Neighborhood ?? "";
        ObsRegistryCity = address?.City ?? ""; ObsRegistryState = address?.State ?? ""; Notify(nameof(IsEditingObsRegistry)); Notify(nameof(ObsRegistryDraftTitle)); Notify(nameof(ObsRegistrySaveLabel)); ObsRegistryDraftOpen = true;
    }
    public void NewObsRegistryDraft() { ClearObsRegistryDraft(); ObsRegistryDraftOpen = true; }
    public Task LookupObsRegistryCepAsync() => RunAsync(async () =>
    {
        if (!BrazilianDataValidator.IsCompleteCep(ObsRegistryCep)) throw new NoraApiException("Informe um CEP com 8 dígitos para pesquisar o endereço.");
        var result = await api.LookupCepAsync(ObsRegistryCep);
        ObsRegistryCep = result.Cep; ObsRegistryStreet = result.Street; ObsRegistryNeighborhood = result.Neighborhood ?? "";
        ObsRegistryCity = result.City; ObsRegistryState = result.State; ObsRegistryComplement = result.Complement ?? "";
        obsRegistryIbge = result.Ibge;
        Say(MessageKind.Success, "Endereço preenchido. Confira e complete o número antes de salvar.");
    });

    private void SetObsUnits(HealthUnit[] units)
    {
        Replace(ObsUnits, units);
        obsUnitsLoaded = true;
        if (candidateObsUnit is { } candidate) CandidateObsUnit = ObsUnits.FirstOrDefault(unit => unit.Id == candidate.Id);
        Notify(nameof(HasAvailableObsUnits)); Notify(nameof(ObsUnitsLoaded)); Notify(nameof(ObsUnitPrompt));
    }

    /// <summary>Depois do login: carrega as UBS autorizadas. Uma única UBS é confirmada sozinha; várias exigem escolha explícita, sem restaurar escolha anterior.</summary>
    private async Task InitializeObsUnitsAsync()
    {
        var units = await api.ObsUnitsAsync();
        if (disposed || !IsAuthenticated) return;
        SetObsUnits(units);
        // Concessão revogada no meio da sessão: a UBS ativa sai e a pessoa precisa escolher de novo, sem troca automática.
        var revoked = ActiveObsUnit is { } active && units.All(unit => unit.Id != active.Id);
        if (revoked) DeactivateObsUnit();
        if (ActiveObsUnit is null && units.Length == 1)
        {
            CandidateObsUnit = units[0];
            if (!revoked) await ActivateUnitAsync(units[0]);
        }
        if (ActiveObsUnit is null) ObsUnitSelectionOpen = true;
    }

    public Task LoadObsUnitsAsync() => RunAsync(async () =>
    {
        if (!IsOperational || !IsAuthenticated) throw new NoraApiException("Entre no OBS para consultar suas UBS.");
        var hadActive = ActiveObsUnit?.Id;
        // A lista de UBS mudou (ou pode ter mudado): os comunicados são relidos por inteiro no próximo ciclo, porque o alcance depende das concessões da conta.
        announcementsFullDue = true;
        await InitializeObsUnitsAsync();
        if (disposed || !IsAuthenticated) return;
        if (hadActive is not null && ActiveObsUnit is null) Say(MessageKind.Error, "A UBS ativa deixou de estar autorizada para esta conta. Escolha outra UBS.");
        else Say(MessageKind.Success, ObsUnits.Count == 0 ? "Lista atualizada: esta conta não tem UBS autorizada." : "Lista de UBS atualizada.");
    });

    /// <summary>Lê tudo da nova UBS antes de trocar qualquer coisa: se a leitura falhar, a UBS ativa anterior e seus dados continuam intactos.</summary>
    private async Task ActivateUnitAsync(HealthUnit unit)
    {
        var snapshot = await FetchUnitDataAsync(unit, UnitScope.Everything | UnitScope.Agenda | UnitScope.PanelMetrics | UnitScope.Catalog);
        if (disposed || !IsAuthenticated) return;
        if (ActiveObsUnit?.Id != unit.Id) ResetUnitState();
        ActiveObsUnit = unit;
        ObsUnitSelectionOpen = false;
        ApplyUnitData(snapshot);
    }

    public Task ConfirmObsUnitAsync() => RunAsync(async () =>
    {
        var unit = CandidateObsUnit ?? throw new NoraApiException("Selecione uma UBS autorizada.");
        await ActivateUnitAsync(unit);
        if (disposed || ActiveObsUnit?.Id != unit.Id) return;
        Say(MessageKind.Success, $"UBS {unit.Name} confirmada. Cadastro, fila, vagas e agenda carregados.");
    });

    public Task LoadObsRegistryAsync() => RunAsync(async () =>
    {
        var unit = ActiveObsUnit ?? throw new NoraApiException("Escolha uma UBS antes de consultar os cadastros.");
        await LoadUnitScopeAsync(unit, UnitScope.Registry);
        if (ActiveObsUnit?.Id == unit.Id) Say(MessageKind.Success, "Cadastros da UBS atualizados.");
    });

    private string? ValidateRegistryContact(string email, string phone)
    {
        if (string.IsNullOrWhiteSpace(email)) return "Informe o e-mail cadastrado do paciente.";
        if (!BrazilianDataValidator.IsValidEmail(email)) return EmailError(email);
        return PhoneError(phone) is { Length: > 0 } phoneError ? phoneError : null;
    }

    public Task CreateObsRegistryAsync() => RunAsync(async () =>
    {
        var unit = ActiveObsUnit ?? throw new NoraApiException("Escolha uma UBS antes de cadastrar o paciente.");
        var name = ObsRegistryName.Trim(); var cpf = BrazilianDataValidator.NormalizeCpf(ObsRegistryCpf); var email = ObsRegistryEmail.Trim();
        var editing = editingObsCpf;
        if (name.Length < 2 || (editing is null && !BrazilianDataValidator.IsValidCpf(cpf))) throw new NoraApiException("Informe o nome e um CPF válido do paciente.");
        if (ValidateRegistryContact(email, ObsRegistryPhone) is { } contactError) throw new NoraApiException(contactError);
        var address = CreateObsRegistryAddress();
        var phone = string.IsNullOrWhiteSpace(ObsRegistryPhone) ? null : ObsRegistryPhone.Trim();
        if (editing is null) await api.CreateObsRegistryAsync(unit.Id, name, cpf, email, phone, address);
        else await api.UpdateObsRegistryAsync(unit.Id, name, editing, email, phone, address);
        ClearObsRegistryDraft();
        await ReloadAfterChangeAsync(unit, UnitScope.Registry,
            editing is null ? "Paciente cadastrado na UBS. Ele poderá criar a conta no aplicativo." : "Cadastro atualizado nesta UBS.");
    });

    public Task UpdateObsRegistryEmailAsync() => RunAsync(async () =>
    {
        var unit = ActiveObsUnit ?? throw new NoraApiException("Escolha uma UBS antes de corrigir o e-mail.");
        var patient = SelectedObsRegistry ?? throw new NoraApiException("Selecione o cadastro que deseja corrigir.");
        var email = CorrectedObsEmail.Trim();
        if (string.IsNullOrWhiteSpace(email)) throw new NoraApiException("Informe o e-mail correto.");
        if (!BrazilianDataValidator.IsValidEmail(email)) throw new NoraApiException(EmailError(email));
        await api.UpdateObsRegistryEmailAsync(unit.Id, patient.Cpf, email);
        SelectedObsRegistry = null;
        await ReloadAfterChangeAsync(unit, UnitScope.Registry, "E-mail atualizado no cadastro da UBS.");
    });

    private (string Cpf, DeactivationPreview Preview)? deactivationPreview;

    /// <summary>
    /// Antes de desativar o cadastro selecionado: pergunta à API quantas consultas futuras seriam canceladas e quantas entradas de fila encerradas (nada é gravado).
    /// Devolve nulo se a leitura falhar (a faixa já mostra o motivo): sem o número a confirmação não pode ser feita.
    /// </summary>
    public async Task<DeactivationPreview?> PreviewObsDeactivationAsync()
    {
        DeactivationPreview? result = null;
        await RunAsync(async () =>
        {
            var unit = ActiveObsUnit ?? throw new NoraApiException("Escolha uma UBS antes de alterar o cadastro.");
            var patient = SelectedObsRegistry ?? throw new NoraApiException("Selecione um paciente.");
            result = await api.DeactivationPreviewAsync(unit.Id, patient.Cpf);
            deactivationPreview = (patient.Cpf, result);
        });
        return result;
    }

    private static string CountText(long value, string singular, string plural) => $"{DisplayFormat.Count(value)} {(value == 1 ? singular : plural)}";

    /// <summary>Texto da confirmação de desativar: o que será cancelado (com o número da prévia), que o cancelamento é da unidade e não pesa na participação, e que reativar não restaura nada.</summary>
    public static string DeactivationConfirmationText(string patientName, DeactivationPreview preview)
    {
        var appointments = preview.FutureAppointments == 0 ? "Este paciente não tem consultas futuras nesta UBS para cancelar."
            : $"{(preview.FutureAppointments == 1 ? "1 consulta futura será cancelada" : $"{DisplayFormat.Count(preview.FutureAppointments)} consultas futuras serão canceladas")}, e o horário volta a ficar livre.";
        var cancellation = preview.FutureAppointments == 0 ? "" : "\n\nEsses cancelamentos contam como da unidade: não pesam na participação do paciente.";
        var queue = preview.ActiveWaitingEntries == 0 ? "Ele não tem entrada ativa na fila."
            : $"{CountText(preview.ActiveWaitingEntries, "entrada ativa na fila será encerrada", "entradas ativas na fila serão encerradas")}, junto com as ofertas pendentes.";
        return $"Desativar {patientName} somente nesta UBS?\n\n{appointments}{cancellation}\n\n{queue}\n\nReativar depois não restaura nada do que foi cancelado.";
    }

    /// <summary>
    /// Desativa ou reativa o cadastro do paciente <paramref name="cpf"/> nesta UBS. O estado pretendido (<paramref name="active"/>) é o que a pessoa confirmou na tela:
    /// se outra pessoa já mudou o cadastro enquanto a pergunta estava aberta, nada é enviado, porque alternar a partir da leitura nova faria o contrário do que foi confirmado.
    /// </summary>
    public Task SetObsRegistryActiveAsync(string cpf, bool active) => RunAsync(async () =>
    {
        var unit = ActiveObsUnit ?? throw new NoraApiException("Escolha uma UBS antes de alterar o cadastro.");
        var patient = ObsRegistry.FirstOrDefault(item => item.Cpf == cpf) ?? throw new NoraApiException("O paciente não está mais na lista desta UBS. Atualize o cadastro.");
        if (patient.IsActive == active)
        {
            deactivationPreview = null;
            Say(MessageKind.Info, active ? $"{patient.FullName} já está ativo nesta UBS: outra pessoa reativou o cadastro. Nada foi alterado." : $"{patient.FullName} já está desativado nesta UBS: outra pessoa desativou o cadastro. Nada foi alterado.");
            return;
        }
        // O número da prévia (se foi pedida para este paciente) entra na mensagem de retorno.
        var preview = !active && deactivationPreview is { } saved && saved.Cpf == patient.Cpf ? saved.Preview : null;
        deactivationPreview = null;
        await api.SetObsRegistryActiveAsync(unit.Id, patient.Cpf, active);
        // Desativar cancela fila, ofertas e consultas futuras da UBS: cadastro, fila, vagas e agenda voltam juntos.
        var scope = UnitScope.Registry | UnitScope.Queue | UnitScope.Slots;
        if (!active) scope |= UnitScope.Agenda | UnitScope.Today;
        var cancelled = preview is null ? "entradas, ofertas pendentes e consultas futuras foram canceladas"
            : preview.FutureAppointments == 0 ? "entradas e ofertas pendentes foram canceladas; não havia consultas futuras"
            : $"{CountText(preview.FutureAppointments, "consulta futura foi cancelada", "consultas futuras foram canceladas")} (não pesam na participação), além das entradas e ofertas pendentes";
        await ReloadAfterChangeAsync(unit, scope, active ? "Paciente reativado nesta UBS. Entradas e consultas canceladas não são restauradas." : $"Paciente desativado nesta UBS; {cancelled}.");
        if (ActiveObsUnit?.Id == unit.Id) SelectedObsRegistry = ObsRegistry.FirstOrDefault(item => item.Cpf == patient.Cpf);
    });

    private AddressInfo? CreateObsRegistryAddress()
    {
        var values = new[] { ObsRegistryCep, ObsRegistryStreet, ObsRegistryNumber, ObsRegistryComplement, ObsRegistryNeighborhood, ObsRegistryCity, ObsRegistryState };
        if (values.All(string.IsNullOrWhiteSpace)) return null;
        if (!BrazilianDataValidator.IsCompleteCep(ObsRegistryCep) || string.IsNullOrWhiteSpace(ObsRegistryStreet) || string.IsNullOrWhiteSpace(ObsRegistryCity) || ObsRegistryState.Trim().Length != 2)
            throw new NoraApiException("Para salvar endereço, confira CEP, logradouro, cidade e UF.");
        return new(BrazilianDataValidator.NormalizeCep(ObsRegistryCep), ObsRegistryStreet.Trim(), EmptyToNull(ObsRegistryNumber), EmptyToNull(ObsRegistryComplement), EmptyToNull(ObsRegistryNeighborhood), ObsRegistryCity.Trim(), ObsRegistryState.Trim().ToUpperInvariant(), obsRegistryIbge);
    }
    private static string? EmptyToNull(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private void ClearObsRegistryDraft()
    {
        editingObsCpf = null; obsRegistryIbge = null; ObsRegistryDraftOpen = false; Notify(nameof(IsEditingObsRegistry)); Notify(nameof(ObsRegistryDraftTitle)); Notify(nameof(ObsRegistrySaveLabel));
        ObsRegistryName = ObsRegistryCpf = ObsRegistryEmail = ObsRegistryPhone = "";
        ObsRegistryCep = ObsRegistryStreet = ObsRegistryNumber = ObsRegistryComplement = ObsRegistryNeighborhood = ObsRegistryCity = ObsRegistryState = "";
    }

    /// <summary>Esvazia os dados da UBS (cadastro, rascunhos, agenda, fila e vagas) sem mexer na UBS ativa nem na lista de UBS.</summary>
    private void ResetUnitState()
    {
        ObsRegistry.Clear(); ObsRegistrySearch = ""; SelectedObsRegistry = null; ClearObsRegistryDraft(); ClearAppointmentData(); ClearUnitLightningData(); ClearUnitMetrics(); ClearUnitScheduling(); ClearFamily();
        Notify(nameof(FilteredObsRegistry)); Notify(nameof(ActiveObsRegistry));
    }

    private void DeactivateObsUnit()
    {
        ResetUnitState(); ActiveObsUnit = null; ObsUnitSelectionOpen = true;
        if (candidateObsUnit is { } candidate && ObsUnits.All(unit => unit.Id != candidate.Id)) CandidateObsUnit = null;
    }

    private void ClearObsUnits()
    {
        ObsUnitSelectionOpen = true;
        ObsUnits.Clear(); obsUnitsLoaded = false; Notify(nameof(HasAvailableObsUnits)); Notify(nameof(ObsUnitsLoaded));
        ObsRegistryStatusFilter = "Todos"; ObsRegistryPresenceFilter = "Todos"; ObsRegistrySort = "Mais recentes"; CandidateObsUnit = null;
        ResetUnitState(); ActiveObsUnit = null;
    }
}
