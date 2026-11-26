using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Text.RegularExpressions;
using Nora.Desktop.Models;
using Nora.Desktop.Services;
using Nora.Desktop.ViewModels;
using Nora.Desktop.Views;

namespace Nora.Desktop.Checks;

/// <summary>OBS: indicadores da UBS, Painel com espera média e série real, participação do paciente, ofertas da vaga, alertas e base da UBS indisponível.</summary>
internal static partial class Program
{
    private static MetricCard UnitCard(WorkspaceViewModel vm, string title) => vm.UnitOverviewCards.Single(card => card.Title == title);
    private static double WindowDays(System.Collections.Specialized.NameValueCollection query) =>
        Math.Round((DateTimeOffset.Parse(query["to"]!, CultureInfo.InvariantCulture) - DateTimeOffset.Parse(query["from"]!, CultureInfo.InvariantCulture)).TotalDays);
    /// <summary>Faz a última tentativa de leitura do Painel parecer mais antiga, para exercitar a regra dos 5 minutos sem esperar.</summary>
    private static void AgePanelRead(WorkspaceViewModel vm, TimeSpan age) =>
        typeof(WorkspaceViewModel).GetField("panelAttemptedAt", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(vm, (DateTimeOffset?)(DateTimeOffset.Now - age));
    private static void AgeUnitMetricsRead(WorkspaceViewModel vm, TimeSpan age) =>
        typeof(WorkspaceViewModel).GetField("unitMetricsAttemptedAt", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(vm, (DateTimeOffset?)(DateTimeOffset.Now - age));
    /// <summary>Texto completo de cada TextBlock visível, inclusive o de blocos montados com Run (a propriedade Text não os traz).</summary>
    private static string[] FullTexts(DependencyObject root) =>
        Descendants<TextBlock>(root).Where(text => text.IsVisible).Select(text => new System.Windows.Documents.TextRange(text.ContentStart, text.ContentEnd).Text).ToArray();
    private static UnitAttendance Attend(long completed, long missed, long cancelled, double weight, double? score, int? points = null, string? band = null, bool? hasHistory = null) => new(completed, missed, cancelled, weight, score, points, band, hasHistory);

    private static void RunUnitIndicatorsChecks(Application application, string storageRoot, string[] args)
    {
        var previewRoot = PreviewRoot(args);
        Check("OBS Indicadores: cada período (Dia, Semana, Mês, Ano, 5 anos, Máximo e Personalizado até 20 anos) faz uma única requisição e só a consulta mais nova altera a tela", () =>
        {
            using var fixture = new ApiFixture();
            var unit = fixture.AddUnit("UBS Indicadores", "Centro");
            var window = OpenOperational(fixture, storageRoot, "unit-filters", Appearance.Light);
            try
            {
                var vm = LoginObs(window);
                Require(fixture.UnitMetricsQueries.Count == 1 && WindowDays(fixture.UnitMetricsQueries[0].Query) == 30, "O login deveria ler os indicadores do Painel uma única vez, com 30 dias.");
                var content = Show(window, "indicators");
                var count = fixture.UnitMetricsQueries.Count;
                Require(count == 2 && vm.HasUnitMetrics, "Entrar em Indicadores deveria fazer uma leitura.");
                void ExpectOneMore(string what, double days)
                {
                    count++;
                    Require(fixture.UnitMetricsQueries.Count == count, $"{what}: esperada 1 requisição a mais, houve {fixture.UnitMetricsQueries.Count - count + 1}.");
                    Require(WindowDays(fixture.UnitMetricsQueries[^1].Query) == days && fixture.UnitMetricsQueries[^1].Unit == unit.Id, $"{what}: janela de {days} dia(s) não enviada para a UBS ativa.");
                }
                var period = Combo(content, "Período dos indicadores da UBS");
                Require(period.Items.Cast<PeriodOption>().Select(option => option.Label).SequenceEqual(["Dia", "Semana", "Mês", "Ano", "5 anos", "Máximo", "Personalizado"]), "Opções de período incorretas.");
                Require(Descendants<ComboBox>((FrameworkElement)window.FindName("PageHeader")).Contains(period) && !Descendants<ComboBox>(OpenPageFilters(window)).Contains(period), "O período deveria ficar em linha no cabeçalho, não no botão Filtros.");
                period.SelectedValue = 1; Settle(vm); ExpectOneMore("Dia", 1);
                Require(vm.UnitMetricsScopeLabel.StartsWith($"Números de {DisplayFormat.Date(DateOnly.FromDateTime(DateTime.Today))} a {DisplayFormat.Date(DateOnly.FromDateTime(DateTime.Today))}", StringComparison.Ordinal), "O escopo de 'Dia' não mostra o dia: " + vm.UnitMetricsScopeLabel);
                period.SelectedValue = 7; Settle(vm); ExpectOneMore("Semana", 7);
                period.SelectedValue = 365; Settle(vm); ExpectOneMore("Ano", 365);
                period.SelectedValue = 1826; Settle(vm); ExpectOneMore("5 anos", 1826);
                period.SelectedValue = 7305; Settle(vm); ExpectOneMore("Máximo", 7305);
                period.SelectedValue = 30; Settle(vm); ExpectOneMore("Mês", 30);
                // Personalizado: escolher não consulta; aplicar valida e consulta uma vez.
                period.SelectedValue = 0; Settle(vm);
                Require(fixture.UnitMetricsQueries.Count == count && vm.IsUnitCustomPeriod, "Escolher 'Personalizado' não deveria consultar.");
                content = Layout(window, 1280, 900);
                (string From, string To, string Fragment)[] invalid = [("31/10/2026", "01/10/2026", "inicial não pode ser posterior"), ("01/01/2010", "01/01/2030", "no máximo 20 anos"), ("abc", "01/10/2026", "formato dd/mm/aaaa")];
                foreach (var (from, to, fragment) in invalid)
                {
                    Input(content, "Data inicial do período da UBS").Text = from; Input(content, "Data final do período da UBS").Text = to;
                    Click(window, content, "Aplicar período da UBS");
                    Require(fixture.UnitMetricsQueries.Count == count && vm.UnitRangeError.Contains(fragment) && HasText(OpenPageFilters(window), vm.UnitRangeError), $"Período inválido {from}–{to} não foi barrado com a mensagem: {vm.UnitRangeError}");
                }
                Input(content, "Data inicial do período da UBS").Text = "01/10/2026"; Input(content, "Data final do período da UBS").Text = "31/10/2026";
                Click(window, content, "Aplicar período da UBS"); ExpectOneMore("Personalizado", 31);
                Require(vm.UnitRangeError == "" && vm.UnitMetricsScopeLabel.StartsWith("Números de 01/10/2026 a 31/10/2026", StringComparison.Ordinal), "O período personalizado não foi aplicado com fim inclusivo: " + vm.UnitMetricsScopeLabel);
                // O personalizado da UBS aceita até 20 anos (7.305 dias, o limite da API); a Secretaria continua em 366 dias.
                Input(content, "Data inicial do período da UBS").Text = "01/01/2026"; Input(content, "Data final do período da UBS").Text = "01/01/2027";
                Click(window, content, "Aplicar período da UBS"); ExpectOneMore("Personalizado de 366 dias", 366);
                Input(content, "Data inicial do período da UBS").Text = "01/01/2010"; Input(content, "Data final do período da UBS").Text = "31/12/2029";
                Click(window, content, "Aplicar período da UBS"); ExpectOneMore("Personalizado de 20 anos", 7305);
                Require(vm.UnitRangeError == "", "Vinte anos exatos deveriam ser aceitos: " + vm.UnitRangeError);
                Require(!Descendants<Button>(OpenPageFilters(window)).Any(button => button.ToolTip?.ToString()?.Contains("366", StringComparison.Ordinal) == true), "O tooltip de 366 dias deveria ter saído do Aplicar período.");
            }
            finally { CloseWindow(window); }
        });
        Check("OBS Indicadores: resposta atrasada é descartada, e entrar de novo na aba só lê depois de 5 minutos", () =>
        {
            using var fixture = new ApiFixture();
            fixture.AddUnit("UBS Atraso");
            var window = OpenOperational(fixture, storageRoot, "unit-stale", Appearance.Light);
            try
            {
                var vm = LoginObs(window);
                Show(window, "indicators");
                var baseline = fixture.UnitMetricsQueries.Count; var initialScope = vm.UnitMetricsScopeLabel;
                window.ViewModel.Navigate("queue"); Settle(vm); window.ViewModel.Navigate("indicators"); Settle(vm);
                Pump(vm.RefreshAutomaticallyAsync()); Pump(vm.RefreshAutomaticallyAsync());
                Require(fixture.UnitMetricsQueries.Count == baseline, "Voltar à aba e o ciclo de 20 s não deveriam reler os indicadores antes de 5 minutos.");
                AgeUnitMetricsRead(vm, TimeSpan.FromMinutes(6)); Pump(vm.RefreshAutomaticallyAsync());
                Require(fixture.UnitMetricsQueries.Count == baseline + 1, "Passados 5 minutos, o ciclo com a aba aberta deveria reler os indicadores uma vez.");
                Pump(vm.RefreshAutomaticallyAsync());
                Require(fixture.UnitMetricsQueries.Count == baseline + 1, "O ciclo seguinte releu os indicadores sem esperar.");
                fixture.HoldUnitMetrics = true;
                vm.UnitPeriodDays = 7; PumpUntil(() => fixture.HeldUnitMetrics.Count == 1);
                vm.UnitPeriodDays = 365;
                fixture.HeldUnitMetrics[0].SetResult(true);
                PumpUntil(() => fixture.HeldUnitMetrics.Count == 2);
                Require(vm.UnitMetricsScopeLabel == initialScope, "A resposta atrasada (7 dias) foi aplicada depois de o filtro mudar.");
                fixture.HeldUnitMetrics[1].SetResult(true); Settle(vm);
                var expectedFirst = DateOnly.FromDateTime(DateTime.Today.AddDays(-364)).ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
                Require(vm.UnitMetricsScopeLabel.StartsWith("Números de " + expectedFirst, StringComparison.Ordinal) && vm.UnitPeriodDays == 365 && !vm.UnitMetricsLoading, "Só a consulta de 365 dias deveria ficar na tela: " + vm.UnitMetricsScopeLabel);
            }
            finally { foreach (var held in fixture.HeldUnitMetrics) held.TrySetResult(true); fixture.HoldUnitMetrics = false; CloseWindow(window); }
        });
        Check("OBS Indicadores: cartões, nota do score, Vaga Relâmpago, especialidades e gráfico com a série preenchida com zero, nos três temas", () =>
        {
            using var fixture = new ApiFixture();
            var unit = fixture.AddUnit("UBS Números", "Centro");
            fixture.AddRegistry(unit.Id, "Ana Souza");
            fixture.AddWaiting(unit.Id, "52998224725", "Ana Souza", "Clínica geral", DateTimeOffset.UtcNow.AddHours(-3)); fixture.AddWaiting(unit.Id, "11144477735", "Bia Lima", "Pediatria", DateTimeOffset.UtcNow.AddHours(-1));
            fixture.AddSlot(unit.Id, new UnitLightningSlot(Guid.NewGuid(), Guid.NewGuid(), "Clínica geral", "Dra. Teste", DateTimeOffset.UtcNow.AddDays(1), DateTimeOffset.UtcNow.AddDays(1).AddMinutes(30), "open", DateTimeOffset.UtcNow));
            var window = OpenOperational(fixture, storageRoot, "unit-numbers", Appearance.Light);
            try
            {
                var vm = LoginObs(window);
                foreach (var mode in new[] { Appearance.Light, Appearance.Dark, Appearance.System })
                {
                    window.ViewModel.SetAppearance(mode);
                    var content = Show(window, "indicators", 1280, 2600);
                    Require(vm.UnitOverviewCards.Select(card => card.Title).SequenceEqual(["Comparecimento", "Consultas concluídas", "Faltas", "Cancelamentos", "Espera média da fila", "Ocupação da agenda"]), "Os cards resumidos deveriam ser seis, na ordem do pedido: " + string.Join(" | ", vm.UnitOverviewCards.Select(card => card.Title)));
                    Require(vm.UnitOverviewCards.Select(card => card.Tone).SequenceEqual([Tone.Teal, Tone.Green, Tone.Pink, Tone.Yellow, Tone.Violet, Tone.Blue]), "Cada card deveria ter o tom do seu sentido (concluídas verde, faltas rosa, cancelamentos amarelo, espera violeta).");
                    Require(UnitCard(vm, "Consultas concluídas").Value == "12" && UnitCard(vm, "Faltas").Value == "3" && UnitCard(vm, "Cancelamentos").Value == "2" && UnitCard(vm, "Cancelamentos").Hint == "1 feito pelo paciente", "Cartões de consultas incorretos.");
                    Require(UnitCard(vm, "Comparecimento").Value == "77,9%" && UnitCard(vm, "Espera média da fila").Value == "1 h 35 min" && UnitCard(vm, "Ocupação da agenda").Value == "75,0%", "Score, espera ou ocupação incorretos: " + UnitCard(vm, "Comparecimento").Value + " / " + UnitCard(vm, "Espera média da fila").Value + " / " + UnitCard(vm, "Ocupação da agenda").Value);
                    foreach (var card in vm.UnitOverviewCards) Require(HasText(content, card.Title) && HasText(content, card.Value), $"{mode}: cartão não renderizado: {card.Title}");
                    Require(!VisibleTexts(content).Any(text => text is "Indicadores da UBS" or "Visão geral" or "Vaga Relâmpago no período" or "Capacidade da agenda no período" or "Maior espera atual" or "Série no tempo") && !Descendants<ChartPanel>(content).Any(panel => panel.IsVisible), $"{mode}: o modo cards não deveria ter título repetido, os blocos antigos nem gráficos.");
                    Require(Descendants<TextBlock>(content).Any(text => text.Text.Contains("Como é calculado o comparecimento") && text.ToolTip is string tip && tip.StartsWith("Comparecimento = concluídas ÷", StringComparison.Ordinal)), "A explicação do score não aparece na dica.");
                    Require(VisibleTexts(content).Contains("Metas da Secretaria") && !Descendants<DataGrid>(content).Any(grid => AutomationProperties.GetName(grid) == "Consultas por dia"), "O bloco curto de metas deveria existir e a tabela do gráfico não.");
                    Require(vm.UnitMetricsScopeLabel.EndsWith("UBS Números", StringComparison.Ordinal), "Título único ou escopo da UBS ausentes.");
                    var table = GridNamed(content, "Indicadores da UBS por especialidade");
                    var cells = VisibleTexts(table);
                    Require(new[] { "Clínica geral", "Pediatria", "82,6%", "75,0%", "2 h" }.All(cells.Contains) && cells.Count(text => text == "—") == 1, "Tabela por especialidade sem os números esperados: " + string.Join(" | ", cells));
                    Require(table.Columns.Single(column => column.Header?.ToString() == "Comparecimento").SortMemberPath == "ScoreSort" && table.Columns.Single(column => column.Header?.ToString() == "Espera média").SortMemberPath == "WaitSort", "Colunas de score e espera ordenariam por texto.");
                    // Série: a API devolve só dois dias; o gráfico cobre os 30 com zero, e a tabela só mostra os dois.
                    var days = vm.UnitChart.Days;
                    Require(days.Count == 30 && vm.UnitChart.Rows.Count == 2 && days.Count(day => day.Completed == 0 && day.Missed == 0 && day.Cancelled == 0) == 28, $"A série deveria cobrir 30 dias com 28 deles zerados: {days.Count} dias.");
                    Require(days[^1].Date == DateOnly.FromDateTime(DateTime.Today) && days[^1].Completed == 2 && days[^1].Cancelled == 1 && days[^5].Completed == 3 && days[^5].Missed == 1, "Os dias com dado não ficaram nas datas certas.");
                    // Modo gráficos: a grade de ChartPanel (um por indicador com dado), sem tabela e sem os cards.
                    vm.UnitViewMode = ViewMode.Charts; content = Layout(window, 1280, 2600);
                    var panels = Descendants<ChartPanel>(content).Where(panel => panel.IsVisible).ToArray();
                    Require(panels.Select(panel => panel.Title).SequenceEqual(["Consultas por situação", "Comparecimento", "Vaga Relâmpago", "Espera por especialidade", "Ocupação da agenda", "Consultas por especialidade"]), $"{mode}: grade de gráficos incorreta: " + string.Join(" | ", panels.Select(panel => panel.Title)));
                    Require(!Descendants<DataGrid>(content).Any(grid => grid.IsVisible) && !Descendants<Expander>(content).Any() && !VisibleTexts(content).Any(text => text.Contains("Valor atual") || text.Contains("em tabela")) && !Descendants<ItemsControl>(content).Any(items => items.IsVisible && AutomationProperties.GetName(items) == "Indicadores da UBS no período"), $"{mode}: o modo gráficos não deveria ter tabela nem cards.");
                    var status = panels[0]; var statusChart = Descendants<LineChartView>(status).Single();
                    Require(status.Toggle.IsVisible && status.Lines!.Count == 3 && status.Lines.All(line => line.Points.Count == days.Count), $"{mode}: a evolução deveria ter 3 linhas de {days.Count} pontos e o alternador Linha | Pizza.");
                    Require(Descendants<System.Windows.Shapes.Polyline>(statusChart).Count() == 3 && Descendants<System.Windows.Shapes.Ellipse>(statusChart).Count() == days.Count * 3, $"{mode}: o gráfico deveria ter 3 linhas e {days.Count * 3} pontos.");
                    Require(status.Slices!.Select(slice => slice.Label + "=" + slice.Value).SequenceEqual(["Concluídas=12", "Faltas=3", "Canceladas=2"]) && status.Slices.Single(slice => slice.Label == "Canceladas").BrushKey == "StatusCancelledBrush", "A pizza deveria ter concluídas, faltas e canceladas (amarelo).");
                    Require(panels.Single(panel => panel.Title == "Vaga Relâmpago").Slices!.Select(slice => slice.Label + "=" + slice.Value).SequenceEqual(["Aceitas=1", "Recusadas=2", "Vencidas=1"]) && !panels.Single(panel => panel.Title == "Vaga Relâmpago").Toggle.IsVisible, "A pizza da Vaga Relâmpago está incorreta (só pizza, sem alternador).");
                    vm.UnitViewMode = ViewMode.Cards; content = Layout(window, 1280, 2600);
                    // Contraste dos textos de apoio e dos ícones de estado sobre a superfície dos cartões.
                    var surface = ((SolidColorBrush)application.FindResource("SurfaceBrush")).Color;
                    Require(Contrast(((SolidColorBrush)application.FindResource("SecondaryBrush")).Color, surface) >= 4.5 && Contrast(((SolidColorBrush)application.FindResource("AccentTextBrush")).Color, surface) >= 4.5, $"{mode}: texto de apoio ou números de destaque sem contraste sobre os cartões.");
                    Require(Contrast(((SolidColorBrush)application.FindResource("SuccessBrush")).Color, surface) >= 3 && Contrast(((SolidColorBrush)application.FindResource("ErrorBrush")).Color, surface) >= 3 && Contrast(((SolidColorBrush)application.FindResource("WarningBrush")).Color, surface) >= 3, $"{mode}: cores das séries sem contraste de 3:1 com a superfície.");
                    if (mode != Appearance.System) SaveScrolled(window, Path.Combine(previewRoot, $"obs-indicators-{mode.ToString().ToLowerInvariant()}.png"), 1280, 900);
                }
                vm.UnitSpecialtySearch = "pedi";
                Require(vm.FilteredUnitSpecialtyPerformance.Select(row => row.Name).SequenceEqual(["Pediatria"]), "Busca por especialidade não filtrou a tabela.");
                vm.UnitSpecialtySearch = "sem correspondência";
                Require(!vm.FilteredUnitSpecialtyPerformance.Any() && vm.UnitSpecialtyState == "Nenhuma especialidade corresponde aos filtros.", "A busca sem resultados deveria avisar na tabela.");
                vm.UnitSpecialtySearch = "";
                // Sem consulta encerrada: score nulo e gráfico vazio explicam o motivo.
                window.ViewModel.SetAppearance(Appearance.Light);
                fixture.NoClosedAppointments = true; vm.UnitPeriodDays = 7; Settle(vm);
                var empty = Layout(window, 1280, 2600);
                Require(UnitCard(vm, "Comparecimento").Value == "—" && UnitCard(vm, "Comparecimento").Hint == "Sem consulta encerrada no período para calcular." && Descendants<Border>(empty).Any(border => border.ToolTip?.ToString() == "Sem consulta encerrada no período para calcular.") && vm.UnitChart.State == "Nenhuma consulta com data marcada no período." && HasText(empty, "Nenhuma especialidade com consulta ou fila neste período."), "Score nulo e tabela vazia deveriam explicar o motivo (o gráfico vazio é conferido no modo gráficos).");
                SaveScrolled(window, Path.Combine(previewRoot, "obs-indicators-empty-light.png"), 1280, 900);
                vm.UnitViewMode = ViewMode.Charts; empty = Layout(window, 1280, 2600);
                Require(Descendants<ChartPanel>(empty).Where(panel => panel.IsVisible).Select(panel => panel.Title).SequenceEqual(["Consultas por situação", "Ocupação da agenda"]) && HasText(empty, "Nenhuma consulta com data marcada no período."), "Sem consultas o modo gráficos deveria mostrar o painel de consultas, com a explicação do vazio, e a ocupação da agenda: " + string.Join(" | ", Descendants<ChartPanel>(empty).Select(panel => panel.Title)));
                SaveScrolled(window, Path.Combine(previewRoot, "obs-indicators-charts-empty-light.png"), 1280, 900);
            }
            finally { CloseWindow(window); }
        });
        Check("OBS Painel: espera média dos últimos 30 dias e série real de 7 dias; sem leitura a cada ciclo de 20 s", () =>
        {
            using var fixture = new ApiFixture();
            var first = fixture.AddUnit("UBS Painel A", "Centro"); var second = fixture.AddUnit("UBS Painel B", "Sul");
            fixture.AddRegistry(first.Id, "Ana Souza"); fixture.SetActiveRooms(first.Id, 2, 5);
            var window = OpenOperational(fixture, storageRoot, "unit-panel", Appearance.Light);
            try
            {
                var vm = LoginObs(window);
                Require(vm.ActiveObsUnit == null && fixture.UnitMetricsQueries.Count == 0 && vm.PanelWaitValue == "—", "Sem UBS confirmada nada deveria ser lido.");
                vm.CandidateObsUnit = first; Pump(vm.ConfirmObsUnitAsync());
                Require(fixture.UnitMetricsQueries.Count == 1 && fixture.UnitMetricsQueries[0].Unit == first.Id && WindowDays(fixture.UnitMetricsQueries[0].Query) == 30, "Confirmar a UBS deveria ler os 30 dias uma única vez.");
                Require(vm.PanelWaitValue == "1 h 35 min" && vm.PanelWaitHint.Contains("30 dias"), "Espera média do Painel incorreta: " + vm.PanelWaitValue);
                var series = vm.PanelChart.Days;
                Require(series.Count == 7 && series[^1].Date == DateOnly.FromDateTime(DateTime.Today) && vm.PanelChart.Rows.Count == 2 && series.Count(day => day.Completed == 0 && day.Missed == 0 && day.Cancelled == 0) == 5, $"A série do Painel deveria ter 7 dias, 2 deles com dado: {series.Count}.");
                var content = Layout(window, 1280, 1500);
                var texts = VisibleTexts(content);
                Require(texts.Contains("1 h 35 min") && texts.Contains("Consultas dos últimos 7 dias") && !texts.Contains("Visão da agenda") && !texts.Contains("Aguardando série de atendimentos"), "O Painel ainda mostra o gráfico decorativo ou não mostra a espera média.");
                Require(texts.Contains("Consultórios ativos") && texts.Contains("em atendimento agora") && texts.Contains("com grade hoje") && vm.ActiveRoomsInUseValue == "2" && vm.ActiveRoomsPlannedValue == "5" && texts.Contains("2") && texts.Contains("5"), "Consultórios ativos deveria mostrar os dois números reais (em atendimento agora e com grade hoje).");
                var table = GridNamed(content, "Consultas do período");
                Require(table.IsVisible && vm.PanelTableRows.Count == 2 && vm.PanelTableRows.Sum(row => row.Completed) == 5 && vm.PanelTableRows.Sum(row => row.Missed) == 1 && vm.PanelTableRows.Sum(row => row.Cancelled) == 1, "A tabela do Painel deveria trazer os dois dias com consulta (os mesmos dados do gráfico).");
                Require(vm.PanelChartCards[0].Title == "Consultas por situação" && vm.PanelChartCards[0].Lines.All(line => line.Points.Count == 7), "O gráfico do Painel deveria ter um ponto por dia dos últimos 7 dias.");
                SaveScrolled(window, Path.Combine(previewRoot, "obs-painel-light.png"), 1280, 900);
                window.ViewModel.SetAppearance(Appearance.Dark); Layout(window, 1280, 1500);
                SaveScrolled(window, Path.Combine(previewRoot, "obs-painel-dark.png"), 1280, 900);
                window.ViewModel.SetAppearance(Appearance.Light);
                // Ciclo de 20 s com o Painel aberto: nenhuma leitura de indicadores até a última tentativa passar de 5 minutos.
                var reads = fixture.UnitMetricsQueries.Count;
                for (var cycle = 0; cycle < 3; cycle++) Pump(vm.RefreshAutomaticallyAsync());
                Require(fixture.UnitMetricsQueries.Count == reads, "O ciclo de 20 s leu os indicadores do Painel.");
                AgePanelRead(vm, TimeSpan.FromMinutes(6)); Pump(vm.RefreshAutomaticallyAsync());
                Require(fixture.UnitMetricsQueries.Count == reads + 1, "Passados 5 minutos, o ciclo deveria reler os indicadores uma vez.");
                Pump(vm.RefreshAutomaticallyAsync()); Pump(vm.RefreshAutomaticallyAsync());
                Require(fixture.UnitMetricsQueries.Count == reads + 1, "O ciclo releu os indicadores logo depois da leitura.");
                // Fora do Painel o ciclo não lê indicadores, mesmo com a leitura antiga.
                window.ViewModel.Navigate("queue"); Settle(vm); AgePanelRead(vm, TimeSpan.FromMinutes(30));
                var beforeQueue = fixture.UnitMetricsQueries.Count; Pump(vm.RefreshAutomaticallyAsync());
                Require(fixture.UnitMetricsQueries.Count == beforeQueue, "O ciclo da aba Fila leu os indicadores do Painel.");
                // Atualização manual: uma leitura. Troca de UBS: uma leitura da nova UBS.
                window.ViewModel.Navigate("home"); Settle(vm);
                beforeQueue = fixture.UnitMetricsQueries.Count;
                vm.RefreshCommand.Execute(null); Settle(vm);
                var afterManual = fixture.UnitMetricsQueries.Count;
                Require(afterManual == beforeQueue + 1 && WindowDays(fixture.UnitMetricsQueries[^1].Query) == 30, "A atualização manual deveria ler os indicadores do Painel uma vez, com 30 dias.");
                vm.CandidateObsUnit = second; Pump(vm.ConfirmObsUnitAsync());
                Require(fixture.UnitMetricsQueries.Count == afterManual + 1 && fixture.UnitMetricsQueries[^1].Unit == second.Id, "Trocar de UBS deveria ler uma vez os indicadores da nova UBS.");
            }
            finally { CloseWindow(window); }
        });
        Check("OBS Painel: sem banner, começa em Resumo do dia; pesquisa e período em linha e alternador à direita; cards coloridos com o fluxo da unidade no mesmo grupo", () =>
        {
            using var fixture = new ApiFixture();
            var unit = fixture.AddUnit("UBS Resumo", "Centro");
            fixture.AddRegistry(unit.Id, "Ana Souza"); fixture.SetActiveRooms(unit.Id, 2, 5);
            fixture.AddWaiting(unit.Id, "52998224725", "Ana Souza", "Clínica geral", DateTimeOffset.UtcNow.AddHours(-3)); fixture.AddWaiting(unit.Id, "11144477735", "Bia Lima", "Pediatria", DateTimeOffset.UtcNow.AddHours(-1));
            fixture.AddSlot(unit.Id, new UnitLightningSlot(Guid.NewGuid(), Guid.NewGuid(), "Clínica geral", "Dra. Teste", DateTimeOffset.UtcNow.AddDays(1), DateTimeOffset.UtcNow.AddDays(1).AddMinutes(30), "open", DateTimeOffset.UtcNow));
            var window = OpenOperational(fixture, storageRoot, "panel-redesign", Appearance.Light);
            try
            {
                var vm = LoginObs(window);
                var content = Show(window, "home", 1280, 1500);
                var texts = VisibleTexts(content);
                Require(texts.Contains("Resumo do dia") && !texts.Any(text => text.Contains("PAINEL OPERACIONAL") || text.Contains("Sua unidade em um só lugar") || text.Contains("UBS da operação") || text == "Fluxo da unidade" || text == "Painel operacional"), "O banner, a UBS da operação ou o bloco Fluxo da unidade continuam na tela: " + string.Join(" | ", texts));
                var page = (FrameworkElement)window.FindName("PageScroll");
                Require(!VisibleTexts(page).Any(text => text is "Painel da UBS" or "Painel operacional"), "O nome da tela deveria aparecer só no cabeçalho, não dentro da tela.");
                var header = (FrameworkElement)window.FindName("PageHeader");
                var search = Descendants<HeaderSearchBox>(header).Single(); var period = Descendants<ComboBox>(header).Single(box => AutomationProperties.GetName(box) == "Período do painel"); var toggle = Descendants<ViewModeToggle>(header).Single();
                Require(AutomationProperties.GetName(search) == "Pesquisar no painel" && search.Tag?.ToString() == "Pesquisar", "A pesquisa do cabeçalho deveria ter o campo Pesquisar com lupa.");
                Require(period.Items.Cast<PeriodOption>().Select(option => option.Label + "=" + option.Days).SequenceEqual(["Dia=1", "Semana=7", "Mês=30", "Ano=365", "5 anos=1826", "Máximo=7305"]) && period.SelectedValue is 1, "O combo Período deveria ter Dia, Semana, Mês, Ano, 5 anos e Máximo, com Dia escolhido.");
                double LeftOf(FrameworkElement element) => element.TranslatePoint(new Point(0, 0), header).X;
                Require(LeftOf(search) < LeftOf(period) && LeftOf(period) < LeftOf(toggle) && toggle.ActualWidth > 0 && LeftOf(toggle) + toggle.ActualWidth <= header.ActualWidth + 1, "A ordem do cabeçalho deveria ser pesquisa, período e alternador (o mais à direita).");
                Require(!((Button)window.FindName("FiltersButton")).IsVisible && !Descendants<Button>(page).Any(button => button.IsVisible && (AutomationProperties.GetName(button).Contains("Atualizar", StringComparison.Ordinal) || button.Content?.ToString()?.Contains("Atualizar", StringComparison.Ordinal) == true)) && !Descendants<Button>(header).Any(button => button.IsVisible && AutomationProperties.GetName(button).Contains("Atualizar", StringComparison.Ordinal)), "O Painel não deveria ter botão Filtros nem Atualizar.");
                Require(vm.PanelCards.Select(card => card.Title).SequenceEqual(["Consultas concluídas", "Faltas", "Cancelamentos", "Consultas agendadas", "Tempo médio de espera", "Ofertas em andamento", "Pacientes cadastrados", "Na fila", "Vagas ativas"]), "Os cards do resumo e do fluxo deveriam estar no mesmo grupo: " + string.Join(" | ", vm.PanelCards.Select(card => card.Title)));
                Require(vm.PanelCards.Select(card => card.Tone).SequenceEqual([Tone.Green, Tone.Pink, Tone.Yellow, Tone.Blue, Tone.Violet, Tone.Teal, Tone.Blue, Tone.Violet, Tone.Teal]), "Cada card deveria ter o tom do seu sentido.");
                Require(vm.PanelCards.Select(card => card.Value).SequenceEqual(["0", "0", "0", "0", "1 h 35 min", "0", "1", "2", "1"]), "Valores do resumo do dia incorretos: " + string.Join(" | ", vm.PanelCards.Select(card => card.Value)));
                foreach (var card in vm.PanelCards) Require(HasText(content, card.Title) && HasText(content, card.Value), $"Card não renderizado: {card.Title}");
                Require(texts.Contains("Consultórios ativos") && texts.Contains("Consultas dos últimos 7 dias") && texts.Contains("Atenção da equipe") && texts.Contains("Ver central de alertas"), "No modo cards deveriam aparecer Consultórios ativos, a tabela das consultas e a Atenção da equipe.");
                Require(!Descendants<ChartPanel>(content).Any(panel => panel.IsVisible) && !Descendants<Expander>(content).Any(), "O modo cards não deveria ter gráficos nem o expansor da tabela.");
            }
            finally { CloseWindow(window); }
        });
        Check("OBS Painel: o resumo acompanha o período (Semana, Mês, Ano, 5 anos, Máximo), com uma leitura por mudança e agrupamento por semana ou mês nos períodos longos", () =>
        {
            using var fixture = new ApiFixture();
            var unit = fixture.AddUnit("UBS Períodos", "Centro"); fixture.AddRegistry(unit.Id, "Ana Souza");
            var window = OpenOperational(fixture, storageRoot, "panel-periods", Appearance.Light);
            try
            {
                var vm = LoginObs(window);
                var content = Show(window, "home", 1280, 1500);
                var period = Descendants<ComboBox>((FrameworkElement)window.FindName("PageHeader")).Single(box => AutomationProperties.GetName(box) == "Período do painel");
                var reads = fixture.UnitMetricsQueries.Count;
                Require(reads == 1 && WindowDays(fixture.UnitMetricsQueries[0].Query) == 30, "A abertura deveria ler a janela de 30 dias uma vez.");
                string Values() => string.Join("|", vm.PanelCards.Take(4).Select(card => card.Value));
                // Mês usa os mesmos 30 dias do Dia: nenhuma leitura nova, mas os números passam a ser os do período.
                period.SelectedValue = 30; Settle(vm); content = Layout(window, 1280, 1500);
                Require(fixture.UnitMetricsQueries.Count == reads && vm.PanelSummaryTitle == "Resumo do mês" && HasText(content, "Resumo do mês") && Values() == "12|3|2|1", "Mês deveria reaproveitar a leitura de 30 dias: " + Values());
                (int Days, string Title, string Table)[] steps = [(7, "Resumo da semana", "Consultas da semana"), (365, "Resumo do ano", "Consultas do ano"), (1826, "Resumo dos últimos 5 anos", "Consultas dos últimos 5 anos"), (7305, "Resumo de todo o período", "Consultas de todo o período")];
                foreach (var step in steps)
                {
                    period.SelectedValue = step.Days; Settle(vm); content = Layout(window, 1280, 1500); reads++;
                    Require(fixture.UnitMetricsQueries.Count == reads && WindowDays(fixture.UnitMetricsQueries[^1].Query) == step.Days, $"{step.Title}: deveria haver uma única leitura da janela de {step.Days} dias.");
                    Require(HasText(content, step.Title) && HasText(content, step.Table) && Values() == "12|3|2|1" && vm.PanelWaitHint.Contains("período", StringComparison.Ordinal), $"{step.Title}: título, tabela ou números incorretos: {Values()}");
                    Require(vm.PanelTableRows.Sum(row => row.Completed) == 5 && vm.PanelTableRows.Sum(row => row.Missed) == 1 && vm.PanelTableRows.Sum(row => row.Cancelled) == 1, $"{step.Title}: a tabela deveria somar os dois dias com consulta.");
                }
                // Ano: eixo de 365 dias agrupado por semana; 5 anos e Máximo começam no primeiro dia com consulta (sem anos vazios).
                period.SelectedValue = 365; Settle(vm);
                Require(vm.PanelChart.Days.Count == 365 && vm.PanelChartCards[0].Lines.All(line => line.Points.Count is >= 52 and <= 54) && vm.PanelChartCards[0].LineNote.Contains("semana", StringComparison.Ordinal), $"Ano deveria agrupar por semana: {vm.PanelChart.Days.Count} dias, {vm.PanelChartCards[0].Lines[0].Points.Count} pontos.");
                period.SelectedValue = 7305; Settle(vm);
                Require(vm.PanelChart.Days.Count == 5 && vm.PanelChartCards[0].Lines.All(line => line.Points.Count == 5), $"Máximo deveria começar no primeiro dia com consulta: {vm.PanelChart.Days.Count} dias.");
                // Voltar ao Dia lê os 30 dias de novo (a janela de 20 anos não vale para ele) e traz de volta a lista de hoje.
                reads = fixture.UnitMetricsQueries.Count;
                period.SelectedValue = 1; Settle(vm);
                Require(fixture.UnitMetricsQueries.Count == reads + 1 && WindowDays(fixture.UnitMetricsQueries[^1].Query) == 30 && vm.PanelSummaryTitle == "Resumo do dia" && vm.PanelChart.Days.Count == 7 && Values() == "0|0|0|0", "Voltar ao Dia deveria ler os 30 dias e mostrar de novo os 7 dias do gráfico.");
                // Resposta atrasada de um período que já mudou não altera a tela.
                fixture.HoldUnitMetrics = true;
                vm.PanelPeriodDays = 365; PumpUntil(() => fixture.HeldUnitMetrics.Count == 1);
                vm.PanelPeriodDays = 7;
                fixture.HeldUnitMetrics[0].SetResult(true);
                PumpUntil(() => fixture.HeldUnitMetrics.Count == 2);
                Require(vm.PanelWaitHint == "Atualizando o período…" && Values() == "—|—|—|—" && vm.PanelMetricsLoading, "A resposta de 365 dias não deveria ser aplicada à semana: " + vm.PanelWaitHint);
                fixture.HeldUnitMetrics[1].SetResult(true); Settle(vm);
                Require(Values() == "12|3|2|1" && vm.PanelSummaryTitle == "Resumo da semana" && !vm.PanelMetricsLoading, "Só a leitura de 7 dias deveria ficar na tela: " + Values());
            }
            finally { foreach (var held in fixture.HeldUnitMetrics) held.TrySetResult(true); fixture.HoldUnitMetrics = false; CloseWindow(window); }
        });
        Check("OBS Painel: a pesquisa filtra cards, painéis de gráfico, tabela e blocos sem diferenciar acentos nem maiúsculas", () =>
        {
            using var fixture = new ApiFixture();
            var unit = fixture.AddUnit("UBS Pesquisa", "Centro"); fixture.AddRegistry(unit.Id, "Ana Souza"); fixture.SetActiveRooms(unit.Id, 2, 5);
            var window = OpenOperational(fixture, storageRoot, "panel-search", Appearance.Light);
            try
            {
                var vm = LoginObs(window);
                var content = Show(window, "home", 1280, 1500);
                var search = Descendants<HeaderSearchBox>((FrameworkElement)window.FindName("PageHeader")).Single();
                search.Text = "FALT"; content = Layout(window, 1280, 1500);
                Require(vm.PanelSearch == "FALT" && vm.PanelCards.Select(card => card.Title).SequenceEqual(["Faltas"]) && !vm.ShowPanelRooms && !vm.ShowPanelAttention && !vm.ShowPanelTable && !HasText(content, "Atenção da equipe") && HasText(content, "Faltas"), "A pesquisa 'FALT' deveria deixar só o card Faltas: " + string.Join(" | ", vm.PanelCards.Select(card => card.Title)));
                search.Text = "atencao";
                Require(vm.PanelCards.Count == 0 && vm.ShowPanelAttention && !vm.ShowPanelRooms && vm.PanelCardsEmptyText == "", "'atencao' (sem acento) deveria achar a Atenção da equipe.");
                search.Text = "CONSULTORIOS";
                Require(vm.PanelCards.Count == 0 && vm.ShowPanelRooms && !vm.ShowPanelAttention, "'CONSULTORIOS' deveria achar Consultórios ativos.");
                search.Text = "últimos 7";
                Require(vm.PanelCards.Count == 0 && vm.ShowPanelTable && vm.PanelTableRows.Count == 2, "A pesquisa pelo título da tabela deveria mostrar a tabela inteira.");
                search.Text = "conclu";
                Require(vm.PanelCards.Select(card => card.Title).SequenceEqual(["Consultas concluídas"]), "'conclu' deveria achar o card de concluídas: " + string.Join(" | ", vm.PanelCards.Select(card => card.Title)));
                search.Text = "zzz"; content = Layout(window, 1280, 1500);
                Require(vm.PanelCards.Count == 0 && !vm.ShowPanelRooms && !vm.ShowPanelAttention && !vm.ShowPanelTable && HasText(content, "Nenhum item do painel corresponde à pesquisa.") && !HasText(content, "Resumo do dia"), "Sem resultado o painel deveria dizer que nada corresponde à pesquisa (e esconder o título do resumo).");
                // Modo gráficos: a pesquisa filtra os painéis pelo título e pelos nomes das linhas e fatias.
                vm.PanelViewMode = ViewMode.Charts; search.Text = "ocupa"; content = Layout(window, 1280, 1500);
                Require(vm.PanelChartCards.Select(card => card.Title).SequenceEqual(["Ocupação da agenda"]) && Descendants<ChartPanel>(content).Count(panel => panel.IsVisible) == 1, "'ocupa' deveria deixar só o painel da ocupação da agenda.");
                search.Text = "faltas";
                Require(vm.PanelChartCards.Select(card => card.Title).Contains("Consultas por situação"), "'faltas' deveria achar o painel que tem a linha de faltas.");
                search.Text = "zzz"; content = Layout(window, 1280, 1500);
                Require(vm.PanelChartCards.Count == 0 && HasText(content, "Nenhum gráfico corresponde à pesquisa."), "Sem resultado o modo gráficos deveria avisar.");
                search.Text = ""; content = Layout(window, 1280, 1500);
                Require(vm.PanelChartCards.Count >= 3, "Apagar a pesquisa deveria devolver todos os painéis.");
            }
            finally { CloseWindow(window); }
        });
        Check("OBS Painel: o alternador troca cards por uma grade de ChartPanel (Linha | Pizza), sem tabela nem Atenção da equipe, lembra a escolha e mantém o desenho escolhido", () =>
        {
            using var fixture = new ApiFixture();
            var unit = fixture.AddUnit("UBS Gráficos", "Centro"); fixture.AddRegistry(unit.Id, "Ana Souza");
            fixture.AddWaiting(unit.Id, "52998224725", "Ana Souza", "Clínica geral", DateTimeOffset.UtcNow.AddHours(-3));
            var window = OpenOperational(fixture, storageRoot, "panel-charts", Appearance.Light);
            try
            {
                var vm = LoginObs(window);
                var content = Show(window, "home", 1280, 1500);
                var toggle = Descendants<ViewModeToggle>((FrameworkElement)window.FindName("PageHeader")).Single();
                Require(vm.PanelViewMode == ViewMode.Cards && vm.ShowPanelCards && !vm.ShowPanelCharts, "O Painel deveria abrir em cards.");
                InvokeButton(toggle); content = Layout(window, 1280, 1500);
                Require(vm.PanelViewMode == ViewMode.Charts && window.ViewModel.GetViewMode("home") == ViewMode.Charts, "O alternador deveria ir para os gráficos e lembrar a escolha.");
                var panels = Descendants<ChartPanel>(content).Where(panel => panel.IsVisible).ToArray();
                Require(panels.Select(panel => panel.Title).SequenceEqual(["Consultas por situação", "Vaga Relâmpago", "Espera por especialidade", "Ocupação da agenda", "Consultas por especialidade", "Pacientes cadastrados"]), "Grade do Painel incorreta: " + string.Join(" | ", panels.Select(panel => panel.Title)));
                var texts = VisibleTexts(content);
                Require(!texts.Contains("Atenção da equipe") && !texts.Contains("Consultórios ativos") && !texts.Contains("Resumo do dia") && !Descendants<DataGrid>(content).Any(grid => grid.IsVisible) && !Descendants<Expander>(content).Any() && !texts.Any(text => text.Contains("Valor atual") || text.Contains("em tabela")), "O modo gráficos não deveria ter tabela, Atenção da equipe, consultórios nem 'Valor atual'.");
                Require(panels.Single(panel => panel.Title == "Vaga Relâmpago").Slices!.Count == 3 && panels.Single(panel => panel.Title == "Ocupação da agenda").Slices!.Select(slice => slice.Label + "=" + slice.Value).SequenceEqual(["Ocupados=30", "Livres=10"]), "As pizzas da Vaga Relâmpago e da ocupação estão incorretas.");
                // Semana tem pizza das encerradas: com linha e pizza o alternador aparece, e o desenho escolhido sobrevive às atualizações.
                var period = Descendants<ComboBox>((FrameworkElement)window.FindName("PageHeader")).Single(box => AutomationProperties.GetName(box) == "Período do painel");
                period.SelectedValue = 7; Settle(vm); content = Layout(window, 1280, 1500);
                var status = Descendants<ChartPanel>(content).First(panel => panel.IsVisible);
                Require(status.Toggle.IsVisible && status.Slices!.Select(slice => slice.Label + "=" + slice.Value).SequenceEqual(["Concluídas=12", "Faltas=3", "Canceladas=2"]), "Na semana o painel de consultas deveria ter Linha e Pizza.");
                status.Toggle.SelectedIndex = 1; content = Layout(window, 1280, 1500);
                Require(vm.PanelChartCards[0].Kind == ChartKind.Pie, "A escolha de Pizza deveria ir para o painel.");
                for (var cycle = 0; cycle < 2; cycle++) Pump(vm.RefreshAutomaticallyAsync());
                content = Layout(window, 1280, 1500);
                Require(vm.PanelChartCards[0].Kind == ChartKind.Pie && Descendants<ChartPanel>(content).First(panel => panel.IsVisible).Kind == ChartKind.Pie, "O ciclo de 20 s não deveria desfazer o desenho escolhido.");
                InvokeButton(toggle); content = Layout(window, 1280, 1500);
                Require(vm.PanelViewMode == ViewMode.Cards && window.ViewModel.GetViewMode("home") == ViewMode.Cards && HasText(content, "Resumo da semana") && !Descendants<ChartPanel>(content).Any(panel => panel.IsVisible), "O segundo clique deveria voltar aos cards.");
            }
            finally { CloseWindow(window); }
        });
        Check("OBS Painel e Indicadores: grade de cards e agrupamento do eixo em períodos longos", () =>
        {
            Require(TileGridPanel.ColumnsFor(9, 1050, 250, 4) == 3 && TileGridPanel.ColumnsFor(6, 1050, 250, 4) == 3 && TileGridPanel.ColumnsFor(7, 1050, 250, 4) == 4 && TileGridPanel.ColumnsFor(5, 1050, 250, 4) == 3, "A grade deveria usar uma coluna a menos quando a última linha ficaria quase vazia.");
            Require(TileGridPanel.ColumnsFor(9, 700, 250, 4) == 2 && TileGridPanel.ColumnsFor(9, 200, 250, 4) == 1 && TileGridPanel.ColumnsFor(1, 2000, 250, 4) == 1 && TileGridPanel.ColumnsFor(4, 5000, 250, 4) == 4 && TileGridPanel.ColumnsFor(6, 1000, 460, 2) == 2, "O número de colunas deveria seguir a largura, o mínimo de 1 e o máximo configurado.");
            var panel = new TileGridPanel { MinTileWidth = 200, MaxColumns = 3 };
            for (var index = 0; index < 5; index++) panel.Children.Add(new Border { Height = 40 + index * 10 });
            panel.Measure(new Size(700, double.PositiveInfinity)); panel.Arrange(new Rect(0, 0, 700, panel.DesiredSize.Height));
            Require(panel.Children.Cast<Border>().Select(child => child.ActualWidth).Distinct().Count() == 1 && Math.Abs(panel.Children.Cast<Border>().First().ActualWidth - 700d / 3) < 0.5 && Math.Abs(panel.DesiredSize.Height - (60 + 80)) < 0.5, "A grade deveria dividir a largura por igual e dar a cada linha a altura do bloco mais alto.");
            // Eixo: dias até 92, semanas até 400 e meses acima; o gráfico de linha usa os mesmos passos (ChartData).
            var start = new DateOnly(2024, 1, 1);
            var sparse = new[] { new DailyPoint(start.AddDays(10), 3, 1, 0), new DailyPoint(start.AddDays(11), 2, 0, 1), new DailyPoint(start.AddDays(700), 4, 0, 0) };
            var axis = (DailyPoint[])typeof(WorkspaceViewModel).GetMethod("BuildAxis", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [sparse, start, start.AddDays(729)])!;
            Require(axis.Length == 720 && axis[0].Date == start.AddDays(10) && axis[^1].Date == start.AddDays(729) && axis.Sum(day => day.Completed) == 9, $"Em janela longa o eixo deveria começar no primeiro dia com consulta: {axis.Length} dias.");
            Require(((DailyPoint[])typeof(WorkspaceViewModel).GetMethod("BuildAxis", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [Array.Empty<DailyPoint>(), start, start.AddDays(729)])!).Length == 0, "Sem nenhuma consulta numa janela longa o eixo deveria ficar vazio, não 730 dias de zeros.");
            var lines = ChartData.Evolution(axis);
            Require(ChartData.For(axis.Length) == ChartGrouping.Month && lines[0].Points.Count == 24 && lines[0].Points.Sum(point => point.Value) == 9, "Mais de 400 dias deveriam agrupar por mês sem perder nenhum total.");
            var daily = Enumerable.Range(0, 200).Select(index => new DailyMetrics(start.AddDays(index).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), 1, 2, 1, 0)).ToArray();
            var dailyLine = typeof(WorkspaceViewModel).GetMethod("DailyLine", BindingFlags.Static | BindingFlags.NonPublic)!;
            object?[] args = [daily, (Func<DailyMetrics, double>)(day => day.Completed), null];
            var points = (ChartPoint[])dailyLine.Invoke(null, args)!;
            Require((ChartGrouping)args[2]! == ChartGrouping.Week && points.Length == 29 && points.Sum(point => point.Value) == 400, $"O gráfico do pop-up deveria somar por semana em 200 dias: {points.Length} pontos.");
            args = [daily.Take(30).ToArray(), (Func<DailyMetrics, double>)(day => day.Completed), null];
            points = (ChartPoint[])dailyLine.Invoke(null, args)!;
            Require((ChartGrouping)args[2]! == ChartGrouping.Day && points.Length == 30, "Até 92 dias o gráfico do pop-up continua com um ponto por dia.");
            // Janela de 20 anos: meia-noite de 2006 tinha outro deslocamento (horário de verão antigo) e passaria do limite da API por uma hora.
            var to = new DateTimeOffset(2026, 11, 27, 0, 0, 0, TimeSpan.FromHours(-3)); var oldStart = new DateTimeOffset(2006, 11, 27, 0, 0, 0, TimeSpan.FromHours(-2));
            Require(to - oldStart > TimeSpan.FromDays(7305) && to - WorkspaceViewModel.ClampWindowStart(oldStart, to) == TimeSpan.FromDays(7305), "A janela de 20 anos deveria ser ajustada para valer exatamente 7.305 dias.");
            var invalidInput = typeof(NoraApiClient).GetMethod("InvalidInputMessage", BindingFlags.Static | BindingFlags.NonPublic)!;
            string Message(string path) => (string)invalidInput.Invoke(null, [path])!;
            Require(Message($"api/obs/units/{Guid.NewGuid()}/metrics?from=a&to=b").Contains("20 anos", StringComparison.Ordinal) && Message("api/municipal/metrics?from=a&to=b").Contains("20 anos", StringComparison.Ordinal)
                && Message("api/municipal/history?from=a&to=b").Contains("366 dias", StringComparison.Ordinal) && !Message($"api/obs/units/{Guid.NewGuid()}/metrics?from=a").Contains("366", StringComparison.Ordinal), "O erro 400 de período dos indicadores deveria falar do limite de 20 anos (o histórico continua em 366 dias).");
            var recent = to.AddDays(-30);
            Require(WorkspaceViewModel.ClampWindowStart(recent, to) == recent, "Uma janela dentro do limite não deveria ser alterada.");
            Require(WorkspaceViewModel.ValidateUnitRange("01/01/2010", "31/12/2029", out _, out _) == "" && WorkspaceViewModel.ValidateUnitRange("01/01/2010", "01/01/2030", out _, out _).Contains("20 anos", StringComparison.Ordinal)
                && WorkspaceViewModel.ValidateUnitRange("31/10/2026", "01/10/2026", out _, out _).Contains("inicial não pode ser posterior", StringComparison.Ordinal) && WorkspaceViewModel.ValidateUnitRange("abc", "01/10/2026", out _, out _).Contains("dd/mm/aaaa", StringComparison.Ordinal), "A validação do período da UBS deveria aceitar 20 anos exatos e recusar mais.");
        });
        Check("OBS Painel e Indicadores: capturas dos dois modos, nos dois temas, com dados, vazio, janela mínima, pop-up do gráfico e Filtros", () =>
        {
            using var fixture = new ApiFixture();
            var unit = fixture.AddUnit("UBS Jardim das Flores", "Centro");
            foreach (var (name, cpf) in new[] { ("Ana Souza", "52998224725"), ("Bia Lima", "11144477735"), ("Caio Rocha", "12345678909") }) fixture.AddRegistry(unit.Id, name, cpf);
            fixture.AddWaiting(unit.Id, "52998224725", "Ana Souza", "Clínica geral", DateTimeOffset.UtcNow.AddHours(-3)); fixture.AddWaiting(unit.Id, "11144477735", "Bia Lima", "Pediatria", DateTimeOffset.UtcNow.AddHours(-1)); fixture.AddWaiting(unit.Id, "12345678909", "Caio Rocha", "Clínica geral", DateTimeOffset.UtcNow.AddMinutes(-40));
            var start = DateTimeOffset.UtcNow.AddHours(2);
            fixture.AddSlot(unit.Id, new UnitLightningSlot(Guid.NewGuid(), Guid.NewGuid(), "Clínica geral", "Dra. Teste", start, start.AddMinutes(30), "open", DateTimeOffset.UtcNow));
            fixture.AddSlot(unit.Id, new UnitLightningSlot(Guid.NewGuid(), Guid.NewGuid(), "Pediatria", "Dr. Teste", start.AddHours(1), start.AddHours(1).AddMinutes(30), "offered", DateTimeOffset.UtcNow));
            var today = DateTime.Today;
            foreach (var (status, hour) in new[] { ("completed", 7), ("completed", 8), ("completed", 9), ("missed", 10), ("cancelled", 11), ("scheduled", 14), ("scheduled", 15), ("scheduled", 16), ("called", 13) })
                fixture.AddAppointment(unit.Id, status, new DateTimeOffset(today.AddHours(hour)));
            fixture.SetActiveRooms(unit.Id, 3, 6);
            var window = OpenOperational(fixture, storageRoot, "panel-captures", Appearance.Light);
            try
            {
                var vm = LoginObs(window);
                foreach (var mode in new[] { Appearance.Light, Appearance.Dark })
                {
                    var tag = mode.ToString().ToLowerInvariant();
                    window.ViewModel.SetAppearance(mode);
                    vm.PanelPeriodDays = 1; vm.PanelViewMode = ViewMode.Cards; vm.PanelSearch = "";
                    Show(window, "home", 1366, 768); SaveScrolled(window, Path.Combine(previewRoot, $"painel-cards-dia-{tag}.png"), 1366, 768);
                    vm.PanelViewMode = ViewMode.Charts; Layout(window, 1366, 768); SaveScrolled(window, Path.Combine(previewRoot, $"painel-graficos-dia-{tag}.png"), 1366, 768);
                    vm.PanelPeriodDays = 7; Settle(vm); vm.PanelViewMode = ViewMode.Cards; Layout(window, 1366, 768);
                    SaveScrolled(window, Path.Combine(previewRoot, $"painel-cards-semana-{tag}.png"), 1366, 768);
                    vm.PanelViewMode = ViewMode.Charts; Layout(window, 1366, 768); SaveScrolled(window, Path.Combine(previewRoot, $"painel-graficos-semana-{tag}.png"), 1366, 768);
                    vm.PanelPeriodDays = 365; Settle(vm); Layout(window, 1366, 768); SaveScrolled(window, Path.Combine(previewRoot, $"painel-graficos-ano-{tag}.png"), 1366, 768);
                    vm.PanelPeriodDays = 1; Settle(vm);
                    Show(window, "indicators", 1366, 768); vm.UnitViewMode = ViewMode.Cards; Layout(window, 1366, 768);
                    SaveScrolled(window, Path.Combine(previewRoot, $"indicadores-cards-{tag}.png"), 1366, 768);
                    vm.UnitViewMode = ViewMode.Charts; Layout(window, 1366, 768); SaveScrolled(window, Path.Combine(previewRoot, $"indicadores-graficos-{tag}.png"), 1366, 768);
                    vm.UnitViewMode = ViewMode.Cards;
                }
                window.ViewModel.SetAppearance(Appearance.Light);
                // Pesquisa no Painel, janela mínima e pop-up do gráfico de um card.
                Show(window, "home", 1366, 768); vm.PanelViewMode = ViewMode.Cards; vm.PanelSearch = "falt"; Layout(window, 1366, 768);
                SaveScrolled(window, Path.Combine(previewRoot, "painel-pesquisa-light.png"), 1366, 768);
                vm.PanelSearch = "zzz"; Layout(window, 1366, 768); SaveScrolled(window, Path.Combine(previewRoot, "painel-pesquisa-sem-resultado-light.png"), 1366, 768);
                vm.PanelSearch = ""; vm.PanelPeriodDays = 365; Settle(vm); Layout(window, 1366, 768);
                SaveScrolled(window, Path.Combine(previewRoot, "painel-cards-ano-light.png"), 1366, 768);
                vm.PanelPeriodDays = 1; Settle(vm);
                Layout(window, 980, 640); SaveScrolled(window, Path.Combine(previewRoot, "painel-cards-janela-minima-light.png"), 980, 640);
                vm.PanelViewMode = ViewMode.Charts; Layout(window, 980, 640); SaveScrolled(window, Path.Combine(previewRoot, "painel-graficos-janela-minima-light.png"), 980, 640);
                vm.PanelViewMode = ViewMode.Cards;
                var popup = new IndicatorChartWindow(vm.BuildIndicatorChart("Consultas concluídas")) { Owner = window, WindowStartupLocation = WindowStartupLocation.Manual, Left = -10000, Top = -10000, ShowActivated = false };
                try { popup.Show(); popup.UpdateLayout(); SavePreview(popup, Path.Combine(previewRoot, "painel-popup-grafico-card-light.png")); window.ViewModel.SetAppearance(Appearance.Dark); popup.UpdateLayout(); SavePreview(popup, Path.Combine(previewRoot, "painel-popup-grafico-card-dark.png")); }
                finally { popup.Close(); window.ViewModel.SetAppearance(Appearance.Light); }
                // Indicadores: período personalizado e filtros no botão Filtros; janela mínima.
                Show(window, "indicators", 1366, 768); vm.UnitPeriodDays = 0; Layout(window, 1366, 768);
                SavePreview(OpenPageFilters(window), Path.Combine(previewRoot, "indicadores-filtros-personalizado-light.png"));
                ((System.Windows.Controls.Primitives.Popup)window.FindName("FiltersPopup")).IsOpen = false;
                vm.UnitPeriodDays = 30; Settle(vm); Layout(window, 980, 640);
                SaveScrolled(window, Path.Combine(previewRoot, "indicadores-cards-janela-minima-light.png"), 980, 640);
            }
            finally { CloseWindow(window); }
            // Estado vazio: UBS sem consulta, fila, vaga nem cadastro.
            using var emptyFixture = new ApiFixture { NoClosedAppointments = true };
            emptyFixture.AddUnit("UBS Nova", "Sul");
            var emptyWindow = OpenOperational(emptyFixture, storageRoot, "panel-captures-empty", Appearance.Light);
            try
            {
                var vm = LoginObs(emptyWindow);
                foreach (var mode in new[] { Appearance.Light, Appearance.Dark })
                {
                    var tag = mode.ToString().ToLowerInvariant();
                    emptyWindow.ViewModel.SetAppearance(mode);
                    vm.PanelViewMode = ViewMode.Cards; Show(emptyWindow, "home", 1366, 768); SaveScrolled(emptyWindow, Path.Combine(previewRoot, $"painel-vazio-cards-{tag}.png"), 1366, 768);
                    vm.PanelViewMode = ViewMode.Charts; Layout(emptyWindow, 1366, 768); SaveScrolled(emptyWindow, Path.Combine(previewRoot, $"painel-vazio-graficos-{tag}.png"), 1366, 768);
                    vm.PanelViewMode = ViewMode.Cards;
                    Show(emptyWindow, "indicators", 1366, 768); vm.UnitViewMode = ViewMode.Cards; Layout(emptyWindow, 1366, 768); SaveScrolled(emptyWindow, Path.Combine(previewRoot, $"indicadores-vazio-cards-{tag}.png"), 1366, 768);
                    vm.UnitViewMode = ViewMode.Charts; Layout(emptyWindow, 1366, 768); SaveScrolled(emptyWindow, Path.Combine(previewRoot, $"indicadores-vazio-graficos-{tag}.png"), 1366, 768);
                    vm.UnitViewMode = ViewMode.Cards;
                }
            }
            finally { CloseWindow(emptyWindow); }
        });
        Check("OBS Painel: se só os indicadores falharem a UBS abre, o cartão avisa e o ciclo não insiste", () =>
        {
            using var fixture = new ApiFixture();
            var unit = fixture.AddUnit("UBS Sem indicadores"); fixture.AddRegistry(unit.Id, "Ana Souza"); fixture.FailingUnitMetrics.Add(unit.Id);
            var window = OpenOperational(fixture, storageRoot, "unit-panel-fail", Appearance.Light);
            try
            {
                var vm = LoginObs(window);
                Require(vm.ActiveObsUnit?.Id == unit.Id && vm.ObsRegistry.Count == 1 && !vm.HasMessage, "A UBS deveria abrir normalmente: " + vm.Message);
                Require(vm.PanelWaitValue == "—" && vm.PanelWaitHint == "Não foi possível ler os indicadores agora." && HasText(Layout(window, 1280, 1500), "Não foi possível ler os indicadores agora."), "O cartão da espera média não avisou a falha.");
                var reads = fixture.UnitMetricsQueries.Count;
                for (var cycle = 0; cycle < 3; cycle++) Pump(vm.RefreshAutomaticallyAsync());
                Require(fixture.UnitMetricsQueries.Count == reads && !vm.HasMessage, "O ciclo insistiu na leitura que falhou.");
            }
            finally { CloseWindow(window); }
        });
        Check("OBS Pacientes: score em pontos (0 a 1000) e faixa na lista, na ficha e na ordenação; sem consulta encerrada = 1000; sem dado da API aparece — com explicação", () =>
        {
            using var fixture = new ApiFixture();
            var unit = fixture.AddUnit("UBS Participação");
            DateTimeOffset when = DateTimeOffset.UtcNow;
            fixture.AddRegistry(unit.Id, new UnitPatientRecord("52998224725", "Ana Alta", "ana@example.test", null, null, true, false, when, Attend(9, 1, 0, 0, 0.9, 900, "excellent", true)));
            fixture.AddRegistry(unit.Id, new UnitPatientRecord("11144477735", "Bia Sem Consulta", "bia@example.test", null, null, true, false, when.AddMinutes(-1), Attend(0, 0, 0, 0, null, 1000, "excellent", false)));
            fixture.AddRegistry(unit.Id, new UnitPatientRecord("12345678909", "Caio Baixo", "caio@example.test", null, null, true, false, when.AddMinutes(-2), Attend(3, 1, 2, 1.4, 0.4286, 429, "regular", true)));
            fixture.AddRegistry(unit.Id, new UnitPatientRecord("39053344705", "Duda Antiga", "duda@example.test", null, null, true, false, when.AddMinutes(-3)));
            // API sem os campos novos: o cliente calcula os pontos e a faixa pelo mesmo cálculo (0,75 x 1000 = 750, faixa Bom).
            fixture.AddRegistry(unit.Id, new UnitPatientRecord("98765432100", "Edu Sem Pontos", "edu@example.test", null, null, true, false, when.AddMinutes(-4), Attend(3, 1, 0, 0, 0.75)));
            var window = OpenOperational(fixture, storageRoot, "unit-participation", Appearance.Light);
            try
            {
                var vm = LoginObs(window);
                var content = Show(window, "patients", 1280, 1300);
                var list = Descendants<ListBox>(content).Single(box => AutomationProperties.GetName(box) == "Cadastros de referência da UBS");
                // A lista rola (altura máxima): só as primeiras linhas estão desenhadas, então o texto é conferido nelas e o resto pelos dados.
                var texts = VisibleTexts(list);
                Require(texts.Contains("Score (12 meses): 900 · Excelente") && texts.Contains("Score (12 meses): 1000 · sem consultas encerradas ainda"), "A lista não mostra o score em pontos: " + string.Join(" | ", texts.Where(text => text.StartsWith("Score", StringComparison.Ordinal))));
                Require(!texts.Any(text => text.Contains('%')), "A lista não deveria mais mostrar participação em %.");
                Require(Descendants<TextBlock>(list).Any(text => text.ToolTip?.ToString() == WorkspaceViewModel.ScorePriorityText), "A dica do score deveria explicar seu efeito na Vaga Relâmpago sem mudar a prioridade clínica.");
                Require(list.Items.Cast<UnitPatientRecord>().Select(patient => patient.ScoreDisplay).OrderBy(text => text, StringComparer.Ordinal).SequenceEqual(["1000 · sem consultas encerradas ainda", "429 · Regular", "750 · Bom", "900 · Excelente", "—"]), "O score de cada cadastro está incorreto.");
                var sort = Combo(content, "Ordenar cadastro da UBS");
                Require(sort.Items.Cast<ComboBoxItem>().Select(item => item.Content?.ToString()).SequenceEqual(["Mais recentes", "Nome A–Z", "Nome Z–A", "Maior score", "Menor score"]), "As ordenações por score não foram renomeadas.");
                sort.SelectedValue = "Maior score"; Layout(window, 1280, 1300);
                Require(list.Items.Cast<UnitPatientRecord>().Select(patient => patient.FullName).SequenceEqual(["Bia Sem Consulta", "Ana Alta", "Edu Sem Pontos", "Caio Baixo", "Duda Antiga"]), "Ordem por maior score incorreta (1000 de quem não tem histórico primeiro; sem dado da API vai para o fim).");
                sort.SelectedValue = "Menor score"; Layout(window, 1280, 1300);
                Require(list.Items.Cast<UnitPatientRecord>().Select(patient => patient.FullName).SequenceEqual(["Caio Baixo", "Edu Sem Pontos", "Ana Alta", "Bia Sem Consulta", "Duda Antiga"]), "Ordem por menor score incorreta (sem dado da API vai para o fim).");
                SavePreview(Layout(window, 1280, 1300), Path.Combine(previewRoot, "obs-patients-participation-light.png"));
                sort.SelectedValue = "Mais recentes";
                const string group = "Score nos últimos 12 meses";
                DetailField Field(ItemDetails details, string label) => details.Fields.Single(field => field.Label == label && field.Group == group);
                var alta = vm.BuildDetails(vm.ObsRegistry.Single(p => p.FullName == "Ana Alta"))!; var sem = vm.BuildDetails(vm.ObsRegistry.Single(p => p.FullName == "Bia Sem Consulta"))!;
                var baixa = vm.BuildDetails(vm.ObsRegistry.Single(p => p.FullName == "Caio Baixo"))!; var antiga = vm.BuildDetails(vm.ObsRegistry.Single(p => p.FullName == "Duda Antiga"))!;
                var edu = vm.BuildDetails(vm.ObsRegistry.Single(p => p.FullName == "Edu Sem Pontos"))!;
                Require(Field(alta, "Score").Value == "900 · Excelente" && Field(alta, "Faixa").Value == "Excelente" && Field(alta, "Concluídas").Value == "9" && Field(alta, "Faltas").Value == "1" && Field(alta, "Cancelamentos").Value == "Nenhum feito pelo paciente", "Ficha com participação incorreta.");
                Require(Field(baixa, "Cancelamentos").Value == "2 feitos pelo paciente (peso somado 1,4)" && Field(baixa, "Score").Value == "429 · Regular" && Field(baixa, "Faixa").Value == "Regular", "Ficha não mostra os cancelamentos do paciente com o peso e o score em pontos.");
                Require(Field(edu, "Score").Value == "750 · Bom" && Field(edu, "Faixa").Value == "Bom", "Sem os campos novos da API o cliente deveria calcular 750 pontos, faixa Bom.");
                Require(Field(alta, "Efeito na Vaga Relâmpago").Value == WorkspaceViewModel.ScorePriorityText && WorkspaceViewModel.ScorePriorityText.Contains("não muda a prioridade clínica", StringComparison.Ordinal), "A ficha deveria explicar o efeito do score na Vaga Relâmpago.");
                Require(Field(sem, "Score").Value == "1000 · sem consultas encerradas ainda" && Field(sem, "Faixa").Value == "Excelente" && Field(sem, "Como é calculado").Value.StartsWith("Pontos de 0 a 1000 = comparecimento × 1000", StringComparison.Ordinal), "Sem consulta encerrada a ficha deveria mostrar 1000 e explicar o cálculo.");
                Require(Field(antiga, "Score").Value == "—" && Field(antiga, "Como é calculado").Value.Contains("A API não enviou"), "Sem o campo na API a ficha deveria dizer que ele não veio.");
                var popup = new ItemDetailsWindow(baixa, new ClipboardFixture()) { Owner = window, WindowStartupLocation = WindowStartupLocation.Manual, Left = -10000, Top = -10000, ShowActivated = false };
                try
                {
                    popup.Show(); popup.UpdateLayout(); Descendants<ScrollViewer>(popup).First().ScrollToEnd(); popup.UpdateLayout();
                    Require(VisibleTexts(popup).Contains(group) && VisibleTexts(popup).Contains("2 feitos pelo paciente (peso somado 1,4)"), "A janela da ficha não mostra o score nos últimos 12 meses.");
                    SavePreview(popup, Path.Combine(previewRoot, "obs-patient-participation-details-light.png"));
                }
                finally { popup.Close(); }
                window.ViewModel.SetAppearance(Appearance.Dark);
                popup = new ItemDetailsWindow(alta, new ClipboardFixture()) { Owner = window, WindowStartupLocation = WindowStartupLocation.Manual, Left = -10000, Top = -10000, ShowActivated = false };
                try { popup.Show(); popup.UpdateLayout(); Descendants<ScrollViewer>(popup).First().ScrollToEnd(); popup.UpdateLayout(); SavePreview(popup, Path.Combine(previewRoot, "obs-patient-participation-details-dark.png")); }
                finally { popup.Close(); }
            }
            finally { CloseWindow(window); }
            // Perfil municipal: a consulta nominal da Secretaria não traz nem mostra participação.
            using var networkFixture = new ApiFixture();
            networkFixture.AddUnit("UBS Rede");
            var municipal = OpenMunicipal(networkFixture, storageRoot, "sec-no-participation", Appearance.Light);
            try
            {
                var vm = LoginObs(municipal); vm.SelectedUnit = vm.Units.Single();
                var content = Show(municipal, "units"); Click(municipal, content, "Consultar pacientes da UBS"); content = Layout(municipal, 1280, 900);
                Require(!GridNamed(content, "Pacientes da UBS selecionada").Columns.Any(column => column.Header?.ToString()?.Contains("Participação", StringComparison.Ordinal) == true || column.Header?.ToString()?.Contains("Score", StringComparison.Ordinal) == true) && !VisibleTexts(content).Any(text => text.Contains("Participação", StringComparison.Ordinal) || text.Contains("Score (12 meses)", StringComparison.Ordinal)), "A Secretaria não deveria mostrar o score nem a participação do paciente.");
                Require(vm.UnitRegistry.All(record => record.Attendance is null), "A consulta municipal trouxe participação.");
            }
            finally { CloseWindow(municipal); }
        });
        Check("OBS Vaga Relâmpago: a ficha lê as ofertas da vaga (uma requisição), em ordem, com estado vazio e falha explicada", () =>
        {
            using var fixture = new ApiFixture();
            var unit = fixture.AddUnit("UBS Ofertas");
            var start = DateTimeOffset.UtcNow.AddDays(1);
            var withOffers = fixture.AddSlot(unit.Id, new UnitLightningSlot(Guid.NewGuid(), Guid.NewGuid(), "Cardiologia", "Dra. Teste", start, start.AddMinutes(30), "accepted", DateTimeOffset.UtcNow.AddHours(-2)));
            var withoutOffers = fixture.AddSlot(unit.Id, new UnitLightningSlot(Guid.NewGuid(), Guid.NewGuid(), "Pediatria", "Dr. Teste", start.AddHours(1), start.AddHours(1).AddMinutes(30), "open", DateTimeOffset.UtcNow.AddHours(-1)));
            var baseTime = DateTimeOffset.UtcNow.AddMinutes(-30);
            fixture.Offers[withOffers.Id] = [new(Guid.NewGuid(), "Maria Recusou", "declined", baseTime.AddMinutes(10)), new(Guid.NewGuid(), "João Expirou", "expired", baseTime.AddMinutes(20)), new(Guid.NewGuid(), "Lia Aceitou", "accepted", baseTime.AddMinutes(30))];
            var window = OpenOperational(fixture, storageRoot, "slot-offers", Appearance.Light);
            try
            {
                var vm = LoginObs(window);
                window.ViewModel.Navigate("slots"); Settle(vm);
                var offersBefore = fixture.Count("GET", "/offers");
                var task = vm.BuildDetailsAsync(vm.UnitLightningSlots.Single(slot => slot.Id == withOffers.Id)); Pump(task);
                var details = task.Result!;
                Require(fixture.Count("GET", "/offers") == offersBefore + 1, "Abrir a ficha deveria ler as ofertas uma vez.");
                var offers = details.Fields.Where(field => field.Group == "Ofertas da vaga").ToArray();
                Require(offers.Select(field => field.Label).SequenceEqual(["1ª oferta", "2ª oferta", "3ª oferta"]), "As ofertas deveriam aparecer em ordem, numeradas.");
                Require(offers[0].Value == $"Maria Recusou · Recusada · prazo {baseTime.AddMinutes(10).ToLocalTime():dd/MM/yyyy HH:mm}" && offers[1].Value.StartsWith("João Expirou · Expirada · prazo", StringComparison.Ordinal) && offers[2].Value.StartsWith("Lia Aceitou · Aceita · prazo", StringComparison.Ordinal), "Paciente, situação traduzida ou prazo incorretos: " + string.Join(" | ", offers.Select(field => field.Value)));
                Require(!details.Fields.Any(field => field.Value.Contains("529", StringComparison.Ordinal)) && details.Fields.Any(field => field.Label == "Situação" && field.Value == "Aceita"), "A ficha não tem os dados da vaga ou expôs CPF.");
                var popup = new ItemDetailsWindow(details, new ClipboardFixture()) { Owner = window, WindowStartupLocation = WindowStartupLocation.Manual, Left = -10000, Top = -10000, ShowActivated = false };
                try
                {
                    popup.Show(); popup.UpdateLayout();
                    var shown = VisibleTexts(popup);
                    Require(shown.Contains(offers[0].Value) && shown.Contains("1ª oferta"), "A janela não mostra as ofertas.");
                    SavePreview(popup, Path.Combine(previewRoot, "obs-slot-offers-light.png"));
                    window.ViewModel.SetAppearance(Appearance.Dark); popup.UpdateLayout();
                    SavePreview(popup, Path.Combine(previewRoot, "obs-slot-offers-dark.png"));
                }
                finally { popup.Close(); window.ViewModel.SetAppearance(Appearance.Light); }
                var emptyTask = vm.BuildDetailsAsync(vm.UnitLightningSlots.Single(slot => slot.Id == withoutOffers.Id)); Pump(emptyTask);
                Require(emptyTask.Result!.Fields.Single(field => field.Group == "Ofertas da vaga").Value == "Nenhuma oferta foi feita para esta vaga até agora.", "Vaga sem ofertas deveria mostrar o estado vazio.");
                fixture.FailReads = true;
                var failedTask = vm.BuildDetailsAsync(vm.UnitLightningSlots.Single(slot => slot.Id == withOffers.Id)); Pump(failedTask);
                Require(failedTask.Result!.Fields.Single(field => field.Group == "Ofertas da vaga").Value.StartsWith("Não foi possível carregar as ofertas", StringComparison.Ordinal) && vm.MessageKind == MessageKind.Error, "A falha na leitura das ofertas deveria ser explicada na ficha e na faixa.");
                fixture.FailReads = false;
            }
            finally { CloseWindow(window); }
        });
        Check("OBS: base da UBS indisponível ou desatualizada mostra orientação específica, preserva a UBS anterior e não repete o erro a cada ciclo", () =>
        {
            using var fixture = new ApiFixture();
            var good = fixture.AddUnit("UBS Boa", "Centro"); var broken = fixture.AddUnit("UBS Quebrada", "Sul");
            fixture.AddRegistry(good.Id, "Ana Souza");
            fixture.OutdatedUnits.Add(broken.Id);
            var window = OpenOperational(fixture, storageRoot, "unit-base-down", Appearance.Light);
            try
            {
                var vm = LoginObs(window);
                vm.CandidateObsUnit = good; Pump(vm.ConfirmObsUnitAsync());
                Require(vm.ActiveObsUnit?.Id == good.Id && vm.ObsRegistry.Count == 1, "Pré-condição: UBS boa ativa.");
                var shown = new List<string>();
                vm.PropertyChanged += (_, change) => { if (change.PropertyName == nameof(WorkspaceViewModel.Message) && vm.Message.Length > 0) shown.Add(vm.Message); };
                vm.CandidateObsUnit = broken; Pump(vm.ConfirmObsUnitAsync());
                Require(vm.ActiveObsUnit?.Id == good.Id && vm.ObsRegistry.Count == 1 && vm.ObsRegistry[0].FullName == "Ana Souza", "A UBS anterior e seus dados deveriam ser preservados.");
                Require(vm.MessageKind == MessageKind.Error && vm.Message.Contains("Secretaria") && vm.Message.Contains("desatualizada") && vm.Message.Contains("UBS"), "Mensagem específica de base desatualizada ausente: " + vm.Message);
                var content = Layout(window, 1280, 900);
                Require(Descendants<Border>(content).Any(border => AutomationProperties.GetName(border) == "Retorno da ação" && border.IsVisible) && FullTexts(content).Any(text => text.Contains("Procure a Secretaria", StringComparison.Ordinal)), "A faixa comum não mostrou a orientação.");
                SavePreview(content, Path.Combine(previewRoot, "obs-base-indisponivel-light.png"));
                fixture.OutdatedUnits.Clear(); fixture.UnavailableUnits.Add(broken.Id);
                Pump(vm.ConfirmObsUnitAsync());
                Require(vm.ActiveObsUnit?.Id == good.Id && vm.Message.Contains("Secretaria") && vm.Message.Contains("indisponível"), "503 genérico da UBS deveria virar a mesma orientação: " + vm.Message);
                // A UBS ativa perde a base no meio da sessão: o erro aparece uma vez e os dados ficam marcados como antigos.
                shown.Clear(); vm.DismissMessageCommand.Execute(null); fixture.UnavailableUnits.Clear(); fixture.UnavailableUnits.Add(good.Id);
                for (var cycle = 0; cycle < 3; cycle++) Pump(vm.RefreshAutomaticallyAsync());
                Require(shown.Count == 1 && vm.Message.Contains("Secretaria") && vm.IsDataStale && vm.ObsRegistry.Count == 1, $"O erro da base deveria aparecer uma vez ({shown.Count}), marcar os dados como antigos e mantê-los.");
            }
            finally { CloseWindow(window); }
            // UBS única com a base desatualizada já no login: nada é carregado e nenhum ciclo insiste.
            using var single = new ApiFixture();
            var only = single.AddUnit("UBS Única Quebrada"); single.OutdatedUnits.Add(only.Id);
            var shell = OpenOperational(single, storageRoot, "unit-base-down-single", Appearance.Dark);
            try
            {
                var vm = LoginObs(shell);
                Require(vm.IsAuthenticated && vm.ActiveObsUnit == null && vm.Message.Contains("Secretaria"), "Login com a única UBS desatualizada deveria orientar, sem ativar a UBS: " + vm.Message);
                var requests = single.Requests.Count;
                for (var cycle = 0; cycle < 3; cycle++) Pump(vm.RefreshAutomaticallyAsync());
                Require(single.Requests.Count == requests, "O ciclo de 20 s continuou chamando a API de uma UBS que não abriu.");
                SavePreview(Layout(shell, 1280, 900), Path.Combine(previewRoot, "obs-base-indisponivel-dark.png"));
            }
            finally { CloseWindow(shell); }
        });
    }
}
