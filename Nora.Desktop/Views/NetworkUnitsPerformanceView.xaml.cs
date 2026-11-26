using System.Windows.Controls;

namespace Nora.Desktop.Views;

/// <summary>Desempenho de cada UBS no período (Secretaria): tabela de números com a pesquisa do cabeçalho.</summary>
public partial class NetworkUnitsPerformanceView : UserControl
{
    public NetworkUnitsPerformanceView()
    {
        InitializeComponent();
        new NetworkSearchBinding(this, SearchEmpty).Watch(UnitsGrid, "Name,Region");
    }
}
