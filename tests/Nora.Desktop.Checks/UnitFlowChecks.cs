using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using Nora.Desktop.Models;
using Nora.Desktop.ViewModels;
using Nora.Desktop.Views;

namespace Nora.Desktop.Checks;

internal static partial class Program
{
    private static string PreviewRoot(string[] args)
    {
        var previewArgument = Array.IndexOf(args, "--preview-dir");
        var root = previewArgument >= 0 && previewArgument + 1 < args.Length ? args[previewArgument + 1] : Path.Combine(AppContext.BaseDirectory, "previews");
        Directory.CreateDirectory(root);
        return root;
    }

    private static string[] VisibleTexts(DependencyObject root) => Descendants<TextBlock>(root).Where(text => text.IsVisible).Select(text => text.Text).ToArray();

    private static void RunUnitSelectionChecks(Application application, string storageRoot, string[] args)
    {
        var previewRoot = PreviewRoot(args);
        Check("OBS: UBS única é confirmada sozinha após o login e carrega cadastro, fila, vagas, resumo do dia e agenda", () =>
        {
            using var fixture = new ApiFixture { UseWorkspaceIdentity = true };
            var unit = fixture.AddUnit("UBS Única", "Centro");
            fixture.AddRegistry(unit.Id, "Ana Souza");
            fixture.AddWaiting(unit.Id, "52998224725", "Ana Souza", "Clínica", DateTimeOffset.UtcNow.AddMinutes(-90));
            fixture.AddSlot(unit.Id, new UnitLightningSlot(Guid.NewGuid(), Guid.NewGuid(), "Clínica", "Profissional", DateTimeOffset.UtcNow.AddDays(1), DateTimeOffset.UtcNow.AddDays(1).AddMinutes(30), "open", DateTimeOffset.UtcNow));
            var noon = new DateTimeOffset(DateTime.Today.AddHours(12));
            fixture.AddAppointment(unit.Id, "scheduled", noon); fixture.AddAppointment(unit.Id, "completed", noon.AddHours(1));
            var window = OpenOperational(fixture, storageRoot, "single-unit");
            try
            {
                var vm = LoginObs(window);
                Require(vm.ActiveObsUnit?.Id == unit.Id && vm.UnitLabel == "UBS Única" && vm.ObsUnitPrompt == "", "UBS única não foi confirmada sozinha.");
                Require(vm.ObsRegistry.Count == 1 && vm.UnitLightningSlots.Count == 1, "Cadastro ou vagas não foram carregados já no login.");
                Require(vm.TodayScheduledCount == "1" && vm.TodayCompletedCount == "1" && vm.PatientTotalCount == "1" && vm.OffersRunningCount == "0" && vm.SlotOpenCount == "1", "Resumo do dia não foi carregado já no login.");
                Require(vm.Appointments.Count == 2, "Agenda da semana não foi carregada já no login.");
                Require(fixture.Count("GET", "/registry") == 1 && fixture.Count("GET", "/lightning-slots") == 1 && fixture.Count("GET", "/appointments") == 2, "O login deveria ler cada parte uma única vez, sem esperar o temporizador.");
                var content = Layout(window, 1280, 740);
                Require(VisibleTexts(content).Count(text => text == "UBS Única") >= 2, "Nome da UBS ativa ausente da barra lateral ou do Painel.");
                window.ViewModel.Navigate("settings"); content = Layout(window, 1280, 740);
                var texts = VisibleTexts(content);
                Require(texts.Contains("UBS Única") && texts.Contains("Operador Fixture") && !texts.Contains("Perfil da conta"), "Configurações devem mostrar o menu da conta e manter o formulário de perfil separado.");
            }
            finally { window.ViewModel.ConfirmClose = false; window.Close(); }
        });
        Check("OBS: ausência de UBS permanece visível no menu da conta", () =>
        {
            using var fixture = new ApiFixture { ObsRole = "attendant" };
            fixture.AddUnit("UBS Norte", "Norte"); fixture.AddUnit("UBS Sul", "Sul");
            var window = OpenOperational(fixture, storageRoot, "attendant-role");
            try
            {
                var vm = LoginObs(window);
                Require(vm.AccountRoleLabel == "Atendente" && vm.UnitLabel == "Nenhuma unidade selecionada", "Perfil ou rótulo sem UBS incorretos.");
                window.ViewModel.Navigate("settings"); var content = Layout(window, 1280, 740);
                var texts = VisibleTexts(content);
                Require(texts.Contains("Nenhuma unidade selecionada"), "A barra lateral não informa que nenhuma unidade foi selecionada.");
            }
            finally { window.ViewModel.ConfirmClose = false; window.Close(); }
        });
        Check("OBS: várias UBS exigem escolha explícita, distinguem nomes iguais e não restauram escolha anterior", () =>
        {
            using var fixture = new ApiFixture();
            var north = fixture.AddUnit("UBS Central", "Norte", -23.4, -46.6);
            var south = fixture.AddUnit("UBS Central", "Sul", -23.9, -46.3);
            fixture.AddRegistry(north.Id, "Paciente Norte");
            fixture.AddRegistry(south.Id, "Paciente Sul 1", "11144477735"); fixture.AddRegistry(south.Id, "Paciente Sul 2", "12345678909");
            var window = OpenOperational(fixture, storageRoot, "multi-unit");
            try
            {
                var vm = LoginObs(window);
                Require(vm.ActiveObsUnit == null && vm.NeedsObsUnitChoice && vm.ObsUnits.Count == 2 && !vm.CanConfirmObsUnit, "Várias UBS não pediram escolha.");
                Require(vm.ObsUnitPrompt.Contains("2 UBS") && fixture.Count("GET", "/registry") == 0, "Prompt ausente ou dados lidos antes da escolha.");
                Require(vm.ObsUnits.Select(unit => unit.SelectorLabel).Distinct().Count() == 2 && vm.ObsUnits.Any(unit => unit.SelectorLabel == "UBS Central · Norte"), "Nomes iguais não foram distinguidos por região.");
                foreach (var mode in new[] { Appearance.Light, Appearance.Dark, Appearance.System })
                {
                    window.ViewModel.SetAppearance(mode);
                    window.ViewModel.Navigate("home");
                    var content = Layout(window, 1280, 740);
                    // O Painel não traz mais o cartão "UBS da operação" nem o seletor: a escolha é o pop-up Trocar perfil.
                    Require(!Descendants<Border>(content).Any(border => AutomationProperties.GetName(border) == "UBS da operação") && !Descendants<ComboBox>(content).Any(box => AutomationProperties.GetName(box) == "UBS autorizada no OBS"), $"{mode}: o Painel ainda mostra o cartão ou o seletor de UBS.");
                    InspectSwitchProfile(window, dialog =>
                    {
                        Require(dialog.Options.Select(option => option.Unit.SelectorLabel).Distinct().Count() == 2 && dialog.Options.Any(option => option.Unit.SelectorLabel == "UBS Central · Norte"), $"{mode}: nomes iguais não foram distinguidos por região.");
                        var labels = VisibleTexts(dialog);
                        Require(labels.Contains("UBS Central") && labels.Contains("Região: Norte") && labels.Contains("Região: Sul"), $"{mode}: a lista não mostra nome e região: {string.Join(" / ", labels)}");
                        Require(!dialog.Options.Any(option => option.IsActive) && vm.ActiveObsUnit == null && !vm.CanConfirmObsUnit && !dialog.Primary.IsEnabled, $"{mode}: sem escolha o Trocar deveria ficar desabilitado.");
                        dialog.Units.SelectedItem = dialog.Options.Single(option => option.Unit.Region == "Norte");
                        Require(vm.CandidateObsUnit?.Region == "Norte" && dialog.Primary.IsEnabled, $"{mode}: escolher na lista deveria habilitar o Trocar.");
                        SavePreview(dialog, Path.Combine(previewRoot, $"obs-trocar-perfil-{mode.ToString().ToLowerInvariant()}.png"));
                    });
                    Require(vm.ActiveObsUnit == null && vm.ObsRegistry.Count == 0, $"{mode}: fechar o pop-up sem Trocar ativou uma UBS.");
                    vm.CandidateObsUnit = null;
                }
                vm.CandidateObsUnit = vm.ObsUnits.Single(unit => unit.Region == "Sul");
                Require(vm.ActiveObsUnit == null && vm.ObsRegistry.Count == 0, "Escolher no seletor já ativou a UBS sem confirmação.");
                Pump(vm.ConfirmObsUnitAsync());
                Require(vm.ActiveObsUnit?.Id == south.Id && vm.ActiveObsUnitRegion == "Sul" && vm.ObsRegistry.Count == 2 && vm.Message.Contains("UBS Central"), "Confirmação não ativou a UBS escolhida com seus dados.");
                vm.CandidateObsUnit = vm.ObsUnits.Single(unit => unit.Region == "Norte");
                Require(vm.ActiveObsUnit?.Id == south.Id && vm.ObsRegistry.Count == 2, "Trocar de UBS sem confirmar alterou a unidade ativa.");
                Pump(vm.ConfirmObsUnitAsync());
                Require(vm.ActiveObsUnit?.Id == north.Id && vm.ObsRegistry.Single().FullName == "Paciente Norte", "Troca confirmada não substituiu os dados da UBS.");
                vm.LogoutCommand.Execute(null); Settle(vm);
                Require(vm.IsSignedOut && vm.ActiveObsUnit == null && vm.ObsUnits.Count == 0, "Logout manteve a UBS escolhida.");
                LoginObs(window);
                Require(vm.ActiveObsUnit == null && vm.UnitLabel == "Nenhuma unidade selecionada" && vm.ObsUnits.Count == 2, "Novo login restaurou a escolha anterior em vez de pedir a UBS.");
            }
            finally { window.ViewModel.ConfirmClose = false; window.Close(); }
        });
        Check("OBS: conta sem UBS autorizada recebe orientação sobre a concessão da Secretaria", () =>
        {
            using var fixture = new ApiFixture();
            fixture.AddUnit("UBS sem concessão"); fixture.Granted = [];
            var window = OpenOperational(fixture, storageRoot, "no-grant");
            try
            {
                var vm = LoginObs(window);
                Require(vm.IsAuthenticated && vm.ObsUnits.Count == 0 && vm.ObsUnitsLoaded && vm.ActiveObsUnit == null, "Conta sem concessão deveria ficar autenticada sem UBS ativa.");
                Require(vm.ObsUnitPrompt.Contains("Nenhuma UBS autorizada") && vm.ObsUnitPrompt.Contains("Secretaria") && vm.ObsUnitPrompt.Contains("Usuários e histórico"), "Orientação não explica como obter a concessão.");
                foreach (var route in new[] { "home", "queue", "agenda", "slots", "patients" })
                {
                    window.ViewModel.Navigate(route); Settle(vm);
                    var content = Layout(window, 980, 640);
                    Require(VisibleTexts(content).Any(text => text.Contains("Nenhuma UBS autorizada", StringComparison.Ordinal)), "Aba sem UBS não explica a ausência de concessão: " + route + " | " + string.Join(" / ", VisibleTexts(content)));
                }
                Require(fixture.Count("GET", "/registry") == 0, "Dados de UBS foram lidos sem UBS ativa.");
            }
            finally { window.ViewModel.ConfirmClose = false; window.Close(); }
        });
        Check("OBS: falha ao confirmar outra UBS preserva a UBS ativa e seus dados", () =>
        {
            using var fixture = new ApiFixture();
            var first = fixture.AddUnit("UBS Primeira", "Norte"); var second = fixture.AddUnit("UBS Segunda", "Sul");
            fixture.AddRegistry(first.Id, "Paciente Primeiro"); fixture.AddRegistry(second.Id, "Paciente Segundo", "11144477735");
            fixture.AddWaiting(first.Id, "52998224725", "Paciente Primeiro", "Clínica", DateTimeOffset.UtcNow.AddHours(-1));
            var window = OpenOperational(fixture, storageRoot, "unit-switch-failure");
            try
            {
                var vm = LoginObs(window);
                vm.CandidateObsUnit = first; Pump(vm.ConfirmObsUnitAsync());
                Require(vm.ActiveObsUnit?.Id == first.Id && vm.UnitLightningSlots.Count >= 0, "Cenário inicial incorreto.");
                fixture.UnavailableUnits.Add(second.Id);
                vm.CandidateObsUnit = second; Pump(vm.ConfirmObsUnitAsync());
                Require(vm.ActiveObsUnit?.Id == first.Id && vm.UnitLabel == "UBS Primeira" && vm.ObsRegistry.Single().FullName == "Paciente Primeiro", "Falha ao trocar de UBS apagou a UBS ativa ou seus dados.");
                Require(vm.MessageKind == MessageKind.Error && vm.Message.Contains("indisponível") && !vm.Message.Contains("private-server-secret"), "Falha não foi explicada na faixa comum: " + vm.Message);
                fixture.UnavailableUnits.Clear();
                Pump(vm.ConfirmObsUnitAsync());
                Require(vm.ActiveObsUnit?.Id == second.Id && vm.ObsRegistry.Single().FullName == "Paciente Segundo", "Nova tentativa não concluiu a troca.");
                // Perder a concessão da UBS ativa: a lista nova não a inclui e a escolha volta a ser obrigatória.
                fixture.Granted = [first.Id];
                Pump(vm.LoadObsUnitsAsync());
                Require(vm.ActiveObsUnit == null && vm.ObsRegistry.Count == 0 && vm.NeedsObsUnitChoice && vm.CandidateObsUnit?.Id == first.Id, "Concessão revogada deixou a UBS ativa ou trocou de unidade sozinha.");
                Require(vm.MessageKind == MessageKind.Error && vm.Message.Contains("deixou de estar autorizada"), "Revogação não foi explicada: " + vm.Message);
            }
            finally { window.ViewModel.ConfirmClose = false; window.Close(); }
        });
    }

    private static void RunUnitFlowChecks(Application application, string storageRoot, string[] args)
    {
        Check("OBS Pacientes: CEP digitado consulta e preenche o endereço automaticamente", () =>
        {
            using var fixture = new ApiFixture();
            fixture.AddUnit("UBS CEP");
            var window = OpenOperational(fixture, storageRoot, "automatic-cep");
            try
            {
                var vm = LoginObs(window);
                var content = Show(window, "patients:register", 1280, 1400);
                var cep = Input(content, "CEP no cadastro da UBS");
                cep.Focus();
                Require(cep.IsKeyboardFocusWithin, "Campo de CEP não recebeu foco.");
                cep.Text = "01001-000";
                PumpUntil(() => fixture.CepLookups == 1 && !vm.IsBusy && vm.ObsRegistryCity == "São Paulo");
                Require(vm.ObsRegistryStreet == "Praça da Sé" && vm.ObsRegistryState == "SP", "CEP não completou o endereço esperado.");
                Require(fixture.CepLookups == 1, "CEP gerou consultas duplicadas.");
            }
            finally { window.ViewModel.ConfirmClose = false; window.Close(); }
        });
        Check("OBS: cadastro de referência, fila, agenda e vaga pelo fluxo por UBS", () =>
        {
            using var fixture = new ApiFixture();
            var unit = fixture.AddUnit("UBS de teste", "Centro");
            var window = OpenOperational(fixture, storageRoot, "unit-flow");
            try
            {
                var vm = LoginObs(window);
                Require(vm.OffersRunningCount == "0" && vm.SlotOpenCount == "0" && vm.TodayCompletedCount == "0" && vm.TodayScheduledCount == "0", "Listas vazias consultadas não apareceram como zero no painel.");
                var noon = new DateTimeOffset(DateTime.Today.AddHours(12));
                foreach (var status in new[] { "scheduled", "called", "completed", "missed", "cancelled" }) fixture.AddAppointment(unit.Id, status, noon);
                vm.RefreshCommand.Execute(null); Settle(vm);
                Require(vm.TodayScheduledCount == "2" && vm.TodayCompletedCount == "1" && vm.TodayMissedCount == "1" && vm.TodayCancelledCount == "1", "Painel não refletiu situações das consultas do dia.");
                Require(vm.MessageKind == MessageKind.Success && vm.Message == "Dados atualizados pela API.", "Atualização manual não confirmou o resultado.");
                vm.ObsRegistryName = "Paciente Sintético"; vm.ObsRegistryCpf = "529.982.247-25"; vm.ObsRegistryEmail = "patient@example.test"; vm.ObsRegistryPhone = "48999990028";
                vm.ObsRegistryCep = "01001-000"; Pump(vm.LookupObsRegistryCepAsync()); vm.ObsRegistryNumber = "10";
                Pump(vm.CreateObsRegistryAsync());
                var created = vm.ObsRegistry.Single();
                Require(created.FullName == "Paciente Sintético" && created.Cpf == "52998224725" && created.Phone == "48999990028" && created.Address?.Cep == "01001000", "Cadastro não voltou com CPF normalizado, contato e endereço da UBS.");
                Require(vm.ObsRegistryName == "" && vm.ActiveObsUnit?.Name == "UBS de teste" && vm.Message.Contains("Paciente cadastrado"), "Formulário, unidade ou mensagem não foram preservados corretamente.");
                vm.ObsRegistrySearch = "529.982.247-25"; Require(vm.FilteredObsRegistry.Count() == 1, "Pesquisa não encontrou CPF formatado.");
                vm.ObsRegistrySearch = "(48) 99999-0028"; Require(vm.FilteredObsRegistry.Count() == 1, "Pesquisa não encontrou telefone formatado.");
                vm.ObsRegistrySearch = "não encontrado"; Require(!vm.FilteredObsRegistry.Any(), "Pesquisa retornou paciente sem correspondência."); vm.ObsRegistrySearch = "";
                vm.SelectedObsRegistry = vm.ObsRegistry.Single(); vm.CorrectedObsEmail = "corrected@example.test";
                Pump(vm.UpdateObsRegistryEmailAsync());
                Require(vm.ObsRegistry.Single().Email == "corrected@example.test" && vm.SelectedObsRegistry == null, "Correção de e-mail não atualizou a lista ou manteve seleção antiga.");
                vm.SelectedObsRegistry = vm.ObsRegistry.Single(); vm.EditSelectedObsRegistry();
                Require(vm.IsEditingObsRegistry && vm.ObsRegistryCpf == "52998224725", "Edição não carregou CPF do cadastro selecionado.");
                vm.ObsRegistryName = "Paciente Atualizado"; vm.ObsRegistryNumber = "20"; Pump(vm.CreateObsRegistryAsync());
                Require(vm.ObsRegistry.Single().FullName == "Paciente Atualizado" && vm.ObsRegistry.Single().Address?.Number == "20" && !vm.IsEditingObsRegistry, "Edição não persistiu nome e endereço.");
                // Agenda de amanhã: o início precisa ser no futuro.
                vm.SelectAgendaDayCommand.Execute(null); vm.MoveAgenda(1); Settle(vm);
                vm.AppointmentPatient = vm.ObsRegistry.Single(); vm.AppointmentSpecialty = "Cardiologia"; vm.AppointmentProfessional = "Profissional"; vm.AppointmentStart = "09:00"; vm.AppointmentEnd = "09:30";
                Pump(vm.CreateAppointmentAsync());
                var appointment = vm.Appointments.Single();
                Require(appointment.Status == "scheduled" && appointment.Cpf == "52998224725" && vm.Message == "Consulta agendada." && vm.AppointmentPatient == null, "Agendamento não foi criado e exibido.");
                vm.SelectedAppointment = appointment;
                Pump(vm.ChangeAppointmentStatusAsync("called", false));
                Require(vm.SelectedAppointment?.Status == "called" && vm.CanCompleteAppointment && !vm.CanCallAppointment, "Chamar não atualizou a consulta e os botões.");
                Pump(vm.ChangeAppointmentStatusAsync("cancelled", true));
                Require(vm.SelectedAppointment?.Status == "cancelled" && !vm.CanCancelAppointment, "Cancelar não atualizou a consulta e os botões.");
                vm.SelectedObsRegistry = vm.ObsRegistry.Single(); Pump(vm.SetObsRegistryActiveAsync(vm.SelectedObsRegistry.Cpf, false));
                Require(vm.SelectedObsRegistry?.IsActive == false && !vm.ActiveObsRegistry.Any(), "Desativação não foi refletida na UBS.");
                vm.ObsRegistryStatusFilter = "Ativos"; Require(!vm.FilteredObsRegistry.Any(), "Filtro exibiu paciente desativado.");
                vm.ObsRegistryStatusFilter = "Desativados"; Require(vm.FilteredObsRegistry.Count() == 1, "Filtro não exibiu paciente desativado.");
                vm.ObsRegistryPresenceFilter = "Online"; Require(!vm.FilteredObsRegistry.Any(), "Filtro de presença mostrou paciente offline.");
                vm.ObsRegistryPresenceFilter = "Offline"; Require(vm.FilteredObsRegistry.Count() == 1, "Filtro não mostrou paciente offline.");
                vm.ObsRegistryPresenceFilter = "Todos";
                Pump(vm.SetObsRegistryActiveAsync(vm.SelectedObsRegistry!.Cpf, true)); vm.ObsRegistryStatusFilter = "Todos";
                Require(vm.SelectedObsRegistry?.IsActive == true && vm.ActiveObsRegistry.Count() == 1, "Reativação não foi refletida na UBS.");
                Require(vm.BuildDetails(vm.SelectedObsRegistry!)?.Fields.Any(field => field.Label == "CPF" && field.CanCopy) == true, "Ficha da UBS não oferece CPF para cópia explícita.");
                window.ViewModel.Navigate("patients"); Settle(vm); var content = Layout(window, 980, 640);
                Require(Descendants<ObsUnitRegistryView>(content).Any(), "Tela de cadastro por UBS não apareceu.");
                // Pacientes não tem mais o expansor "Escolher ou trocar UBS" nem o seletor: a troca é pelo menu do perfil (pop-up Trocar perfil), que reabre com a UBS ativa marcada.
                Require(!Descendants<Expander>(content).Any(expander => expander.Header?.ToString() == "Escolher ou trocar UBS") && !Descendants<Button>(content).Any(button => button.Content?.ToString() == "Confirmar UBS"), "Pacientes ainda mostra o seletor de UBS.");
                InspectSwitchProfile(window, dialog => Require(dialog.Options.Single(option => option.IsActive).Unit.Id == unit.Id && dialog.Units.SelectedItem is ProfileOption { IsActive: true }, "O pop-up não reabriu com a UBS ativa marcada e selecionada."));
                window.ViewModel.Navigate("slots"); Settle(vm); content = Layout(window, 980, 640);
                Require(Descendants<UnitLightningSlotsView>(content).Any() && Descendants<DataGrid>(content).Any(grid => AutomationProperties.GetName(grid) == "Vagas da UBS"), "Vagas por UBS não apareceram na tela.");
                vm.LogoutCommand.Execute(null); Settle(vm);
                Require(vm.ObsRegistry.Count == 0 && vm.ActiveObsUnit == null && vm.Appointments.Count == 0, "Saída preservou dados da UBS.");
            }
            finally { window.ViewModel.ConfirmClose = false; window.Close(); }
        });
        Check("OBS: e-mail e telefone são conferidos por campo no cadastro, na edição e na correção de e-mail", () =>
        {
            using var fixture = new ApiFixture();
            var unit = fixture.AddUnit("UBS de validação"); fixture.AddRegistry(unit.Id, "Paciente Existente");
            var window = OpenOperational(fixture, storageRoot, "contact-validation");
            try
            {
                var vm = LoginObs(window);
                foreach (var invalid in new[] { "sem-arroba", "a@b", "a b@example.test", "@example.test", "a@@example.test" })
                {
                    vm.ObsRegistryEmail = invalid; Require(vm.ObsRegistryEmailError.Contains("E-mail inválido"), "E-mail inválido aceito: " + invalid);
                }
                foreach (var valid in new[] { "pessoa@example.test", "nome.sobrenome+tag@sub.example.com.br" }) { vm.ObsRegistryEmail = valid; Require(vm.ObsRegistryEmailError == "", "E-mail válido rejeitado: " + valid); }
                foreach (var invalid in new[] { "1234567", "1234567890123456", "48-9999-AAAA", "55++48999990028", "48+999990028" }) { vm.ObsRegistryPhone = invalid; Require(vm.ObsRegistryPhoneError.Contains("Telefone inválido"), "Telefone inválido aceito: " + invalid); }
                foreach (var valid in new[] { "", "12345678", "+55 (48) 99999-0028", "48.99999.0028", "123456789012345" }) { vm.ObsRegistryPhone = valid; Require(vm.ObsRegistryPhoneError == "", "Telefone válido rejeitado: " + valid); }
                vm.ObsRegistryName = "Paciente Novo"; vm.ObsRegistryCpf = "11144477735";
                vm.ObsRegistryEmail = "sem-arroba"; vm.ObsRegistryPhone = "";
                Pump(vm.CreateObsRegistryAsync());
                Require(vm.MessageKind == MessageKind.Error && vm.Message.Contains("E-mail inválido") && fixture.Count("POST", "/registry") == 0, "Cadastro com e-mail inválido foi enviado.");
                vm.ObsRegistryEmail = "novo@example.test"; vm.ObsRegistryPhone = "123";
                Pump(vm.CreateObsRegistryAsync());
                Require(vm.MessageKind == MessageKind.Error && vm.Message.Contains("Telefone inválido") && fixture.Count("POST", "/registry") == 0, "Cadastro com telefone inválido foi enviado.");
                vm.ObsRegistryPhone = "+55 (48) 99999-0028";
                Pump(vm.CreateObsRegistryAsync());
                Require(fixture.Count("POST", "/registry") == 1 && vm.ObsRegistry.Count == 2, "Cadastro válido não foi enviado.");
                vm.SelectedObsRegistry = vm.ObsRegistry.First(); vm.CorrectedObsEmail = "inválido";
                Require(vm.CorrectedObsEmailError.Contains("E-mail inválido"), "Correção não mostra erro do campo.");
                Pump(vm.UpdateObsRegistryEmailAsync());
                Require(vm.MessageKind == MessageKind.Error && fixture.Count("PATCH", "/registry/email") == 0, "Correção com e-mail inválido foi enviada.");
                vm.EditSelectedObsRegistry(); vm.ObsRegistryEmail = "ruim"; vm.ObsRegistryPhone = "12";
                Pump(vm.CreateObsRegistryAsync());
                Require(vm.MessageKind == MessageKind.Error && fixture.Count("PUT", "/registry") == 0, "Edição com contato inválido foi enviada.");
                vm.NewObsRegistryDraft();
                var content = Layout(window, 980, 640); window.ViewModel.Navigate("patients"); content = Layout(window, 980, 640);
                var draft = Descendants<StackPanel>(content).Single(form => Nora.Desktop.Views.SubPage.GetKey(form) == "register");  content = Layout(window, 980, 640);
                Input(content, "E-mail no cadastro da UBS").Text = "ruim"; Input(content, "Telefone no cadastro da UBS").Text = "12"; content = Layout(window, 980, 640);
                var errors = VisibleTexts(content);
                Require(errors.Any(text => text.StartsWith("E-mail inválido", StringComparison.Ordinal)) && errors.Any(text => text.StartsWith("Telefone inválido", StringComparison.Ordinal)), "Mensagens por campo não aparecem sob os campos.");
            }
            finally { window.ViewModel.ConfirmClose = false; window.Close(); }
        });
    }
}
