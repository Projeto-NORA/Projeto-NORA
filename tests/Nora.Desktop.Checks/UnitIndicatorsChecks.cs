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
        Check("OBS Indicadores: cada período (hoje, 7, 30, 90, personalizado) faz uma única requisição e só a consulta mais nova altera a tela", () =>
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
                Require(period.Items.Cast<PeriodOption>().Select(option => option.Label).SequenceEqual(["Hoje", "Últimos 7 dias", "Últimos 14 dias", "Últimos 30 dias", "Últimos 90 dias", "Últimos 180 dias", "Últimos 365 dias", "Personalizado"]), "Opções de período incorretas.");
                period.SelectedValue = 1; Settle(vm); ExpectOneMore("Hoje", 1);
                Require(vm.UnitMetricsScopeLabel.StartsWith($"Números de {DisplayFormat.Date(DateOnly.FromDateTime(DateTime.Today))} a {DisplayFormat.Date(DateOnly.FromDateTime(DateTime.Today))}", StringComparison.Ordinal), "O escopo de 'Hoje' não mostra o dia: " + vm.UnitMetricsScopeLabel);
                period.SelectedValue = 7; Settle(vm); ExpectOneMore("7 dias", 7);
                period.SelectedValue = 90; Settle(vm); ExpectOneMore("90 dias", 90);
                period.SelectedValue = 30; Settle(vm); ExpectOneMore("30 dias", 30);
                // Personalizado: escolher não consulta; aplicar valida e consulta uma vez.
                period.SelectedValue = 0; Settle(vm);
                Require(fixture.UnitMetricsQueries.Count == count && vm.IsUnitCustomPeriod, "Escolher 'Personalizado' não deveria consultar.");
                content = Layout(window, 1280, 900);
                (string From, string To, string Fragment)[] invalid = [("31/10/2026", "01/10/2026", "inicial não pode ser posterior"), ("01/01/2026", "03/01/2027", "no máximo 366 dias"), ("abc", "01/10/2026", "formato dd/mm/aaaa")];
                foreach (var (from, to, fragment) in invalid)
                {
                    Input(content, "Data inicial do período da UBS").Text = from; Input(content, "Data final do período da UBS").Text = to;
                    Click(window, content, "Aplicar período da UBS");
                    Require(fixture.UnitMetricsQueries.Count == count && vm.UnitRangeError.Contains(fragment) && HasText(OpenPageFilters(window), vm.UnitRangeError), $"Período inválido {from}–{to} não foi barrado com a mensagem: {vm.UnitRangeError}");
                }
                Input(content, "Data inicial do período da UBS").Text = "01/10/2026"; Input(content, "Data final do período da UBS").Text = "31/10/2026";
                Click(window, content, "Aplicar período da UBS"); ExpectOneMore("Personalizado", 31);
                Require(vm.UnitRangeError == "" && vm.UnitMetricsScopeLabel.StartsWith("Números de 01/10/2026 a 31/10/2026", StringComparison.Ordinal), "O período personalizado não foi aplicado com fim inclusivo: " + vm.UnitMetricsScopeLabel);
                // Intervalo máximo de 366 dias é aceito.
                Input(content, "Data inicial do período da UBS").Text = "01/01/2026"; Input(content, "Data final do período da UBS").Text = "01/01/2027";
                Click(window, content, "Aplicar período da UBS"); ExpectOneMore("Personalizado de 366 dias", 366);
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
                vm.UnitPeriodDays = 90;
                fixture.HeldUnitMetrics[0].SetResult(true);
                PumpUntil(() => fixture.HeldUnitMetrics.Count == 2);
                Require(vm.UnitMetricsScopeLabel == initialScope, "A resposta atrasada (7 dias) foi aplicada depois de o filtro mudar.");
                fixture.HeldUnitMetrics[1].SetResult(true); Settle(vm);
                var expectedFirst = DateOnly.FromDateTime(DateTime.Today.AddDays(-89)).ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
                Require(vm.UnitMetricsScopeLabel.StartsWith("Números de " + expectedFirst, StringComparison.Ordinal) && vm.UnitPeriodDays == 90 && !vm.UnitMetricsLoading, "Só a consulta de 90 dias deveria ficar na tela: " + vm.UnitMetricsScopeLabel);
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
                    Require(UnitCard(vm, "Consultas concluídas").Value == "12" && UnitCard(vm, "Faltas").Value == "3" && UnitCard(vm, "Cancelamentos").Value == "2" && UnitCard(vm, "Cancelamentos").Hint == "1 feito pelo paciente", "Cartões de consultas incorretos.");
                    Require(UnitCard(vm, "Comparecimento").Value == "77,9%" && UnitCard(vm, "Espera média da fila").Value == "1 h 35 min" && UnitCard(vm, "Maior espera atual").Value == "1 d 1 h", "Score ou esperas incorretos: " + UnitCard(vm, "Comparecimento").Value + " / " + UnitCard(vm, "Espera média da fila").Value + " / " + UnitCard(vm, "Maior espera atual").Value);
                    foreach (var card in vm.UnitOverviewCards.Concat(vm.UnitLightningCards)) Require(HasText(content, card.Title) && HasText(content, card.Value), $"{mode}: cartão não renderizado: {card.Title}");
                    Require(vm.UnitLightningCards.Select(card => card.Title + "=" + card.Value).SequenceEqual(["Vagas registradas=1", "Ofertas aceitas=1", "Ofertas recusadas=2", "Ofertas expiradas=1"]), "Cartões da Vaga Relâmpago incorretos.");
                    Require(Descendants<TextBlock>(content).Any(text => text.Text.Contains("Como é calculado o comparecimento") && text.ToolTip is string tip && tip.StartsWith("Comparecimento = concluídas ÷", StringComparison.Ordinal)), "A explicação do score não aparece na dica.");
                    Require(vm.UnitMetricsScopeLabel.EndsWith("UBS Números", StringComparison.Ordinal), "Título único ou escopo da UBS ausentes.");
                    var table = GridNamed(content, "Indicadores da UBS por especialidade");
                    var cells = VisibleTexts(table);
                    Require(new[] { "Clínica geral", "Pediatria", "82,6%", "75,0%", "2 h" }.All(cells.Contains) && cells.Count(text => text == "—") == 1, "Tabela por especialidade sem os números esperados: " + string.Join(" | ", cells));
                    Require(table.Columns.Single(column => column.Header?.ToString() == "Comparecimento").SortMemberPath == "ScoreSort" && table.Columns.Single(column => column.Header?.ToString() == "Espera média").SortMemberPath == "WaitSort", "Colunas de score e espera ordenariam por texto.");
                    // Série: a API devolve só dois dias; o gráfico cobre os 30 com zero, e a tabela só mostra os dois.
                    var days = vm.UnitChart.Days;
                    Require(days.Count == 30 && vm.UnitChart.Rows.Count == 2 && days.Count(day => day.Completed == 0 && day.Missed == 0 && day.Cancelled == 0) == 28, $"A série deveria cobrir 30 dias com 28 deles zerados: {days.Count} dias.");
                    Require(days[^1].Date == DateOnly.FromDateTime(DateTime.Today) && days[^1].Completed == 2 && days[^1].Cancelled == 1 && days[^5].Completed == 3 && days[^5].Missed == 1, "Os dias com dado não ficaram nas datas certas.");
                    var chart = Descendants<EvolutionChartView>(content).Single();
                    var rectangles = Descendants<System.Windows.Shapes.Rectangle>(chart).ToArray(); // as áreas sensíveis do gráfico (uma por dia, com a dica)
                    Require(rectangles.Where(rectangle => rectangle.ToolTip is string).Select(rectangle => (string)rectangle.ToolTip).SequenceEqual(days.Select(day => day.Description)), "As dicas do gráfico não trazem o número exato de cada dia.");
                    Require(Descendants<System.Windows.Shapes.Polyline>(chart).Count() == 3 && Descendants<System.Windows.Shapes.Ellipse>(chart).Count() == days.Count * 3, $"{mode}: o gráfico deveria ter 3 linhas e {days.Count * 3} pontos.");
                    Require(!Descendants<Expander>(chart).Any(), "O gráfico não deveria ter o expansor da tabela.");
                    var expander = Descendants<Expander>(content).Single(item => AutomationProperties.GetName(item) == "Tabela dos dados do gráfico"); expander.IsExpanded = true; content = Layout(window, 1280, 2600);
                    Require(GridNamed(content, "Consultas por dia").Items.Cast<DailyPoint>().SequenceEqual(vm.UnitChart.Rows), "A tabela do gráfico não traz os mesmos valores.");
                    expander.IsExpanded = false;
                    // Contraste dos textos de apoio e dos ícones de estado sobre a superfície dos cartões.
                    var surface = ((SolidColorBrush)application.FindResource("SurfaceBrush")).Color;
                    Require(Contrast(((SolidColorBrush)application.FindResource("SecondaryBrush")).Color, surface) >= 4.5 && Contrast(((SolidColorBrush)application.FindResource("AccentTextBrush")).Color, surface) >= 4.5, $"{mode}: texto de apoio ou números de destaque sem contraste sobre os cartões.");
                    Require(Contrast(((SolidColorBrush)application.FindResource("SuccessBrush")).Color, surface) >= 3 && Contrast(((SolidColorBrush)application.FindResource("ErrorBrush")).Color, surface) >= 3 && Contrast(((SolidColorBrush)application.FindResource("WarningBrush")).Color, surface) >= 3, $"{mode}: cores das séries sem contraste de 3:1 com a superfície.");
                    if (mode != Appearance.System) SaveScrolled(window, Path.Combine(previewRoot, $"obs-indicators-{mode.ToString().ToLowerInvariant()}.png"), 1280, 900);
                }
                var pieContent = Layout(window, 1280, 2600);
                var pie = Descendants<OutcomePie>(pieContent).Single();
                Require(pie.IsVisible && AutomationProperties.GetName(pie).Contains("12 concluídas, 3 faltas, 2 canceladas"), "O gráfico de distribuição não apresenta os totais reais.");
                vm.UnitSpecialtySearch = "pedi";
                Require(vm.FilteredUnitSpecialtyPerformance.Select(row => row.Name).SequenceEqual(["Pediatria"]), "Busca por especialidade não filtrou a tabela.");
                vm.UnitSpecialtySearch = "sem correspondência";
                Require(!vm.FilteredUnitSpecialtyPerformance.Any() && vm.UnitSpecialtyState == "Nenhuma especialidade corresponde aos filtros.", "A busca sem resultados deveria avisar na tabela.");
                vm.UnitSpecialtySearch = "";
                // Sem consulta encerrada: score nulo e gráfico vazio explicam o motivo.
                window.ViewModel.SetAppearance(Appearance.Light);
                fixture.NoClosedAppointments = true; vm.UnitPeriodDays = 7; Settle(vm);
                var empty = Layout(window, 1280, 2600);
                Require(UnitCard(vm, "Comparecimento").Value == "—" && UnitCard(vm, "Comparecimento").Hint == "Sem consulta encerrada no período para calcular." && Descendants<Border>(empty).Any(border => border.ToolTip?.ToString() == "Sem consulta encerrada no período para calcular.") && vm.UnitChart.State == "Nenhuma consulta com data marcada no período." && HasText(empty, "Nenhuma consulta com data marcada no período.") && HasText(empty, "Nenhuma especialidade com consulta ou fila neste período."), "Score nulo, gráfico vazio e tabela vazia deveriam explicar o motivo.");
                SaveScrolled(window, Path.Combine(previewRoot, "obs-indicators-empty-light.png"), 1280, 900);
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
                var chart = Descendants<EvolutionChartView>(content).Single();
                Require(Descendants<System.Windows.Shapes.Rectangle>(chart).Count(rectangle => rectangle.ToolTip is string) == 7, "O gráfico do Painel deveria ter uma dica por dia da semana.");
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
