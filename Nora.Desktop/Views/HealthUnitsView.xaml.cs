using System.Windows;
using System.Windows.Controls;
using Nora.Desktop.ViewModels;
namespace Nora.Desktop.Views;
public partial class HealthUnitsView : UserControl
{
    public HealthUnitsView() { InitializeComponent(); Loaded += OnLoaded; }
    private async void OnLoaded(object sender, RoutedEventArgs e) { if (DataContext is WorkspaceViewModel { IsAuthenticated: true, UnitsLoaded: false, CanOperate: true } vm) await vm.LoadUnitsAsync(); }
    private async void OnRefresh(object sender, RoutedEventArgs e) { if (DataContext is WorkspaceViewModel vm) await vm.LoadUnitsAsync(); }
    private async void OnReadRegistry(object sender, RoutedEventArgs e) { if (DataContext is WorkspaceViewModel vm) await vm.LoadUnitRegistryAsync(); }
    private async void OnReadAppointments(object sender, RoutedEventArgs e) { if (DataContext is WorkspaceViewModel vm) await vm.LoadMunicipalAppointmentsAsync(); }
    private async void OnSave(object sender, RoutedEventArgs e) { if (DataContext is WorkspaceViewModel vm) await vm.SaveUnitAsync(); }
    private void OnNew(object sender, RoutedEventArgs e) { if (DataContext is WorkspaceViewModel vm) vm.NewUnitDraft(); }
    private void OnEdit(object sender, RoutedEventArgs e) { if (DataContext is WorkspaceViewModel vm) vm.EditSelectedUnit(); }
}
