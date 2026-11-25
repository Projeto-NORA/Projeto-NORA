using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Nora.Desktop.Models;
using Nora.Desktop.Views;

namespace Nora.Desktop.Checks;

internal static partial class Program
{
    private static void RunPatientInterfaceChecks(Application application, string storageRoot, string[] args)
    {
        var previewRoot = PreviewRoot(args);
        Check("OBS: rótulos do cadastro seguem o contraste do tema", () =>
        {
            using var fixture = new ApiFixture();
            fixture.AddUnit("UBS Contraste");
            var window = OpenOperational(fixture, storageRoot, "registration-contrast");
            try
            {
                var vm = LoginObs(window); window.ViewModel.Navigate("patients"); Settle(vm);
                foreach (var mode in new[] { Appearance.Light, Appearance.Dark, Appearance.System })
                {
                    window.ViewModel.SetAppearance(mode);
                    var content = Layout(window, 1280, 740);
                    var expander = Descendants<Border>(content).Single(card => Nora.Desktop.Views.SubPage.GetKey(card) == "register"); 
                    content = Layout(window, 1280, 740);
                    var label = Descendants<TextBlock>(expander).Single(text => text.Text == "Nome completo");
                    Require(((SolidColorBrush)label.Foreground).Color == ((SolidColorBrush)application.FindResource("TextBrush")).Color, $"Rótulo do cadastro não usa a cor de texto do tema {mode}.");
                    SavePreview(content, Path.Combine(previewRoot, $"obs-registration-contrast-{mode.ToString().ToLowerInvariant()}.png"));
                }
            }
            finally { window.ViewModel.ConfirmClose = false; window.Close(); }
        });
        Check("OBS: dica longa quebra linha sem ultrapassar a largura disponível", () =>
        {
            var tooltip = new ToolTip { Content = "A presença indica atividade recente do paciente no APPNORA, inclusive quando o aplicativo está em segundo plano." };
            try
            {
                tooltip.IsOpen = true;
                application.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                tooltip.UpdateLayout();
                var label = Descendants<TextBlock>(tooltip).Single(text => text.Text == tooltip.Content.ToString());
                Require(label.TextWrapping == TextWrapping.Wrap && tooltip.ActualWidth <= 360 && tooltip.ActualHeight > 40, "Tooltip longo não quebra a explicação em linhas legíveis.");
            }
            finally { tooltip.IsOpen = false; }
        });
        Check("OBS: texto da ação roxa usa cor com contraste definido pelo tema", () =>
        {
            var window = new ShellWindow(DesktopProfile.Operational, Path.Combine(storageRoot, "primary-contrast"));
            try
            {
                window.ViewModel.ConfirmClose = false;
                PrepareWindow(window); window.ViewModel.SetAppearance(Appearance.Light);
                var content = Layout(window, 980, 640);
                var button = FindActionButton(content, "Entrar");
                var label = Descendants<TextBlock>(button).Single(text => text.Text == "Entrar");
                Require(((SolidColorBrush)label.Foreground).Color == ((SolidColorBrush)application.FindResource("BrandTextBrush")).Color, "Texto real do botão roxo perdeu a cor de contraste BrandTextBrush.");
            }
            finally { window.Close(); }
        });
        Check("OBS: input senha possui a mesma altura compacta dos filtros", () =>
        {
            var window = new ShellWindow(DesktopProfile.Operational, Path.Combine(storageRoot, "input-height"));
            try
            {
                window.ViewModel.ConfirmClose = false; PrepareWindow(window);
                var content = Layout(window, 980, 640);
                var password = Descendants<PasswordBox>(content).Single(input => AutomationProperties.GetName(input) == "Senha de acesso");
                Require(password.ActualHeight is >= 42 and <= 46, $"Senha tem altura {password.ActualHeight:F0}, incompatível com inputs compactos de 42px.");
            }
            finally { window.Close(); }
        });
        Check("OBS: cadastros da UBS, filtros, menu e dicas em 980×640 e três temas", () =>
        {
            using var fixture = new ApiFixture();
            var unit = fixture.AddUnit("UBS Layout");
            for (var index = 1; index <= 20; index++)
                fixture.AddRegistry(unit.Id, new UnitPatientRecord($"{index:00000000000}", index == 1 ? "Paciente Fixture " + new string('N', 144) : $"Paciente Fixture {index:00}",
                    index == 1 ? new string('p', 64) + "@" + new string('d', 60) + "." + new string('e', 60) + "." + new string('f', 62) + ".test" : $"patient-with-long-email-{index}@example.invalid", "(48) 99900-0001", null, true, index % 2 == 0, DateTimeOffset.UtcNow.AddMinutes(-index)));
            var window = OpenOperational(fixture, storageRoot, "patient-layout");
            try
            {
                var vm = LoginObs(window);
                window.ViewModel.Navigate("patients"); Settle(vm);
                foreach (var mode in new[] { Appearance.Light, Appearance.Dark, Appearance.System })
                {
                    window.ViewModel.SetAppearance(mode);
                    foreach (var size in new[] { new Size(980, 640), new Size(1280, 740) })
                    {
                        var content = Layout(window, size.Width, size.Height);
                        var list = Descendants<ListBox>(content).Single(box => AutomationProperties.GetName(box) == "Cadastros de referência da UBS");
                        Require(list.Items.Count == 20 && list.ActualHeight > 0 && list.ActualWidth <= content.ActualWidth, "Listagem da UBS não foi renderizada dentro da janela.");
                        list.SelectedItem = vm.ObsRegistry[1];
                        Layout(window, size.Width, size.Height);
                        var selectedCpf = vm.SelectedObsRegistry?.Cpf;
                        Require(selectedCpf == vm.ObsRegistry[1].Cpf, "Seleção da linha não atualizou o cadastro.");
                        Pump(vm.RefreshAutomaticallyAsync());
                        Layout(window, size.Width, size.Height);
                        Require(vm.SelectedObsRegistry?.Cpf == selectedCpf && (list.SelectedItem as UnitPatientRecord)?.Cpf == selectedCpf, "Atualização perdeu a seleção no controle nativo.");
                        list.SelectedItem = null;
                        var prohibited = new[] { "Conta conectada", "NORA OBS", "Seu espaço de cuidado", "Uma jornada organizada, com acesso seguro.", "Preferências salvas neste computador" };
                        Require(!Descendants<TextBlock>(content).Any(text => text.IsVisible && prohibited.Contains(text.Text)), "Texto solicitado para remoção permanece visível.");
                        var search = Input(content, "Pesquisar cadastro da UBS");
                        Require(search.ActualHeight is >= 42 and <= 46, $"Pesquisa tem altura {search.ActualHeight:F0}, incompatível com filtros de 42px.");
                        var inputBorder = Descendants<Border>(search).FirstOrDefault(border => border.CornerRadius.TopLeft > 0);
                        Require(inputBorder is not null && inputBorder.CornerRadius.TopLeft <= 8, "Pesquisa não possui o arredondamento discreto dos inputs.");
                        var presence = Combo(content, "Filtrar presença na UBS");
                        presence.SelectedValue = "Online";
                        Layout(window, size.Width, size.Height);
                        Require(list.Items.Count == 10 && list.Items.Cast<UnitPatientRecord>().All(patient => patient.IsOnline), "Filtro nativo online não atualizou a listagem.");
                        presence.SelectedValue = "Todos";
                        var sort = Combo(content, "Ordenar cadastro da UBS");
                        sort.SelectedValue = "Nome A–Z";
                        Layout(window, size.Width, size.Height);
                        Require(list.Items.Cast<UnitPatientRecord>().Select(patient => patient.FullName).SequenceEqual(vm.ObsRegistry.OrderBy(patient => patient.FullName, StringComparer.CurrentCultureIgnoreCase).Select(patient => patient.FullName)), "Ordenação nativa não atualizou a listagem.");
                        sort.SelectedValue = "Mais recentes";
                        ((System.Windows.Controls.Primitives.Popup)window.FindName("FiltersPopup")).IsOpen = false;
                        SavePreview(Layout(window, size.Width, size.Height), Path.Combine(previewRoot, $"obs-patients-{mode.ToString().ToLowerInvariant()}-{size.Width:0}x{size.Height:0}.png"));
                    }
                }
                window.ViewModel.SetAppearance(Appearance.Light);
                var root = Layout(window, 1280, 740);
                var originalWidth = window.ViewModel.SidebarWidth;
                window.ViewModel.ToggleSidebarCommand.Execute(null);
                Require(window.ViewModel.SidebarWidth != originalWidth, "Comando da barra lateral não alterna a largura.");
                window.ViewModel.ToggleSidebarCommand.Execute(null);
                var account = Descendants<Button>(root).Single(button => AutomationProperties.GetName(button) == "Menu da conta NORA");
                Require(account.TranslatePoint(new Point(0, 0), root).Y > root.ActualHeight / 2, "Conta não está na parte inferior da navegação.");
                InvokeButton(account);
                var menu = account.ContextMenu!;
                Require(menu.IsOpen && menu.Items.Count == 3, "A conta deve mostrar Configurações, Dados do perfil e Sair.");
                menu.UpdateLayout();
                SavePreview(menu, Path.Combine(previewRoot, "obs-account-menu-light.png"));
                menu.IsOpen = false;
                InvokeAccountAction(window, "Configurações");
                Require(window.ViewModel.IsSettings, "Menu da conta não abriu configurações.");
                SavePreview(Layout(window, 1280, 740), Path.Combine(previewRoot, "obs-settings-via-account-light.png"));
            }
            finally { window.Close(); }
        });
        Check("OBS e Municipal: cantos nativos restaurados e maximização/tela cheia", () =>
        {
            if (Environment.OSVersion.Version.Build < 22000)
                throw new InvalidOperationException("Conferência de cantos nativos exige Windows 11 neste ambiente.");
            foreach (var profile in new[] { DesktopProfile.Operational, DesktopProfile.Municipal })
            {
                var window = new ShellWindow(profile, Path.Combine(storageRoot, "native-corners", profile.Id)) { Opacity = 0 };
                try
                {
                    window.ViewModel.ConfirmClose = false;
                    PrepareWindow(window);
                    var handle = new WindowInteropHelper(window).Handle;
                    Require(DwmGetWindowAttribute(handle, 33, out var corner, sizeof(int)) == 0 && corner == 3, "Janela restaurada não solicita cantos discretos nativos.");
                    window.WindowState = WindowState.Maximized;
                    Require(DwmGetWindowAttribute(handle, 33, out corner, sizeof(int)) == 0 && corner == 1, "Janela maximizada mantém arredondamento de janela restaurada.");
                    window.WindowState = WindowState.Normal;
                    window.ViewModel.FullScreen = true;
                    Require(DwmGetWindowAttribute(handle, 33, out corner, sizeof(int)) == 0 && corner == 1 && window.WindowStyle == WindowStyle.None, "Tela cheia manteve cantos ou bordas.");
                    window.ViewModel.FullScreen = false;
                    Require(DwmGetWindowAttribute(handle, 33, out corner, sizeof(int)) == 0 && corner == 3 && window.WindowStyle == WindowStyle.SingleBorderWindow, "Sair de tela cheia não restaurou cantos/moldura.");
                }
                finally { window.Close(); }
            }
        });
    }
    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out int value, int size);
}
