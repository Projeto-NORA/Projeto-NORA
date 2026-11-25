using System.ComponentModel;
using System.IO;
using System.Text;
using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
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
    private static Border Card(UIElement child, Thickness margin)
    {
        return new Border { Style = (Style)Application.Current.FindResource("Card"), Margin = margin, Child = child };
    }
    private static StackPanel Panel(string title, string description)
    {
        var panel = new StackPanel();
        panel.Children.Add(Text(title, 18, true));
        var detail = Text(description, 13, false, true); detail.Margin = new Thickness(0, 8, 0, 0); panel.Children.Add(detail);
        return panel;
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
    private void Metrics(params (string Title, string? Binding, string Hint)[] items)
    {
        var grid = new UniformGrid { Columns = 3, Margin = new Thickness(-8, 0, -8, 24) };
        foreach (var (title, binding, hint) in items)
        {
            var panel = new StackPanel(); panel.Children.Add(Text(title, 13, true));
            var value = Text("—", 29, true); value.Margin = new Thickness(0, 10, 0, 4);
            if (binding != null) value.SetBinding(TextBlock.TextProperty, new Binding(binding));
            panel.Children.Add(value); panel.Children.Add(Text(hint, 11, false, true));
            panel.Margin = new Thickness(0, 0, 24, 0);
            var chart = new Button { Style = (Style)Application.Current.FindResource("ChartButton"), Command = ChartCommands.Show, CommandParameter = title };
            var layout = new Grid(); layout.Children.Add(panel); layout.Children.Add(chart);
            var card = Card(layout, new Thickness(8)); card.ToolTip = hint; grid.Children.Add(card);
        }
        ContentPanel.Children.Add(grid);
    }
    /// <summary>Parte da página que aparece só na subaba indicada (barra lateral em dois níveis).</summary>
    private void Add(FrameworkElement part, string subPage) { var host = new ContentControl { Content = part, Focusable = false }; SubPage.SetKey(host, subPage); ContentPanel.Children.Add(host); }
    private void Box(string title, string description) => ContentPanel.Children.Add(Card(Panel(title, description), new Thickness(0, 0, 0, 18)));
    private void ExportSummary(object sender, RoutedEventArgs e)
    {
        if (workspace?.CanExportSummary != true) return;
        var dialog = new SaveFileDialog { Title = "Exportar resumo agregado", Filter = "Arquivo CSV (*.csv)|*.csv", DefaultExt = ".csv", FileName = "nora-resumo-rede.csv", AddExtension = true };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true || workspace?.CanExportSummary != true) return;
        try
        {
            File.WriteAllText(dialog.FileName, workspace.CreateSummaryCsv(), new UTF8Encoding(true));
            Nora.Desktop.Services.DialogService.Success(Window.GetWindow(this), "Resumo exportado", "O arquivo CSV foi salvo no local escolhido.");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Nora.Desktop.Services.DialogService.Error(Window.GetWindow(this), "Exportação não concluída", "Não foi possível salvar o arquivo. Confira o local escolhido e tente de novo.");
        }
    }
    private void Render()
    {
        ContentPanel.Children.Clear(); if (workspace == null) return;
        if (!workspace.IsAuthenticated) Notice("Entre na Visão geral para ver os dados da rede.");
        switch (workspace.Section)
        {
            case "home":
                var hero = Panel("Toda a rede em um só lugar", "Acompanhe o acesso e identifique onde a rede precisa de atenção.");
                foreach (TextBlock t in hero.Children) t.SetResourceReference(TextBlock.ForegroundProperty, "BrandTextBrush");
                var banner = new Border { Child = hero, CornerRadius = new CornerRadius(22), Padding = new Thickness(28), Margin = new Thickness(0, 0, 0, 24) }; banner.SetResourceReference(Border.BackgroundProperty, "BrandGradientBrush"); ContentPanel.Children.Add(banner);
                var refresh = new Button { Content = "Atualizar dados", Style = (Style)Application.Current.FindResource("IconRefreshButton"), ToolTip = "Atualizar dados", HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 0, 18) }; System.Windows.Automation.AutomationProperties.SetName(refresh, "Atualizar dados"); refresh.SetBinding(Button.CommandProperty, new Binding(nameof(WorkspaceViewModel.RefreshCommand))); ContentPanel.Children.Add(refresh);
                Heading("Panorama da rede", "Totais atuais da rede. Consulte a aba Indicadores para análise do período.");
                ContentPanel.Children.Add(new NetworkOverviewView()); break;
            case "units": Heading("Unidades de saúde", "Cadastro, base e desempenho das UBS."); Add(new HealthUnitsView(), "registry"); Add(new NetworkUnitsPerformanceView(), "performance"); Add(new UnitRegistryView(), "reference"); break;
            case "specialties": Heading("Especialidades", "Lista da rede e demanda por especialidade."); Add(new SpecialtyCatalogView(), "catalog"); Add(new NetworkSpecialtiesView(), "demand"); break;
            case "network": Heading("Mapa da rede", "Explore o território da rede de saúde."); ContentPanel.Children.Add(new NetworkMapView()); break;
            case "indicators": Heading("Indicadores da rede", "Acompanhe acesso, produção e evolução ao longo do tempo."); ContentPanel.Children.Add(new NetworkIndicatorsView()); break;
            case "slots": ContentPanel.Children.Add(new MunicipalLightningHeroView()); ContentPanel.Children.Add(new LightningFiltersView()); Metrics(("Vagas abertas", "LightningOpen", "Estado atual das vagas no período"), ("Ofertas aceitas", "LightningAccepted", "Ofertas das vagas no período"), ("Vagas registradas", "LightningRegistered", "Vagas criadas no período"), ("Ofertas criadas", "LightningCreated", "Não mede envio de notificações"), ("Recusadas", "LightningDeclined", "Ofertas das vagas no período"), ("Expiradas", "LightningExpired", "Ofertas das vagas no período"), ("Pendentes", "LightningPending", "Estado atual das ofertas"), ("Canceladas", "LightningCancelled", "Ofertas das vagas no período")); Box("Como ler", "A oferta é automática. Aceite não confirma o atendimento, e ofertas criadas não comprovam a entrega da notificação."); break;
            case "capacity": Heading("Capacidade da rede", "Horários ofertados pela grade, ocupados e ocupação, por UBS e região, e o cadastro de cada UBS em leitura."); ContentPanel.Children.Add(new CapacityView()); break;
            case "alerts": Heading("Central de alertas", "Fila alta, espera longa, faltas, ocupação da grade e metas fora, avaliados pela API em cada UBS."); ContentPanel.Children.Add(new NetworkAlertsView()); break;
            case "reports": Heading("Relatórios e metas", "Consulte resultados, acompanhe metas e exporte dados da rede."); ContentPanel.Children.Add(new LightningFiltersView());
                var report = Panel("Exportar resumo agregado", ""); ((TextBlock)report.Children[1]).SetBinding(TextBlock.TextProperty, new Binding(nameof(WorkspaceViewModel.SummaryExportNote)));
                var received = Text("", 12, false, true); received.Margin = new Thickness(0, 12, 0, 0); received.SetBinding(TextBlock.TextProperty, new Binding(nameof(WorkspaceViewModel.SummaryReceivedLabel))); report.Children.Add(received);
                var export = new Button { Content = "Exportar resumo em CSV", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 16, 0, 0) }; export.SetBinding(IsEnabledProperty, new Binding(nameof(WorkspaceViewModel.CanExportSummary))); export.Click += ExportSummary; report.Children.Add(export);
                ContentPanel.Children.Add(Card(report, new Thickness(0, 0, 0, 18)));
                ContentPanel.Children.Add(new GoalsView()); break;
            case "communication": Heading("Comunicação", "Comunicados para equipes e pacientes, com leituras."); ContentPanel.Children.Add(new CommunicationView()); break;
            case "users": Heading("Usuários", "Contas, acessos às UBS e histórico de ações."); Add(new StaffAccountsView(), "accounts"); Add(new StaffCreateView(), "create"); Add(new StaffAccessView(), "access"); Add(new HistoryView(), "history"); break;
        }
    }
}
