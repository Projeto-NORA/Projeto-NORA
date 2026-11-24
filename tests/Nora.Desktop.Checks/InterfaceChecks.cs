using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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
                    var backgrounds = frame.Background switch
                    {
                        SolidColorBrush solid => new[] { solid.Color },
                        LinearGradientBrush gradient => gradient.GradientStops.Select(stop => stop.Color).ToArray(),
                        _ => throw new InvalidOperationException("Fundo do botão não possui cor verificável.")
                    };
                    var contrasts = backgrounds.Select(background => Contrast(foreground, background)).ToArray();
                    Console.WriteLine($"PrimaryButton {appearance}: texto={foreground}, contraste mínimo={contrasts.Min():F2}:1.");
                    Require(contrasts.All(contrast => contrast >= 4.5), $"Texto efetivo do botão tem contraste mínimo {contrasts.Min():F2}:1; esperado >=4,5:1.");
                    Require(foreground == ((SolidColorBrush)button.Foreground).Color, "Rótulo não respeita Foreground efetivo do botão.");
                }
                finally { window.ViewModel.ConfirmClose = false; window.Close(); }
            });
            Check("Municipal: nitidez do texto na área principal nos dois temas", () =>
            {
                using var fixture = new ApiFixture { UseWorkspaceIdentity = true };
                var window = new ShellWindow(DesktopProfile.Municipal, Path.Combine(storageRoot, "municipal-text"), new NoraApiClient(fixture));
                try
                {
                    window.ViewModel.ConfirmClose = false;
                    window.ViewModel.Animations = false;
                    PrepareWindow(window);
                    LoginFromControls(window, "https://fixture.invalid", "fixture@example.invalid", "isolated-test-password");
                    Require(window.ViewModel.Workspace.IsAuthenticated, "Login da fixture falhou.");
                    window.ViewModel.Navigate("users");
                    foreach (var appearance in new[] { Appearance.Light, Appearance.Dark })
                    {
                        window.ViewModel.SetAppearance(appearance);
                        var content = Layout(window, 1280, 860);
                        var labels = Descendants<TextBlock>(content).Where(text => text.IsVisible &&
                            text.Text is "Vínculos de contas OBS" or "UBS autorizadas" or "Histórico de ações").ToArray();
                        Require(labels.Length == 3, "Faltam textos da tela Usuários e histórico.");
                        foreach (var label in labels)
                        {
                            var card = (Border)VisualTreeHelper.GetParent(VisualTreeHelper.GetParent(label));
                            var foreground = ((SolidColorBrush)label.Foreground).Color;
                            var contrast = ((LinearGradientBrush)card.Background).GradientStops
                                .Min(stop => Contrast(foreground, stop.Color));
                            Console.WriteLine($"{appearance}: {label.Text}, contraste {contrast:F2}:1.");
                            Require(contrast >= 4.5, $"Contraste insuficiente em {label.Text}.");
                            for (DependencyObject? parent = label; parent != null; parent = VisualTreeHelper.GetParent(parent))
                            {
                                if (parent is UIElement { Effect: { } effect })
                                    throw new InvalidOperationException($"{label.Text} está dentro de {effect.GetType().Name}.");
                            }
                        }
                    }
                }
                finally { window.Close(); }
            });
            Check("Municipal: ícones de Unidades, Especialidades e Relatórios e metas seguem navegação e tema", () =>
            {
                using var fixture = new ApiFixture { UseWorkspaceIdentity = true };
                var window = new ShellWindow(DesktopProfile.Municipal, Path.Combine(storageRoot, "municipal-icons"), new NoraApiClient(fixture));
                try
                {
                    window.ViewModel.ConfirmClose = false;
                    window.ViewModel.Animations = false;
                    PrepareWindow(window);
                    LoginFromControls(window, "https://fixture.invalid", "fixture@example.invalid", "isolated-test-password");
                    Require(window.ViewModel.Workspace.IsAuthenticated, "Login da fixture falhou.");
                    var items = new[]
                    {
                        (Id: "units", Title: "Unidades", Resource: "HealthUnitIcon"),
                        (Id: "specialties", Title: "Especialidades", Resource: "SpecialtiesIcon"),
                        (Id: "reports", Title: "Relatórios e metas", Resource: "ReportsIcon")
                    };
                    foreach (var appearance in new[] { Appearance.Light, Appearance.Dark })
                    {
                        window.ViewModel.SetAppearance(appearance);
                        Layout(window, 1280, 860);
                        var sidebar = (Border)window.FindName("Sidebar");
                        foreach (var item in items)
                        {
                            var button = Descendants<Button>(sidebar).Single(candidate => Equals(candidate.CommandParameter, item.Id));
                            Require(AutomationProperties.GetName(button) == item.Title, $"Nome acessível incorreto em {item.Id}.");
                            InvokeButton(button);
                            Layout(window, 1280, 860);
                            Require(window.ViewModel.Page == item.Id, $"Destino incorreto em {item.Title}.");
                            var icon = Descendants<System.Windows.Shapes.Path>(button).Single();
                            Require(icon.IsVisible && Equals(icon.Data, window.FindResource(item.Resource)), $"Desenho incorreto em {item.Title}.");
                            var stroke = ((SolidColorBrush)icon.Stroke).Color;
                            var background = ((SolidColorBrush)button.Background).Color;
                            Require(Contrast(stroke, background) >= 3, $"Contraste insuficiente no ícone de {item.Title}.");
                        }
                    }
                }
                finally { window.Close(); }
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
                            var content = Layout(window, 1280, 740);
                            var appearanceButtons = Descendants<RadioButton>(content).Where(button => button.CommandParameter?.ToString() is "Light" or "Dark" or "System").ToArray();
                            Require(appearanceButtons.Length == 3, "Opções de aparência ausentes.");
                            var selected = appearanceButtons.Single(button => button.CommandParameter?.ToString() == mode.ToString());
                            var tile = (Border)selected.Template.FindName("Tile", selected);
                            Require(((SolidColorBrush)tile.BorderBrush).Color == ((SolidColorBrush)application.FindResource("AccentTextBrush")).Color, "Card selecionado não tem borda de destaque.");
                            var selectedPeer = new RadioButtonAutomationPeer(selected);
                            Require(((ISelectionItemProvider)selectedPeer.GetPattern(PatternInterface.SelectionItem)).IsSelected, "A acessibilidade não reconhece a aparência selecionada.");
                            Require(appearanceButtons.Count(button => button.IsChecked == true) == 1, "O tema selecionado deve ser único.");
                            SavePreview(content, Path.Combine(previewRoot, $"{profile.Id}-settings-{mode.ToString().ToLowerInvariant()}.png"));
                        }
                        window.ViewModel.Navigate("home");
                        window.ViewModel.SetAppearance(profile.IsMunicipal ? Appearance.Dark : Appearance.Light);
                        SavePreview(Layout(window, 1280, 740), Path.Combine(previewRoot, $"{profile.Id}-home.png"));
                    }
                    finally { window.ViewModel.ConfirmClose = false; window.Close(); }
                });
            }
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
                    var scrolling = (ScrollViewer)window.FindName("PageScroll"); // a rolagem da página (a primeira da árvore era a do menu, que já não estoura com a lista maior)
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
                    var content = Layout(window, 1280, 740);
                    var dark = Descendants<RadioButton>(content).Single(button => button.CommandParameter?.ToString() == "Dark");
                    ((ISelectionItemProvider)new RadioButtonAutomationPeer(dark).GetPattern(PatternInterface.SelectionItem)).Select();
                    Layout(window, 1280, 740);
                    Require(window.ViewModel.DarkSelected, "Ação do card não atualizou aparência.");
                    Require(new PreferencesService("obs", isolatedStorage).Load().Appearance == Appearance.Dark, "Ação do card não persistiu aparência.");
                    var restore = Descendants<Button>(content).Single(button => button.Content?.ToString() == "Restaurar padrões");
                    InvokeButton(restore);
                    Layout(window, 1280, 740);
                    InvokeButton(Descendants<Button>(content).Single(button => Equals(button.Content, "Confirmar restauração")));
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
                        var content = Layout(window, 980, 420);
                        var scrolling = (ScrollViewer)window.FindName("PageScroll");
                        scrolling.ScrollToEnd();
                        Layout(window, 980, 420);
                        Require(scrolling.VerticalOffset > 0, "Cenário não rolou a Home.");
                        InvokeAccountAction(window, "Configurações");
                        Layout(window, 980, 420);
                        Require(window.ViewModel.IsSettings && scrolling.VerticalOffset == 0, "Configurações herdou a rolagem da Home e escondeu seu início.");
                        scrolling.ScrollToEnd();
                        Layout(window, 980, 420);
                        Require(scrolling.VerticalOffset > 0, "Cenário não rolou Configurações.");
                        var home = Descendants<Button>(content).Single(button => button.CommandParameter?.ToString() == "home" && AutomationProperties.GetName(button) == window.ViewModel.Navigation.First(item => item.Id == "home").Title);
                        InvokeButton(home);
                        Layout(window, 980, 420);
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
        content.ClearValue(FrameworkElement.WidthProperty);
        content.ClearValue(FrameworkElement.HeightProperty);
        window.UpdateLayout();
        var horizontalFrame = Math.Max(0, window.ActualWidth - content.ActualWidth);
        var verticalFrame = Math.Max(0, window.ActualHeight - content.ActualHeight);
        window.Width = width + horizontalFrame;
        window.Height = height + verticalFrame;
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        window.UpdateLayout();
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
