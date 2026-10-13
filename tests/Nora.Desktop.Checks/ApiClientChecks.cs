using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using Nora.Desktop.Models;
using Nora.Desktop.Services;
using Nora.Desktop.Views;

namespace Nora.Desktop.Checks;

internal static partial class Program
{
    private static void RunApiClientChecks(Application application, string storageRoot, string[] args)
    {
        Check("HTTP desktop rejeita transporte inseguro e credenciais no endpoint", () =>
        {
            foreach (var address in new[] { "http://api.example.test", "https://user:secret@api.example.test", "https://api.example.test?secret=value", "file:///tmp/api" })
            {
                try { NoraApiClient.ValidateEndpoint(address); }
                catch (NoraApiException) { continue; }
                throw new InvalidOperationException("Endereço inseguro foi aceito.");
            }
            Require(NoraApiClient.ValidateEndpoint("http://127.0.0.1:5000").IsLoopback, "Loopback de desenvolvimento rejeitado.");
        });
        Check("HTTP desktop envia CPF/e-mail, scope e bearer sem persistir sessão", () => Task.Run(async () =>
        {
            using var fixture = new ApiFixture();
            using var api = new NoraApiClient(fixture);
            api.Configure("https://fixture.invalid");
            await api.LoginAsync("obs", "fixture@example.invalid", "isolated-test-password");
            Require(fixture.LoginIdentifier == "fixture@example.invalid" && fixture.Scope == "obs", "Login perdeu identificador ou scope.");
            Require((await api.PatientsAsync()).Single().MaskedCpf == "***.***.***-00", "Contrato pacientes inválido.");
            Require(fixture.LastBearer == "fixture-token", "Token não foi enviado no header.");
            await api.LogoutAsync();
            try { await api.PatientsAsync(); }
            catch (NoraApiException error) { Require(error.Unauthorized, "Logout não removeu sessão."); return; }
            throw new InvalidOperationException("Sessão continuou válida após logout.");
        }).GetAwaiter().GetResult());
        Check("HTTP desktop sanitiza erro e apaga token mesmo se logout remoto falha", () => Task.Run(async () =>
        {
            using var fixture = new ApiFixture { FailLogout = true };
            using var api = new NoraApiClient(fixture);
            api.Configure("https://fixture.invalid");
            await api.LoginAsync("obs", "00000000000", "isolated-test-password");
            try { await api.LogoutAsync(); }
            catch (NoraApiException error) { Require(!error.Message.Contains("private-server-secret"), "Corpo privado do servidor foi exposto."); }
            try { await api.MeAsync(); }
            catch (NoraApiException error) { Require(error.Unauthorized, "Token preservado após erro de logout."); return; }
            throw new InvalidOperationException("Cliente preservou sessão após erro remoto.");
        }).GetAwaiter().GetResult());
        Check("HTTP desktop trata operação após encerramento sem exceção bruta", () => Task.Run(async () =>
        {
            using var fixture = new ApiFixture();
            var api = new NoraApiClient(fixture);
            api.Configure("https://fixture.invalid"); api.Dispose();
            try { await api.LoginAsync("obs", "fixture@example.invalid", "isolated-test-password"); }
            catch (NoraApiException error) { Require(error.Message.Contains("fechado"), "Encerramento não retornou erro tratado."); return; }
            throw new InvalidOperationException("Cliente encerrado ainda aceitou operação.");
        }).GetAwaiter().GetResult());
        foreach (var profile in new[] { DesktopProfile.Operational, DesktopProfile.Municipal })
        Check($"{profile.Name}: login, dados reais do transporte fixture, temas e logout na janela nativa", () =>
        {
            using var fixture = new ApiFixture();
            var window = new ShellWindow(profile, Path.Combine(storageRoot, "http-ui", profile.Id), new NoraApiClient(fixture));
            try
            {
                window.ViewModel.ConfirmClose = false;
                window.ViewModel.Animations = false;
                PrepareWindow(window);
                var vm = window.ViewModel.Workspace;
                vm.Endpoint = "https://fixture.invalid";
                var loginContent = Layout(window, 980, 640);
                Descendants<TextBox>(loginContent).Single(input => AutomationProperties.GetName(input) == "CPF ou e-mail").Text = "fixture@example.invalid";
                Descendants<PasswordBox>(loginContent).Single(input => AutomationProperties.GetName(input) == "Senha de acesso").Password = "isolated-test-password";
                InvokeButton(Descendants<Button>(loginContent).Single(button => button.Content?.ToString() == "Entrar"));
                PumpUntil(() => !vm.IsBusy);
                Require(vm.IsAuthenticated && vm.AccountName == "Paciente Fixture", "Nome completo/sessão não refletidos na janela.");
                Pump(vm.RefreshAutomaticallyAsync());
                Require(!vm.IsBusy && !vm.HasMessage, "Atualização automática deixou uma mensagem redundante na tela.");
                if (profile.IsMunicipal)
                    Require(vm.PatientCount == "1" && vm.WaitingCount == "1" && vm.Patients.Count == 0, "Municipal perdeu totais ou carregou indivíduos.");
                else
                {
                    Require(vm.Patients.Count == 1 && vm.Queue.Count == 1 && vm.Slots.Count == 1, "Listagens não foram carregadas.");
                    vm.PatientSearch = "NOME INEXISTENTE";
                    Require(!vm.FilteredPatients.Any() && vm.HasNoPatientResults, "Pesquisa não filtrou ou não anunciou ausência de pacientes.");
                    vm.PatientSearch = "Fixture";
                    Require(vm.FilteredPatients.Count() == 1, "Pesquisa não encontrou paciente.");
                    vm.SelectedPatient = vm.Patients.Single();
                    Require(vm.EnqueueCommand.CanExecute(null), "Seleção não habilitou ação da fila.");
                    vm.SelectedQueue = vm.Queue.Single();
                    Require(vm.CancelQueueCommand.CanExecute(null), "Seleção não habilitou cancelamento.");
                    vm.SelectedQueue = vm.Queue.Single() with { Status = "offered" };
                    Require(vm.CancelQueueCommand.CanExecute(null), "Oferta em andamento não permitiu cancelamento previsto na API.");
                }
                var previewArgument = Array.IndexOf(args, "--preview-dir");
                var previewRoot = previewArgument >= 0 && previewArgument + 1 < args.Length ? args[previewArgument + 1] : Path.Combine(AppContext.BaseDirectory, "previews");
                Directory.CreateDirectory(previewRoot);
                if (!profile.IsMunicipal) window.ViewModel.Navigate("patients");
                foreach (var mode in new[] { Appearance.Light, Appearance.Dark, Appearance.System })
                {
                    window.ViewModel.SetAppearance(mode);
                    var content = Layout(window, 980, 640);
                    if (!profile.IsMunicipal)
                    {
                        var patients = Descendants<DataGrid>(content).Single(grid => AutomationProperties.GetName(grid) == "Pacientes cadastrados");
                        Require(patients.Items.Count == 1 && patients.ActualHeight > 0, "Pacientes não renderizados.");
                        Require(Descendants<TextBlock>(patients).Any(text => text.Text.Contains("000.000.000-00", StringComparison.Ordinal)), "CPF completo e formatado não aparece nos contatos da listagem conforme o novo requisito.");
                        Require(!Descendants<TextBlock>(patients).Any(text => text.Text.Contains("***.***.***-00", StringComparison.Ordinal)), "A linha manteve a máscara antiga após o pedido de CPF completo.");
                    }
                    SavePreview(content, Path.Combine(previewRoot, $"{profile.Id}-authenticated-fixture-{mode.ToString().ToLowerInvariant()}.png"));
                    if (!profile.IsMunicipal && mode == Appearance.Light)
                    {
                        var registration = Descendants<Expander>(content).Single();
                        registration.IsExpanded = true;
                        content = Layout(window, 1280, 740);
                        Descendants<Button>(registration).Single(button => button.Content?.ToString() == "Cadastrar paciente").BringIntoView();
                        application.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                        SavePreview(content, Path.Combine(previewRoot, "obs-registration-fixture-light.png"));
                        registration.IsExpanded = false;
                        ((ScrollViewer)window.FindName("PageScroll")).ScrollToTop();
                    }
                }
                InvokeAccountAction(window, "Sair da conta");
                PumpUntil(() => !vm.IsBusy);
                Require(vm.IsSignedOut && vm.Patients.Count == 0 && vm.Queue.Count == 0 && fixture.LogoutCalled, "Logout não revogou ou limpou dados.");
                window.ViewModel.Navigate("settings");
                Require(window.ViewModel.IsSettings, "Configurações ficou inacessível após logout.");
            }
            finally { window.ViewModel.ConfirmClose = false; window.Close(); }
        });
        Check("OBS: cadastro/telefone, inclusão na tela Fila, cancelamento e vaga por controles nativos", () =>
        {
            using var fixture = new ApiFixture();
            var window = new ShellWindow(DesktopProfile.Operational, Path.Combine(storageRoot, "mutation-ui"), new NoraApiClient(fixture));
            try
            {
                window.ViewModel.ConfirmClose = false;
                window.ViewModel.Animations = false;
                PrepareWindow(window);
                var vm = window.ViewModel.Workspace;
                vm.Endpoint = "https://fixture.invalid"; vm.Identifier = "fixture@example.invalid";
                Pump(vm.LoginAsync("isolated-test-password"));
                var content = Layout(window, 980, 640);
                Descendants<Expander>(content).Single().IsExpanded = true;
                Layout(window, 980, 640);
                Descendants<TextBox>(content).Single(input => AutomationProperties.GetName(input) == "Nome completo do paciente").Text = "Paciente Novo Fixture";
                Descendants<TextBox>(content).Single(input => AutomationProperties.GetName(input) == "CPF do paciente").Text = "00000000001";
                Descendants<TextBox>(content).Single(input => AutomationProperties.GetName(input) == "E-mail do paciente").Text = "new@example.invalid";
                Descendants<TextBox>(content).Single(input => AutomationProperties.GetName(input) == "Telefone do paciente").Text = "(48) 99900-0001";
                var patientPassword = Descendants<PasswordBox>(content).Single(input => AutomationProperties.GetName(input) == "Senha inicial do paciente");
                patientPassword.Password = "isolated-initial-password";
                InvokeButton(Descendants<Button>(content).Single(button => button.Content?.ToString() == "Cadastrar paciente"));
                PumpUntil(() => !vm.IsBusy);
                Require(fixture.Registered && fixture.RegisteredPhone == "(48) 99900-0001" && vm.SelectedPatient?.Phone == fixture.RegisteredPhone && vm.Phone == "" && vm.Patients.Count == 2 && vm.SelectedPatient?.FullName == "Paciente Novo Fixture" && patientPassword.Password == "", "Cadastro não preservou telefone, seleção ou limpeza do formulário/senha.");
                window.ViewModel.Navigate("queue");
                content = Layout(window, 980, 640);
                Descendants<ComboBox>(content).Single(input => AutomationProperties.GetName(input) == "Paciente para incluir na fila").SelectedItem = vm.Patients.Single(patient => patient.Id == ApiFixture.NewPatientId);
                InvokeButton(FindActionButton(content, "Incluir selecionado na fila"));
                PumpUntil(() => !vm.IsBusy);
                Require(fixture.EnqueuedPatient == ApiFixture.NewPatientId && vm.Queue.Count == 2, "Inclusão perdeu identidade do paciente.");
                var queue = Descendants<DataGrid>(content).Single(grid => AutomationProperties.GetName(grid) == "Fila de Vaga Relâmpago");
                queue.SelectedItem = vm.Queue.Single(entry => entry.PatientId == ApiFixture.NewPatientId);
                Layout(window, 980, 640);
                InvokeButton(FindActionButton(content, "Cancelar entrada selecionada"));
                PumpUntil(() => !vm.IsBusy);
                Require(vm.Queue.Single(entry => entry.PatientId == ApiFixture.NewPatientId).Status == "cancelled", "Cancelamento não atualizou situação da fila.");
                window.ViewModel.Navigate("slots");
                content = Layout(window, 980, 640);
                var openSlot = FindActionButton(content, "Abrir vaga");
                Require(!openSlot.IsEnabled, "Vaga vazia pode ser criada.");
                Descendants<TextBox>(content).Single(input => AutomationProperties.GetName(input) == "Descrição da vaga").Text = "Vaga nova fixture";
                Layout(window, 980, 640);
                Require(openSlot.IsEnabled, "Digitar descrição não habilitou vaga.");
                InvokeButton(openSlot); PumpUntil(() => !vm.IsBusy);
                Require(fixture.CreatedSlotLabel == "Vaga nova fixture" && vm.Slots.Count == 2 && vm.SlotLabel == "", "Vaga não foi criada ou formulário preservou descrição.");
            }
            finally { window.ViewModel.ConfirmClose = false; window.Close(); }
        });
        Check("OBS: mutação confirmada é diferenciada da falha ao recarregar", () =>
        {
            using var fixture = new ApiFixture();
            var window = new ShellWindow(DesktopProfile.Operational, Path.Combine(storageRoot, "mutation-refresh"), new NoraApiClient(fixture));
            try
            {
                window.ViewModel.ConfirmClose = false; PrepareWindow(window);
                var vm = window.ViewModel.Workspace;
                vm.Endpoint = "https://fixture.invalid"; vm.Identifier = "fixture@example.invalid";
                Pump(vm.LoginAsync("isolated-test-password"));
                fixture.FailReads = true;
                vm.SlotLabel = "Vaga confirmada fixture";
                vm.CreateSlotCommand.Execute(null); PumpUntil(() => !vm.IsBusy);
                Require(vm.Message.StartsWith("Vaga aberta.") && vm.Message.Contains("antes de repetir") && fixture.CreatedSlotLabel != null, "Erro de recarga ocultou mutação confirmada.");
                fixture.FailReads = false; fixture.UnauthorizedReads = true;
                vm.RefreshCommand.Execute(null); PumpUntil(() => !vm.IsBusy);
                Require(vm.IsSignedOut && vm.Patients.Count == 0 && vm.Queue.Count == 0 && vm.Slots.Count == 0, "401 deixou dados da sessão anterior visíveis.");
            }
            finally { window.ViewModel.ConfirmClose = false; window.Close(); }
        });
        Check("OBS: atualização automática preserva seleção/rascunho e recupera falha; 401 limpa sessão", () =>
        {
            using var fixture = new ApiFixture();
            var window = new ShellWindow(DesktopProfile.Operational, Path.Combine(storageRoot, "automatic-refresh"), new NoraApiClient(fixture));
            try
            {
                window.ViewModel.ConfirmClose = false; window.ViewModel.Animations = false;
                PrepareWindow(window);
                var vm = window.ViewModel.Workspace;
                vm.Endpoint = "https://fixture.invalid"; vm.Identifier = "fixture@example.invalid";
                Pump(vm.LoginAsync("isolated-test-password"));
                vm.SelectedPatient = vm.Patients.Single(); vm.SelectedQueue = vm.Queue.Single();
                vm.FullName = "Rascunho não enviado"; vm.Cpf = "01234567890";
                vm.Email = "draft@example.invalid"; vm.Phone = "(48) 99900-0099";
                var patientId = vm.SelectedPatient.Id; var queueId = vm.SelectedQueue.Id;
                fixture.PatientOnline = true;
                Pump(vm.RefreshAutomaticallyAsync());
                Require(vm.SelectedPatient?.Id == patientId && vm.SelectedPatient.IsOnline && vm.SelectedQueue?.Id == queueId, "Atualização perdeu seleção ou presença atualizada.");
                Require(vm.FullName == "Rascunho não enviado" && vm.Cpf == "01234567890" && vm.Email == "draft@example.invalid" && vm.Phone == "(48) 99900-0099", "Atualização apagou cadastro não enviado.");
                fixture.FailReads = true;
                Pump(vm.RefreshAutomaticallyAsync());
                Require(vm.IsAuthenticated && vm.HasMessage && vm.Patients.Count == 1 && vm.SelectedPatient?.Id == patientId, "Falha de leitura perdeu dados anteriores ou feedback.");
                fixture.FailReads = false;
                Pump(vm.RefreshAutomaticallyAsync());
                Require(!vm.HasMessage && vm.SelectedPatient?.Id == patientId, "Recuperação manteve erro antigo ou perdeu seleção.");
                fixture.UnauthorizedReads = true;
                Pump(vm.RefreshAutomaticallyAsync());
                Require(vm.IsSignedOut && vm.Patients.Count == 0 && vm.Queue.Count == 0 && vm.FullName == "" && vm.Phone == "", "401 de atualização deixou dados pessoais ou rascunhos na sessão.");
                var calls = fixture.PatientReads;
                Pump(vm.RefreshAutomaticallyAsync());
                Require(fixture.PatientReads == calls, "Atualização continuou usando a sessão encerrada.");
            }
            finally { window.Close(); }
        });
        Check("OBS: pesquisa por contato, filtros de presença e ordenação não confundem online com cadastro", () =>
        {
            var window = new ShellWindow(DesktopProfile.Operational, Path.Combine(storageRoot, "patient-filters"));
            try
            {
                window.ViewModel.ConfirmClose = false;
                var vm = window.ViewModel.Workspace;
                var offline = new PatientAccount(Guid.NewGuid(), "Zélia Fixture", "00000000001", "zelia@example.invalid");
                var online = new PatientAccount(Guid.NewGuid(), "Ana Fixture", "00000000002", "ana@example.invalid", "48999000002", true);
                vm.Patients.Add(offline); vm.Patients.Add(online);
                vm.PatientSearch = "999000002";
                Require(vm.FilteredPatients.Single().Id == online.Id, "Pesquisa não encontrou telefone.");
                vm.PatientSearch = ""; vm.PatientPresenceFilter = "Online";
                Require(vm.FilteredPatients.Single().Id == online.Id, "Online incluiu paciente offline.");
                vm.PatientPresenceFilter = "Offline";
                Require(vm.FilteredPatients.Single().Id == offline.Id && offline.PhoneDisplay == "Telefone não informado", "Offline ou compatibilidade sem telefone incorretos.");
                vm.PatientPresenceFilter = "Todos"; vm.PatientSort = "Nome A–Z";
                Require(vm.FilteredPatients.First().Id == online.Id, "Ordenação crescente divergente.");
                vm.PatientSort = "Nome Z–A";
                Require(vm.FilteredPatients.First().Id == offline.Id, "Ordenação decrescente divergente.");
                Require(online.PresenceDisplay == "Online no APPNORA" && offline.PresenceDisplay == "Offline no APPNORA" && online.ContactDisplay.Contains(online.Phone!), "Texto de presença/contatos não corresponde ao contrato.");
            }
            finally { window.Close(); }
        });
        Check("OBS: polling preserva paciente da Fila fora do filtro da tela Pacientes", () =>
        {
            using var fixture = new ApiFixture();
            var window = new ShellWindow(DesktopProfile.Operational, Path.Combine(storageRoot, "queue-filter-refresh"), new NoraApiClient(fixture));
            try
            {
                window.ViewModel.ConfirmClose = false; PrepareWindow(window);
                var vm = window.ViewModel.Workspace;
                vm.Endpoint = "https://fixture.invalid"; vm.Identifier = "fixture@example.invalid";
                Pump(vm.LoginAsync("isolated-test-password"));
                window.ViewModel.Navigate("patients");
                vm.PatientPresenceFilter = "Online";
                window.ViewModel.Navigate("queue");
                var content = Layout(window, 980, 640);
                var input = Descendants<ComboBox>(content).Single(input => AutomationProperties.GetName(input) == "Paciente para incluir na fila");
                input.SelectedItem = vm.Patients.Single();
                var selectedId = vm.SelectedPatient?.Id;
                Require(selectedId != null && !vm.FilteredPatients.Any(), "Reprodução não excluiu o paciente da Fila pelo filtro de presença.");
                Pump(vm.RefreshAutomaticallyAsync());
                Layout(window, 980, 640);
                Require(vm.SelectedPatient?.Id == selectedId && (input.SelectedItem as PatientAccount)?.Id == selectedId, "Polling perdeu paciente da Fila porque o filtro de Pacientes exclui pacientes offline.");
            }
            finally { window.Close(); }
        });
        Check("OBS: polling recupera leitura inicial sem manter erro do login", () =>
        {
            using var fixture = new ApiFixture { FailReads = true };
            var window = new ShellWindow(DesktopProfile.Operational, Path.Combine(storageRoot, "initial-read-recovery"), new NoraApiClient(fixture));
            try
            {
                window.ViewModel.ConfirmClose = false; PrepareWindow(window);
                var vm = window.ViewModel.Workspace;
                vm.Endpoint = "https://fixture.invalid"; vm.Identifier = "fixture@example.invalid";
                Pump(vm.LoginAsync("isolated-test-password"));
                Require(vm.IsAuthenticated && vm.HasMessage && vm.Patients.Count == 0, "Reprodução não autenticou antes da falha inicial de leitura.");
                fixture.FailReads = false;
                Pump(vm.RefreshAutomaticallyAsync());
                Require(vm.Patients.Count == 1 && !vm.HasMessage, "Polling recuperou dados, mas deixou erro antigo do login visível.");
            }
            finally { window.Close(); }
        });
        Check("OBS: cadastro continua editável durante atualização automática sem permitir envio simultâneo", () =>
        {
            using var fixture = new ApiFixture();
            var window = new ShellWindow(DesktopProfile.Operational, Path.Combine(storageRoot, "draft-during-refresh"), new NoraApiClient(fixture));
            Task? refresh = null;
            var previousContext = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(window.Dispatcher));
            try
            {
                window.ViewModel.ConfirmClose = false; PrepareWindow(window);
                var vm = window.ViewModel.Workspace;
                vm.Endpoint = "https://fixture.invalid"; vm.Identifier = "fixture@example.invalid";
                Pump(vm.LoginAsync("isolated-test-password"));
                window.ViewModel.Navigate("patients");
                var content = Layout(window, 980, 640);
                Descendants<Expander>(content).Single().IsExpanded = true;
                Layout(window, 980, 640);
                var name = Input(content, "Nome completo do paciente");
                name.Text = "Nome digitado antes do polling";
                Require(name.IsEnabled, "Cadastro estava desabilitado antes da reprodução.");
                fixture.PauseReads = new(TaskCreationOptions.RunContinuationsAsynchronously);
                refresh = vm.RefreshAutomaticallyAsync();
                PumpUntil(() => vm.IsBusy);
                Layout(window, 980, 640);
                Require(name.IsEnabled && name.Focusable, "Polling desabilitou cadastro e interrompeu digitação/foco.");
                name.Text = "Nome digitado durante o polling";
                Require(vm.FullName == name.Text, "Cadastro não recebeu digitação durante polling.");
                var submit = FindActionButton(content, "Cadastrar paciente");
                Require(!submit.IsEnabled, "Cadastro permite envio simultâneo à atualização automática.");
                fixture.PauseReads.TrySetResult();
                Pump(refresh);
                Require(vm.FullName == "Nome digitado durante o polling" && name.IsEnabled, "Atualização perdeu digitação após concluir.");
            }
            finally
            {
                fixture.PauseReads?.TrySetResult();
                if (refresh != null) Pump(refresh);
                window.Close();
                SynchronizationContext.SetSynchronizationContext(previousContext);
            }
        });
        RunPatientInterfaceChecks(application, storageRoot, args);
        RunListingChecks(storageRoot, args);
        Check("Rotas funcionais são restauradas somente no perfil correspondente", () =>
        {
            foreach (var profile in new[] { DesktopProfile.Operational, DesktopProfile.Municipal })
            foreach (var route in profile.IsMunicipal ? new[] { "indicators" } : new[] { "patients", "queue", "slots" })
            {
                var path = Path.Combine(storageRoot, "routes", profile.Id, route);
                var storage = new PreferencesService(profile.Id, path);
                Require(storage.Save(new() { LastPage = route }), "Rota não foi salva.");
                var window = new ShellWindow(profile, path);
                try
                {
                    window.ViewModel.ConfirmClose = false;
                    Require(window.ViewModel.Page == route && window.ViewModel.Workspace.Section == route, "Rota/seção divergiram na reabertura.");
                    PrepareWindow(window);
                    var content = Layout(window, 980, 640);
                    var home = Descendants<Button>(content).Single(button => button.CommandParameter?.ToString() == "home");
                    var active = Descendants<Button>(content).Single(button => button.CommandParameter?.ToString() == route);
                    Require(!Equals(home.Background, active.Background), "Home continua marcada quando outra rota foi restaurada.");
                }
                finally { window.Close(); }
            }
            var municipalStorage = new PreferencesService("municipal", Path.Combine(storageRoot, "wrong-profile-route"));
            municipalStorage.Save(new() { LastPage = "patients" });
            Require(municipalStorage.Load().LastPage == "home", "Municipal restaurou rota individual do OBS.");
        });
    }
    private static void Pump(Task task)
    {
        PumpUntil(() => task.IsCompleted);
        task.GetAwaiter().GetResult();
    }
    private static void InvokeAccountAction(ShellWindow window, string header)
    {
        var account = Descendants<Button>((FrameworkElement)window.Content).Single(button => AutomationProperties.GetName(button) == "Menu da conta NORA");
        InvokeButton(account);
        var menu = account.ContextMenu ?? throw new InvalidOperationException("Menu da conta ausente.");
        Require(menu.IsOpen && menu.Items.Count == 2, "Conta não abriu exatamente Configurações e Sair.");
        var item = menu.Items.Cast<MenuItem>().Single(item => item.Header?.ToString() == header);
        Require(item.IsEnabled && item.Command?.CanExecute(item.CommandParameter) == true, "Ação do menu indisponível: " + header);
        var peer = new System.Windows.Automation.Peers.MenuItemAutomationPeer(item);
        ((System.Windows.Automation.Provider.IInvokeProvider)peer.GetPattern(System.Windows.Automation.Peers.PatternInterface.Invoke)).Invoke();
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        menu.IsOpen = false;
    }
    private static void PumpUntil(Func<bool> complete)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!complete())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("Operação fixture não concluiu.");
            var frame = new DispatcherFrame();
            // Modal inspectors may capture ApplicationIdle; finish below that priority
            // so their asynchronous continuations are not starved by the test pump.
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.SystemIdle, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
        }
    }
    private sealed class ApiFixture : HttpMessageHandler
    {
        private static readonly Guid PatientId = Guid.Parse("0fc6ec25-fb80-4cdd-881b-000000000001");
        public static readonly Guid NewPatientId = Guid.Parse("0fc6ec25-fb80-4cdd-881b-000000000002");
        private readonly List<PatientAccount> patients = [new(PatientId, "Paciente Fixture", "00000000000", "fixture@example.invalid")];
        private readonly List<QueueEntry> queue = [new(1, PatientId, "Paciente Fixture", DateTimeOffset.UtcNow, "waiting")];
        private readonly List<LightningSlot> slots = [new(Guid.NewGuid(), "Vaga exclusiva fixture", "open", DateTimeOffset.UtcNow)];
        public string? LoginIdentifier { get; private set; }
        public string Scope { get; private set; } = "obs";
        public string? LastBearer { get; private set; }
        public bool FailLogout { get; init; }
        public string ObsRole { get; set; } = "administrator";
        public List<bool> ActiveChanges { get; } = [];
        public bool LogoutCalled { get; private set; }
        public bool Registered { get; private set; }
        public bool UseWorkspaceIdentity { get; init; }

        public string? RegisteredPhone { get; private set; }
        public bool PatientOnline { get; set; }
        public int PatientReads { get; private set; }
        public TaskCompletionSource? PauseReads { get; set; }
        public void AddPatient(PatientAccount patient) => patients.Add(patient);
        public Guid? EnqueuedPatient { get; private set; }
        public string? CreatedSlotLabel { get; private set; }
        public bool FailReads { get; set; }
        public bool UnauthorizedReads { get; set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastBearer = request.Headers.Authorization?.Parameter;
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/api/auth/login")
            {
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
                LoginIdentifier = body.RootElement.GetProperty("identifier").GetString();
                Scope = body.RootElement.GetProperty("scope").GetString()!;
                Require(!string.IsNullOrEmpty(body.RootElement.GetProperty("password").GetString()), "Senha não enviada.");
                var accountName = UseWorkspaceIdentity ? Scope == "obs" ? "Operador Fixture" : "Gestor Fixture" : "Paciente Fixture";
                return Reply(new LoginSession("fixture-token", DateTimeOffset.UtcNow.AddHours(1), new(PatientId, accountName, Scope == "obs" ? ObsRole : "manager", Scope)));
            }
            Require(LastBearer == "fixture-token", "Rota autenticada sem bearer.");
            if (request.Method == HttpMethod.Get && PauseReads != null) await PauseReads.Task.WaitAsync(cancellationToken);
            if (request.Method == HttpMethod.Get && path == "/api/patients") PatientReads++;
            if (request.Method == HttpMethod.Get && (FailReads || UnauthorizedReads))
                return new(UnauthorizedReads ? HttpStatusCode.Unauthorized : HttpStatusCode.InternalServerError);
            if (request.Method == HttpMethod.Patch && path.StartsWith("/api/patients/", StringComparison.Ordinal) && path.EndsWith("/active", StringComparison.Ordinal))
            {
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
                var id = Guid.Parse(path.Split('/')[3]);
                var active = body.RootElement.GetProperty("isActive").GetBoolean();
                var index = patients.FindIndex(patient => patient.Id == id);
                Require(index >= 0, "Alteração tentou usar identidade de paciente ausente.");
                patients[index] = patients[index] with { IsActive = active, IsOnline = active && patients[index].IsOnline };
                ActiveChanges.Add(active);
                return new(HttpStatusCode.NoContent);
            }
            if (request.Method == HttpMethod.Post && path == "/api/patients")
            {
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
                RegisteredPhone = body.RootElement.TryGetProperty("phone", out var phone) ? phone.GetString() : null;
                var patient = new PatientAccount(NewPatientId, body.RootElement.GetProperty("fullName").GetString()!, body.RootElement.GetProperty("cpf").GetString()!, body.RootElement.GetProperty("email").GetString()!, RegisteredPhone);
                Require(body.RootElement.GetProperty("password").GetString() == "isolated-initial-password", "Cadastro não enviou senha inicial.");
                patients.Add(patient); Registered = true;
                return Reply(patient);
            }
            if (request.Method == HttpMethod.Post && path == "/api/queue")
            {
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
                EnqueuedPatient = body.RootElement.GetProperty("patientId").GetGuid();
                var entry = new QueueEntry(2, EnqueuedPatient.Value, patients.Single(p => p.Id == EnqueuedPatient).FullName, DateTimeOffset.UtcNow, "waiting");
                queue.Add(entry); return Reply(entry);
            }
            if (request.Method == HttpMethod.Post && path == "/api/queue/2/cancel")
            {
                var index = queue.FindIndex(entry => entry.Id == 2);
                queue[index] = queue[index] with { Status = "cancelled" };
                return new(HttpStatusCode.NoContent);
            }
            if (request.Method == HttpMethod.Post && path == "/api/slots")
            {
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
                CreatedSlotLabel = body.RootElement.GetProperty("label").GetString()!;
                var slot = new LightningSlot(Guid.NewGuid(), CreatedSlotLabel, "open", DateTimeOffset.UtcNow);
                slots.Add(slot); return Reply(slot);
            }
            return path switch
            {
                "/api/patients" => Reply(patients.Select(patient => patient.Id == PatientId ? patient with { IsOnline = PatientOnline && patient.IsActive } : patient)),
                "/api/queue" => Reply(queue),
                "/api/slots" => Reply(slots),
                "/api/municipal/summary" => Reply(new MunicipalSummary(1, 1, 1, 0)),
                "/api/auth/logout" => Logout(),
                _ => throw new InvalidOperationException("Rota inesperada da fixture: " + path)
            };
        }
        private HttpResponseMessage Logout()
        {
            LogoutCalled = true;
            return FailLogout ? new(HttpStatusCode.InternalServerError) { Content = new StringContent("private-server-secret") } : new(HttpStatusCode.NoContent);
        }
        private static HttpResponseMessage Reply<T>(T payload) => new(HttpStatusCode.OK) { Content = JsonContent.Create(payload) };
    }
}
