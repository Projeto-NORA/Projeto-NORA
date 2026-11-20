using System.Windows;
using System.Windows.Controls;
using Nora.Desktop.ViewModels;

namespace Nora.Desktop.Views;

/// <summary>Capacidade da rede por UBS e região, com o cadastro (profissionais, consultórios e grade) de uma UBS em leitura.</summary>
public partial class CapacityView : UserControl
{
    public CapacityView() => InitializeComponent();
    /// <summary>Clicar (ou Enter) numa linha de "Capacidade por UBS" escolhe essa UBS no cadastro em leitura e leva a tela até ele; andar pelas setas só muda a seleção.</summary>
    private void OnUnitRowClicked(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        for (var source = e.OriginalSource as DependencyObject; source != null && source != sender; source = System.Windows.Media.VisualTreeHelper.GetParent(source))
            if (source is DataGridRow) { ShowUnitCatalog(sender as DataGrid); return; }
    }
    private void OnUnitRowKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != System.Windows.Input.Key.Enter) return;
        if (ShowUnitCatalog(sender as DataGrid)) e.Handled = true;
    }
    private bool ShowUnitCatalog(DataGrid? grid)
    {
        if (DataContext is not WorkspaceViewModel vm || grid?.SelectedItem is not Nora.Desktop.Models.CapacityRow { UnitId: var id } || id == Guid.Empty) return false;
        if (vm.Units.FirstOrDefault(unit => unit.Id == id) is { } unit) { vm.CapacityUnit = unit; CatalogCard.BringIntoView(); return true; }
        return false;
    }
    private void OnRetryCatalog(object sender, RoutedEventArgs e) => (DataContext as WorkspaceViewModel)?.RetryCapacityCatalog();
}
