using System.Windows;
using System.Windows.Controls;
using Nora.Desktop.ViewModels;
namespace Nora.Desktop.Views;
public partial class HealthUnitsView : UserControl
{
    public HealthUnitsView() => InitializeComponent();
    private async void OnRefresh(object sender, RoutedEventArgs e) { if (DataContext is WorkspaceViewModel vm) await vm.LoadUnitsAsync(); }
    private async void OnSave(object sender, RoutedEventArgs e) { if (DataContext is WorkspaceViewModel vm) await vm.SaveUnitAsync(); }
    private async void OnProvision(object sender, RoutedEventArgs e) { if (DataContext is WorkspaceViewModel vm) await vm.ProvisionSelectedUnitDatabaseAsync(); }
    private void OnNew(object sender, RoutedEventArgs e) { if (DataContext is WorkspaceViewModel vm) vm.NewUnitDraft(); }
    private void OnCancel(object sender, RoutedEventArgs e) { if (DataContext is WorkspaceViewModel vm) vm.CancelUnitForm(); }
    private void OnEdit(object sender, RoutedEventArgs e) { if (DataContext is WorkspaceViewModel vm) vm.EditSelectedUnit(); }
}
