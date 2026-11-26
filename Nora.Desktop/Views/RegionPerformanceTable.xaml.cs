using System.Windows.Controls;

namespace Nora.Desktop.Views;

/// <summary>Tabela do comparativo por região (Visão geral e Indicadores): a mesma nas duas telas.</summary>
public partial class RegionPerformanceTable : UserControl
{
    public RegionPerformanceTable() => InitializeComponent();

    /// <summary>A tabela, para a pesquisa do cabeçalho filtrar as regiões.</summary>
    internal DataGrid Table => RegionGrid;
}
