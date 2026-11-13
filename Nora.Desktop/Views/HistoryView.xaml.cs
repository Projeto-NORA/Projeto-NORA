using System.Windows;
using System.Windows.Controls;
using Nora.Desktop.ViewModels;

namespace Nora.Desktop.Views;

/// <summary>Histórico de ações da gestão, paginado por "Carregar mais".</summary>
public partial class HistoryView : UserControl
{
    public HistoryView() => InitializeComponent();
    private async void OnQuery(object sender, RoutedEventArgs e) { if (DataContext is WorkspaceViewModel vm) await vm.LoadHistoryAsync(); }
    private async void OnMore(object sender, RoutedEventArgs e) { if (DataContext is WorkspaceViewModel vm) await vm.LoadMoreHistoryAsync(); }
}
