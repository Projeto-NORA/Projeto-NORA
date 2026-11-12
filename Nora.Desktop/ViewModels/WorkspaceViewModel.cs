using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Threading;
using Nora.Desktop.Generics;
using Nora.Desktop.Models;
using Nora.Desktop.Services;

namespace Nora.Desktop.ViewModels;

/// <summary>
/// Estado do espaço de trabalho. Partes por assunto: Messages (faixa de retorno), ObsUnits (UBS e cadastro),
/// UnitData (leitura por UBS), Appointments (agenda), Lightning (fila e vagas), Listings (fichas), Units/Staff (Secretaria).
/// </summary>
public sealed partial class WorkspaceViewModel : ObservableObject, IDisposable
{
    private readonly DesktopProfile profile;
    private readonly NoraApiClient api;
    // Uma operação por vez: ação do usuário, ciclo automático e carga de seção entram em fila em vez de se descartarem.
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly DispatcherTimer refreshTimer;
    private AccountUser? user;
    private string endpoint;
    private string identifier = "";
    private MunicipalSummary? summary;
    private int lightningPeriodDays;
    private DateTimeOffset? summaryReceivedAt;
    private string section = "home";
    private bool userBusy, disposed;
    private int backgroundOperations;
    private static readonly string[] BusyDependents =
    [
        nameof(IsBusy), nameof(ShowProgress), nameof(CanOperate), nameof(CanLogin), nameof(CanEditDraft), nameof(CanEditUnit), nameof(CanReadUnitRegistry),
        nameof(CanCallAppointment), nameof(CanCompleteAppointment), nameof(CanMissAppointment), nameof(CanCancelAppointment), nameof(CanCreateLightning),
        nameof(CanCancelWaitingEntry), nameof(CanConfirmObsUnit)
    ];
    private static readonly string[] SessionDependents =
    [
        nameof(IsAuthenticated), nameof(IsSignedOut), nameof(CanOperate), nameof(CanLogin), nameof(CanEditDraft), nameof(CanEditUnit), nameof(AccountName), nameof(AccountInitials),
        nameof(AccountRoleLabel), nameof(UnitLabel), nameof(ConnectionLabel), nameof(UnitsState), nameof(ObsUnitPrompt), nameof(NeedsObsUnitChoice), nameof(CanConfirmObsUnit)
    ];
    private static readonly string[] SectionDependents =
    [
        nameof(ShowMunicipal), nameof(ShowDashboard), nameof(ShowPatients), nameof(ShowQueue), nameof(ShowAgenda), nameof(ShowSlots), nameof(ShowIndicators), nameof(ShowAlerts),
        nameof(RequiresSignInForCurrentSection), nameof(AccessRequiredTitle), nameof(AccessRequiredDescription), nameof(AccessRequiredHint), nameof(IsIndicatorsPage), nameof(IsAlertsPage),
        nameof(PageTitle), nameof(PageSubtitle)
    ];

    public WorkspaceViewModel(DesktopProfile profile, NoraApiClient? api = null)
    {
        this.profile = profile;
        this.api = api ?? new();
        refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
        refreshTimer.Tick += OnRefreshTimer;
        endpoint = Environment.GetEnvironmentVariable("NORA_API_URL") ?? "";
        RefreshCommand = new RelayCommand(async _ => await RunAsync(async () =>
        {
            await RefreshAsync(ScopeFor(Section) | UnitScope.Everything);
            MarkFresh();
            Say(MessageKind.Success, "Dados atualizados pela API.");
        }), _ => CanOperate);
        LogoutCommand = new RelayCommand(async _ => await RunAsync(LogoutAsync), _ => CanOperate);
        DismissMessageCommand = new RelayCommand(_ => ClearMessage());
        SelectAgendaDayCommand = new RelayCommand(_ => AgendaViewMode = "Dia");
        SelectAgendaWeekCommand = new RelayCommand(_ => AgendaViewMode = "Semana");
        SelectCalendarDayCommand = new RelayCommand(day => SelectCalendarDay(day as AgendaCalendarDay));
        BuildAgendaDays();
    }

    public string Endpoint { get => endpoint; set { endpoint = value; Notify(); } }
    public string Identifier { get => identifier; set { identifier = value; Notify(); } }
    /// <summary>Há trabalho em andamento: ação do usuário, ciclo automático ou carga de seção.</summary>
    public bool IsBusy => userBusy || backgroundOperations > 0;
    /// <summary>A barra de progresso só acompanha ação pedida pelo usuário; a atualização automática é silenciosa.</summary>
    public bool ShowProgress => userBusy;
    public bool IsAuthenticated => user != null;
    public bool IsSignedOut => !IsAuthenticated;
    /// <summary>Botões e formulários só ficam bloqueados por ação do usuário; a atualização automática não os desabilita.</summary>
    public bool CanOperate => IsAuthenticated && !userBusy;
    public bool CanEditDraft => CanOperate;
    public bool CanLogin => !userBusy && !IsAuthenticated;
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
    public string AccountRoleLabel => user?.Role switch
    {
        null => "Sem sessão ativa",
        "administrator" => "Administrador",
        "attendant" => "Atendente",
        "manager" => "Gestão",
        var other => other
    };
    public string UnitLabelTitle => IsMunicipal ? "Escopo da conta" : "Unidade ativa";
    public string UnitLabel => IsMunicipal
        ? (IsAuthenticated ? "Gestão conectada" : "Gestão desconectada")
        : !IsAuthenticated ? "Unidade não conectada" : ActiveObsUnit?.Name ?? "Nenhuma unidade selecionada";
    public bool ShowMunicipal => IsMunicipal && (IsAuthenticated || Section != "home");
    public string ConnectionLabel => IsAuthenticated ? "Conta conectada" : "Conta desconectada";
    public string Section
    {
        get => section;
        set
        {
            if (section == value) return;
            section = value; ClearTransientMessage(); Notify(); NotifySections(); OnSectionEntered();
        }
    }
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
        "queue" => "Acompanhe posição, tempo de espera e a situação de cada entrada da fila.",
        "patients" => "Pesquise e consulte apenas os dados necessários para o trabalho da sua UBS.",
        "slots" => "Acompanhe as oportunidades reaproveitadas como Vaga Relâmpago.",
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
    public string QueueWaitingCount => HasActiveObsUnit && unitQueueLoaded ? UnitWaitingList.Count(entry => entry.IsActive).ToString() : "—";
    public string SlotOpenCount => HasActiveObsUnit && unitSlotsLoaded ? UnitLightningSlots.Count(entry => entry.Status is "open" or "offered").ToString() : "—";

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
    private static readonly string[] LightningNames =
        [nameof(CanFilterLightning), nameof(LightningOpen), nameof(LightningAccepted), nameof(LightningRegistered), nameof(LightningCreated), nameof(LightningDeclined), nameof(LightningExpired), nameof(LightningPending), nameof(LightningCancelled)];
    private static readonly string[] SummaryNames =
        [nameof(PatientCount), nameof(WaitingCount), nameof(SlotCount), nameof(AcceptedCount), nameof(RegisteredSlotsCount), nameof(CreatedOffersCount), nameof(DeclinedOffersCount), nameof(ExpiredOffersCount), nameof(PendingOffersCount), nameof(CancelledOffersCount), nameof(CanExportSummary), nameof(SummaryReceivedLabel)];
    private void NotifyLightning() { foreach (var name in LightningNames) Notify(name); }
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
        foreach (var name in SummaryNames) Notify(name);
    }
    public ICommand RefreshCommand { get; }
    public ICommand LogoutCommand { get; }
    public ICommand DismissMessageCommand { get; }
    public ICommand SelectAgendaDayCommand { get; }
    public ICommand SelectAgendaWeekCommand { get; }
    public ICommand SelectCalendarDayCommand { get; }

    public Task LoginAsync(string password) => RunAsync(async () =>
    {
        if (string.IsNullOrWhiteSpace(Identifier) || string.IsNullOrWhiteSpace(password)) throw new NoraApiException("Informe seu CPF ou e-mail e sua senha.");
        if (!BrazilianDataValidator.IsValidLoginIdentifier(Identifier)) throw new NoraApiException("Informe um CPF válido ou um e-mail válido para entrar.");
        api.Configure(Endpoint);
        var session = await api.LoginAsync(profile.Id, Identifier.Trim(), password);
        user = session.User;
        NotifySession();
        refreshTimer.Start();
        try
        {
            if (IsMunicipal) await LoadSummaryAsync();
            else await InitializeObsUnitsAsync();
            MarkFresh();
        }
        catch (NoraApiException error) when (!error.Unauthorized) { RegisterRefreshFailure(error.Message, show: false); throw; }
    });

    private async Task LogoutAsync()
    {
        try { await api.LogoutAsync(); }
        catch (NoraApiException error)
        {
            if (!error.Unauthorized) throw new NoraApiException("Você saiu deste aplicativo, mas a API não confirmou a revogação da sessão. A sessão no servidor permanece até expirar.");
        }
        finally { SignOut(); }
        Say(MessageKind.Success, "Você saiu da conta.");
    }

    private async Task LoadSummaryAsync()
    {
        var result = await api.SummaryAsync(appliedSummaryUnitId);
        if (disposed) return;
        summary = result;
        if (LightningPeriodDays != 0 && CurrentLightningCohort == null) LightningPeriodDays = 0;
        summaryReceivedAt = DateTimeOffset.Now;
        NotifySummary();
    }

    /// <summary>Lê o que a seção atual precisa. Municipal: resumo da rede. OBS: dados da UBS ativa, ou a lista de UBS quando ela ainda não veio.</summary>
    private async Task RefreshAsync(UnitScope scope)
    {
        if (IsMunicipal) { await LoadSummaryAsync(); return; }
        if (ActiveObsUnit is { } unit) { if (scope != UnitScope.None) await LoadUnitScopeAsync(unit, scope); return; }
        if (!ObsUnitsLoaded) await InitializeObsUnitsAsync();
    }

    /// <summary>Ação do usuário: uma por vez, aguarda o ciclo automático em andamento e depois executa; nunca é descartada em silêncio.</summary>
    private async Task RunAsync(Func<Task> operation)
    {
        if (userBusy || disposed) return;
        SetUserBusy(true);
        ClearMessage();
        try
        {
            await gate.WaitAsync();
            try
            {
                if (disposed) return;
                await operation();
            }
            catch (NoraApiException error) { if (error.Unauthorized && IsAuthenticated) SignOut(); Say(MessageKind.Error, error.Message); }
            finally { gate.Release(); }
        }
        finally { SetUserBusy(false); }
    }

    /// <summary>Atualização automática ou carga de seção: silenciosa, sem barra de progresso. Falhas viram aviso único e dados marcados como desatualizados.</summary>
    private async Task RunBackgroundAsync(Func<Task> work, bool skipIfBusy = false)
    {
        if (disposed || !IsAuthenticated || (skipIfBusy && (userBusy || gate.CurrentCount == 0))) return;
        backgroundOperations++; Notify(nameof(IsBusy));
        try
        {
            await gate.WaitAsync();
            try
            {
                if (disposed || !IsAuthenticated) return;
                await work();
                if (disposed) return;
                RecoverFromRefreshFailure();
                MarkFresh();
            }
            catch (NoraApiException error)
            {
                if (disposed) return;
                if (error.Unauthorized) { SignOut(); Say(MessageKind.Error, error.Message); }
                else RegisterRefreshFailure(error.Message, show: true);
            }
            finally { gate.Release(); }
        }
        finally { backgroundOperations--; if (!disposed) Notify(nameof(IsBusy)); }
    }

    private async void StartBackground(Func<Task> work) => await RunBackgroundAsync(work);
    private void SetUserBusy(bool value)
    {
        userBusy = value;
        if (disposed) return;
        foreach (var name in BusyDependents) Notify(name);
        CommandManager.InvalidateRequerySuggested();
    }

    private async void OnRefreshTimer(object? sender, EventArgs args) => await RefreshAutomaticallyAsync();
    public Task RefreshAutomaticallyAsync() => RunBackgroundAsync(() => RefreshAsync(ScopeFor(Section)), skipIfBusy: true);

    private bool sectionLoadPending;
    private void OnSectionEntered()
    {
        if (disposed || !IsAuthenticated) return;
        if (IsMunicipal || ScopeFor(Section) == UnitScope.None) return;
        sectionLoadPending = true;
        StartBackground(() =>
        {
            if (!sectionLoadPending) return Task.CompletedTask;
            sectionLoadPending = false;
            return RefreshAsync(ScopeFor(Section));
        });
    }

    private void SignOut()
    {
        refreshTimer.Stop();
        api.ClearSession(); user = null; summary = null; summaryReceivedAt = null; LightningPeriodDays = 0;
        ClearRefreshState();
        NotifySummary();
        ClearObsUnits(); ClearStaff(); ClearUnits();
        Identifier = "";
        NotifySession();
    }
    private void NotifySession()
    {
        foreach (var name in SessionDependents) Notify(name);
        NotifySections();
    }
    private void NotifySections() { foreach (var name in SectionDependents) Notify(name); }
    public void Dispose() { disposed = true; refreshTimer.Tick -= OnRefreshTimer; SignOut(); api.Dispose(); }
}
