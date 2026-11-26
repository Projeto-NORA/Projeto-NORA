using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Nora.Desktop.Models;
using Nora.Desktop.Services;
using Nora.Desktop.ViewModels;
using Nora.Desktop.Views;

namespace Nora.Desktop.Checks;

/// <summary>OBS: Painel (consultórios, capacidade, alertas), Indicadores (capacidade e metas) e Alertas; histórico por cursor.</summary>
internal static partial class Program
{
    private static void AgeAlertsRead(WorkspaceViewModel vm, TimeSpan age) =>
        typeof(WorkspaceViewModel).GetField("alertsAttemptedAt", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(vm, (DateTimeOffset?)(DateTimeOffset.Now - age));

    private static AlertItem[] SampleAlerts(HealthUnit unit) =>
    [
        new("high-queue", unit.Id, unit.Name, "Cardiologia", 23, 20, null, null),
        new("long-wait", unit.Id, unit.Name, "Pediatria", 45, 30, new DateTimeOffset(2026, 10, 2, 8, 30, 0, TimeSpan.FromHours(-3)), null),
        new("missed-rate", unit.Id, unit.Name, null, 0.25, 0.2, null, null),
        new("occupancy", unit.Id, unit.Name, null, 0.97, 0.95, null, null),
        new("goal-off", unit.Id, unit.Name, null, 0.6, 0.8, null, GoalMetric.Score)
    ];

    private static void RunSchedulingInsightsChecks(Application application, string storageRoot, string[] args)
    {
        var previewRoot = PreviewRoot(args);
        Check("OBS Alertas: aviso por novidade desaparece ao abrir e reaparece apenas para outro alerta", () =>
        {
            using var fixture = new ApiFixture();
            var unit = fixture.AddUnit("UBS Avisos"); var sample = SampleAlerts(unit);
            fixture.AddAlert(unit.Id, sample[0]);
            var window = OpenOperational(fixture, storageRoot, "alert-badge", Appearance.Light);
            try
            {
                var vm = LoginObs(window); var nav = window.ViewModel.Navigation.SelectMany(item => item.Children.Prepend(item)).Single(item => item.Id == "alerts" && !item.HasChildren);
                Require(nav.Badge == 1, "O alerta inicial deveria aparecer no menu.");
                window.ViewModel.Navigate("alerts"); Settle(vm);
                Require(nav.Badge == 0 && vm.UnitAlerts.Count == 1, "Abrir a central deveria ocultar o aviso sem apagar o alerta.");
                window.ViewModel.Navigate("queue"); Settle(vm);
                AgeAlertsRead(vm, TimeSpan.FromMinutes(6)); Pump(vm.RefreshAutomaticallyAsync());
                Require(nav.Badge == 0, "O mesmo alerta não deveria reaparecer como novidade.");
                fixture.AddAlert(unit.Id, sample[1]); AgeAlertsRead(vm, TimeSpan.FromMinutes(6)); Pump(vm.RefreshAutomaticallyAsync());
                Require(nav.Badge == 1 && vm.UnitAlerts.Count == 2, "Um novo alerta deveria ser contado mesmo fora da central.");
            }
            finally { CloseWindow(window); }
        });
        Check("OBS Painel: consultórios ativos (em atendimento agora e com grade hoje), capacidade dos próximos 7 dias e alertas reais; o ciclo de 20 s só relê o que é leve", () =>
        {
            using var fixture = new ApiFixture();
            var unit = fixture.AddUnit("UBS Painel Novo", "Centro");
            fixture.SetActiveRooms(unit.Id, 2, 5); fixture.SetCapacity(unit.Id, new CapacityMetrics(40, 30, 0.75));
            fixture.AddAlert(unit.Id, SampleAlerts(unit)[0]);
            var window = OpenOperational(fixture, storageRoot, "panel-scheduling", Appearance.Light);
            try
            {
                var vm = LoginObs(window);
                Require(fixture.CapacityQueries.Count == 1 && WindowDays(fixture.CapacityQueries[0].Query) == 7 && fixture.Count("GET", "/active-rooms") == 1 && fixture.Count("GET", "/alerts") == 1, "A abertura da UBS deveria ler consultórios ativos, alertas e a capacidade de 7 dias uma vez cada.");
                Require(vm.ActiveRoomsInUseValue == "2" && vm.ActiveRoomsPlannedValue == "5" && vm.PanelOfferedValue == "40" && vm.PanelBookedValue == "30" && vm.PanelOccupancyValue == "75,0%" && vm.PanelAlertsTitle == "1 alerta ativo", "Os números reais do Painel não foram aplicados.");
                var content = Layout(window, 1280, 1800);
                var texts = FullTexts(content);
                Require(new[] { "Consultórios ativos", "em atendimento agora", "com grade hoje", "1 alerta ativo", "Fila alta em Cardiologia: 23 pessoas (limite 20 pessoas)." }.All(texts.Contains) && !texts.Contains("Capacidade dos próximos 7 dias"), "O Painel deveria mostrar consultórios e alerta, sem o cartão de capacidade: " + string.Join(" | ", texts.Where(text => text.Length < 60).Take(40)));
                Require(!texts.Any(text => text.Contains("Indisponível: depende do cadastro", StringComparison.Ordinal)) && !texts.Contains("Nenhum alerta avaliado ainda"), "Sobrou texto de funcionalidade pendente no Painel.");
                SaveScrolled(window, Path.Combine(previewRoot, "obs-painel-agenda-light.png"), 1280, 900);
                window.ViewModel.SetAppearance(Appearance.Dark); Layout(window, 1280, 1800); SaveScrolled(window, Path.Combine(previewRoot, "obs-painel-agenda-dark.png"), 1280, 900); window.ViewModel.SetAppearance(Appearance.Light);
                for (var cycle = 0; cycle < 3; cycle++) Pump(vm.RefreshAutomaticallyAsync());
                Require(fixture.CapacityQueries.Count == 1 && fixture.Count("GET", "/alerts") == 1 && fixture.Count("GET", "/active-rooms") == 4, "O ciclo de 20 s deveria reler só os consultórios ativos (leve); capacidade e alertas esperam 5 minutos.");
                AgePanelRead(vm, TimeSpan.FromMinutes(6)); Pump(vm.RefreshAutomaticallyAsync());
                Require(fixture.CapacityQueries.Count == 2 && fixture.Count("GET", "/alerts") == 2, "Passados 5 minutos, o ciclo deveria reler capacidade e alertas uma vez.");
                // Sem grade, sem alertas.
                fixture.SetCapacity(unit.Id, new CapacityMetrics(0, 0, null)); fixture.SetActiveRooms(unit.Id, 0, 0); fixture.Reset(unit.Id, alerts: true);
                vm.RefreshCommand.Execute(null); Settle(vm); content = Layout(window, 1280, 1800);
                Require(vm.PanelOfferedValue == "0" && vm.PanelOccupancyValue == "—" && vm.PanelCapacityHint.Contains("Nenhum horário ofertado") && vm.PanelAlertsTitle == "Nenhum alerta" && vm.ActiveRoomsInUseValue == "0", "Sem grade e sem alertas o Painel deveria dizer isso, sem inventar número: " + vm.PanelCapacityHint);
                // Falha só das rotas novas: a UBS abre, o cartão avisa e mantém a leitura anterior.
                fixture.SetCapacity(unit.Id, new CapacityMetrics(40, 30, 0.75)); fixture.SetActiveRooms(unit.Id, 2, 5); fixture.AddAlert(unit.Id, SampleAlerts(unit)[0]); vm.RefreshCommand.Execute(null); Settle(vm);
                Require(vm.PanelAlertsTitle == "1 alerta ativo" && vm.ActiveRoomsInUseValue == "2", "Os números deveriam voltar depois de a grade e o alerta reaparecerem.");
                fixture.FailActiveRooms = true; fixture.FailAlerts = true; vm.RefreshCommand.Execute(null); Settle(vm); content = Layout(window, 1280, 1800);
                Require(vm.HasActiveObsUnit && vm.ActiveRoomsInUseValue == "2" && vm.ActiveRoomsHint.Contains("última leitura não concluiu") && vm.PanelAlertsText.Contains("não concluiu") && FullTexts(content).Contains(vm.ActiveRoomsHint), "Falha das rotas novas deveria avisar nos cartões e manter os números anteriores.");
            }
            finally { CloseWindow(window); }
            using var failing = new ApiFixture(); var broken = failing.AddUnit("UBS Rotas Novas Falham"); failing.FailActiveRooms = true; failing.FailAlerts = true;
            var shell = OpenOperational(failing, storageRoot, "panel-scheduling-fail", Appearance.Light);
            try
            {
                var vm = LoginObs(shell);
                Require(vm.HasActiveObsUnit && vm.ActiveRoomsInUseValue == "—" && vm.ActiveRoomsHint == "Não foi possível ler os consultórios agora." && vm.PanelAlertsTitle == "Alertas indisponíveis agora" && !vm.PanelAlertsTitle.Contains("Nenhum"), "Sem a leitura nova a UBS deveria abrir com avisos honestos (não com zero nem 'Nenhum alerta'): " + vm.PanelAlertsTitle);
            }
            finally { CloseWindow(shell); }
        });
        Check("OBS Indicadores: capacidade do período e metas nos quatro estados (atingida, perto, fora, sem dado), com meta da UBS sobrescrevendo a da rede", () =>
        {
            using var fixture = new ApiFixture();
            var unit = fixture.AddUnit("UBS Metas", "Centro");
            fixture.SetCapacity(unit.Id, new CapacityMetrics(100, 80, 0.8));
            fixture.Goals.Add(new Goal(GoalMetric.Score, null, 0.70m)); fixture.Goals.Add(new Goal(GoalMetric.MissedRate, null, 0.19m)); fixture.Goals.Add(new Goal(GoalMetric.AverageWait, null, 60m));
            var window = OpenOperational(fixture, storageRoot, "unit-goals", Appearance.Light);
            try
            {
                var vm = LoginObs(window);
                var content = Show(window, "indicators", 1280, 2800);
                Require(UnitCard(vm, "Ocupação da agenda") is { Value: "80,0%", Tone: Tone.Blue }, "O card de ocupação da agenda do período está incorreto.");
                Require(new[] { "Ocupação da agenda", "80,0%", "Metas da Secretaria" }.All(FullTexts(content).Contains) && !FullTexts(content).Contains("Capacidade da agenda no período"), "A ocupação da agenda ou as metas não aparecem em Indicadores (a capacidade em três cards saiu).");
                Require(!FullTexts(content).Any(text => text.Contains("previsto para uma próxima etapa", StringComparison.Ordinal)), "Sobrou o aviso de funcionalidade pendente em Indicadores.");
                GoalCell Cell(string metric) => vm.UnitGoalCells.Single(cell => cell.Title == GoalMetric.Title(metric));
                Require(Cell(GoalMetric.Score) is { Status: "met", StatusDisplay: "Atingida" } && Cell(GoalMetric.Score).Detail == "77,9% · meta mín. 70,0%", "Comparecimento deveria estar atingido: " + Cell(GoalMetric.Score).Detail);
                Require(Cell(GoalMetric.MissedRate) is { Status: "near", StatusDisplay: "Perto da meta" } && Cell(GoalMetric.MissedRate).Detail == "20,0% · meta máx. 19,0%", "A taxa de faltas deveria estar perto da meta: " + Cell(GoalMetric.MissedRate).Detail);
                Require(Cell(GoalMetric.AverageWait) is { Status: "off", StatusDisplay: "Fora da meta" } && Cell(GoalMetric.AverageWait).Detail == "1 h 35 min · meta máx. 1 h", "A espera deveria estar fora da meta: " + Cell(GoalMetric.AverageWait).Detail);
                var shown = FullTexts(content);
                Require(new[] { "Atingida", "Perto da meta", "Fora da meta" }.All(shown.Contains) && Descendants<TextBlock>(content).Count(text => text.FontFamily.Source.Contains("Segoe MDL2", StringComparison.Ordinal) && text.IsVisible && text.Text is "" or "" or "") >= 3, "O estado das metas deveria vir escrito e com ícone.");
                foreach (var mode in new[] { Appearance.Light, Appearance.Dark, Appearance.System })
                {
                    window.ViewModel.SetAppearance(mode); Layout(window, 1280, 2800);
                    if (mode != Appearance.System) SaveScrolled(window, Path.Combine(previewRoot, $"obs-indicadores-metas-{mode.ToString().ToLowerInvariant()}.png"), 1280, 900);
                }
                window.ViewModel.SetAppearance(Appearance.Light);
                // Meta própria da UBS sobrescreve a da rede (uma leitura nova de indicadores).
                fixture.Goals.Add(new Goal(GoalMetric.Score, unit.Id, 0.95m)); var reads = fixture.UnitMetricsQueries.Count; vm.UnitPeriodDays = 90; Settle(vm);
                Require(fixture.UnitMetricsQueries.Count == reads + 1 && Cell(GoalMetric.Score) is { Status: "off" } && Cell(GoalMetric.Score).Detail.EndsWith("meta mín. 95,0%", StringComparison.Ordinal), "A meta da UBS deveria valer no lugar da meta da rede: " + Cell(GoalMetric.Score).Detail);
                // Sem consulta encerrada nem fila: sem dado.
                fixture.NoClosedAppointments = true; vm.UnitPeriodDays = 7; Settle(vm);
                Require(vm.UnitGoalCells.All(cell => cell is { Status: "no-data", StatusDisplay: "Sem dado" }) && Cell(GoalMetric.Score).Detail.StartsWith("Sem valor no período", StringComparison.Ordinal), "Sem dado no período as três metas deveriam aparecer como Sem dado.");
                content = Layout(window, 1280, 2800); Require(FullTexts(content).Count(text => text == "Sem dado") == 3, "O estado Sem dado deveria estar escrito nas três metas.");
                SaveScrolled(window, Path.Combine(previewRoot, "obs-indicadores-metas-sem-dado-light.png"), 1280, 900);
                // Sem nenhuma meta.
                fixture.Goals.Clear(); fixture.NoClosedAppointments = false; vm.UnitPeriodDays = 30; Settle(vm); content = Layout(window, 1280, 2800);
                Require(vm.UnitGoalCells.Count == 0 && vm.UnitGoalsState.StartsWith("Nenhuma meta definida pela Secretaria", StringComparison.Ordinal) && FullTexts(content).Contains(vm.UnitGoalsState), "Sem metas o estado vazio deveria ser explicado.");
            }
            finally { CloseWindow(window); }
        });
        Check("OBS Alertas: cada alerta mostra tipo, especialidade, valor, limite e desde quando; estado vazio honesto; falha não vira 'nenhum alerta'; leitura só depois de 5 minutos", () =>
        {
            using var fixture = new ApiFixture();
            var unit = fixture.AddUnit("UBS Alertas Reais", "Centro");
            foreach (var item in SampleAlerts(unit)) fixture.AddAlert(unit.Id, item);
            var window = OpenOperational(fixture, storageRoot, "unit-alerts-real", Appearance.Light);
            try
            {
                var vm = LoginObs(window);
                var reads = fixture.Count("GET", "/alerts");
                var content = Show(window, "alerts", 1280, 2000);
                Require(fixture.Count("GET", "/alerts") == reads && vm.UnitAlerts.Count == 5, "Entrar em Alertas logo depois do Painel não deveria reler (já está atualizado).");
                var texts = FullTexts(content);
                // Listagem aberta: o tipo (com a especialidade, quando existe) na primeira linha, valor | limite | desde na segunda e a etiqueta do tipo à direita.
                Require(new[] { "Fila alta · Cardiologia", "Espera longa · Pediatria", "Taxa de faltas acima do limite", "Ocupação da grade acima do limite", "Meta fora: Comparecimento" }.All(texts.Contains), "Os tipos de alerta não aparecem: " + string.Join(" | ", texts.Where(text => text.Length < 50)));
                var sinceText = new DateTimeOffset(2026, 10, 2, 8, 30, 0, TimeSpan.FromHours(-3)).ToLocalTime().ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);
                Require(new[] { "Valor: 23 pessoas | Limite: 20 pessoas", "Valor: 45 dias | Limite: 30 dias | Desde: " + sinceText, "Valor: 25,0% | Limite: 20,0%", "Valor: 97,0% | Limite: 95,0%", "Valor: 60,0% | Limite: mín. 80,0%" }.All(texts.Contains), "Valor, limite ou 'desde' ausentes: " + string.Join(" | ", texts.Where(text => text.StartsWith("Valor", StringComparison.Ordinal))));
                Require(new[] { "Fila", "Espera", "Faltas", "Ocupação", "Meta" }.All(tag => texts.Count(text => text == tag) == 1) && !texts.Any(text => text.Contains("Desde: —", StringComparison.Ordinal) || text.StartsWith("Especialidade:", StringComparison.Ordinal)), "Cada alerta tem uma etiqueta do tipo; o 'desde' só aparece quando existe e a especialidade fica no título.");
                Require(!texts.Any(text => text.Contains("Nenhum alerta é avaliado ainda", StringComparison.Ordinal)) && !texts.Any(text => text.StartsWith("Nenhum alerta.", StringComparison.Ordinal)), "Com alertas não deveria aparecer o estado vazio.");
                SavePreview(content, Path.Combine(previewRoot, "obs-alertas-reais-light.png"));
                window.ViewModel.SetAppearance(Appearance.Dark); content = Layout(window, 1280, 2000); SavePreview(content, Path.Combine(previewRoot, "obs-alertas-reais-dark.png")); window.ViewModel.SetAppearance(Appearance.Light);
                // Sem botão Atualizar: a tela de alertas reavalia sozinha no ciclo (a cada 15 s ou mais). Sem alerta: estado vazio honesto com a hora da leitura.
                Require(!Descendants<Button>(content).Any(button => AutomationProperties.GetName(button) == "Atualizar alertas"), "A tela de alertas não deveria ter o botão Atualizar alertas.");
                fixture.Reset(unit.Id, alerts: true); RefreshAlertsNow(vm); content = Layout(window, 1280, 2000);
                Require(fixture.Count("GET", "/alerts") == reads + 1 && vm.UnitAlerts.Count == 0 && vm.UnitAlertsState.StartsWith("Nenhum alerta. Nenhum critério", StringComparison.Ordinal), "Sem alertas o estado vazio deveria dizer que nada passou do limite: " + vm.UnitAlertsState);
                content = Layout(window, 1280, 2000); Require(FullTexts(content).Any(text => text.StartsWith("Nenhum alerta. Nenhum critério", StringComparison.Ordinal)), "O estado vazio não aparece na tela.");
                SavePreview(content, Path.Combine(previewRoot, "obs-alertas-vazio-light.png"));
                // Sair e voltar não reavalia logo em seguida (a tela de alertas reavalia a cada 15 s ou mais; fora dela, a cada 5 minutos).
                window.ViewModel.Navigate("queue"); Settle(vm); window.ViewModel.Navigate("alerts"); Settle(vm); Pump(vm.RefreshAutomaticallyAsync());
                Require(fixture.Count("GET", "/alerts") == reads + 1, "Voltar à aba e o ciclo de 20 s não deveriam reavaliar os alertas antes de 15 s.");
                AgeAlertsRead(vm, TimeSpan.FromMinutes(6)); Pump(vm.RefreshAutomaticallyAsync());
                Require(fixture.Count("GET", "/alerts") == reads + 2, "Passados mais de 15 s o ciclo com a aba aberta deveria reavaliar os alertas uma vez.");
                // Falha: a lista anterior continua e o erro aparece; nunca vira "nenhum alerta".
                fixture.AddAlert(unit.Id, SampleAlerts(unit)[0]); RefreshAlertsNow(vm); Require(vm.UnitAlerts.Count == 1, "Pré-condição: um alerta.");
                fixture.FailAlerts = true; RefreshAlertsNow(vm);
                Require(vm.UnitAlerts.Count == 1 && vm.UnitAlertsState.Contains("não concluiu") && vm.PanelAlertsText.Contains("não concluiu"), "A falha deveria avisar na tela e manter a última avaliação: " + vm.UnitAlertsState);
            }
            finally { CloseWindow(window); }
            using var down = new ApiFixture(); var other = down.AddUnit("UBS Sem Avaliação"); down.FailAlerts = true;
            var shell = OpenOperational(down, storageRoot, "unit-alerts-down", Appearance.Dark);
            try
            {
                var vm = LoginObs(shell); var content = Show(shell, "alerts", 1280, 1400);
                Require(vm.UnitAlertsState.StartsWith("Não foi possível avaliar os alertas agora", StringComparison.Ordinal) && !vm.UnitAlertsState.Contains("Nenhum alerta") && FullTexts(content).Contains(vm.UnitAlertsState), "Sem avaliação o estado deveria ser de falha, nunca 'Nenhum alerta': " + vm.UnitAlertsState);
                SavePreview(content, Path.Combine(previewRoot, "obs-alertas-falha-dark.png"));
            }
            finally { CloseWindow(shell); }
        });
    }
}
