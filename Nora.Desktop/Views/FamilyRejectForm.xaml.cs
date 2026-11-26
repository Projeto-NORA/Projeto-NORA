using System.Windows.Controls;

namespace Nora.Desktop.Views;

/// <summary>Corpo do pop-up "Recusar pedido" da Família (aberto por <see cref="FamilyView"/>): o resumo do pedido e o campo do motivo.</summary>
public partial class FamilyRejectForm : UserControl
{
    public FamilyRejectForm(string summary)
    {
        InitializeComponent();
        SummaryText.Text = summary;
    }
}
