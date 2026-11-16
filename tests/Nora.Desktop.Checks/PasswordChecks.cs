using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Nora.Desktop.Models;
using Nora.Desktop.Services;
using Nora.Desktop.ViewModels;
using Nora.Desktop.Views;

namespace Nora.Desktop.Checks;

/// <summary>Troca de senha em Configurações, troca obrigatória da senha provisória, limpeza no logout e os três temas das telas novas.</summary>
internal static partial class Program
{
    private static ShellWindow OpenProfile(DesktopProfile profile, ApiFixture fixture, string storageRoot, string name, Appearance? mode = null)
        => profile.IsMunicipal ? OpenMunicipal(fixture, storageRoot, name, mode) : OpenOperational(fixture, storageRoot, name, mode);

    private static void RunPasswordChecks(Application application, string storageRoot, string[] args)
    {
        var previewRoot = PreviewRoot(args);
        foreach (var profile in new[] { DesktopProfile.Operational, DesktopProfile.Municipal })
        Check($"{profile.Name}: trocar senha em Configurações valida localmente, mostra a senha atual errada no campo e informa o sucesso", () =>
        {
            using var fixture = new ApiFixture();
            var unit = fixture.AddUnit("UBS Senha"); fixture.AddRegistry(unit.Id, "Ana Souza");
            var window = OpenProfile(profile, fixture, storageRoot, "password-" + profile.Id, Appearance.Light);
            try
            {
                var vm = LoginObs(window);
                window.ViewModel.Navigate("settings");
                var content = Layout(window, 1280, 1400);
                var current = Secret(content, "Senha atual"); var next = Secret(content, "Nova senha"); var confirm = Secret(content, "Confirmar nova senha");
                Require(!Descendants<Button>(content).Any(button => button.Content?.ToString() == "Alterar senha" && !button.IsEnabled), "O controle desabilitado 'Alterar senha' ainda existe.");
                // Validação local: nada é enviado.
                current.Password = "isolated-test-password"; next.Password = "nova-senha-123"; confirm.Password = "outra-coisa-123";
                Click(window, content, "Salvar nova senha", 1280, 1400);
                Require(fixture.Count("POST", "/api/auth/password") == 0 && vm.ConfirmPasswordError.Contains("confirmação") && HasText(Layout(window, 1280, 1400), vm.ConfirmPasswordError), "Confirmação diferente deveria ser barrada junto ao campo.");
                next.Password = "curta"; confirm.Password = "curta"; Click(window, content, "Salvar nova senha", 1280, 1400);
                Require(fixture.Count("POST", "/api/auth/password") == 0 && vm.NewPasswordError.Contains("8 a 256"), "Senha curta deveria ser barrada.");
                next.Password = "isolated-test-password"; confirm.Password = "isolated-test-password"; Click(window, content, "Salvar nova senha", 1280, 1400);
                Require(fixture.Count("POST", "/api/auth/password") == 0 && vm.NewPasswordError.Contains("diferente da atual"), "Nova igual à atual deveria ser barrada.");
                // Senha atual errada: 400 invalid-current-password no campo, sessão mantida.
                current.Password = "senha-errada-123"; next.Password = "nova-senha-123"; confirm.Password = "nova-senha-123";
                Click(window, content, "Salvar nova senha", 1280, 1400);
                Require(fixture.Count("POST", "/api/auth/password") == 1 && vm.CurrentPasswordError == "A senha atual não confere." && HasText(Layout(window, 1280, 1400), "A senha atual não confere.") && vm.IsAuthenticated && !vm.HasMessage, "A senha atual errada deveria aparecer no campo, sem encerrar a sessão.");
                Require(current.Password.Length == 0, "O campo da senha atual errada deveria ser limpo.");
                next.Clear(); confirm.Clear();
                SaveScrolled(window, Path.Combine(previewRoot, $"{profile.Id}-settings-password-error-light.png"));
                next.Password = "nova-senha-123"; confirm.Password = "nova-senha-123";
                // Sucesso.
                current.Password = "isolated-test-password"; Click(window, content, "Salvar nova senha", 1280, 1400);
                Require(fixture.PasswordChanges.Single() == ("isolated-test-password", "nova-senha-123") && vm.MessageKind == MessageKind.Success && vm.Message.Contains("outras sessões") && vm.IsAuthenticated, "A troca de senha não foi concluída com o aviso das outras sessões: " + vm.Message);
                Require(Descendants<PasswordBox>(content).All(box => box.Password.Length == 0) && vm.CurrentPasswordError == "" && vm.NewPasswordError == "" && vm.ConfirmPasswordError == "", "Os campos e erros deveriam ser limpos depois da troca.");
                Require(!VisibleTexts(content).Any(text => text.Contains("nova-senha-123")), "A senha apareceu em texto.");
                SaveScrolled(window, Path.Combine(previewRoot, $"{profile.Id}-settings-password-ok-light.png"));
            }
            finally { CloseWindow(window); }
        });
        foreach (var profile in new[] { DesktopProfile.Operational, DesktopProfile.Municipal })
        Check($"{profile.Name}: senha provisória mostra só a tela de nova senha e Sair, sem carregar dados, e depois segue o fluxo normal", () =>
        {
            using var fixture = new ApiFixture { MustChangePassword = true };
            var unit = fixture.AddUnit("UBS Provisória"); fixture.AddRegistry(unit.Id, "Ana Souza");
            var window = OpenProfile(profile, fixture, storageRoot, "forced-" + profile.Id, Appearance.Dark);
            try
            {
                var vm = LoginObs(window);
                Require(vm.IsAuthenticated && vm.MustChangePassword && fixture.Requests.Count == 1, $"Só o login deveria ter sido feito ({fixture.Requests.Count} requisições): " + string.Join(", ", fixture.Requests));
                Require(vm.Units.Count == 0 && vm.ObsUnits.Count == 0 && vm.ActiveObsUnit == null && vm.PatientCount == "—", "Nenhum dado deveria ser carregado antes da troca.");
                var content = Layout(window, 1280, 900);
                Require(Descendants<PasswordChangeRequiredView>(content).Single().IsVisible && window.ViewModel.PageLabel == "Definir nova senha", "A tela de nova senha não apareceu.");
                var navigation = Descendants<Button>(content).Where(button => button.CommandParameter is "home" or "settings" or "units" or "queue").ToArray();
                Require(navigation.Length > 0 && navigation.All(button => !button.IsVisible), "O menu de navegação deveria ficar oculto.");
                Require(!Descendants<WorkspaceView>(content).Any(view => view.IsVisible) && !Descendants<SettingsView>(content).Any(view => view.IsVisible) && !Descendants<MunicipalView>(content).Any(view => view.IsVisible), "Outra tela ficou visível durante a troca obrigatória.");
                Require(Descendants<Button>(content).Count(button => button.IsVisible && button.Content?.ToString() is "Sair" or "Salvar nova senha") == 2, "Deveria haver somente o formulário de senha e o botão Sair.");
                var page = window.ViewModel.Page; window.ViewModel.Navigate("settings"); Require(window.ViewModel.Page == page, "Navegar não deveria funcionar durante a troca obrigatória.");
                var requests = fixture.Requests.Count; Pump(vm.RefreshAutomaticallyAsync()); Require(fixture.Requests.Count == requests, "A atualização automática leu dados durante a troca obrigatória.");
                SaveScrolled(window, Path.Combine(previewRoot, $"{profile.Id}-password-required-dark.png"));
                Secret(content, "Senha atual").Password = "isolated-test-password"; Secret(content, "Nova senha").Password = "definitiva-123"; Secret(content, "Confirmar nova senha").Password = "definitiva-123";
                Click(window, content, "Salvar nova senha", 1280, 900);
                Require(!vm.MustChangePassword && fixture.PasswordChanges.Single().New == "definitiva-123", "A troca obrigatória não foi concluída: " + vm.Message);
                content = Layout(window, 1280, 900);
                Require(!Descendants<PasswordChangeRequiredView>(content).Single().IsVisible && Descendants<Button>(content).Where(button => button.CommandParameter is "home").Any(button => button.IsVisible), "Depois da troca o aplicativo deveria voltar ao normal.");
                if (profile.IsMunicipal) Require(vm.PatientCount == "1" && vm.Units.Count == 1, "A Secretaria deveria carregar os dados depois da troca.");
                else Require(vm.ActiveObsUnit?.Name == "UBS Provisória" && vm.ObsRegistry.Count == 1, "O OBS deveria seguir o fluxo normal (UBS única confirmada) depois da troca.");
                // 403 password-change-required no meio da sessão também bloqueia tudo.
                fixture.MustChangePassword = true; Pump(vm.RefreshAutomaticallyAsync());
                Require(vm.MustChangePassword && vm.Units.Count == 0 && vm.ActiveObsUnit == null && vm.PatientCount == "—" && vm.IsAuthenticated, "Um 403 password-change-required deveria levar à tela de nova senha e limpar os dados.");
                vm.LogoutCommand.Execute(null); Settle(vm);
                Require(vm.IsSignedOut && !vm.MustChangePassword && !Descendants<PasswordChangeRequiredView>(Layout(window, 1280, 900)).Single().IsVisible, "Sair da troca obrigatória deveria voltar ao login.");
            }
            finally { CloseWindow(window); }
        });
        Check("Secretaria: sair limpa contas, histórico, indicadores, filtros, listas e campos de senha", () =>
        {
            using var fixture = new ApiFixture();
            var unit = fixture.AddUnit("UBS Limpeza", "Centro"); fixture.AddUnit("UBS Sem Base", "Sul", databaseState: "pending");
            fixture.History.Add(new(DateTimeOffset.UtcNow.AddMinutes(-5), "staff-granted", "Gestor Fixture", unit.Id, unit.Name, "Equipe Fixture"));
            var window = OpenMunicipal(fixture, storageRoot, "sec-logout", Appearance.Light);
            try
            {
                var vm = LoginObs(window);
                vm.MetricsPeriodDays = 7; vm.MetricsRegion = "Centro"; Settle(vm);
                vm.SelectedUnit = vm.Units.First(); Pump(vm.LoadUnitRegistryAsync()); Pump(vm.LoadMunicipalAppointmentsAsync());
                var content = Show(window, "users", 1280, 1900);
                vm.SelectedStaff = vm.Staff.Single(); vm.StaffName = "Rascunho"; vm.StaffCpf = "x";
                Secret(content, "Senha provisória da nova conta").Password = "rascunho-123"; Secret(content, "Nova senha provisória da conta").Password = "rascunho-456";
                Require(vm.Staff.Count == 1 && vm.HistoryEvents.Count >= 1 && vm.UnitPerformance.Count > 0 && vm.UnitRegistry.Count > 0 && vm.RegionOptions.Count > 1, "Pré-condição: dados carregados.");
                vm.LogoutCommand.Execute(null); Settle(vm);
                content = Layout(window, 1280, 1900);
                Require(vm.Staff.Count == 0 && vm.StaffUnits.Count == 0 && vm.SelectedStaff == null && vm.HistoryEvents.Count == 0 && vm.UnitRegistry.Count == 0 && vm.MunicipalAppointments.Count == 0 && vm.VisibleMunicipalAppointments.Count == 0 && vm.SelectedUnit == null, "Logout manteve listas.");
                Require(vm.UnitPerformance.Count == 0 && vm.SpecialtyPerformance.Count == 0 && vm.RegionPerformance.Count == 0 && vm.DailyRows.Count == 0 && vm.ChartDays.Count == 0 && vm.OverviewCards.All(card => card.Value == "—") && vm.SummaryCards.All(card => card.Value == "—"), "Logout manteve indicadores.");
                Require(vm.MetricsPeriodDays == 30 && vm.MetricsRegion == "" && vm.MetricsSpecialty == "" && vm.SummaryUnitFilter == Guid.Empty && vm.UnitFilterOptions.Count == 1 && vm.RegionOptions.Count == 1 && vm.SpecialtyOptions.Count == 1 && vm.MetricsScopeLabel == "" && vm.UnitsWithoutDatabaseNotice == "", "Logout manteve filtros ou opções.");
                Require(vm.StaffName == "" && vm.StaffCpf == "" && Descendants<PasswordBox>(content).All(box => box.Password.Length == 0), "Logout manteve rascunhos ou senhas digitadas.");
                var requests = fixture.Requests.Count; Pump(vm.RefreshAutomaticallyAsync()); Require(fixture.Requests.Count == requests, "Atualização continuou depois do logout.");
            }
            finally { CloseWindow(window); }
        });
        Check("Secretaria: telas novas funcionam nos temas Claro, Escuro e Sistema e as cores de estado mantêm contraste", () =>
        {
            foreach (var mode in new[] { Appearance.Light, Appearance.Dark })
            {
                var themes = new ThemeService(application);
                try
                {
                    themes.Apply(mode);
                    foreach (var pair in new[] { ("ErrorBrush", "SurfaceBrush"), ("WarningBrush", "SurfaceBrush"), ("SuccessBrush", "SurfaceBrush"), ("ErrorBrush", "SurfaceAltBrush"), ("WarningBrush", "SurfaceAltBrush"), ("SecondaryBrush", "SurfaceBrush") })
                    {
                        var contrast = Contrast(((SolidColorBrush)application.FindResource(pair.Item1)).Color, ((SolidColorBrush)application.FindResource(pair.Item2)).Color);
                        Require(contrast >= 4.5, $"{mode}: {pair.Item1}/{pair.Item2} tem contraste {contrast:F2}.");
                    }
                }
                finally { themes.Dispose(); }
            }
            using var fixture = new ApiFixture();
            fixture.AddUnit("UBS Tema", "Centro"); fixture.AddUnit("UBS Sem Base", "Sul", databaseState: "pending");
            foreach (var mode in new[] { Appearance.Light, Appearance.Dark, Appearance.System })
            {
                var window = OpenMunicipal(fixture, storageRoot, "sec-theme-" + mode, mode);
                try
                {
                    var vm = LoginObs(window);
                    foreach (var route in new[] { "home", "units", "specialties", "indicators", "slots", "reports", "users", "settings" })
                    {
                        var content = Show(window, route, 1280, 1500);
                        Require(Descendants<FrameworkElement>(content).Where(element => element.IsVisible && element.ActualWidth > content.ActualWidth + 1).All(element => element is ScrollViewer or Canvas or Grid or StackPanel), $"{mode}/{route}: conteúdo mais largo que a janela.");
                        if (mode != Appearance.System && route is "home" or "users") SaveScrolled(window, Path.Combine(previewRoot, $"municipal-{route}-theme-{mode.ToString().ToLowerInvariant()}.png"));
                    }
                }
                finally { CloseWindow(window); }
            }
        });
    }
}

internal static partial class Program
{
    private static void RunSecretariaCycleChecks(Application application, string storageRoot, string[] args)
    {
        Check("Secretaria: o ciclo automático lê só o que a seção precisa e não recria listas nem recalcula o que não mudou", () =>
        {
            using var fixture = new ApiFixture();
            fixture.AddUnit("UBS Ciclo A", "Centro"); fixture.AddUnit("UBS Ciclo B", "Sul");
            var window = OpenMunicipal(fixture, storageRoot, "sec-cycle", Appearance.Light);
            try
            {
                var vm = LoginObs(window);
                var events = 0; var changed = new List<string>();
                void Watch<T>(System.Collections.ObjectModel.ObservableCollection<T> list) => list.CollectionChanged += (_, _) => events++;
                Watch(vm.UnitPerformance); Watch(vm.RegionPerformance); Watch(vm.SpecialtyPerformance); Watch(vm.OverviewCards); Watch(vm.SummaryCards); Watch(vm.DailyRows); Watch(vm.Units); Watch(vm.UnitFilterOptions); Watch(vm.RegionOptions); Watch(vm.SpecialtyOptions);
                vm.PropertyChanged += (_, change) => { if (change.PropertyName is nameof(WorkspaceViewModel.ChartDays) or nameof(WorkspaceViewModel.MetricsScopeLabel) or nameof(WorkspaceViewModel.PatientCount) or nameof(WorkspaceViewModel.UnitsWithoutDatabaseNotice)) changed.Add(change.PropertyName!); };
                (int Summary, int Metrics, int Units) Snapshot() => (fixture.SummaryQueries.Count, fixture.MetricsQueries.Count, fixture.Count("GET", "/api/municipal/units"));
                void Cycle(string route, (int Summary, int Metrics, int Units) delta)
                {
                    window.ViewModel.Navigate(route); Settle(vm);
                    var before = Snapshot(); Pump(vm.RefreshAutomaticallyAsync()); Pump(vm.RefreshAutomaticallyAsync()); var after = Snapshot();
                    var actual = ((after.Summary - before.Summary) / 2, (after.Metrics - before.Metrics) / 2, (after.Units - before.Units) / 2);
                    Require(actual == delta, $"{route}: leituras por ciclo {actual}, esperadas {delta}.");
                }
                Settle(vm); events = 0; changed.Clear();
                Cycle("home", (1, 1, 0)); Cycle("units", (1, 1, 1)); Cycle("specialties", (1, 1, 0)); Cycle("indicators", (1, 1, 0));
                Cycle("slots", (1, 0, 0)); Cycle("capacity", (1, 1, 0)); Cycle("alerts", (1, 0, 0)); Cycle("users", (1, 0, 0));
                Require(events == 0 && changed.Count == 0, $"Ciclos sem mudança recriaram listas ({events} eventos) ou recalcularam {string.Join(", ", changed)}.");
            }
            finally { CloseWindow(window); }
        });
    }
}
