using System.Collections.ObjectModel;
using Nora.Desktop.Models;
using Nora.Desktop.Services;

namespace Nora.Desktop.ViewModels;

/// <summary>
/// Computadores do OBS (P-014). A atendente só entra em computador ativado pelo administrador da UBS; o administrador entra em qualquer um e, de dentro do OBS, ativa este
/// e gera códigos para outros. O segredo do computador fica só aqui (DPAPI do usuário do Windows) e a API guarda apenas o hash. "Manter conectado" também só existe em computador ativado.
/// </summary>
public sealed partial class WorkspaceViewModel
{
    private const string RevokedNotice = "Este computador foi desativado pelo administrador da UBS. Peça um novo código para continuar.";
    private readonly IObsDeviceStore? devices;
    private readonly IClipboardService clipboard;
    private StoredDevice? storedDevice;
    private DeviceSummary? currentDevice;
    private bool storedDeviceRead, deviceOpened, deviceUnverified, keepSignedIn, devicesLoaded, devicesFailed;
    private string activationCode = "", deviceDraftName = "", deviceNameError = "", issuedCode = "", issuedCodeFor = "";
    private DateTimeOffset? issuedCodeExpiresAt;
    private ObsDevice? selectedObsDevice;
    private Task deviceCheck = Task.CompletedTask;

    /// <summary>Passos da abertura do OBS e da ativação por código, na tela de entrada.</summary>
    public ObservableCollection<ProgressStep> OpeningSteps { get; } = [];
    /// <summary>Passos das ações da seção Computadores.</summary>
    public ObservableCollection<ProgressStep> DeviceSteps { get; } = [];
    public ObservableCollection<ObsDevice> ObsDevices { get; } = [];

    /// <summary>Termina quando a conferência do computador, disparada por uma sessão recusada pela API, acaba (para a tela e o teste esperarem).</summary>
    public Task DeviceCheckCompletion => deviceCheck;

    // ---- Tela de entrada ----

    /// <summary>Este aplicativo identifica e ativa o computador: só o OBS e só com o recurso ligado no armazenamento (desligado por padrão desde 24/11/2026, quando o login passou a valer sozinho).</summary>
    public bool HasDeviceSupport => devices is { ComputersEnabled: true } && IsOperational;
    /// <summary>Há ativação neste computador: conferida pela API ou guardada e ainda não conferida (a API estava fora do ar na abertura; a conferência vem no login).</summary>
    public bool DeviceActivated => currentDevice is not null || (deviceUnverified && storedDevice is not null);
    public string DeviceStateLabel => currentDevice is { } device ? $"Computador ativado: {device.Display}"
        : deviceUnverified && storedDevice is not null ? "Este computador tem uma ativação guardada, mas a API não respondeu para conferi-la. Ela será conferida quando você entrar."
        : "Este computador não está ativado. A atendente precisa de um computador ativado; o administrador da UBS entra normalmente.";
    /// <summary>"Manter conectado" só vale em computador ativado.</summary>
    public bool CanKeepSignedIn => HasDeviceSupport && DeviceActivated && CanLogin;
    public bool KeepSignedIn { get => keepSignedIn && DeviceActivated; set { keepSignedIn = value; Notify(); } }
    public bool ShowActivation => HasDeviceSupport && !DeviceActivated && IsSignedOut;
    public string ActivationCode { get => activationCode; set { activationCode = value ?? ""; Notify(); Notify(nameof(CanActivateWithCode)); } }
    public bool CanActivateWithCode => ShowActivation && !userBusy && activationCode.Trim().Length > 0;
    public bool HasOpeningSteps => OpeningSteps.Count > 0;
    /// <summary>A abertura do OBS já começou (o teste espera por isto antes de conferir os passos).</summary>
    public bool OpenAttempted => deviceOpened;

    private static readonly string[] DeviceNames =
    [
        nameof(HasDeviceSupport), nameof(DeviceActivated), nameof(DeviceStateLabel), nameof(CanKeepSignedIn), nameof(KeepSignedIn), nameof(ShowActivation), nameof(CanActivateWithCode),
        nameof(ShowActivateThisComputer), nameof(CanActivateThisComputer), nameof(CanCreateDeviceCode)
    ];
    private void NotifyDevice() { foreach (var name in DeviceNames) Notify(name); }

    /// <summary>Abertura do OBS: confere este computador na API e, se há sessão guardada, a restaura sem pedir a senha. Cada passo concluído fica marcado; falha de conexão mantém os arquivos.</summary>
    public Task OpenAsync() => desktopAccess ? RunAsync(RestoreDesktopAccessAsync) : HasDeviceSupport ? RunAsync(OpenCoreAsync) : Task.CompletedTask;

    private async Task OpenCoreAsync()
    {
        if (devices is null || !IsOperational || (deviceOpened && !devices.DeviceReadUnavailable) || IsAuthenticated) return;
        deviceOpened = true;
        OpeningSteps.Clear();
        var device = ReadStoredDevice();
        var session = devices.ReadSession();
        ShowStoreWarning();
        if (device is null && devices.DeviceReadUnavailable)
        {
            // Não foi possível ler agora: nada é apagado (nem a sessão guardada) e o próximo OpenAsync (ou o login, que lê de novo o arquivo) tenta outra vez.
            NotifyDevice();
            return;
        }
        if (device is null)
        {
            // Sem ativação não há por que guardar uma sessão: ela só existe junto de um computador ativado.
            if (session is not null) devices.DeleteSession();
            NotifyDevice();
            return;
        }
        if (string.IsNullOrWhiteSpace(Endpoint)) Endpoint = device.Endpoint;
        api.Configure(Endpoint);
        ApplyDeviceCredential();
        if (!api.SendsDeviceCredential) { NotifyDevice(); return; }
        try
        {
            await WithStepAsync(OpeningSteps, "Conferindo este computador…", async () => currentDevice = await api.CurrentDeviceAsync(), clear: false);
        }
        catch (NoraApiException error) when (error.Code == "device-revoked")
        {
            DropDevice();
            Say(MessageKind.Error, RevokedNotice);
            return;
        }
        catch (NoraApiException)
        {
            // Sem conexão (ou erro do servidor): a ativação guardada continua valendo e a tela não afirma que o computador está desativado.
            deviceUnverified = true;
            throw;
        }
        finally { NotifyDevice(); }
        deviceUnverified = false;
        if (session is null) return;
        if (session.ExpiresAt <= DateTimeOffset.UtcNow || !string.Equals(session.Endpoint, api.EndpointAddress, StringComparison.Ordinal))
        {
            devices.DeleteSession();
            return;
        }
        AccountUser? account = null;
        try
        {
            api.RestoreSession(session.Token, session.ExpiresAt);
            await WithStepAsync(OpeningSteps, "Restaurando a sessão salva…", async () => account = await api.MeAsync(), clear: false);
        }
        catch (NoraApiException error) when (error.Unauthorized)
        {
            devices.DeleteSession();
            Say(MessageKind.Info, "A sessão salva neste computador venceu ou foi encerrada. Entre com a senha.");
            return;
        }
        catch (NoraApiException)
        {
            // Sem conexão (ou erro do servidor): a sessão guardada continua valendo para a próxima tentativa.
            api.ClearSession();
            throw;
        }
        if (disposed || account is null || account.Scope != profile.Id) { api.ClearSession(); return; }
        user = account;
        mustChangePassword = account.MustChangePassword;
        NotifySession(); Notify(nameof(MustChangePassword));
        if (mustChangePassword) { Say(MessageKind.Info, "Esta conta usa uma senha provisória. Defina uma nova senha para continuar."); return; }
        refreshTimer.Start();
        try { await LoadAfterLoginAsync(); }
        catch (NoraApiException error) when (!error.Unauthorized) { RegisterRefreshFailure(error.Message, show: false); throw; }
    }

    /// <summary>Ativa este computador com o código gerado pelo administrador da UBS (uso único, vale 15 minutos). O segredo recebido só é guardado aqui.</summary>
    public Task ActivateWithCodeAsync() => RunAsync(async () =>
    {
        if (devices is null || !IsOperational) return;
        var code = ActivationCode.Trim();
        if (code.Length == 0) throw new NoraApiException("Informe o código de ativação que o administrador da UBS gerou.");
        api.Configure(Endpoint);
        await WithStepAsync(OpeningSteps, "Ativando este computador…", async () =>
        {
            var result = await api.ActivateDeviceAsync(code);
            var stored = new StoredDevice(api.EndpointAddress!, result.DeviceId, result.Secret);
            // Se o segredo não puder ser guardado, a ativação já valeu na API e só um novo código resolve; o texto diz isso.
            if (!devices.SaveDevice(stored)) throw new NoraApiException("O computador foi ativado, mas a ativação não pôde ser guardada aqui. Peça um novo código ao administrador da UBS.");
            storedDevice = stored; storedDeviceRead = true;
            ApplyDeviceCredential();
            currentDevice = new DeviceSummary(result.DeviceId, result.Name, result.UnitId, result.UnitName);
        });
        ActivationCode = "";
        NotifyDevice();
        Say(MessageKind.Success, $"Computador ativado: {currentDevice!.Display}.");
    });

    private StoredDevice? ReadStoredDevice()
    {
        // Leitura que falhou por E/S transitória não vale como "sem ativação": não é memorizada e o arquivo continua lá para a próxima tentativa.
        if (!storedDeviceRead) { storedDevice = devices?.ReadDevice(); storedDeviceRead = devices?.DeviceReadUnavailable != true; }
        return storedDevice;
    }

    /// <summary>Entrega a credencial guardada ao cliente de API; ele só a envia ao mesmo endereço para o qual foi ativada.</summary>
    private void ApplyDeviceCredential() =>
        api.SetDeviceCredential(storedDevice?.Endpoint, storedDevice is { } device ? new DeviceCredential(device.DeviceId, device.Secret) : null);

    private void ShowStoreWarning()
    {
        if (devices?.TakeWarning() is { } warning) Say(MessageKind.Info, warning);
    }

    /// <summary>A API deixou de reconhecer este computador (revogado ou segredo antigo): apaga a ativação e a sessão guardadas.</summary>
    private void DropDevice()
    {
        // Com a leitura indisponível (E/S transitória) a ativação existe e não é provado que a API a recusou: só a sessão sai.
        if (devices?.DeviceReadUnavailable != true) devices?.DeleteDevice();
        devices?.DeleteSession();
        storedDevice = null; storedDeviceRead = true; currentDevice = null;
        api.SetDeviceCredential(null, null);
        ShowStoreWarning();
        NotifyDevice();
    }

    /// <summary>
    /// Depois do login: guarda (ou apaga) a sessão conforme "Manter conectado" e atualiza o estado do computador pela resposta. Login sem computador apesar da credencial enviada pode ter duas causas
    /// (computador revogado, ou conta sem acesso à UBS dele com a exigência desligada): só a conferência na API decide, e a ativação guardada só é apagada se ele foi mesmo revogado.
    /// </summary>
    private async Task ApplyLoginDeviceAsync(LoginSession session, bool sentCredential)
    {
        if (devices is null || !HasDeviceSupport) return;
        if (session.Device is { } device) { currentDevice = device; deviceUnverified = false; }
        else if (sentCredential)
        {
            switch (await ConfirmDeviceAsync())
            {
                case DeviceCheck.Revoked:
                    Say(MessageKind.Info, "Este computador não está mais ativado. O acesso continua, mas a atendente precisará de um novo código do administrador da UBS.");
                    break;
                case DeviceCheck.Active:
                    Say(MessageKind.Info, $"Este computador continua ativado para {currentDevice!.UnitName}, mas esta sessão não ficou ligada a ele: a sua conta não tem acesso a essa UBS.");
                    break;
            }
        }
        if (session.KeepSignedIn && session.Device is not null) devices.SaveSession(new StoredSession(api.EndpointAddress!, session.Token, session.ExpiresAt));
        else devices.DeleteSession();
        ShowStoreWarning();
        NotifyDevice();
    }

    /// <summary>A API recusou a sessão (401): sai da conta e, com credencial de computador, confere uma vez se ele foi desativado.</summary>
    private void SignOutRejected(string message)
    {
        SignOut();
        devices?.DeleteSession();
        Say(MessageKind.Error, message);
        if (devices is not null && storedDevice is not null && api.SendsDeviceCredential) deviceCheck = VerifyDeviceAsync();
    }

    private enum DeviceCheck { Active, Revoked, Unknown }

    /// <summary>Confere este computador na API (cabeçalho): ativo atualiza o estado, revogado apaga a ativação guardada, falha de conexão ou do servidor deixa tudo como está.</summary>
    private async Task<DeviceCheck> ConfirmDeviceAsync()
    {
        try
        {
            var device = await api.CurrentDeviceAsync();
            if (disposed) return DeviceCheck.Unknown;
            currentDevice = device; deviceUnverified = false; NotifyDevice();
            return DeviceCheck.Active;
        }
        catch (NoraApiException error) when (error.Code == "device-revoked")
        {
            if (disposed) return DeviceCheck.Unknown;
            DropDevice();
            return DeviceCheck.Revoked;
        }
        catch (NoraApiException error)
        {
            System.Diagnostics.Trace.TraceWarning("NORA: não foi possível conferir o computador: {0}", error.Message);
            return DeviceCheck.Unknown;
        }
    }

    private async Task VerifyDeviceAsync()
    {
        if (await ConfirmDeviceAsync() == DeviceCheck.Revoked) Say(MessageKind.Error, RevokedNotice);
    }

    // ---- Seção Computadores (Configurações da UBS) ----

    /// <summary>Só o administrador vê e opera a seção; a atendente nem a enxerga.</summary>
    public bool ShowDevicesSection => IsAuthenticated && HasDeviceSupport && IsAdministrator;
    public bool CanManageDevices => CanEditCatalog;
    public bool ShowActivateThisComputer => devices is not null && !DeviceActivated;
    public string DeviceDraftName { get => deviceDraftName; set { deviceDraftName = value ?? ""; deviceNameError = ""; Notify(); Notify(nameof(DeviceNameError)); NotifyDevice(); } }
    public string DeviceNameError => deviceNameError;
    public bool CanActivateThisComputer => CanManageDevices && ShowActivateThisComputer && deviceDraftName.Trim().Length > 0;
    public bool CanCreateDeviceCode => CanManageDevices && deviceDraftName.Trim().Length > 0;
    public ObsDevice? SelectedObsDevice
    {
        get => selectedObsDevice;
        set { selectedObsDevice = value; Notify(); Notify(nameof(CanIssueNewDeviceCode)); Notify(nameof(CanRevokeDevice)); }
    }
    public bool CanIssueNewDeviceCode => CanManageDevices && SelectedObsDevice is { IsRevoked: false };
    public bool CanRevokeDevice => CanManageDevices && SelectedObsDevice is { IsRevoked: false };
    public string DevicesState => !HasActiveObsUnit ? "Escolha uma UBS no Painel da UBS para ver os computadores."
        : devicesFailed && !devicesLoaded ? "Não foi possível ler os computadores agora. Use Atualizar computadores para tentar de novo."
        : devicesFailed ? "A última leitura não concluiu; a lista é da leitura anterior. Use Atualizar computadores para tentar de novo."
        : !devicesLoaded ? "Lendo os computadores…"
        : ObsDevices.Count == 0 ? "Nenhum computador ativado ainda. A atendente só entra em computadores ativados." : "";
    public bool HasIssuedCode => issuedCode.Length > 0;
    public string IssuedCodeText => issuedCode;
    public string IssuedCodeTitle => issuedCodeFor.Length == 0 ? "Código de ativação" : $"Código de ativação para {issuedCodeFor}";
    public string IssuedCodeValidity => issuedCodeExpiresAt is { } at ? $"Vale até {at.ToLocalTime():HH:mm}, uma única vez. Ele não será mostrado de novo." : "";
    public string RevokeConfirmationText => SelectedObsDevice is not { } device ? "" : device.IsThisDevice
        ? $"Revogar {device.Name}? Sua sessão neste computador será encerrada e a atendente não poderá mais entrar aqui."
        : $"Revogar {device.Name}? A atendente não poderá mais entrar nele e as sessões abertas nele serão encerradas.";

    private static readonly string[] DeviceListNames = [nameof(ShowDevicesSection), nameof(CanManageDevices), nameof(DevicesState), nameof(CanIssueNewDeviceCode), nameof(CanRevokeDevice), nameof(HasIssuedCode), nameof(IssuedCodeText), nameof(IssuedCodeTitle), nameof(IssuedCodeValidity), nameof(RevokeConfirmationText)];
    private void NotifyDevices() { foreach (var name in DeviceListNames) Notify(name); NotifyDevice(); }

    private void ApplyDevices(Tolerant<ObsDevice[]> read)
    {
        devicesFailed = read.Failed;
        if (read.Value is { } list)
        {
            var selectedId = SelectedObsDevice?.Id;
            Replace(ObsDevices, list);
            devicesLoaded = true;
            SelectedObsDevice = selectedId is { } id ? ObsDevices.FirstOrDefault(item => item.Id == id) : null;
        }
        NotifyDevices();
    }

    /// <summary>Botão "Atualizar computadores": refaz só esta leitura.</summary>
    public Task LoadDevicesAsync() => RunAsync(async () =>
    {
        var unit = RequireDeviceAdmin();
        devicesFailed = false; NotifyDevices();
        await WithStepAsync(DeviceSteps, "Lendo os computadores…", async () =>
        {
            await LoadUnitScopeAsync(unit, UnitScope.Devices);
            if (ActiveObsUnit?.Id == unit.Id && devicesFailed) throw new NoraApiException("Não foi possível ler os computadores agora. A lista mostra a última leitura, se houver.");
        });
        if (ActiveObsUnit?.Id == unit.Id) Say(MessageKind.Success, "Lista de computadores atualizada.");
    });

    /// <summary>Ativa este computador: a API devolve o segredo uma única vez e ele é guardado só aqui, protegido pelo usuário do Windows.</summary>
    public Task ActivateThisComputerAsync() => RunAsync(async () =>
    {
        var unit = RequireDeviceAdmin();
        if (devices is null) throw new NoraApiException("Este aplicativo não guarda a ativação do computador.");
        var name = ReadDeviceName();
        DeviceSteps.Clear();
        CreatedObsDevice? created = null;
        await WithStepAsync(DeviceSteps, "Ativando este computador…", async () => created = await api.CreateObsDeviceAsync(unit.Id, name, true), clear: false);
        var credential = created?.Credential ?? throw new NoraApiException("A API não devolveu a credencial do computador. Gere um código e ative com ele.");
        await WithStepAsync(DeviceSteps, "Guardando a ativação neste computador…", () =>
        {
            var stored = new StoredDevice(api.EndpointAddress!, credential.DeviceId, credential.Secret);
            if (!devices.SaveDevice(stored)) throw new NoraApiException("O computador foi ativado, mas a ativação não pôde ser guardada aqui. Gere um código novo para ele e ative com o código.");
            storedDevice = stored; storedDeviceRead = true;
            ApplyDeviceCredential();
            currentDevice = new DeviceSummary(credential.DeviceId, created!.Device.Name, unit.Id, unit.Name);
            return Task.CompletedTask;
        }, clear: false);
        DeviceDraftName = "";
        ShowStoreWarning();
        await ReloadAfterChangeAsync(unit, UnitScope.Devices, $"Computador ativado: {currentDevice!.Display}. A atendente já pode entrar aqui.");
    });

    /// <summary>Gera o código para ativar outro computador: aparece uma única vez, grande, e some ao fechar o painel.</summary>
    public Task CreateDeviceCodeAsync() => RunAsync(async () =>
    {
        var unit = RequireDeviceAdmin();
        var name = ReadDeviceName();
        DeviceSteps.Clear(); CloseIssuedCode();
        CreatedObsDevice? created = null;
        await WithStepAsync(DeviceSteps, "Gerando o código de ativação…", async () => created = await api.CreateObsDeviceAsync(unit.Id, name, false), clear: false);
        if (created?.ActivationCode is not { Length: > 0 } code || created.CodeExpiresAt is not { } expires) throw new NoraApiException("A API não devolveu o código de ativação. Tente gerar de novo.");
        ShowIssuedCode(code, created.Device.Name, expires);
        DeviceDraftName = "";
        await ReloadAfterChangeAsync(unit, UnitScope.Devices, "Código gerado. Digite-o no outro computador, na tela de entrada, antes de vencer.");
    });

    /// <summary>Gera um código novo para o computador selecionado (reativação: o segredo antigo vale até o código ser usado).</summary>
    public Task IssueNewDeviceCodeAsync() => RunAsync(async () =>
    {
        var unit = RequireDeviceAdmin();
        var device = SelectedObsDevice ?? throw new NoraApiException("Selecione um computador da lista.");
        if (device.IsRevoked) throw new NoraApiException("Este computador foi revogado. Crie um computador novo para ele.");
        DeviceSteps.Clear(); CloseIssuedCode();
        ActivationCodeInfo? info = null;
        await WithStepAsync(DeviceSteps, "Gerando o código de ativação…", async () => info = await api.IssueDeviceCodeAsync(unit.Id, device.Id), clear: false);
        ShowIssuedCode(info!.ActivationCode, device.Name, info.CodeExpiresAt);
        await ReloadAfterChangeAsync(unit, UnitScope.Devices, "Novo código gerado. O anterior deixou de valer.");
    });

    /// <summary>Revoga o computador selecionado: as sessões abertas nele caem. Se for este computador, a ativação e a sessão guardadas aqui são apagadas e a sessão encerra.</summary>
    public Task RevokeDeviceAsync() => RunAsync(async () =>
    {
        var unit = RequireDeviceAdmin();
        var device = SelectedObsDevice ?? throw new NoraApiException("Selecione um computador da lista.");
        if (device.IsRevoked) throw new NoraApiException("Este computador já foi revogado.");
        DeviceSteps.Clear();
        await WithStepAsync(DeviceSteps, "Revogando o computador…", () => api.RevokeDeviceAsync(unit.Id, device.Id), clear: false);
        var wasThis = device.IsThisDevice || (currentDevice is { } current && current.Id == device.Id);
        if (wasThis)
        {
            DropDevice();
            if (!disposed) { SignOut(); Say(MessageKind.Success, "Computador revogado. Sua sessão neste computador foi encerrada; entre de novo para continuar."); }
            return;
        }
        await ReloadAfterChangeAsync(unit, UnitScope.Devices, $"Computador {device.Name} revogado. As sessões abertas nele foram encerradas.");
    });

    private void ShowIssuedCode(string code, string forName, DateTimeOffset expiresAt)
    {
        issuedCode = code; issuedCodeFor = forName; issuedCodeExpiresAt = expiresAt;
        NotifyDevices();
    }

    /// <summary>Fecha o painel do código: ele não fica guardado em memória.</summary>
    public void CloseIssuedCode()
    {
        if (issuedCode.Length == 0) return;
        issuedCode = ""; issuedCodeFor = ""; issuedCodeExpiresAt = null;
        NotifyDevices();
    }

    public void CopyIssuedCode()
    {
        if (issuedCode.Length == 0) return;
        clipboard.SetText(issuedCode);
        Say(MessageKind.Success, "Código copiado.");
    }

    private HealthUnit RequireDeviceAdmin()
    {
        if (!IsAdministrator) throw new NoraApiException("Somente o administrador da UBS gerencia os computadores. Peça a um administrador da UBS.");
        return ActiveObsUnit ?? throw new NoraApiException("Escolha uma UBS no Painel da UBS antes de gerenciar os computadores.");
    }

    private string ReadDeviceName()
    {
        var name = string.Join(' ', DeviceDraftName.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        deviceNameError = name.Length is 0 or > 60 ? "Informe o nome do computador, de 1 a 60 caracteres."
            : name.Any(char.IsControl) ? "O nome não pode ter caracteres de controle." : "";
        Notify(nameof(DeviceNameError));
        if (deviceNameError.Length > 0) throw new NoraApiException(deviceNameError);
        return name;
    }

    private void ClearDevices()
    {
        devicesLoaded = devicesFailed = false;
        ObsDevices.Clear(); SelectedObsDevice = null; DeviceSteps.Clear();
        CloseIssuedCode();
        deviceDraftName = ""; deviceNameError = ""; Notify(nameof(DeviceDraftName)); Notify(nameof(DeviceNameError));
        NotifyDevices();
    }
}
