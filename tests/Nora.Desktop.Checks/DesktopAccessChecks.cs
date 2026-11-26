using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using Nora.Desktop.Models;
using Nora.Desktop.Services;
using Nora.Desktop.Views;

namespace Nora.Desktop.Checks;

internal static partial class Program
{
    private static void RunDesktopAccessChecks(string root, string[] args)
    {
        Check("Login compacto municipal: reabertura por 30 dias, tela isolada e saída limpa", () =>
        {
            using var fixture = new ApiFixture();
            fixture.AddUnit("UBS de teste");
            var storage = Path.Combine(root, "compact-municipal");
            var devices = new ObsDeviceStore(storage);
            ShellWindow NewWindow()
            {
                var window = new ShellWindow(DesktopProfile.Municipal, storage, new NoraApiClient(fixture), devices, compactAccess: true);
                window.ViewModel.ConfirmClose = false;
                window.ViewModel.Animations = false;
                window.ViewModel.Workspace.Endpoint = "https://fixture.invalid";
                PrepareWindow(window);
                return window;
            }

            var first = NewWindow();
            try
            {
                var vm = first.ViewModel.Workspace;
                var content = Layout(first, 1080, 740);
                var access = Descendants<DesktopAccessView>(content).Single(view => view.IsVisible);
                Require(!((FrameworkElement)first.FindName("Sidebar")).IsVisible && !((FrameworkElement)first.FindName("PageHeader")).IsVisible,
                    "A entrada mostrou o painel antes da autenticação.");
                Require(!Descendants<TextBox>(access).Any(box => AutomationProperties.GetName(box) == "Endereço da API"), "A entrada mostra campo técnico.");
                var keep = Descendants<CheckBox>(access).Single(box => box.IsVisible);
                Require(keep.IsChecked == false && Equals(keep.Content, "Manter conectado por 30 dias"), "A opção duradoura deve começar desmarcada.");
                SavePreview(content, Path.Combine(PreviewRoot(args), "municipal-acesso-compacto.png"));
                keep.IsChecked = true;
                Descendants<TextBox>(access).Single(box => box.IsVisible).Text = "fixture@example.invalid";
                Descendants<PasswordBox>(access).Single(box => box.IsVisible).Password = "isolated-test-password";
                InvokeButton(FindActionButton(access, "Entrar"));
                Settle(vm);
                Require(vm.IsAuthenticated && devices.ReadSession()?.ExpiresAt > DateTimeOffset.UtcNow.AddDays(29), "Sessão de 30 dias não foi guardada.");
                Require(!access.IsVisible, "O login permaneceu visível sobre o painel.");
            }
            finally { CloseWindow(first); }

            var reopened = NewWindow();
            try
            {
                var vm = reopened.ViewModel.Workspace;
                Settle(vm);
                Require(vm.IsAuthenticated && fixture.Count("POST", "/api/auth/login") == 1, "Reabertura pediu login com uma sessão válida.");
                var settings = Show(reopened, "settings");
                var retention = Descendants<RetentionSettingsView>(settings).Single(view => view.IsVisible);
                var never = (CheckBox)retention.FindName("NeverDelete");
                var days = (TextBox)retention.FindName("PolicyDays");
                PumpUntil(() => never.IsEnabled);
                Require(never.IsChecked == true && !days.IsVisible, "O prazo automático deve começar desligado e recolhido.");
                never.IsChecked = false;
                Require(days.IsVisible && fixture.Count("PUT", "/retention") == 0, "Alterar a opção deve revelar o prazo e aguardar a confirmação.");
                never.IsChecked = true;
                vm.LogoutCommand.Execute(null);
                Settle(vm);
                Require(vm.IsSignedOut && devices.ReadSession() is null && reopened.ViewModel.ShowCompactAccess, "Sair não voltou à entrada ou reteve o token.");
            }
            finally { CloseWindow(reopened); }
        });

        Check("Login compacto do OBS vale sozinho: sem computador, reabre por 30 dias e sem a seção Computadores", () =>
        {
            using var fixture = new ApiFixture();
            fixture.AddUnit("UBS do login");
            var storage = Path.Combine(root, "compact-obs");
            // Armazenamento como o OBS entrega desde 24/11/2026: computadores desligados.
            var devices = new ObsDeviceStore(storage);
            ShellWindow NewWindow()
            {
                var window = new ShellWindow(DesktopProfile.Operational, storage, new NoraApiClient(fixture), devices, compactAccess: true);
                window.ViewModel.ConfirmClose = false;
                window.ViewModel.Animations = false;
                window.ViewModel.Workspace.Endpoint = "https://fixture.invalid";
                PrepareWindow(window);
                return window;
            }

            var first = NewWindow();
            try
            {
                var vm = first.ViewModel.Workspace;
                var content = Layout(first, 1080, 740);
                var access = Descendants<DesktopAccessView>(content).Single(view => view.IsVisible);
                Require(!vm.HasDeviceSupport && !vm.ShowActivation, "O OBS mostrou a ativação do computador com o recurso desligado.");
                var keep = Descendants<CheckBox>(access).Single(box => box.IsVisible && Equals(box.Content, "Manter conectado por 30 dias"));
                keep.IsChecked = true;
                Descendants<TextBox>(access).Single(box => box.IsVisible).Text = "fixture@example.invalid";
                Descendants<PasswordBox>(access).Single(box => box.IsVisible).Password = "isolated-test-password";
                InvokeButton(FindActionButton(access, "Entrar"));
                Settle(vm);
                Require(vm.IsAuthenticated && devices.ReadSession()?.ExpiresAt > DateTimeOffset.UtcNow.AddDays(29), "O OBS não guardou a sessão de 30 dias só pelo login.");
                Require(fixture.DeviceHeaders.All(header => header is null) && !File.Exists(devices.DevicePath), "O OBS enviou ou guardou a identificação do computador.");
                Show(first, "unit-settings");
                Settle(vm);
                Require(fixture.Count("GET", "/devices") == 0, "As Informações da UBS leram a lista de computadores, que não é mais uma tela.");
            }
            finally { CloseWindow(first); }

            var reopened = NewWindow();
            try
            {
                var vm = reopened.ViewModel.Workspace;
                Settle(vm);
                Require(vm.IsAuthenticated && fixture.Count("POST", "/api/auth/login") == 1, "A reabertura pediu login com a sessão de 30 dias válida.");
                vm.LogoutCommand.Execute(null);
                Settle(vm);
                Require(vm.IsSignedOut && devices.ReadSession() is null, "Sair não apagou a sessão guardada.");
            }
            finally { CloseWindow(reopened); }
        });

        Check("Pesquisa atual: encontra texto sem acento, percorre resultados e limpa ao navegar", () =>
        {
            using var fixture = new ApiFixture();
            var unit = fixture.AddUnit("UBS da pesquisa");
            fixture.AddWaiting(unit.Id, "52998224725", "João da Silva", "Clínica", DateTimeOffset.UtcNow.AddHours(-1));
            var window = OpenOperational(fixture, root, "screen-search");
            try
            {
                LoginObs(window);
                var content = Show(window, "patients");
                window.GetType().GetMethod("OpenScreenSearch", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
                var search = Descendants<TextBox>(content).Single(box => box.IsVisible && AutomationProperties.GetName(box) == "Pesquisar na tela");
                search.Text = "pacientes";
                Require(VisibleTexts(content).Any(text => text.StartsWith("1 de ", StringComparison.Ordinal)), "A busca sem acento não achou o paciente da fila.");
                search.Text = "sem-resultado-xyz";
                Require(HasText(content, "Sem resultados"), "A busca não apresentou o estado vazio.");
                Show(window, "home");
                Require(!search.IsVisible && search.Text == "", "A pesquisa levou a consulta da fila para a outra tela.");
                var model = window.ViewModel;
                Require(model.ChangeShortcut("settings", new(Key.F, ModifierKeys.Control)) is not null, "O editor permitiu conflito com Ctrl+F.");
                Require(model.ChangeShortcut("settings", new(Key.F6)) is null &&
                    model.ResolveShortcut(DesktopShortcuts.Definitions.Single(item => item.Id == "settings")).Key == Key.F6,
                    "O editor não aplicou a nova combinação.");
            }
            finally { CloseWindow(window); }
        });
    }
}
