using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Nora.Desktop.Models;
using Nora.Desktop.Services;
using Nora.Desktop.ViewModels;
using Nora.Desktop.Views;

namespace Nora.Desktop.Checks;

/// <summary>
/// Painel Municipal, telas de números (Visão geral, Indicadores, Vaga Relâmpago, Alertas, Relatórios e metas, Capacidade, Desempenho das unidades e Demanda) no
/// desenho novo: cabeçalho com pesquisa, período e alternador, cartões coloridos, gráficos em grade, listagens abertas com balão e ficha, sem botão de atualizar.
/// Com <c>--preview-dir</c> as checagens gravam as capturas (claro e escuro, com dados, vazio, balão, pop-up e ficha).
/// </summary>
internal static partial class Program
{
    /// <summary>A dica do botão "Exportar CSV" do cabeçalho (o texto que diz o escopo e o conteúdo do resumo exportado).</summary>
    private static string? ExportTip(FrameworkElement content) => Descendants<Button>(content).Single(button => AutomationProperties.GetName(button) == "Exportar resumo em CSV").ToolTip as string;

    private static bool GridsVisible(FrameworkElement content) => Descendants<DataGrid>(content).Any(grid => grid.IsVisible);

    private static bool AnyRefreshButton(FrameworkElement content) => Descendants<Button>(content).Any(button => button.IsVisible
        && ((button.Content as string ?? "").StartsWith("Atualizar", StringComparison.Ordinal) || AutomationProperties.GetName(button).StartsWith("Atualizar", StringComparison.Ordinal)));

    /// <summary>Uma tabela solta na tela: nenhum Border do estilo Card (fundo em degradê) fica em volta dela.</summary>
    private static bool GridInsideCard(DataGrid grid)
    {
        for (DependencyObject? node = System.Windows.Media.VisualTreeHelper.GetParent(grid); node is not null; node = System.Windows.Media.VisualTreeHelper.GetParent(node))
            if (node is Border { CornerRadius.TopLeft: 16, Effect: not null } or Border { CornerRadius.TopLeft: 16, BorderThickness.Left: 1, Padding.Left: 24 }) return true;
        return false;
    }

    /// <summary>Captura o pop-up (o conteúdo da própria janela, que é o cartão; a janela dona não o mostra).</summary>
    private static void SaveDialog(Window dialog, string path)
    {
        var content = (FrameworkElement)dialog.Content; PumpUntil(() => content.Opacity >= 0.99); dialog.UpdateLayout();
        dialog.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle); content.UpdateLayout();
        // A moldura do pop-up tem margem (sombra): captura a raiz da janela para não cortar a borda direita e a de baixo.
        SavePreview(System.Windows.Media.VisualTreeHelper.GetChild(dialog, 0) is FrameworkElement root ? root : content, path);
    }

    private static string Snap(string root, string name, ShellWindow window, double width = 1280, double height = 760)
    {
        var path = Path.Combine(root, name + ".png");
        SavePreview(Layout(window, width, height), path);
        return path;
    }

    private static void RunMunicipalPanelsChecks(Application application, string storageRoot, string[] args)
    {
        var previewRoot = PreviewRoot(args);
        Check("Painel Municipal (painéis): Visão geral sem banner nem botão Atualizar; cabeçalho com pesquisa, Período em linha e alternador; cartões coloridos", () =>
        {
            using var fixture = new ApiFixture();
            fixture.AddUnit("UBS Alfa", "Centro"); fixture.AddUnit("UBS Beta", "Sul");
            var window = OpenMunicipal(fixture, storageRoot, "panels-home", Appearance.Light);
            try
            {
                var vm = LoginObs(window);
                var content = Show(window, "home", 1280, 900);
                Require(!HasText(content, "Toda a rede em um só lugar") && !HasText(content, "Panorama da rede") && !AnyRefreshButton(content), "A Visão geral não tem banner, título repetido nem botão de atualizar.");
                var search = Descendants<TextBox>(content).Single(box => AutomationProperties.GetName(box) == "Pesquisar: Visão geral");
                var period = Combo(content, "Período dos indicadores");
                Require(period.Items.Cast<PeriodOption>().Select(option => option.Label).SequenceEqual(["Dia", "Semana", "Mês", "Ano", "5 anos", "Máximo", "Personalizado"]), "O Período deveria ter Dia, Semana, Mês, Ano, 5 anos e Máximo (mais Personalizado, escondido onde não há datas).");
                period.IsDropDownOpen = true; period.UpdateLayout();
                var custom = (ComboBoxItem)period.ItemContainerGenerator.ContainerFromIndex(6); var monthItem = (ComboBoxItem)period.ItemContainerGenerator.ContainerFromIndex(2);
                Require(custom.Visibility == Visibility.Collapsed && monthItem.Visibility == Visibility.Visible, "Na Visão geral o item Personalizado fica escondido (as datas são do Indicadores).");
                period.IsDropDownOpen = false;
                Require(Descendants<ViewModeToggle>(content).Count(button => button.IsVisible) == 1 && Descendants<Button>(content).Count(button => button.IsVisible && AutomationProperties.GetName(button).StartsWith("Alternar visualização", StringComparison.Ordinal)) == 1, "O alternador cards / gráficos deveria estar no cabeçalho.");
                var order = new[] { search, period, (FrameworkElement)Descendants<ViewModeToggle>(content).Single(button => button.IsVisible) }.Select(item => item.TranslatePoint(new Point(0, 0), content).X).ToArray();
                Require(order[0] < order[1] && order[1] < order[2], "No cabeçalho vêm a pesquisa, o Período e o alternador, nessa ordem, da esquerda para a direita.");
                Require(vm.SummaryCards.Select(card => card.Tone).SequenceEqual(new[] { Tone.Blue, Tone.Neutral, Tone.Teal, Tone.Green }) && vm.OverviewCards.Select(card => card.Tone).SequenceEqual(new[] { Tone.Green, Tone.Pink, Tone.Yellow, Tone.Blue, Tone.Violet, Tone.Violet }), "Os cartões deveriam ter o tom do sentido (concluídas verde, faltas rosa, cancelamentos amarelo, espera violeta).");
                Require(Descendants<DataGrid>(content).Where(grid => grid.IsVisible).All(grid => !GridInsideCard(grid)) && Descendants<DataGrid>(content).Any(grid => grid.IsVisible && AutomationProperties.GetName(grid) == "Comparativo por região"), "A tabela por região fica solta na tela, sem card.");
                Snap(previewRoot, "municipal-visao-geral-cards-claro", window);
                // Pesquisa em linha: filtra os cartões pelo título.
                search.Text = "Faltas"; content = Layout(window, 1280, 900);
                Require(vm.NetworkSearch == "Faltas" && Descendants<ItemsControl>(content).Single(list => AutomationProperties.GetName(list) == "Indicadores do período").Items.Count == 1 && Descendants<ItemsControl>(content).Single(list => AutomationProperties.GetName(list) == "Totais atuais da rede").Items.Count == 0, "A pesquisa deveria filtrar os cartões pelo título.");
                search.Text = "zzzz"; content = Layout(window, 1280, 900);
                Require(HasText(content, "Nenhum resultado para esta busca."), "Sem resultado a tela deveria dizer que nada combina com a busca.");
                Snap(previewRoot, "municipal-visao-geral-busca-vazia-claro", window);
                search.Text = ""; content = Layout(window, 1280, 900);
                Require(Descendants<ItemsControl>(content).Single(list => AutomationProperties.GetName(list) == "Indicadores do período").Items.Count == 6 && !HasText(content, "Nenhum resultado para esta busca."), "Apagar a busca devolve os cartões.");
                // A pesquisa é de cada tela: o que foi digitado na Visão geral não filtra Indicadores e volta ao retornar.
                search.Text = "Faltas"; content = Show(window, "indicators", 1280, 900);
                Require(vm.NetworkSearch == "" && Descendants<TextBox>(content).Single(box => AutomationProperties.GetName(box) == "Pesquisar: Indicadores").Text == "" && Descendants<ItemsControl>(content).Single(list => AutomationProperties.GetName(list) == "Indicadores do período").Items.Count == 6, "A pesquisa da Visão geral não deveria filtrar os Indicadores.");
                content = Show(window, "home", 1280, 900); search = Descendants<TextBox>(content).Single(box => AutomationProperties.GetName(box) == "Pesquisar: Visão geral");
                Require(vm.NetworkSearch == "Faltas" && search.Text == "Faltas" && Descendants<ItemsControl>(content).Single(list => AutomationProperties.GetName(list) == "Indicadores do período").Items.Count == 1, "Ao voltar à Visão geral a pesquisa dela volta.");
                search.Text = ""; content = Layout(window, 1280, 900);
                // Modo gráficos: grade de gráficos, sem tabelas; a escolha fica lembrada.
                var toggle = Descendants<ViewModeToggle>(content).Single(button => button.IsVisible); InvokeButton(toggle); content = Layout(window, 1280, 900);
                Require(toggle.Mode == ViewMode.Charts && window.ViewModel.GetViewMode("home") == ViewMode.Charts && !GridsVisible(content) && !HasText(content, "Rede agora"), "O modo gráficos mostra só os gráficos, sem tabelas nem cartões.");
                var panels = Descendants<ChartPanel>(content).Where(panel => panel.IsVisible).ToArray();
                Require(panels.Select(panel => panel.Title).SequenceEqual(vm.NetworkCharts.Select(chart => chart.Title)) && vm.NetworkCharts[0].Title == "Consultas por situação" && panels.Length >= 5, "A grade deveria ter um gráfico por indicador com dado: " + string.Join(", ", panels.Select(panel => panel.Title)));
                var situation = panels.Single(panel => panel.Title == "Consultas por situação");
                Require(situation.Toggle.IsVisible && situation.HasLine && situation.HasPie && !panels.Single(panel => panel.Title == "Comparecimento").Toggle.IsVisible, "O alternador Linha | Pizza aparece só onde há os dois tipos de dado.");
                Snap(previewRoot, "municipal-visao-geral-graficos-claro", window, 1280, 1100);
                window.ViewModel.SetAppearance(Appearance.Dark); Snap(previewRoot, "municipal-visao-geral-graficos-escuro", window, 1280, 1100);
                InvokeButton(toggle); Layout(window, 1280, 900); Snap(previewRoot, "municipal-visao-geral-cards-escuro", window);
                window.ViewModel.SetAppearance(Appearance.Light);
                // Estado vazio: sem consulta encerrada o cartão explica e o gráfico das consultas diz o motivo (em vez de um desenho vazio).
                fixture.NoClosedAppointments = true; vm.MetricsPeriodDays = 7; Settle(vm); content = Layout(window, 1280, 900);
                Require(HasText(content, "Sem consulta encerrada no período para calcular."), "Sem consulta encerrada o cartão do comparecimento explica o motivo.");
                Snap(previewRoot, "municipal-visao-geral-cards-vazio-claro", window);
                InvokeButton(toggle); content = Layout(window, 1280, 900);
                Require(HasText(content, "Nenhuma consulta com data marcada no período."), "O gráfico das consultas, sem dados, explica o motivo.");
                Snap(previewRoot, "municipal-visao-geral-graficos-vazio-claro", window);
            }
            finally { CloseWindow(window); }
        });

        Check("Painel Municipal (painéis): Período de Dia a Máximo e período personalizado de até 20 anos; a lista de consultas continua em 366 dias", () =>
        {
            using var fixture = new ApiFixture();
            fixture.AddUnit("UBS Alfa", "Centro");
            var window = OpenMunicipal(fixture, storageRoot, "panels-period", Appearance.Light);
            try
            {
                var vm = LoginObs(window);
                var content = Show(window, "indicators", 1280, 900);
                static double Days(System.Collections.Specialized.NameValueCollection query) => (DateTimeOffset.Parse(query["to"]!, CultureInfo.InvariantCulture) - DateTimeOffset.Parse(query["from"]!, CultureInfo.InvariantCulture)).TotalDays;
                foreach (var (label, days) in new[] { ("Dia", 1), ("Semana", 7), ("Mês", 30), ("Ano", 365), ("5 anos", 1826), ("Máximo", 7305) })
                {
                    var option = WorkspaceViewModel.PeriodOptions.Single(item => item.Label == label); Require(option.Days == days, $"{label} deveria valer {days} dias.");
                    Combo(content, "Período dos indicadores").SelectedValue = days; Settle(vm);
                    Require(Days(fixture.MetricsQueries[^1]) == days && vm.PeriodSummaryTitle.StartsWith("Resumo", StringComparison.Ordinal), $"{label}: a API deveria receber {days} dias.");
                }
                Require(vm.PeriodSummaryTitle == "Resumo de todo o período", "O título do resumo acompanha o período.");
                // Personalizado: 20 anos exatos é aceito; 20 anos e um dia, não.
                vm.MetricsPeriodDays = 0; vm.MetricsFromText = "01/01/2007"; vm.MetricsToText = "31/12/2026"; var reads = fixture.MetricsQueries.Count; vm.ApplyCustomPeriod(); Settle(vm);
                Require(vm.MetricsRangeError == "" && fixture.MetricsQueries.Count == reads + 1 && Days(fixture.MetricsQueries[^1]) == 7305, "Vinte anos (7.305 dias) deveriam ser aceitos: " + vm.MetricsRangeError);
                vm.MetricsFromText = "31/12/2006"; reads = fixture.MetricsQueries.Count; vm.ApplyCustomPeriod(); Settle(vm);
                Require(vm.MetricsRangeError == "O período pode ter no máximo 20 anos (7.305 dias)." && fixture.MetricsQueries.Count == reads, "Mais de 20 anos deveria ser barrado, sem requisição: " + vm.MetricsRangeError);
                // Na tela de Indicadores o Personalizado aparece; na Visão geral continua escondido, a menos que o período personalizado esteja valendo.
                var period = Combo(content, "Período dos indicadores"); period.IsDropDownOpen = true; period.UpdateLayout();
                Require(((ComboBoxItem)period.ItemContainerGenerator.ContainerFromIndex(6)).Visibility == Visibility.Visible, "Em Indicadores o Personalizado é uma opção."); period.IsDropDownOpen = false;
                Require(Descendants<Button>(OpenPageFilters(window)).Any(button => button.Content as string == "Aplicar período"), "As datas e o Aplicar período ficam no botão Filtros.");
                ((System.Windows.Controls.Primitives.Popup)window.FindName("FiltersPopup")).IsOpen = false;
                // A lista de consultas de uma UBS não aceita mais que 366 dias.
                vm.MetricsPeriodDays = 30;
                Require(WorkspaceViewModel.ValidateDateRange("01/01/2026", "02/01/2027", out _, out _, WorkspaceViewModel.MaxListRangeDays) == "O período pode ter no máximo 366 dias." && WorkspaceViewModel.ValidateDateRange("01/01/2026", "01/01/2027", out _, out _, WorkspaceViewModel.MaxListRangeDays) == "", "As listas de consultas continuam limitadas a 366 dias.");
            }
            finally { CloseWindow(window); }
        });

        Check("Painel Municipal (painéis): Indicadores com cartões e tabelas, gráficos em grade (Linha | Pizza) e a pesquisa filtrando os gráficos pelo título", () =>
        {
            using var fixture = new ApiFixture();
            fixture.AddUnit("UBS Alfa", "Centro"); fixture.AddUnit("UBS Beta", "Sul");
            var window = OpenMunicipal(fixture, storageRoot, "panels-indicators", Appearance.Light);
            try
            {
                var vm = LoginObs(window);
                var content = Show(window, "indicators", 1280, 1000);
                Require(Descendants<ViewModeToggle>(content).Any(button => button.IsVisible) && !AnyRefreshButton(content) && HasText(content, vm.PeriodSummaryTitle), "Indicadores tem o alternador no cabeçalho e não tem botão de atualizar.");
                Require(vm.OverviewCards.All(card => card.Tone != Tone.Neutral) && Descendants<ItemsControl>(content).Single(list => AutomationProperties.GetName(list) == "Indicadores do período").Items.Count == 6, "Os seis cartões do período vêm coloridos.");
                // A leitura em andamento mostra o indicador girando na cor da marca com o passo escrito; parada, nada aparece.
                Require(Descendants<LoadingStatus>(content).Any() && Descendants<LoadingStatus>(content).All(status => !status.IsVisible), "Sem leitura em andamento o indicador fica escondido.");
                var loading = new LoadingStatus { Text = "Atualizando os indicadores…" }; loading.Measure(new Size(400, 40)); loading.Arrange(new Rect(0, 0, 400, 40));
                Require(loading.Visibility == Visibility.Visible && Descendants<TextBlock>(loading).Any(text => text.Text == "Atualizando os indicadores…") && AutomationProperties.GetName(loading) == "Atualizando os indicadores…"
                    && Descendants<System.Windows.Shapes.Path>(loading).Single().Stroke == Application.Current.FindResource("BrandBrush"), "O indicador de leitura mostra o passo escrito e gira na cor da marca.");
                loading.Text = ""; Require(loading.Visibility == Visibility.Collapsed, "Terminada a leitura o indicador some.");
                Snap(previewRoot, "municipal-indicadores-cards-claro", window, 1280, 1000);
                var toggle = Descendants<ViewModeToggle>(content).Single(button => button.IsVisible); InvokeButton(toggle); content = Layout(window, 1280, 1000);
                var search = Descendants<TextBox>(content).Single(box => AutomationProperties.GetName(box) == "Pesquisar: Indicadores");
                search.Text = "região"; content = Layout(window, 1280, 1000);
                var shown = Descendants<ChartPanel>(content).Where(panel => panel.IsVisible).Select(panel => panel.Title).ToArray();
                Require(shown.Length == 1 && shown[0] == "Consultas por região", "A pesquisa deveria filtrar os gráficos pelo título: " + string.Join(", ", shown));
                search.Text = ""; content = Layout(window, 1280, 1000);
                var pie = Descendants<ChartPanel>(content).First(panel => panel.IsVisible && panel.Title == "Consultas por situação");
                pie.Kind = ChartKind.Pie; content = Layout(window, 1280, 1000);
                Require(pie.Toggle.SelectedIndex == 1 && Descendants<OutcomePie>(pie).Any(item => item.IsVisible), "Escolher Pizza troca o gráfico da grade.");
                Snap(previewRoot, "municipal-indicadores-graficos-claro", window, 1280, 1100);
                window.ViewModel.SetAppearance(Appearance.Dark); Snap(previewRoot, "municipal-indicadores-graficos-escuro", window, 1280, 1100);
                InvokeButton(toggle); Snap(previewRoot, "municipal-indicadores-cards-escuro", window, 1280, 1000);
                // O pop-up do gráfico do cartão só tem o título e o alternador (sem "Valor atual").
                var card = vm.OverviewCards.First();
                var chart = vm.BuildIndicatorChart(card.Title);
                var popup = new IndicatorChartWindow(chart) { Owner = window, WindowStartupLocation = WindowStartupLocation.Manual, Left = -10000, Top = -10000, ShowActivated = false };
                try { popup.Show(); popup.UpdateLayout(); Require(!Descendants<TextBlock>(popup).Any(text => (text.Text ?? "").Contains("Valor atual", StringComparison.Ordinal)) && Descendants<TextBlock>(popup).Any(text => text.Text == card.Title), "O pop-up do cartão da Secretaria não mostra Valor atual."); }
                finally { popup.Close(); }
            }
            finally { CloseWindow(window); }
        });

        Check("Painel Municipal (painéis): Desempenho das unidades, Demanda e Capacidade têm tabelas soltas, sem título repetido, sem Atualizar, e a pesquisa filtra as linhas", () =>
        {
            using var fixture = new ApiFixture();
            var alfa = fixture.AddUnit("UBS Alfa", "Centro"); fixture.AddUnit("UBS Beta", "Sul");
            fixture.SetCapacity(alfa.Id, new CapacityMetrics(40, 30, 0.75));
            var window = OpenMunicipal(fixture, storageRoot, "panels-tables", Appearance.Light);
            try
            {
                var vm = LoginObs(window);
                var content = Show(window, "units:performance", 1280, 800);
                var units = GridNamed(content, "Desempenho por unidade");
                Require(!GridInsideCard(units) && !HasText(content, "Desempenho por unidade") && !HasText(content, "Unidades de saúde") && !HasText(content, "Cadastro, base e desempenho das UBS.") && !AnyRefreshButton(content) && units.Items.Count == 2, "Desempenho das unidades: tabela solta, sem título repetido nem botão de atualizar.");
                var search = Descendants<TextBox>(content).Single(box => AutomationProperties.GetName(box) == "Pesquisar: Desempenho das unidades");
                search.Text = "beta"; content = Layout(window, 1280, 800);
                Require(units.Items.Count == 1 && ((PerformanceRow)units.Items[0]).Name == "UBS Beta", "A pesquisa deveria filtrar as unidades (sem diferenciar maiúsculas).");
                Snap(previewRoot, "municipal-desempenho-unidades-claro", window);
                search.Text = "";
                content = Show(window, "specialties:demand", 1280, 800);
                var specialties = GridNamed(content, "Indicadores por especialidade");
                Require(!GridInsideCard(specialties) && !HasText(content, "Demanda por especialidade") && !HasText(content, "Especialidades") && !HasText(content, "Lista da rede e demanda por especialidade.") && !AnyRefreshButton(content), "Demanda: tabela solta, sem título repetido nem botão de atualizar.");
                Snap(previewRoot, "municipal-demanda-claro", window);
                content = Show(window, "capacity", 1280, 1400);
                Require(Descendants<DataGrid>(content).Where(grid => grid.IsVisible).All(grid => !GridInsideCard(grid)) && !AnyRefreshButton(content) && !HasText(content, "Cadastro de uma UBS (somente leitura)") && !HasText(content, "Capacidade da rede")
                    && vm.NetworkCapacityCards.Select(card => card.Tone).SequenceEqual(new[] { Tone.Blue, Tone.Teal, Tone.Violet }), "Capacidade: cartões coloridos, tabelas soltas e nenhum botão de atualizar.");
                var searchCapacity = Descendants<TextBox>(content).Single(box => AutomationProperties.GetName(box) == "Pesquisar: Capacidade");
                searchCapacity.Text = "Beta"; content = Layout(window, 1280, 1400);
                Require(GridNamed(content, "Capacidade por UBS").Items.Count == 1, "A pesquisa filtra as unidades da capacidade.");
                searchCapacity.Text = ""; content = Layout(window, 1280, 1400);
                Snap(previewRoot, "municipal-capacidade-claro", window, 1280, 1000);
                window.ViewModel.SetAppearance(Appearance.Dark); Snap(previewRoot, "municipal-capacidade-escuro", window, 1280, 1000);
            }
            finally { CloseWindow(window); }
        });

        Check("Painel Municipal (painéis): Vaga Relâmpago da rede com cartões coloridos, filtros no cabeçalho e sem título nem Atualizar", () =>
        {
            using var fixture = new ApiFixture();
            fixture.AddUnit("UBS Alfa", "Centro");
            var window = OpenMunicipal(fixture, storageRoot, "panels-slots", Appearance.Light);
            try
            {
                var vm = LoginObs(window);
                var content = Show(window, "slots", 1280, 800);
                Require(!AnyRefreshButton(content) && vm.NetworkLightningCards.Count == 8 && vm.NetworkLightningCards.Select(card => card.Title).SequenceEqual(["Vagas abertas", "Ofertas aceitas", "Vagas registradas", "Ofertas criadas", "Recusadas", "Expiradas", "Pendentes", "Canceladas"]) && vm.NetworkLightningCards.Count(card => card.Tone != Tone.Neutral) == 7, "Oito cartões de Vaga Relâmpago, a maioria colorida.");
                Require(vm.NetworkLightningCards.ToDictionary(card => card.Title, card => card.Value)["Vagas abertas"] == vm.LightningOpen && vm.NetworkLightningCards.All(card => HasText(content, card.Title)), "Os cartões mostram os números do resumo.");
                // O período da vaga fica em linha no cabeçalho; a UBS fica no pop-up do botão Filtros (não no cabeçalho).
                var header = (FrameworkElement)window.FindName("PageHeader");
                Require(Descendants<ComboBox>(header).Count(box => box.IsVisible && AutomationProperties.GetName(box) == "Período das vagas") == 1 && Combo(content, "Período das vagas").Items.Count == 4
                    && !Descendants<ComboBox>(header).Any(box => box.IsVisible && AutomationProperties.GetName(box) == "Filtrar resumo por UBS"), "O período da vaga deveria estar em linha no cabeçalho, sem a UBS.");
                Require(Descendants<ComboBox>(OpenPageFilters(window)).Any(box => AutomationProperties.GetName(box) == "Filtrar resumo por UBS"), "A UBS deveria estar no botão Filtros.");
                ((System.Windows.Controls.Primitives.Popup)window.FindName("FiltersPopup")).IsOpen = false; content = Layout(window, 1280, 800);
                // Sem texto de leitura solto (item A1): o que cada número significa fica na dica do cartão.
                var hints = vm.NetworkLightningCards.ToDictionary(card => card.Title, card => card.Hint);
                Require(!FullTexts(content).Any(text => text.StartsWith("Os totais seguem", StringComparison.Ordinal) || text.Contains("A oferta é automática", StringComparison.Ordinal))
                    && hints["Ofertas aceitas"].Contains("não confirma o atendimento", StringComparison.Ordinal) && hints["Ofertas criadas"].Contains("não comprova a notificação", StringComparison.Ordinal), "A leitura dos números deveria estar nas dicas dos cartões, sem texto solto na tela.");
                Combo(content, "Período das vagas").SelectedValue = 7; Settle(vm); content = Layout(window, 1280, 800);
                Require(vm.LightningPeriodDays == 7 && vm.NetworkLightningCards.ToDictionary(card => card.Title, card => card.Value)["Vagas abertas"] == vm.LightningOpen, "Mudar o período atualiza os cartões na hora.");
                Snap(previewRoot, "municipal-vaga-relampago-claro", window);
                window.ViewModel.SetAppearance(Appearance.Dark); Snap(previewRoot, "municipal-vaga-relampago-escuro", window);
            }
            finally { CloseWindow(window); }
        });

        Check("Painel Municipal (painéis): Alertas da rede em listagem aberta com balão, ficha no duplo clique, pesquisa e estado vazio", () =>
        {
            using var fixture = new ApiFixture();
            var alfa = fixture.AddUnit("UBS Alfa", "Centro"); var beta = fixture.AddUnit("UBS Beta", "Sul");
            var items = SampleAlerts(alfa); fixture.AddAlert(alfa.Id, items[0]); fixture.AddAlert(alfa.Id, items[1]); fixture.AddAlert(beta.Id, SampleAlerts(beta)[3]);
            var window = OpenMunicipal(fixture, storageRoot, "panels-alerts", Appearance.Light);
            try
            {
                var vm = LoginObs(window);
                var content = Show(window, "alerts", 1280, 760);
                var list = Descendants<ListBox>(content).Single(box => AutomationProperties.GetName(box) == "Alertas da rede");
                Require(list.Items.Count == 3 && !GridsVisible(content) && !Descendants<Border>(list).Any(border => border.CornerRadius.TopLeft == 16), "Os alertas são uma listagem aberta (sem tabela nem card).");
                var screen = (FrameworkElement)Descendants<NetworkAlertsView>(content).Single(view => view.IsVisible);
                var area = (ScrollViewer)window.FindName("PageScroll");
                Require(Math.Abs(screen.ActualHeight - (area.ViewportHeight - 48)) < 2 && area.VerticalScrollBarVisibility == ScrollBarVisibility.Disabled, $"A tela ocupa a altura da área de conteúdo (tela {screen.ActualHeight:F0}, área {area.ViewportHeight - 48:F0}).");
                var balloon = Descendants<SelectionBalloon>(content).Single(item => item.IsLoaded && FindAncestor<NetworkAlertsView>(item) is not null);
                Require(!balloon.IsVisible, "Sem item selecionado não há balão.");
                list.SelectedItem = list.Items[0]; content = Layout(window, 1280, 760);
                var details = Descendants<Button>(content).Single(button => button.IsVisible && AutomationProperties.GetName(button) == "Ver detalhes do alerta");
                Require(balloon.IsVisible && details.IsVisible, "Com um alerta selecionado o balão mostra Ver detalhes.");
                // Uma nova leitura dos alertas (sem mudança) não tira a seleção nem esconde o balão.
                var selectedKey = ((NetworkAlertEntry)list.SelectedItem).Key; var alertReads = fixture.NetworkAlertQueries.Count;
                vm.RefreshCommand.Execute(null); PumpUntil(() => fixture.NetworkAlertQueries.Count > alertReads); Settle(vm); content = Layout(window, 1280, 760);
                Require(list.SelectedItems.Count == 1 && ((NetworkAlertEntry)list.SelectedItem).Key == selectedKey && balloon.IsVisible, "A releitura dos alertas manteve a seleção e o balão.");
                Snap(previewRoot, "municipal-alertas-balao-claro", window);
                InspectModal<ItemDetailsWindow>(window, dialog =>
                {
                    var texts = VisibleTexts(dialog);
                    Require(new[] { "UBS", "UBS Alfa", "Tipo", "Fila alta", "Especialidade", "Cardiologia", "Valor", "23 pessoas", "Limite", "20 pessoas" }.All(texts.Contains) && !Descendants<Border>(dialog).Any(border => border.CornerRadius.TopLeft == 16 && border.Effect is null && border.Padding.Left == 24), "A ficha do alerta traz as linhas do alerta.");
                    SaveDialog(dialog, Path.Combine(previewRoot, "municipal-alertas-ficha-claro.png"));
                    window.ViewModel.SetAppearance(Appearance.Dark); SaveDialog(dialog, Path.Combine(previewRoot, "municipal-alertas-ficha-escuro.png")); window.ViewModel.SetAppearance(Appearance.Light);
                }, () => InvokeButton(details));
                list.SelectedItems.Clear(); list.SelectedItems.Add(list.Items[0]); list.SelectedItems.Add(list.Items[1]); content = Layout(window, 1280, 760);
                Require(!Descendants<Button>(content).Any(button => button.IsVisible && AutomationProperties.GetName(button) == "Ver detalhes do alerta"), "Com vários alertas a ação de uma ficha só some do balão.");
                list.UnselectAll();
                var search = Descendants<TextBox>(content).Single(box => AutomationProperties.GetName(box) == "Pesquisar: Alertas da rede");
                search.Text = "Beta"; content = Layout(window, 1280, 760);
                Require(list.Items.Count == 1, "A pesquisa filtra os alertas pela linha de dados (UBS).");
                search.Text = "zzzz"; content = Layout(window, 1280, 760);
                Require(list.Items.Count == 0 && HasText(content, "Nenhum alerta encontrado para esta busca."), "Busca sem resultado diz que nada foi encontrado (diferente de não haver alertas).");
                search.Text = "";
                vm.AlertsRegion = "Norte"; Settle(vm); content = Layout(window, 1280, 760);
                Require(list.Items.Count == 0 && vm.NetworkAlertsState.StartsWith("Nenhum alerta neste filtro", StringComparison.Ordinal) && FullTexts(content).Contains(vm.NetworkAlertsState) && !HasText(content, "Nenhum alerta encontrado para esta busca."), "Sem alertas o estado vazio vem da API, e não da busca.");
                Require(vm.ShowNetworkAlertsInfo && !vm.ShowNetworkAlertsWarning, "Nenhum alerta é informação, sem o ícone de atenção.");
                Snap(previewRoot, "municipal-alertas-vazio-claro", window);
                fixture.FailAlerts = true; vm.AlertsRegion = "Sul"; Settle(vm); content = Layout(window, 1280, 760);
                Require(vm.ShowNetworkAlertsWarning && !vm.ShowNetworkAlertsInfo && FullTexts(content).Contains(vm.NetworkAlertsState) && vm.NetworkAlertsState.Contains("tenta de novo sozinha", StringComparison.Ordinal), "A falha da avaliação é um aviso e diz que a tela tenta de novo.");
                Snap(previewRoot, "municipal-alertas-falha-claro", window);
                fixture.FailAlerts = false; vm.AlertsRegion = ""; Settle(vm); window.ViewModel.SetAppearance(Appearance.Dark); Layout(window, 1280, 760); Snap(previewRoot, "municipal-alertas-escuro", window);
            }
            finally { CloseWindow(window); }
        });

        Check("Painel Municipal (painéis): Relatórios e metas com metas em listagem aberta, pop-up de meta, lixeira com uma confirmação para várias e exportação no cabeçalho", () =>
        {
            using var fixture = new ApiFixture();
            var alfa = fixture.AddUnit("UBS Alfa", "Centro"); fixture.AddUnit("UBS Beta", "Sul");
            var asked = new List<string>(); Confirmation.Handler = (_, title, _) => { asked.Add(title); return true; };
            var window = OpenMunicipal(fixture, storageRoot, "panels-goals", Appearance.Light);
            try
            {
                var vm = LoginObs(window);
                var content = Show(window, "reports", 1280, 900);
                var list = Descendants<ListBox>(content).Single(box => AutomationProperties.GetName(box) == "Metas editáveis");
                Require(list.Items.Count == 3 && !AnyRefreshButton(content) && Descendants<Button>(content).Any(button => button.IsVisible && AutomationProperties.GetName(button) == "Exportar resumo em CSV" && button.IsEnabled), "Metas: três linhas e a exportação em CSV no cabeçalho, sem Atualizar.");
                Require(Combo(content, "Escopo das metas") is not null && Combo(content, "Escopo das metas").Items.Count == 3 && !Descendants<TextBox>(content).Any(box => AutomationProperties.GetName(box).StartsWith("Valor da meta de", StringComparison.Ordinal)), "O escopo das metas fica no cabeçalho e o campo de valor só existe no pop-up.");
                var goal = vm.GoalEditors.Single(item => item.Metric == GoalMetric.Score);
                Require(goal.StatusText == "Sem meta" && goal.MetaLine == "Sem meta definida aqui.", "Sem meta gravada a linha diz isso.");
                // Editar abre o pop-up de cadastro; salvar grava e fecha.
                list.SelectedItem = goal; content = Layout(window, 1280, 900);
                var edit = Descendants<Button>(content).Single(button => button.IsVisible && AutomationProperties.GetName(button) == "Editar meta");
                Require(!Descendants<Button>(content).Any(button => button.IsVisible && AutomationProperties.GetName(button) == "Remover meta"), "Sem meta gravada a lixeira não aparece.");
                Snap(previewRoot, "municipal-metas-balao-claro", window);
                InspectModal<FormDialog>(window, dialog =>
                {
                    var value = Descendants<TextBox>(dialog).Single(box => AutomationProperties.GetName(box) == "Valor da meta de Comparecimento");
                    value.Text = "abc"; InvokeButton(Descendants<Button>(dialog).Single(button => button.Content as string == "Salvar meta")); PumpUntil(() => !dialog.IsBusy);
                    Require(dialog.IsVisible && dialog.ErrorMessage.Contains("número", StringComparison.Ordinal) && fixture.Goals.Count == 0, "Valor inválido mantém o pop-up aberto com o erro do campo: " + dialog.ErrorMessage);
                    SaveDialog(dialog, Path.Combine(previewRoot, "municipal-metas-popup-erro-claro.png"));
                    value.Text = "82"; InvokeButton(Descendants<Button>(dialog).Single(button => button.Content as string == "Salvar meta")); PumpUntil(() => !dialog.IsVisible);
                    Require(dialog.Saved, "Salvar com valor válido fecha o pop-up.");
                }, () => InvokeButton(edit));
                Settle(vm);
                Require(fixture.Goals.Single() is { Metric: GoalMetric.Score, UnitId: null, Target: 0.82m } && goal.StatusText == "Definida" && goal.MetaLine == "Meta atual: mín. 82,0%", "A meta foi gravada e a linha mostra a meta atual.");
                // Cancelar o pop-up não deixa rascunho.
                var wait = vm.GoalEditors.Single(item => item.Metric == GoalMetric.AverageWait);
                content = Layout(window, 1280, 900); list.SelectedItem = wait; content = Layout(window, 1280, 900);
                InspectModal<FormDialog>(window, dialog =>
                {
                    Require(Descendants<ComboBox>(dialog).Any(box => AutomationProperties.GetName(box) == "Unidade da espera máxima"), "A espera tem a unidade (horas ou dias).");
                    Descendants<TextBox>(dialog).Single(box => AutomationProperties.GetName(box) == "Valor da meta de Espera média da fila").Text = "9";
                    SaveDialog(dialog, Path.Combine(previewRoot, "municipal-metas-popup-espera-claro.png"));
                    window.ViewModel.SetAppearance(Appearance.Dark); SaveDialog(dialog, Path.Combine(previewRoot, "municipal-metas-popup-espera-escuro.png")); window.ViewModel.SetAppearance(Appearance.Light);
                    InvokeButton(Descendants<Button>(dialog).Single(button => AutomationProperties.GetName(button) == "Fechar sem salvar"));
                }, () => InvokeButton(Descendants<Button>(content).Single(button => button.IsVisible && AutomationProperties.GetName(button) == "Editar meta")));
                Require(wait.Input == "" && !wait.IsDirty && fixture.Goals.Count == 1, "Fechar sem salvar não deixa o valor digitado nem grava.");
                // Duas metas com meta gravada: uma única confirmação para a remoção em lote.
                wait.Input = "2"; wait.WaitUnit = "d"; Pump(vm.SaveGoalAsync(GoalMetric.AverageWait)); Settle(vm); content = Layout(window, 1280, 900);
                list.SelectedItems.Clear(); list.SelectedItems.Add(goal); list.SelectedItems.Add(wait); content = Layout(window, 1280, 900);
                Require(!Descendants<Button>(content).Any(button => button.IsVisible && AutomationProperties.GetName(button) == "Editar meta") && Descendants<Button>(content).Any(button => button.IsVisible && AutomationProperties.GetName(button) == "Remover meta"), "Com várias metas selecionadas a edição some e a lixeira continua.");
                Snap(previewRoot, "municipal-metas-lixeira-claro", window);
                window.ViewModel.SetAppearance(Appearance.Dark); Snap(previewRoot, "municipal-metas-lixeira-escuro", window); window.ViewModel.SetAppearance(Appearance.Light); content = Layout(window, 1280, 900);
                asked.Clear(); InvokeButton(Descendants<Button>(content).Single(button => button.IsVisible && AutomationProperties.GetName(button) == "Remover meta")); Settle(vm); PumpUntil(() => fixture.Goals.Count == 0);
                Require(asked.SequenceEqual(["Remover metas"]) && fixture.Goals.Count == 0, "A remoção em lote pede uma única confirmação.");
                // Ficha da meta (duplo clique / Enter).
                list.UnselectAll(); content = Layout(window, 1280, 900);
                InspectModal<ItemDetailsWindow>(window, dialog =>
                {
                    Require(new[] { "Métrica", "Comparecimento", "Limite", "Mínimo", "Escopo", "Toda a rede", "Como funciona" }.All(VisibleTexts(dialog).Contains), "A ficha da meta traz as linhas da meta (o escopo pelo nome).");
                    SaveDialog(dialog, Path.Combine(previewRoot, "municipal-metas-ficha-claro.png"));
                }, () => ListingBehavior.GetOpenHandler(list)!(goal));
                Snap(previewRoot, "municipal-metas-claro", window, 1280, 1000);
                window.ViewModel.SetAppearance(Appearance.Dark); Snap(previewRoot, "municipal-metas-escuro", window, 1280, 1000);
                _ = alfa;
            }
            finally { Confirmation.Handler = null; CloseWindow(window); }
        });

        Check("Painel Municipal (painéis): janela mínima (980) e o tema escuro mantêm o cabeçalho e as telas legíveis", () =>
        {
            using var fixture = new ApiFixture();
            fixture.AddUnit("UBS Alfa", "Centro"); fixture.AddUnit("UBS Beta", "Sul");
            var window = OpenMunicipal(fixture, storageRoot, "panels-narrow", Appearance.Dark);
            try
            {
                var vm = LoginObs(window);
                foreach (var route in new[] { "home", "indicators", "slots", "alerts", "reports", "capacity", "units:performance", "specialties:demand" })
                {
                    var content = Show(window, route, 980, 700);
                    var header = (FrameworkElement)window.FindName("PageHeader");
                    Require(header.ActualWidth <= 980 + 1 && Descendants<Button>(header).Where(button => button.IsVisible).All(button => button.TranslatePoint(new Point(button.ActualWidth, 0), content).X <= 981), $"{route}: o cabeçalho cabe na janela mínima.");
                    Snap(previewRoot, $"municipal-minima-{route.Replace(':', '-')}-escuro", window, 980, 700);
                }
                _ = vm;
            }
            finally { CloseWindow(window); }
        });
    }
}
