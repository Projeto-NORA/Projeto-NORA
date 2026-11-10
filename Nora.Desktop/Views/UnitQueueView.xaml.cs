using System.Windows;
using System.Windows.Controls;
using Nora.Desktop.ViewModels;

namespace Nora.Desktop.Views;

public partial class UnitQueueView : UserControl
{
    public UnitQueueView() => InitializeComponent();
    private async void OnLoad(object sender, RoutedEventArgs e) { if (DataContext is WorkspaceViewModel vm) await vm.LoadUnitWaitingListAsync(); }
    private async void OnAdd(object sender, RoutedEventArgs e) { if (DataContext is WorkspaceViewModel vm) await vm.AddUnitWaitingEntryAsync(); }
    private async void OnCancel(object sender, RoutedEventArgs e)
    {
        if (DataContext is not WorkspaceViewModel vm || vm.SelectedWaitingEntry is null) return;
        if (MessageBox.Show("Confirma o cancelamento desta entrada na fila?", "Cancelar entrada", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            await vm.CancelUnitWaitingEntryAsync();
    }
}
