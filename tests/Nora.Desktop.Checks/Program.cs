using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using Nora.Desktop.Models;
using Nora.Desktop.Services;
using Nora.Desktop.ViewModels;

namespace Nora.Desktop.Checks;

internal static partial class Program
{
    private static int failures;
    private static int checks;
    private static string? checkFilter;

    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Contains("--secretaria-live-dark") || args.Contains("--secretaria-live-light")) return RunLiveMapPreview(args);
        var filterArgument = Array.IndexOf(args, "--check-filter");
        checkFilter = filterArgument >= 0 && filterArgument + 1 < args.Length ? args[filterArgument + 1] : null;
        var root = Path.Combine(AppContext.BaseDirectory, "check-data", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        // O pop-up "Trocar perfil" que abre sozinho depois do login é modal: as janelas de teste não podem ficar presas nele. Só as checagens do próprio pop-up ligam de novo.
        Nora.Desktop.Views.ShellWindow.AutoOpenProfileChoice = false;
        try
        {
            Check("Primeira abertura usa Sistema e preferências padrão", () =>
            {
                var service = new PreferencesService("obs", root);
                var state = service.Load();
                Require(state.Appearance == Appearance.System && state.ConfirmClose && state.RememberSidebar && state.LastPage == "home", "Padrões inválidos.");
                Require(service.Error == null, "Arquivo inexistente gerou erro.");
            });
            Check("Preferências completas sobrevivem a uma nova instância", () =>
            {
                var state = new DesktopPreferences { Appearance = Appearance.Dark, ConfirmClose = false, Animations = false, ShowSidebarTips = false, SaveNavigation = false, RememberSidebar = false, SidebarExpanded = false, FullScreen = true, LastPage = "settings" };
                Require(new PreferencesService("obs", root).Save(state), "Gravação falhou.");
                var loaded = new PreferencesService("obs", root).Load();
                Require(loaded.Appearance == Appearance.Dark && !loaded.ConfirmClose && !loaded.Animations && !loaded.ShowSidebarTips && !loaded.SaveNavigation && !loaded.RememberSidebar && !loaded.SidebarExpanded && loaded.FullScreen && loaded.LastPage == "settings", "Persistência alterou valores.");
            });
            Check("OBS e Municipal mantêm preferências independentes", () =>
            {
                Require(new PreferencesService("municipal", root).Save(new() { Appearance = Appearance.Light }), "Gravação municipal falhou.");
                Require(new PreferencesService("obs", root).Load().Appearance == Appearance.Dark, "Municipal sobrescreveu OBS.");
                Require(new PreferencesService("municipal", root).Load().Appearance == Appearance.Light, "Tema municipal não persistiu.");
            });
            Check("JSON corrompido preserva arquivo e informa erro", () =>
            {
                var service = new PreferencesService("obs", root);
                File.WriteAllText(service.FilePath, "{invalid}");
                Require(service.Load().Appearance == Appearance.System && service.Error != null, "Falha não recuperada com feedback.");
                Require(File.ReadAllText(service.FilePath) == "{invalid}", "Arquivo original foi sobrescrito.");
            });
            Check("Tema e rota desconhecidos voltam a valores seguros", () =>
            {
                var service = new PreferencesService("obs", root);
                File.WriteAllText(service.FilePath, "{\"Appearance\":999,\"LastPage\":\"unknown\"}");
                var loaded = service.Load();
                Require(loaded.Appearance == Appearance.System && loaded.LastPage == "home", "Configuração inválida não normalizada.");
            });
            Check("JSON nulo recupera padrões com feedback", () =>
            {
                var service = new PreferencesService("obs", root);
                File.WriteAllText(service.FilePath, "null");
                Require(service.Load().Appearance == Appearance.System && service.Error != null, "Configuração nula não recuperada.");
            });
            Check("Falha de gravação informa que mudança é apenas da sessão", () =>
            {
                var blockedRoot = Path.Combine(root, "blocked");
                File.WriteAllText(blockedRoot, "arquivo ocupa caminho da pasta");
                var service = new PreferencesService("obs", blockedRoot);
                Require(!service.Save(new()) && service.Error != null, "Falha de armazenamento não detectada.");
            });
            Check("Identificador de app inválido é rejeitado", () =>
            {
                try { _ = new PreferencesService("../outside", root); }
                catch (ArgumentException) { return; }
                throw new InvalidOperationException("Identificador permitiu caminho arbitrário.");
            });
            var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            // The harness pumps Dispatcher frames instead of Application.Run; install
            // the same UI context so real asynchronous HTTP continuations stay on STA.
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(application.Dispatcher));
            using var themes = new ThemeService(application);
            themes.Apply(Appearance.Light);
            CheckEnterNavigation();
            CheckHealthUnitGeocoding();
            Check("Temas claro e escuro expõem as mesmas chaves", () =>
            {
                var light = new ResourceDictionary { Source = new Uri("/Nora.Desktop;component/Themes/LightTheme.xaml", UriKind.Relative) };
                var dark = new ResourceDictionary { Source = new Uri("/Nora.Desktop;component/Themes/DarkTheme.xaml", UriKind.Relative) };
                Require(light.Keys.Cast<object>().ToHashSet().SetEquals(dark.Keys.Cast<object>()), "Tokens divergem entre temas.");
            });
            Check("Troca de tema atualiza recurso de controle já criado", () =>
            {
                themes.Apply(Appearance.Light);
                var button = new Button();
                var window = new Window { Content = button };
                button.SetResourceReference(Control.BackgroundProperty, "SurfaceBrush");
                var before = ((SolidColorBrush)button.Background).Color;
                themes.Apply(Appearance.Dark);
                application.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                Require(((SolidColorBrush)button.Background).Color != before, "Controle conservou superfície clara.");
                window.Close();
            });
            Check("Texto, descrição e ações mantêm contraste nos dois temas", () =>
            {
                foreach (var mode in new[] { Appearance.Light, Appearance.Dark })
                {
                    themes.Apply(mode);
                    foreach (var pair in new[] { ("TextBrush", "SurfaceBrush"), ("SecondaryBrush", "BackgroundBrush"), ("BrandTextBrush", "BrandBrush") })
                    {
                        var contrast = Contrast(((SolidColorBrush)application.FindResource(pair.Item1)).Color, ((SolidColorBrush)application.FindResource(pair.Item2)).Color);
                        Require(contrast >= 4.5, $"{mode}: {pair.Item1}/{pair.Item2} tem contraste {contrast:F2}.");
                    }
                }
            });
            Check("Sistema resolve a paleta do Windows", () =>
            {
                themes.Apply(Appearance.System);
                var color = ((SolidColorBrush)application.FindResource("BackgroundBrush")).Color;
                themes.Apply(ThemeService.SystemIsDark() ? Appearance.Dark : Appearance.Light);
                Require(((SolidColorBrush)application.FindResource("BackgroundBrush")).Color == color, "Sistema diverge do Windows.");
            });
            Check("Evento de sistema atrasado preserva escolha manual", () =>
            {
                themes.Apply(Appearance.System);
                typeof(ThemeService).GetMethod("OnSystemChanged", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(themes, [null, new UserPreferenceChangedEventArgs(UserPreferenceCategory.Color)]);
                themes.Apply(Appearance.Dark);
                application.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                Require(themes.Mode == Appearance.Dark, "Callback sobrescreveu tema manual.");
            });
            Check("Navegação e estado da sidebar respeitam preferências", () =>
            {
                var storage = new PreferencesService("obs", Path.Combine(root, "navigation"));
                var vm = new ShellViewModel(DesktopProfile.Operational, storage, themes);
                vm.Navigate("settings");
                vm.SidebarExpanded = false;
                var reopened = new ShellViewModel(DesktopProfile.Operational, storage, themes);
                Require(reopened.IsSettings && !reopened.SidebarExpanded, "Estado não foi restaurado.");
                reopened.SaveNavigation = false;
                reopened.RememberSidebar = false;
                var defaults = new ShellViewModel(DesktopProfile.Operational, storage, themes);
                Require(defaults.IsHome && defaults.SidebarExpanded, "Preferências desativadas ainda restauraram estado.");
                defaults.Navigate("unknown");
                Require(defaults.IsHome, "Destino inexistente foi aceito.");
            });
            Check("Navegação em dois níveis: área abre as subabas, Voltar ao menu mantém a página e a seta volta pelo histórico", () =>
            {
                var vm = new ShellViewModel(DesktopProfile.Operational, new PreferencesService("obs", Path.Combine(root, "two-level")), themes);
                vm.Navigate("patients:register");
                Require(vm.ShowingSubmenu && vm.Page == "patients" && vm.SubPage == "register" && vm.SidebarItems.Any(item => item.Id == "family"), "A área Pacientes não abriu as subabas.");
                vm.ShowMainMenu();
                Require(!vm.ShowingSubmenu && vm.Page == "patients" && vm.SidebarItems.Any(item => item.Id == "home"), "Voltar ao menu trocou a página ou não mostrou as áreas.");
                vm.Navigate("queue");
                Require(vm.Page == "slots", "A antiga Fila deveria levar ao painel da Vaga Relâmpago.");
                vm.GoBack();
                Require(vm.Route == "patients:register" && vm.CanGoBack, "A seta de voltar não voltou à página anterior.");
                Require(Nora.Desktop.Views.SubPage.Matches("list,register", "register") && !Nora.Desktop.Views.SubPage.Matches("list", "register") && Nora.Desktop.Views.SubPage.Matches("list", null), "Regra de visibilidade das subabas inválida.");
            });
            Check("Foto de perfil é reduzida para no máximo 512 px e enviada em JPEG", () =>
            {
                var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(1200, 800, 96, 96, PixelFormats.Pbgra32);
                var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                using var png = new MemoryStream(); encoder.Save(png);
                var jpeg = ProfilePhoto.PrepareJpeg(png.ToArray());
                var frame = System.Windows.Media.Imaging.BitmapDecoder.Create(new MemoryStream(jpeg), System.Windows.Media.Imaging.BitmapCreateOptions.None, System.Windows.Media.Imaging.BitmapCacheOption.OnLoad).Frames[0];
                Require(jpeg[0] == 0xFF && jpeg[1] == 0xD8 && frame.PixelWidth == 512 && frame.PixelHeight <= 512 && jpeg.Length <= NoraApiClient.MaxPhotoBytes, "A foto não foi preparada como JPEG de até 512 px.");
                try { ProfilePhoto.PrepareJpeg([1, 2, 3]); throw new InvalidOperationException("Imagem inválida foi aceita."); }
                catch (NoraApiException) { }
            });
            Check("Restaurar padrões atualiza tema, interface e persistência", () =>
            {
                var storage = new PreferencesService("municipal", Path.Combine(root, "restore"));
                var vm = new ShellViewModel(DesktopProfile.Municipal, storage, themes);
                vm.Navigate("settings");
                vm.SetAppearance(Appearance.Dark);
                vm.SidebarExpanded = false;
                vm.ConfirmClose = false;
                vm.RestoreDefaults();
                var loaded = storage.Load();
                Require(vm.SystemSelected && vm.SidebarExpanded && vm.ConfirmClose && themes.Mode == Appearance.System && loaded.Appearance == Appearance.System && loaded.LastPage == "home", "Restauração não atualizou todas as camadas.");
            });
            RunInterfaceChecks(application, root, args);
            RunPageFilterChecks(root);
            RunHeaderChecks(root);
            RunTitleBarChecks(application, root);
            RunListingComponentsChecks(application, themes, root, args);
            RunChartAndDialogChecks(application, themes, root, args);
            RunDesktopAccessChecks(root, args);
            RunProfileChecks(application, themes, root, args);
            RunApiClientChecks(application, root, args);
            RunEndToEndChecks(root, args);
            RunRoundEndToEndChecks(root, args);
            if (args.Contains("--secretaria-gallery")) RunSecretaryGallery(root, args);
            if (args.Contains("--map-online")) RunOnlineMapCheck(root);
            if (args.Contains("--changed-secretaria")) RunChangedSecretaryScreens(root, args);
        }
        finally { Directory.Delete(root, true); }
        Console.WriteLine($"{checks - failures}/{checks} verificações passaram.");
        if (checks == 0) { Console.WriteLine("FAIL Nenhuma verificação corresponde ao filtro informado."); return 1; }
        return failures == 0 ? 0 : 1;
    }

    private static void Check(string name, Action action)
    {
        if (checkFilter != null && !name.Contains(checkFilter, StringComparison.OrdinalIgnoreCase)) return;
        checks++;
        try { action(); Console.WriteLine($"PASS {name}"); }
        catch (Exception error) { failures++; Console.WriteLine($"FAIL {name}: {error.Message}"); if (Environment.GetEnvironmentVariable("NORA_CHECK_TRACE") == "1") Console.WriteLine(error.ToString()); }
    }

    private static void Require(bool valid, string message)
    {
        if (!valid) throw new InvalidOperationException(message);
    }

    private static double Contrast(Color foreground, Color background)
    {
        static double Channel(byte value)
        {
            var channel = value / 255d;
            return channel <= 0.04045 ? channel / 12.92 : Math.Pow((channel + 0.055) / 1.055, 2.4);
        }
        static double Luminance(Color color) => 0.2126 * Channel(color.R) + 0.7152 * Channel(color.G) + 0.0722 * Channel(color.B);
        var a = Luminance(foreground);
        var b = Luminance(background);
        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }
}
