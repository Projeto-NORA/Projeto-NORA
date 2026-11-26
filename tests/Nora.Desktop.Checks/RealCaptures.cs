using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Nora.Desktop.Models;
using Nora.Desktop.Services;
using Nora.Desktop.ViewModels;
using Nora.Desktop.Views;

namespace Nora.Desktop.Checks;

/// <summary>
/// Ferramenta de capturas reais: abre os dois programas (OBS-NORA e Painel Municipal) fora da tela, entra na API de verdade com as credenciais das variáveis de ambiente,
/// percorre o menu lido do ShellViewModel em tempo de execução e grava um PNG por tela (1600x900 a 144 DPI = 2400x1350). Só lê da API; abre e fecha pop-ups sem salvar.
/// Uso: <c>--capturas-reais --saida &lt;pasta&gt; --perfil obs|municipal|ambos</c>.
/// </summary>
internal static partial class Program
{
    private const double RealWidth = 1600, RealHeight = 900, RealDpi = 144;
    private static readonly TimeSpan RealSettleLimit = TimeSpan.FromSeconds(20);

    private static string? ArgumentValue(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static int RunRealCaptures(string[] args)
    {
        var output = Path.GetFullPath(ArgumentValue(args, "--saida") ?? Path.Combine(AppContext.BaseDirectory, "capturas-reais"));
        var which = (ArgumentValue(args, "--perfil") ?? "ambos").ToLowerInvariant();
        var url = Environment.GetEnvironmentVariable("NORA_CAPTURA_URL");
        if (string.IsNullOrWhiteSpace(url)) { Console.WriteLine("ERRO Defina NORA_CAPTURA_URL (ex.: http://127.0.0.1:5190)."); return 2; }
        Directory.CreateDirectory(output);
        var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(application.Dispatcher));
        using var themes = new ThemeService(application);
        themes.Apply(Appearance.Light);
        ShellWindow.AutoOpenProfileChoice = false;
        var failures = 0;
        var jobs = new List<(DesktopProfile Profile, string Prefix, string EmailVariable, string PasswordVariable)>();
        if (which is "obs" or "ambos") jobs.Add((DesktopProfile.Operational, "obs", "NORA_CAPTURA_OBS_EMAIL", "NORA_CAPTURA_OBS_SENHA"));
        if (which is "municipal" or "ambos") jobs.Add((DesktopProfile.Municipal, "municipal", "NORA_CAPTURA_MUN_EMAIL", "NORA_CAPTURA_MUN_SENHA"));
        foreach (var job in jobs)
        {
            var email = Environment.GetEnvironmentVariable(job.EmailVariable);
            var password = Environment.GetEnvironmentVariable(job.PasswordVariable);
            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrEmpty(password)) { Console.WriteLine($"ERRO Defina {job.EmailVariable} e {job.PasswordVariable}."); failures++; continue; }
            var storage = Path.Combine(Path.GetTempPath(), "nora-capturas-" + Guid.NewGuid().ToString("N"));
            try { RealProfileCaptures(application, job.Profile, job.Prefix, url, email, password, output, storage); }
            catch (Exception error) { failures++; Console.WriteLine($"ERRO {job.Prefix}: {error.Message}"); Console.WriteLine(error); }
            finally { try { Directory.Delete(storage, true); } catch { } }
        }
        Console.WriteLine($"Capturas gravadas em {output} ({Directory.GetFiles(output, "*.png").Length} imagens).");
        return failures == 0 ? 0 : 1;
    }

    private static ShellWindow OpenRealWindow(Application application, DesktopProfile profile, string storage, bool compact = false)
    {
        var window = new ShellWindow(profile, storage, compactAccess: compact);
        application.MainWindow = window;
        window.ViewModel.ConfirmClose = false;
        window.ViewModel.Animations = false;
        window.ViewModel.SaveNavigation = false;
        window.ViewModel.RememberSidebar = false;
        window.ViewModel.ShowSidebarTips = false;
        window.ViewModel.SetAppearance(Appearance.Light);
        PrepareWindow(window);
        return window;
    }

    private static void RealProfileCaptures(Application application, DesktopProfile profile, string prefix, string url, string email, string password, string output, string storage)
    {
        Console.WriteLine($"== Perfil {prefix} ==");
        // Telas de login: a que o programa abre de verdade (acesso compacto) e a de login completa, claras e escuras.
        var access = OpenRealWindow(application, profile, Path.Combine(storage, "acesso"), compact: true);
        try
        {
            // E-mail preenchido (a senha nunca vai para a imagem).
            try { Input(Layout(access, RealWidth, RealHeight), "E-mail").Text = email; } catch (Exception) { }
            RealSettle(access, null);
            RealSave(access, Path.Combine(output, $"{prefix}-00-login.png"));
            access.ViewModel.SetAppearance(Appearance.Dark); RealSettle(access, null);
            RealSave(access, Path.Combine(output, $"escuro-{prefix}-00-login.png"));
        }
        finally { access.Close(); }

        var window = OpenRealWindow(application, profile, Path.Combine(storage, "dados"));
        try
        {
            var vm = window.ViewModel;
            var workspace = vm.Workspace;
            var content = Layout(window, RealWidth, RealHeight);
            Input(content, "Endereço da API").Text = url;
            Input(content, "E-mail").Text = email;
            Descendants<PasswordBox>(content).Single(input => AutomationProperties.GetName(input) == "Senha de acesso").Password = password;
            InvokeButton(FindActionButton(content, "Entrar"));
            RealPumpUntil(() => !workspace.IsBusy, 60, "login");
            if (!workspace.IsAuthenticated) throw new InvalidOperationException("O login real falhou (confira NORA_CAPTURA_URL, e-mail e senha).");
            if (!profile.IsMunicipal) ChooseRealUnit(workspace);
            RealSettle(window, workspace);

            var routes = vm.Navigation.SelectMany(item => item.HasChildren ? item.Children : [item]).Where(item => item.Available).Select(item => item.Route).Distinct().ToList();
            routes.Add("settings");
            var number = 1;
            foreach (var route in routes)
            {
                if (route == "network") { Console.WriteLine($"AVISO {prefix}: o Mapa da rede usa WebView2 (não renderiza por RenderTargetBitmap); pulado. Use captura da janela real."); continue; }
                var name = $"{prefix}-{number:00}-{route.Replace(':', '-')}";
                number++;
                try { RealCaptureRoute(window, workspace, route, name, output); }
                catch (Exception error) { Console.WriteLine($"ERRO {name}: {error.Message}"); }
            }

            if (!profile.IsMunicipal)
            {
                vm.Navigate("home");
                RealSettle(window, workspace);
                RealCaptureProfileMenu(window, Path.Combine(output, $"{prefix}-{number:00}-menu-perfil.png"));
                var switchPath = Path.Combine(output, $"{prefix}-{number + 1:00}-trocar-perfil.png");
                RealModalCapture(window, switchPath, () => window.OpenSwitchProfile());
            }

            // Segundo passe, em tema escuro, só com as telas principais (uma por item do menu).
            vm.SetAppearance(Appearance.Dark);
            var main = vm.Navigation.Where(item => item.Available).Select(item => item.Route).Distinct().ToList();
            var darkNumber = 1;
            foreach (var route in main)
            {
                if (route == "network") continue;
                try
                {
                    vm.Navigate(route);
                    RealSettle(window, workspace);
                    RealSave(window, Path.Combine(output, $"escuro-{prefix}-{darkNumber:00}-{route.Replace(':', '-')}.png"));
                }
                catch (Exception error) { Console.WriteLine($"ERRO escuro {route}: {error.Message}"); }
                darkNumber++;
            }
        }
        finally { window.Close(); }
    }

    /// <summary>Conta com várias UBS e nenhuma ativa: escolhe a UBS de NORA_CAPTURA_UBS (trecho do nome) ou a primeira. Só lê dados.</summary>
    private static void ChooseRealUnit(WorkspaceViewModel workspace)
    {
        if (!workspace.NeedsObsUnitChoice) return;
        var wanted = Environment.GetEnvironmentVariable("NORA_CAPTURA_UBS");
        var unit = (string.IsNullOrWhiteSpace(wanted) ? null : workspace.ObsUnits.FirstOrDefault(item => item.Name.Contains(wanted, StringComparison.CurrentCultureIgnoreCase))) ?? workspace.ObsUnits.FirstOrDefault()
            ?? throw new InvalidOperationException("A conta do OBS não tem UBS autorizada.");
        workspace.CandidateObsUnit = unit;
        var task = workspace.ConfirmObsUnitAsync();
        RealPumpUntil(() => task.IsCompleted, 60, "escolha da UBS");
        task.GetAwaiter().GetResult();
        if (!workspace.HasActiveObsUnit) throw new InvalidOperationException("A UBS não ficou ativa.");
        Console.WriteLine($"UBS ativa: {workspace.ActiveObsUnitLabel}");
    }

    private static void RealCaptureRoute(ShellWindow window, WorkspaceViewModel workspace, string route, string name, string output)
    {
        var vm = window.ViewModel;
        vm.Navigate(route);
        var content = RealSettle(window, workspace);
        RealSave(window, Path.Combine(output, name + ".png"));

        // Telas longas: a parte de baixo também.
        if (window.FindName("PageScroll") is ScrollViewer scroll && scroll.ScrollableHeight > 40)
        {
            scroll.ScrollToVerticalOffset(scroll.ScrollableHeight);
            RealSettle(window, workspace);
            RealSave(window, Path.Combine(output, name + "-rolagem.png"));
            scroll.ScrollToTop();
            RealSettle(window, workspace);
        }

        // Modo gráficos.
        var toggle = Descendants<ViewModeToggle>(content).FirstOrDefault(item => item.IsVisible && item.IsEnabled);
        if (toggle is not null)
        {
            var before = toggle.Mode;
            InvokeButton(toggle);
            RealSettle(window, workspace);
            RealSave(window, Path.Combine(output, name + (toggle.Mode == ViewMode.Charts ? "-graficos" : "-cards") + ".png"));
            if (toggle.Mode != before) { InvokeButton(toggle); RealSettle(window, workspace); }
            content = (FrameworkElement)window.Content;
        }

        // Seleção com balão e ficha.
        var openStyles = new[] { "OpenList", "OpenListLow" }.Select(key => Application.Current.TryFindResource(key) as Style).Where(style => style is not null).ToList();
        var list = Descendants<ListBox>(content).FirstOrDefault(box => box.IsVisible && box.Items.Count > 0 && openStyles.Any(style => ReferenceEquals(box.Style, style) || ReferenceEquals(box.Style?.BasedOn, style)));
        if (list is not null)
        {
            var item = list.Items[0];
            list.SelectedItem = item;
            RealPump(500);
            RealSettle(window, workspace);
            RealSave(window, Path.Combine(output, name + "-selecao.png"));
            RealModalCapture(window, Path.Combine(output, name + "-ficha.png"), () => _ = window.OpenItemDetailsAsync(item));
            ListingBehavior.ClearSelection(list);
            RealPump(200);
        }

        // Pop-up de cadastro ("Novo ...").
        var newStyle = Application.Current.TryFindResource("NewItemButton") as Style;
        var add = newStyle is null ? null : Descendants<Button>(content).FirstOrDefault(button => button.IsVisible && button.IsEnabled && ReferenceEquals(button.Style, newStyle));
        if (add is not null) RealModalCapture(window, Path.Combine(output, name + "-novo.png"), () => InvokeButton(add));
    }

    private static void RealCaptureProfileMenu(ShellWindow window, string path)
    {
        var content = RealSettle(window, window.ViewModel.Workspace);
        var button = Descendants<Button>(content).Single(item => AutomationProperties.GetName(item) == "Menu da conta NORA");
        InvokeButton(button);
        var menu = button.ContextMenu ?? throw new InvalidOperationException("Menu da conta ausente.");
        RealPump(400);
        try
        {
            var shot = RenderReal(content);
            var menuShot = RenderReal(menu);
            var origin = button.TransformToAncestor(content).Transform(new Point(0, 0));
            var x = Math.Clamp(origin.X, 0, Math.Max(0, RealWidth - menu.ActualWidth));
            var y = Math.Clamp(origin.Y - menu.ActualHeight, 0, Math.Max(0, RealHeight - menu.ActualHeight));
            Compose(shot, [(menuShot, new Rect(x, y, menu.ActualWidth, menu.ActualHeight))], path);
        }
        finally { menu.IsOpen = false; RealPump(150); }
    }

    /// <summary>
    /// Pop-ups são janelas próprias e modais. Uma verificação periódica espera o pop-up aparecer e os dados carregarem, renderiza o pop-up, compõe com a janela principal
    /// (já escurecida pelo próprio programa) e fecha o pop-up sem salvar; <paramref name="open"/> pode bloquear até o fechamento.
    /// </summary>
    private static void RealModalCapture(ShellWindow window, string path, Action open)
    {
        var finished = false; string? error = null;
        var clock = Stopwatch.StartNew();
        TimeSpan? appeared = null; var stable = 0;
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(250) };
        timer.Tick += (_, _) =>
        {
            Window? dialog = null;
            try
            {
                dialog = Application.Current.Windows.OfType<Window>().FirstOrDefault(item => !ReferenceEquals(item, window) && item.IsVisible && item.IsLoaded && item.Content is FrameworkElement);
                if (dialog is null)
                {
                    if (clock.Elapsed > TimeSpan.FromSeconds(6)) { timer.Stop(); finished = true; error = "o pop-up não abriu (o item pode não ter ficha, ou o botão não abre pop-up)"; }
                    return;
                }
                var root = (FrameworkElement)dialog.Content;
                appeared ??= clock.Elapsed;
                if (clock.Elapsed - appeared < TimeSpan.FromMilliseconds(900)) return;
                var loading = RealIsLoading(root, null);
                stable = loading && clock.Elapsed - appeared < RealSettleLimit ? 0 : stable + 1;
                if (stable < 2) return;
                timer.Stop();
                // Animações de entrada do pop-up (opacidade e zoom) terminam no valor final antes da imagem, mesmo com a janela fora da tela.
                if (root.RenderTransform is ScaleTransform zoom)
                {
                    zoom.BeginAnimation(ScaleTransform.ScaleXProperty, null); zoom.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                    zoom.ScaleX = 1; zoom.ScaleY = 1;
                }
                root.BeginAnimation(UIElement.OpacityProperty, null); root.Opacity = 1;
                dialog.UpdateLayout();
                var main = (FrameworkElement)window.Content;
                var shot = RenderReal(main);
                var width = dialog.ActualWidth; var height = dialog.ActualHeight;
                var dialogShot = RenderReal(root, width, height);
                // Centralizado na janela principal (a janela fica fora da tela e o Windows reposiciona o pop-up, então a posição real não serve).
                var x = Math.Max(0, (RealWidth - width) / 2);
                var y = Math.Max(0, (RealHeight - height) / 2);
                Compose(shot, [(dialogShot, new Rect(x, y, width, height))], path);
                dialog.Close();
                finished = true;
            }
            catch (Exception failure)
            {
                timer.Stop(); error = failure.Message; finished = true;
                try { dialog?.Close(); } catch { }
            }
        };
        timer.Start();
        try { open(); }
        finally { RealPumpUntil(() => finished, 90, "pop-up"); timer.Stop(); }
        if (error is not null) Console.WriteLine($"AVISO {Path.GetFileName(path)}: {error}.");
        RealPump(250);
    }

    private static bool RealIsLoading(FrameworkElement root, WorkspaceViewModel? workspace) =>
        workspace?.IsBusy == true ||
        Descendants<ProgressBar>(root).Any(bar => bar.IsVisible && bar.IsIndeterminate && bar.ActualWidth > 0) ||
        Descendants<TextBlock>(root).Any(text => text.IsVisible && text.Text.StartsWith("Carregando", StringComparison.OrdinalIgnoreCase));

    /// <summary>Atualiza o layout e espera os dados carregarem (sem barra de progresso nem "Carregando") por algumas leituras seguidas, com tempo máximo.</summary>
    private static FrameworkElement RealSettle(ShellWindow window, WorkspaceViewModel? workspace)
    {
        var clock = Stopwatch.StartNew();
        RealPump(450);
        var stable = 0;
        FrameworkElement content = Layout(window, RealWidth, RealHeight);
        while (stable < 3 && clock.Elapsed < RealSettleLimit)
        {
            content = Layout(window, RealWidth, RealHeight);
            stable = RealIsLoading(content, workspace) ? 0 : stable + 1;
            RealPump(200);
        }
        if (stable < 3) Console.WriteLine("AVISO a tela ainda indicava carregamento ao fim do tempo máximo.");
        return Layout(window, RealWidth, RealHeight);
    }

    private static void RealSave(ShellWindow window, string path)
    {
        var content = Layout(window, RealWidth, RealHeight);
        Require(Descendants<PasswordBox>(content).All(input => input.Password.Length == 0), "Uma senha está preenchida; captura recusada.");
        Compose(RenderReal(content), [], path);
    }

    /// <summary>Renderiza o elemento a 144 DPI. Com <paramref name="width"/> e <paramref name="height"/> a imagem é maior que o elemento (inclui a margem e a sombra do pop-up).</summary>
    private static RenderTargetBitmap RenderReal(FrameworkElement element, double? width = null, double? height = null)
    {
        var scale = RealDpi / 96;
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling((width ?? element.ActualWidth) * scale), (int)Math.Ceiling((height ?? element.ActualHeight) * scale), RealDpi, RealDpi, PixelFormats.Pbgra32);
        bitmap.Render(element);
        return bitmap;
    }

    private static void Compose(BitmapSource baseImage, IEnumerable<(BitmapSource Image, Rect Where)> layers, string path)
    {
        var scale = RealDpi / 96;
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.DrawImage(baseImage, new Rect(0, 0, baseImage.PixelWidth / scale, baseImage.PixelHeight / scale));
            foreach (var layer in layers) context.DrawImage(layer.Image, layer.Where);
        }
        var final = new RenderTargetBitmap(baseImage.PixelWidth, baseImage.PixelHeight, RealDpi, RealDpi, PixelFormats.Pbgra32);
        final.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(final));
        using var stream = File.Create(path);
        encoder.Save(stream);
        Console.WriteLine($"CAPTURA {path}");
    }

    private static void RealPump(int milliseconds)
    {
        var end = DateTime.UtcNow.AddMilliseconds(milliseconds);
        do
        {
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.SystemIdle, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
            Thread.Sleep(10);
        } while (DateTime.UtcNow < end);
    }

    private static void RealPumpUntil(Func<bool> complete, int seconds, string what)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (!complete())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException($"Tempo esgotado esperando: {what}.");
            RealPump(20);
        }
    }
}
