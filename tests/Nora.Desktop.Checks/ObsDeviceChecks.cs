using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Nora.Desktop.Models;
using Nora.Desktop.Services;
using Nora.Desktop.ViewModels;
using Nora.Desktop.Views;

namespace Nora.Desktop.Checks;

/// <summary>Rodada final de 2026-11-16 no desktop: computadores do OBS, Chamar próximo, atendimento ao público, aprovação das vagas e os textos de nova tentativa.</summary>
internal static partial class Program
{
    private static void RunRoundChecks(Application application, string storageRoot, string[] args)
    {
        RunObsDeviceChecks(storageRoot, args);
        RunCallNextChecks(storageRoot, args);
        RunUnitProfileChecks(storageRoot, args);
        RunSlotApprovalChecks(storageRoot, args);
        RunRetryTextsChecks(storageRoot, args);
        RunScoreHistoryChecks(storageRoot, args);
    }

    /// <summary>Janela do OBS com o armazenamento do computador numa pasta de teste; espera a abertura (conferência do computador e sessão guardada) terminar.</summary>
    private static ShellWindow OpenOperationalWithDevices(ApiFixture fixture, string storageRoot, string name, IObsDeviceStore store, Appearance? mode = null)
    {
        var window = new ShellWindow(DesktopProfile.Operational, Path.Combine(storageRoot, name), new NoraApiClient(fixture), store);
        window.ViewModel.ConfirmClose = false; window.ViewModel.Animations = false;
        if (mode is { } appearance) window.ViewModel.SetAppearance(appearance);
        PrepareWindow(window);
        var vm = window.ViewModel.Workspace;
        PumpUntil(() => vm.OpenAttempted && !vm.IsBusy);
        return window;
    }

    private static StoredDevice StoredFor(ApiFixture.FixtureDevice device) => new("https://fixture.invalid/", device.Id, device.Secret!);

    private static void RunObsDeviceChecks(string storageRoot, string[] args)
    {
        var previewRoot = PreviewRoot(args);
        Check("Computador do OBS: o armazenamento usa o DPAPI do usuário, não grava o segredo em texto e descarta arquivo corrompido com aviso", () =>
        {
            var folder = Path.Combine(storageRoot, "device-store");
            var store = new ObsDeviceStore(folder);
            var secret = new string('B', 64);
            var device = new StoredDevice("https://fixture.invalid/", Guid.NewGuid(), secret);
            Require(store.SaveDevice(device) && store.SaveSession(new StoredSession("https://fixture.invalid/", "token-da-sessao-12345", DateTimeOffset.UtcNow.AddDays(30))), "A gravação deveria funcionar.");
            var deviceBytes = File.ReadAllBytes(store.DevicePath); var sessionBytes = File.ReadAllBytes(store.SessionPath);
            Require(!System.Text.Encoding.UTF8.GetString(deviceBytes).Contains(secret, StringComparison.Ordinal) && !System.Text.Encoding.UTF8.GetString(sessionBytes).Contains("token-da-sessao", StringComparison.Ordinal), "O segredo e o token não podem ficar em texto nos arquivos.");
            Require(store.ReadDevice() == device && store.ReadSession() is { Token: "token-da-sessao-12345" } && store.TakeWarning() is null, "A leitura deveria devolver o que foi gravado, sem aviso.");
            File.WriteAllBytes(store.DevicePath, [1, 2, 3, 4, 5, 6, 7, 8]);
            Require(store.ReadDevice() is null && !File.Exists(store.DevicePath) && store.TakeWarning() is { Length: > 0 } && store.TakeWarning() is null, "Arquivo corrompido deveria ser apagado, tratado como ausente e avisar uma única vez.");
            Require(store.ReadSession() is not null, "A sessão guardada não deveria ser afetada pela ativação corrompida.");
            store.DeleteSession();
            Require(!File.Exists(store.SessionPath) && !File.Exists(store.SessionPath + ".tmp"), "Apagar a sessão deveria remover o arquivo.");
        });

        Check("Computador do OBS: falha de E/S ao ler o device.dat (arquivo em uso) não apaga a ativação nem a sessão: avisa, mantém o arquivo e a leitura seguinte funciona; só dado inválido é descartado", () =>
        {
            var folder = Path.Combine(storageRoot, "device-store-locked");
            var store = new ObsDeviceStore(folder);
            var device = new StoredDevice("https://fixture.invalid/", Guid.NewGuid(), new string('C', 64));
            Require(store.SaveDevice(device) && store.SaveSession(new StoredSession("https://fixture.invalid/", "token-da-sessao-12345", DateTimeOffset.UtcNow.AddDays(30))), "A gravação deveria funcionar.");
            using (new FileStream(store.DevicePath, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                Require(store.ReadDevice() is null && store.DeviceReadUnavailable && File.Exists(store.DevicePath) && store.TakeWarning() is { Length: > 0 } warning && warning.Contains("Tente de novo", StringComparison.Ordinal),
                    "Arquivo em uso: a leitura devolve nada, marca a leitura como indisponível, avisa e NÃO apaga o arquivo.");
            }
            Require(store.ReadDevice() == device && !store.DeviceReadUnavailable && store.TakeWarning() is null && store.ReadSession() is not null, "Liberado o arquivo, a leitura seguinte devolve a ativação, sem aviso, e a sessão continua guardada.");
            File.WriteAllBytes(store.DevicePath, [1, 2, 3, 4, 5, 6, 7, 8]);
            Require(store.ReadDevice() is null && !store.DeviceReadUnavailable && !File.Exists(store.DevicePath) && store.TakeWarning() is { Length: > 0 }, "Dado inválido (não é falha de E/S) continua sendo apagado, com aviso.");
        });

        Check("Computador do OBS: abertura com o device.dat em uso não apaga a sessão guardada nem marca o computador como não ativado, e o login seguinte lê de novo", () =>
        {
            using var fixture = new ApiFixture { UseWorkspaceIdentity = true };
            var unit = fixture.AddUnit("UBS Arquivo em uso", "Centro");
            var device = fixture.AddDevice(unit.Id, "Recepção 10");
            var store = new ObsDeviceStore(Path.Combine(storageRoot, "device-in-use"));
            store.SaveDevice(StoredFor(device)); store.SaveSession(new StoredSession("https://fixture.invalid/", "fixture-token", DateTimeOffset.UtcNow.AddDays(5)));
            ShellWindow window;
            FileStream hold = new(store.DevicePath, FileMode.Open, FileAccess.Read, FileShare.None);
            try { window = OpenOperationalWithDevices(fixture, storageRoot, "device-in-use", store, Appearance.Light); }
            catch { hold.Dispose(); throw; }
            try
            {
                var vm = window.ViewModel.Workspace;
                Require(!vm.DeviceActivated && File.Exists(store.DevicePath) && File.Exists(store.SessionPath), "Com o arquivo em uso a ativação e a sessão guardadas não podem ser apagadas.");
                hold.Dispose();
                Pump(vm.OpenAsync());
                Require(vm.DeviceActivated && File.Exists(store.DevicePath), "Liberado o arquivo, a abertura seguinte deveria reconhecer o computador ativado: " + vm.DeviceStateLabel);
            }
            finally { hold.Dispose(); CloseWindow(window); }
        });
        Check("Computador do OBS: o cabeçalho vai em toda requisição só para o mesmo endereço de API e a Secretaria nunca o envia", () =>
        {
            using var fixture = new ApiFixture();
            var unit = fixture.AddUnit("UBS Dispositivo", "Centro");
            var device = fixture.AddDevice(unit.Id, "Recepção 1");
            using var api = new NoraApiClient(fixture);
            api.Configure("https://fixture.invalid");
            api.SetDeviceCredential(api.EndpointAddress, new DeviceCredential(device.Id, device.Secret!));
            Require(api.SendsDeviceCredential, "A credencial do mesmo endereço deveria ser enviada.");
            var session = Task.Run(() => api.LoginAsync("obs", "fixture@example.invalid", "isolated-test-password")).GetAwaiter().GetResult();
            Task.Run(() => api.ObsUnitsAsync()).GetAwaiter().GetResult();
            Require(session.Device is { } summary && summary.Name == "Recepção 1" && fixture.DeviceHeaders.Count == 2 && fixture.DeviceHeaders.All(header => header == ApiFixture.HeaderFor(device)), "O login e a leitura seguinte deveriam levar o cabeçalho do computador.");
            Require(fixture.DeviceHeaders[0] is { Length: 97 }, "O cabeçalho deve ter 32 caracteres do id, ponto e 64 do segredo (97).");
            api.Configure("https://outro-servidor.invalid");
            Require(!api.SendsDeviceCredential, "Outro endereço de API não pode receber o segredo.");
            var before = fixture.DeviceHeaders.Count;
            try { Task.Run(() => api.CurrentDeviceAsync()).GetAwaiter().GetResult(); }
            catch (NoraApiException error) { Require(error.Code == "device-revoked" && fixture.DeviceHeaders.Count == before + 1 && fixture.DeviceHeaders[^1] is null, "A conferência em outro endereço deveria ir sem o cabeçalho."); }
            // Secretaria: mesmo com a ativação guardada no disco, o perfil municipal nunca carrega o armazenamento nem envia o cabeçalho.
            var store = new ObsDeviceStore(Path.Combine(storageRoot, "device-municipal"));
            store.SaveDevice(StoredFor(device));
            using var municipalFixture = new ApiFixture { UseWorkspaceIdentity = true };
            municipalFixture.AddUnit("UBS Municipal", "Centro");
            var window = new ShellWindow(DesktopProfile.Municipal, Path.Combine(storageRoot, "device-municipal-prefs"), new NoraApiClient(municipalFixture), store);
            window.ViewModel.ConfirmClose = false; window.ViewModel.Animations = false; PrepareWindow(window);
            try
            {
                var vm = LoginObs(window);
                Require(vm.IsAuthenticated && !vm.HasDeviceSupport && municipalFixture.DeviceHeaders.Count > 0 && municipalFixture.DeviceHeaders.All(header => header is null), "A Secretaria nunca deveria enviar o cabeçalho do computador.");
            }
            finally { CloseWindow(window); }
        });

        Check("Computador do OBS: Manter conectado grava a sessão, reabrir restaura sem senha com os passos concluídos e Sair apaga só a sessão", () =>
        {
            using var fixture = new ApiFixture { UseWorkspaceIdentity = true };
            var unit = fixture.AddUnit("UBS Sessão", "Centro");
            var device = fixture.AddDevice(unit.Id, "Recepção 1");
            var store = new ObsDeviceStore(Path.Combine(storageRoot, "device-session"));
            store.SaveDevice(StoredFor(device));
            var first = OpenOperationalWithDevices(fixture, storageRoot, "device-session-1", store, Appearance.Light);
            try
            {
                var vm = first.ViewModel.Workspace;
                Require(vm.DeviceActivated && vm.DeviceStateLabel == "Computador ativado: Recepção 1 · UBS Sessão" && vm.OpeningSteps.Count == 1 && vm.OpeningSteps[0] is { Text: "Computador conferido", IsDone: true }, "A abertura deveria conferir o computador e mostrar o passo concluído.");
                var content = Layout(first, 1100, 900);
                var keep = Descendants<CheckBox>(content).Single(box => AutomationProperties.GetName(box) == "Manter conectado neste computador");
                Require(keep.IsEnabled && keep.IsVisible, "Com o computador ativado a caixa Manter conectado deveria estar habilitada.");
                Require(FullTexts(content).Any(text => text == "Computador ativado: Recepção 1 · UBS Sessão"), "A linha de estado do computador não aparece na entrada.");
                SaveScrolled(first, Path.Combine(previewRoot, "obs-entrada-computador-ativado-light.png"), 1100, 900);
                vm.KeepSignedIn = true;
                LoginObs(first);
                Require(vm.IsAuthenticated && fixture.LastKeepSignedInRequested && store.ReadSession() is { Token: "fixture-token" } saved && saved.ExpiresAt - DateTimeOffset.UtcNow > TimeSpan.FromDays(29), "Manter conectado deveria guardar a sessão de 30 dias.");
            }
            finally { CloseWindow(first); }
            var logins = fixture.Count("POST", "/api/auth/login");
            var second = OpenOperationalWithDevices(fixture, storageRoot, "device-session-2", store, Appearance.Light);
            try
            {
                var vm = second.ViewModel.Workspace;
                Require(vm.IsAuthenticated && fixture.Count("POST", "/api/auth/login") == logins && vm.AccountName == "Operador Fixture", "Reabrir deveria restaurar a sessão sem pedir a senha.");
                Require(vm.OpeningSteps.Select(step => (step.Text, step.IsDone)).SequenceEqual([("Computador conferido", true), ("Sessão restaurada", true)]), "Os dois passos da abertura deveriam ficar concluídos: " + string.Join(" | ", vm.OpeningSteps.Select(step => step.Text + ":" + step.State)));
                vm.LogoutCommand.Execute(null); Settle(vm);
                Require(!vm.IsAuthenticated && store.ReadSession() is null && store.ReadDevice() is not null, "Sair deveria apagar só a sessão guardada, não a ativação do computador.");
            }
            finally { CloseWindow(second); }
        });

        Check("Computador do OBS: ativação por código na entrada (inválido, vencido e certo), com a caixa Manter conectado desabilitada até ativar", () =>
        {
            using var fixture = new ApiFixture();
            var unit = fixture.AddUnit("UBS Ativação", "Centro");
            var pending = fixture.AddDevice(unit.Id, "Recepção 2", "pending");
            pending.Code = "ABCD-2345"; pending.CodeExpiresAt = DateTimeOffset.UtcNow.AddMinutes(15);
            var expired = fixture.AddDevice(unit.Id, "Sala 3", "pending");
            expired.Code = "ZZZZ-9999"; expired.CodeExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1);
            var store = new ObsDeviceStore(Path.Combine(storageRoot, "device-activation"));
            var window = OpenOperationalWithDevices(fixture, storageRoot, "device-activation", store, Appearance.Light);
            try
            {
                var vm = window.ViewModel.Workspace;
                var content = Layout(window, 1100, 900);
                var keep = Descendants<CheckBox>(content).Single(box => AutomationProperties.GetName(box) == "Manter conectado neste computador");
                Require(vm.ShowActivation && !vm.DeviceActivated && !keep.IsEnabled && vm.DeviceStateLabel.StartsWith("Este computador não está ativado. A atendente precisa de um computador ativado;", StringComparison.Ordinal), "Sem ativação a caixa Manter conectado fica desabilitada e o estado explica.");
                Require(Descendants<Button>(content).Any(button => button.IsVisible && button.Content?.ToString() == "Ativar com código") && Descendants<TextBox>(content).Any(box => AutomationProperties.GetName(box) == "Código de ativação do computador"), "A entrada deveria oferecer o código e o botão de ativar.");
                SaveScrolled(window, Path.Combine(previewRoot, "obs-entrada-sem-ativacao-light.png"), 1100, 900);
                window.ViewModel.SetAppearance(Appearance.Dark); Layout(window, 1100, 900); SaveScrolled(window, Path.Combine(previewRoot, "obs-entrada-sem-ativacao-dark.png"), 1100, 900); window.ViewModel.SetAppearance(Appearance.Light);
                vm.Endpoint = "https://fixture.invalid";
                vm.ActivationCode = "ZZZZ-1111"; Pump(vm.ActivateWithCodeAsync());
                Require(vm.MessageKind == MessageKind.Error && vm.Message == "Código inválido, vencido ou já usado. Peça um novo código ao administrador da UBS." && vm.OpeningSteps[^1] is { Text: "Falhou: Ativando este computador", IsFailed: true } && store.ReadDevice() is null && !vm.DeviceActivated, "Código inexistente deveria mostrar a mensagem certa e marcar o passo como falho.");
                vm.ActivationCode = expired.Code; Pump(vm.ActivateWithCodeAsync());
                Require(vm.Message.StartsWith("Código inválido, vencido ou já usado.", StringComparison.Ordinal) && !vm.DeviceActivated, "Código vencido deveria ser recusado.");
                vm.ActivationCode = " abcd-2345 "; Pump(vm.ActivateWithCodeAsync());
                Require(vm.DeviceActivated && vm.OpeningSteps[^1] is { IsDone: true } && store.ReadDevice() is { } stored && stored.DeviceId == pending.Id && stored.Secret == pending.Secret && vm.ActivationCode == "", "O código certo (com hífen e minúsculas) deveria ativar e guardar o segredo só neste computador.");
                content = Layout(window, 1100, 900);
                Require(Descendants<CheckBox>(content).Single(box => AutomationProperties.GetName(box) == "Manter conectado neste computador").IsEnabled && !vm.ShowActivation, "Depois de ativar, Manter conectado habilita e o campo do código some.");
                vm.ActivationCode = "ABCD-2345"; Pump(vm.ActivateWithCodeAsync());
                Require(!vm.CanActivateWithCode, "Com o computador ativado não há mais o que ativar na entrada.");
            }
            finally { CloseWindow(window); }
        });

        Check("Computador do OBS: 401 com credencial apaga ativação e sessão e avisa; login com device nulo apaga a credencial; atendente sem computador recebe a mensagem certa", () =>
        {
            using var fixture = new ApiFixture { UseWorkspaceIdentity = true };
            var unit = fixture.AddUnit("UBS Revogação", "Centro");
            var device = fixture.AddDevice(unit.Id, "Recepção 1");
            var store = new ObsDeviceStore(Path.Combine(storageRoot, "device-revoked"));
            store.SaveDevice(StoredFor(device));
            var window = OpenOperationalWithDevices(fixture, storageRoot, "device-revoked", store, Appearance.Light);
            try
            {
                var vm = window.ViewModel.Workspace;
                vm.KeepSignedIn = true; LoginObs(window);
                Require(store.ReadSession() is not null, "A sessão deveria estar guardada.");
                fixture.RevokeDevice(device);
                Pump(vm.RefreshAutomaticallyAsync()); Pump(vm.DeviceCheckCompletion);
                Require(!vm.IsAuthenticated && !vm.DeviceActivated && store.ReadDevice() is null && store.ReadSession() is null, "O 401 com credencial deveria apagar a ativação e a sessão guardadas.");
                Require(vm.MessageKind == MessageKind.Error && vm.Message == "Este computador foi desativado pelo administrador da UBS. Peça um novo código para continuar.", "O aviso de revogação está errado: " + vm.Message);
                var content = Layout(window, 1100, 900);
                Require(vm.ShowActivation && FullTexts(content).Any(text => text.StartsWith("Este computador não está ativado.", StringComparison.Ordinal)), "A tela deveria voltar ao estado sem ativação, pedindo um novo código.");
            }
            finally { CloseWindow(window); }
            // Login que volta com device nulo apesar de ter enviado a credencial: a ativação guardada é apagada com aviso.
            using var staleFixture = new ApiFixture { UseWorkspaceIdentity = true };
            var staleUnit = staleFixture.AddUnit("UBS Revogação 2", "Centro");
            var stale = staleFixture.AddDevice(staleUnit.Id, "Recepção 9");
            var staleStore = new ObsDeviceStore(Path.Combine(storageRoot, "device-stale"));
            staleStore.SaveDevice(StoredFor(stale));
            staleFixture.RevokeDevice(stale);
            var direct = new WorkspaceViewModel(DesktopProfile.Operational, new NoraApiClient(staleFixture), staleStore);
            try
            {
                direct.Endpoint = "https://fixture.invalid"; direct.Identifier = "fixture@example.invalid";
                Pump(direct.LoginAsync("isolated-test-password"));
                Require(direct.IsAuthenticated && staleStore.ReadDevice() is null && direct.Message.Contains("não está mais ativado", StringComparison.Ordinal), "O login com device nulo deveria apagar a ativação e avisar: " + direct.Message);
            }
            finally { direct.Dispose(); }
            // Atendente: sem computador ativado o login é recusado; com o computador entra.
            using var attendantFixture = new ApiFixture { UseWorkspaceIdentity = true, ObsRole = "attendant", RequireDeviceForAttendants = true };
            var attendantUnit = attendantFixture.AddUnit("UBS Atendente", "Centro");
            var attendantDevice = attendantFixture.AddDevice(attendantUnit.Id, "Recepção 4");
            var attendantStore = new ObsDeviceStore(Path.Combine(storageRoot, "device-attendant"));
            var withoutDevice = new WorkspaceViewModel(DesktopProfile.Operational, new NoraApiClient(attendantFixture), attendantStore);
            try
            {
                withoutDevice.Endpoint = "https://fixture.invalid"; withoutDevice.Identifier = "fixture@example.invalid";
                Pump(withoutDevice.LoginAsync("isolated-test-password"));
                Require(!withoutDevice.IsAuthenticated && withoutDevice.Message == "Este computador não está ativado para a sua UBS. Peça ao administrador da UBS para ativá-lo.", "Atendente sem computador deveria receber device-required: " + withoutDevice.Message);
            }
            finally { withoutDevice.Dispose(); }
            attendantStore.SaveDevice(StoredFor(attendantDevice));
            var withDevice = new WorkspaceViewModel(DesktopProfile.Operational, new NoraApiClient(attendantFixture), attendantStore);
            try
            {
                withDevice.Endpoint = "https://fixture.invalid"; withDevice.Identifier = "fixture@example.invalid";
                Pump(withDevice.LoginAsync("isolated-test-password"));
                Require(withDevice.IsAuthenticated && withDevice.AccountRoleLabel == "Atendente" && !withDevice.ShowDevicesSection, "A atendente com computador ativado deveria entrar, sem ver a seção Computadores.");
            }
            finally { withDevice.Dispose(); }
        });

        Check("Computador do OBS: login recusado com device-required confere o computador (revogado some a ativação e reaparece o código); API fora na abertura não diz que está desativado; login sem computador com a ativação válida não a apaga", () =>
        {
            // 1. Atendente com a ativação guardada que o administrador revogou: o login recebe 403 device-required e a tela volta ao estado sem ativação.
            using var attendantFixture = new ApiFixture { UseWorkspaceIdentity = true, ObsRole = "attendant", RequireDeviceForAttendants = true };
            var unit = attendantFixture.AddUnit("UBS Código novo", "Centro");
            var device = attendantFixture.AddDevice(unit.Id, "Recepção 6");
            var store = new ObsDeviceStore(Path.Combine(storageRoot, "device-required-revoked"));
            store.SaveDevice(StoredFor(device));
            var window = OpenOperationalWithDevices(attendantFixture, storageRoot, "device-required-revoked", store, Appearance.Light);
            try
            {
                var vm = window.ViewModel.Workspace;
                Require(vm.DeviceActivated && !vm.ShowActivation, "Antes da revogação o computador está ativado e o campo do código fica escondido.");
                attendantFixture.RevokeDevice(device);
                vm.Identifier = "fixture@example.invalid"; Pump(vm.LoginAsync("isolated-test-password"));
                Require(!vm.IsAuthenticated && !vm.DeviceActivated && store.ReadDevice() is null && vm.ShowActivation && vm.Message.Contains("desativado pelo administrador da UBS", StringComparison.Ordinal), "O 403 device-required deveria conferir o computador, apagar a ativação e mostrar o campo do código: " + vm.Message);
                var content = Layout(window, 1100, 900);
                Require(FullTexts(content).Any(text => text.StartsWith("Este computador não está ativado.", StringComparison.Ordinal)) && Descendants<TextBox>(content).Any(box => AutomationProperties.GetName(box) == "Código de ativação do computador" && box.IsVisible), "A tela deveria mostrar que não está ativado e oferecer onde digitar o código.");
            }
            finally { CloseWindow(window); }
            // 2. Com a ativação ainda válida, o 403 device-required (conta sem acesso à UBS dele) não mexe no estado.
            var kept = attendantFixture.AddDevice(unit.Id, "Recepção 7");
            var keptStore = new ObsDeviceStore(Path.Combine(storageRoot, "device-required-kept")); keptStore.SaveDevice(StoredFor(kept));
            var keptVm = new WorkspaceViewModel(DesktopProfile.Operational, new NoraApiClient(attendantFixture), keptStore);
            try
            {
                keptVm.Endpoint = "https://fixture.invalid"; keptVm.Identifier = "fixture@example.invalid";
                attendantFixture.IgnoreDeviceOnLogin = true;
                Pump(keptVm.LoginAsync("isolated-test-password"));
                Require(!keptVm.IsAuthenticated && keptVm.DeviceActivated && keptStore.ReadDevice() is not null && keptVm.DeviceStateLabel.StartsWith("Computador ativado: Recepção 7", StringComparison.Ordinal), "Com o computador ainda ativo a recusa não pode apagar nem esconder a ativação: " + keptVm.DeviceStateLabel);
            }
            finally { keptVm.Dispose(); }
            // 3. A API fora do ar na abertura: a ativação guardada continua, a tela não afirma desativação e o login seguinte a confere.
            using var downFixture = new ApiFixture { UseWorkspaceIdentity = true };
            var downUnit = downFixture.AddUnit("UBS API fora", "Centro");
            var downDevice = downFixture.AddDevice(downUnit.Id, "Recepção 8");
            var downStore = new ObsDeviceStore(Path.Combine(storageRoot, "device-api-down")); downStore.SaveDevice(StoredFor(downDevice));
            downFixture.FailDeviceCurrent = true;
            var downWindow = OpenOperationalWithDevices(downFixture, storageRoot, "device-api-down", downStore, Appearance.Light);
            try
            {
                var vm = downWindow.ViewModel.Workspace;
                Require(vm.DeviceActivated && !vm.ShowActivation && vm.CanKeepSignedIn && !vm.DeviceStateLabel.Contains("não está ativado", StringComparison.Ordinal) && vm.DeviceStateLabel.Contains("não respondeu", StringComparison.Ordinal) && downStore.ReadDevice() is not null,
                    "Sem resposta da API a tela deveria manter a ativação, esconder o campo do código e dizer que não conseguiu conferir: " + vm.DeviceStateLabel);
                var down = Layout(downWindow, 1100, 900);
                Require(Descendants<CheckBox>(down).Single(box => AutomationProperties.GetName(box) == "Manter conectado neste computador").IsEnabled, "Manter conectado continua disponível com a ativação guardada.");
                downFixture.FailDeviceCurrent = false;
                vm.KeepSignedIn = true; LoginObs(downWindow);
                Require(vm.IsAuthenticated && vm.DeviceStateLabel == "Computador ativado: Recepção 8 · UBS API fora" && downStore.ReadSession() is not null, "O login seguinte deveria conferir o computador e guardar a sessão: " + vm.DeviceStateLabel);
            }
            finally { CloseWindow(downWindow); }
            // 4. Exigência desligada e atendente sem acesso à UBS do computador: entra sem computador e a ativação, ainda válida, continua guardada.
            using var relaxedFixture = new ApiFixture { UseWorkspaceIdentity = true, ObsRole = "attendant", IgnoreDeviceOnLogin = true };
            var relaxedUnit = relaxedFixture.AddUnit("UBS Sem acesso", "Centro");
            var relaxedDevice = relaxedFixture.AddDevice(relaxedUnit.Id, "Recepção 9");
            var relaxedStore = new ObsDeviceStore(Path.Combine(storageRoot, "device-relaxed")); relaxedStore.SaveDevice(StoredFor(relaxedDevice));
            var relaxed = new WorkspaceViewModel(DesktopProfile.Operational, new NoraApiClient(relaxedFixture), relaxedStore);
            try
            {
                relaxed.Endpoint = "https://fixture.invalid"; relaxed.Identifier = "fixture@example.invalid";
                Pump(relaxed.LoginAsync("isolated-test-password"));
                Require(relaxed.IsAuthenticated && relaxedStore.ReadDevice() is not null && relaxed.DeviceActivated && relaxed.Message.Contains("continua ativado", StringComparison.Ordinal), "Login sem computador com a ativação válida não pode apagá-la: " + relaxed.Message);
            }
            finally { relaxed.Dispose(); }
        });

        Check("Computador do OBS: seção Computadores do administrador (lista, vazio, falha com botão que refaz a leitura, ativar este, código que some, revogar este) e a atendente não a vê", () =>
        {
            using var fixture = new ApiFixture { UseWorkspaceIdentity = true };
            var unit = fixture.AddUnit("UBS Computadores", "Centro");
            var store = new ObsDeviceStore(Path.Combine(storageRoot, "device-section"));
            var window = OpenOperationalWithDevices(fixture, storageRoot, "device-section", store, Appearance.Light);
            var asked = new List<(string Title, string Text)>(); Confirmation.Handler = (_, title, text) => { asked.Add((title, text)); return true; };
            try
            {
                var vm = LoginObs(window);
                var content = Show(window, "unit-settings", 1280, 4200);
                Require(vm.ShowDevicesSection && Descendants<Border>(content).Any(border => AutomationProperties.GetName(border) == "Computadores" && border.IsVisible), "O administrador deveria ver a seção Computadores.");
                Require(vm.DevicesState == "Nenhum computador ativado ainda. A atendente só entra em computadores ativados.", "Estado vazio errado: " + vm.DevicesState);
                SaveScrolled(window, Path.Combine(previewRoot, "obs-computadores-vazio-light.png"), 1280, 900);
                // Falha: o texto aponta para um botão da mesma seção que refaz a leitura.
                fixture.FailDevices = true;
                Click(window, content, "Atualizar computadores", 1280, 4200);
                Require(vm.DevicesState.Contains("Use Atualizar computadores", StringComparison.Ordinal) && ButtonIn(content, "Atualizar computadores").IsEnabled && vm.MessageKind == MessageKind.Error, "A falha deveria apontar para o botão Atualizar computadores: " + vm.DevicesState);
                Require(vm.DeviceSteps.Single() is { Text: "Falhou: Lendo os computadores", IsFailed: true }, "O passo da leitura deveria ficar marcado como falho.");
                SaveScrolled(window, Path.Combine(previewRoot, "obs-computadores-falha-light.png"), 1280, 900);
                fixture.FailDevices = false; var reads = fixture.Count("GET", "/devices");
                Click(window, content, "Atualizar computadores", 1280, 4200);
                Require(fixture.Count("GET", "/devices") == reads + 1 && vm.DevicesState.StartsWith("Nenhum computador ativado ainda.", StringComparison.Ordinal) && vm.MessageKind == MessageKind.Success && vm.DeviceSteps.Single().IsDone, "O botão deveria refazer a leitura e limpar a falha.");
                // Nome obrigatório (1 a 60) e ativar este computador: duas etapas visíveis, segredo guardado só aqui.
                vm.DeviceDraftName = "   "; Pump(vm.ActivateThisComputerAsync());
                Require(vm.DeviceNameError.Contains("1 a 60") && fixture.Devices.Count == 0, "Nome vazio não pode chegar à API.");
                vm.DeviceDraftName = "Recepção   1"; Pump(vm.ActivateThisComputerAsync());
                var activated = fixture.Devices.Single();
                Require(vm.DeviceActivated && activated is { Status: "active", Name: "Recepção 1" } && store.ReadDevice() is { } saved && saved.DeviceId == activated.Id && saved.Secret == activated.Secret && !vm.ShowActivateThisComputer, "Ativar este computador deveria gravar a ativação e esconder o botão.");
                Require(vm.DeviceSteps.Select(step => (step.Text, step.IsDone)).SequenceEqual([("Computador ativado", true), ("Ativação guardada neste computador", true)]) && vm.ObsDevices.Single().IsThisDevice, "Os passos concluídos deveriam acumular e a lista marcar este computador.");
                // Código para outro computador: aparece uma única vez e some ao fechar.
                vm.DeviceDraftName = "Sala 2"; Pump(vm.CreateDeviceCodeAsync());
                var other = fixture.Devices.Single(item => item.Name == "Sala 2");
                content = Layout(window, 1280, 4200);
                Require(vm.HasIssuedCode && vm.IssuedCodeText == other.Code && vm.IssuedCodeValidity.Contains("uma única vez", StringComparison.Ordinal) && FullTexts(content).Contains(other.Code!) && other.Status == "pending", "O código deveria aparecer grande, com a validade.");
                SaveScrolled(window, Path.Combine(previewRoot, "obs-computadores-codigo-light.png"), 1280, 900);
                Click(window, content, "Fechar", 1280, 4200);
                Require(!vm.HasIssuedCode && vm.IssuedCodeText == "" && !FullTexts(Layout(window, 1280, 4200)).Contains(other.Code!), "Fechar o painel deveria apagar o código da memória e da tela.");
                vm.SelectedObsDevice = vm.ObsDevices.Single(item => item.Name == "Sala 2"); Pump(vm.IssueNewDeviceCodeAsync());
                Require(vm.HasIssuedCode && vm.IssuedCodeText == other.Code && vm.MessageKind == MessageKind.Success, "Gerar novo código deveria mostrar o código novo.");
                vm.CloseIssuedCode();
                // Revogar outro computador e depois este: a confirmação de este avisa da sessão.
                vm.SelectedObsDevice = vm.ObsDevices.Single(item => item.Name == "Sala 2"); content = Layout(window, 1280, 4200);
                Click(window, content, "Revogar computador selecionado", 1280, 4200);
                Require(asked.Last().Title == "Revogar computador" && other.Status == "revoked" && vm.IsAuthenticated, "Revogar outro computador deveria pedir confirmação e manter a sessão.");
                vm.SelectedObsDevice = vm.ObsDevices.Single(item => item.IsThisDevice); content = Layout(window, 1280, 4200);
                Require(vm.RevokeConfirmationText.Contains("Sua sessão neste computador será encerrada e a atendente não poderá mais entrar aqui.", StringComparison.Ordinal), "A confirmação de revogar este computador deveria avisar da sessão: " + vm.RevokeConfirmationText);
                Click(window, content, "Revogar computador selecionado", 1280, 4200);
                Require(asked.Last().Text.Contains("Sua sessão neste computador será encerrada", StringComparison.Ordinal) && !vm.IsAuthenticated && store.ReadDevice() is null && store.ReadSession() is null && !vm.DeviceActivated, "Revogar este computador deveria apagar a ativação e encerrar a sessão.");
                // Desfazer a revogação não existe: revogado não gera código.
                Require(fixture.Devices.All(item => item.Status == "revoked"), "Os dois computadores deveriam estar revogados na API.");
            }
            finally { Confirmation.Handler = null; CloseWindow(window); }
            // A atendente não vê a seção nem lê computadores.
            using var attendantFixture = new ApiFixture { UseWorkspaceIdentity = true, ObsRole = "attendant" };
            attendantFixture.AddUnit("UBS Computadores", "Centro");
            var attendantWindow = OpenOperational(attendantFixture, storageRoot, "device-section-attendant", Appearance.Light);
            try
            {
                var vm = LoginObs(attendantWindow);
                var content = Show(attendantWindow, "unit-settings", 1280, 4200);
                Require(!vm.ShowDevicesSection && !Descendants<Border>(content).Any(border => AutomationProperties.GetName(border) == "Computadores" && border.IsVisible) && attendantFixture.Count("GET", "/devices") == 0, "A atendente não deveria ver nem ler a seção Computadores.");
            }
            finally { CloseWindow(attendantWindow); }
            // Copiar o código usa a área de transferência injetada.
            using var copyFixture = new ApiFixture { UseWorkspaceIdentity = true };
            copyFixture.AddUnit("UBS Copiar", "Centro");
            var clipboard = new ClipboardFixture();
            var copyVm = new WorkspaceViewModel(DesktopProfile.Operational, new NoraApiClient(copyFixture), new ObsDeviceStore(Path.Combine(storageRoot, "device-copy")), clipboard);
            try
            {
                copyVm.Endpoint = "https://fixture.invalid"; copyVm.Identifier = "fixture@example.invalid"; Pump(copyVm.LoginAsync("isolated-test-password"));
                copyVm.DeviceDraftName = "Sala 5"; Pump(copyVm.CreateDeviceCodeAsync()); copyVm.CopyIssuedCode();
                Require(clipboard.Values.Count == 1 && clipboard.Values[0] == copyFixture.Devices.Single().Code, "Copiar código deveria usar a área de transferência.");
            }
            finally { copyVm.Dispose(); }
        });

        Check("Computador do OBS: mensagens dos códigos novos (ativação inválida, computador já ativado, computador desativado) e erro de permissão do administrador", () => Task.Run(async () =>
        {
            using var fixture = new ApiFixture();
            var unit = fixture.AddUnit("UBS Mensagens", "Centro");
            var device = fixture.AddDevice(unit.Id, "Recepção 1");
            using var api = new NoraApiClient(fixture);
            api.Configure("https://fixture.invalid");
            api.SetDeviceCredential(api.EndpointAddress, new DeviceCredential(device.Id, device.Secret!));
            await api.LoginAsync("obs", "fixture@example.invalid", "isolated-test-password");
            try { await api.CreateObsDeviceAsync(unit.Id, "Outro", true); throw new InvalidOperationException("Ativar este computador de novo deveria ser recusado."); }
            catch (NoraApiException error) { Require(error.Status == 409 && error.Code == "device-already-active" && error.Message == "Este computador já está ativado. Revogue-o antes de ativá-lo para outra UBS.", "Mensagem de device-already-active errada: " + error.Message); }
            try { await api.ActivateDeviceAsync("ABCD-0000"); throw new InvalidOperationException("Código inexistente deveria ser recusado."); }
            catch (NoraApiException error) { Require(error.Code == "invalid-activation-code" && error.Message.StartsWith("Código inválido, vencido ou já usado.", StringComparison.Ordinal), "Mensagem de invalid-activation-code errada: " + error.Message); }
            fixture.RevokeDevice(device);
            try { await api.CurrentDeviceAsync(); throw new InvalidOperationException("Computador revogado deveria dar 401."); }
            catch (NoraApiException error) { Require(error.Unauthorized && error.Code == "device-revoked" && error.Message.Contains("desativado pelo administrador da UBS", StringComparison.Ordinal), "Mensagem de device-revoked errada: " + error.Message); }
            // Nova sessão como atendente, sem computador (a anterior ficou presa ao computador revogado e só recebe 401).
            api.SetDeviceCredential(api.EndpointAddress, null);
            fixture.ObsRole = "attendant";
            await api.LoginAsync("obs", "fixture@example.invalid", "isolated-test-password");
            try { await api.CreateObsDeviceAsync(unit.Id, "Sala", false); throw new InvalidOperationException("Atendente não gerencia computadores."); }
            catch (NoraApiException error) { Require(error.Status == 403 && error.Message.StartsWith("Somente o administrador da UBS altera", StringComparison.Ordinal), "Mensagem de permissão errada: " + error.Message); }
        }).GetAwaiter().GetResult());
    }

    /// <summary>E2E com a API real: o administrador ativa este computador e gera um código, a atendente ativa o outro com o código e entra com Manter conectado, e a revogação a devolve à ativação.</summary>
    private static void RunRoundEndToEndChecks(string storageRoot, string[] args)
    {
        var address = Environment.GetEnvironmentVariable("NORA_E2E_URL");
        var attendantIdentifier = Environment.GetEnvironmentVariable("NORA_E2E_ATTENDANT_IDENTIFIER");
        var attendantPassword = Environment.GetEnvironmentVariable("NORA_E2E_ATTENDANT_PASSWORD");
        if (string.IsNullOrWhiteSpace(address) || Environment.GetEnvironmentVariable("NORA_E2E_UNIT_REGISTRY") != "1" || string.IsNullOrWhiteSpace(attendantIdentifier) || string.IsNullOrWhiteSpace(attendantPassword))
        {
            Console.WriteLine("E2E de computadores do OBS não solicitado: defina NORA_E2E_URL, NORA_E2E_UNIT_REGISTRY=1 e a conta de atendente em NORA_E2E_ATTENDANT_IDENTIFIER e NORA_E2E_ATTENDANT_PASSWORD.");
            return;
        }
        Check("E2E contratos da rodada com a API real: perfil da UBS (horários e serviços), aprovação das vagas, prévia do 1º da fila, vagas com os campos novos e erro de Chamar próximo", () => Task.Run(async () =>
        {
            var identifier = Environment.GetEnvironmentVariable("NORA_E2E_IDENTIFIER") ?? throw new InvalidOperationException("Falta identificador E2E.");
            var password = Environment.GetEnvironmentVariable("NORA_E2E_PASSWORD") ?? throw new InvalidOperationException("Falta senha E2E.");
            using var api = new NoraApiClient();
            api.Configure(address);
            await api.LoginAsync("obs", identifier, password);
            var unit = (await api.ObsUnitsAsync()).First(item => !item.DatabasePending);
            // Perfil: os intervalos e o texto voltam exatamente como foram gravados, com horário em HH:mm.
            var saved = await api.SaveUnitProfileAsync(unit.Id, new UnitProfileDraft([new(1, "07:00", "11:30"), new(1, "13:00", "17:00"), new(6, "08:00", "12:00")], "Vacinas e curativos"));
            var read = await api.UnitProfileAsync(unit.Id);
            Require(saved.Informed && read.Informed && read.Services == "Vacinas e curativos" && read.OpeningHours.Length == 3 && OpeningHoursText.ForDay(read.OpeningHours, 1) == "07:00–11:30 e 13:00–17:00" && OpeningHoursText.ForDay(read.OpeningHours, 0) == "Fechado", "O perfil gravado deveria voltar igual na API real.");
            // Aprovação das vagas: liga e desliga, e o perfil do OBS mostra a chave.
            var on = await api.SetSlotApprovalAsync(unit.Id, true);
            Require(on.Enabled && on.Since is not null && (await api.UnitProfileAsync(unit.Id)).SlotApprovalRequired, "A API real deveria ligar a aprovação das vagas.");
            var off = await api.SetSlotApprovalAsync(unit.Id, false);
            Require(!off.Enabled && !(await api.UnitProfileAsync(unit.Id)).SlotApprovalRequired, "A API real deveria desligar a aprovação das vagas.");
            // Prévia da fila e vagas: o formato do corpo (entryId numérico, campos novos da vaga) é lido pelo cliente sem erro.
            var specialty = (await api.ObsSpecialtiesAsync()).First(item => item.IsActive);
            var next = await api.NextInQueueAsync(unit.Id, specialty.Id);
            Require(next is null || (next.EntryId > 0 && next.PatientName.Length > 0), "A prévia do 1º da fila veio num formato inesperado.");
            var slots = await api.UnitLightningSlotsAsync(unit.Id);
            Require(slots.Items.All(slot => slot.Origin.Length > 0 && slot.Status.Length > 0), "As vagas deveriam trazer origem e situação.");
            // Computadores: a lista da UBS é lida pelo administrador.
            _ = await api.ObsDevicesAsync(unit.Id);
            // Chamar próximo com profissional desconhecido é recusado com mensagem e status, nunca com falha genérica.
            try { await api.CallNextAsync(unit.Id, specialty.Id, Guid.NewGuid(), DateTimeOffset.UtcNow.AddHours(3)); throw new InvalidOperationException("Profissional desconhecido não pode receber a vaga."); }
            catch (NoraApiException error) { Require(error.Status is 400 or 409 && error.Message.Length > 0, $"Erro inesperado de Chamar próximo: {error.Status} {error.Message}"); }
        }).GetAwaiter().GetResult());

        Check("E2E computadores do OBS: administrador ativa este e gera código → atendente ativa com o código e entra com Manter conectado → administrador revoga → atendente volta à ativação", () =>
        {
            var identifier = Environment.GetEnvironmentVariable("NORA_E2E_IDENTIFIER") ?? throw new InvalidOperationException("Falta identificador E2E.");
            var password = Environment.GetEnvironmentVariable("NORA_E2E_PASSWORD") ?? throw new InvalidOperationException("Falta senha E2E.");
            var suffix = Guid.NewGuid().ToString("N")[..8];
            var adminStore = new ObsDeviceStore(Path.Combine(storageRoot, "e2e-device-admin")); var attendantStore = new ObsDeviceStore(Path.Combine(storageRoot, "e2e-device-attendant"));
            var admin = new ShellWindow(DesktopProfile.Operational, Path.Combine(storageRoot, "e2e-admin-prefs"), devices: adminStore);
            var attendant = new ShellWindow(DesktopProfile.Operational, Path.Combine(storageRoot, "e2e-attendant-prefs"), devices: attendantStore);
            try
            {
                foreach (var window in new[] { admin, attendant }) { window.ViewModel.ConfirmClose = false; window.ViewModel.Animations = false; PrepareWindow(window); }
                LoginFromControls(admin, address, identifier, password);
                var adminVm = admin.ViewModel.Workspace;
                Require(adminVm.IsAuthenticated && adminVm.IsAdministrator, "O administrador deveria entrar sem computador ativado: " + adminVm.Message);
                adminVm.CandidateObsUnit = adminVm.ObsUnits.First(); Pump(adminVm.ConfirmObsUnitAsync());
                adminVm.DeviceDraftName = "Recepção E2E " + suffix; Pump(adminVm.ActivateThisComputerAsync());
                Require(adminVm.DeviceActivated && adminStore.ReadDevice() is not null, "O administrador não ativou este computador na API real: " + adminVm.Message);
                adminVm.DeviceDraftName = "Sala E2E " + suffix; Pump(adminVm.CreateDeviceCodeAsync());
                var code = adminVm.IssuedCodeText;
                Require(code.Length == 9 && code[4] == '-', "A API real deveria devolver o código no formato XXXX-XXXX: " + code);
                // Atendente: sem computador a API recusa; com o código ativa e entra mantendo a sessão.
                attendant.ViewModel.Workspace.Endpoint = address; attendant.ViewModel.Workspace.Identifier = attendantIdentifier;
                var attendantVm = attendant.ViewModel.Workspace;
                Pump(attendantVm.LoginAsync(attendantPassword));
                Require(!attendantVm.IsAuthenticated && attendantVm.Message.StartsWith("Este computador não está ativado para a sua UBS.", StringComparison.Ordinal), "A atendente sem computador deveria receber device-required: " + attendantVm.Message);
                attendantVm.ActivationCode = code; Pump(attendantVm.ActivateWithCodeAsync());
                Require(attendantVm.DeviceActivated && attendantStore.ReadDevice() is not null, "A atendente não ativou o computador com o código: " + attendantVm.Message);
                attendantVm.KeepSignedIn = true; Pump(attendantVm.LoginAsync(attendantPassword));
                Require(attendantVm.IsAuthenticated && attendantStore.ReadSession() is not null && attendantVm.ObsUnits.Count == 1, "A atendente deveria entrar presa à UBS do computador e guardar a sessão: " + attendantVm.Message);
                adminVm.SelectedObsDevice = adminVm.ObsDevices.Single(item => item.Name == "Sala E2E " + suffix); Pump(adminVm.RevokeDeviceAsync());
                Pump(attendantVm.RefreshAutomaticallyAsync()); Pump(attendantVm.DeviceCheckCompletion);
                Require(!attendantVm.IsAuthenticated && !attendantVm.DeviceActivated && attendantStore.ReadDevice() is null && attendantVm.ShowActivation, "Depois da revogação a atendente deveria voltar à tela de ativação: " + attendantVm.Message);
            }
            finally { CloseWindow(attendant); CloseWindow(admin); }
        });
    }
}
