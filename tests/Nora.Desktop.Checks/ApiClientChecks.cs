using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using Nora.Desktop.Models;
using Nora.Desktop.Services;
using Nora.Desktop.ViewModels;
using Nora.Desktop.Views;

namespace Nora.Desktop.Checks;

internal static partial class Program
{
    private static void RunApiClientChecks(Application application, string storageRoot, string[] args)
    {
        using var bindingErrors = new BindingErrors();
        PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Error;
        PresentationTraceSources.DataBindingSource.Listeners.Add(bindingErrors);
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
        Check("OBS valida CPF antes do login e consulta CEP sem dados do paciente", () => Task.Run(async () =>
        {
            Require(BrazilianDataValidator.IsValidCpf("529.982.247-25"), "CPF válido foi rejeitado.");
            Require(!BrazilianDataValidator.IsValidCpf("111.111.111-11"), "CPF repetido foi aceito.");
            using var fixture = new ApiFixture();
            using var api = new NoraApiClient(fixture);
            api.Configure("https://fixture.invalid");
            var address = await api.LookupCepAsync("01001-000");
            Require(address.Cep == "01001000" && address.City == "São Paulo" && fixture.CepLookups == 1, "Consulta de CEP não preservou somente o CEP ou o endereço retornado.");
        }).GetAwaiter().GetResult());
        Check("HTTP desktop envia CPF/e-mail, scope e bearer sem persistir sessão", () => Task.Run(async () =>
        {
            using var fixture = new ApiFixture();
            fixture.AddUnit("UBS Contrato");
            using var api = new NoraApiClient(fixture);
            api.Configure("https://fixture.invalid");
            await api.LoginAsync("obs", "fixture@example.invalid", "isolated-test-password");
            Require(fixture.LoginIdentifier == "fixture@example.invalid" && fixture.Scope == "obs", "Login perdeu identificador ou scope.");
            Require((await api.ObsUnitsAsync()).Single().Name == "UBS Contrato", "Contrato das UBS autorizadas inválido.");
            Require(fixture.LastBearer == "fixture-token", "Token não foi enviado no header.");
            await api.LogoutAsync();
            try { await api.ObsUnitsAsync(); }
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
        Check("HTTP desktop escolhe a mensagem por status, code e operação sem exibir o corpo do servidor", () => Task.Run(async () =>
        {
            var unit = Guid.NewGuid();
            // Cada caso: método, rota, status, code do problem+json (ou nulo), trecho esperado na mensagem.
            var cases = new (HttpMethod Method, string Path, HttpStatusCode Status, string? Code, string Expected)[]
            {
                (HttpMethod.Get, $"api/obs/units/{unit}/registry", HttpStatusCode.Forbidden, "forbidden", "concessão à Secretaria"),
                (HttpMethod.Get, "api/municipal/summary", HttpStatusCode.Forbidden, "forbidden", "não tem acesso a este aplicativo"),
                (HttpMethod.Post, $"api/obs/units/{unit}/registry", HttpStatusCode.Conflict, "conflict", "cadastro com este CPF"),
                (HttpMethod.Post, $"api/obs/units/{unit}/appointments", HttpStatusCode.Conflict, "conflict", "Horário em conflito"),
                (HttpMethod.Patch, $"api/obs/units/{unit}/appointments/{Guid.NewGuid()}/status", HttpStatusCode.Conflict, "conflict", "já foi alterada"),
                (HttpMethod.Post, $"api/obs/units/{unit}/waiting-list", HttpStatusCode.Conflict, "conflict", "já está na fila desta especialidade"),
                (HttpMethod.Post, $"api/obs/units/{unit}/lightning-slots", HttpStatusCode.Conflict, "conflict", "Vaga Relâmpago gerada"),
                (HttpMethod.Get, $"api/obs/units/{unit}/waiting-list", HttpStatusCode.ServiceUnavailable, "service-unavailable", "indisponível"),
                (HttpMethod.Get, "api/municipal/summary", HttpStatusCode.ServiceUnavailable, null, "indisponível"),
                (HttpMethod.Get, "api/obs/units", HttpStatusCode.TooManyRequests, "too-many-requests", "Muitas tentativas"),
                (HttpMethod.Get, "api/obs/units", HttpStatusCode.BadRequest, "invalid-input", "Confira os dados"),
                (HttpMethod.Get, "api/obs/units", HttpStatusCode.InternalServerError, "internal-error", "Não foi possível concluir"),
                // Um code desconhecido é ignorado: vale o status.
                (HttpMethod.Get, "api/obs/units", HttpStatusCode.TooManyRequests, "texto-livre-do-servidor", "Muitas tentativas"),
            };
            foreach (var item in cases)
            {
                var handler = new StubHandler(request => request.RequestUri!.AbsolutePath == "/api/auth/login"
                    ? new(HttpStatusCode.OK) { Content = JsonContent.Create(new LoginSession("t", DateTimeOffset.UtcNow.AddHours(1), new(Guid.NewGuid(), "Conta", "attendant", "obs"))) }
                    : new(item.Status) { Content = new StringContent($"{{\"title\":\"private-server-secret\",\"detail\":\"private-server-secret\",\"code\":{(item.Code is null ? "null" : $"\"{item.Code}\"")}}}", System.Text.Encoding.UTF8, "application/problem+json") });
                using var api = new NoraApiClient(handler);
                api.Configure("https://fixture.invalid");
                await api.LoginAsync("obs", "fixture@example.invalid", "isolated-test-password");
                try
                {
                    if (item.Path == "api/municipal/summary") await api.SummaryAsync();
                    else if (item.Path == "api/obs/units") await api.ObsUnitsAsync();
                    else if (item.Method == HttpMethod.Post && item.Path.EndsWith("/registry")) await api.CreateObsRegistryAsync(unit, "Nome", "52998224725", "a@example.test");
                    else if (item.Method == HttpMethod.Post && item.Path.EndsWith("/appointments")) await api.CreateAppointmentAsync(unit, "52998224725", "Clínica", "Profissional", DateTimeOffset.Now, DateTimeOffset.Now.AddHours(1));
                    else if (item.Method == HttpMethod.Patch) await api.ChangeAppointmentStatusAsync(unit, Guid.Parse(item.Path.Split('/')[5]), "called", false);
                    else if (item.Method == HttpMethod.Post && item.Path.EndsWith("/waiting-list")) await api.AddUnitWaitingEntryAsync(unit, "52998224725", "Clínica");
                    else if (item.Method == HttpMethod.Post && item.Path.EndsWith("/lightning-slots")) await api.CreateUnitLightningSlotAsync(unit, Guid.NewGuid());
                    else if (item.Path.EndsWith("/registry")) await api.ObsRegistryAsync(unit);
                    else if (item.Path.EndsWith("/waiting-list")) await api.UnitWaitingListAsync(unit);
                    else throw new InvalidOperationException("Caso sem chamada: " + item.Path);
                }
                catch (NoraApiException error)
                {
                    Require(error.Message.Contains(item.Expected, StringComparison.Ordinal), $"{item.Method} {item.Status}/{item.Code}: mensagem '{error.Message}' não contém '{item.Expected}'.");
                    Require(!error.Message.Contains("private-server-secret") && !error.Unauthorized, "Mensagem revelou o corpo do servidor ou tratou o erro como sessão encerrada.");
                    continue;
                }
                throw new InvalidOperationException($"{item.Method} {item.Status} não gerou erro.");
            }
            // 401 encerra a sessão do cliente.
            var unauthorized = new StubHandler(request => request.RequestUri!.AbsolutePath == "/api/auth/login"
                ? new(HttpStatusCode.OK) { Content = JsonContent.Create(new LoginSession("t", DateTimeOffset.UtcNow.AddHours(1), new(Guid.NewGuid(), "Conta", "attendant", "obs"))) }
                : new(HttpStatusCode.Unauthorized));
            using var session = new NoraApiClient(unauthorized);
            session.Configure("https://fixture.invalid");
            await session.LoginAsync("obs", "fixture@example.invalid", "isolated-test-password");
            try { await session.ObsUnitsAsync(); }
            catch (NoraApiException error) { Require(error.Unauthorized && error.Message.Contains("sessão terminou"), "401 não encerrou a sessão com mensagem própria."); }
            try { await session.ObsUnitsAsync(); }
            catch (NoraApiException error) { Require(error.Unauthorized && unauthorized.Requests.Count(request => request.EndsWith("/api/obs/units")) == 1, "Cliente continuou enviando requisições com a sessão encerrada."); return; }
            throw new InvalidOperationException("Cliente aceitou requisição sem sessão.");
        }).GetAwaiter().GetResult());
        Check("HTTP desktop: 200 com corpo que não é JSON vira erro tratado e o aplicativo continua de pé", () =>
        {
            var login = new Func<HttpRequestMessage, HttpResponseMessage?>(request => request.RequestUri!.AbsolutePath == "/api/auth/login"
                ? new(HttpStatusCode.OK) { Content = JsonContent.Create(new LoginSession("t", DateTimeOffset.UtcNow.AddHours(1), new(Guid.NewGuid(), "Conta HTML", "attendant", "obs"))) } : null);
            // Cliente isolado: HTML de proxy, JSON truncado e tipo errado de conteúdo.
            foreach (var (body, media) in new[] { ("<html><body>proxy-secret</body></html>", "text/html"), ("{\"id\":", "application/json"), ("[]", "text/plain") })
            {
                var handler = new StubHandler(request => login(request) ?? new(HttpStatusCode.OK) { Content = new StringContent(body, System.Text.Encoding.UTF8, media) });
                using var api = new NoraApiClient(handler);
                api.Configure("https://fixture.invalid");
                Pump(api.LoginAsync("obs", "fixture@example.invalid", "isolated-test-password"));
                try { Pump(api.ObsUnitsAsync()); }
                catch (NoraApiException error) { Require(error.Message.Contains("formato inesperado") && !error.Message.Contains("proxy-secret"), "Mensagem de formato inesperado inválida: " + error.Message); continue; }
                throw new InvalidOperationException("Corpo inválido foi aceito como sucesso: " + media);
            }
            // Pela tela: o login funciona, a leitura das UBS falha e a faixa comum mostra o erro sem derrubar nada.
            var stub = new StubHandler(request => login(request) ?? new(HttpStatusCode.OK) { Content = new StringContent("<html>proxy-secret</html>", System.Text.Encoding.UTF8, "text/html") });
            var window = new ShellWindow(DesktopProfile.Operational, Path.Combine(storageRoot, "non-json-ui"), new NoraApiClient(stub));
            try
            {
                window.ViewModel.ConfirmClose = false; window.ViewModel.Animations = false; PrepareWindow(window);
                var vm = window.ViewModel.Workspace; vm.Endpoint = "https://fixture.invalid"; vm.Identifier = "fixture@example.invalid";
                Pump(vm.LoginAsync("isolated-test-password"));
                Require(vm.IsAuthenticated && vm.MessageKind == MessageKind.Error && vm.Message.Contains("formato inesperado") && !vm.Message.Contains("proxy-secret"), "Corpo HTML não virou erro visível: " + vm.Message);
                Require(vm.IsDataStale, "Leitura inválida não marcou os dados como desatualizados.");
                var content = Layout(window, 980, 640);
                Require(Descendants<Border>(content).Any(border => AutomationProperties.GetName(border) == "Retorno da ação" && border.IsVisible), "Erro não apareceu na faixa comum.");
            }
            finally { window.ViewModel.ConfirmClose = false; window.Close(); }
        });
        Check("Secretaria concede e revoga UBS de conta OBS pela API", () =>
        {
            using var fixture = new ApiFixture();
            var window = new ShellWindow(DesktopProfile.Municipal, Path.Combine(storageRoot, "staff-access"), new NoraApiClient(fixture));
            try
            {
                window.ViewModel.ConfirmClose = false; window.ViewModel.Animations = false; PrepareWindow(window);
                var vm = window.ViewModel.Workspace;
                vm.Endpoint = "https://fixture.invalid"; vm.Identifier = "fixture@example.invalid";
                Pump(vm.LoginAsync("isolated-test-password"));
                vm.UnitName = "UBS Teste"; vm.UnitRegion = "Centro"; vm.UnitAddress = "Rua Teste"; vm.UnitLatitude = "-23.5"; vm.UnitLongitude = "-46.6";
                Pump(vm.SaveUnitAsync()); Pump(vm.LoadUnitRegistryAsync());
                Require(vm.UnitRegistry.Count == 2 && vm.UnitRegistry.Any(item => item.FullName == "Paciente Sintético"), "Consulta de pacientes da UBS não atualizou a lista.");
                Pump(vm.LoadMunicipalAppointmentsAsync());
                Require(vm.MunicipalAppointments.Single().PatientName == "Paciente Sintético", "Consulta de agenda da UBS não atualizou a lista.");
                vm.SelectedUnit = null; Require(vm.UnitRegistry.Count == 0 && !vm.UnitRegistryLoaded, "Troca de UBS preservou dados anteriores.");
                Require(vm.MunicipalAppointments.Count == 0 && !vm.MunicipalAppointmentsLoaded, "Troca de UBS preservou consultas anteriores.");
                Pump(vm.LoadStaffAsync());
                vm.SelectedStaff = vm.Staff.Single(); vm.SelectedStaffUnit = vm.Units.Single();
                Pump(vm.ChangeStaffUnitAsync(true)); Require(vm.StaffUnits.Count == 1, "Concessão não atualizou a lista.");
                vm.SelectedStaffGrant = vm.StaffUnits.Single(); Pump(vm.ChangeStaffUnitAsync(false));
                Require(vm.StaffUnits.Count == 0, "Revogação não atualizou a lista.");
                window.ViewModel.Navigate("users"); var content = Layout(window, 980, 640);
                Require(Descendants<StaffAccessView>(content).Any(), "Tela de vínculos não apareceu.");
            }
            finally { window.ViewModel.ConfirmClose = false; window.Close(); }
        });
        Check("Secretaria: formulários de UBS e de vínculos continuam editáveis durante a atualização automática", () =>
        {
            using var fixture = new ApiFixture();
            fixture.AddUnit("UBS Secretaria");
            var window = new ShellWindow(DesktopProfile.Municipal, Path.Combine(storageRoot, "municipal-forms-refresh"), new NoraApiClient(fixture));
            Task? refresh = null;
            try
            {
                window.ViewModel.ConfirmClose = false; window.ViewModel.Animations = false; PrepareWindow(window);
                var vm = window.ViewModel.Workspace; vm.Endpoint = "https://fixture.invalid"; vm.Identifier = "fixture@example.invalid";
                Pump(vm.LoginAsync("isolated-test-password"));
                window.ViewModel.Navigate("units"); Settle(vm);
                var content = Layout(window, 980, 640);
                var unitName = Input(content, "Nome da UBS");
                fixture.PauseReads = new(TaskCreationOptions.RunContinuationsAsynchronously);
                refresh = vm.RefreshAutomaticallyAsync(); PumpUntil(() => vm.IsBusy);
                Layout(window, 980, 640);
                Require(unitName.IsEnabled && vm.CanEditUnit == vm.HasSelectedUnit && vm.CanOperate && !vm.ShowProgress, "Atualização automática desabilitou o formulário de UBS ou acendeu a barra.");
                unitName.Text = "Digitado durante a atualização"; Require(vm.UnitName == unitName.Text, "Formulário de UBS perdeu a digitação durante o ciclo.");
                window.ViewModel.Navigate("users"); content = Layout(window, 980, 640);
                Require(Descendants<DataGrid>(content).Single(grid => AutomationProperties.GetName(grid) == "Contas OBS").IsEnabled && Descendants<ComboBox>(content).Single(box => AutomationProperties.GetName(box) == "UBS para conceder acesso").IsEnabled, "Formulário de vínculos foi desabilitado pelo ciclo automático.");
                fixture.PauseReads.TrySetResult(); Pump(refresh);
            }
            finally { fixture.PauseReads?.TrySetResult(); if (refresh != null) Pump(refresh); window.ViewModel.ConfirmClose = false; window.Close(); }
        });
        foreach (var profile in new[] { DesktopProfile.Operational, DesktopProfile.Municipal })
        Check($"{profile.Name}: login, dados do transporte fixture, temas e logout na janela nativa", () =>
        {
            using var fixture = new ApiFixture();
            var unit = fixture.AddUnit("UBS Única", "Centro");
            fixture.AddRegistry(unit.Id, "Paciente Fixture", "00000000000");
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
                {
                    Require(vm.PatientCount == "1" && vm.WaitingCount == "1", "Municipal perdeu totais agregados.");
                    Require(vm.RegisteredSlotsCount == "3" && vm.CreatedOffersCount == "5" && vm.DeclinedOffersCount == "2" && vm.ExpiredOffersCount == "1", "Estados agregados não foram carregados.");
                    var csv = vm.CreateSummaryCsv();
                    Require(csv.Contains("Ofertas criadas;5;") && csv.Contains("Ofertas recusadas;2;") && !csv.Contains("Paciente Fixture"), "CSV não preservou os totais agregados ou revelou identidade.");
                }
                else
                {
                    Require(vm.ActiveObsUnit?.Name == "UBS Única" && vm.ObsRegistry.Count == 1, "UBS única não foi confirmada com o cadastro carregado.");
                }
                var previewArgument = Array.IndexOf(args, "--preview-dir");
                var previewRoot = previewArgument >= 0 && previewArgument + 1 < args.Length ? args[previewArgument + 1] : Path.Combine(AppContext.BaseDirectory, "previews");
                Directory.CreateDirectory(previewRoot);
                if (!profile.IsMunicipal) window.ViewModel.Navigate("patients");
                foreach (var mode in new[] { Appearance.Light, Appearance.Dark, Appearance.System })
                {
                    window.ViewModel.SetAppearance(mode);
                    var content = Layout(window, 980, 640);
                    if (profile.IsMunicipal)
                    {
                        Require(Descendants<TextBlock>(content).Any(text => text.Text == "Panorama da rede"), "Panorama da Secretaria não renderizou.");
                        Require(Descendants<TextBlock>(content).Any(text => text.Text == "Pacientes aguardando"), "Total de pacientes aguardando não apareceu na Visão geral.");
                        // Os quatro filtros agora são reais: nenhum combo desabilitado nem rótulo "Indisponível".
                        Require(Descendants<ComboBox>(content).Count(box => box.IsEnabled) == 4 && Descendants<ComboBox>(content).All(box => box.IsEnabled), "Os quatro filtros da rede deveriam estar habilitados.");
                        Require(!Descendants<TextBlock>(content).Any(text => text.Text == "Indisponível") && !Descendants<ComboBox>(content).Any(box => box.Items.Count == 1 && box.Items[0] is string),  "Ainda há filtro rotulado como indisponível.");
                    }
                    else
                    {
                        var list = Descendants<ListBox>(content).Single(box => AutomationProperties.GetName(box) == "Cadastros de referência da UBS");
                        Require(list.Items.Count == 1 && list.ActualHeight > 0, "Cadastros da UBS não renderizados.");
                        Require(Descendants<TextBlock>(list).Any(text => text.Text.Contains("000.000.000-00", StringComparison.Ordinal)), "CPF formatado não aparece na listagem da UBS.");
                    }
                    SavePreview(content, Path.Combine(previewRoot, $"{profile.Id}-authenticated-fixture-{mode.ToString().ToLowerInvariant()}.png"));
                }
                if (profile.IsMunicipal)
                {
                    window.ViewModel.Navigate("slots");
                    var slotsContent = Layout(window, 980, 640);
                    Require(Descendants<TextBlock>(slotsContent).Any(text => text.Text == "Ofertas criadas") && Descendants<TextBlock>(slotsContent).Any(text => text.Text == "5"), "Contagens de ofertas não aparecem na tela.");
                    var period = Descendants<ComboBox>(slotsContent).Single(box => AutomationProperties.GetName(box) == "Período das vagas");
                    Require(period.IsEnabled, "Filtro de período não habilitado.");
                    period.SelectedValue = 7;
                    Layout(window, 980, 640);
                    Require(vm.LightningPeriodDays == 7 && vm.LightningCreated == "2" && vm.LightningDeclined == "1", "Filtro não selecionou a coorte correta.");
                    var filteredCsv = vm.CreateSummaryCsv();
                    Require(filteredCsv.Contains("Últimos 7 dias") && filteredCsv.Contains("Ofertas criadas;2;") && filteredCsv.Contains("Pacientes cadastrados;1;"), "CSV não refletiu o período e o escopo dos totais.");
                    window.ViewModel.Navigate("units");
                    Layout(window, 980, 640); PumpUntil(() => !vm.IsBusy);
                    Require(vm.UnitsLoaded && vm.Units.Count == 1, "Cadastro não carregou as unidades da API.");
                    vm.UnitName = "UBS Teste"; vm.UnitRegion = "Centro"; vm.UnitAddress = "Rua de teste, 10"; vm.UnitLatitude = "-23,55"; vm.UnitLongitude = "-46,63";
                    Pump(vm.SaveUnitAsync());
                    Require(vm.Units.Count == 2 && vm.SelectedUnit?.Latitude == -23.55, "Cadastro não salvou coordenadas normalizadas.");
                    vm.EditSelectedUnit(); vm.UnitName = "UBS Teste Atualizada"; Pump(vm.SaveUnitAsync());
                    vm.UnitSearch = "Atualizada"; Require(vm.FilteredUnits.Single().Name == "UBS Teste Atualizada", "Pesquisa de unidade falhou.");
                    vm.UnitSearch = "inexistente"; Require(!vm.FilteredUnits.Any(), "Pesquisa encontrou unidade indevida.");
                    vm.UnitSearch = ""; vm.SelectedUnit = vm.Units.Single(u => u.Name == "UBS Teste Atualizada");
                    var mapAddress = NetworkMapView.MapAddress(vm.SelectedUnit);
                    Require(mapAddress.Contains("marker=-23.55%2C-46.63") && !mapAddress.Contains("UBS"), "Mapa não recebeu somente as coordenadas da unidade.");
                    window.ViewModel.Navigate("network");
                    var mapContent = Layout(window, 980, 640);
                    Require(Descendants<Button>(mapContent).Any(button => button.Content?.ToString() == "Mostrar no mapa"), "Mapa não oferece localização da UBS.");
                    window.ViewModel.Navigate("reports");
                    var reportContent = Layout(window, 980, 640);
                    Require(Descendants<Button>(reportContent).Single(button => button.Content?.ToString() == "Exportar resumo em CSV").IsEnabled, "Exportação não foi habilitada após o carregamento.");
                    var legacyCsv = Nora.Desktop.Services.MunicipalCsv.Create(new MunicipalSummary(1, 1, 1, 0), DateTimeOffset.Now);
                    Require(legacyCsv.Contains("Ofertas criadas;Indisponível;"), "API antiga apresentou dados ausentes como zero.");
                }
                InvokeAccountAction(window, "Sair da conta");
                PumpUntil(() => !vm.IsBusy);
                Require(vm.IsSignedOut && vm.ObsRegistry.Count == 0 && vm.ActiveObsUnit == null && fixture.LogoutCalled, "Logout não revogou ou limpou dados.");
                if (profile.IsMunicipal) Require(!vm.CanExportSummary && vm.RegisteredSlotsCount == "—" && vm.LightningPeriodDays == 0 && vm.Units.Count == 0 && vm.SelectedUnit == null, "Logout manteve dados ou filtro da sessão.");
                window.ViewModel.Navigate("settings");
                Require(window.ViewModel.IsSettings, "Configurações ficou inacessível após logout.");
            }
            finally { window.ViewModel.ConfirmClose = false; window.Close(); }
        });
        RunUnitSelectionChecks(application, storageRoot, args);
        RunUnitFlowChecks(application, storageRoot, args);
        RunUnitIndicatorsChecks(application, storageRoot, args);
        RunSchedulingChecks(application, storageRoot, args);
        RunSchedulingInsightsChecks(application, storageRoot, args);
        RunSecretariaSchedulingChecks(application, storageRoot, args);
        RunCommunityChecks(application, storageRoot, args);
        RunReviewFixesChecks(application, storageRoot, args);
        RunAgendaAndQueueChecks(application, storageRoot, args);
        RunMessageChecks(application, storageRoot, args);
        RunSecretariaChecks(application, storageRoot, args);
        RunSecretariaMetricsChecks(application, storageRoot, args);
        RunPasswordChecks(application, storageRoot, args);
        RunSecretariaCycleChecks(application, storageRoot, args);
        RunPatientInterfaceChecks(application, storageRoot, args);
        RunListingChecks(storageRoot, args);
        Check("Rotas funcionais são restauradas somente no perfil correspondente", () =>
        {
            foreach (var profile in new[] { DesktopProfile.Operational, DesktopProfile.Municipal })
            foreach (var route in profile.IsMunicipal ? new[] { "units", "specialties", "network", "indicators", "slots", "capacity", "alerts", "reports", "communication", "users" } : new[] { "patients", "queue", "slots", "unit-settings", "family", "announcements" })
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
                    var home = Descendants<Button>(content).Single(button => button.CommandParameter?.ToString() == "home" && AutomationProperties.GetName(button) == window.ViewModel.Navigation.First(item => item.Id == "home").Title);
                    var active = Descendants<Button>(content).Single(button => button.CommandParameter?.ToString() == route && AutomationProperties.GetName(button) == window.ViewModel.Navigation.First(item => item.Id == route).Title);
                    Require(!Equals(home.Background, active.Background), "Home continua marcada quando outra rota foi restaurada.");
                }
                finally { window.Close(); }
            }
            var municipalStorage = new PreferencesService("municipal", Path.Combine(storageRoot, "wrong-profile-route"));
            municipalStorage.Save(new() { LastPage = "patients" });
            Require(municipalStorage.Load().LastPage == "home", "Municipal restaurou rota individual do OBS.");
        });
        Check("Telas por UBS, faixa comum e seletores não produzem erros de binding", () => Require(bindingErrors.Messages.Count == 0, string.Join(Environment.NewLine, bindingErrors.Messages)));
        PresentationTraceSources.DataBindingSource.Listeners.Remove(bindingErrors);
    }

    /// <summary>Janela do OBS isolada, ligada ao transporte sintético e pronta para medir; o login fica a cargo de <see cref="LoginObs"/>.</summary>
    private static ShellWindow OpenOperational(ApiFixture fixture, string storageRoot, string name, Appearance? mode = null)
    {
        var window = new ShellWindow(DesktopProfile.Operational, Path.Combine(storageRoot, name), new NoraApiClient(fixture));
        window.ViewModel.ConfirmClose = false; window.ViewModel.Animations = false;
        if (mode is { } appearance) window.ViewModel.SetAppearance(appearance);
        PrepareWindow(window);
        return window;
    }
    private static WorkspaceViewModel LoginObs(ShellWindow window)
    {
        var vm = window.ViewModel.Workspace;
        vm.Endpoint = "https://fixture.invalid"; vm.Identifier = "fixture@example.invalid";
        Pump(vm.LoginAsync("isolated-test-password"));
        return vm;
    }
    private static void Settle(WorkspaceViewModel vm) => PumpUntil(() => !vm.IsBusy);
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
}
