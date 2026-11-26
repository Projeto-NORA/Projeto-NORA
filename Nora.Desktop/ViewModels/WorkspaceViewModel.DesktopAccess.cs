using System.Reflection;
using System.Security.Cryptography;
using Nora.Desktop.Models;
using Nora.Desktop.Services;

namespace Nora.Desktop.ViewModels;

public sealed partial class WorkspaceViewModel
{
    private bool desktopAccess;
    private StaffUnitOption[] loginUnits = [];
    private StaffUnitOption? loginUnit;
    public string AccessTitle => profile.Name;
    public bool RememberDesktopSession { get; set; }
    public IReadOnlyList<StaffUnitOption> LoginUnits => loginUnits;
    public bool NeedsLoginUnit => loginUnits.Length > 0;
    public StaffUnitOption? LoginUnit { get => loginUnit; set { loginUnit = value; Notify(); } }

    public void EnableDesktopAccess()
    {
        desktopAccess = true;
        if (string.IsNullOrWhiteSpace(Endpoint))
        {
#if DEBUG
            Endpoint = "http://127.0.0.1:5080";
#else
            Endpoint = typeof(WorkspaceViewModel).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
                .FirstOrDefault(item => item.Key == "NoraServiceUrl")?.Value ?? "";
#endif
        }
    }

    private async Task LoginDesktopAsync(string password)
    {
        api.Configure(Endpoint);
        StoredDevice? enrollment = null;
        if (HasDeviceSupport && devices is not null)
        {
            var endpointAddress = api.EndpointAddress!;
            enrollment = devices.ReadEnrollment(endpointAddress);
            CheckDeviceRead(devices);
            if (enrollment is null)
            {
                var legacy = devices.ReadDevice();
                CheckDeviceRead(devices);
                enrollment = legacy?.Endpoint == endpointAddress ? legacy
                    : new StoredDevice(endpointAddress, Guid.NewGuid(), Convert.ToHexString(RandomNumberGenerator.GetBytes(32)));
                if (!devices.SaveEnrollment(enrollment))
                {
                    // Outra janela pode ter guardado a primeira identificação enquanto este login abria.
                    enrollment = devices.ReadEnrollment(endpointAddress)
                        ?? throw new NoraApiException("Não foi possível guardar a identificação deste OBS.");
                    devices.TakeWarning();
                }
            }
            api.SetDeviceCredential(enrollment.Endpoint, new DeviceCredential(enrollment.DeviceId, enrollment.Secret));
        }

        LoginSession? session = null;
        OpeningSteps.Clear();
        try
        {
            await WithStepAsync(OpeningSteps, "Entrando na sua conta…", async () =>
                session = await api.LoginAsync(profile.Id, Identifier.Trim(), password, RememberDesktopSession, true, LoginUnit?.Id), clear: false);
        }
        catch (NoraApiException error) when (error.Code == "device-unit-selection-required" && error.Units is { Length: > 0 } options)
        {
            OpeningSteps.Clear();
            SetLoginUnits(options);
            Say(MessageKind.Info, "Escolha a unidade em que você vai trabalhar.");
            return;
        }
        if (session is null || disposed) return;
        SetLoginUnits([]);
        if (enrollment is not null && session.Device is { } confirmed)
        {
            storedDevice = enrollment;
            storedDeviceRead = true;
            currentDevice = confirmed;
            deviceUnverified = false;
            devices?.SaveDevice(enrollment);
            NotifyDevice();
        }
        // Login salvo: a sessão é sempre gravada, com o vencimento que a API devolveu (24 h sem a caixa, 30 dias com "Manter conectado por 30 dias").
        // Fechar o programa não a apaga; só "Sair da conta", o vencimento ou a recusa da API. Com a ativação do computador ligada a API continua amarrando a sessão ao
        // computador e a reabertura confere o computador de novo, então gravar a sessão de 24 h não enfraquece essa exigência.
        devices?.SaveSession(new StoredSession(api.EndpointAddress!, session.Token, session.ExpiresAt));
        ShowStoreWarning();
        await CompleteDesktopAccessAsync(session.User);
    }

    private async Task RestoreDesktopAccessAsync()
    {
        if (devices?.ReadSession() is not { } saved) { ShowStoreWarning(); return; }
        api.Configure(Endpoint);
        if (saved.ExpiresAt <= DateTimeOffset.UtcNow || saved.Endpoint != api.EndpointAddress)
        {
            devices.DeleteSession();
            return;
        }
        if (HasDeviceSupport)
        {
            var enrollment = devices.ReadEnrollment(saved.Endpoint);
            CheckDeviceRead(devices);
            enrollment ??= devices.ReadDevice();
            CheckDeviceRead(devices);
            if (enrollment?.Endpoint != saved.Endpoint) return;
            api.SetDeviceCredential(enrollment.Endpoint, new DeviceCredential(enrollment.DeviceId, enrollment.Secret));
            storedDevice = enrollment;
            storedDeviceRead = true;
        }
        AccountUser? account = null;
        try
        {
            api.RestoreSession(saved.Token, saved.ExpiresAt);
            if (HasDeviceSupport)
                await WithStepAsync(OpeningSteps, "Conferindo este computador…", async () => currentDevice = await api.CurrentDeviceAsync(), clear: false);
            await WithStepAsync(OpeningSteps, "Restaurando a sessão salva…", async () => account = await api.MeAsync(), clear: false);
        }
        catch (NoraApiException error) when (error.Unauthorized || error.Code == "device-revoked")
        {
            api.ClearSession();
            devices.DeleteSession();
            Say(MessageKind.Info, "A sessão salva terminou. Entre com seu e-mail e sua senha.");
            return;
        }
        catch (NoraApiException) { api.ClearSession(); throw; }
        if (!disposed && account?.Scope == profile.Id)
        {
            NotifyDevice();
            await CompleteDesktopAccessAsync(account);
        }
        else { api.ClearSession(); devices.DeleteSession(); }
    }

    private static void CheckDeviceRead(IObsDeviceStore store)
    {
        if (store.DeviceReadUnavailable)
            throw new NoraApiException("Não foi possível ler a identificação deste OBS agora. Tente novamente.");
    }

    private async Task CompleteDesktopAccessAsync(AccountUser account)
    {
        user = account;
        mustChangePassword = account.MustChangePassword;
        NotifySession();
        Notify(nameof(MustChangePassword));
        if (mustChangePassword) { Say(MessageKind.Info, "Defina uma senha nova para continuar."); return; }
        refreshTimer.Start();
        try { await LoadAfterLoginAsync(); }
        catch (NoraApiException error) when (!error.Unauthorized) { RegisterRefreshFailure(error.Message, show: false); throw; }
    }

    private void SetLoginUnits(StaffUnitOption[] options)
    {
        loginUnits = options;
        LoginUnit = null;
        Notify(nameof(LoginUnits));
        Notify(nameof(NeedsLoginUnit));
    }

    public Task<StaffSelfProfile> ReadStaffProfileAsync() => api.ReadProfileAsync();

    public async Task<StaffSelfProfile> SaveStaffNameAsync(string fullName)
    {
        var account = user;
        var profileData = await api.SaveProfileNameAsync(fullName);
        if (user is not null && user.Id == account?.Id)
        {
            user = user with { FullName = profileData.FullName };
            NotifySession();
        }
        return profileData;
    }
}
