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

public partial class ShellWindow : Window
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
        SourceInitialized += (_, _) => ApplyWindowCorners();
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
        if (args.PropertyName == nameof(ShellViewModel.FullScreen) && IsLoaded) { fullScreen.Apply(ViewModel.FullScreen); ApplyWindowCorners(); }
        if (args.PropertyName == nameof(ShellViewModel.Page))
        {
            CloseScreenSearch(restoreFocus: false);
            PageScroll.ScrollToTop();
            if (ViewModel.Animations)
                PageContainer.BeginAnimation(OpacityProperty, new DoubleAnimation(0.55, 1, TimeSpan.FromMilliseconds(160)));
        }
        if (args.PropertyName == nameof(ShellViewModel.Animations) && !ViewModel.Animations)
            PageContainer.BeginAnimation(OpacityProperty, null);
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
        if (HandleScreenSearchKey(args)) { args.Handled = true; return; }
        var shortcut = DesktopShortcuts.Definitions.FirstOrDefault(item => item.Id != "close" && ViewModel.ResolveShortcut(item).Matches(this, args));
        switch (shortcut?.Id)
        {
            case "fullscreen": ViewModel.FullScreen = !ViewModel.FullScreen; break;
            case "esc" when ViewModel.FullScreen: ViewModel.FullScreen = false; break;
            case "home": ViewModel.Navigate("home"); break;
            case "settings": ViewModel.Navigate("settings"); break;
            case "search": OpenScreenSearch(); break;
            case "sidebar": ViewModel.ToggleSidebarCommand.Execute(null); break;
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
        var dialog = new Window { Owner = this, Title = "Fechar " + ViewModel.Profile.Name, Width = 420, Height = 220, ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = (System.Windows.Media.Brush)Application.Current.FindResource("SurfaceBrush"), Foreground = (System.Windows.Media.Brush)Application.Current.FindResource("TextBrush"), ShowInTaskbar = false };
        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(new TextBlock { Text = "Deseja fechar o aplicativo?", FontSize = 21, FontWeight = FontWeights.SemiBold });
        panel.Children.Add(new TextBlock { Text = "Suas preferências salvas estarão disponíveis ao voltar.", Margin = new Thickness(0, 12, 0, 20), TextWrapping = TextWrapping.Wrap });
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "Continuar aqui", IsCancel = true, Margin = new Thickness(0, 0, 8, 0) };
        var close = new Button { Content = "Fechar", IsDefault = true, Style = (Style)Application.Current.FindResource("PrimaryButton") };
        close.Click += (_, _) => dialog.DialogResult = true;
        actions.Children.Add(cancel); actions.Children.Add(close); panel.Children.Add(actions); dialog.Content = panel;
        args.Cancel = dialog.ShowDialog() != true;
    }
}
