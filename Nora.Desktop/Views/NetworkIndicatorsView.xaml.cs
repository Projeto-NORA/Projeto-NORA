using System.Windows.Controls;

namespace Nora.Desktop.Views;

/// <summary>Indicadores da rede (Desempenho): resumo do período, comparativo por região e o modo gráficos.</summary>
public partial class NetworkIndicatorsView : UserControl
{
    public NetworkIndicatorsView()
    {
        InitializeComponent();
        new NetworkSearchBinding(this, "indicators", SearchEmpty).Watch(PeriodCards, "Title,Value").Watch(Regions.Table, "Name").Watch(DailyGrid, "DateDisplay").Watch(Charts.List, "Title");
    }
}
