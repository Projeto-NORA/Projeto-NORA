using System.Windows;
using System.Windows.Controls;
using Nora.Desktop.Services;
using Nora.Desktop.ViewModels;

namespace Nora.Desktop.Views;

/// <summary>Comunicação da Secretaria: publicar (com confirmação), listar, ver leituras e encerrar (com confirmação).</summary>
public partial class CommunicationView : UserControl
{
    public CommunicationView() => InitializeComponent();
    private WorkspaceViewModel? Workspace => DataContext as WorkspaceViewModel;
    private async void OnRefresh(object sender, RoutedEventArgs e) { if (Workspace is { } vm) await vm.LoadMyAnnouncementsAsync(); }
    private async void OnLoadMore(object sender, RoutedEventArgs e) { if (Workspace is { } vm) await vm.LoadMoreMyAnnouncementsAsync(); }
    private async void OnReaders(object sender, RoutedEventArgs e) { if (Workspace is { } vm) await vm.LoadAnnouncementReadersAsync(); }
    private void OnClear(object sender, RoutedEventArgs e) => Workspace?.NewAnnouncementDraft();

    private async void OnPublish(object sender, RoutedEventArgs e)
    {
        if (Workspace is not { CanPublishAnnouncement: true } vm) return;
        // Com campos inválidos o formulário mostra os erros; só um comunicado válido chega à pergunta de confirmação.
        if (!vm.IsAnnouncementDraftValid()) { await vm.PublishAnnouncementAsync(); return; }
        if (!Confirmation.Ask(Window.GetWindow(this), "Publicar comunicado", vm.AnnouncementConfirmationText)) return;
        await vm.PublishAnnouncementAsync();
    }

    private async void OnClose(object sender, RoutedEventArgs e)
    {
        if (Workspace is not { SelectedMyAnnouncement: { CanClose: true } item, CanCloseMyAnnouncement: true } vm) return;
        if (!Confirmation.Ask(Window.GetWindow(this), "Encerrar comunicado",
            $"Encerrar o comunicado \"{item.Title}\"?\n\nEle deixa de aparecer para as equipes e para os pacientes. Continua na sua lista, com a contagem de leituras, e não dá para reabri-lo."))
            return;
        await vm.CloseMyAnnouncementAsync();
    }
}
