using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Nora.Desktop.ViewModels;

namespace Nora.Desktop.Views;

/// <summary>Seleciona o conteúdo da seção atual da Secretaria.</summary>
public partial class MunicipalView : UserControl
{
    private WorkspaceViewModel? workspace;
    public MunicipalView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Attach();
        Loaded += (_, _) => Attach();
        Unloaded += (_, _) => Detach();
    }
    private void Attach()
    {
        Detach();
        workspace = DataContext as WorkspaceViewModel;
        if (workspace != null) workspace.PropertyChanged += OnWorkspaceChanged;
        Render();
    }
    private void Detach()
    {
        if (workspace != null) workspace.PropertyChanged -= OnWorkspaceChanged;
        workspace = null;
    }
    private void OnWorkspaceChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(WorkspaceViewModel.Section) or nameof(WorkspaceViewModel.IsAuthenticated)) Render();
    }
    private static TextBlock Text(string value, double size = 14, bool bold = false, bool muted = false)
    {
        var t = new TextBlock { Text = value, FontSize = size, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, TextWrapping = TextWrapping.Wrap };
        t.SetResourceReference(TextBlock.ForegroundProperty, muted ? "SecondaryBrush" : "TextBrush");
        return t;
    }
    private void Notice(string message)
    {
        var block = Text(message, 13); var border = new Border { CornerRadius = new CornerRadius(12), Padding = new Thickness(16), Margin = new Thickness(0, 0, 0, 20), Child = block };
        border.SetResourceReference(Border.BackgroundProperty, "BrandSoftBrush"); ContentPanel.Children.Add(border);
    }
    /// <summary>Parte da página que aparece só na subaba indicada (barra lateral em dois níveis).</summary>
    private void Add(FrameworkElement part, string subPage, bool fill = false)
    {
        var host = new ContentControl { Content = part, Focusable = false };
        SubPage.SetKey(host, subPage);
        // Listagem aberta: a subtela recebe a altura inteira da área de conteúdo e a lista rola sozinha (o balão fica no pé da tela).
        if (fill) { host.HorizontalContentAlignment = HorizontalAlignment.Stretch; host.VerticalContentAlignment = VerticalAlignment.Stretch; PageLayout.SetFill(host, true); }
        ContentPanel.Children.Add(host);
    }
    /// <summary>Tela inteira de uma rota sem subtelas (Mapa da rede, Comunicação): recebe a altura inteira da área de conteúdo.</summary>
    private void AddFill(FrameworkElement screen) { PageLayout.SetFill(screen, true); ContentPanel.Children.Add(screen); }
    private void Render()
    {
        ContentPanel.Children.Clear(); if (workspace == null) return;
        if (!workspace.IsAuthenticated) Notice("Entre na Visão geral para ver os dados da rede.");
        switch (workspace.Section)
        {
            // Telas de números: o nome da tela fica só no cabeçalho da janela; filtros, alternador e ações vão para o cabeçalho (PageFilterGroup e PageActions).
            case "home": ContentPanel.Children.Add(new NetworkOverviewView()); break;
            case "units": Add(new HealthUnitsView(), "registry", fill: true); Add(new NetworkUnitsPerformanceView(), "performance"); Add(new UnitRegistryView(), "reference", fill: true); break;
            case "specialties": Add(new SpecialtyCatalogView(), "catalog", fill: true); Add(new NetworkSpecialtiesView(), "demand"); break;
            case "network": AddFill(new NetworkMapView()); break;
            case "indicators": ContentPanel.Children.Add(new NetworkIndicatorsView()); break;
            case "slots": ContentPanel.Children.Add(new NetworkSlotsView()); break;
            case "capacity": ContentPanel.Children.Add(new CapacityView()); break;
            case "alerts": ContentPanel.Children.Add(new NetworkAlertsView()); break;
            case "reports": ContentPanel.Children.Add(new GoalsView()); break;
            case "communication": AddFill(new CommunicationView()); break;
            // "Nova conta" leva à tela Contas com o pop-up de cadastro aberto: as duas subtelas mostram a mesma listagem.
            case "users": Add(new StaffAccountsView(), "accounts,create", fill: true); Add(new StaffAccessView(), "access", fill: true); Add(new HistoryView(), "history", fill: true); break;
        }
    }
}
