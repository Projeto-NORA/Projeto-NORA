using System.Collections.ObjectModel;
using System.Windows.Input;
using Nora.Desktop.Generics;
using Nora.Desktop.Models;
using Nora.Desktop.Services;

namespace Nora.Desktop.ViewModels;

public sealed class WorkspaceViewModel : ObservableObject, IDisposable
{
    private readonly DesktopProfile profile;
    private readonly NoraApiClient api;
    private bool busy;
    private AccountUser? user;
    private string endpoint;
    private string message = "Informe o endereço da API e entre com CPF ou e-mail.";
    private string identifier = "";
    private PatientAccount? selectedPatient;
    private QueueEntry? selectedQueue;
    private MunicipalSummary? summary;
    private string section = "home";
    private string patientSearch = "";
    private string slotLabel = "";
    public WorkspaceViewModel(DesktopProfile profile, NoraApiClient? api = null)
    {
        this.profile = profile;
        this.api = api ?? new();
        endpoint = Environment.GetEnvironmentVariable("NORA_API_URL") ?? "";
        RefreshCommand = new RelayCommand(async _ => await RunAsync(async () =>
        {
            await RefreshAsync();
            Message = "Dados atualizados pela API.";
        }), _ => CanOperate);
        LogoutCommand = new RelayCommand(async _ => await RunAsync(LogoutAsync), _ => CanOperate);
        EnqueueCommand = new RelayCommand(async _ => await RunAsync(async () => { await this.api.EnqueueAsync(SelectedPatient!.Id); await RefreshAfterChangeAsync("Paciente entrou na fila por ordem de entrada."); }), _ => CanOperate && IsOperational && SelectedPatient != null);
        CancelQueueCommand = new RelayCommand(async _ => await RunAsync(async () => { await this.api.CancelAsync(SelectedQueue!.Id); await RefreshAfterChangeAsync("Entrada da fila cancelada."); }), _ => CanOperate && IsOperational && SelectedQueue?.Status is "waiting" or "offered");
        CreateSlotCommand = new RelayCommand(async _ => await RunAsync(async () => { await this.api.CreateSlotAsync(SlotLabel.Trim()); SlotLabel = ""; await RefreshAfterChangeAsync("Vaga aberta. A oferta segue a ordem da fila, com 10 minutos para resposta."); }), _ => CanOperate && IsOperational && !string.IsNullOrWhiteSpace(SlotLabel));
    }
    public string Endpoint { get => endpoint; set { endpoint = value; Notify(); } }
    public string Identifier { get => identifier; set { identifier = value; Notify(); } }
    public string FullName { get; set; } = "";
    public string Cpf { get; set; } = "";
    public string Email { get; set; } = "";
    public string SlotLabel { get => slotLabel; set { slotLabel = value; Notify(); CommandManager.InvalidateRequerySuggested(); } }
    public bool IsBusy { get => busy; private set { busy = value; Notify(); Notify(nameof(CanOperate)); Notify(nameof(CanLogin)); CommandManager.InvalidateRequerySuggested(); } }
    public bool IsAuthenticated => user != null;
    public bool IsSignedOut => !IsAuthenticated;
    public bool CanOperate => IsAuthenticated && !IsBusy;
    public bool CanLogin => !IsBusy && !IsAuthenticated;
    public bool IsOperational => !profile.IsMunicipal;
    public bool IsMunicipal => profile.IsMunicipal;
    public string AccountName => user?.FullName ?? "Conta desconectada";
    public string ConnectionLabel => IsAuthenticated ? "Conta conectada" : "Conta desconectada";
    public string Message { get => message; private set { message = value; Notify(); } }
    public string Section { get => section; set { section = value; Notify(); Notify(nameof(ShowPatients)); Notify(nameof(ShowQueue)); Notify(nameof(ShowSlots)); } }
    public bool ShowPatients => IsAuthenticated && IsOperational && Section is "home" or "patients";
    public bool ShowQueue => IsAuthenticated && IsOperational && Section is "home" or "queue";
    public bool ShowSlots => IsAuthenticated && IsOperational && Section is "home" or "slots";
    public ObservableCollection<PatientAccount> Patients { get; } = [];
    public string PatientSearch { get => patientSearch; set { patientSearch = value; Notify(); Notify(nameof(FilteredPatients)); Notify(nameof(HasNoPatientResults)); SelectedPatient = null; } }
    public IEnumerable<PatientAccount> FilteredPatients => Patients.Where(p => p.FullName.Contains(PatientSearch.Trim(), StringComparison.CurrentCultureIgnoreCase) || p.Email.Contains(PatientSearch.Trim(), StringComparison.OrdinalIgnoreCase) || p.Cpf.Contains(PatientSearch.Trim(), StringComparison.Ordinal));
    public ObservableCollection<QueueEntry> Queue { get; } = [];
    public ObservableCollection<LightningSlot> Slots { get; } = [];
    public PatientAccount? SelectedPatient { get => selectedPatient; set { selectedPatient = value; Notify(); CommandManager.InvalidateRequerySuggested(); } }
    public QueueEntry? SelectedQueue { get => selectedQueue; set { selectedQueue = value; Notify(); CommandManager.InvalidateRequerySuggested(); } }
    public bool HasNoPatients => Patients.Count == 0;
    public bool HasNoPatientResults => !HasNoPatients && !FilteredPatients.Any();
    public bool HasNoQueue => Queue.Count == 0;
    public bool HasNoSlots => Slots.Count == 0;
    public string PatientCount => summary?.Patients.ToString() ?? "—";
    public string WaitingCount => summary?.WaitingPatients.ToString() ?? "—";
    public string SlotCount => summary?.OpenSlots.ToString() ?? "—";
    public string AcceptedCount => summary?.AcceptedOffers.ToString() ?? "—";
    public ICommand RefreshCommand { get; }
    public ICommand LogoutCommand { get; }
    public ICommand EnqueueCommand { get; }
    public ICommand CancelQueueCommand { get; }
    public ICommand CreateSlotCommand { get; }

    public Task LoginAsync(string password) => RunAsync(async () =>
    {
        if (string.IsNullOrWhiteSpace(Identifier) || string.IsNullOrWhiteSpace(password)) throw new NoraApiException("Informe seu CPF ou e-mail e sua senha.");
        api.Configure(Endpoint);
        var session = await api.LoginAsync(profile.Id, Identifier.Trim(), password);
        user = session.User;
        NotifySession();
        await RefreshAsync();
        Message = "Conta conectada. Dados atualizados pela API.";
    });
    public Task RegisterPatientAsync(string password) => RunAsync(async () =>
    {
        if (!IsOperational || !IsAuthenticated) throw new NoraApiException("Entre no OBS para cadastrar pacientes.");
        if (string.IsNullOrWhiteSpace(FullName) || string.IsNullOrWhiteSpace(Cpf) || string.IsNullOrWhiteSpace(Email) || string.IsNullOrWhiteSpace(password))
            throw new NoraApiException("Preencha nome completo, CPF, e-mail e senha inicial do paciente.");
        var patient = await api.RegisterPatientAsync(FullName.Trim(), Cpf.Trim(), Email.Trim(), password);
        FullName = Cpf = Email = "";
        Notify(nameof(FullName)); Notify(nameof(Cpf)); Notify(nameof(Email));
        await RefreshAfterChangeAsync("Paciente cadastrado. A conta já pode entrar no APPNORA com CPF ou e-mail.");
        SelectedPatient = Patients.FirstOrDefault(p => p.Id == patient.Id);
    });
    private async Task LogoutAsync()
    {
        try { await api.LogoutAsync(); Message = "Você saiu da conta."; }
        catch (NoraApiException error)
        {
            if (error.Unauthorized) Message = "Você saiu da conta.";
            else throw new NoraApiException("Você saiu deste aplicativo, mas a API não confirmou a revogação da sessão. A sessão no servidor permanece até expirar.");
        }
        finally { SignOut(); }
    }
    private async Task RefreshAsync()
    {
        if (IsMunicipal) summary = await api.SummaryAsync();
        else
        {
            // Read all data before publishing to avoid a partly updated screen on errors.
            var patients = await api.PatientsAsync();
            var queue = await api.QueueAsync();
            var slots = await api.SlotsAsync();
            Replace(Patients, patients); Replace(Queue, queue); Replace(Slots, slots);
            SelectedPatient = null; SelectedQueue = null;
        }
        foreach (var name in new[] { nameof(PatientCount), nameof(WaitingCount), nameof(SlotCount), nameof(AcceptedCount), nameof(HasNoPatients), nameof(HasNoPatientResults), nameof(HasNoQueue), nameof(HasNoSlots), nameof(FilteredPatients) }) Notify(name);
    }
    private async Task RefreshAfterChangeAsync(string successMessage)
    {
        try { await RefreshAsync(); Message = successMessage; }
        catch (NoraApiException error) when (!error.Unauthorized)
        {
            Message = successMessage + " Não foi possível atualizar a tela. Use Atualizar antes de repetir a operação.";
        }
    }
    private async Task RunAsync(Func<Task> operation)
    {
        if (IsBusy) return;
        IsBusy = true;
        Message = "Conectando à API…";
        try { await operation(); }
        catch (NoraApiException error) { if (error.Unauthorized && IsAuthenticated) SignOut(); Message = error.Message; }
        finally { IsBusy = false; }
    }
    private static void Replace<T>(ObservableCollection<T> destination, IEnumerable<T> source) { destination.Clear(); foreach (var item in source) destination.Add(item); }
    private void SignOut()
    {
        api.ClearSession(); user = null; summary = null;
        Patients.Clear(); Queue.Clear(); Slots.Clear(); SelectedPatient = null; SelectedQueue = null;
        Identifier = FullName = Cpf = Email = SlotLabel = "";
        PatientSearch = "";
        foreach (var name in new[] { nameof(PatientCount), nameof(WaitingCount), nameof(SlotCount), nameof(AcceptedCount), nameof(FullName), nameof(Cpf), nameof(Email), nameof(SlotLabel) }) Notify(name);
        NotifySession();
    }
    private void NotifySession()
    {
        foreach (var name in new[] { nameof(IsAuthenticated), nameof(IsSignedOut), nameof(CanOperate), nameof(CanLogin), nameof(AccountName), nameof(ConnectionLabel), nameof(ShowPatients), nameof(ShowQueue), nameof(ShowSlots) }) Notify(name);
    }
    public void Dispose() { SignOut(); api.Dispose(); }
}
