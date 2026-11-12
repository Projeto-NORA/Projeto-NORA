using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Nora.Desktop.ViewModels;

namespace Nora.Desktop.Views;

public partial class UnitLightningSlotsView : UserControl
{
    public UnitLightningSlotsView() => InitializeComponent();
    private async void OnLoad(object sender, RoutedEventArgs e) { if (DataContext is WorkspaceViewModel vm) await vm.LoadUnitLightningSlotsAsync(); }
    private void OnRowDoubleClick(object sender, MouseButtonEventArgs e) => ListingInteraction.OpenOnDoubleClick(this, sender, e);
    private void OnRowKeyDown(object sender, KeyEventArgs e) => ListingInteraction.OpenOnEnter(this, sender, e);
}
