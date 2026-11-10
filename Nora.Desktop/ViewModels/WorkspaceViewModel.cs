using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Threading;
using Nora.Desktop.Generics;
using Nora.Desktop.Models;
using Nora.Desktop.Services;

namespace Nora.Desktop.ViewModels;

public sealed partial class WorkspaceViewModel : ObservableObject, IDisposable
{
    private readonly DesktopProfile profile;
    private readonly NoraApiClient api;
    private bool busy;
    private AccountUser? user;
    private string endpoint;
    private string message = "Informe o endereço da API e entre com CPF ou e-mail.";
    private string identifier = "";
    private string cep = "";
    private string street = "";
    private string addressNumber = "";
    private string complement = "";
    private string neighborhood = "";
    private string city = "";
    private string state = "";
    private PatientAccount? selectedPatient;
    private QueueEntry? selectedQueue;
    private MunicipalSummary? summary;
    private int lightningPeriodDays;
    private DateTimeOffset? summaryReceivedAt;
    private string section = "home";
    private string patientSearch = "";
    private string slotLabel = "";
    private string patientPresenceFilter = "Todos";
    private string patientSort = "Mais recentes";
    private string agendaViewMode = "Semana";
    private DateTime selectedAgendaDate = DateTime.Today;
    private readonly DispatcherTimer refreshTimer;
    private bool disposed;
    private bool refreshFailed;
    private bool automaticRefresh;
    public WorkspaceViewModel(DesktopProfile profile, NoraApiClient? api = null)
    {
        this.profile = profile;
        this.api = api ?? new();
        refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
        refreshTimer.Tick += OnRefreshTimer;
        endpoint = Environment.GetEnvironmentVariable("NORA_API_URL") ?? "";
        ClearSelectionCommand = new RelayCommand(_ => ClearActionSelection());
        RefreshCommand = new RelayCommand(async _ => await RunAsync(async () =>
        {
            await RefreshAsync();
            Message = "Dados atualizados pela API.";
        }), _ => CanOperate);
        LogoutCommand = new RelayCommand(async _ => await RunAsync(LogoutAsync), _ => CanOperate);
        EnqueueCommand = new RelayCommand(async _ => await RunAsync(async () => { await this.api.EnqueueAsync(SelectedPatient!.Id); await RefreshAfterChangeAsync("Paciente entrou na fila por ordem de entrada."); }), _ => CanOperate && IsOperational && SelectedPatient?.IsActive == true);
        CancelQueueCommand = new RelayCommand(async _ => await RunAsync(async () => { await this.api.CancelAsync(SelectedQueue!.Id); await RefreshAfterChangeAsync("Entrada da fila cancelada."); }), _ => CanOperate && IsOperational && SelectedQueue?.Status is "waiting" or "offered");
        CreateSlotCommand = new RelayCommand(async _ => await RunAsync(async () => { await this.api.CreateSlotAsync(SlotLabel.Trim()); SlotLabel = ""; await RefreshAfterChangeAsync("Vaga aberta. A oferta segue a ordem da fila, com 10 minutos para resposta."); }), _ => CanOperate && IsOperational && !string.IsNullOrWhiteSpace(SlotLabel));
        LookupCepCommand = new RelayCommand(async _ => await LookupCepAsync(), _ => CanLookupCep);
        SelectAgendaDayCommand = new RelayCommand(_ => AgendaViewMode = "Dia");
        SelectAgendaWeekCommand = new RelayCommand(_ => AgendaViewMode = "Semana");
        SelectCalendarDayCommand = new RelayCommand(day => SelectCalendarDay(day as AgendaCalendarDay));
        BuildAgendaDays();
    }
    public string Endpoint { get => endpoint; set { endpoint = value; Notify(); } }
    public string Identifier { get => identifier; set { identifier = value; Notify(); } }
    public string FullName { get; set; } = "";
    public string Cpf { get; set; } = "";
    public string Email { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Cep { get => cep; set { cep = value; Notify(); Notify(nameof(CanLookupCep)); CommandManager.InvalidateRequerySuggested(); } }
    public string Street { get => street; set { street = value; Notify(); } }
    public string AddressNumber { get => addressNumber; set { addressNumber = value; Notify(); } }
    public string Complement { get => complement; set { complement = value; Notify(); } }
    public string Neighborhood { get => neighborhood; set { neighborhood = value; Notify(); } }
    public string City { get => city; set { city = value; Notify(); } }
    public string State { get => state; set { state = value; Notify(); } }
    public string SlotLabel { get => slotLabel; set { slotLabel = value; Notify(); CommandManager.InvalidateRequerySuggested(); } }
    public bool IsBusy { get => busy; private set { busy = value; Notify(); Notify(nameof(CanOperate)); Notify(nameof(CanLogin)); Notify(nameof(CanEditDraft)); Notify(nameof(CanLookupCep)); Notify(nameof(CanSetPatientActive)); Notify(nameof(CanEditUnit)); Notify(nameof(CanReadUnitRegistry)); CommandManager.InvalidateRequerySuggested(); } }
    public bool IsAuthenticated => user != null;
    public bool IsSignedOut => !IsAuthenticated;
    public bool CanOperate => IsAuthenticated && !IsBusy;
    public bool CanEditDraft => IsAuthenticated && (!IsBusy || automaticRefresh);
    public bool CanLogin => !IsBusy && !IsAuthenticated;
    public bool CanLookupCep => CanEditDraft && BrazilianDataValidator.IsCompleteCep(Cep);
    public bool IsOperational => !profile.IsMunicipal;
    public bool IsMunicipal => profile.IsMunicipal;
    public string AccountName => user?.FullName ?? "Conta desconectada";
    public string AccountInitials
    {
        get
        {
            var names = AccountName.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return names.Length switch { 0 => "N", 1 => names[0][..1].ToUpperInvariant(), _ => (names[0][..1] + names[^1][..1]).ToUpperInvariant() };
        }
    }
    public string UnitLabel => IsMunicipal ? (IsAuthenticated ? "Gestão conectada" : "Gestão desconectada") : (IsAuthenticated ? "Dados da unidade conectada" : "Unidade não conectada");
    public bool ShowMunicipal => IsMunicipal && (IsAuthenticated || Section != "home");
    public string ConnectionLabel => IsAuthenticated ? "Conta conectada" : "Conta desconectada";
    public string Message { get => message; private set { message = value; Notify(); Notify(nameof(HasMessage)); } }
    public bool HasMessage => !string.IsNullOrWhiteSpace(Message)
        && Message != "Conectando à API…"
        && Message != "Dados atualizados pela API."
        && Message != "Conta conectada. Dados atualizados pela API.";
    public string Section { get => section; set { section = value; SelectListItem(null); Notify(); NotifySections(); } }
    public bool ShowDashboard => IsAuthenticated && IsOperational && Section == "home";
    public bool ShowPatients => IsAuthenticated && IsOperational && Section == "patients";
    public bool ShowQueue => IsAuthenticated && IsOperational && Section == "queue";
    public bool ShowAgenda => IsOperational && Section == "agenda";
    public bool ShowSlots => IsAuthenticated && IsOperational && Section == "slots";
    public bool ShowIndicators => IsOperational && Section == "indicators";
    public bool ShowAlerts => IsOperational && Section == "alerts";
    public bool RequiresSignInForCurrentSection => IsOperational && IsSignedOut && Section is "queue" or "patients" or "slots";
    public string AccessRequiredTitle => Section switch
    {
        "queue" => "Fila da unidade",
        "patients" => "Pacientes da unidade",
        "slots" => "Vagas Relâmpago",
        _ => "Área da unidade"
    };
    public string AccessRequiredDescription => Section switch
    {
        "queue" => "Acompanhe senhas, posições, tempo de espera e o atendimento da fila.",
        "patients" => "Pesquise e consulte apenas os dados necessários para o trabalho da sua UBS.",
        "slots" => "Libere e acompanhe oportunidades reaproveitadas para Vaga Relâmpago.",
        _ => "Entre na unidade para acessar os dados protegidos."
    };
    public string AccessRequiredHint => Section switch
    {
        "queue" => "Entre no Painel da UBS para carregar a fila protegida da sua unidade.",
        "patients" => "Entre no Painel da UBS para carregar os pacientes autorizados da sua unidade.",
        "slots" => "Entre no Painel da UBS para carregar as vagas e seus estados.",
        _ => "Entre no Painel da UBS para continuar."
    };
    public bool IsIndicatorsPage => ShowIndicators;
    public bool IsAlertsPage => ShowAlerts;
    public string PageTitle => Section == "alerts" ? "Central de alertas" : "Indicadores da UBS";
    public string PageSubtitle => Section == "alerts"
        ? "Acompanhe alertas de fila, atrasos e disponibilidade da agenda."
        : "Tempo de espera, produção, comparecimento e uso da agenda da sua unidade.";
    public string PatientTotalCount => HasActiveObsUnit ? ObsRegistry.Count.ToString() : "—";
    public string QueueWaitingCount => HasActiveObsUnit && unitQueueLoaded ? UnitWaitingList.Count(entry => entry.Status is "waiting" or "offered").ToString() : "—";
    public string SlotOpenCount => HasActiveObsUnit && unitSlotsLoaded ? UnitLightningSlots.Count(entry => entry.Status is "open" or "offered").ToString() : "—";
    public string AgendaViewMode { get => agendaViewMode; private set { if (agendaViewMode == value) return; agendaViewMode = value; ClearAgendaList(); Notify(); Notify(nameof(IsAgendaDay)); Notify(nameof(IsAgendaWeek)); Notify(nameof(AgendaRangeLabel)); } }
    public bool IsAgendaDay => AgendaViewMode == "Dia";
    public bool IsAgendaWeek => AgendaViewMode == "Semana";
    public string AgendaRangeLabel => IsAgendaDay ? selectedAgendaDate.ToString("dddd, dd 'de' MMMM") : $"Semana de {StartOfWeek(selectedAgendaDate):dd/MM} a {StartOfWeek(selectedAgendaDate).AddDays(6):dd/MM}";
    public string SelectedAgendaDateLabel => selectedAgendaDate.ToString("dddd, dd 'de' MMMM 'de' yyyy");
    public ObservableCollection<AgendaCalendarDay> AgendaDays { get; } = [];
    public ObservableCollection<PatientAccount> Patients { get; } = [];
    public string PatientSearch { get => patientSearch; set { patientSearch = value; Notify(); NotifyPatientFilters(); } }
    public string PatientPresenceFilter { get => patientPresenceFilter; set { patientPresenceFilter = value; Notify(); NotifyPatientFilters(); } }
    public string PatientSort { get => patientSort; set { patientSort = value; Notify(); NotifyPatientFilters(); } }
    public IEnumerable<PatientAccount> FilteredPatients
    {
        get
        {
            var search = PatientSearch.Trim();
            var matches = Patients.Where(p => (p.FullName.Contains(search, StringComparison.CurrentCultureIgnoreCase)
                || p.Email.Contains(search, StringComparison.OrdinalIgnoreCase)
                || p.Cpf.Contains(search, StringComparison.Ordinal)
                || p.FormattedCpf.Contains(search, StringComparison.Ordinal)
                || (p.Phone?.Contains(search, StringComparison.Ordinal) ?? false))
                && (PatientPresenceFilter == "Todos" || p.IsOnline == (PatientPresenceFilter == "Online"))
                && (PatientStatusFilter == "Todos" || p.IsActive == (PatientStatusFilter == "Ativos")));
            return PatientSort switch
            {
                "Nome A–Z" => matches.OrderBy(p => p.FullName, StringComparer.CurrentCultureIgnoreCase),
                "Nome Z–A" => matches.OrderByDescending(p => p.FullName, StringComparer.CurrentCultureIgnoreCase),
                _ => matches
            };
        }
    }
    private void NotifyPatientFilters()
    {
        var patientId = SelectedPatient?.Id;
        var previousAction = actionSelection;
        Notify(nameof(FilteredPatients)); Notify(nameof(HasNoPatientResults));
        SelectedPatient = FilteredPatients.FirstOrDefault(item => item.Id == patientId);
        if (previousAction is PatientAccount) SelectListItem(CurrentActionItem(previousAction));
    }
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
    public string RegisteredSlotsCount => summary?.RegisteredSlots?.ToString() ?? "—";
    public string CreatedOffersCount => summary?.CreatedOffers?.ToString() ?? "—";
    public string DeclinedOffersCount => summary?.DeclinedOffers?.ToString() ?? "—";
    public string ExpiredOffersCount => summary?.ExpiredOffers?.ToString() ?? "—";
    public string PendingOffersCount => summary?.PendingOffers?.ToString() ?? "—";
    public string CancelledOffersCount => summary?.CancelledOffers?.ToString() ?? "—";
    public int LightningPeriodDays
    {
        get => lightningPeriodDays;
        set
        {
            var next = value is 0 or 7 or 30 or 90 ? value : 0;
            if (lightningPeriodDays == next) return;
            lightningPeriodDays = next; Notify(); NotifyLightning();
        }
    }
    public bool CanFilterLightning => IsAuthenticated && summary?.LightningCohorts is { Length: > 0 };
    private LightningCohort? CurrentLightningCohort => summary?.LightningCohorts?.FirstOrDefault(c => c.Days == LightningPeriodDays);
    public string LightningOpen => LightningPeriodDays == 0 ? SlotCount : CurrentLightningCohort?.OpenSlots.ToString() ?? "—";
    public string LightningAccepted => LightningPeriodDays == 0 ? AcceptedCount : CurrentLightningCohort?.AcceptedOffers.ToString() ?? "—";
    public string LightningRegistered => LightningPeriodDays == 0 ? RegisteredSlotsCount : CurrentLightningCohort?.RegisteredSlots.ToString() ?? "—";
    public string LightningCreated => LightningPeriodDays == 0 ? CreatedOffersCount : CurrentLightningCohort?.CreatedOffers.ToString() ?? "—";
    public string LightningDeclined => LightningPeriodDays == 0 ? DeclinedOffersCount : CurrentLightningCohort?.DeclinedOffers.ToString() ?? "—";
    public string LightningExpired => LightningPeriodDays == 0 ? ExpiredOffersCount : CurrentLightningCohort?.ExpiredOffers.ToString() ?? "—";
    public string LightningPending => LightningPeriodDays == 0 ? PendingOffersCount : CurrentLightningCohort?.PendingOffers.ToString() ?? "—";
    public string LightningCancelled => LightningPeriodDays == 0 ? CancelledOffersCount : CurrentLightningCohort?.CancelledOffers.ToString() ?? "—";
    private void NotifyLightning()
    {
        foreach (var name in new[] { nameof(CanFilterLightning), nameof(LightningOpen), nameof(LightningAccepted), nameof(LightningRegistered), nameof(LightningCreated), nameof(LightningDeclined), nameof(LightningExpired), nameof(LightningPending), nameof(LightningCancelled) }) Notify(name);
    }
    public bool CanExportSummary => IsAuthenticated && summary != null;
    public string SummaryReceivedLabel => summaryReceivedAt is { } time ? $"Recebido em {time.ToLocalTime():dd/MM/yyyy HH:mm:ss}" : "Nenhum resumo carregado";
    public string CreateSummaryCsv()
    {
        if (!CanExportSummary || summary is null || summaryReceivedAt is null) throw new InvalidOperationException("Nenhum resumo disponível para exportar.");
        return Nora.Desktop.Services.MunicipalCsv.Create(summary, summaryReceivedAt.Value, LightningPeriodDays, appliedSummaryUnitId);
    }
    private void NotifySummary()
    {
        NotifyLightning();
        foreach (var name in new[] { nameof(PatientCount), nameof(WaitingCount), nameof(SlotCount), nameof(AcceptedCount), nameof(RegisteredSlotsCount), nameof(CreatedOffersCount), nameof(DeclinedOffersCount), nameof(ExpiredOffersCount), nameof(PendingOffersCount), nameof(CancelledOffersCount), nameof(CanExportSummary), nameof(SummaryReceivedLabel) }) Notify(name);
    }
    public ICommand RefreshCommand { get; }
    public ICommand LogoutCommand { get; }
    public ICommand EnqueueCommand { get; }
    public ICommand CancelQueueCommand { get; }
    public ICommand CreateSlotCommand { get; }
    public ICommand LookupCepCommand { get; }
    public ICommand SelectAgendaDayCommand { get; }
    public ICommand SelectAgendaWeekCommand { get; }
    public ICommand SelectCalendarDayCommand { get; }

    private static DateTime StartOfWeek(DateTime date)
    {
        var offset = ((int)date.DayOfWeek + 6) % 7;
        return date.Date.AddDays(-offset);
    }

    private void BuildAgendaDays()
    {
        AgendaDays.Clear();
        var first = new DateTime(selectedAgendaDate.Year, selectedAgendaDate.Month, 1);
        var start = StartOfWeek(first);
        var last = first.AddMonths(1).AddDays(-1);
        var daysInGrid = (StartOfWeek(last).AddDays(6) - start).Days + 1;
        for (var offset = 0; offset < daysInGrid; offset++)
        {
            var date = start.AddDays(offset);
            AgendaDays.Add(new AgendaCalendarDay(date, date.Month == first.Month) { IsSelected = date == selectedAgendaDate });
        }
    }

    private void SelectCalendarDay(AgendaCalendarDay? day)
    {
        if (day == null) return;
        var sameMonth = day.Date.Month == selectedAgendaDate.Month && day.Date.Year == selectedAgendaDate.Year;
        selectedAgendaDate = day.Date;
        if (sameMonth) foreach (var item in AgendaDays) item.IsSelected = item == day;
        else BuildAgendaDays();
        ClearAgendaList();
        AgendaViewMode = "Dia";
        Notify(nameof(AgendaRangeLabel));
        Notify(nameof(SelectedAgendaDateLabel));
    }

    public Task LoginAsync(string password) => RunAsync(async () =>
    {
        if (string.IsNullOrWhiteSpace(Identifier) || string.IsNullOrWhiteSpace(password)) throw new NoraApiException("Informe seu CPF ou e-mail e sua senha.");
        if (!BrazilianDataValidator.IsValidLoginIdentifier(Identifier)) throw new NoraApiException("Informe um CPF válido ou um e-mail válido para entrar.");
        api.Configure(Endpoint);
        var session = await api.LoginAsync(profile.Id, Identifier.Trim(), password);
        user = session.User;
        NotifySession();
        refreshTimer.Start();
        try { await RefreshAsync(); }
        catch (NoraApiException) { refreshFailed = true; throw; }
        Message = "Conta conectada. Dados atualizados pela API.";
    });
    public Task RegisterPatientAsync(string password) => RunAsync(async () =>
    {
        if (!IsOperational || !IsAuthenticated) throw new NoraApiException("Entre no OBS para cadastrar pacientes.");
        if (string.IsNullOrWhiteSpace(FullName) || string.IsNullOrWhiteSpace(Cpf) || string.IsNullOrWhiteSpace(Email) || string.IsNullOrWhiteSpace(password))
            throw new NoraApiException("Preencha nome completo, CPF, e-mail e senha inicial do paciente.");
        if (!BrazilianDataValidator.IsValidCpf(Cpf)) throw new NoraApiException("O CPF do paciente é inválido. Confira os 11 dígitos e os verificadores.");
        var address = CreateAddressDraft();
        var patient = await api.RegisterPatientAsync(FullName.Trim(), BrazilianDataValidator.NormalizeCpf(Cpf), Email.Trim(), password, string.IsNullOrWhiteSpace(Phone) ? null : Phone.Trim(), address);
        ClearPatientDraft();
        await RefreshAfterChangeAsync("Paciente cadastrado. A conta já pode entrar no APPNORA com CPF ou e-mail.");
        SelectedPatient = Patients.FirstOrDefault(p => p.Id == patient.Id);
    });

    private Task LookupCepAsync() => RunAsync(async () =>
    {
        if (!BrazilianDataValidator.IsCompleteCep(Cep)) throw new NoraApiException("Informe um CEP com 8 dígitos para pesquisar o endereço.");
        var address = await api.LookupCepAsync(Cep);
        Cep = address.Cep;
        Street = address.Street;
        Complement = address.Complement ?? "";
        Neighborhood = address.Neighborhood ?? "";
        City = address.City;
        State = address.State;
        Message = "Endereço preenchido a partir do CEP. Confira o número antes de cadastrar.";
    });

    private AddressInfo? CreateAddressDraft()
    {
        var hasAddress = new[] { Cep, Street, AddressNumber, Complement, Neighborhood, City, State }.Any(value => !string.IsNullOrWhiteSpace(value));
        if (!hasAddress) return null;
        if (!BrazilianDataValidator.IsCompleteCep(Cep) || string.IsNullOrWhiteSpace(Street) || string.IsNullOrWhiteSpace(City) || State.Trim().Length != 2)
            throw new NoraApiException("Para salvar endereço, pesquise um CEP válido e confira logradouro, cidade e UF.");
        return new(BrazilianDataValidator.NormalizeCep(Cep), Street.Trim(), EmptyToNull(AddressNumber), EmptyToNull(Complement), EmptyToNull(Neighborhood), City.Trim(), State.Trim().ToUpperInvariant());
    }

    private static string? EmptyToNull(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private void ClearPatientDraft()
    {
        FullName = Cpf = Email = Phone = Cep = Street = AddressNumber = Complement = Neighborhood = City = State = "";
        foreach (var name in new[] { nameof(FullName), nameof(Cpf), nameof(Email), nameof(Phone), nameof(Cep), nameof(Street), nameof(AddressNumber), nameof(Complement), nameof(Neighborhood), nameof(City), nameof(State) }) Notify(name);
    }
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
        var patientsChanged = false;
        if (IsMunicipal)
        {
            var result = await api.SummaryAsync(appliedSummaryUnitId);
            if (disposed) return;
            summary = result;
            if (LightningPeriodDays != 0 && CurrentLightningCohort == null) LightningPeriodDays = 0;
            summaryReceivedAt = DateTimeOffset.Now;
            NotifySummary();
        }
        else
        {
            var unit = ActiveObsUnit;
            if (unit is not null)
            {
                var registry = await api.ObsRegistryAsync(unit.Id);
                var queue = await api.UnitWaitingListAsync(unit.Id);
                var slots = await api.UnitLightningSlotsAsync(unit.Id);
                var today = DateTime.Today;
                var agendaToday = await api.AppointmentsAsync(unit.Id, new DateTimeOffset(today), new DateTimeOffset(today.AddDays(1)));
                if (disposed || !IsAuthenticated || ActiveObsUnit?.Id != unit.Id) return;
                SetTodayOverview(today, agendaToday);
                var selectedCpf = SelectedObsRegistry?.Cpf; var correctedEmail = CorrectedObsEmail; var waitingCpf = WaitingPatient?.Cpf; var appointmentCpf = AppointmentPatient?.Cpf; var entryId = SelectedWaitingEntry?.Id;
                Replace(ObsRegistry, registry); Notify(nameof(FilteredObsRegistry)); Notify(nameof(ActiveObsRegistry)); Replace(UnitWaitingList, queue); Replace(UnitLightningSlots, slots); unitQueueLoaded = unitSlotsLoaded = true;
                SelectedObsRegistry = ObsRegistry.FirstOrDefault(p => p.Cpf == selectedCpf);
                if (SelectedObsRegistry is not null) CorrectedObsEmail = correctedEmail;
                WaitingPatient = ObsRegistry.FirstOrDefault(p => p.Cpf == waitingCpf);
                AppointmentPatient = ObsRegistry.FirstOrDefault(p => p.Cpf == appointmentCpf);
                SelectedWaitingEntry = UnitWaitingList.FirstOrDefault(entry => entry.Id == entryId);
            }
        }
        foreach (var name in new[] { nameof(PatientCount), nameof(WaitingCount), nameof(SlotCount), nameof(AcceptedCount), nameof(PatientTotalCount), nameof(QueueWaitingCount), nameof(SlotOpenCount), nameof(HasNoPatients), nameof(HasNoPatientResults), nameof(HasNoQueue), nameof(HasNoSlots) }) Notify(name);
        if (patientsChanged) Notify(nameof(FilteredPatients));
    }
    private async Task RefreshAfterChangeAsync(string successMessage)
    {
        try { await RefreshAsync(); Message = successMessage; }
        catch (NoraApiException error) when (!error.Unauthorized)
        {
            refreshFailed = true;
            Message = successMessage + " Não foi possível atualizar a tela. Aguarde a atualização automática antes de repetir a operação.";
        }
    }
    private async Task RunAsync(Func<Task> operation)
    {
        if (IsBusy || disposed) return;
        IsBusy = true;
        Message = "Conectando à API…";
        try { await operation(); }
        catch (NoraApiException error) { if (error.Unauthorized && IsAuthenticated) SignOut(); Message = error.Message; }
        finally { IsBusy = false; }
    }
    private static bool Replace<T>(ObservableCollection<T> destination, IEnumerable<T> source)
    {
        var items = source.ToArray();
        if (destination.SequenceEqual(items)) return false;
        destination.Clear();
        foreach (var item in items) destination.Add(item);
        return true;
    }
    private async void OnRefreshTimer(object? sender, EventArgs args) => await RefreshAutomaticallyAsync();
    public async Task RefreshAutomaticallyAsync()
    {
        if (!CanOperate || disposed) return;
        automaticRefresh = true;
        IsBusy = true;
        try
        {
            await RefreshAsync();
            if (refreshFailed) Message = "";
            refreshFailed = false;
        }
        catch (NoraApiException error)
        {
            if (disposed) return;
            if (error.Unauthorized) SignOut();
            Message = error.Message;
            refreshFailed = true;
        }
        finally { automaticRefresh = false; IsBusy = false; }
    }
    private void SignOut()
    {
        refreshTimer.Stop();
        api.ClearSession(); user = null; summary = null; summaryReceivedAt = null; LightningPeriodDays = 0;
        NotifySummary();
        ClearObsUnits(); ClearStaff(); ClearUnits();
        Patients.Clear(); Queue.Clear(); Slots.Clear(); SelectedPatient = null; SelectedQueue = null; SelectedSlot = null;
        SelectListItem(null);
        Identifier = SlotLabel = "";
        ClearPatientDraft();
        PatientSearch = "";
        PatientPresenceFilter = "Todos"; PatientSort = "Mais recentes"; PatientStatusFilter = "Todos";
        foreach (var name in new[] { nameof(PatientCount), nameof(WaitingCount), nameof(SlotCount), nameof(AcceptedCount), nameof(SlotLabel) }) Notify(name);
        NotifySession();
    }
    private void NotifySession()
    {
        foreach (var name in new[] { nameof(IsAuthenticated), nameof(IsSignedOut), nameof(CanOperate), nameof(CanLogin), nameof(CanEditDraft), nameof(CanSetPatientActive), nameof(CanEditUnit), nameof(AccountName), nameof(AccountInitials), nameof(UnitLabel), nameof(ConnectionLabel), nameof(UnitsState) }) Notify(name);
        NotifySections();
    }
    private void NotifySections()
    {
        foreach (var name in new[] { nameof(ShowMunicipal), nameof(ShowDashboard), nameof(ShowPatients), nameof(ShowQueue), nameof(ShowAgenda), nameof(ShowSlots), nameof(ShowIndicators), nameof(ShowAlerts), nameof(RequiresSignInForCurrentSection), nameof(AccessRequiredTitle), nameof(AccessRequiredDescription), nameof(AccessRequiredHint), nameof(IsIndicatorsPage), nameof(IsAlertsPage), nameof(PageTitle), nameof(PageSubtitle) }) Notify(name);
    }
    public void Dispose() { disposed = true; refreshTimer.Tick -= OnRefreshTimer; SignOut(); api.Dispose(); }
}
