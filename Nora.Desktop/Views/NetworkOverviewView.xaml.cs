using System.Windows.Controls;

namespace Nora.Desktop.Views;

/// <summary>Visão geral da rede (Secretaria): totais de agora, resumo do período, comparativo por região e o modo gráficos.</summary>
public partial class NetworkOverviewView : UserControl
{
    public NetworkOverviewView()
    {
        InitializeComponent();
        // A pesquisa do cabeçalho filtra os cartões, os gráficos e as regiões pelo que a tela mostra.
        new NetworkSearchBinding(this, "home", SearchEmpty)
            .Watch(NowCards, "Title,Value").Watch(PeriodCards, "Title,Value").Watch(Regions.Table, "Name").Watch(Charts.List, "Title");
    }
}
