using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Nora.Desktop.Models;
using Nora.Desktop.Services;
using Nora.Desktop.ViewModels;

namespace Nora.Desktop.Views;

/// <summary>Alertas da rede (Secretaria): listagem aberta com filtros de UBS e região, pesquisa do cabeçalho e ficha no duplo clique.</summary>
public partial class NetworkAlertsView : UserControl
{
    private ScrollViewer? scroll;

    public NetworkAlertsView()
    {
        InitializeComponent();
        new NetworkSearchBinding(this, SearchEmpty).Watch(AlertList, "Name,MetaLine,Category");
        ListingBehavior.SetOpenHandler(AlertList, item => { OpenDetails(item); return Task.CompletedTask; });
        Loaded += (_, _) => FillArea();
        Unloaded += (_, _) => { if (scroll is not null) scroll.SizeChanged -= OnAreaChanged; scroll = null; };
    }

    /// <summary>
    /// A tela ocupa a altura da área de conteúdo (a página não rola; a lista rola sozinha e o balão fica no pé). O contêiner da Secretaria empilha as telas
    /// sem limitar a altura, então a tela mede a área sozinha e se ajusta quando a janela muda de tamanho.
    /// </summary>
    private void FillArea()
    {
        if (scroll is not null) scroll.SizeChanged -= OnAreaChanged;
        for (DependencyObject? node = VisualTreeHelper.GetParent(this); node is not null; node = VisualTreeHelper.GetParent(node))
            if (node is ScrollViewer found) { scroll = found; break; }
        if (scroll is null) return;
        scroll.SizeChanged += OnAreaChanged;
        OnAreaChanged(scroll, null!);
    }

    private void OnAreaChanged(object sender, SizeChangedEventArgs args)
    {
        // 48 = margens da área de conteúdo (32 em cima e 16 embaixo).
        if (scroll is { ActualHeight: > 0 }) Height = Math.Max(320, scroll.ActualHeight - 48);
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
