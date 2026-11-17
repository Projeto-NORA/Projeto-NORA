using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Nora.Desktop.ViewModels;

namespace Nora.Desktop.Views;

public partial class WorkspaceView : UserControl
{
    private WorkspaceViewModel? observed;
    public WorkspaceView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Observe();
        Loaded += (_, _) => Observe();
        Unloaded += (_, _) => { LoginPassword.Clear(); Unobserve(); };
    }
    private void Observe()
    {
        Unobserve();
        observed = DataContext as WorkspaceViewModel;
        if (observed != null) observed.PropertyChanged += OnWorkspaceChanged;
    }
    private void Unobserve() { if (observed != null) observed.PropertyChanged -= OnWorkspaceChanged; observed = null; }
    private void OnWorkspaceChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(WorkspaceViewModel.IsAuthenticated)) LoginPassword.Clear();
    }
    private async void OnLogin(object sender, RoutedEventArgs args)
    {
        if (DataContext is not WorkspaceViewModel vm || !vm.CanLogin) return;
        var password = LoginPassword.Password;
        LoginPassword.Clear();
        await vm.LoginAsync(password);
    }
    private async void OnActivate(object sender, RoutedEventArgs args)
    {
        if (DataContext is WorkspaceViewModel { CanActivateWithCode: true } vm) await vm.ActivateWithCodeAsync();
    }
    private void OnActivationKeyDown(object sender, KeyEventArgs args)
    {
        if (args.Key != Key.Enter) return;
        OnActivate(sender, args); args.Handled = true;
    }
    private void OnLoginKeyDown(object sender, KeyEventArgs args)
    {
        if (args.Key != Key.Enter) return;
        OnLogin(sender, args); args.Handled = true;
    }
}
