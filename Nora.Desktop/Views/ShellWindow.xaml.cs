using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media.Animation;
using Nora.Desktop.Models;
using Nora.Desktop.Services;
using Nora.Desktop.ViewModels;

namespace Nora.Desktop.Views;

public partial class ShellWindow : Window, IDialogHost
{
    private readonly ThemeService themes;
    private readonly FullScreenService fullScreen;
    public ShellViewModel ViewModel { get; }
    /// <param name="devices">Armazenamento do computador do OBS; o OBS real passa o dele, os testes e a Secretaria não passam nada.</param>
    public ShellWindow(DesktopProfile profile, string? storageRoot = null, NoraApiClient? api = null, IObsDeviceStore? devices = null)
    {
        themes = new ThemeService(Application.Current);
        ViewModel = new ShellViewModel(profile, new PreferencesService(profile.Id, storageRoot), themes, api, devices);
        InitializeComponent();
        DataContext = ViewModel;
        fullScreen = new(this);
        SourceInitialized += (_, _) => ApplyWindowCorners();
        StateChanged += (_, _) => ApplyWindowCorners();
        ViewModel.PropertyChanged += OnPreferenceChanged;
        PreviewKeyDown += OnKeyDown;
        Loaded += async (_, _) =>
        {
            fullScreen.Apply(ViewModel.FullScreen); ApplyWindowCorners();
            // Abertura do OBS: confere o computador e restaura a sessão guardada (sem armazenamento de computador, não faz nada).
            try { await ViewModel.Workspace.OpenAsync(); }
            catch (Exception error) when (error is not OutOfMemoryException) { ViewModel.Workspace.ReportUnexpected(error); }
        };
        Closing += OnClosing;
        Closed += (_, _) => { ViewModel.PropertyChanged -= OnPreferenceChanged; ViewModel.Workspace.Dispose(); themes.Dispose(); };
    }
    private void OnPreferenceChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(ShellViewModel.FullScreen) && IsLoaded) { fullScreen.Apply(ViewModel.FullScreen); ApplyWindowCorners(); }
        if (args.PropertyName == nameof(ShellViewModel.Page))
        {
            PageScroll.ScrollToTop();
            if (ViewModel.Animations)
                PageContainer.BeginAnimation(OpacityProperty, new DoubleAnimation(0.55, 1, TimeSpan.FromMilliseconds(160)));
        }
        if (args.PropertyName == nameof(ShellViewModel.SidebarItems) && ViewModel.Animations)
        {
            // Troca entre o menu principal e as subabas: desliza da direita (entrando na área) ou da esquerda (voltando ao menu).
            var from = ViewModel.ShowingSubmenu ? 28 : -28;
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            SidebarSlide.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, new DoubleAnimation(from, 0, TimeSpan.FromMilliseconds(200)) { EasingFunction = ease });
            SidebarList.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200)));
        }
        if (args.PropertyName == nameof(ShellViewModel.Route) && ViewModel.Animations)
            PageSlide.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, new DoubleAnimation(10, 0, TimeSpan.FromMilliseconds(180)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        if (args.PropertyName == nameof(ShellViewModel.Animations) && !ViewModel.Animations)
            PageContainer.BeginAnimation(OpacityProperty, null);
    }
    private void OnPhotoMenu(object sender, RoutedEventArgs args) { if (sender is FrameworkElement target) AccountAvatar.OpenPhotoMenu(target, ViewModel.Workspace); }
    /// <summary>Mini botão de gráfico dos cartões de indicador: abre o pop-up com Linha e Pizza.</summary>
    private void OnShowChart(object sender, ExecutedRoutedEventArgs args)
    {
        var (title, value) = args.Parameter switch
        {
            MetricCard card => (card.Title, card.Value),
            string text when text.Split('|') is { Length: 2 } parts => (parts[0], parts[1]),
            string text => (text, ""),
            _ => ("Indicador", "")
        };
        var chart = ViewModel.Workspace.BuildIndicatorChart(title, value);
        DialogService.ShowModal(new IndicatorChartWindow(chart), this);
        args.Handled = true;
    }
    private void OnAccountMenu(object sender, RoutedEventArgs args)
    {
        if (sender is not Button { ContextMenu: { } menu } button) return;
        menu.PlacementTarget = button;
        menu.Placement = PlacementMode.Top;
        menu.IsOpen = true;
    }
    /// <summary>Abre a ficha do item. A da Vaga Relâmpago espera a leitura das ofertas, por isso o método é assíncrono (chamado só por eventos da tela).</summary>
    public async void OpenItemDetails(object item)
    {
        var details = item is UnitLightningSlot ? await ViewModel.Workspace.BuildDetailsAsync(item) : ViewModel.Workspace.BuildDetails(item);
        if (details == null) return;
        var health = item is UnitPatientRecord patient ? ViewModel.Workspace.CreateHealthRecords(patient) : null;
        var dialog = new ItemDetailsWindow(details, null, health)
        {
            Owner = this,
            Title = details.Title,
            MaxHeight = Math.Max(320, Math.Min(SystemParameters.WorkArea.Height - 40, ActualHeight - 40))
        };
        CloseDetailsOnSessionChange(dialog);
        dialog.ShowDialog();
    }
    /// <summary>A ficha fecha se a sessão termina ou a UBS ativa muda, para nunca mostrar dados de outra unidade.</summary>
    private void CloseDetailsOnSessionChange(Window dialog)
    {
        var initialUnit = ViewModel.Workspace.ActiveObsUnit?.Id;
        PropertyChangedEventHandler closeOnSignOut = (_, changed) =>
        {
            if ((changed.PropertyName == nameof(WorkspaceViewModel.IsAuthenticated) && !ViewModel.Workspace.IsAuthenticated) ||
                (changed.PropertyName == nameof(WorkspaceViewModel.ActiveObsUnit) && ViewModel.Workspace.ActiveObsUnit?.Id != initialUnit)) dialog.Close();
        };
        ViewModel.Workspace.PropertyChanged += closeOnSignOut;
        dialog.Closed += (_, _) => ViewModel.Workspace.PropertyChanged -= closeOnSignOut;
    }
    private void ApplyWindowCorners()
    {
        // DWM keeps the native resize/title-bar behavior; Windows 11 owns the corner radius.
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)) return;
        var preference = WindowState == WindowState.Normal && !ViewModel.FullScreen ? 3 : 1;
        var result = DwmSetWindowAttribute(new WindowInteropHelper(this).Handle, 33, ref preference, sizeof(int));
        if (result < 0) System.Diagnostics.Trace.TraceWarning("NORA: o Windows não aplicou a preferência de cantos da janela (HRESULT {0:X8}).", result);
    }
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
    private void OnKeyDown(object sender, KeyEventArgs args)
    {
        var modifiers = Keyboard.Modifiers;
        if (args.Key == Key.F11 && modifiers == ModifierKeys.None) ViewModel.FullScreen = !ViewModel.FullScreen;
        else if (args.SystemKey == Key.Left && modifiers == ModifierKeys.Alt) ViewModel.GoBack();
        else if (args.Key == Key.Escape && ViewModel.FullScreen) ViewModel.FullScreen = false;
        else if (args.Key == Key.D1 && modifiers == ModifierKeys.Control) ViewModel.Navigate("home");
        else if (args.Key == Key.OemComma && modifiers == ModifierKeys.Control) ViewModel.Navigate("settings");
        else if (args.Key == Key.B && modifiers == ModifierKeys.Control) ViewModel.SidebarExpanded = !ViewModel.SidebarExpanded;
        else if (args.Key == Key.T && modifiers == (ModifierKeys.Control | ModifierKeys.Shift)) ViewModel.CycleTheme();
        else return;
        args.Handled = true;
    }
    private void OnClosing(object? sender, CancelEventArgs args)
    {
        if (!ViewModel.ConfirmClose) return;
        args.Cancel = !DialogService.Ask(this, "Fechar " + ViewModel.Profile.Name + "?", "Suas preferências ficam salvas para a próxima vez.", "Fechar", "Continuar aqui");
    }
    private int dimmed;
    /// <summary>Escurece o conteúdo enquanto um diálogo da marca está aberto (diálogos aninhados contam).</summary>
    public void SetDimmed(bool value)
    {
        dimmed = Math.Max(0, dimmed + (value ? 1 : -1));
        var show = dimmed > 0;
        if (show == (DialogOverlay.Visibility == Visibility.Visible)) return;
        DialogOverlay.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (show && ViewModel.Animations) DialogOverlay.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(140)));
    }
}