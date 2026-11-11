using System.Windows;
using System.Windows.Controls;
using Nora.Desktop.ViewModels;

namespace Nora.Desktop.Views;
public partial class StaffAccessView : UserControl
{
    public StaffAccessView() => InitializeComponent();
    private async void OnLoad(object sender, RoutedEventArgs e) { if (DataContext is WorkspaceViewModel vm) await vm.LoadStaffAsync(); }
    private async void OnLoadUnits(object sender, RoutedEventArgs e) { if (DataContext is WorkspaceViewModel vm) await vm.LoadStaffUnitsAsync(); }
    private async void OnGrant(object sender, RoutedEventArgs e) { if (DataContext is WorkspaceViewModel vm) await vm.ChangeStaffUnitAsync(true); }
    private async void OnRevoke(object sender, RoutedEventArgs e)
    {
        if (DataContext is not WorkspaceViewModel { SelectedStaff: { } staff, SelectedStaffGrant: { } unit } vm) return;
        if (MessageBox.Show(Window.GetWindow(this), $"Revogar o vínculo de {staff.FullName} com {unit.Name}?", "Confirmar revogação", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            await vm.ChangeStaffUnitAsync(false);
    }
}
