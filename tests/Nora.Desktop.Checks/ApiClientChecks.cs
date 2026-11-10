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
        Check("OBS valida CPF antes do login e cadastro e consulta CEP sem dados do paciente", () => Task.Run(async () =>
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
        Check("OBS confirma UBS e cadastra paciente no registro da unidade", () =>
        {
            using var fixture = new ApiFixture();
            fixture.AddUnit(new HealthUnit(Guid.NewGuid(), "UBS de teste", "Centro", "Rua de teste", -23.5, -46.6));
            fixture.AddUnit(new HealthUnit(Guid.NewGuid(), "UBS distante", "Norte", "Outra rua", -20.0, -40.0));
            var window = new ShellWindow(DesktopProfile.Operational, Path.Combine(storageRoot, "obs-unit-registry"), new NoraApiClient(fixture));
            try
            {
                window.ViewModel.ConfirmClose = false; window.ViewModel.Animations = false; PrepareWindow(window);
                var vm = window.ViewModel.Workspace; vm.Endpoint = "https://fixture.invalid"; vm.Identifier = "fixture@example.invalid";
                Pump(vm.LoginAsync("isolated-test-password")); Pump(vm.LoadObsUnitsAsync());
                vm.SuggestClosestObsUnit(-23.49, -46.61);
                Require(vm.CandidateObsUnit?.Name == "UBS de teste" && vm.ActiveObsUnit == null, "Localização não sugeriu UBS autorizada ou confirmou sem ação da equipe.");
                Pump(vm.ConfirmObsUnitAsync());
                Require(!vm.ObsUnitSelectionOpen, "Seletor da UBS não recolheu após a confirmação.");
                Require(vm.QueueWaitingCount == "—" && vm.SlotOpenCount == "—", "Dados ainda não consultados apareceram como zero no painel.");
                Require(vm.TodayCompletedCount == "—" && vm.TodayScheduledCount == "—", "Resumo da agenda não carregada apareceu como zero.");
                Pump(vm.LoadUnitWaitingListAsync()); Pump(vm.LoadUnitLightningSlotsAsync());
                Require(vm.QueueWaitingCount == "0" && vm.SlotOpenCount == "0", "Listas vazias consultadas não apareceram como zero no painel.");
                fixture.AddTodayAppointment("scheduled"); fixture.AddTodayAppointment("called"); fixture.AddTodayAppointment("completed"); fixture.AddTodayAppointment("missed"); fixture.AddTodayAppointment("cancelled");
                vm.RefreshCommand.Execute(null); PumpUntil(() => !vm.IsBusy);
                Require(vm.TodayScheduledCount == "2" && vm.TodayCompletedCount == "1" && vm.TodayMissedCount == "1" && vm.TodayCancelledCount == "1", "Painel não refletiu situações das consultas do dia.");
                vm.ObsRegistryName = "Paciente Sintético"; vm.ObsRegistryCpf = "529.982.247-25"; vm.ObsRegistryEmail = "patient@example.test"; vm.ObsRegistryPhone = "48999990028";
                vm.ObsRegistryCep = "01001-000"; Pump(vm.LookupObsRegistryCepAsync()); vm.ObsRegistryNumber = "10";
                Pump(vm.CreateObsRegistryAsync());
                Require(vm.ObsRegistry.Single().FullName == "Paciente Sintético" && vm.ObsRegistry.Single().Phone == "48999990028" && vm.ObsRegistry.Single().Address?.Cep == "01001000", "Cadastro não voltou com contato e endereço da UBS selecionada.");
                Require(vm.ObsRegistryName == "" && vm.ActiveObsUnit?.Name == "UBS de teste", "Formulário ou unidade não foram preservados corretamente.");
                vm.ObsRegistrySearch = "529.982.247-25"; Require(vm.FilteredObsRegistry.Count() == 1, "Pesquisa não encontrou CPF formatado.");
                vm.ObsRegistrySearch = "(48) 99999-0028"; Require(vm.FilteredObsRegistry.Count() == 1, "Pesquisa não encontrou telefone formatado.");
                vm.ObsRegistrySearch = "não encontrado"; Require(!vm.FilteredObsRegistry.Any(), "Pesquisa retornou paciente sem correspondência."); vm.ObsRegistrySearch = "";
                vm.SelectedObsRegistry = vm.ObsRegistry.Single(); vm.CorrectedObsEmail = "corrected@example.test";
                Pump(vm.UpdateObsRegistryEmailAsync());
                Require(vm.ObsRegistry.Single().Email == "corrected@example.test" && vm.SelectedObsRegistry == null, "Correção de e-mail não atualizou a lista ou manteve seleção antiga.");
                vm.SelectedObsRegistry = vm.ObsRegistry.Single(); vm.EditSelectedObsRegistry();
                Require(vm.IsEditingObsRegistry && vm.ObsRegistryCpf == "529.982.247-25", "Edição não carregou CPF do cadastro selecionado.");
                vm.ObsRegistryName = "Paciente Atualizado"; vm.ObsRegistryNumber = "20"; Pump(vm.CreateObsRegistryAsync());
                Require(vm.ObsRegistry.Single().FullName == "Paciente Atualizado" && vm.ObsRegistry.Single().Address?.Number == "20" && !vm.IsEditingObsRegistry, "Edição não persistiu nome e endereço.");
                vm.WaitingPatient = vm.ObsRegistry.Single(); vm.WaitingSpecialty = "Cardiologia"; Pump(vm.AddUnitWaitingEntryAsync());
                Require(vm.UnitWaitingList.Single().Specialty == "Cardiologia", "Fila da UBS não recebeu especialidade.");
                vm.SelectedWaitingEntry = vm.UnitWaitingList.Single(); Pump(vm.CancelUnitWaitingEntryAsync());
                Require(vm.UnitWaitingList.Single().Status == "cancelled", "Cancelamento da fila da UBS não foi refletido.");
                var starts = DateTimeOffset.UtcNow.AddDays(1);
                vm.SelectedAppointment = new AppointmentInfo(Guid.NewGuid(), "52998224725", "Paciente Sintético", "Cardiologia", "Profissional", starts, starts.AddMinutes(30), "cancelled");
                Pump(vm.CreateUnitLightningSlotAsync()); Require(vm.UnitLightningSlots.Single().AppointmentId == vm.SelectedAppointment.Id, "Vaga não foi criada para a consulta selecionada.");
                vm.SelectedObsRegistry = vm.ObsRegistry.Single(); Pump(vm.SetObsRegistryActiveAsync());
                Require(vm.SelectedObsRegistry?.IsActive == false && !vm.ActiveObsRegistry.Any(), "Desativação não foi refletida na UBS.");
                vm.ObsRegistryStatusFilter = "Ativos"; Require(!vm.FilteredObsRegistry.Any(), "Filtro exibiu paciente desativado.");
                vm.ObsRegistryStatusFilter = "Desativados"; Require(vm.FilteredObsRegistry.Count() == 1, "Filtro não exibiu paciente desativado.");
                vm.ObsRegistryPresenceFilter = "Online"; Require(!vm.FilteredObsRegistry.Any(), "Filtro de presença mostrou paciente offline.");
                vm.ObsRegistryPresenceFilter = "Offline"; Require(vm.FilteredObsRegistry.Count() == 1, "Filtro não mostrou paciente offline.");
                vm.ObsRegistryPresenceFilter = "Todos";
                Pump(vm.SetObsRegistryActiveAsync()); vm.ObsRegistryStatusFilter = "Todos";
                Require(vm.SelectedObsRegistry?.IsActive == true && vm.ActiveObsRegistry.Count() == 1, "Reativação não foi refletida na UBS.");
                Require(vm.BuildDetails(vm.SelectedObsRegistry!)?.Fields.Any(field => field.Label == "CPF" && field.CanCopy) == true, "Ficha da UBS não oferece CPF para cópia explícita.");
                window.ViewModel.Navigate("patients"); var content = Layout(window, 980, 640);
                Require(Descendants<ObsUnitRegistryView>(content).Any(), "Tela de cadastro por UBS não apareceu.");
                var unitSelector = Descendants<Expander>(content).Single(expander => expander.Header?.ToString() == "Escolher ou trocar UBS");
                Require(!unitSelector.IsExpanded, "Escolha da UBS permaneceu aberta após confirmação.");
                unitSelector.IsExpanded = true; Layout(window, 980, 640);
                Require(vm.ObsUnitSelectionOpen && Descendants<Button>(unitSelector).Any(button => button.Content?.ToString() == "Confirmar UBS" && button.IsVisible), "Não é possível reabrir a escolha da UBS.");
                window.ViewModel.Navigate("queue"); content = Layout(window, 980, 640);
                Require(Descendants<UnitQueueView>(content).Any() && Descendants<DataGrid>(content).Any(grid => AutomationProperties.GetName(grid) == "Fila por especialidade da UBS" && grid.Items.Count == 1), "Fila por UBS não apareceu na tela.");
                window.ViewModel.Navigate("slots"); content = Layout(window, 980, 640);
                Require(Descendants<UnitLightningSlotsView>(content).Any() && Descendants<DataGrid>(content).Any(grid => AutomationProperties.GetName(grid) == "Vagas da UBS" && grid.Items.Count == 1), "Vagas por UBS não apareceram na tela.");
                vm.LogoutCommand.Execute(null); PumpUntil(() => !vm.IsBusy);
                Require(vm.ObsRegistry.Count == 0 && vm.ActiveObsUnit == null, "Saída preservou dados da UBS.");
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
                Require(vm.UnitRegistry.Single().FullName == "Paciente Sintético", "Consulta de pacientes da UBS não atualizou a lista.");
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
                    {
                    Require(vm.PatientCount == "1" && vm.WaitingCount == "1" && vm.Patients.Count == 0, "Municipal perdeu totais ou carregou indivíduos.");
                    Require(vm.RegisteredSlotsCount == "3" && vm.CreatedOffersCount == "5" && vm.DeclinedOffersCount == "2" && vm.ExpiredOffersCount == "1", "Estados agregados não foram carregados.");
                    var csv = vm.CreateSummaryCsv();
                    Require(csv.Contains("Ofertas criadas;5;") && csv.Contains("Ofertas recusadas;2;") && !csv.Contains("Paciente Fixture"), "CSV não preservou os totais agregados ou revelou identidade.");
                }
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
                    if (profile.IsMunicipal)
                    {
                        Require(Descendants<TextBlock>(content).Any(text => text.Text == "Panorama da rede"), "Panorama da Secretaria não renderizou.");
                        Require(Descendants<TextBlock>(content).Any(text => text.Text == "Fila de ofertas · total atual"), "Indicador agregado não foi identificado.");
                        Require(Descendants<ComboBox>(content).Count(box => !box.IsEnabled) >= 4, "Filtros sem integração não foram desabilitados.");
                    }
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
                        var registration = Descendants<Expander>(content).Single(expander => AutomationProperties.GetName(expander) == "Abrir cadastro de paciente");
                        registration.IsExpanded = true;
                        content = Layout(window, 1280, 740);
                        Descendants<Button>(registration).Single(button => button.Content?.ToString() == "Cadastrar paciente").BringIntoView();
                        application.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                        SavePreview(content, Path.Combine(previewRoot, "obs-registration-fixture-light.png"));
                        registration.IsExpanded = false;
                        ((ScrollViewer)window.FindName("PageScroll")).ScrollToTop();
                    }
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
                    Require(filteredCsv.Contains("Últimos 7 dias") && filteredCsv.Contains("Ofertas criadas;2;") && filteredCsv.Contains("Pacientes cadastrados;1;Total atual"), "CSV não refletiu o período e o escopo dos totais.");
                    window.ViewModel.Navigate("units");
                    Layout(window, 980, 640); PumpUntil(() => !vm.IsBusy);
                    Require(vm.UnitsLoaded && vm.Units.Count == 0, "Cadastro não carregou estado vazio.");
                    vm.UnitName = "UBS Teste"; vm.UnitRegion = "Centro"; vm.UnitAddress = "Rua de teste, 10"; vm.UnitLatitude = "-23,55"; vm.UnitLongitude = "-46,63";
                    Pump(vm.SaveUnitAsync());
                    Require(vm.Units.Count == 1 && vm.SelectedUnit?.Latitude == -23.55, "Cadastro não salvou coordenadas normalizadas.");
                    vm.EditSelectedUnit(); vm.UnitName = "UBS Teste Atualizada"; Pump(vm.SaveUnitAsync());
                    vm.UnitSearch = "Centro"; Require(vm.FilteredUnits.Single().Name == "UBS Teste Atualizada", "Pesquisa de unidade falhou.");
                    vm.UnitSearch = "inexistente"; Require(!vm.FilteredUnits.Any(), "Pesquisa encontrou unidade indevida.");
                    vm.UnitSearch = ""; vm.SelectedUnit = vm.Units.Single();
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
                Require(vm.IsSignedOut && vm.Patients.Count == 0 && vm.Queue.Count == 0 && fixture.LogoutCalled, "Logout não revogou ou limpou dados.");
                if (profile.IsMunicipal) Require(!vm.CanExportSummary && vm.RegisteredSlotsCount == "—" && vm.LightningPeriodDays == 0 && vm.Units.Count == 0 && vm.SelectedUnit == null, "Logout manteve dados ou filtro da sessão.");
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
                window.ViewModel.Navigate("patients");
                var content = Layout(window, 980, 640);
                Descendants<Expander>(content).Single(expander => AutomationProperties.GetName(expander) == "Abrir cadastro de paciente").IsExpanded = true;
                Layout(window, 980, 640);
                Descendants<TextBox>(content).Single(input => AutomationProperties.GetName(input) == "Nome completo do paciente").Text = "Paciente Novo Fixture";
                Descendants<TextBox>(content).Single(input => AutomationProperties.GetName(input) == "CPF do paciente").Text = "12345678143";
                Descendants<TextBox>(content).Single(input => AutomationProperties.GetName(input) == "E-mail do paciente").Text = "new@example.invalid";
                Descendants<TextBox>(content).Single(input => AutomationProperties.GetName(input) == "Telefone do paciente").Text = "(48) 99900-0001";
                vm.Cep = "01001-000"; vm.Street = "Praça da Sé"; vm.AddressNumber = "10"; vm.Neighborhood = "Sé"; vm.City = "São Paulo"; vm.State = "SP";
                var patientPassword = Descendants<PasswordBox>(content).Single(input => AutomationProperties.GetName(input) == "Senha inicial do paciente");
                patientPassword.Password = "isolated-initial-password";
                InvokeButton(Descendants<Button>(content).Single(button => button.Content?.ToString() == "Cadastrar paciente"));
                PumpUntil(() => !vm.IsBusy);
                Require(fixture.Registered && fixture.RegisteredPhone == "(48) 99900-0001" && fixture.RegisteredAddress?.Cep == "01001000" && vm.SelectedPatient?.Phone == fixture.RegisteredPhone && vm.Phone == "" && vm.Cep == "" && vm.Patients.Count == 2 && vm.SelectedPatient?.FullName == "Paciente Novo Fixture" && patientPassword.Password == "", "Cadastro não preservou telefone/endereço, seleção ou limpeza do formulário/senha.");
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
                Descendants<Expander>(content).Single(expander => AutomationProperties.GetName(expander) == "Abrir cadastro de paciente").IsExpanded = true;
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
            foreach (var route in profile.IsMunicipal ? new[] { "units", "specialties", "network", "indicators", "slots", "capacity", "alerts", "reports", "communication", "users" } : new[] { "patients", "queue", "slots" })
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
        private readonly List<HealthUnit> units = [];
        private readonly List<UnitPatientRecord> obsRegistry = [];
        private readonly List<UnitWaitingEntry> unitWaiting = [];
        private readonly List<UnitLightningSlot> unitSlots = [];
        private readonly List<AppointmentInfo> unitAppointments = [];
        private readonly HashSet<Guid> staffUnitIds = [];
        private static readonly Guid StaffId = Guid.Parse("0fc6ec25-fb80-4cdd-881b-000000000003");
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
        public AddressInfo? RegisteredAddress { get; private set; }
        public int CepLookups { get; private set; }
        public bool PatientOnline { get; set; }
        public int PatientReads { get; private set; }
        public TaskCompletionSource? PauseReads { get; set; }
        public void AddPatient(PatientAccount patient) => patients.Add(patient);
        public void AddUnit(HealthUnit unit) => units.Add(unit);
        public void AddTodayAppointment(string status) => unitAppointments.Add(new AppointmentInfo(Guid.NewGuid(), "52998224725", "Paciente Fixture", "Clínica", "Profissional", DateTimeOffset.Now, DateTimeOffset.Now.AddMinutes(30), status));
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
            if (request.Method == HttpMethod.Get && path == "/api/address/cep/01001000")
            {
                CepLookups++;
                return Reply(new AddressInfo("01001000", "Praça da Sé", null, null, "Sé", "São Paulo", "SP", "3550308"));
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
                if (body.RootElement.TryGetProperty("address", out var address) && address.ValueKind != JsonValueKind.Null)
                    RegisteredAddress = JsonSerializer.Deserialize<AddressInfo>(address.GetRawText(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                var patient = new PatientAccount(NewPatientId, body.RootElement.GetProperty("fullName").GetString()!, body.RootElement.GetProperty("cpf").GetString()!, body.RootElement.GetProperty("email").GetString()!, RegisteredPhone, Address: RegisteredAddress);
                Require(body.RootElement.GetProperty("password").GetString() == "isolated-initial-password", "Cadastro não enviou senha inicial.");
                patients.Add(patient); Registered = true;
                return Reply(patient);
            }
            if (path == "/api/obs/units" && request.Method == HttpMethod.Get) return Reply(units.ToArray());
            if (path.StartsWith("/api/obs/units/", StringComparison.Ordinal) && path.EndsWith("/appointments", StringComparison.Ordinal) && request.Method == HttpMethod.Get) return Reply(unitAppointments.ToArray());
            if (path.StartsWith("/api/obs/units/", StringComparison.Ordinal) && path.EndsWith("/waiting-list", StringComparison.Ordinal))
            {
                if (request.Method == HttpMethod.Get) return Reply(unitWaiting.ToArray());
                if (request.Method == HttpMethod.Post)
                {
                    using var payload = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
                    var cpf = payload.RootElement.GetProperty("cpf").GetString()!; var name = obsRegistry.Single(p => p.Cpf == cpf).FullName;
                    var item = new UnitWaitingEntry(unitWaiting.Count + 1, cpf, name, payload.RootElement.GetProperty("specialty").GetString()!, DateTimeOffset.UtcNow, "waiting");
                    unitWaiting.Add(item); return Reply(item);
                }
            }
            if (path.StartsWith("/api/obs/units/", StringComparison.Ordinal) && path.Contains("/waiting-list/", StringComparison.Ordinal) && path.EndsWith("/cancel", StringComparison.Ordinal))
            {
                var id = long.Parse(path.Split('/')[6]); var index = unitWaiting.FindIndex(entry => entry.Id == id);
                Require(index >= 0, "Entrada de fila inexistente."); unitWaiting[index] = unitWaiting[index] with { Status = "cancelled" }; return new(HttpStatusCode.NoContent);
            }
            if (path.StartsWith("/api/obs/units/", StringComparison.Ordinal) && path.EndsWith("/lightning-slots", StringComparison.Ordinal))
            {
                if (request.Method == HttpMethod.Get) return Reply(unitSlots.ToArray());
                if (request.Method == HttpMethod.Post)
                {
                    using var payload = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
                    var start = DateTimeOffset.UtcNow.AddDays(1);
                    var slot = new UnitLightningSlot(Guid.NewGuid(), payload.RootElement.GetProperty("appointmentId").GetGuid(), "Cardiologia", "Profissional", start, start.AddMinutes(30), "open", DateTimeOffset.UtcNow);
                    unitSlots.Add(slot); return Reply(slot);
                }
            }
            if (path.StartsWith("/api/obs/units/", StringComparison.Ordinal) && path.EndsWith("/email", StringComparison.Ordinal) && request.Method == HttpMethod.Patch)
            {
                var parts = path.Split('/');
                Require(units.Any(unit => unit.Id == Guid.Parse(parts[4])), "Correção solicitou UBS inexistente.");
                using var payload = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
                var cpf = payload.RootElement.GetProperty("cpf").GetString(); var index = obsRegistry.FindIndex(patient => patient.Cpf == cpf);
                Require(index >= 0, "Correção solicitou paciente inexistente.");
                obsRegistry[index] = obsRegistry[index] with { Email = payload.RootElement.GetProperty("email").GetString()! };
                return new(HttpStatusCode.NoContent);
            }
            if (path.StartsWith("/api/obs/units/", StringComparison.Ordinal) && path.EndsWith("/registry/active", StringComparison.Ordinal) && request.Method == HttpMethod.Patch)
            {
                using var payload = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
                Require(payload.RootElement.GetProperty("confirmed").GetBoolean(), "Alteração de situação sem confirmação.");
                var cpf = payload.RootElement.GetProperty("cpf").GetString(); var index = obsRegistry.FindIndex(patient => patient.Cpf == cpf);
                Require(index >= 0, "Alteração solicitou paciente inexistente.");
                obsRegistry[index] = obsRegistry[index] with { IsActive = payload.RootElement.GetProperty("isActive").GetBoolean() };
                return new(HttpStatusCode.NoContent);
            }
            if (path.StartsWith("/api/obs/units/", StringComparison.Ordinal) && path.EndsWith("/registry", StringComparison.Ordinal))
            {
                var id = Guid.Parse(path.Split('/')[4]); Require(units.Any(unit => unit.Id == id), "OBS escolheu UBS inexistente.");
                if (request.Method == HttpMethod.Get) return Reply(obsRegistry.ToArray());
                if (request.Method == HttpMethod.Post)
                {
                    using var payload = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
                    var phone = payload.RootElement.GetProperty("phone").ValueKind == JsonValueKind.Null ? null : payload.RootElement.GetProperty("phone").GetString();
                    var address = payload.RootElement.GetProperty("address").ValueKind == JsonValueKind.Null ? null : JsonSerializer.Deserialize<AddressInfo>(payload.RootElement.GetProperty("address").GetRawText(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    obsRegistry.Add(new UnitPatientRecord(payload.RootElement.GetProperty("cpf").GetString()!, payload.RootElement.GetProperty("fullName").GetString()!, payload.RootElement.GetProperty("email").GetString()!, phone, address));
                    return Reply(new { created = true });
                }
                if (request.Method == HttpMethod.Put)
                {
                    using var payload = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
                    var cpf = payload.RootElement.GetProperty("cpf").GetString()!; var index = obsRegistry.FindIndex(patient => patient.Cpf == cpf);
                    Require(index >= 0, "Edição solicitou paciente inexistente.");
                    var phone = payload.RootElement.GetProperty("phone").ValueKind == JsonValueKind.Null ? null : payload.RootElement.GetProperty("phone").GetString();
                    var address = payload.RootElement.GetProperty("address").ValueKind == JsonValueKind.Null ? null : JsonSerializer.Deserialize<AddressInfo>(payload.RootElement.GetProperty("address").GetRawText(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    obsRegistry[index] = obsRegistry[index] with { FullName = payload.RootElement.GetProperty("fullName").GetString()!, Email = payload.RootElement.GetProperty("email").GetString()!, Phone = phone, Address = address };
                    return new(HttpStatusCode.NoContent);
                }
            }
            if (path == "/api/municipal/staff" && request.Method == HttpMethod.Get) return Reply(new[] { new StaffAccount(StaffId, "Equipe Fixture", "attendant") });
            if (path == $"/api/municipal/staff/{StaffId}/units" && request.Method == HttpMethod.Get) return Reply(units.Where(unit => staffUnitIds.Contains(unit.Id)).ToArray());
            if (path.StartsWith($"/api/municipal/staff/{StaffId}/units/", StringComparison.Ordinal))
            {
                var id = Guid.Parse(path.Split('/').Last());
                if (request.Method == HttpMethod.Put) staffUnitIds.Add(id);
                else if (request.Method == HttpMethod.Delete) staffUnitIds.Remove(id);
                else throw new InvalidOperationException("Método não previsto para vínculo.");
                return new(HttpStatusCode.NoContent);
            }
            if (path.StartsWith("/api/municipal/units/", StringComparison.Ordinal) && path.EndsWith("/registry", StringComparison.Ordinal) && request.Method == HttpMethod.Get)
            {
                var id = Guid.Parse(path.Split('/')[4]);
                Require(units.Any(unit => unit.Id == id), "Consulta solicitou uma UBS inexistente.");
                return Reply(new[] { new UnitPatientRecord("52998224725", "Paciente Sintético", "registered@example.test") });
            }
            if (path.StartsWith("/api/municipal/units/", StringComparison.Ordinal) && path.EndsWith("/appointments", StringComparison.Ordinal) && request.Method == HttpMethod.Get)
            {
                var id = Guid.Parse(path.Split('/')[4]); Require(units.Any(unit => unit.Id == id), "Agenda solicitou UBS inexistente.");
                var start = DateTimeOffset.UtcNow.AddDays(1);
                return Reply(new[] { new AppointmentInfo(Guid.NewGuid(), "52998224725", "Paciente Sintético", "Clínica", "Profissional", start, start.AddMinutes(30), "scheduled") });
            }
            if (path == "/api/municipal/units" && request.Method == HttpMethod.Get) return Reply(units.ToArray());
            if ((path == "/api/municipal/units" && request.Method == HttpMethod.Post) || (path.StartsWith("/api/municipal/units/") && request.Method == HttpMethod.Put))
            {
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
                var id = request.Method == HttpMethod.Post ? Guid.NewGuid() : Guid.Parse(path.Split('/').Last());
                var unit = new HealthUnit(id, body.RootElement.GetProperty("name").GetString()!, body.RootElement.GetProperty("region").GetString()!, body.RootElement.GetProperty("address").GetString()!, body.RootElement.GetProperty("latitude").GetDouble(), body.RootElement.GetProperty("longitude").GetDouble());
                units.RemoveAll(existing => existing.Id == id); units.Add(unit); return Reply(unit);
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
                "/api/municipal/summary" => Reply(new MunicipalSummary(1, 1, 1, 0, 3, 5, 2, 1, 2, 0, [new(7, 1, 0, 1, 2, 1, 0, 1, 0), new(30, 1, 0, 2, 3, 1, 1, 1, 0), new(90, 1, 0, 3, 5, 2, 1, 2, 0)])),
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
