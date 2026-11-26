using System.Windows.Controls;

namespace Nora.Desktop.Views;

/// <summary>Demanda por especialidade no período (Secretaria): tabela de números com a pesquisa do cabeçalho.</summary>
public partial class NetworkSpecialtiesView : UserControl
{
    public NetworkSpecialtiesView()
    {
        InitializeComponent();
        new NetworkSearchBinding(this, SearchEmpty).Watch(SpecialtiesGrid, "Name");
    }
}
