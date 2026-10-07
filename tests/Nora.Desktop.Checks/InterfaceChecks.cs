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
                    var table = Descendants<DataGrid>(content).Single();
                    table.SelectedItem = row;
                    Layout(window, 980, 640);
                    Require(window.ViewModel.HasSelection && table.ActualHeight > table.ColumnHeaderHeight, "Linha não pode ser vista/selecionada.");
                    var context = Descendants<Button>(content).Single(button => button.Content?.ToString() == "Ações do atendimento indisponíveis");
                    Require(FindAncestor<Border>(context)?.Visibility == Visibility.Visible, "Barra contextual permanece oculta.");
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
            Check("Telas nativas não produzem erros de binding", () => Require(errors.Messages.Count == 0, string.Join(Environment.NewLine, errors.Messages)));
        }
        finally { PresentationTraceSources.DataBindingSource.Listeners.Remove(errors); }
        Console.WriteLine($"Prévias WPF: {Path.GetFullPath(previewRoot)}");
    }

    private static FrameworkElement Layout(ShellWindow window, double width, double height)
    {
        var content = (FrameworkElement)window.Content;
        // Render the same backdrop provided by Window when its content is shown.
        if (content is Panel panel) panel.SetResourceReference(Panel.BackgroundProperty, "BackgroundBrush");
        content.Width = width;
        content.Height = height;
        content.Measure(new Size(width, height));
        content.Arrange(new Rect(0, 0, width, height));
        content.UpdateLayout();
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
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
