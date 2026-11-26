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
    private void Heading(string title, string description)
    {
        ContentPanel.Children.Add(Text(title, 28, true));
        ContentPanel.Children[^1].SetValue(ToolTipService.ToolTipProperty, description);
        ((TextBlock)ContentPanel.Children[^1]).Margin = new Thickness(0, 0, 0, 22);
    }
    private void Notice(string message)
    {
        var block = Text(message, 13); var border = new Border { CornerRadius = new CornerRadius(12), Padding = new Thickness(16), Margin = new Thickness(0, 0, 0, 20), Child = block };
        border.SetResourceReference(Border.BackgroundProperty, "BrandSoftBrush"); ContentPanel.Children.Add(border);
    }
    /// <summary>Parte da página que aparece só na subaba indicada (barra lateral em dois níveis).</summary>
    private void Add(FrameworkElement part, string subPage) { var host = new ContentControl { Content = part, Focusable = false }; SubPage.SetKey(host, subPage); ContentPanel.Children.Add(host); }
    private void Render()
    {
        ContentPanel.Children.Clear(); if (workspace == null) return;
        if (!workspace.IsAuthenticated) Notice("Entre na Visão geral para ver os dados da rede.");
        switch (workspace.Section)
        {
            // Telas de números: o nome da tela fica só no cabeçalho da janela; filtros, alternador e ações vão para o cabeçalho (PageFilterGroup e PageActions).
            case "home": ContentPanel.Children.Add(new NetworkOverviewView()); break;
            case "units": Add(new HealthUnitsView(), "registry"); Add(new NetworkUnitsPerformanceView(), "performance"); Add(new UnitRegistryView(), "reference"); break;
            case "specialties": Add(new SpecialtyCatalogView(), "catalog"); Add(new NetworkSpecialtiesView(), "demand"); break;
            case "network": Heading("Mapa da rede", "Explore o território da rede de saúde."); ContentPanel.Children.Add(new NetworkMapView()); break;
            case "indicators": ContentPanel.Children.Add(new NetworkIndicatorsView()); break;
            case "slots": ContentPanel.Children.Add(new NetworkSlotsView()); break;
            case "capacity": ContentPanel.Children.Add(new CapacityView()); break;
            case "alerts": ContentPanel.Children.Add(new NetworkAlertsView()); break;
            case "reports": ContentPanel.Children.Add(new GoalsView()); break;
            case "communication": Heading("Comunicação", "Comunicados para equipes e pacientes, com leituras."); ContentPanel.Children.Add(new CommunicationView()); break;
            case "users": Heading("Usuários", "Contas, acessos às UBS e histórico de ações."); Add(new StaffAccountsView(), "accounts"); Add(new StaffCreateView(), "create"); Add(new StaffAccessView(), "access"); Add(new HistoryView(), "history"); break;
        }
    }
}
