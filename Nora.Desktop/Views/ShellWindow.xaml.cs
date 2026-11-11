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
    public ShellWindow(DesktopProfile profile, string? storageRoot = null, NoraApiClient? api = null)
    {
        themes = new ThemeService(Application.Current);
        ViewModel = new ShellViewModel(profile, new PreferencesService(profile.Id, storageRoot), themes, api);
        InitializeComponent();
        DataContext = ViewModel;
        fullScreen = new(this);
        SourceInitialized += (_, _) => ApplyWindowCorners();
        StateChanged += (_, _) => ApplyWindowCorners();
        ViewModel.PropertyChanged += OnPreferenceChanged;
        PreviewKeyDown += OnKeyDown;
        Loaded += (_, _) => { fullScreen.Apply(ViewModel.FullScreen); ApplyWindowCorners(); };
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
    public void OpenItemDetails(object item)
    {
        var details = ViewModel.Workspace.BuildDetails(item);
        if (details == null) return;
        var dialog = new ItemDetailsWindow(details)
        {
            Owner = this,
            Title = details.Title,
            MaxHeight = Math.Max(320, Math.Min(SystemParameters.WorkArea.Height - 40, ActualHeight - 40))
        };
        if (item is not OperationalRow)
            ClosePatientDialogOnSignOut(dialog, item is UnitPatientRecord or UnitWaitingEntry or UnitLightningSlot);
        dialog.ShowDialog();
    }
    private void OnActionDetails(object sender, RoutedEventArgs args)
    {
        if (ViewModel.Workspace.ActionSelection is { } item) OpenItemDetails(item);
    }
    private void OnLegacyDetails(object sender, RoutedEventArgs args)
    {
        if (ViewModel.SelectedRow is { } item) OpenItemDetails(item);
    }
    private void OnClearLegacySelection(object sender, RoutedEventArgs args) => ViewModel.SelectedRow = null;
    private void ClosePatientDialogOnSignOut(Window dialog, bool closeOnUnitChange = false)
    {
        var initialUnit = ViewModel.Workspace.ActiveObsUnit?.Id;
        PropertyChangedEventHandler closeOnSignOut = (_, changed) =>
        {
            if ((changed.PropertyName == nameof(WorkspaceViewModel.IsAuthenticated) && !ViewModel.Workspace.IsAuthenticated) ||
                (closeOnUnitChange && changed.PropertyName == nameof(WorkspaceViewModel.ActiveObsUnit) && ViewModel.Workspace.ActiveObsUnit?.Id != initialUnit)) dialog.Close();
        };
        ViewModel.Workspace.PropertyChanged += closeOnSignOut;
        dialog.Closed += (_, _) => ViewModel.Workspace.PropertyChanged -= closeOnSignOut;
    }
    private async void OnSetPatientActive(object sender, RoutedEventArgs args)
    {
        var workspace = ViewModel.Workspace;
        if (!workspace.CanSetPatientActive || workspace.ActionSelectedPatient is not { } patient) return;
        if (!ConfirmPatientAccess(patient)) return;
        await workspace.SetPatientActiveAsync(patient);
    }
    private bool ConfirmPatientAccess(PatientAccount patient)
    {
        var action = patient.IsActive ? "Desativar" : "Reativar";
        var message = patient.IsActive
            ? "A desativação bloqueia novos logins, encerra as sessões e cancela entradas e ofertas pendentes. Vagas liberadas seguem para o próximo paciente por ordem de entrada. O cadastro e o histórico são preservados."
            : "A reativação permite novos logins. Sessões antigas continuam encerradas e o paciente não retorna automaticamente às filas. Uma nova inclusão na fila deve ser feita manualmente.";
        var dialog = new Window
        {
            Owner = this, Title = action + " paciente", Width = 540, SizeToContent = SizeToContent.Height,
            WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = System.Windows.Media.Brushes.Transparent,
            FontFamily = FontFamily, FontSize = 14,
            ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false
        };
        dialog.SetResourceReference(ForegroundProperty, "TextBrush");
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = action + " " + patient.FullName + "?", FontSize = 22, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 16) });
        panel.Children.Add(new TextBlock { Text = message, Margin = new Thickness(0, 0, 0, 24), TextWrapping = TextWrapping.Wrap });
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        actions.Children.Add(new Button { Content = "Cancelar", IsCancel = true, Margin = new Thickness(0, 0, 8, 0) });
        var confirm = new Button { Content = action + " paciente", IsDefault = true, Style = (Style)Application.Current.FindResource("PrimaryButton") };
        confirm.SetBinding(IsEnabledProperty, new Binding(nameof(WorkspaceViewModel.CanOperate)) { Source = ViewModel.Workspace, Mode = BindingMode.OneWay });
        confirm.Click += (_, _) => dialog.DialogResult = true;
        actions.Children.Add(confirm); panel.Children.Add(actions);
        var frame = new Border { Child = panel, CornerRadius = new CornerRadius(18), BorderThickness = new Thickness(1), Padding = new Thickness(24), Margin = new Thickness(10) };
        frame.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush"); frame.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        dialog.Content = frame;
        ClosePatientDialogOnSignOut(dialog);
        return dialog.ShowDialog() == true;
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
