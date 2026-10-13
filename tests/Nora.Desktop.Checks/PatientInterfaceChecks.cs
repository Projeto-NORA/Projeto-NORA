using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Nora.Desktop.Models;
using Nora.Desktop.Services;
using Nora.Desktop.Views;

namespace Nora.Desktop.Checks;

internal static partial class Program
{
    private static void RunPatientInterfaceChecks(Application application, string storageRoot, string[] args)
    {
        var previewArgument = Array.IndexOf(args, "--preview-dir");
        var previewRoot = previewArgument >= 0 && previewArgument + 1 < args.Length ? args[previewArgument + 1] : Path.Combine(AppContext.BaseDirectory, "previews");
        Check("OBS: rótulos do cadastro seguem o contraste do tema", () =>
        {
            using var fixture = new ApiFixture();
            var window = new ShellWindow(DesktopProfile.Operational, Path.Combine(storageRoot, "registration-contrast"), new NoraApiClient(fixture));
            try
            {
                window.ViewModel.ConfirmClose = false; PrepareWindow(window);
                var vm = window.ViewModel.Workspace;
                vm.Endpoint = "https://fixture.invalid"; vm.Identifier = "fixture@example.invalid";
                Pump(vm.LoginAsync("isolated-test-password")); window.ViewModel.Navigate("patients");
                foreach (var mode in new[] { Appearance.Light, Appearance.Dark, Appearance.System })
                {
                    window.ViewModel.SetAppearance(mode);
                    var content = Layout(window, 1280, 740);
                    var expander = Descendants<Expander>(content).Single(); expander.IsExpanded = true;
                    content = Layout(window, 1280, 740);
                    var label = Descendants<TextBlock>(expander).Single(text => text.Text == "Nome completo");
                    Require(((SolidColorBrush)label.Foreground).Color == ((SolidColorBrush)application.FindResource("TextBrush")).Color, $"Rótulo do cadastro não usa a cor de texto do tema {mode}.");
                    SavePreview(content, Path.Combine(previewRoot, $"obs-registration-contrast-{mode.ToString().ToLowerInvariant()}.png"));
                }
            }
            finally { window.Close(); }
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
        Check("OBS: pacientes zebra, contatos longos, filtros, menu e dicas em 980×640 e três temas", () =>
        {
            using var fixture = new ApiFixture { PatientOnline = true };
            for (var index = 1; index <= 20; index++)
                fixture.AddPatient(new(Guid.NewGuid(), index == 1 ? "Paciente Fixture " + new string('N', 144) : $"Paciente Fixture {index:00}", "00000000001", index == 1 ? new string('p', 64) + "@" + new string('d', 60) + "." + new string('e', 60) + "." + new string('f', 62) + ".test" : $"patient-with-long-email-{index}@example.invalid", "(48) 99900-0001", index % 2 == 0));
            var window = new ShellWindow(DesktopProfile.Operational, Path.Combine(storageRoot, "patient-layout"), new NoraApiClient(fixture));
            try
            {
                window.ViewModel.ConfirmClose = false; window.ViewModel.Animations = false;
                PrepareWindow(window);
                var vm = window.ViewModel.Workspace;
                vm.Endpoint = "https://fixture.invalid"; vm.Identifier = "fixture@example.invalid";
                Pump(vm.LoginAsync("isolated-test-password"));
                window.ViewModel.Navigate("patients");
                foreach (var mode in new[] { Appearance.Light, Appearance.Dark, Appearance.System })
                {
                    window.ViewModel.SetAppearance(mode);
                    foreach (var size in new[] { new Size(980, 640), new Size(1280, 740) })
                    {
                        var content = Layout(window, size.Width, size.Height);
                        var table = Descendants<DataGrid>(content).Single(grid => AutomationProperties.GetName(grid) == "Pacientes cadastrados");
                        var pageScrolling = (ScrollViewer)window.FindName("PageScroll");
                        Require(pageScrolling.ScrollableHeight <= 1, $"Pacientes com cadastro recolhido exige rolagem externa adicional de {pageScrolling.ScrollableHeight:F0}px além da listagem.");
                        Require(table.Items.Count == 21 && table.Columns.Count == 1 && table.HeadersVisibility == DataGridHeadersVisibility.None, "Listagem não usa uma linha com nome/contatos sem cabeçalho de colunas.");
                        Require(table.GridLinesVisibility == DataGridGridLinesVisibility.None && table.BorderThickness == new Thickness(0) && !Equals(table.RowBackground, table.AlternatingRowBackground), "Zebra ou separadores da listagem divergentes.");
                        var scrolling = Descendants<ScrollViewer>(table).Single();
                        Require(scrolling.ScrollableHeight > 0 && scrolling.ScrollableWidth == 0, "Pacientes excedentes não têm rolagem vertical ou causam rolagem horizontal.");
                        var rows = Descendants<DataGridRow>(table).Where(row => row.ActualHeight > 0).ToArray();
                        Require(rows.Length > 1 && rows.All(row => row.ActualHeight >= 64), "Linhas/contatos não foram renderizados legivelmente.");
                        table.SelectedItem = vm.Patients[1];
                        Layout(window, size.Width, size.Height);
                        var selectedId = vm.SelectedPatient?.Id;
                        Require(selectedId == vm.Patients[1].Id, "Seleção da linha não atualizou o paciente.");
                        Pump(vm.RefreshAutomaticallyAsync());
                        Layout(window, size.Width, size.Height);
                        Require(vm.SelectedPatient?.Id == selectedId && (table.SelectedItem as PatientAccount)?.Id == selectedId, "Polling perdeu a seleção no controle nativo.");
                        scrolling.ScrollToEnd();
                        Layout(window, size.Width, size.Height);
                        var previousOffset = scrolling.VerticalOffset;
                        Require(previousOffset > 0, "Reprodução não rolou a listagem antes do polling.");
                        Pump(vm.RefreshAutomaticallyAsync());
                        Layout(window, size.Width, size.Height);
                        Require(Math.Abs(scrolling.VerticalOffset - previousOffset) < 0.1, "Polling retornou a rolagem dos pacientes ao topo sem alteração de dados.");
                        scrolling.ScrollToTop();
                        var prohibited = new[] { "Conta conectada", "NORA OBS", "Seu espaço de cuidado", "Uma jornada organizada, com acesso seguro.", "Preferências salvas neste computador" };
                        Require(!Descendants<TextBlock>(content).Any(text => text.IsVisible && prohibited.Contains(text.Text)), "Texto solicitado para remoção permanece visível.");
                        Require(!Descendants<Button>(content).Any(button => button.IsVisible && button.Content?.ToString() is "Atualizar" or "Sair da conta" or "Incluir selecionado na fila"), "Ação removida permanece na tela de pacientes.");
                        var search = Input(content, "Pesquisar pacientes");
                        Require(search.ActualHeight is >= 42 and <= 46, $"Pesquisa tem altura {search.ActualHeight:F0}, incompatível com filtros de 42px.");
                        var inputBorder = Descendants<Border>(search).FirstOrDefault(border => border.CornerRadius.TopLeft > 0);
                        Require(inputBorder is not null && inputBorder.CornerRadius.TopLeft <= 8, "Pesquisa não possui o arredondamento discreto dos inputs.");
                        var presence = Descendants<ComboBox>(content).Single(combo => AutomationProperties.GetName(combo) == "Filtrar presença do paciente");
                        presence.SelectedValue = "Online";
                        Layout(window, size.Width, size.Height);
                        Require(table.Items.Cast<PatientAccount>().All(patient => patient.IsOnline), "Filtro nativo online não atualizou a listagem.");
                        presence.SelectedValue = "Todos";
                        var sort = Descendants<ComboBox>(content).Single(combo => AutomationProperties.GetName(combo) == "Ordenar pacientes");
                        sort.SelectedValue = "Nome A–Z";
                        Layout(window, size.Width, size.Height);
                        Require(table.Items.Cast<PatientAccount>().Select(patient => patient.FullName).SequenceEqual(vm.Patients.OrderBy(patient => patient.FullName, StringComparer.CurrentCultureIgnoreCase).Select(patient => patient.FullName)), "Ordenação nativa não atualizou a listagem.");
                        sort.SelectedValue = "Mais recentes";
                        table.SelectedItem = null;
                        SavePreview(Layout(window, size.Width, size.Height), Path.Combine(previewRoot, $"obs-patients-zebra-{mode.ToString().ToLowerInvariant()}-{size.Width:0}x{size.Height:0}.png"));
                    }
                }
                window.ViewModel.SetAppearance(Appearance.Light);
                var root = Layout(window, 1280, 740);
                var toggle = Descendants<Button>(root).Single(button => AutomationProperties.GetName(button) == "Alternar barra lateral");
                Require(toggle.ToolTip?.ToString()?.Contains("Ctrl+B", StringComparison.Ordinal) == true, "Três barras não explicam ação e atalho.");
                var originalExpanded = window.ViewModel.SidebarExpanded;
                InvokeButton(toggle);
                Require(window.ViewModel.SidebarExpanded != originalExpanded, "Três barras não alternam sidebar.");
                InvokeButton(toggle);
                var account = Descendants<Button>(root).Single(button => AutomationProperties.GetName(button) == "Menu da conta NORA");
                Require(account.TranslatePoint(new Point(0, 0), root).Y > root.ActualHeight / 2, "Conta não está na parte inferior da navegação.");
                InvokeButton(account);
                var menu = account.ContextMenu!;
                Require(menu.IsOpen && menu.Items.Count == 2, "Conta não abriu menu com duas ações.");
                menu.UpdateLayout();
                SavePreview(menu, Path.Combine(previewRoot, "obs-account-menu-light.png"));
                menu.IsOpen = false;
                var tooltip = new ToolTip { Content = toggle.ToolTip, PlacementTarget = toggle };
                tooltip.IsOpen = true;
                application.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                tooltip.UpdateLayout();
                SavePreview(tooltip, Path.Combine(previewRoot, "obs-sidebar-tooltip-light.png"));
                tooltip.IsOpen = false;
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
