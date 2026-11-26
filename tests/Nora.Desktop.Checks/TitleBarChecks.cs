using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Threading;
using Nora.Desktop.Models;
using Nora.Desktop.Services;
using Nora.Desktop.ViewModels;
using Nora.Desktop.Views;

namespace Nora.Desktop.Checks;

/// <summary>Barra de título própria, voltar e avançar, barra lateral recolhível e janela maximizada ou em tela cheia.</summary>
internal static partial class Program
{
    private static readonly string[] TitleBarButtons = ["ToggleSidebarButton", "NavBackButton", "NavForwardButton", "TitleSearchButton", "MinimizeButton", "MaximizeButton", "CloseButton"];

    private static ShellWindow OpenHidden(DesktopProfile profile, string storageRoot, string name)
    {
        var window = new ShellWindow(profile, Path.Combine(storageRoot, name)) { Opacity = 0 };
        window.ViewModel.ConfirmClose = false; window.ViewModel.Animations = false;
        PrepareWindow(window);
        return window;
    }

    private static void RunTitleBarChecks(Application application, string storageRoot)
    {
        foreach (var profile in new[] { DesktopProfile.Operational, DesktopProfile.Municipal })
        Check($"{profile.Name}: barra de título própria com os botões na ordem pedida, sem o nome do aplicativo e sem a seta de voltar no cabeçalho", () =>
        {
            var window = OpenHidden(profile, storageRoot, "title-bar-" + profile.Id);
            try
            {
                foreach (var mode in new[] { Appearance.Light, Appearance.Dark })
                {
                    window.ViewModel.SetAppearance(mode);
                    var content = Layout(window, 1280, 740);
                    var bar = (Border)window.FindName("TitleBar");
                    var buttons = TitleBarButtons.ToDictionary(name => name, name => (Button)window.FindName(name) ?? throw new InvalidOperationException("Botão ausente: " + name));
                    foreach (var (name, button) in buttons)
                        Require(button.IsVisible && AutomationProperties.GetName(button).Length > 0 && WindowChrome.GetIsHitTestVisibleInChrome(button), $"{name} não está visível, sem nome acessível ou fora da área clicável da barra.");
                    double Left(Button button) => button.TranslatePoint(new Point(0, 0), content).X;
                    var order = TitleBarButtons.Select(name => Left(buttons[name])).ToArray();
                    Require(order.Zip(order.Skip(1)).All(pair => pair.First < pair.Second), "Ordem dos botões da barra de título incorreta: " + string.Join(", ", order.Select(value => value.ToString("F0"))));
                    Require(Math.Abs(Left(buttons["CloseButton"]) + buttons["CloseButton"].ActualWidth - content.ActualWidth) < 1.5, "Os botões da janela não encostam na borda direita.");
                    Require(Math.Abs(bar.ActualHeight - 35) < 0.5 && Math.Abs(bar.ActualWidth - content.ActualWidth) < 0.5 && Math.Abs(bar.TranslatePoint(new Point(0, 0), content).Y) < 0.5, "A barra de título não ocupa o topo da janela inteira.");
                    var sidebar = (Border)window.FindName("Sidebar");
                    Require(sidebar.TranslatePoint(new Point(0, 0), content).Y >= 34.5, "A barra lateral não começa abaixo da barra de título.");
                    Require(!Descendants<TextBlock>(bar).Any(text => text.Text.Contains("NORA", StringComparison.OrdinalIgnoreCase)) && window.Title == profile.Name, "O nome do aplicativo não pode ser desenhado na barra de título (só em Window.Title).");
                    var chrome = WindowChrome.GetWindowChrome(window);
                    Require(chrome.CaptionHeight == 35 && chrome.ResizeBorderThickness == new Thickness(6) && chrome.GlassFrameThickness == new Thickness(0) && !chrome.UseAeroCaptionButtons, "WindowChrome fora do combinado.");
                    var surface = ((SolidColorBrush)bar.Background).Color;
                    Require(surface == ((SolidColorBrush)application.FindResource("TitleBarBrush")).Color && surface == ((LinearGradientBrush)sidebar.Background).GradientStops[0].Color, $"A barra de título não usa a superfície da barra lateral no tema {mode}.");
                    var header = (FrameworkElement)window.FindName("PageHeader");
                    Require(window.FindName("BackButton") == null && !Descendants<Button>(header).Any(button => AutomationProperties.GetName(button).StartsWith("Voltar")), "A seta de voltar continua no cabeçalho da tela.");
                }
            }
            finally { CloseWindow(window); }
        });
        Check("Barra de título: voltar e avançar habilitam, desabilitam e percorrem o histórico nos dois sentidos", () =>
        {
            var window = OpenHidden(DesktopProfile.Operational, storageRoot, "title-history");
            try
            {
                var vm = window.ViewModel;
                var content = Layout(window, 1280, 740);
                var back = (Button)window.FindName("NavBackButton");
                var forward = (Button)window.FindName("NavForwardButton");
                void Idle() => window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                Idle();
                Require(!back.IsEnabled && !forward.IsEnabled && !vm.CanGoBack && !vm.CanGoForward && vm.ForwardTip == "Nenhuma página para avançar", "Sem histórico os dois botões deveriam estar desabilitados.");
                vm.Navigate("agenda"); vm.Navigate("announcements"); vm.Navigate("indicators"); Idle();
                Require(back.IsEnabled && !forward.IsEnabled && vm.BackTip.StartsWith("Voltar para Comunicados"), "Depois de navegar só o voltar deveria estar habilitado: " + vm.BackTip);
                InvokeButton(back); Idle();
                Require(vm.Route == "announcements" && back.IsEnabled && forward.IsEnabled && vm.ForwardTip.StartsWith("Avançar para Desempenho"), $"Voltar não levou a Comunicados ({vm.Route}; {vm.ForwardTip}).");
                InvokeButton(back); Idle();
                Require(vm.Route == "agenda" && forward.IsEnabled, "Segundo voltar não levou à Agenda.");
                InvokeButton(forward); Idle();
                Require(vm.Route == "announcements" && back.IsEnabled && forward.IsEnabled, "Avançar não retornou a Comunicados.");
                InvokeButton(forward); Idle();
                Require(vm.Route == "indicators" && back.IsEnabled && !forward.IsEnabled && vm.ForwardTip == "Nenhuma página para avançar", "Avançar até o fim deveria desabilitar o botão.");
                InvokeButton(back); InvokeButton(back); Idle();
                vm.Navigate(vm.Route); Idle();
                Require(vm.CanGoForward, "Abrir de novo a página atual não pode apagar o avanço.");
                vm.Navigate("alerts"); Idle();
                Require(vm.Route == "alerts" && !vm.CanGoForward && !forward.IsEnabled, "Navegar para uma rota nova deveria limpar o avanço.");
                Require(back.IsEnabled && vm.BackTip.StartsWith("Voltar para Agenda"), "Depois de limpar o avanço o voltar deveria levar à Agenda: " + vm.BackTip);
                // Cada pilha guarda no máximo 50 rotas.
                for (var index = 0; index < 120; index++) vm.Navigate(index % 2 == 0 ? "home" : "agenda");
                var steps = 0; while (vm.CanGoBack) { vm.GoBack(); steps++; }
                Require(steps == 50, $"A pilha de voltar deveria guardar 50 rotas ({steps}).");
                steps = 0; while (vm.CanGoForward) { vm.GoForward(); steps++; }
                Require(steps == 50, $"A pilha de avançar deveria guardar 50 rotas ({steps}).");
            }
            finally { CloseWindow(window); }
        });
        Check("Barra lateral: recolher e reabrir some de verdade, volta à largura salva e persiste", () =>
        {
            var path = Path.Combine(storageRoot, "title-sidebar");
            var window = OpenHidden(DesktopProfile.Operational, storageRoot, "title-sidebar");
            try
            {
                var vm = window.ViewModel;
                vm.SidebarWidth = 300;
                var content = Layout(window, 1280, 740);
                var column = (ColumnDefinition)window.FindName("SidebarColumn");
                var sidebarContent = (FrameworkElement)window.FindName("SidebarContent");
                var splitter = (FrameworkElement)window.FindName("SidebarSplitter");
                var workspace = (FrameworkElement)window.FindName("WorkspaceLayout");
                var toggle = (Button)window.FindName("ToggleSidebarButton");
                Require(Math.Abs(column.ActualWidth - 300) < 1 && sidebarContent.IsVisible && splitter.ActualWidth > 0, "A barra lateral aberta deveria ter a largura salva.");
                InvokeButton(toggle); content = Layout(window, 1280, 740);
                Require(!vm.SidebarExpanded && column.ActualWidth == 0 && !sidebarContent.IsVisible && splitter.ActualWidth == 0 && Math.Abs(workspace.ActualWidth - content.ActualWidth) < 1, $"Recolhida, a barra e o divisor deveriam sumir e a tela ocupar a largura toda (expandida={vm.SidebarExpanded}; coluna={column.ActualWidth:F1}; conteúdo visível={sidebarContent.IsVisible}; divisor={splitter.ActualWidth:F1}; tela={workspace.ActualWidth:F1} de {content.ActualWidth:F1}).");
                Require(vm.SidebarWidth == 300, "Recolher não pode mudar a largura salva.");
                var saved = new PreferencesService("obs", path).Load();
                Require(!saved.SidebarExpanded && saved.SidebarWidth == 300, "O estado recolhido não foi gravado.");
                InvokeButton(toggle); content = Layout(window, 1280, 740);
                Require(vm.SidebarExpanded && Math.Abs(column.ActualWidth - 300) < 1 && sidebarContent.IsVisible && column.MinWidth == 208, "Reabrir deveria voltar à largura salva.");
                saved = new PreferencesService("obs", path).Load();
                Require(saved.SidebarExpanded && saved.SidebarWidth == 300, "O estado aberto não foi gravado.");
                vm.ToggleSidebarCommand.Execute(null);
                Require(!vm.SidebarExpanded, "O comando da barra lateral não recolheu.");
                vm.SidebarWidth = 240;
                Layout(window, 1280, 740);
                Require(column.ActualWidth == 0, "Mudar a largura salva não pode reabrir a barra recolhida.");
                vm.ToggleSidebarCommand.Execute(null); Layout(window, 1280, 740);
                Require(vm.SidebarExpanded && Math.Abs(column.ActualWidth - 240) < 1, "A largura escolhida com a barra recolhida deveria valer ao reabrir.");
            }
            finally { CloseWindow(window); }
        });
        Check("Barra lateral: com animação a coluna termina na largura certa, sem gravar valores intermediários e mesmo com cliques rápidos", () =>
        {
            var path = Path.Combine(storageRoot, "title-sidebar-motion");
            var window = OpenHidden(DesktopProfile.Operational, storageRoot, "title-sidebar-motion");
            try
            {
                var vm = window.ViewModel;
                vm.SidebarWidth = 300;
                Layout(window, 1280, 740);
                vm.Animations = true;
                var column = (ColumnDefinition)window.FindName("SidebarColumn");
                var sidebar = (FrameworkElement)window.FindName("Sidebar");
                var sidebarContent = (FrameworkElement)window.FindName("SidebarContent");
                bool Settled() => BindingOperations.IsDataBound(column, ColumnDefinition.WidthProperty) && sidebar.IsHitTestVisible;
                vm.SidebarExpanded = false;
                Require(!BindingOperations.IsDataBound(column, ColumnDefinition.WidthProperty) && !sidebar.IsHitTestVisible, "A animação de recolher não começou.");
                PumpUntil(Settled);
                Layout(window, 1280, 740);
                Require(column.ActualWidth == 0 && !sidebarContent.IsVisible && vm.SidebarWidth == 300, $"Recolher animado terminou em {column.ActualWidth:F0}.");
                vm.SidebarExpanded = true;
                PumpUntil(Settled);
                Layout(window, 1280, 740);
                Require(Math.Abs(column.ActualWidth - 300) < 1 && sidebarContent.IsVisible && vm.SidebarWidth == 300, $"Reabrir animado terminou em {column.ActualWidth:F0}.");
                vm.SidebarExpanded = false; vm.SidebarExpanded = true; vm.SidebarExpanded = false; vm.SidebarExpanded = true;
                PumpUntil(Settled);
                Layout(window, 1280, 740);
                Require(Math.Abs(column.ActualWidth - 300) < 1 && sidebarContent.IsVisible, "Cliques rápidos deveriam terminar na largura aberta.");
                var saved = new PreferencesService("obs", path).Load();
                Require(saved.SidebarExpanded && saved.SidebarWidth == 300, $"Movimento gravou valores intermediários (largura {saved.SidebarWidth}).");
            }
            finally { CloseWindow(window); }
        });
        Check("Janela: maximizada ocupa a área de trabalho do monitor, restaurada não tem região do WindowChrome e o tamanho mínimo está na escala da tela", () =>
        {
            foreach (var profile in new[] { DesktopProfile.Operational, DesktopProfile.Municipal })
            {
                var window = OpenHidden(profile, storageRoot, "title-maximize-" + profile.Id);
                try
                {
                    var handle = new WindowInteropHelper(window).Handle;
                    Layout(window, 1280, 740);
                    var maximize = (Button)window.FindName("MaximizeButton");
                    Require(!HasWindowRegion(handle) && AutomationProperties.GetName(maximize) == "Maximizar janela", "Restaurada, a janela não deveria ter região do WindowChrome (cantos nativos).");
                    var info = new NativeMinMaxInfo();
                    var memory = Marshal.AllocHGlobal(Marshal.SizeOf<NativeMinMaxInfo>());
                    try
                    {
                        Marshal.StructureToPtr(info, memory, false);
                        SendMessage(handle, 0x0024, IntPtr.Zero, memory);
                        info = Marshal.PtrToStructure<NativeMinMaxInfo>(memory);
                    }
                    finally { Marshal.FreeHGlobal(memory); }
                    var (monitor, work) = MonitorOf(handle);
                    Require(info.MaxSize.X == work.Right - work.Left && info.MaxSize.Y == work.Bottom - work.Top && info.MaxPosition.X == work.Left - monitor.Left && info.MaxPosition.Y == work.Top - monitor.Top, "A janela maximizada deveria ocupar exatamente a área de trabalho do monitor.");
                    var scale = VisualTreeHelper.GetDpi(window);
                    Require(info.MinTrackSize.X == (int)Math.Ceiling(window.MinWidth * scale.DpiScaleX) && info.MinTrackSize.Y == (int)Math.Ceiling(window.MinHeight * scale.DpiScaleY), "O tamanho mínimo não está na escala da tela.");
                    window.WindowState = WindowState.Maximized;
                    window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                    (monitor, work) = MonitorOf(handle);
                    GetWindowRect(handle, out var bounds);
                    Require(bounds.Left == work.Left && bounds.Top == work.Top && bounds.Right == work.Right && bounds.Bottom == work.Bottom, $"Maximizada, a janela deveria ocupar a área de trabalho ({bounds.Left},{bounds.Top},{bounds.Right},{bounds.Bottom} contra {work.Left},{work.Top},{work.Right},{work.Bottom}).");
                    var content = Layout(window, 1280, 740);
                    var close = (Button)window.FindName("CloseButton");
                    Require(AutomationProperties.GetName(maximize) == "Restaurar janela" && !((FrameworkElement)window.FindName("MaximizeIcon")).IsVisible && ((FrameworkElement)window.FindName("RestoreIcon")).IsVisible, "Maximizada, o botão deveria virar Restaurar.");
                    Require(close.TranslatePoint(new Point(close.ActualWidth, 0), content).X <= content.ActualWidth + 0.5, "O botão Fechar vazou da janela maximizada.");
                    InvokeButton(maximize);
                    Require(window.WindowState == WindowState.Normal && AutomationProperties.GetName(maximize) == "Maximizar janela", "O botão não restaurou a janela.");
                    window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                    Require(!HasWindowRegion(handle), "Depois de restaurar a janela voltou a ter região do WindowChrome.");
                }
                finally { CloseWindow(window); }
            }
        });
        Check("Janela: a área livre da barra arrasta e o duplo clique maximiza e restaura, os botões continuam clicáveis e as bordas redimensionam", () =>
        {
            var window = OpenHidden(DesktopProfile.Operational, storageRoot, "title-drag");
            try
            {
                var handle = new WindowInteropHelper(window).Handle;
                var content = Layout(window, 1280, 740);
                FrameworkElement Part(string name) => (FrameworkElement)window.FindName(name);
                IntPtr ScreenPoint(double x, double y)
                {
                    var point = content.PointToScreen(new Point(x, y));
                    return (IntPtr)(((int)point.Y & 0xFFFF) << 16 | ((int)point.X & 0xFFFF));
                }
                int HitTest(double x, double y) => (int)SendMessage(handle, 0x0084, IntPtr.Zero, ScreenPoint(x, y));
                const int Client = 1, Caption = 2, Left = 10, Bottom = 15;
                var searchRight = Part("TitleSearchButton").TranslatePoint(new Point(Part("TitleSearchButton").ActualWidth, 0), content).X;
                var minimizeLeft = Part("MinimizeButton").TranslatePoint(new Point(0, 0), content).X;
                var free = (searchRight + minimizeLeft) / 2;
                Require(HitTest(free, 17) == Caption, "A área livre da barra deveria arrastar a janela (HTCAPTION).");
                foreach (var name in TitleBarButtons)
                {
                    var button = Part(name);
                    var center = button.TranslatePoint(new Point(button.ActualWidth / 2, button.ActualHeight / 2), content);
                    Require(HitTest(center.X, center.Y) == Client, $"O botão {name} deveria receber o clique (HTCLIENT).");
                }
                Require(HitTest(free, 80) == Client, "Abaixo da barra de título o clique pertence ao conteúdo.");
                Require(HitTest(2, 300) == Left && HitTest(600, content.ActualHeight - 2) == Bottom, "As bordas da janela deveriam redimensionar.");
                SendMessage(handle, 0x00A3, (IntPtr)Caption, ScreenPoint(free, 17));
                Require(window.WindowState == WindowState.Maximized, "O duplo clique na barra deveria maximizar.");
                SendMessage(handle, 0x00A3, (IntPtr)Caption, ScreenPoint(free, 17));
                Require(window.WindowState == WindowState.Normal, "O duplo clique na barra maximizada deveria restaurar.");
            }
            finally { CloseWindow(window); }
        });
        Check("Janela: o botão Fechar da barra de título respeita a confirmação de fechamento", () =>
        {
            var window = OpenHidden(DesktopProfile.Operational, storageRoot, "title-close");
            var closed = false;
            window.Closed += (_, _) => closed = true;
            var asked = new List<string>();
            var answer = false;
            DialogService.Handler = request => { asked.Add(request.Title); return answer; };
            try
            {
                window.ViewModel.ConfirmClose = true;
                Layout(window, 1280, 740);
                var close = (Button)window.FindName("CloseButton");
                InvokeButton(close);
                Require(asked.Count == 1 && asked[0] == "Fechar NORA OBS?" && !closed && window.IsVisible, "Cancelar a confirmação deveria manter a janela aberta.");
                answer = true;
                InvokeButton(close);
                Require(asked.Count == 2 && closed, "Confirmar deveria fechar a janela.");
            }
            finally { DialogService.Handler = null; if (!closed) CloseWindow(window); }
        });
        Check("Janela: tela cheia (F11) esconde a barra de título e libera o topo; ao sair a barra volta com os cantos e a moldura", () =>
        {
            foreach (var profile in new[] { DesktopProfile.Operational, DesktopProfile.Municipal })
            {
                var window = OpenHidden(profile, storageRoot, "title-fullscreen-" + profile.Id);
                try
                {
                    var handle = new WindowInteropHelper(window).Handle;
                    var bar = (FrameworkElement)window.FindName("TitleBar");
                    var chrome = WindowChrome.GetWindowChrome(window);
                    Layout(window, 1280, 740);
                    Require(bar.IsVisible && chrome.CaptionHeight == 35, "A barra deveria estar visível fora da tela cheia.");
                    window.ViewModel.FullScreen = true;
                    window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                    var (monitor, _) = MonitorOf(handle);
                    GetWindowRect(handle, out var bounds);
                    Require(!bar.IsVisible && chrome.CaptionHeight == 0 && chrome.ResizeBorderThickness == new Thickness(0), "Em tela cheia a barra de título e a faixa de arrasto deveriam sumir.");
                    Require(Math.Abs(bounds.Left - monitor.Left) <= 2 && Math.Abs(bounds.Top - monitor.Top) <= 2 && Math.Abs(bounds.Right - monitor.Right) <= 2 && Math.Abs(bounds.Bottom - monitor.Bottom) <= 2, $"Tela cheia não cobre o monitor inteiro ({bounds.Left},{bounds.Top},{bounds.Right},{bounds.Bottom} contra {monitor.Left},{monitor.Top},{monitor.Right},{monitor.Bottom}).");
                    window.ViewModel.FullScreen = false;
                    Layout(window, 1280, 740);
                    Require(bar.IsVisible && chrome.CaptionHeight == 35 && chrome.ResizeBorderThickness == new Thickness(6) && window.WindowStyle == WindowStyle.None && window.ResizeMode == ResizeMode.CanResize, "Ao sair da tela cheia a barra de título e a moldura deveriam voltar.");
                    Require(DwmGetWindowAttribute(handle, 33, out var corner, sizeof(int)) == 0 && corner == 3 && !HasWindowRegion(handle), "Ao sair da tela cheia os cantos nativos deveriam voltar.");
                }
                finally { CloseWindow(window); }
            }
        });
        Check("Janela: na tela de acesso e na troca obrigatória de senha só aparecem os três botões da janela", () =>
        {
            using var fixture = new ApiFixture { MustChangePassword = true };
            fixture.AddUnit("UBS do acesso");
            var path = Path.Combine(storageRoot, "title-access");
            var window = new ShellWindow(DesktopProfile.Municipal, path, new NoraApiClient(fixture), new ObsDeviceStore(path), compactAccess: true) { Opacity = 0 };
            window.ViewModel.ConfirmClose = false; window.ViewModel.Animations = false;
            window.ViewModel.Workspace.Endpoint = "https://fixture.invalid";
            PrepareWindow(window);
            try
            {
                var vm = window.ViewModel.Workspace;
                Layout(window, 1080, 740);
                void OnlyWindowButtons(string moment)
                {
                    Layout(window, 1080, 740);
                    var visible = TitleBarButtons.Where(name => ((Button)window.FindName(name)).IsVisible).ToArray();
                    Require(visible.SequenceEqual(["MinimizeButton", "MaximizeButton", "CloseButton"]), $"{moment}: deveriam aparecer só os botões da janela ({string.Join(", ", visible)}).");
                }
                OnlyWindowButtons("Tela de acesso");
                var access = Descendants<DesktopAccessView>(Layout(window, 1080, 740)).Single(view => view.IsVisible);
                Descendants<TextBox>(access).Single(box => box.IsVisible).Text = "fixture@example.invalid";
                Descendants<PasswordBox>(access).Single(box => box.IsVisible).Password = "isolated-test-password";
                InvokeButton(FindActionButton(access, "Entrar")); Settle(vm);
                Require(vm.IsAuthenticated && vm.MustChangePassword, "A fixture deveria exigir a troca de senha.");
                OnlyWindowButtons("Troca obrigatória de senha");
                var back = (Button)window.FindName("NavBackButton");
                Require(!window.ViewModel.CanGoBack && !window.ViewModel.CanGoForward && !back.IsVisible, "Na troca obrigatória não há para onde voltar ou avançar.");
                Pump(vm.ChangePasswordAsync("isolated-test-password", "definitiva-123", "definitiva-123"));
                Require(!vm.MustChangePassword, "A troca de senha não terminou.");
                Layout(window, 1080, 740);
                Require(TitleBarButtons.All(name => ((Button)window.FindName(name)).IsVisible), "Depois da troca todos os botões da barra deveriam voltar.");
            }
            finally { CloseWindow(window); }
        });
        Check("Janela: o fundo escurecido dos diálogos cobre a janela inteira, inclusive a barra de título", () =>
        {
            var window = OpenHidden(DesktopProfile.Operational, storageRoot, "title-dim");
            try
            {
                var content = Layout(window, 1280, 740);
                var overlay = (FrameworkElement)window.FindName("DialogOverlay");
                Require(!overlay.IsVisible, "O fundo escurecido deveria começar escondido.");
                window.SetDimmed(true);
                content = Layout(window, 1280, 740);
                var origin = overlay.TranslatePoint(new Point(0, 0), content);
                Require(overlay.IsVisible && Math.Abs(origin.Y) < 0.5 && Math.Abs(origin.X) < 0.5 && Math.Abs(overlay.ActualHeight - content.ActualHeight) < 0.5 && Math.Abs(overlay.ActualWidth - content.ActualWidth) < 0.5, "O fundo escurecido não cobre a barra de título.");
                window.SetDimmed(false);
                Layout(window, 1280, 740);
                Require(!overlay.IsVisible, "O fundo escurecido deveria sair com o diálogo.");
            }
            finally { CloseWindow(window); }
        });
    }

    private static bool HasWindowRegion(IntPtr window) => GetWindowRgnBox(window, out _) != 0;

    private static (NativeRect Monitor, NativeRect Work) MonitorOf(IntPtr window)
    {
        var info = new NativeMonitorInfo { Size = Marshal.SizeOf<NativeMonitorInfo>() };
        Require(GetMonitorInfo(MonitorFromWindow(window, 2), ref info), "O Windows não informou o monitor da janela.");
        return (info.Monitor, info.Work);
    }

    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct NativeMinMaxInfo { public NativePoint Reserved, MaxSize, MaxPosition, MinTrackSize, MaxTrackSize; }
    [StructLayout(LayoutKind.Sequential)] private struct NativeMonitorInfo { public int Size; public NativeRect Monitor, Work; public uint Flags; }
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetWindowRect(IntPtr window, out NativeRect bounds);
    [DllImport("user32.dll")] private static extern int GetWindowRgnBox(IntPtr window, out NativeRect box);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetMonitorInfo(IntPtr monitor, ref NativeMonitorInfo info);
}
