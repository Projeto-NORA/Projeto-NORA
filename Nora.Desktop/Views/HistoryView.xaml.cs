using System.Windows;
using System.Windows.Controls;
using Nora.Desktop.Models;
using Nora.Desktop.ViewModels;

namespace Nora.Desktop.Views;

/// <summary>Histórico de ações da gestão: listagem aberta só de leitura, com ficha em linhas; os filtros ficam no botão Filtros e a lista é paginada por "Carregar mais".</summary>
public partial class HistoryView : UserControl
{
    public HistoryView() { InitializeComponent(); MunicipalDetailsHost.Attach(HistoryList, this); }

    private async void OnQuery(object sender, RoutedEventArgs e) { if (DataContext is WorkspaceViewModel vm) await vm.LoadHistoryAsync(); }
    private async void OnMore(object sender, RoutedEventArgs e) { if (DataContext is WorkspaceViewModel vm) await vm.LoadMoreHistoryAsync(); }
    private async void OnView(object sender, RoutedEventArgs e) { if (ListingBehavior.GetSelectedItems<HistoryEvent>(HistoryList).FirstOrDefault() is { } item) await MunicipalDetailsHost.OpenAsync(this, item); }
}
