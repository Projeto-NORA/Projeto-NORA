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
    private bool obsRegistryDraftOpen, obsUnitSelectionOpen = true;
    private string obsRegistryStatusFilter = "Todos", obsRegistrySort = "Mais recentes";
    private string obsRegistryPresenceFilter = "Todos";
    private string obsRegistryCep = "", obsRegistryStreet = "", obsRegistryNumber = "", obsRegistryComplement = "", obsRegistryNeighborhood = "", obsRegistryCity = "", obsRegistryState = "";
    public ObservableCollection<HealthUnit> ObsUnits { get; } = [];
    public bool HasAvailableObsUnits => ObsUnits.Count > 0;
    public bool ObsUnitSelectionOpen { get => obsUnitSelectionOpen; set { obsUnitSelectionOpen = value; Notify(); } }
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
        Message = "UBS autorizada mais próxima sugerida. Confira e confirme a escolha.";
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
    public string CorrectedObsEmail { get => correctedObsEmail; set { correctedObsEmail = value; Notify(); } }
    public HealthUnit? CandidateObsUnit { get => candidateObsUnit; set { candidateObsUnit = value; Notify(); } }
    public HealthUnit? ActiveObsUnit { get => activeObsUnit; private set { activeObsUnit = value; Notify(); Notify(nameof(ActiveObsUnitLabel)); Notify(nameof(HasActiveObsUnit)); Notify(nameof(PatientTotalCount)); Notify(nameof(QueueWaitingCount)); Notify(nameof(SlotOpenCount)); } }
    public string ActiveObsUnitLabel => ActiveObsUnit?.Name ?? "Nenhuma UBS confirmada";
    public bool HasActiveObsUnit => ActiveObsUnit != null;
    public string ObsRegistryName { get => obsRegistryName; set { obsRegistryName = value; Notify(); } }
    public string ObsRegistryCpf { get => obsRegistryCpf; set { obsRegistryCpf = value; Notify(); } }
    public string ObsRegistryEmail { get => obsRegistryEmail; set { obsRegistryEmail = value; Notify(); } }
    public string ObsRegistryPhone { get => obsRegistryPhone; set { obsRegistryPhone = value; Notify(); } }
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
        Message = "Endereço preenchido. Confira e complete o número antes de salvar.";
    });
    public Task LoadObsUnitsAsync() => RunAsync(async () =>
    {
        if (!IsOperational || !IsAuthenticated) throw new NoraApiException("Entre no OBS para consultar suas UBS.");
        ActiveObsUnit = null; ObsUnitSelectionOpen = true; ObsRegistry.Clear(); ObsRegistrySearch = ""; SelectedObsRegistry = null; ClearObsRegistryDraft(); CandidateObsUnit = null; ObsUnits.Clear(); Notify(nameof(HasAvailableObsUnits)); ClearAppointmentData(); ClearUnitLightningData();
        var units = await api.ObsUnitsAsync();
        if (disposed || !IsAuthenticated) return;
        Replace(ObsUnits, units); Notify(nameof(HasAvailableObsUnits));
        Message = "UBS autorizadas atualizadas. Escolha e confirme a unidade.";
    });
    public Task ConfirmObsUnitAsync() => RunAsync(async () =>
    {
        var unit = CandidateObsUnit ?? throw new NoraApiException("Selecione uma UBS autorizada.");
        ActiveObsUnit = null; ObsRegistry.Clear(); ObsRegistrySearch = ""; SelectedObsRegistry = null; ClearObsRegistryDraft(); ClearAppointmentData(); ClearUnitLightningData();
        var records = await api.ObsRegistryAsync(unit.Id);
        if (disposed || !IsAuthenticated) return;
        ActiveObsUnit = unit; Replace(ObsRegistry, records); Notify(nameof(PatientTotalCount)); Notify(nameof(FilteredObsRegistry)); Notify(nameof(ActiveObsRegistry));
        ObsUnitSelectionOpen = false;
        Message = "UBS confirmada e cadastros atualizados.";
    });
    public Task LoadObsRegistryAsync() => RunAsync(async () =>
    {
        var unit = ActiveObsUnit ?? throw new NoraApiException("Confirme uma UBS antes de consultar os cadastros.");
        var records = await api.ObsRegistryAsync(unit.Id);
        if (disposed || !IsAuthenticated || ActiveObsUnit?.Id != unit.Id) return;
        Replace(ObsRegistry, records); Notify(nameof(PatientTotalCount)); Notify(nameof(FilteredObsRegistry)); Notify(nameof(ActiveObsRegistry)); Message = "Cadastros da UBS atualizados.";
    });
    public Task CreateObsRegistryAsync() => RunAsync(async () =>
    {
        var unit = ActiveObsUnit ?? throw new NoraApiException("Confirme uma UBS antes de cadastrar o paciente.");
        var name = ObsRegistryName.Trim(); var cpf = ObsRegistryCpf.Trim(); var email = ObsRegistryEmail.Trim();
        if (name.Length < 2 || !BrazilianDataValidator.IsValidCpf(cpf) || string.IsNullOrWhiteSpace(email)) throw new NoraApiException("Informe nome, CPF válido e e-mail cadastrado do paciente.");
        var address = CreateObsRegistryAddress();
        var editing = editingObsCpf;
        if (editing is null) await api.CreateObsRegistryAsync(unit.Id, name, cpf, email, string.IsNullOrWhiteSpace(ObsRegistryPhone) ? null : ObsRegistryPhone.Trim(), address);
        else await api.UpdateObsRegistryAsync(unit.Id, name, editing, email, string.IsNullOrWhiteSpace(ObsRegistryPhone) ? null : ObsRegistryPhone.Trim(), address);
        ClearObsRegistryDraft();
        try
        {
            var records = await api.ObsRegistryAsync(unit.Id);
            if (disposed || !IsAuthenticated || ActiveObsUnit?.Id != unit.Id) return;
            Replace(ObsRegistry, records); Notify(nameof(PatientTotalCount)); Notify(nameof(FilteredObsRegistry)); Notify(nameof(ActiveObsRegistry));
            Message = editing is null ? "Paciente cadastrado na UBS. Ele poderá criar a conta no aplicativo." : "Cadastro atualizado nesta UBS.";
        }
        catch (NoraApiException error) when (!error.Unauthorized)
        {
            Message = editing is null ? "Paciente cadastrado, mas não foi possível atualizar a lista. Use Atualizar cadastros da UBS." : "Cadastro atualizado, mas não foi possível atualizar a lista.";
        }
    });
    public Task UpdateObsRegistryEmailAsync() => RunAsync(async () =>
    {
        var unit = ActiveObsUnit ?? throw new NoraApiException("Confirme uma UBS antes de corrigir o e-mail.");
        var patient = SelectedObsRegistry ?? throw new NoraApiException("Selecione o cadastro que deseja corrigir.");
        var email = CorrectedObsEmail.Trim();
        if (string.IsNullOrWhiteSpace(email)) throw new NoraApiException("Informe o e-mail correto.");
        await api.UpdateObsRegistryEmailAsync(unit.Id, patient.Cpf, email);
        SelectedObsRegistry = null;
        try
        {
            var records = await api.ObsRegistryAsync(unit.Id);
            if (disposed || !IsAuthenticated || ActiveObsUnit?.Id != unit.Id) return;
            Replace(ObsRegistry, records); Notify(nameof(FilteredObsRegistry)); Notify(nameof(ActiveObsRegistry)); Message = "E-mail atualizado no cadastro da UBS.";
        }
        catch (NoraApiException error) when (!error.Unauthorized)
        {
            Message = "E-mail atualizado, mas não foi possível atualizar a lista. Use Atualizar cadastros da UBS.";
        }
    });
    public Task SetObsRegistryActiveAsync() => RunAsync(async () =>
    {
        var unit = ActiveObsUnit ?? throw new NoraApiException("Confirme uma UBS antes de alterar o cadastro.");
        var patient = SelectedObsRegistry ?? throw new NoraApiException("Selecione um paciente.");
        var active = !patient.IsActive;
        await api.SetObsRegistryActiveAsync(unit.Id, patient.Cpf, active);
        try
        {
            var records = await api.ObsRegistryAsync(unit.Id); var entries = await api.UnitWaitingListAsync(unit.Id); var slots = await api.UnitLightningSlotsAsync(unit.Id);
            if (disposed || !IsAuthenticated || ActiveObsUnit?.Id != unit.Id) return;
            Replace(ObsRegistry, records); Notify(nameof(FilteredObsRegistry)); Notify(nameof(ActiveObsRegistry)); Replace(UnitWaitingList, entries); Replace(UnitLightningSlots, slots); unitQueueLoaded = unitSlotsLoaded = true;
            SelectedObsRegistry = ObsRegistry.FirstOrDefault(item => item.Cpf == patient.Cpf); Notify(nameof(QueueWaitingCount)); Notify(nameof(SlotOpenCount));
            Message = active ? "Paciente reativado nesta UBS. Entradas canceladas não são restauradas." : "Paciente desativado nesta UBS; entradas e ofertas pendentes foram canceladas.";
        }
        catch (NoraApiException error) when (!error.Unauthorized) { SelectedObsRegistry = null; Message = "Situação alterada. Atualize os cadastros e a fila para conferir."; }
    });
    private AddressInfo? CreateObsRegistryAddress()
    {
        var values = new[] { ObsRegistryCep, ObsRegistryStreet, ObsRegistryNumber, ObsRegistryComplement, ObsRegistryNeighborhood, ObsRegistryCity, ObsRegistryState };
        if (values.All(string.IsNullOrWhiteSpace)) return null;
        if (!BrazilianDataValidator.IsCompleteCep(ObsRegistryCep) || string.IsNullOrWhiteSpace(ObsRegistryStreet) || string.IsNullOrWhiteSpace(ObsRegistryCity) || ObsRegistryState.Trim().Length != 2)
            throw new NoraApiException("Para salvar endereço, confira CEP, logradouro, cidade e UF.");
        return new(BrazilianDataValidator.NormalizeCep(ObsRegistryCep), ObsRegistryStreet.Trim(), EmptyToNull(ObsRegistryNumber), EmptyToNull(ObsRegistryComplement), EmptyToNull(ObsRegistryNeighborhood), ObsRegistryCity.Trim(), ObsRegistryState.Trim().ToUpperInvariant(), obsRegistryIbge);
    }
    private void ClearObsRegistryDraft()
    {
        editingObsCpf = null; obsRegistryIbge = null; ObsRegistryDraftOpen = false; Notify(nameof(IsEditingObsRegistry)); Notify(nameof(ObsRegistryDraftTitle)); Notify(nameof(ObsRegistrySaveLabel));
        ObsRegistryName = ObsRegistryCpf = ObsRegistryEmail = ObsRegistryPhone = "";
        ObsRegistryCep = ObsRegistryStreet = ObsRegistryNumber = ObsRegistryComplement = ObsRegistryNeighborhood = ObsRegistryCity = ObsRegistryState = "";
    }
    private void ClearObsUnits()
    {
        ObsUnitSelectionOpen = true;
        ObsUnits.Clear(); Notify(nameof(HasAvailableObsUnits)); ObsRegistry.Clear(); ObsRegistrySearch = ""; ObsRegistryStatusFilter = "Todos"; ObsRegistryPresenceFilter = "Todos"; ObsRegistrySort = "Mais recentes"; CandidateObsUnit = null; ActiveObsUnit = null; SelectedObsRegistry = null; ClearAppointmentData(); ClearUnitLightningData();
        ClearObsRegistryDraft();
    }
}
