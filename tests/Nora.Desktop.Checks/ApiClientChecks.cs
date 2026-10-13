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
                InvokeButton(Descendants<Button>((FrameworkElement)window.Content).Single(button => button.Content?.ToString() == "Atualizar"));
                PumpUntil(() => !vm.IsBusy);
                Require(!vm.IsBusy && vm.Message.StartsWith("Dados atualizados", StringComparison.Ordinal), "Atualização concluída manteve a mensagem de conexão em andamento.");
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
                foreach (var mode in new[] { Appearance.Light, Appearance.Dark, Appearance.System })
                {
                    window.ViewModel.SetAppearance(mode);
                    var content = Layout(window, 980, 640);
                    if (!profile.IsMunicipal)
                    {
                        var patients = Descendants<DataGrid>(content).Single(grid => AutomationProperties.GetName(grid) == "Pacientes cadastrados");
                        Require(patients.Items.Count == 1 && patients.ActualHeight > 0, "Pacientes não renderizados.");
                        Require(patients.Columns.OfType<DataGridTextColumn>().Single(column => column.Header.ToString() == "CPF").Binding is System.Windows.Data.Binding { Path.Path: "MaskedCpf" }, "CPF foi exposto sem máscara na tabela.");
                    }
                    SavePreview(content, Path.Combine(previewRoot, $"{profile.Id}-authenticated-fixture-{mode.ToString().ToLowerInvariant()}.png"));
                }
                InvokeButton(Descendants<Button>((FrameworkElement)window.Content).Single(button => button.Content?.ToString() == "Sair da conta"));
                PumpUntil(() => !vm.IsBusy);
                Require(vm.IsSignedOut && vm.Patients.Count == 0 && vm.Queue.Count == 0 && fixture.LogoutCalled, "Logout não revogou ou limpou dados.");
                window.ViewModel.Navigate("settings");
                Require(window.ViewModel.IsSettings, "Configurações ficou inacessível após logout.");
            }
            finally { window.ViewModel.ConfirmClose = false; window.Close(); }
        });
        Check("OBS: cadastro, inclusão, cancelamento e vaga por controles nativos", () =>
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
                var patientPassword = Descendants<PasswordBox>(content).Single(input => AutomationProperties.GetName(input) == "Senha inicial do paciente");
                patientPassword.Password = "isolated-initial-password";
                InvokeButton(Descendants<Button>(content).Single(button => button.Content?.ToString() == "Cadastrar paciente"));
                PumpUntil(() => !vm.IsBusy);
                Require(fixture.Registered && vm.Patients.Count == 2 && vm.SelectedPatient?.FullName == "Paciente Novo Fixture" && patientPassword.Password == "", "Cadastro não publicou seleção/limpeza da senha.");
                InvokeButton(Descendants<Button>(content).Single(button => button.Content?.ToString() == "Incluir selecionado na fila"));
                PumpUntil(() => !vm.IsBusy);
                Require(fixture.EnqueuedPatient == ApiFixture.NewPatientId && vm.Queue.Count == 2, "Inclusão perdeu identidade do paciente.");
                var queue = Descendants<DataGrid>(content).Single(grid => AutomationProperties.GetName(grid) == "Fila de Vaga Relâmpago");
                queue.SelectedItem = vm.Queue.Single(entry => entry.PatientId == ApiFixture.NewPatientId);
                Layout(window, 980, 640);
                InvokeButton(Descendants<Button>(content).Single(button => button.Content?.ToString() == "Cancelar entrada selecionada"));
                PumpUntil(() => !vm.IsBusy);
                Require(vm.Queue.Single(entry => entry.PatientId == ApiFixture.NewPatientId).Status == "cancelled", "Cancelamento não atualizou situação da fila.");
                var openSlot = Descendants<Button>(content).Single(button => button.Content?.ToString() == "Abrir vaga");
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
    private static void PumpUntil(Func<bool> complete)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!complete())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("Operação fixture não concluiu.");
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
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
        public bool LogoutCalled { get; private set; }
        public bool Registered { get; private set; }
        public bool UseWorkspaceIdentity { get; init; }
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
                return Reply(new LoginSession("fixture-token", DateTimeOffset.UtcNow.AddHours(1), new(PatientId, accountName, Scope == "obs" ? "admin" : "manager", Scope)));
            }
            Require(LastBearer == "fixture-token", "Rota autenticada sem bearer.");
            if (request.Method == HttpMethod.Get && (FailReads || UnauthorizedReads))
                return new(UnauthorizedReads ? HttpStatusCode.Unauthorized : HttpStatusCode.InternalServerError);
            if (request.Method == HttpMethod.Post && path == "/api/patients")
            {
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
                var patient = new PatientAccount(NewPatientId, body.RootElement.GetProperty("fullName").GetString()!, body.RootElement.GetProperty("cpf").GetString()!, body.RootElement.GetProperty("email").GetString()!);
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
                "/api/patients" => Reply(patients),
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
