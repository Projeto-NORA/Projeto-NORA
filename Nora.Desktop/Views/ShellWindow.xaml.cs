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
    /// <param name="devices">Armazenamento protegido das sessões e da identificação do computador do OBS.</param>
    public ShellWindow(DesktopProfile profile, string? storageRoot = null, NoraApiClient? api = null, IObsDeviceStore? devices = null, bool compactAccess = false)
    {
        themes = new ThemeService(Application.Current);
        ViewModel = new ShellViewModel(profile, new PreferencesService(profile.Id, storageRoot), themes, api, devices);
        InitializeComponent();
        DataContext = ViewModel;
        if (compactAccess)
        {
            DesktopAccessHost.Content = new DesktopAccessView();
            ViewModel.EnableCompactAccess();
        }
        InitializeScreenSearch();
        fullScreen = new(this);
        InitializeTitleBar();
        StateChanged += (_, _) => ApplyWindowCorners();
        ViewModel.PropertyChanged += OnPreferenceChanged;
        PreviewKeyDown += OnKeyDown;
        Loaded += async (_, _) =>
        {
            fullScreen.Apply(ViewModel.FullScreen); ApplyWindowCorners();
            // Restaura a sessão guardada e confere o computador nas instalações OBS.
            try { await ViewModel.Workspace.OpenAsync(); }
            catch (Exception error) when (error is not OutOfMemoryException) { ViewModel.Workspace.ReportUnexpected(error); }
        };
        Closing += OnClosing;
        Closed += (_, _) => { ViewModel.PropertyChanged -= OnPreferenceChanged; ViewModel.Workspace.Dispose(); themes.Dispose(); };
    }
    private void OnPreferenceChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(ShellViewModel.FullScreen)) { UpdateChrome(); if (IsLoaded) { fullScreen.Apply(ViewModel.FullScreen); ApplyWindowCorners(); } }
        if (args.PropertyName == nameof(ShellViewModel.SidebarExpanded)) ApplySidebarState(animate: true);
        if (args.PropertyName == nameof(ShellViewModel.Page))
        {
            CloseScreenSearch(restoreFocus: false);
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
        var title = args.Parameter switch { MetricCard card => card.Title, string text => text, _ => "Indicador" };
        var chart = ViewModel.Workspace.BuildIndicatorChart(title);
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
    /// <summary>Abre a ficha do item (chamado só por eventos da tela). A fila de fichas da listagem usa <see cref="OpenItemDetailsAsync"/>, que espera a ficha fechar.</summary>
    public async void OpenItemDetails(object item) => await OpenItemDetailsAsync(item);
    /// <summary>Abre a ficha do item e só termina quando ela fecha. A da Vaga Relâmpago espera a leitura das ofertas.</summary>
    public async Task OpenItemDetailsAsync(object item)
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
        DialogService.ShowModal(dialog, this);
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
        if (HandleScreenSearchKey(args)) { args.Handled = true; return; }
        // Voltar e avançar pelo histórico (Alt + seta para a esquerda e para a direita): atalhos fixos, os mesmos botões da barra de título.
        if (args.SystemKey == Key.Left && Keyboard.Modifiers == ModifierKeys.Alt) { ViewModel.GoBack(); args.Handled = true; return; }
        if (args.SystemKey == Key.Right && Keyboard.Modifiers == ModifierKeys.Alt) { ViewModel.GoForward(); args.Handled = true; return; }
        var shortcut = DesktopShortcuts.Definitions.FirstOrDefault(item => item.Id != "close" && ViewModel.ResolveShortcut(item).Matches(this, args));
        switch (shortcut?.Id)
        {
            case "fullscreen": ViewModel.FullScreen = !ViewModel.FullScreen; break;
            case "esc" when ViewModel.FullScreen: ViewModel.FullScreen = false; break;
            case "home": ViewModel.Navigate("home"); break;
            case "settings": ViewModel.Navigate("settings"); break;
            case "search": OpenScreenSearch(); break;
            case "sidebar": ViewModel.SidebarExpanded = !ViewModel.SidebarExpanded; break;
            case "theme": ViewModel.CycleTheme(); break;
            default: return;
        }
        args.Handled = true;
    }

    private void OnOpenProfile(object sender, RoutedEventArgs args)
    {
        if (!ViewModel.Workspace.IsAuthenticated || ViewModel.Workspace.MustChangePassword) return;
        var dialog = new StaffProfileWindow(ViewModel.Workspace) { Owner = this };
        CloseDetailsOnSessionChange(dialog);
        dialog.ShowDialog();
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