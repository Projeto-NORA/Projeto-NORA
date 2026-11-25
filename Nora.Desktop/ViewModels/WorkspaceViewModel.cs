using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Threading;
using Nora.Desktop.Generics;
using Nora.Desktop.Models;
using Nora.Desktop.Services;

namespace Nora.Desktop.ViewModels;

/// <summary>
/// Estado do espaço de trabalho. Partes por assunto: Messages (faixa de retorno), ObsUnits (UBS e cadastro),
/// UnitData (leitura por UBS), Appointments (agenda), Lightning (vagas), Listings (fichas), Units/Staff (Secretaria).
/// </summary>
public sealed partial class WorkspaceViewModel : ObservableObject, IDisposable
{
    private readonly DesktopProfile profile;
    private readonly NoraApiClient api;
    private readonly IHealthUnitGeocoder unitGeocoder;
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
        nameof(CanCallAppointment), nameof(CanCompleteAppointment), nameof(CanMissAppointment), nameof(CanCancelAppointment),
        nameof(CanConfirmObsUnit), nameof(CanProvisionUnitDatabase), nameof(CanLoadStaffUnits), nameof(CanGrantStaffUnit), nameof(CanRevokeStaffUnit),
        nameof(CanChangeStaffActive), nameof(CanResetStaffPassword), nameof(CanLoadMoreHistory), nameof(CanLoadMoreSlotsHistory), nameof(CanQueryHistory), nameof(MetricsLoading),
        nameof(CanEditCatalog), nameof(CanChangeProfessional), nameof(CanChangeRoom), nameof(CanChangeSchedule), nameof(CanChangeBlock), nameof(CanBook), nameof(CanChangeSpecialty), nameof(CanSaveSpecialty), nameof(CanEditGoals),
        nameof(CanDecideFamily), nameof(CanEndFamily), nameof(CanPublishAnnouncement), nameof(CanChangeMyAnnouncement), nameof(CanCloseMyAnnouncement), nameof(CanLoadMoreAnnouncements), nameof(CanLoadAnnouncementReaders),
        nameof(CanKeepSignedIn), nameof(CanActivateWithCode), nameof(CanManageDevices), nameof(CanActivateThisComputer), nameof(CanCreateDeviceCode), nameof(CanIssueNewDeviceCode), nameof(CanRevokeDevice),
        nameof(CanEditProfile), nameof(CanSaveProfile)
    ];
    private static readonly string[] SessionDependents =
    [
        nameof(IsAuthenticated), nameof(IsSignedOut), nameof(CanOperate), nameof(CanLogin), nameof(CanEditDraft), nameof(CanEditUnit), nameof(AccountName), nameof(AccountInitials),
        nameof(AccountRoleLabel), nameof(PhotoStateLabel), nameof(UnitLabel), nameof(ConnectionLabel), nameof(UnitsState), nameof(ObsUnitPrompt), nameof(NeedsObsUnitChoice), nameof(CanConfirmObsUnit), nameof(MustChangePassword), nameof(CanFilterNetwork), nameof(StaffState), nameof(HistoryState), nameof(CanLoadMoreSlotsHistory),
        nameof(IsAdministrator), nameof(CanEditCatalog), nameof(ShowCatalogReadOnlyNotice), nameof(CanFilterAlerts), nameof(SpecialtyCatalogState), nameof(GoalsState), nameof(NetworkAlertsState), nameof(CapacityState), nameof(CapacityCatalogState), nameof(UnitAlertsState), nameof(CanChangeSpecialty), nameof(CanSaveSpecialty), nameof(CanEditGoals),
        nameof(AnnouncementsState), nameof(FamilyPendingState), nameof(FamilyApprovedState), nameof(MyAnnouncementsState), nameof(CanDecideFamily), nameof(CanEndFamily), nameof(CanPublishAnnouncement), nameof(CanChangeMyAnnouncement), nameof(CanCloseMyAnnouncement),
        nameof(ShowActivation), nameof(CanKeepSignedIn), nameof(CanActivateWithCode), nameof(ShowDevicesSection), nameof(CanManageDevices), nameof(CanActivateThisComputer), nameof(CanCreateDeviceCode), nameof(CanIssueNewDeviceCode), nameof(CanRevokeDevice),
        nameof(CanEditProfile), nameof(CanSaveProfile), nameof(ShowProfileReadOnlyNotice)
    ];
    private static readonly string[] SectionDependents =
    [
        nameof(ShowMunicipal), nameof(ShowDashboard), nameof(ShowPatients), nameof(ShowAgenda), nameof(ShowSlots), nameof(ShowIndicators), nameof(ShowAlerts), nameof(ShowUnitSettings), nameof(ShowAnnouncements), nameof(ShowFamily),
        nameof(RequiresSignInForCurrentSection), nameof(AccessRequiredTitle), nameof(AccessRequiredDescription), nameof(AccessRequiredHint), nameof(IsIndicatorsPage), nameof(IsAlertsPage),
        nameof(PageTitle), nameof(PageSubtitle)
    ];

    /// <param name="devices">Armazenamento local da sessão no Windows e da identificação do computador do OBS.</param>
    public WorkspaceViewModel(DesktopProfile profile, NoraApiClient? api = null, IObsDeviceStore? devices = null, IClipboardService? clipboard = null, IHealthUnitGeocoder? unitGeocoder = null)
    {
        this.profile = profile;
        this.api = api ?? new();
        this.unitGeocoder = unitGeocoder ?? new HealthUnitGeocoder();
        this.devices = devices;
        if (profile.IsMunicipal) this.api.SetDeviceCredential(null, null);
        this.clipboard = clipboard ?? new WindowsClipboardService();
        OpeningSteps.CollectionChanged += (_, _) => Notify(nameof(HasOpeningSteps));
        EnsureOpeningDays();
        refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
        refreshTimer.Tick += OnRefreshTimer;
        endpoint = Environment.GetEnvironmentVariable("NORA_API_URL") ?? "";
        RefreshCommand = new RelayCommand(async _ => await RunAsync(async () =>
        {
            await RefreshAsync(ScopeFor(Section) | UnitScope.Everything | UnitScope.PanelMetrics | UnitScope.Catalog, manual: true);
            MarkFresh();
            Say(MessageKind.Success, "Dados atualizados pela API.");
        }), _ => CanOperate);
        LogoutCommand = new RelayCommand(async _ => await RunAsync(LogoutAsync), _ => CanOperate);
        DismissMessageCommand = new RelayCommand(_ => ClearMessage());
        SelectAgendaDayCommand = new RelayCommand(_ => AgendaViewMode = "Dia");
        SelectAgendaWeekCommand = new RelayCommand(_ => AgendaViewMode = "Semana");
        SelectCalendarDayCommand = new RelayCommand(day => SelectCalendarDay(day as AgendaCalendarDay));
        BuildAgendaDays(); ResetAgendaRange();
        Replace(SummaryCards, BuildSummaryCards()); Replace(OverviewCards, BuildOverviewCards());
        Replace(UnitOverviewCards, BuildCards(null, null)); Replace(UnitLightningCards, BuildLightningCards(null));
        Replace(UnitCapacityCards, BuildCapacityCards(null, "")); Replace(NetworkCapacityCards, BuildCapacityCards(null, ""));
    }

    public string Endpoint { get => endpoint; set { endpoint = value; Notify(); } }
    public string Identifier
    {
        get => identifier;
        set
        {
            if (identifier == value) return;
            identifier = value;
            if (NeedsLoginUnit) SetLoginUnits([]);
            Notify();
        }
    }
    /// <summary>Há trabalho em andamento: ação do usuário, ciclo automático ou carga de seção.</summary>
    public bool IsBusy => userBusy || backgroundOperations > 0;
    /// <summary>A barra de progresso acompanha ação pedida pelo usuário e a consulta disparada por filtro; a atualização automática é silenciosa.</summary>
    public bool ShowProgress => userBusy || networkLoading > 0 || unitMetricsLoading > 0 || bookingLoading > 0 || networkAlertsLoading > 0 || capacityCatalogLoading > 0;
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
    public bool ShowAgenda => IsOperational && Section == "agenda";
    public bool ShowSlots => IsAuthenticated && IsOperational && Section == "slots";
    public bool ShowIndicators => IsOperational && Section == "indicators";
    public bool ShowAlerts => IsOperational && Section == "alerts";
    public bool ShowUnitSettings => IsAuthenticated && IsOperational && Section == "unit-settings";
    public bool RequiresSignInForCurrentSection => IsOperational && IsSignedOut && Section is "patients" or "slots" or "unit-settings" or "family" or "announcements";
    public string AccessRequiredTitle => Section switch
    {
        "patients" => "Pacientes da unidade",
        "slots" => "Vagas Relâmpago",
        "unit-settings" => "Informações da UBS",
        "family" => "Família",
        "announcements" => "Comunicados",
        _ => "Área da unidade"
    };
    public string AccessRequiredDescription => Section switch
    {
        "patients" => "Pesquise e consulte apenas os dados necessários para o trabalho da sua UBS.",
        "slots" => "Painel automático das vagas e ofertas.",
        "unit-settings" => "Profissionais, consultórios, grade de horários e bloqueios da sua UBS.",
        "family" => "Pedidos de vínculo entre responsáveis e dependentes, decididos pela equipe da UBS.",
        "announcements" => "Comunicados da Secretaria para a equipe das UBS desta conta.",
        _ => "Entre na unidade para acessar os dados protegidos."
    };
    public string AccessRequiredHint => Section switch
    {
        "patients" => "Entre no Painel da UBS para carregar os pacientes autorizados da sua unidade.",
        "slots" => "Entre no Painel da UBS para carregar as vagas e seus estados.",
        "unit-settings" => "Entre no Painel da UBS para carregar o cadastro de profissionais, consultórios e grade.",
        "family" => "Entre no Painel da UBS para carregar os pedidos de família da sua unidade.",
        "announcements" => "Entre no Painel da UBS para ler os comunicados da Secretaria.",
        _ => "Entre no Painel da UBS para continuar."
    };
    public bool IsIndicatorsPage => ShowIndicators;
    public bool IsAlertsPage => ShowAlerts;
    public string PageTitle => Section == "alerts" ? "Central de alertas" : "Indicadores da UBS";
    public string PageSubtitle => Section == "alerts"
        ? "Situações que pedem atenção, avaliadas pela API."
        : "Espera, produção, comparecimento e Vaga Relâmpago da UBS.";
    public string PatientTotalCount => HasActiveObsUnit ? ObsRegistry.Count.ToString() : "—";
    /// <summary>Ofertas de Vaga Relâmpago correndo agora.</summary>
    public string OffersRunningCount => HasActiveObsUnit && unitSlotsLoaded ? UnitLightningSlots.Count(slot => slot.Status == "offered").ToString() : "—";
    public string SlotOpenCount => HasActiveObsUnit && unitSlotsLoaded ? UnitLightningSlots.Count(entry => entry.Status is "open" or "offered").ToString() : "—";

    public string PatientCount => summary?.Patients.ToString() ?? "—";
    public string InactiveCount => summary?.InactivePatients?.ToString() ?? "—";
    /// <summary>Totais "agora" do resumo da rede (respeitam o filtro de UBS, não o de período).</summary>
    public System.Collections.ObjectModel.ObservableCollection<MetricCard> SummaryCards { get; } = [];
    private MetricCard[] BuildSummaryCards() =>
    [
        new("Pacientes cadastrados", PatientCount, "Pessoas com cadastro ativo em alguma UBS (CPF contado uma vez)"),
        new("Desativados", InactiveCount, "Pessoas que só têm cadastro desativado"),
        new("Vagas abertas", SlotCount, "Vagas abertas ou ofertadas agora"),
        new("Ofertas aceitas", AcceptedCount, "Total histórico; não confirma o atendimento")
    ];
    public string SlotCount => summary?.OpenSlots.ToString() ?? "—";
    public string AcceptedCount => summary?.AcceptedOffers.ToString() ?? "—";
    public string RegisteredSlotsCount => summary?.RegisteredSlots?.ToString() ?? "—";
    public string CreatedOffersCount => summary?.CreatedOffers?.ToString() ?? "—";
    public string DeclinedOffersCount => summary?.DeclinedOffers?.ToString() ?? "—";
    public string ExpiredOffersCount => summary?.ExpiredOffers?.ToString() ?? "—";
    public string PendingOffersCount => summary?.PendingOffers?.ToString() ?? "—";
    public string CancelledOffersCount => summary?.CancelledOffers?.ToString() ?? "—";
    public static IReadOnlyList<PeriodOption> LightningPeriodOptions { get; } =
        [new(0, "Todo o histórico"), new(7, "Últimos 7 dias"), new(30, "Últimos 30 dias"), new(90, "Últimos 90 dias")];
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
        [nameof(PatientCount), nameof(InactiveCount), nameof(AppliedSummaryUnitLabel), nameof(SummaryExportNote), nameof(UnitsWithoutDatabaseCount), nameof(HasUnitsWithoutDatabase), nameof(UnitsWithoutDatabaseNotice), nameof(SlotCount), nameof(AcceptedCount), nameof(RegisteredSlotsCount), nameof(CreatedOffersCount), nameof(DeclinedOffersCount), nameof(ExpiredOffersCount), nameof(PendingOffersCount), nameof(CancelledOffersCount), nameof(CanExportSummary), nameof(SummaryReceivedLabel)];
    private void NotifyLightning() { foreach (var name in LightningNames) Notify(name); }
    public bool CanExportSummary => IsAuthenticated && summary != null;
    public string SummaryReceivedLabel => summaryReceivedAt is { } time ? $"Recebido em {time.ToLocalTime():dd/MM/yyyy HH:mm:ss}" : "Nenhum resumo carregado";
    public string CreateSummaryCsv()
    {
        if (!CanExportSummary || summary is null || summaryReceivedAt is null) throw new InvalidOperationException("Nenhum resumo disponível para exportar.");
        return Nora.Desktop.Services.MunicipalCsv.Create(summary, summaryReceivedAt.Value, LightningPeriodDays, appliedSummaryUnitId is null ? null : AppliedSummaryUnitLabel);
    }
    private void NotifySummary()
    {
        NotifyLightning();
        foreach (var name in SummaryNames) Notify(name);
        Replace(SummaryCards, BuildSummaryCards());
    }
    public ICommand RefreshCommand { get; }
    public ICommand LogoutCommand { get; }
    public ICommand DismissMessageCommand { get; }
    public ICommand SelectAgendaDayCommand { get; }
    public ICommand SelectAgendaWeekCommand { get; }
    public ICommand SelectCalendarDayCommand { get; }

    public Task LoginAsync(string password) => RunAsync(async () =>
    {
        if (string.IsNullOrWhiteSpace(Identifier) || string.IsNullOrWhiteSpace(password)) throw new NoraApiException("Informe seu e-mail e sua senha.");
        if (!BrazilianDataValidator.IsLoginEmail(Identifier)) throw new NoraApiException("Informe um e-mail válido para entrar (nome@dominio.com).");
        if (desktopAccess) { await LoginDesktopAsync(password); return; }
        api.Configure(Endpoint);
        // A credencial do computador (só no OBS) vai no login e em toda requisição, mas apenas ao mesmo endereço de API para o qual foi ativada.
        if (HasDeviceSupport && ReadStoredDevice() is not null) ApplyDeviceCredential();
        var sentCredential = api.SendsDeviceCredential;
        LoginSession session;
        try { session = await api.LoginAsync(profile.Id, Identifier.Trim(), password, keepSignedIn && sentCredential); }
        catch (NoraApiException error) when (error.Code == "device-required" && sentCredential && devices is not null)
        {
            // A API recusou a atendente por falta de computador válido: confere este computador para a tela não seguir dizendo "ativado" (revogado, ou reativado em outra máquina) nem esconder o campo do código.
            if (await ConfirmDeviceAsync() == DeviceCheck.Revoked) throw new NoraApiException(RevokedNotice);
            throw;
        }
        await ApplyLoginDeviceAsync(session, sentCredential);
        user = session.User;
        mustChangePassword = session.User.MustChangePassword;
        NotifySession(); Notify(nameof(MustChangePassword));
        // Senha provisória: nenhum dado é lido até a pessoa definir a senha nova.
        if (mustChangePassword) { Say(MessageKind.Info, "Esta conta usa uma senha provisória. Defina uma nova senha para continuar."); return; }
        refreshTimer.Start();
        try { await LoadAfterLoginAsync(); }
        catch (NoraApiException error) when (!error.Unauthorized) { RegisterRefreshFailure(error.Message, show: false); throw; }
    });

    /// <summary>Carga inicial depois do login (ou da troca obrigatória de senha): rede e UBS na Secretaria; UBS autorizadas no OBS.</summary>
    private async Task LoadAfterLoginAsync()
    {
        _ = LoadAccountPhotoAsync();
        if (IsMunicipal) await LoadMunicipalAsync(); else await Task.WhenAll(InitializeObsUnitsAsync(), ReadAnnouncementsAsync(full: true));
        MarkFresh();
    }

    private async Task LogoutAsync()
    {
        devices?.DeleteSession();
        try { await api.LogoutAsync(); }
        catch (NoraApiException error)
        {
            if (!error.Unauthorized) throw new NoraApiException("Você saiu deste aplicativo, mas a API não confirmou a revogação da sessão. A sessão no servidor permanece até expirar.");
        }
        finally { SignOut(); }
        Say(MessageKind.Success, "Você saiu da conta.");
    }

    /// <summary>Resumo, UBS e indicadores da rede em paralelo; cada parte se aplica assim que chega, mesmo que outra falhe.</summary>
    private Task LoadMunicipalAsync() => Task.WhenAll(LoadSummaryAsync(), LoadUnitsCoreAsync(), LoadMetricsAsync(networkVersion), RefreshNetworkAlertBadgeIfDueAsync());

    /// <summary>Resumo da rede com o filtro de UBS atual. Sem mudança de conteúdo nada é recalculado, só o horário da leitura.</summary>
    private async Task LoadSummaryAsync(int version = -1, CancellationToken cancellation = default)
    {
        if (version < 0) version = networkVersion;
        Guid? unit = summaryUnitFilter == Guid.Empty ? null : summaryUnitFilter;
        var result = await api.SummaryAsync(unit, cancellation);
        if (disposed || !IsAuthenticated || version != networkVersion || cancellation.IsCancellationRequested) return;
        summaryReceivedAt = DateTimeOffset.Now;
        if (summary is not null && appliedSummaryUnitId == unit && System.Text.Json.JsonSerializer.Serialize(summary) == System.Text.Json.JsonSerializer.Serialize(result)) { Notify(nameof(SummaryReceivedLabel)); return; }
        summary = result; appliedSummaryUnitId = unit;
        if (LightningPeriodDays != 0 && CurrentLightningCohort == null) LightningPeriodDays = 0;
        NotifySummary(); Notify(nameof(MetricsScopeLabel));
    }

    /// <summary>Lê o que a seção atual precisa. Municipal: resumo da rede, indicadores (só nas seções que os mostram) e UBS (em Unidades). OBS: dados da UBS ativa, ou a lista de UBS quando ela ainda não veio.</summary>
    private async Task RefreshAsync(UnitScope scope, bool manual = false)
    {
        if (mustChangePassword) return;
        if (IsMunicipal)
        {
            var version = networkVersion;
            await Task.WhenAll(LoadSummaryAsync(version), NetworkSections.Contains(Section) ? LoadMetricsAsync(version) : Task.CompletedTask, Section == "units" || !UnitsLoaded ? LoadUnitsCoreAsync() : Task.CompletedTask, LoadMunicipalSectionExtrasAsync(manual),
                Section != "alerts" || AlertsUnitFilter != Guid.Empty || AlertsRegion.Length != 0 ? RefreshNetworkAlertBadgeIfDueAsync() : Task.CompletedTask);
            return;
        }
        // Comunicados são da conta (não da UBS): uma chamada incremental por ciclo, em paralelo com a leitura da UBS; a falha dela não derruba o resto.
        // Sem UBS ativa o ciclo não insiste: a leitura sai só do login, da abertura da aba e do Atualizar dados.
        var announcements = ActiveObsUnit is not null || manual || announcementsFullDue ? RefreshAnnouncementsAsync(manual) : Task.CompletedTask;
        try
        {
            if (ActiveObsUnit is { } unit) { if (scope != UnitScope.None) await LoadUnitScopeAsync(unit, scope); }
            else if (!ObsUnitsLoaded) await InitializeObsUnitsAsync();
        }
        finally { await announcements; }
    }
    private static readonly HashSet<string> NetworkSections = ["home", "units", "specialties", "indicators", "capacity", "reports"];

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
            catch (NoraApiException error)
            {
                if (error.Code == "password-change-required" && IsAuthenticated) { EnterPasswordChange(); return; }
                if (error.Unauthorized && IsAuthenticated) { SignOutRejected(error.Message); return; }
                Say(MessageKind.Error, error.Message);
            }
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
                if (error.Code == "password-change-required") EnterPasswordChange();
                else if (error.Unauthorized) SignOutRejected(error.Message);
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
    public Task RefreshAutomaticallyAsync() => RunBackgroundAsync(() => RefreshAsync(AutoScopeFor(Section)), skipIfBusy: true);

    private bool sectionLoadPending;
    private void OnSectionEntered()
    {
        if (disposed || !IsAuthenticated || mustChangePassword) return;
        if (IsMunicipal) { EnterMunicipalSection(); return; }
        if (Section == "alerts") MarkAlertsViewed();
        // Abrir a aba de comunicados faz uma leitura completa (traz o que foi encerrado ou venceu desde a última).
        if (Section == "announcements") { MarkAnnouncementsViewed(); announcementsFullDue = true; }
        if (ScopeFor(Section) == UnitScope.None && Section != "announcements") return;
        sectionLoadPending = true;
        StartBackground(() =>
        {
            if (!sectionLoadPending) return Task.CompletedTask;
            sectionLoadPending = false;
            return RefreshAsync(AutoScopeFor(Section));
        });
    }

    /// <summary>Secretaria: as contas e o histórico carregam ao abrir Usuários e histórico; os indicadores só se ainda não vieram.</summary>
    private void EnterMunicipalSection()
    {
        if (Section == "alerts") MarkNetworkAlertsViewed();
        if (Section == "users" && (!staffLoaded || !historyLoaded))
            StartBackground(async () =>
            {
                if (!staffLoaded) await LoadStaffCoreAsync();
                if (!historyLoaded) await LoadHistoryPageAsync(reset: true);
            });
        else if (NetworkSections.Contains(Section) && metrics is null && networkLoading == 0) ReloadNetwork(includeSummary: false);
        if (Section is "specialties" or "reports" or "alerts" or "capacity" or "communication") StartBackground(() => LoadMunicipalSectionExtrasAsync(manual: false));
    }

    /// <summary>Tira da memória tudo o que foi lido da API (a sessão continua): usado ao sair e ao exigir a troca da senha provisória.</summary>
    private void ResetLoadedData()
    {
        summary = null; summaryReceivedAt = null; LightningPeriodDays = 0;
        ClearRefreshState();
        NotifySummary();
        ClearObsUnits(); ClearStaff(); ClearUnits(); ClearMunicipalScheduling(); ClearAnnouncements(); ClearFamily(); ClearCommunication(); OpeningSteps.Clear();
    }

    private void SignOut()
    {
        refreshTimer.Stop();
        api.ClearSession(); user = null; mustChangePassword = false; ClearAccountPhoto();
        ResetLoadedData();
        Identifier = "";
        ClearPasswordErrors();
        NotifySession(); Notify(nameof(MustChangePassword));
    }
    private void NotifySession()
    {
        foreach (var name in SessionDependents) Notify(name);
        NotifySections();
    }
    private void NotifySections() { foreach (var name in SectionDependents) Notify(name); }
    public void Dispose() { disposed = true; refreshTimer.Tick -= OnRefreshTimer; SignOut(); api.Dispose(); }
}
