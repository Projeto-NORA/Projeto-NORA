using System.Windows;
using System.Windows.Controls;
using Nora.Desktop.ViewModels;

namespace Nora.Desktop.Views;

/// <summary>Painel automático da Vaga Relâmpago (só leitura): lista aberta, ficha com as ofertas no duplo clique e no Enter.</summary>
public partial class UnitLightningSlotsView : UserControl
{
    public UnitLightningSlotsView()
    {
        InitializeComponent();
        // A lista tem linhas do painel, e a ficha é a da vaga (lê as ofertas antes de abrir).
        ListingBehavior.SetOpenHandler(SlotList, async item =>
        {
            if (item is SlotPanelRow row && Window.GetWindow(this) is ShellWindow shell) await shell.OpenItemDetailsAsync(row.Slot);
        });
    }

    private async void OnMoreHistory(object sender, RoutedEventArgs e) { if (DataContext is WorkspaceViewModel { CanLoadMoreSlotsHistory: true } vm) await vm.LoadMoreSlotsHistoryAsync(); }
}
