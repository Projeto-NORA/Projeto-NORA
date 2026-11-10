using System.Windows;
using System.Windows.Controls;
using Nora.Desktop.ViewModels;

namespace Nora.Desktop.Views;

public partial class UnitLightningSlotsView : UserControl
{
    public UnitLightningSlotsView() => InitializeComponent();
    private async void OnLoad(object sender, RoutedEventArgs e) { if (DataContext is WorkspaceViewModel vm) await vm.LoadUnitLightningSlotsAsync(); }
}
