using System.Windows.Controls;

namespace Nora.Desktop.Views;

/// <summary>Grade de gráficos (<c>NetworkCharts</c> do ViewModel) do modo gráficos das telas de números da rede.</summary>
public partial class NetworkChartsView : UserControl
{
    public NetworkChartsView() => InitializeComponent();

    /// <summary>A lista de gráficos, para a pesquisa do cabeçalho filtrar pelo título.</summary>
    internal ItemsControl List => ChartList;
}
