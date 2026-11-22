using System.Windows;
using System.Windows.Controls;
using Nora.Desktop.ViewModels;
using Nora.Desktop.Services;

namespace Nora.Desktop.Views;

public partial class ObsUnitPickerView : UserControl
{
    public ObsUnitPickerView() => InitializeComponent();
    private async void OnLoadUnits(object sender, RoutedEventArgs e) { if (DataContext is WorkspaceViewModel vm) await vm.LoadObsUnitsAsync(); }
    private async void OnConfirm(object sender, RoutedEventArgs e) { if (DataContext is WorkspaceViewModel vm) await vm.ConfirmObsUnitAsync(); }
    private void OnLocate(object sender, RoutedEventArgs e)
    {
        if (DataContext is not WorkspaceViewModel vm || !vm.HasAvailableObsUnits) return;
        if (!Confirmation.Ask(Window.GetWindow(this), "Acessar localização", "Permitir que o NORA acesse a localização deste computador para sugerir a UBS autorizada mais próxima?")) return;
        var dialog = new ObsLocationDialog { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() == true) vm.SuggestClosestObsUnit(dialog.Latitude, dialog.Longitude);
    }
}
