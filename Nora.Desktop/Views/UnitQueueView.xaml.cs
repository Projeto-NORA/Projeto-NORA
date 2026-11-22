using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Nora.Desktop.Services;
using Nora.Desktop.ViewModels;

namespace Nora.Desktop.Views;

public partial class UnitQueueView : UserControl
{
    public UnitQueueView() => InitializeComponent();
    private async void OnLoad(object sender, RoutedEventArgs e) { if (DataContext is WorkspaceViewModel vm) await vm.LoadUnitWaitingListAsync(); }
    private async void OnMoreHistory(object sender, RoutedEventArgs e) { if (DataContext is WorkspaceViewModel { CanLoadMoreQueueHistory: true } vm) await vm.LoadMoreQueueHistoryAsync(); }
    private async void OnAdd(object sender, RoutedEventArgs e) { if (DataContext is WorkspaceViewModel vm) await vm.AddUnitWaitingEntryAsync(); }
    private void OnToggleCallNext(object sender, RoutedEventArgs e) { if (DataContext is WorkspaceViewModel vm) vm.ToggleCallNext(); }
    private async void OnCallNextRetryCatalog(object sender, RoutedEventArgs e) { if (DataContext is WorkspaceViewModel { CanRetryCallNextCatalog: true } vm) await vm.RetryCallNextCatalogAsync(); }
    private async void OnCallNextSearch(object sender, RoutedEventArgs e) { if (DataContext is WorkspaceViewModel { CanCallNextSearch: true } vm) await vm.SearchCallNextSlotsAsync(); }
    private bool confirmingCallNext;
    private async void OnCallNextOffer(object sender, RoutedEventArgs e)
    {
        // Um clique por vez: o duplo clique não abre duas confirmações enquanto a conferência do horário roda.
        if (confirmingCallNext || DataContext is not WorkspaceViewModel { CanCallNextOffer: true } vm) return;
        confirmingCallNext = true;
        try
        {
            if (!await vm.PrepareCallNextConfirmationAsync()) return;
            if (!Confirmation.Ask(Window.GetWindow(this), "Chamar próximo", vm.CallNextConfirmationText)) return;
        }
        finally { confirmingCallNext = false; }
        await vm.OfferCallNextAsync();
    }
    private void OnRowDoubleClick(object sender, MouseButtonEventArgs e) => ListingInteraction.OpenOnDoubleClick(this, sender, e);
    private void OnRowKeyDown(object sender, KeyEventArgs e) => ListingInteraction.OpenOnEnter(this, sender, e);
    private async void OnCancel(object sender, RoutedEventArgs e)
    {
        if (DataContext is not WorkspaceViewModel vm || vm.SelectedWaitingEntry is null) return;
        if (MessageBox.Show("Confirma o cancelamento desta entrada na fila?", "Cancelar entrada", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            await vm.CancelUnitWaitingEntryAsync();
    }
}
