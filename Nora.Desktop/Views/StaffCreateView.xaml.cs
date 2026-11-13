using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Nora.Desktop.ViewModels;

namespace Nora.Desktop.Views;

/// <summary>Formulário de nova conta OBS. A senha provisória só existe nos campos: segue como argumento do envio e os campos são limpos ao criar ou sair.</summary>
public partial class StaffCreateView : UserControl
{
    private WorkspaceViewModel? observed;
    public StaffCreateView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Observe();
        Loaded += (_, _) => Observe();
        Unloaded += (_, _) => { ClearPasswords(); Unobserve(); };
    }
    private void Observe() { Unobserve(); observed = DataContext as WorkspaceViewModel; if (observed != null) observed.PropertyChanged += OnChanged; }
    private void Unobserve() { if (observed != null) observed.PropertyChanged -= OnChanged; observed = null; }
    private void OnChanged(object? sender, PropertyChangedEventArgs args) { if (args.PropertyName == nameof(WorkspaceViewModel.IsAuthenticated)) ClearPasswords(); }
    private void ClearPasswords() { Password.Clear(); Confirm.Clear(); }
    private async void OnCreate(object sender, RoutedEventArgs e)
    {
        if (DataContext is not WorkspaceViewModel { CanOperate: true } vm) return;
        if (await vm.CreateStaffAsync(Password.Password, Confirm.Password)) ClearPasswords();
    }
}
