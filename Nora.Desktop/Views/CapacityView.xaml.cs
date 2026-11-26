using System.Windows.Controls;

namespace Nora.Desktop.Views;

/// <summary>Capacidade da rede por UBS e região, com o cadastro (profissionais, consultórios e grade) de uma UBS em leitura.</summary>
public partial class CapacityView : UserControl
{
    public CapacityView()
    {
        InitializeComponent();
        // A pesquisa do cabeçalho filtra os cartões e todas as tabelas da tela pelo que elas mostram.
        new NetworkSearchBinding(this, SearchEmpty)
            .Watch(CapacityCards, "Title,Value").Watch(UnitsGrid, "Name,Region").Watch(RegionsGrid, "Name")
            .Watch(ProfessionalsGrid, "FullName,SpecialtiesDisplay,CouncilDisplay").Watch(RoomsGrid, "Name").Watch(ScheduleGrid, "WeekdayDisplay,ProfessionalName,RoomName,SpecialtyName");
    }
}
