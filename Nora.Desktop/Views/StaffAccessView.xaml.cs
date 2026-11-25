using System.Windows;
using System.Windows.Controls;
using Nora.Desktop.Services;
using Nora.Desktop.ViewModels;

namespace Nora.Desktop.Views;
public partial class StaffAccessView : UserControl
{
    public StaffAccessView() => InitializeComponent();
    private async void OnLoadUnits(object sender, RoutedEventArgs e) { if (DataContext is WorkspaceViewModel vm) await vm.LoadStaffUnitsAsync(); }
    private async void OnGrant(object sender, RoutedEventArgs e)
    {
        if (DataContext is not WorkspaceViewModel { SelectedStaff: { } staff, SelectedStaffUnit: { } unit } vm) return;
        if (Confirmation.Ask(Window.GetWindow(this), "Confirmar concessão", $"Conceder a {staff.FullName} acesso à UBS {unit.Name}? A conta passa a ver cadastros, agenda e vagas dessa unidade."))
            await vm.ChangeStaffUnitAsync(true);
    }
    private async void OnRevoke(object sender, RoutedEventArgs e)
    {
        if (DataContext is not WorkspaceViewModel { SelectedStaff: { } staff, SelectedStaffGrant: { } unit } vm) return;
        if (Confirmation.Ask(Window.GetWindow(this), "Confirmar revogação", $"Revogar o vínculo de {staff.FullName} com {unit.Name}?"))
            await vm.ChangeStaffUnitAsync(false);
    }
}
