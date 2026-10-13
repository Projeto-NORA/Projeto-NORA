using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Nora.Desktop.Models;
using Nora.Desktop.Services;
using Nora.Desktop.Views;

namespace Nora.Desktop.Checks;

internal static partial class Program
{
    private static void RunInterfaceChecks(Application application, string storageRoot, string[] args)
    {
        var previewArgument = Array.IndexOf(args, "--preview-dir");
        var previewRoot = previewArgument >= 0 && previewArgument + 1 < args.Length ? args[previewArgument + 1] : Path.Combine(AppContext.BaseDirectory, "previews");
        Directory.CreateDirectory(previewRoot);
        using var errors = new BindingErrors();
        PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Error;
        PresentationTraceSources.DataBindingSource.Listeners.Add(errors);
        try
        {
            foreach (var appearance in new[] { Appearance.Light, Appearance.Dark })
            Check($"Botão principal {appearance}: texto efetivo renderizado mantém contraste", () =>
            {
                var window = new ShellWindow(DesktopProfile.Operational, Path.Combine(storageRoot, "primary-text", appearance.ToString()));
                try
                {
                    window.ViewModel.ConfirmClose = false;
                    window.ViewModel.Animations = false;
                    window.ViewModel.SetAppearance(appearance);
                    PrepareWindow(window);
                    var content = Layout(window, 1280, 820);
                    var button = FindActionButton(content, "Entrar");
                    var label = Descendants<TextBlock>(button).Single(text => text.Text == "Entrar");
                    var frame = (Border)button.Template.FindName("Frame", button);
                    var foreground = ((SolidColorBrush)label.Foreground).Color;
                    var background = ((SolidColorBrush)frame.Background).Color;
                    var contrast = Contrast(foreground, background);
                    Console.WriteLine($"PrimaryButton {appearance}: texto={foreground}, fundo={background}, contraste={contrast:F2}:1.");
                    Require(contrast >= 4.5, $"Texto efetivo do botão tem contraste {contrast:F2}:1; esperado >=4,5:1.");
                    Require(foreground == ((SolidColorBrush)button.Foreground).Color, "Rótulo não respeita Foreground efetivo do botão.");
                }
                finally { window.ViewModel.ConfirmClose = false; window.Close(); }
            });
            foreach (var profile in new[] { DesktopProfile.Operational, DesktopProfile.Municipal })
            {
                Check($"{profile.Name}: render nativo e seleção acessível nos três temas", () =>
                {
                    var window = new ShellWindow(profile, Path.Combine(storageRoot, "interface"));
                    try
                    {
                        window.ViewModel.ConfirmClose = false;
                        window.ViewModel.Animations = false;
                        window.ViewModel.Navigate("settings");
                        PrepareWindow(window);
                        foreach (var mode in new[] { Appearance.Light, Appearance.Dark, Appearance.System })
                        {
                            window.ViewModel.SetAppearance(mode);
                            var content = Layout(window, 1280, 820);
                            var appearanceButtons = Descendants<Button>(content).Where(button => button.CommandParameter?.ToString() is "Light" or "Dark" or "System").ToArray();
                            Require(appearanceButtons.Length == 3, "Opções de aparência ausentes.");
                            var selected = appearanceButtons.Single(button => button.CommandParameter?.ToString() == mode.ToString());
                            Require(((SolidColorBrush)selected.BorderBrush).Color == ((SolidColorBrush)application.FindResource("BrandBrush")).Color, "Card selecionado não tem borda da marca.");
                            Require(AutomationProperties.GetHelpText(selected) == "Opção de aparência selecionada", "Seleção não anunciada pela acessibilidade.");
                            var selectedLabel = Descendants<TextBlock>(selected).Single(text => text.Text == "Selecionada ✓");
                            Require(selectedLabel.Visibility == Visibility.Visible && selectedLabel.ActualHeight > 0, "Texto da seleção está oculto.");
                            Require(appearanceButtons.Where(button => button != selected).All(button => AutomationProperties.GetHelpText(button) == "Opção de aparência não selecionada"), "Mais de uma aparência anunciada como selecionada.");
                            SavePreview(content, Path.Combine(previewRoot, $"{profile.Id}-settings-{mode.ToString().ToLowerInvariant()}.png"));
                        }
                        window.ViewModel.Navigate("home");
                        window.ViewModel.SetAppearance(profile.IsMunicipal ? Appearance.Dark : Appearance.Light);
                        SavePreview(Layout(window, 1280, 820), Path.Combine(previewRoot, $"{profile.Id}-home.png"));
                    }
                    finally { window.ViewModel.ConfirmClose = false; window.Close(); }
                });
            }
            Check("OBS: tabela permite seleção e apresenta contexto", () =>
            {
                var window = new ShellWindow(DesktopProfile.Operational, Path.Combine(storageRoot, "rows"));
                try
                {
                    window.ViewModel.ConfirmClose = false;
                    window.ViewModel.Animations = false;
                    var row = new OperationalRow("TEST", "Linha sintética exclusiva do teste", "Teste");
                    window.ViewModel.Rows.Add(row);
                    PrepareWindow(window);
                    var content = Layout(window, 980, 640);
                    var table = Descendants<DataGrid>(content).Single(grid => AutomationProperties.GetName(grid) == "Atendimentos da unidade");
                    table.SelectedItem = row;
                    Layout(window, 980, 640);
                    Require(window.ViewModel.HasSelection && table.ActualHeight > table.ColumnHeaderHeight, "Linha não pode ser vista/selecionada.");
                    var context = Descendants<Button>(content).Single(button => button.Content?.ToString() == "Ações do atendimento indisponíveis");
                    Require(FindAncestor<Border>(context)?.Visibility == Visibility.Visible, "Barra contextual permanece oculta.");
                    CaptureVisualThemes(window, args, "atendimentos-contexto-base", "fim", 1, "Linha sintética do scaffold visual de atendimentos; sem integração clínica/API");
                    window.ViewModel.SelectedRow = null;
                    Layout(window, 980, 640);
                    Require(FindAncestor<Border>(context)?.Visibility == Visibility.Collapsed, "Barra permanece sem seleção.");
                }
                finally { window.ViewModel.ConfirmClose = false; window.Close(); }
            });
            Check("Configurações têm rolagem utilizável no tamanho mínimo", () =>
            {
                var window = new ShellWindow(DesktopProfile.Operational, Path.Combine(storageRoot, "small"));
                try
                {
                    window.ViewModel.ConfirmClose = false;
                    window.ViewModel.Animations = false;
                    window.ViewModel.Navigate("settings");
                    PrepareWindow(window);
                    var content = Layout(window, 980, 640);
                    var scrolling = Descendants<ScrollViewer>(content).First();
                    Require(scrolling.ScrollableHeight > 0, "Conteúdo excedente não possui rolagem.");
                    scrolling.ScrollToEnd();
                    Layout(window, 980, 640);
                    Require(scrolling.VerticalOffset > 0, "Rolagem não alcança preferências finais.");
                    SavePreview(content, Path.Combine(previewRoot, "obs-settings-small-bottom.png"));
                }
                finally { window.ViewModel.ConfirmClose = false; window.Close(); }
            });
            Check("Botões de aparência e restauração aplicam comandos e persistem", () =>
            {
                var isolatedStorage = Path.Combine(storageRoot, "commands");
                var window = new ShellWindow(DesktopProfile.Operational, isolatedStorage);
                try
                {
                    window.ViewModel.ConfirmClose = false;
                    window.ViewModel.Animations = false;
                    window.ViewModel.Navigate("settings");
                    PrepareWindow(window);
                    var content = Layout(window, 1280, 820);
                    var dark = Descendants<Button>(content).Single(button => button.CommandParameter?.ToString() == "Dark");
                    InvokeButton(dark);
                    Layout(window, 1280, 820);
                    Require(window.ViewModel.DarkSelected, "Ação do card não atualizou aparência.");
                    Require(new PreferencesService("obs", isolatedStorage).Load().Appearance == Appearance.Dark, "Ação do card não persistiu aparência.");
                    var restore = Descendants<Button>(content).Single(button => button.Content?.ToString() == "Restaurar padrões");
                    InvokeButton(restore);
                    Layout(window, 1280, 820);
                    var restored = new PreferencesService("obs", isolatedStorage).Load();
                    Require(window.ViewModel.SystemSelected && window.ViewModel.IsSettings && restored.Appearance == Appearance.System && restored.LastPage == "home", "Ação de restauração deixou interface ou persistência inconsistente.");
                }
                finally { window.ViewModel.ConfirmClose = false; window.Close(); }
            });
            Check("Os dois desktops abrem cada tela no topo com e sem animação", () =>
            {
                foreach (var profile in new[] { DesktopProfile.Operational, DesktopProfile.Municipal })
                foreach (var animations in new[] { false, true })
                {
                    var window = new ShellWindow(profile, Path.Combine(storageRoot, "page-scroll", profile.Id, animations.ToString()));
                    try
                    {
                        window.ViewModel.ConfirmClose = false;
                        window.ViewModel.Animations = animations;
                        PrepareWindow(window);
                        var content = Layout(window, 980, 640);
                        var scrolling = Descendants<ScrollViewer>(content).First();
                        scrolling.ScrollToEnd();
                        Layout(window, 980, 640);
                        Require(scrolling.VerticalOffset > 0, "Cenário não rolou a Home.");
                        var open = Descendants<Button>(content).Single(button => button.Content?.ToString() == "Personalizar minha experiência");
                        InvokeButton(open);
                        Layout(window, 980, 640);
                        Require(window.ViewModel.IsSettings && scrolling.VerticalOffset == 0, "Configurações herdou a rolagem da Home e escondeu seu início.");
                        scrolling.ScrollToEnd();
                        Layout(window, 980, 640);
                        Require(scrolling.VerticalOffset > 0, "Cenário não rolou Configurações.");
                        var home = Descendants<Button>(content).Single(button => button.CommandParameter?.ToString() == "home");
                        InvokeButton(home);
                        Layout(window, 980, 640);
                        Require(window.ViewModel.IsHome && scrolling.VerticalOffset == 0, "Home herdou a rolagem de Configurações.");
                    }
                    finally { window.ViewModel.ConfirmClose = false; window.Close(); }
                }
            });
            Check("Confirmação de fechamento permite cancelar e fechar a janela", () =>
            {
                var window = new ShellWindow(DesktopProfile.Operational, Path.Combine(storageRoot, "closing"));
                var closed = false;
                string? layoutError = null;
                window.Closed += (_, _) => closed = true;
                try
                {
                    PrepareWindow(window);
                    application.Dispatcher.BeginInvoke(() =>
                    {
                        var dialog = window.OwnedWindows.Cast<Window>().Single();
                        dialog.UpdateLayout();
                        var panel = (StackPanel)dialog.Content;
                        var actions = (FrameworkElement)panel.Children[^1];
                        var bottom = actions.TranslatePoint(new Point(0, actions.ActualHeight), panel).Y;
                        if (bottom > panel.ActualHeight + 1)
                            layoutError = $"Diálogo corta os botões: limite {bottom:F0}, disponível {panel.ActualHeight:F0}.";
                        dialog.DialogResult = false;
                    }, DispatcherPriority.ApplicationIdle);
                    window.Close();
                    Require(!closed && window.IsVisible, "Cancelar fechou a janela principal.");
                    Require(layoutError == null, layoutError ?? "");
                    application.Dispatcher.BeginInvoke(() =>
                    {
                        var dialog = window.OwnedWindows.Cast<Window>().Single();
                        var close = Descendants<Button>(dialog).Single(button => button.Content?.ToString() == "Fechar");
                        InvokeButton(close);
                    }, DispatcherPriority.ApplicationIdle);
                    window.Close();
                    Require(closed, "Confirmar não fechou a janela principal.");
                }
                finally { if (!closed) { window.ViewModel.ConfirmClose = false; window.Close(); } }
            });
            Check("Tela cheia remove borda e restaura tamanho e estado", () =>
            {
                var window = new Window { Width = 1080, Height = 700, Left = 100, Top = 100, Opacity = 0, ShowActivated = false, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual };
                window.Show();
                try
                {
                    var original = new Rect(window.Left, window.Top, window.Width, window.Height);
                    var fullScreen = new FullScreenService(window);
                    fullScreen.Apply(true);
                    Require(window.WindowStyle == WindowStyle.None && window.ResizeMode == ResizeMode.NoResize, "Tela cheia manteve bordas.");
                    fullScreen.Apply(false);
                    Require(window.WindowStyle == WindowStyle.SingleBorderWindow && window.WindowState == WindowState.Normal && new Rect(window.Left, window.Top, window.Width, window.Height) == original, "Tela cheia não restaurou a janela.");
                    window.WindowState = WindowState.Maximized;
                    fullScreen.Apply(true);
                    fullScreen.Apply(false);
                    Require(window.WindowState == WindowState.Maximized, "Estado maximizado não restaurado.");
                }
                finally { window.Close(); }
            });
            if (args.Contains("--visual-gallery")) RunVisualGalleryChecks(storageRoot, args);
            Check("Telas nativas não produzem erros de binding", () => Require(errors.Messages.Count == 0, string.Join(Environment.NewLine, errors.Messages)));
        }
        finally { PresentationTraceSources.DataBindingSource.Listeners.Remove(errors); }
        Console.WriteLine($"Prévias WPF: {Path.GetFullPath(previewRoot)}");
    }

    private static void InvokeButton(Button button)
    {
        var peer = new ButtonAutomationPeer(button);
        ((IInvokeProvider)peer.GetPattern(PatternInterface.Invoke)).Invoke();
        button.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    }

    private static FrameworkElement Layout(ShellWindow window, double width, double height)
    {
        var content = (FrameworkElement)window.Content;
        // Render the same backdrop provided by Window when its content is shown.
        if (content is Panel panel) panel.SetResourceReference(Panel.BackgroundProperty, "BackgroundBrush");
        content.Width = width;
        content.Height = height;
        content.InvalidateMeasure();
        content.Measure(new Size(width, height));
        content.Arrange(new Rect(0, 0, width, height));
        content.UpdateLayout();
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        content.InvalidateMeasure();
        content.Measure(new Size(width, height));
        content.Arrange(new Rect(0, 0, width, height));
        content.UpdateLayout();
        return content;
    }

    private static void PrepareWindow(ShellWindow window)
    {
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = -10000;
        window.Top = -10000;
        window.ShowInTaskbar = false;
        window.ShowActivated = false;
        window.Show();
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject node) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
        {
            var child = VisualTreeHelper.GetChild(node, i);
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }

    private static T? FindAncestor<T>(DependencyObject node) where T : DependencyObject
    {
        var parent = VisualTreeHelper.GetParent(node);
        return parent is T match ? match : parent == null ? null : FindAncestor<T>(parent);
    }

    private static void SavePreview(FrameworkElement content, string path)
    {
        var bitmap = new RenderTargetBitmap((int)content.ActualWidth, (int)content.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(content);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    private sealed class BindingErrors : TraceListener
    {
        public List<string> Messages { get; } = [];
        public override void Write(string? message) { if (!string.IsNullOrWhiteSpace(message)) Messages.Add(message); }
        public override void WriteLine(string? message) => Write(message);
    }
}
