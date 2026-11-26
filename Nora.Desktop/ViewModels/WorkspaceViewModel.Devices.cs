using System.Collections.ObjectModel;
using Nora.Desktop.Models;
using Nora.Desktop.Services;

namespace Nora.Desktop.ViewModels;

/// <summary>
/// Computador ativado do OBS (P-014). Com o recurso ligado no armazenamento, a atendente só entra em computador ativado (código digitado na tela de entrada); o administrador entra em qualquer um.
/// O segredo do computador fica só aqui (DPAPI do usuário do Windows) e a API guarda apenas o hash. A subtela "Computadores" das Informações da UBS foi retirada em 26/11/2026:
/// o que sobra aqui é a abertura, a ativação por código, a conferência do computador e a sessão guardada.
/// </summary>
public sealed partial class WorkspaceViewModel
{
    private const string RevokedNotice = "Este computador foi desativado pelo administrador da UBS. Peça um novo código para continuar.";
    private readonly IObsDeviceStore? devices;
    private StoredDevice? storedDevice;
    private DeviceSummary? currentDevice;
    private bool storedDeviceRead, deviceOpened, deviceUnverified, keepSignedIn;
    private string activationCode = "";
    private Task deviceCheck = Task.CompletedTask;

    /// <summary>Passos da abertura do OBS e da ativação por código, na tela de entrada.</summary>
    public ObservableCollection<ProgressStep> OpeningSteps { get; } = [];

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
        nameof(HasDeviceSupport), nameof(DeviceActivated), nameof(DeviceStateLabel), nameof(CanKeepSignedIn), nameof(KeepSignedIn), nameof(ShowActivation), nameof(CanActivateWithCode)
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
}
