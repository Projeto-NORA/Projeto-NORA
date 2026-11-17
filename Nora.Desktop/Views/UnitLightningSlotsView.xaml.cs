using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Nora.Desktop.Services;
using Nora.Desktop.ViewModels;

namespace Nora.Desktop.Views;

public partial class UnitLightningSlotsView : UserControl
{
    public UnitLightningSlotsView() => InitializeComponent();
    private async void OnLoad(object sender, RoutedEventArgs e) { if (DataContext is WorkspaceViewModel vm) await vm.LoadUnitLightningSlotsAsync(); }
    private async void OnApprove(object sender, RoutedEventArgs e)
    {
        if (DataContext is not WorkspaceViewModel { CanDecideSlotApproval: true } vm) return;
        if (!Confirmation.Ask(Window.GetWindow(this), "Aprovar e ofertar vaga", vm.ApproveSlotConfirmationText)) return;
        await vm.ApproveSelectedSlotAsync();
    }
    private async void OnDiscard(object sender, RoutedEventArgs e)
    {
        if (DataContext is not WorkspaceViewModel { CanDecideSlotApproval: true } vm) return;
        if (!Confirmation.Ask(Window.GetWindow(this), "Descartar vaga", vm.DiscardSlotConfirmationText)) return;
        await vm.DiscardSelectedSlotAsync();
    }
    private void OnRowDoubleClick(object sender, MouseButtonEventArgs e) => ListingInteraction.OpenOnDoubleClick(this, sender, e);
    private void OnRowKeyDown(object sender, KeyEventArgs e) => ListingInteraction.OpenOnEnter(this, sender, e);
}
