using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Nora.Desktop.Models;
using Nora.Desktop.Services;
using Nora.Desktop.ViewModels;

namespace Nora.Desktop.Views;

/// <summary>Alertas da rede (Secretaria): listagem aberta com filtros de UBS e região, pesquisa do cabeçalho e ficha no duplo clique.</summary>
public partial class NetworkAlertsView : UserControl
{
    public NetworkAlertsView()
    {
        InitializeComponent();
        new NetworkSearchBinding(this, "alerts", SearchEmpty).Watch(AlertList, "Name,MetaLine,Category");
        ListingBehavior.SetOpenHandler(AlertList, item => { OpenDetails(item); return Task.CompletedTask; });
        // A tela ocupa a altura da área de conteúdo: a página não rola, a lista rola sozinha e o balão fica no pé.
        ContentAreaFill.Attach(this);
    }

    private void OnDetails(object sender, RoutedEventArgs e)
    {
        if (ListingBehavior.GetSelectedItems<NetworkAlertEntry>(AlertList) is [var item]) OpenDetails(item);
    }

    private void OpenDetails(object item)
    {
        if (item is not NetworkAlertEntry alert) return;
        var fields = new List<DetailField>
        {
            new("UBS", alert.UnitName), new("Tipo", alert.Title), new("Especialidade", alert.Specialty.Length > 0 ? alert.Specialty : "Não se aplica"),
            new("Valor", alert.Value), new("Limite", alert.Threshold)
        };
        if (alert.Since != "—") fields.Add(new("Desde", alert.Since));
        fields.Add(new("O que significa", alert.Description));
        var owner = Window.GetWindow(this);
        var dialog = new ItemDetailsWindow(new ItemDetails(alert.Name, "Alerta da rede", fields)) { Owner = owner, Title = alert.Name };
        var workspace = DataContext as WorkspaceViewModel;
        // A ficha fecha se a sessão termina, para nunca ficar aberta sobre a tela de acesso.
        PropertyChangedEventHandler closeOnSignOut = (_, changed) => { if (changed.PropertyName == nameof(WorkspaceViewModel.IsAuthenticated) && workspace?.IsAuthenticated == false) dialog.Close(); };
        if (workspace is not null) workspace.PropertyChanged += closeOnSignOut;
        dialog.Closed += (_, _) => { if (workspace is not null) workspace.PropertyChanged -= closeOnSignOut; };
        DialogService.ShowModal(dialog, owner);
    }
}
