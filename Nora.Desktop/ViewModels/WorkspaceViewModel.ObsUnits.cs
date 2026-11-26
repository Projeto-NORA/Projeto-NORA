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
    private int obsRegistryDraftVersion;
    private bool obsUnitsLoaded, profileChoicePending, obsRegistryLoaded;
    private string obsRegistryStatusFilter = "Todos", obsRegistrySort = "Nome A–Z";
    private string obsRegistryPresenceFilter = "Todos";
    private string obsRegistryCep = "", obsRegistryStreet = "", obsRegistryNumber = "", obsRegistryComplement = "", obsRegistryNeighborhood = "", obsRegistryCity = "", obsRegistryState = "";
    public ObservableCollection<HealthUnit> ObsUnits { get; } = [];
    public bool HasAvailableObsUnits => ObsUnits.Count > 0;
    public bool ObsUnitsLoaded => obsUnitsLoaded;
    /// <summary>
    /// Depois do login (ou de restaurar a sessão) a conta tem mais de uma UBS e nenhuma ativa: a janela principal abre sozinha o pop-up "Trocar perfil".
    /// O pedido fica guardado até a janela poder atender (sem ação do usuário em andamento) e é atendido uma vez só por <see cref="AcknowledgeProfileChoice"/>.
    /// </summary>
    public bool ProfileChoicePending => profileChoicePending;
    /// <summary>A janela principal atendeu (ou dispensou) o pedido de escolha da UBS.</summary>
    public void AcknowledgeProfileChoice() { if (!profileChoicePending) return; profileChoicePending = false; Notify(nameof(ProfileChoicePending)); }
    /// <summary>Orientação das telas que dependem de uma UBS: explica o que falta para ter uma UBS ativa (nenhuma concedida, várias a escolher, lista ainda não lida).</summary>
    public string ObsUnitPrompt => !IsAuthenticated || !IsOperational || HasActiveObsUnit ? ""
        : !obsUnitsLoaded ? "As UBS autorizadas ainda não foram carregadas. Clique em Escolher UBS para ler a lista."
        : ObsUnits.Count == 0 ? "Nenhuma UBS autorizada para esta conta. A Secretaria precisa conceder acesso a uma UBS (aba Usuários e histórico da gestão)."
        : ObsUnits.Count == 1 ? "Escolha a UBS e clique em Trocar para carregar os dados da operação."
        : $"Esta conta tem acesso a {ObsUnits.Count} UBS. Escolha a unidade e clique em Trocar para carregar os dados da operação.";
    public bool NeedsObsUnitChoice => IsAuthenticated && IsOperational && !HasActiveObsUnit;
    /// <summary>"Trocar" do pop-up: há uma UBS escolhida e ela não é a que já está ativa.</summary>
    public bool CanConfirmObsUnit => CanOperate && CandidateObsUnit is { } candidate && candidate.Id != ActiveObsUnit?.Id;
    /// <summary>Seleciona (sem trocar) a UBS autorizada mais próxima da posição. Devolve falso quando não há o que sugerir (sem conta entrada, posição inválida ou lista vazia).</summary>
    public bool SuggestClosestObsUnit(double latitude, double longitude)
    {
        if (!IsAuthenticated || !double.IsFinite(latitude) || !double.IsFinite(longitude) || latitude is < -90 or > 90 || longitude is < -180 or > 180 || ObsUnits.Count == 0) return false;
        static double Radians(double degrees) => degrees * Math.PI / 180;
        static double Distance(HealthUnit unit, double latitude, double longitude)
        {
            var dLat = Radians(unit.Latitude - latitude); var dLon = Radians(unit.Longitude - longitude);
            var a = Math.Pow(Math.Sin(dLat / 2), 2) + Math.Cos(Radians(latitude)) * Math.Cos(Radians(unit.Latitude)) * Math.Pow(Math.Sin(dLon / 2), 2);
            return Math.Asin(Math.Sqrt(Math.Clamp(a, 0, 1)));
        }
        CandidateObsUnit = ObsUnits.OrderBy(unit => Distance(unit, latitude, longitude)).ThenBy(unit => unit.Name, StringComparer.CurrentCultureIgnoreCase).First();
        Say(MessageKind.Info, "UBS autorizada mais próxima sugerida. Confira e clique em Trocar.");
        return true;
    }
    public ObservableCollection<UnitPatientRecord> ObsRegistry { get; } = [];
    public string ObsRegistrySearch { get => obsRegistrySearch; set { obsRegistrySearch = value; Notify(); Notify(nameof(FilteredObsRegistry)); Notify(nameof(ActiveObsRegistry)); Notify(nameof(ObsRegistryListState)); } }
    public string ObsRegistryStatusFilter { get => obsRegistryStatusFilter; set { obsRegistryStatusFilter = value; Notify(); Notify(nameof(FilteredObsRegistry)); Notify(nameof(ActiveObsRegistry)); Notify(nameof(ObsRegistryListState)); } }
    public string ObsRegistryPresenceFilter { get => obsRegistryPresenceFilter; set { obsRegistryPresenceFilter = value; Notify(); Notify(nameof(FilteredObsRegistry)); Notify(nameof(ObsRegistryListState)); } }
    public string ObsRegistrySort { get => obsRegistrySort; set { obsRegistrySort = value; Notify(); Notify(nameof(FilteredObsRegistry)); Notify(nameof(ActiveObsRegistry)); } }
    /// <summary>A primeira leitura dos cadastros ainda não terminou: a tela mostra o indicador na cor da marca.</summary>
    public bool ObsRegistryLoading => HasActiveObsUnit && !obsRegistryLoaded;
    /// <summary>Estado da listagem de pacientes: "Carregando…" antes da primeira leitura, sem cadastro nenhum na UBS ou nenhum resultado para a busca e os filtros. Vazio quando há itens (ou sem UBS ativa, caso do aviso de escolha).</summary>
    public string ObsRegistryListState => !HasActiveObsUnit ? "" : !obsRegistryLoaded ? "Carregando os pacientes…" : ObsRegistry.Count == 0 ? "Nenhum paciente cadastrado nesta UBS." : MatchingObsRegistry().Any() ? "" : "Nenhum paciente encontrado para esta busca.";
    public IEnumerable<UnitPatientRecord> ActiveObsRegistry => ObsRegistry.Where(patient => patient.IsActive);
    public IEnumerable<UnitPatientRecord> FilteredObsRegistry => ObsRegistrySort switch
    {
        "Nome Z–A" => MatchingObsRegistry().OrderByDescending(patient => patient.FullName, StringComparer.CurrentCultureIgnoreCase),
        "Nome A–Z" => MatchingObsRegistry().OrderBy(patient => patient.FullName, StringComparer.CurrentCultureIgnoreCase),
        // Pelos pontos do score (0 a 1000; quem nunca teve consulta encerrada tem 1000). Cadastro sem o dado da API (Attendance nulo) vai para o fim nas duas direções.
        "Maior score" => MatchingObsRegistry().OrderBy(patient => patient.Attendance is null).ThenByDescending(patient => patient.Attendance?.PointsValue).ThenBy(patient => patient.FullName, StringComparer.CurrentCultureIgnoreCase),
        "Menor score" => MatchingObsRegistry().OrderBy(patient => patient.Attendance is null).ThenBy(patient => patient.Attendance?.PointsValue).ThenBy(patient => patient.FullName, StringComparer.CurrentCultureIgnoreCase),
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
    public UnitPatientRecord? SelectedObsRegistry { get => selectedObsRegistry; set { selectedObsRegistry = value; CorrectedObsEmail = value?.Email ?? ""; Notify(); } }
    public string CorrectedObsEmail { get => correctedObsEmail; set { correctedObsEmail = value; Notify(); Notify(nameof(CorrectedObsEmailError)); } }
    public HealthUnit? CandidateObsUnit { get => candidateObsUnit; set { candidateObsUnit = value; Notify(); Notify(nameof(CanConfirmObsUnit)); } }
    public HealthUnit? ActiveObsUnit
    {
        get => activeObsUnit;
        private set
        {
            activeObsUnit = value;
            Notify(); Notify(nameof(ActiveObsUnitLabel)); Notify(nameof(ActiveObsUnitRegion)); Notify(nameof(HasActiveObsUnit)); Notify(nameof(ObsRegistryListState)); Notify(nameof(ObsRegistryLoading)); Notify(nameof(NeedsObsUnitChoice)); Notify(nameof(CanConfirmObsUnit)); Notify(nameof(ObsUnitPrompt)); Notify(nameof(UnitLabel)); Notify(nameof(AgendaStateText));
            NotifyUnitMetrics(); Notify(nameof(UnitMetricsScopeLabel));
            NotifyUnitCounts();
            // Família e Vínculos aprovados: com a UBS escolhida agora, o estado das listas passa de vazio para "Carregando…" até a primeira leitura.
            NotifyFamily();
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
    public string ObsRegistryCity { get => obsRegistryCity; set { if (obsRegistryCity != value) obsRegistryIbge = null; obsRegistryCity = value; Notify(); } }
    public string ObsRegistryState { get => obsRegistryState; set { if (obsRegistryState != value) obsRegistryIbge = null; obsRegistryState = value; Notify(); } }
    public bool IsEditingObsRegistry => editingObsCpf is not null;
    public string ObsRegistryDraftTitle => IsEditingObsRegistry ? "Editar cadastro da UBS" : "Cadastrar referência na UBS";
    public string ObsRegistrySaveLabel => IsEditingObsRegistry ? "Salvar alterações" : "Cadastrar paciente";
    public void EditSelectedObsRegistry()
    {
        if (ActiveObsUnit is null || SelectedObsRegistry is not { } patient) return;
        obsRegistryDraftVersion++;
        editingObsCpf = patient.Cpf; ObsRegistryName = patient.FullName; ObsRegistryCpf = patient.Cpf; ObsRegistryEmail = patient.Email; ObsRegistryPhone = patient.Phone ?? "";
        var address = patient.Address; ObsRegistryCep = address?.Cep ?? ""; ObsRegistryStreet = address?.Street ?? "";
        ObsRegistryNumber = address?.Number ?? ""; ObsRegistryComplement = address?.Complement ?? ""; ObsRegistryNeighborhood = address?.Neighborhood ?? "";
        ObsRegistryCity = address?.City ?? ""; ObsRegistryState = address?.State ?? ""; obsRegistryIbge = address?.Ibge; Notify(nameof(IsEditingObsRegistry)); Notify(nameof(ObsRegistryDraftTitle)); Notify(nameof(ObsRegistrySaveLabel));
    }
    public void NewObsRegistryDraft() => ClearObsRegistryDraft();
    public Task LookupObsRegistryCepAsync() => RunAsync(async () =>
    {
        if (!BrazilianDataValidator.IsCompleteCep(ObsRegistryCep)) throw new NoraApiException("Informe um CEP com 8 dígitos para pesquisar o endereço.");
        var requestedCep = BrazilianDataValidator.NormalizeCep(ObsRegistryCep);
        var unitId = ActiveObsUnit?.Id; var draftCpf = editingObsCpf; var draftVersion = obsRegistryDraftVersion;
        var before = (ObsRegistryStreet, ObsRegistryNeighborhood, ObsRegistryCity, ObsRegistryState, ObsRegistryComplement);
        var result = await api.LookupCepAsync(requestedCep);
        if (disposed || ActiveObsUnit?.Id != unitId || editingObsCpf != draftCpf || obsRegistryDraftVersion != draftVersion || BrazilianDataValidator.NormalizeCep(ObsRegistryCep) != requestedCep) return;
        if (ObsRegistryStreet == before.ObsRegistryStreet) ObsRegistryStreet = result.Street;
        if (ObsRegistryNeighborhood == before.ObsRegistryNeighborhood) ObsRegistryNeighborhood = result.Neighborhood ?? "";
        if (ObsRegistryCity == before.ObsRegistryCity) ObsRegistryCity = result.City;
        if (ObsRegistryState == before.ObsRegistryState) ObsRegistryState = result.State;
        if (ObsRegistryComplement == before.ObsRegistryComplement) ObsRegistryComplement = result.Complement ?? "";
        obsRegistryIbge = ObsRegistryCity == result.City && ObsRegistryState == result.State ? result.Ibge : null;
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
    }

    /// <summary>Fim da carga do login: conta com várias UBS e nenhuma ativa pede à janela principal que abra o pop-up "Trocar perfil" (uma vez).</summary>
    private void RequestProfileChoiceIfNeeded()
    {
        if (disposed || !NeedsObsUnitChoice || ObsUnits.Count < 2 || profileChoicePending) return;
        profileChoicePending = true; Notify(nameof(ProfileChoicePending));
    }

    /// <summary>
    /// Relê a lista de UBS da conta (o pop-up "Trocar perfil" faz isso ao abrir). Devolve se a leitura terminou. <paramref name="quiet"/> deixa de mostrar a mensagem de sucesso na faixa
    /// (a de falha e a de concessão revogada aparecem sempre).
    /// </summary>
    public async Task<bool> LoadObsUnitsAsync(bool quiet = false)
    {
        var finished = false;
        await RunAsync(async () =>
        {
            if (!IsOperational || !IsAuthenticated) throw new NoraApiException("Entre no OBS para consultar suas UBS.");
            var hadActive = ActiveObsUnit?.Id;
            // A lista de UBS mudou (ou pode ter mudado): os comunicados são relidos por inteiro no próximo ciclo, porque o alcance depende das concessões da conta.
            announcementsFullDue = true;
            await InitializeObsUnitsAsync();
            if (disposed || !IsAuthenticated) return;
            finished = true;
            if (hadActive is not null && ActiveObsUnit is null) Say(MessageKind.Error, "A UBS ativa deixou de estar autorizada para esta conta. Escolha outra UBS.");
            else if (!quiet) Say(MessageKind.Success, ObsUnits.Count == 0 ? "Lista atualizada: esta conta não tem UBS autorizada." : "Lista de UBS atualizada.");
        });
        return finished;
    }

    /// <summary>Lê tudo da nova UBS antes de trocar qualquer coisa: se a leitura falhar, a UBS ativa anterior e seus dados continuam intactos.</summary>
    private async Task ActivateUnitAsync(HealthUnit unit)
    {
        // Trocar de UBS com Família ou Vínculos aprovados aberto já traz os pedidos e vínculos da nova UBS (a tela não tem mais o botão Atualizar; leitura tolerante, não derruba a troca).
        var snapshot = await FetchUnitDataAsync(unit, UnitScope.Everything | UnitScope.Agenda | UnitScope.PanelMetrics | UnitScope.Catalog | UnitScope.Profile | (Section == "family" ? UnitScope.Family : UnitScope.None));
        if (disposed || !IsAuthenticated) return;
        if (ActiveObsUnit?.Id != unit.Id) ResetUnitState();
        ActiveObsUnit = unit;
        ApplyUnitData(snapshot);
    }

    public Task ConfirmObsUnitAsync() => RunAsync(async () =>
    {
        var unit = CandidateObsUnit ?? throw new NoraApiException("Selecione uma UBS autorizada.");
        await ActivateUnitAsync(unit);
        if (disposed || ActiveObsUnit?.Id != unit.Id) return;
        Say(MessageKind.Success, $"UBS {unit.Name} confirmada. Cadastro, vagas e agenda carregados.");
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

    /// <summary>Corrige só o e-mail do cadastro selecionado. Devolve se a API aceitou (o pop-up "Corrigir e-mail" fecha só nesse caso); a mensagem de erro fica em <see cref="Message"/>, que o pop-up mostra.</summary>
    public async Task<bool> UpdateObsRegistryEmailAsync()
    {
        var updated = false;
        await RunAsync(async () =>
        {
            var unit = ActiveObsUnit ?? throw new NoraApiException("Escolha uma UBS antes de corrigir o e-mail.");
            var patient = SelectedObsRegistry ?? throw new NoraApiException("Selecione o cadastro que deseja corrigir.");
            var email = CorrectedObsEmail.Trim();
            if (string.IsNullOrWhiteSpace(email)) throw new NoraApiException("Informe o e-mail correto.");
            if (!BrazilianDataValidator.IsValidEmail(email)) throw new NoraApiException(EmailError(email));
            await api.UpdateObsRegistryEmailAsync(unit.Id, patient.Cpf, email);
            updated = true;
            SelectedObsRegistry = null;
            await ReloadAfterChangeAsync(unit, UnitScope.Registry, "E-mail atualizado no cadastro da UBS.");
        });
        return updated;
    }

    private (string Cpf, DeactivationPreview Preview)? deactivationPreview;

    /// <summary>
    /// Antes de desativar o cadastro selecionado: pergunta à API quantas consultas futuras seriam canceladas e quantas ofertas pendentes encerradas (nada é gravado).
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

    /// <summary>Texto da confirmação de desativar: o que será cancelado (com o número da prévia), que o cancelamento é da unidade e não pesa no score, e que reativar não restaura nada.</summary>
    public static string DeactivationConfirmationText(string patientName, DeactivationPreview preview)
    {
        var appointments = preview.FutureAppointments == 0 ? "Este paciente não tem consultas futuras nesta UBS para cancelar."
            : $"{(preview.FutureAppointments == 1 ? "1 consulta futura será cancelada" : $"{DisplayFormat.Count(preview.FutureAppointments)} consultas futuras serão canceladas")}, e o horário pode ser oferecido a outros pacientes como vaga relâmpago.";
        var cancellation = preview.FutureAppointments == 0 ? "" : "\n\nEsses cancelamentos contam como da unidade: não pesam no score do paciente.";
        return $"Desativar {patientName} somente nesta UBS?\n\n{appointments}{cancellation}\n\nReativar depois não restaura nada do que foi cancelado.";
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
        // Desativar cancela ofertas e consultas futuras da UBS: cadastro, vagas e agenda voltam juntos.
        var scope = UnitScope.Registry | UnitScope.Slots;
        if (!active) scope |= UnitScope.Agenda | UnitScope.Today;
        var cancelled = preview is null ? "entradas, ofertas pendentes e consultas futuras foram canceladas"
            : preview.FutureAppointments == 0 ? "entradas e ofertas pendentes foram canceladas; não havia consultas futuras"
            : $"{CountText(preview.FutureAppointments, "consulta futura foi cancelada", "consultas futuras foram canceladas")} (não pesam no score), além das entradas e ofertas pendentes";
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
        obsRegistryDraftVersion++;
        editingObsCpf = null; obsRegistryIbge = null; Notify(nameof(IsEditingObsRegistry)); Notify(nameof(ObsRegistryDraftTitle)); Notify(nameof(ObsRegistrySaveLabel));
        ObsRegistryName = ObsRegistryCpf = ObsRegistryEmail = ObsRegistryPhone = "";
        ObsRegistryCep = ObsRegistryStreet = ObsRegistryNumber = ObsRegistryComplement = ObsRegistryNeighborhood = ObsRegistryCity = ObsRegistryState = "";
    }

    /// <summary>Esvazia os dados da UBS (cadastro, rascunhos, agenda e vagas) sem mexer na UBS ativa nem na lista de UBS.</summary>
    private void ResetUnitState()
    {
        ObsRegistry.Clear(); ObsRegistrySearch = ""; SelectedObsRegistry = null; ClearObsRegistryDraft(); ClearAppointmentData(); ClearUnitLightningData(); ClearUnitMetrics(); ClearUnitScheduling(); ClearFamily(); ClearUnitProfile(); ClearDevices();
        Notify(nameof(FilteredObsRegistry)); Notify(nameof(ActiveObsRegistry));
        // A listagem de Pacientes volta a "Carregando os pacientes…" até a primeira leitura da UBS nova.
        obsRegistryLoaded = false; Notify(nameof(ObsRegistryListState)); Notify(nameof(ObsRegistryLoading));
    }

    private void DeactivateObsUnit()
    {
        ResetUnitState(); ActiveObsUnit = null;
        if (candidateObsUnit is { } candidate && ObsUnits.All(unit => unit.Id != candidate.Id)) CandidateObsUnit = null;
    }

    private void ClearObsUnits()
    {
        profileChoicePending = false; Notify(nameof(ProfileChoicePending));
        ObsUnits.Clear(); obsUnitsLoaded = false; Notify(nameof(HasAvailableObsUnits)); Notify(nameof(ObsUnitsLoaded));
        ObsRegistryStatusFilter = "Todos"; ObsRegistryPresenceFilter = "Todos"; ObsRegistrySort = "Nome A–Z"; CandidateObsUnit = null;
        ResetUnitState(); ActiveObsUnit = null;
    }
}
