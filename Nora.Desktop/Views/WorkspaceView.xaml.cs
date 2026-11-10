using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Nora.Desktop.ViewModels;

namespace Nora.Desktop.Views;

public partial class WorkspaceView : UserControl
{
    private WorkspaceViewModel? observed;
    private ScrollViewer? viewport;
    public WorkspaceView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Observe();
        Loaded += (_, _) => { Observe(); ObserveViewport(); };
        Unloaded += (_, _) => { LoginPassword.Clear(); PatientPassword.Clear(); Unobserve(); UnobserveViewport(); };
    }
    private void UpdatePatientListHeight()
    {
        if (viewport != null)
            PatientList.MaxHeight = Math.Clamp(viewport.ActualHeight - 280, 160, 620);
    }
    private void ObserveViewport()
    {
        UnobserveViewport();
        for (var parent = VisualTreeHelper.GetParent(this); parent != null; parent = VisualTreeHelper.GetParent(parent))
        {
            if (parent is not ScrollViewer scrolling) continue;
            viewport = scrolling;
            viewport.SizeChanged += OnViewportSizeChanged;
            break;
        }
        UpdatePatientListHeight();
    }
    private void UnobserveViewport()
    {
        if (viewport != null) viewport.SizeChanged -= OnViewportSizeChanged;
        viewport = null;
    }
    private void OnViewportSizeChanged(object sender, SizeChangedEventArgs args) => UpdatePatientListHeight();
    private void Observe()
    {
        Unobserve();
        observed = DataContext as WorkspaceViewModel;
        if (observed != null) observed.PropertyChanged += OnWorkspaceChanged;
    }
    private void Unobserve() { if (observed != null) observed.PropertyChanged -= OnWorkspaceChanged; observed = null; }
    private void OnWorkspaceChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(WorkspaceViewModel.IsAuthenticated)) { LoginPassword.Clear(); PatientPassword.Clear(); }
    }
    private void OnListSelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (sender is DataGrid table && DataContext is WorkspaceViewModel vm)
            vm.SelectListItem(table.SelectedItem);
    }
    private void OnListPointerSelection(object sender, MouseButtonEventArgs args)
    {
        if (sender is not DataGrid table || DataContext is not WorkspaceViewModel vm) return;
        if (ListingInteraction.FindRow(args.OriginalSource as DependencyObject, table) is { } row)
            vm.SelectListItem(row.Item);
    }
    private void OnListDoubleClick(object sender, MouseButtonEventArgs args)
    {
        if (args.ChangedButton != MouseButton.Left || sender is not DataGrid table) return;
        var row = ListingInteraction.FindRow(args.OriginalSource as DependencyObject, table);
        if (row == null || Window.GetWindow(this) is not ShellWindow shell) return;
        shell.OpenItemDetails(row.Item);
        args.Handled = true;
    }
    private void OnListKeyDown(object sender, KeyEventArgs args)
    {
        if (args.Key != Key.Enter || sender is not DataGrid { SelectedItem: { } item } || Window.GetWindow(this) is not ShellWindow shell) return;
        shell.OpenItemDetails(item);
        args.Handled = true;
    }
    private async void OnLogin(object sender, RoutedEventArgs args)
    {
        if (DataContext is not WorkspaceViewModel vm || !vm.CanLogin) return;
        var password = LoginPassword.Password;
        LoginPassword.Clear();
        await vm.LoginAsync(password);
    }
    private void OnLoginKeyDown(object sender, KeyEventArgs args)
    {
        if (args.Key != Key.Enter) return;
        OnLogin(sender, args); args.Handled = true;
    }
    private async void OnRegisterPatient(object sender, RoutedEventArgs args)
    {
        if (DataContext is not WorkspaceViewModel vm || !vm.CanOperate) return;
        var password = PatientPassword.Password;
        PatientPassword.Clear();
        await vm.RegisterPatientAsync(password);
    }
    private void OnRegistrationExpanded(object sender, RoutedEventArgs args)
    {
        if (DataContext is WorkspaceViewModel vm && vm.ClearSelectionCommand.CanExecute(null)) vm.ClearSelectionCommand.Execute(null);
    }
}
