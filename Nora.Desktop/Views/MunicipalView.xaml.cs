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
        var subtitle = Text(description, 13, false, true); subtitle.Margin = new Thickness(0, 7, 0, 22); ContentPanel.Children.Add(subtitle);
    }
    private void Notice(string message)
    {
        var block = Text(message, 13); var border = new Border { CornerRadius = new CornerRadius(12), Padding = new Thickness(16), Margin = new Thickness(0, 0, 0, 20), Child = block };
        border.SetResourceReference(Border.BackgroundProperty, "BrandSoftBrush"); ContentPanel.Children.Add(border);
    }
    private void Filters(bool lightningPeriod = false)
    {
        var panel = new StackPanel(); panel.Children.Add(Text("Filtros da rede", 16, true));
        var row = new UniformGrid { Columns = 4, Margin = new Thickness(0, 12, 0, 0) };
        foreach (var name in new[] { "Período", "UBS", "Região", "Especialidade" })
        {
            var item = new StackPanel { Margin = new Thickness(0, 0, 10, 0) }; item.Children.Add(Text(name, 12, true));
            if (lightningPeriod && name == "Período")
            {
                var selector = new ComboBox { SelectedValuePath = "Tag", Margin = new Thickness(0, 6, 0, 0), ToolTip = "Selecione pela data de criação da vaga.", IsSynchronizedWithCurrentItem = false };
                foreach (var (label, days) in new[] { ("Todo o histórico", 0), ("Últimos 7 dias", 7), ("Últimos 30 dias", 30), ("Últimos 90 dias", 90) }) selector.Items.Add(new ComboBoxItem { Content = label, Tag = days });
                selector.SetBinding(ComboBox.SelectedValueProperty, new Binding(nameof(WorkspaceViewModel.LightningPeriodDays)) { Mode = BindingMode.TwoWay });
                selector.SetBinding(IsEnabledProperty, new Binding(nameof(WorkspaceViewModel.CanFilterLightning)));
                System.Windows.Automation.AutomationProperties.SetName(selector, "Período das vagas");
                item.Children.Add(selector);
            }
            else if (name == "UBS")
            {
                var selector = new ComboBox { DisplayMemberPath = "Name", SelectedValuePath = "Id", Margin = new Thickness(0, 6, 0, 0), IsSynchronizedWithCurrentItem = false };
                selector.SetBinding(ItemsControl.ItemsSourceProperty, new Binding(nameof(WorkspaceViewModel.SummaryUnitOptions)));
                selector.SetBinding(ComboBox.SelectedValueProperty, new Binding(nameof(WorkspaceViewModel.SummaryUnitFilter)) { Mode = BindingMode.TwoWay });
                selector.SetBinding(IsEnabledProperty, new Binding(nameof(WorkspaceViewModel.CanOperate)));
                System.Windows.Automation.AutomationProperties.SetName(selector, "Filtrar resumo por UBS");
                item.Children.Add(selector);
                var load = new Button { Content = "Carregar UBS", Margin = new Thickness(0, 6, 0, 0) };
                load.SetBinding(IsEnabledProperty, new Binding(nameof(WorkspaceViewModel.CanOperate)));
                load.Click += async (_, _) => { if (workspace != null) await workspace.LoadUnitsAsync(); }; item.Children.Add(load);
                var apply = new Button { Content = "Aplicar UBS", Margin = new Thickness(0, 6, 0, 0) };
                apply.SetBinding(IsEnabledProperty, new Binding(nameof(WorkspaceViewModel.CanOperate)));
                apply.Click += async (_, _) => { if (workspace != null) await workspace.ApplySummaryUnitAsync(); }; item.Children.Add(apply);
                var applied = Text("", 11, false, true); applied.SetBinding(TextBlock.TextProperty, new Binding(nameof(WorkspaceViewModel.AppliedSummaryUnitLabel))); item.Children.Add(applied);
            }
            else item.Children.Add(new ComboBox { ItemsSource = new[] { "Indisponível" }, SelectedIndex = 0, IsEnabled = false, Margin = new Thickness(0, 6, 0, 0), ToolTip = "A API ainda não fornece este filtro." });
            row.Children.Add(item);
        }
        panel.Children.Add(row); var note = Text(lightningPeriod ? "O período considera a criação da vaga; os estados são os atuais. Região e especialidade aguardam integração." : "Selecione e aplique uma UBS para filtrar os totais disponíveis. Região, período e especialidade aguardam integração.", 12, false, true); note.Margin = new Thickness(0, 12, 0, 0); panel.Children.Add(note);
        ContentPanel.Children.Add(Card(panel, new Thickness(0, 0, 0, 20)));
    }
    private void Metrics(params (string Title, string? Binding, string Hint)[] items)
    {
        var grid = new UniformGrid { Columns = 3, Margin = new Thickness(-6, 0, -6, 18) };
        foreach (var (title, binding, hint) in items)
        {
            var panel = new StackPanel(); panel.Children.Add(Text(title, 13, true));
            var value = Text("—", 29, true); value.Margin = new Thickness(0, 10, 0, 4); value.SetResourceReference(TextBlock.ForegroundProperty, "BrandBrush");
            if (binding != null) value.SetBinding(TextBlock.TextProperty, new Binding(binding));
            panel.Children.Add(value); panel.Children.Add(Text(hint, 11, false, true)); grid.Children.Add(Card(panel, new Thickness(6)));
        }
        ContentPanel.Children.Add(grid);
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
        if (!workspace.IsAuthenticated) Notice("Entre na Visão geral para consultar os dados agregados disponíveis. As demais áreas ainda dependem de integração.");
        switch (workspace.Section)
        {
            case "home":
                var hero = Panel("Toda a rede em um só lugar", "Acompanhe o acesso e identifique onde a rede precisa de atenção.");
                foreach (TextBlock t in hero.Children) t.SetResourceReference(TextBlock.ForegroundProperty, "BrandTextBrush");
                var banner = new Border { Child = hero, CornerRadius = new CornerRadius(22), Padding = new Thickness(28), Margin = new Thickness(0, 0, 0, 24) }; banner.SetResourceReference(Border.BackgroundProperty, "BrandGradientBrush"); ContentPanel.Children.Add(banner);
                var refresh = new Button { Content = "Atualizar dados", HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 0, 18) }; refresh.SetBinding(Button.CommandProperty, new Binding(nameof(WorkspaceViewModel.RefreshCommand))); ContentPanel.Children.Add(refresh);
                Filters(); Heading("Panorama da rede", "Indicadores disponíveis e informações aguardando integração.");
                Metrics(("Pacientes aguardando", "WaitingCount", "Fila de ofertas · total atual"), ("Atendimentos", null, "Agenda ainda não integrada"), ("Tempo médio de espera", null, "Aguardando atendimentos"), ("Faltas", null, "Aguardando agenda"), ("Comparecimento", null, "Aguardando agenda"), ("Cancelamentos", null, "Aguardando agenda"), ("Ocupação das UBS", null, "Aguardando capacidade"), ("Vagas reaproveitadas", null, "Confirmação clínica indisponível"), ("Ofertas aceitas", "AcceptedCount", "Total histórico de ofertas"));
                Box("Evolução dos indicadores", "As séries históricas aparecerão quando a API disponibilizar dados por período.");
                Box("Atenção na rede", "Alertas de sobrecarga, absenteísmo e filas aguardam regras e dados das unidades."); break;
            case "units": Heading("Unidades de saúde", "Cadastre e encontre as UBS da rede."); ContentPanel.Children.Add(new HealthUnitsView()); Box("Desempenho por unidade", "Os indicadores comparativos aguardam integração dos dados assistenciais."); break;
            case "specialties": Heading("Especialidades", "Demanda, tamanho da fila e tempo de espera por especialidade."); Filters(); Box("Demanda por especialidade", "Aguardando especialidades e dados de agenda e fila da rede."); break;
            case "network": Heading("Mapa da rede", "Explore o território da rede de saúde."); ContentPanel.Children.Add(new NetworkMapView()); break;
            case "indicators": Heading("Indicadores da rede", "Acompanhe acesso, produção e evolução ao longo do tempo."); Filters(); Metrics(("Atendimentos", null, "Aguardando agenda"), ("Espera média", null, "Aguardando atendimentos"), ("Faltas", null, "Aguardando agenda"), ("Comparecimento", null, "Aguardando agenda"), ("Cancelamentos", null, "Aguardando agenda"), ("Ocupação", null, "Aguardando capacidade")); Box("Evolução no tempo", "A API ainda não fornece séries históricas para construir os gráficos."); break;
            case "slots": Heading("Vaga Relâmpago", "Acompanhe as oportunidades e o reaproveitamento na rede."); Filters(true); Metrics(("Vagas abertas", "LightningOpen", "Estado atual das vagas no período"), ("Ofertas aceitas", "LightningAccepted", "Ofertas das vagas no período"), ("Vagas registradas", "LightningRegistered", "Vagas criadas no período"), ("Ofertas criadas", "LightningCreated", "Não mede envio de notificações"), ("Recusadas", "LightningDeclined", "Ofertas das vagas no período"), ("Expiradas", "LightningExpired", "Ofertas das vagas no período"), ("Pendentes", "LightningPending", "Estado atual das ofertas"), ("Canceladas", "LightningCancelled", "Ofertas das vagas no período"), ("Confirmadas", null, "Confirmação clínica indisponível"), ("Taxa de reaproveitamento", null, "Aguardando definição e dados")); Box("Acompanhamento", "Aceite da oferta não equivale a atendimento confirmado. A contagem de ofertas criadas não comprova entrega de notificações. Confirmação clínica e taxa de reaproveitamento ainda não são calculadas."); break;
            case "capacity": Heading("Capacidade da rede", "Acompanhe uso das agendas e horários ociosos."); Filters(); Metrics(("Capacidade", null, "Aguardando agendas"), ("Uso das agendas", null, "Aguardando agendas"), ("Horários ociosos", null, "Aguardando agendas")); Box("Capacidade por unidade", "A API ainda não disponibiliza horários e capacidade das UBS."); break;
            case "alerts": Heading("Central de alertas", "Identifique situações que precisam de atenção na rede."); Filters(); Box("Sobrecarga das UBS", "Aguardando capacidade e regras de avaliação."); Box("Absenteísmo", "Aguardando registros de presença e critérios de aumento."); Box("Fila por especialidade", "Aguardando séries e critérios de comparação."); break;
            case "reports": Heading("Relatórios e metas", "Consulte resultados, acompanhe metas e exporte dados da rede."); Filters(true);
                var report = Panel("Exportar resumo agregado", "Baixe em CSV os totais carregados da API. O arquivo contém dados agregados e não inclui pacientes identificáveis; O período filtra somente as vagas pela data de criação; pacientes e fila permanecem totais atuais da rede. UBS, região e especialidade não são filtrados.");
                var received = Text("", 12, false, true); received.Margin = new Thickness(0, 12, 0, 0); received.SetBinding(TextBlock.TextProperty, new Binding(nameof(WorkspaceViewModel.SummaryReceivedLabel))); report.Children.Add(received);
                var export = new Button { Content = "Exportar resumo em CSV", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 16, 0, 0) }; export.SetBinding(IsEnabledProperty, new Binding(nameof(WorkspaceViewModel.CanExportSummary))); export.Click += ExportSummary; report.Children.Add(export);
                ContentPanel.Children.Add(Card(report, new Thickness(0, 0, 0, 18)));
                Box("Metas", "O cadastro, acompanhamento e as permissões de metas ainda não estão disponíveis."); break;
            case "communication": Heading("Comunicação com as UBS", "Acompanhe a comunicação entre a Secretaria e as unidades."); Box("Mensagens", "O envio e o histórico de mensagens aguardam integração com as unidades."); break;
            case "users": Heading("Usuários e histórico", "Administre acessos e acompanhe as ações realizadas."); ContentPanel.Children.Add(new StaffAccessView()); Box("Histórico de ações", "Concessões e revogações são registradas no servidor; a consulta do histórico ainda não está disponível nesta tela."); break;
        }
    }
}
