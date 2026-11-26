using System.Windows;
using System.Windows.Controls;
using Nora.Desktop.ViewModels;

namespace Nora.Desktop.Views;

/// <summary>Comunicados recebidos pela equipe (OBS): só a listagem aberta, com o balão "Marcar como lido" (um ou vários) e o texto completo na ficha.</summary>
public partial class AnnouncementsView : UserControl
{
    public AnnouncementsView()
    {
        InitializeComponent();
        // "Marcar como lido" só aparece quando há algum comunicado não lido entre os selecionados.
        ListingBehavior.AddBalloonRefreshedHandler(ReceivedFilterList, (_, _) => UpdateMarkReadButton());
    }

    private void UpdateMarkReadButton()
    {
        MarkReadButton.Visibility = ListingBehavior.GetSelectedItems<AnnouncementRow>(ReceivedFilterList).Any(row => row.IsUnread) ? Visibility.Visible : Visibility.Collapsed;
        // O balão só percebe a mudança de um botão que já está na tela; com a área de ações escondida (nenhuma ação visível antes) ele precisa ser avisado.
        Balloon.RefreshActions();
    }

    private async void OnMarkRead(object sender, RoutedEventArgs e)
    {
        if (DataContext is not WorkspaceViewModel vm) return;
        await vm.MarkAnnouncementsReadAsync(ListingBehavior.GetSelectedItems<AnnouncementRow>(ReceivedFilterList));
        // A seleção continua (as linhas mudam no lugar): o balão ajusta as ações ao novo estado de leitura.
        UpdateMarkReadButton();
    }
}
