using System.Windows;
using System.Windows.Controls;
using Nora.Desktop.Models;
using Nora.Desktop.Services;
using Nora.Desktop.ViewModels;

namespace Nora.Desktop.Views;

/// <summary>Comunicação da Secretaria: listagem aberta dos comunicados da conta, publicar em pop-up (com confirmação) e encerrar pelo balão (lixeira, com confirmação).</summary>
public partial class CommunicationView : UserControl
{
    public CommunicationView()
    {
        InitializeComponent();
        MunicipalDetailsHost.Attach(PublishedList, this);
        ListingBehavior.AddBalloonRefreshedHandler(PublishedList, OnBalloonRefreshed);
    }

    private WorkspaceViewModel? Workspace => DataContext as WorkspaceViewModel;
    private IReadOnlyList<AnnouncementSummary> Selected => ListingBehavior.GetSelectedItems<AnnouncementSummary>(PublishedList);

    /// <summary>Encerrar só serve a quem está em vigor; com nenhum em vigor na seleção a ação some.</summary>
    private void OnBalloonRefreshed(object sender, RoutedEventArgs args) =>
        CloseButton.Visibility = Selected.Any(item => item.CanClose) ? Visibility.Visible : Visibility.Collapsed;

    private async void OnLoadMore(object sender, RoutedEventArgs e) { if (Workspace is { } vm) await vm.LoadMoreMyAnnouncementsAsync(); }
    private async void OnView(object sender, RoutedEventArgs e) { if (Selected.FirstOrDefault() is { } item) await MunicipalDetailsHost.OpenAsync(this, item); }

    /// <summary>Comunicado novo no pop-up: com campos inválidos o formulário mostra os erros; só um comunicado válido chega à pergunta de confirmação.</summary>
    private void OnNew(object sender, RoutedEventArgs e)
    {
        if (Workspace is not { } vm) return;
        vm.NewAnnouncementDraft();
        var problem = "";
        async Task<bool> Publish()
        {
            problem = "";
            if (!vm.CanPublishAnnouncement) { problem = "Aguarde a operação em andamento terminar."; return false; }
            if (!vm.IsAnnouncementDraftValid()) { problem = "Corrija os campos destacados do comunicado."; return false; }
            if (!Confirmation.Ask(null, "Publicar comunicado", vm.AnnouncementConfirmationText)) { problem = "A publicação não foi confirmada."; return false; }
            return await vm.PublishAnnouncementAsync();
        }
        DialogService.ShowForm(Window.GetWindow(this), "Novo comunicado", new AnnouncementFormView { DataContext = vm }, Publish, "Publicar", error: () => problem.Length > 0 ? problem : vm.Message);
    }

    private async void OnClose(object sender, RoutedEventArgs e)
    {
        if (Workspace is not { } vm) return;
        var items = Selected.Where(item => item.CanClose).ToArray();
        if (items.Length == 0) return;
        var question = items.Length == 1
            ? $"Encerrar o comunicado \"{items[0].Title}\"?\n\nEle deixa de aparecer para as equipes e para os pacientes. Continua na sua lista, com a contagem de leituras, e não dá para reabri-lo."
            : $"Encerrar os {items.Length} comunicados em vigor selecionados?\n\nEles deixam de aparecer para as equipes e para os pacientes. Continuam na sua lista, com a contagem de leituras, e não dá para reabri-los.";
        if (!Confirmation.Ask(Window.GetWindow(this), "Encerrar comunicado", question)) return;
        await vm.CloseMyAnnouncementsAsync(items);
    }
}
