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

/// <summary>Moldura das seções da Secretaria. Cada seção com dados reais é um UserControl próprio; aqui ficam só os títulos e os avisos das seções ainda sem dados.</summary>
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
    private void Heading(string title, string? description = null)
    {
        var heading = Text(title, 28, true);
        ContentPanel.Children.Add(heading);
        if (description is null) { heading.Margin = new Thickness(0, 0, 0, 22); return; }
        var subtitle = Text(description, 13, false, true); subtitle.Margin = new Thickness(0, 7, 0, 22); ContentPanel.Children.Add(subtitle);
    }
    private void Notice(string message)
    {
        var block = Text(message, 13); var border = new Border { CornerRadius = new CornerRadius(12), Padding = new Thickness(16), Margin = new Thickness(0, 0, 0, 20), Child = block };
        border.SetResourceReference(Border.BackgroundProperty, "BrandSoftBrush"); ContentPanel.Children.Add(border);
    }
    private void Metrics(string? group, params (string Title, string? Binding, string Hint)[] items)
    {
        if (group != null) { var heading = Text(group, 16, true); heading.Margin = new Thickness(0, 4, 0, 10); ContentPanel.Children.Add(heading); }
        var grid = new UniformGrid { Columns = 3, Margin = new Thickness(-6, 0, -6, 18) };
        foreach (var (title, binding, hint) in items)
        {
            var panel = new StackPanel(); panel.Children.Add(Text(title, 13, true));
            var value = new TextBlock { Text = "—", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 10, 0, 4), Style = UnavailableAwareValueStyle() };
            if (binding != null) value.SetBinding(TextBlock.TextProperty, new Binding(binding));
            panel.Children.Add(value); panel.Children.Add(Text(hint, 11, false, true));
            var reason = Text("O servidor ainda não informa este número.", 11, false, true); reason.Style = HiddenUnlessUnavailableStyle(value); panel.Children.Add(reason);
            grid.Children.Add(Card(panel, new Thickness(6)));
        }
        ContentPanel.Children.Add(grid);
    }
    /// <summary>Número grande; quando o dado é "Indisponível" vira texto pequeno e cinza, para não parecer um resultado.</summary>
    private static Style UnavailableAwareValueStyle()
    {
        var style = new Style(typeof(TextBlock));
        style.Setters.Add(new Setter(TextBlock.FontSizeProperty, 29d));
        style.Setters.Add(new Setter(TextBlock.ForegroundProperty, new DynamicResourceExtension("TextBrush")));
        var trigger = new DataTrigger { Binding = new Binding(nameof(TextBlock.Text)) { RelativeSource = RelativeSource.Self }, Value = "Indisponível" };
        trigger.Setters.Add(new Setter(TextBlock.FontSizeProperty, 14d));
        trigger.Setters.Add(new Setter(TextBlock.ForegroundProperty, new DynamicResourceExtension("SecondaryBrush")));
        style.Triggers.Add(trigger);
        return style;
    }
    private static Style HiddenUnlessUnavailableStyle(TextBlock value)
    {
        var style = new Style(typeof(TextBlock));
        style.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Collapsed));
        var trigger = new DataTrigger { Binding = new Binding(nameof(TextBlock.Text)) { Source = value }, Value = "Indisponível" };
        trigger.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Visible));
        style.Triggers.Add(trigger);
        return style;
    }
    private void Box(string title, string description) => ContentPanel.Children.Add(Card(Panel(title, description), new Thickness(0, 0, 0, 18)));
    private void ExportSummary(object sender, RoutedEventArgs e)
    {
        if (workspace?.CanExportSummary != true) return;
        var dialog = new SaveFileDialog { Title = "Exportar resumo agregado", Filter = "Arquivo CSV (*.csv)|*.csv", DefaultExt = ".csv", FileName = "nora-resumo-rede.csv", AddExtension = true };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true || workspace?.CanExportSummary != true) return;
        try
        {
            File.WriteAllText(dialog.FileName, workspace.CreateSummaryCsv(), new UTF8Encoding(true));
            MessageBox.Show(Window.GetWindow(this), "Resumo agregado exportado.", "Exportação", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(Window.GetWindow(this), "Não foi possível salvar o arquivo. Verifique o local escolhido e tente novamente.", "Exportação", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
    private void Render()
    {
        ContentPanel.Children.Clear(); if (workspace == null) return;
        if (!workspace.IsAuthenticated) Notice("Entre na Visão geral para consultar os dados da rede.");
        switch (workspace.Section)
        {
            case "home":
                var hero = Panel("Toda a rede em um só lugar", "Onde a rede precisa de atenção, com totais atuais e indicadores do período.");
                foreach (TextBlock t in hero.Children) t.SetResourceReference(TextBlock.ForegroundProperty, "BrandTextBrush");
                var refresh = new Button { Content = "Atualizar dados", HorizontalAlignment = HorizontalAlignment.Right }; refresh.SetBinding(Button.CommandProperty, new Binding(nameof(WorkspaceViewModel.RefreshCommand)));
                var updated = Text("", 12); updated.Margin = new Thickness(0, 8, 0, 0); updated.HorizontalAlignment = HorizontalAlignment.Right; updated.SetResourceReference(TextBlock.ForegroundProperty, "BrandTextBrush"); updated.SetBinding(TextBlock.TextProperty, new Binding(nameof(WorkspaceViewModel.SummaryUpdatedLabel)));
                var actions = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(20, 0, 0, 0) }; actions.Children.Add(refresh); actions.Children.Add(updated);
                var bannerRow = new Grid(); bannerRow.ColumnDefinitions.Add(new ColumnDefinition()); bannerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                Grid.SetColumn(actions, 1); bannerRow.Children.Add(hero); bannerRow.Children.Add(actions);
                var banner = new Border { Child = bannerRow, CornerRadius = new CornerRadius(22), Padding = new Thickness(28), Margin = new Thickness(0, 0, 0, 24) }; banner.SetResourceReference(Border.BackgroundProperty, "BrandGradientBrush"); ContentPanel.Children.Add(banner);
                ContentPanel.Children.Add(new NetworkOverviewView()); break;
            case "units": Heading("Unidades de saúde"); ContentPanel.Children.Add(new HealthUnitsView()); ContentPanel.Children.Add(new NetworkFiltersView()); ContentPanel.Children.Add(new NetworkUnitsPerformanceView()); ContentPanel.Children.Add(new UnitRegistryView()); break;
            case "specialties": Heading("Especialidades"); ContentPanel.Children.Add(new SpecialtyCatalogView()); ContentPanel.Children.Add(new NetworkFiltersView()); ContentPanel.Children.Add(new NetworkSpecialtiesView()); break;
            case "network": Heading("Mapa da rede"); ContentPanel.Children.Add(new NetworkMapView()); break;
            case "indicators": Heading("Indicadores da rede"); ContentPanel.Children.Add(new NetworkIndicatorsView()); break;
            case "slots": Heading("Vaga Relâmpago"); ContentPanel.Children.Add(new LightningFiltersView());
                Metrics("Agora", ("Vagas abertas", "LightningOpen", "Ainda abertas agora, entre as vagas do período"), ("Pendentes", "LightningPending", "Pendentes agora, entre as vagas do período"), ("Aguardando aprovação", "AwaitingApprovalSlotsCount", "Esperam a equipe agora, sem o filtro de período"));
                Metrics("No período escolhido", ("Vagas registradas", "LightningRegistered", "Vagas criadas no período"), ("Ofertas criadas", "LightningCreated", "Não mede envio de notificações"), ("Ofertas aceitas", "LightningAccepted", "Ofertas das vagas no período"), ("Recusadas", "LightningDeclined", "Ofertas das vagas no período"), ("Expiradas", "LightningExpired", "Ofertas das vagas no período"), ("Canceladas", "LightningCancelled", "Ofertas das vagas no período"), ("Taxa de reaproveitamento", null, "Aguardando definição e dados"));
                Metrics("Histórico, sem o filtro de período", ("Aprovadas pela equipe", "ApprovedSlotsCount", "Liberadas pela equipe para oferta"), ("Fechadas sem aprovação", "ApprovalTimeoutSlotsCount", "Faltou menos de 1 h para a consulta sem aprovação"), ("Descartadas pela equipe", "DiscardedSlotsCount", "Fechadas sem serem oferecidas"));
                Box("Acompanhamento", "Aceite da oferta e aprovação da equipe não confirmam o atendimento. Ofertas criadas não comprovam entrega de notificações. A taxa de reaproveitamento ainda não é calculada."); break;
            case "capacity": Heading("Capacidade da rede"); ContentPanel.Children.Add(new CapacityView()); break;
            case "alerts": Heading("Central de alertas", "Fila alta, espera longa, faltas, ocupação da grade e metas fora, por UBS."); ContentPanel.Children.Add(new NetworkAlertsView()); break;
            case "reports": Heading("Relatórios e metas"); ContentPanel.Children.Add(new GoalsView()); ContentPanel.Children.Add(new LightningFiltersView { Title = "Filtros do resumo exportado" });
                var report = Panel("Exportar resumo agregado", ""); ((TextBlock)report.Children[1]).SetBinding(TextBlock.TextProperty, new Binding(nameof(WorkspaceViewModel.SummaryExportNote)));
                var received = Text("", 12, false, true); received.Margin = new Thickness(0, 12, 0, 0); received.SetBinding(TextBlock.TextProperty, new Binding(nameof(WorkspaceViewModel.SummaryReceivedLabel))); report.Children.Add(received);
                var export = new Button { Content = "Exportar resumo em CSV", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 16, 0, 0) }; export.SetBinding(IsEnabledProperty, new Binding(nameof(WorkspaceViewModel.CanExportSummary))); export.Click += ExportSummary; report.Children.Add(export);
                ContentPanel.Children.Add(Card(report, new Thickness(0, 0, 0, 18))); break;
            case "communication": Heading("Comunicação"); ContentPanel.Children.Add(new CommunicationView()); break;
            case "users": Heading("Usuários e histórico"); ContentPanel.Children.Add(new StaffAccountsView()); ContentPanel.Children.Add(new StaffAccessView()); ContentPanel.Children.Add(new Expander { Header = "Nova conta", Content = new StaffCreateView(), Margin = new Thickness(0, 0, 0, 18) }); ContentPanel.Children.Add(new HistoryView()); break;
        }
    }
}
