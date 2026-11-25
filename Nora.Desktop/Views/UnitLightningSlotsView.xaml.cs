using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Nora.Desktop.ViewModels;

namespace Nora.Desktop.Views;

/// <summary>Painel automático da Vaga Relâmpago (só leitura).</summary>
public partial class UnitLightningSlotsView : UserControl
{
    public UnitLightningSlotsView() => InitializeComponent();
    private async void OnLoad(object sender, RoutedEventArgs e) { if (DataContext is WorkspaceViewModel vm) await vm.LoadUnitLightningSlotsAsync(); }
    private async void OnMoreHistory(object sender, RoutedEventArgs e) { if (DataContext is WorkspaceViewModel { CanLoadMoreSlotsHistory: true } vm) await vm.LoadMoreSlotsHistoryAsync(); }
    private void OnRowDoubleClick(object sender, MouseButtonEventArgs e) => ListingInteraction.OpenOnDoubleClick(this, sender, e);
    private void OnRowKeyDown(object sender, KeyEventArgs e) => ListingInteraction.OpenOnEnter(this, sender, e);
}
