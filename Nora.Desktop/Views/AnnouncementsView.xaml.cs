using System.Windows;
using System.Windows.Controls;
using Nora.Desktop.ViewModels;

namespace Nora.Desktop.Views;

/// <summary>Comunicados recebidos pela equipe (OBS): lista, atualização completa e marcação de lido.</summary>
public partial class AnnouncementsView : UserControl
{
    public AnnouncementsView() => InitializeComponent();
    private async void OnRefresh(object sender, RoutedEventArgs e) { if (DataContext is WorkspaceViewModel vm) await vm.LoadAnnouncementsAsync(); }
    private async void OnMarkRead(object sender, RoutedEventArgs e)
    {
        if (DataContext is WorkspaceViewModel vm && (sender as FrameworkElement)?.DataContext is AnnouncementRow row) await vm.MarkAnnouncementReadAsync(row);
    }
}
