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

/// <summary>Secretaria: lista de especialidades, metas, alertas da rede, capacidade e histórico por cursor.</summary>
internal static partial class Program
{
    private static void RunSecretariaSchedulingChecks(Application application, string storageRoot, string[] args)
    {
        var previewRoot = PreviewRoot(args);
        Check("Secretaria Especialidades: lista da rede com criar, renomear, ativar e desativar; 409 de nome duplicado no campo; a demanda por especialidade continua", () =>
        {
            using var fixture = new ApiFixture(); fixture.AddUnit("UBS Esp", "Centro");
            var asked = new List<string>(); Confirmation.Handler = (_, title, _) => { asked.Add(title); return true; };
            var window = OpenMunicipal(fixture, storageRoot, "sec-specialties", Appearance.Light);
            try
            {
                var vm = LoginObs(window);
                Require(fixture.Count("GET", "/specialties") == 0, "As especialidades não deveriam ser lidas antes de abrir a seção.");
                var content = Show(window, "specialties", 1280, 2400);
                Require(fixture.Count("GET", "/specialties") == 1 && vm.SpecialtyCatalogState.StartsWith("Nenhuma especialidade cadastrada", StringComparison.Ordinal) && FullTexts(content).Contains(vm.SpecialtyCatalogState), "Entrar na seção deveria ler a lista uma vez e explicar a lista vazia.");
                Require(Descendants<DataGrid>(content).Any(grid => AutomationProperties.GetName(grid) == "Indicadores por especialidade") && Descendants<DataGrid>(content).Any(grid => AutomationProperties.GetName(grid) == "Lista de especialidades"), "A seção deveria ter a lista da rede e a tabela de demanda.");
                vm.SpecialtyDraftName = "A"; Pump(vm.SaveSpecialtyAsync());
                Require(vm.SpecialtyNameError.Contains("2 a 120") && fixture.Count("POST", "/specialties") == 0, "Nome curto demais foi enviado.");
                vm.SpecialtyDraftName = "  Cardiologia   "; Pump(vm.SaveSpecialtyAsync());
                Require(vm.SpecialtyCatalog.Single() is { Name: "Cardiologia", IsActive: true } && vm.SpecialtyDraftName == "" && vm.MessageKind == MessageKind.Success, "A especialidade não foi criada (espaços sobrando deveriam sair): " + vm.Message);
                vm.SpecialtyDraftName = "cardiología"; Pump(vm.SaveSpecialtyAsync());
                Require(vm.SpecialtyNameError.Contains("Já existe") && vm.SpecialtyCatalog.Count == 1 && vm.MessageKind == MessageKind.Error, "O nome duplicado (409) deveria aparecer no campo: " + vm.SpecialtyNameError);
                content = Layout(window, 1280, 2400); Require(FullTexts(content).Contains(vm.SpecialtyNameError), "O aviso de nome duplicado não aparece no campo.");
                SaveScrolled(window, Path.Combine(previewRoot, "municipal-especialidades-duplicada-light.png"), 1280, 900);
                vm.NewSpecialtyDraft(); vm.SpecialtyDraftName = "Pediatria"; Pump(vm.SaveSpecialtyAsync());
                Require(vm.SpecialtyCatalog.Select(item => item.Name).SequenceEqual(["Cardiologia", "Pediatria"]), "A lista deveria vir ordenada pelo nome.");
                vm.SelectedSpecialty = vm.SpecialtyCatalog.Single(item => item.Name == "Cardiologia"); vm.EditSelectedSpecialty();
                Require(vm.SpecialtyDraftName == "Cardiologia" && vm.SpecialtySaveLabel == "Salvar novo nome", "Editar deveria preencher o nome e mudar o botão.");
                vm.SpecialtyDraftName = "pediatria"; Pump(vm.SaveSpecialtyAsync());
                Require(vm.SpecialtyNameError.Contains("Já existe"), "Renomear para o nome de outra especialidade deveria dar 409 no campo.");
                vm.SpecialtyDraftName = "Cardiologia Clínica"; Pump(vm.SaveSpecialtyAsync());
                Require(vm.SpecialtyCatalog.Any(item => item.Name == "Cardiologia Clínica") && fixture.Count("PUT", "/specialties/" + fixture.Specialties.First().Id) == 2, "O novo nome não foi gravado.");
                vm.SelectedSpecialty = vm.SpecialtyCatalog.Single(item => item.Name == "Pediatria"); content = Layout(window, 1280, 2400);
                Click(window, content, "Desativar especialidade", 1280, 2400);
                Require(asked.Contains("Desativar especialidade") && vm.SpecialtyCatalog.Single(item => item.Name == "Pediatria") is { IsActive: false, StatusDisplay: "Desativada" } && vm.SpecialtyActiveLabel == "Reativar especialidade", "Desativar deveria pedir confirmação e mostrar Desativada.");
                content = Layout(window, 1280, 2400); var confirmations = asked.Count; Click(window, content, "Reativar especialidade", 1280, 2400);
                Require(vm.SpecialtyCatalog.Single(item => item.Name == "Pediatria").IsActive && asked.Count == confirmations, "Reativar não deveria pedir confirmação e deveria reativar.");
                var reads = fixture.Count("GET", "/specialties"); for (var cycle = 0; cycle < 2; cycle++) Pump(vm.RefreshAutomaticallyAsync());
                Require(fixture.Count("GET", "/specialties") == reads, "O ciclo automático não deveria reler a lista de especialidades.");
                vm.RefreshCommand.Execute(null); Settle(vm); Require(fixture.Count("GET", "/specialties") == reads + 1, "Atualizar dados deveria reler a lista.");
                SaveScrolled(window, Path.Combine(previewRoot, "municipal-especialidades-light.png"), 1280, 900);
                window.ViewModel.SetAppearance(Appearance.Dark); Layout(window, 1280, 2400); SaveScrolled(window, Path.Combine(previewRoot, "municipal-especialidades-dark.png"), 1280, 900);
            }
            finally { Confirmation.Handler = null; CloseWindow(window); }
        });
        Check("Secretaria Metas: define, altera e remove metas da rede e por UBS; a situação (atingida, perto, fora, sem dado) vem dos indicadores", () =>
        {
            using var fixture = new ApiFixture();
            var alfa = fixture.AddUnit("UBS Alfa", "Centro"); var beta = fixture.AddUnit("UBS Beta", "Sul");
            var asked = new List<string>(); Confirmation.Handler = (_, title, _) => { asked.Add(title); return true; };
            var window = OpenMunicipal(fixture, storageRoot, "sec-goals", Appearance.Light);
            try
            {
                var vm = LoginObs(window);
                var content = Show(window, "reports", 1280, 3000);
                Require(fixture.Count("GET", "/goals") == 1 && vm.GoalsState.StartsWith("Nenhuma meta definida ainda", StringComparison.Ordinal) && vm.GoalEditors.Select(editor => editor.Title).SequenceEqual(["Comparecimento", "Taxa de faltas", "Espera média da fila"]), "A seção deveria ler as metas e mostrar as três métricas.");
                GoalEditor Editor(string metric) => vm.GoalEditors.Single(item => item.Metric == metric);
                string Attempt(string metric, string input, string unit = "h") { var editor = Editor(metric); editor.Input = input; editor.WaitUnit = unit; Pump(vm.SaveGoalAsync(metric)); return editor.Error; }
                Require(Attempt(GoalMetric.Score, "abc").Contains("número") && Attempt(GoalMetric.Score, "150").Contains("0 a 100") && Attempt(GoalMetric.MissedRate, "-1").Contains("0 a 100") && Attempt(GoalMetric.AverageWait, "0").Contains("maior que zero"), "Valores inválidos deveriam ser barrados no campo.");
                Require(fixture.Goals.Count == 0 && fixture.Count("PUT", "/goals/attendance_score") == 0, "Algum valor inválido chegou ao servidor.");
                Attempt(GoalMetric.Score, "82"); Attempt(GoalMetric.MissedRate, "10"); Attempt(GoalMetric.AverageWait, "2", "d");
                Require(fixture.Goals.Single(goal => goal.Metric == GoalMetric.Score) is { UnitId: null, Target: 0.82m } && fixture.Goals.Single(goal => goal.Metric == GoalMetric.MissedRate).Target == 0.10m && fixture.Goals.Single(goal => goal.Metric == GoalMetric.AverageWait).Target == 2880m, "As metas da rede deveriam ir como fração (0 a 1) e minutos: " + string.Join(", ", fixture.Goals.Select(goal => goal.Metric + "=" + goal.Target)));
                Settle(vm);
                Require(Editor(GoalMetric.Score).Current == "Meta atual: mín. 82,0%" && Editor(GoalMetric.Score).HasCurrent && Editor(GoalMetric.AverageWait).Current == "Meta atual: máx. 2 d" && Editor(GoalMetric.AverageWait).WaitUnit == "d" && Editor(GoalMetric.AverageWait).Input == "2", "Os campos deveriam mostrar a meta gravada.");
                // Situação por UBS, calculada pelos indicadores: Alfa 83,3% (atingida), Beta 81,6% (perto), faltas 16,7% (fora), espera dentro da meta.
                var rows = vm.GoalUnitRows.ToDictionary(row => row.Name);
                Require(rows.Keys.SequenceEqual(["Toda a rede (no filtro)", "UBS Alfa", "UBS Beta"]), "A tabela deveria ter a rede e cada UBS: " + string.Join(", ", rows.Keys));
                Require(rows["UBS Alfa"].Score is { Status: "met", StatusDisplay: "Atingida" } && rows["UBS Alfa"].Score.Detail == "83,3% · meta mín. 82,0%" && rows["UBS Beta"].Score is { Status: "near", StatusDisplay: "Perto da meta" } && rows["UBS Beta"].Score.Detail == "81,6% · meta mín. 82,0%", "Comparecimento: atingida em Alfa e perto em Beta: " + rows["UBS Alfa"].Score.Detail + " / " + rows["UBS Beta"].Score.Detail);
                Require(rows["UBS Alfa"].Missed is { Status: "off", StatusDisplay: "Fora da meta" } && rows["UBS Alfa"].Missed.Detail == "16,7% · meta máx. 10,0%" && rows["UBS Alfa"].Wait.Status == "met" && rows["Toda a rede (no filtro)"].Score.Status == "met", "Faltas fora da meta e espera dentro dela.");
                content = Layout(window, 1280, 3000);
                var shown = FullTexts(content);
                Require(new[] { "Atingida", "Perto da meta", "Fora da meta" }.All(shown.Contains), "O estado das metas deveria vir escrito na tabela.");
                // Sobrescrita por UBS.
                vm.GoalScope = alfa.Id;
                Require(Editor(GoalMetric.AverageWait).Input == "" && Editor(GoalMetric.AverageWait).Current == "Sem meta definida aqui." && Editor(GoalMetric.AverageWait).Inherited.StartsWith("Esta UBS herda a meta da rede", StringComparison.Ordinal) && !Editor(GoalMetric.AverageWait).HasCurrent && vm.GoalScopeLabel.Contains("UBS Alfa"), "Escolher a UBS deveria mostrar o que ela herda da rede.");
                Attempt(GoalMetric.AverageWait, "1", "h"); Settle(vm);
                Require(fixture.Goals.Any(goal => goal.Metric == GoalMetric.AverageWait && goal.UnitId == alfa.Id && goal.Target == 60m) && vm.GoalUnitRows.Single(row => row.Name == "UBS Alfa").Wait is { Status: "off" } && vm.GoalUnitRows.Single(row => row.Name == "UBS Beta").Wait.Status == "met", "A meta própria da UBS deveria valer só nela.");
                content = Layout(window, 1280, 3000);
                SaveScrolled(window, Path.Combine(previewRoot, "municipal-metas-light.png"), 1280, 900);
                var remove = Descendants<Button>(content).Single(button => AutomationProperties.GetName(button) == "Remover meta de Espera média da fila"); Require(remove.IsEnabled, "Remover deveria estar habilitado com meta gravada.");
                InvokeButton(remove); Settle(vm);
                Require(asked.Contains("Remover meta") && !fixture.Goals.Any(goal => goal.UnitId == alfa.Id) && vm.GoalUnitRows.Single(row => row.Name == "UBS Alfa").Wait.Status == "met" && fixture.Count("DELETE", "/goals/average_wait_minutes") == 1, "Remover a sobrescrita deveria pedir confirmação e voltar à meta da rede.");
                vm.GoalScope = Guid.Empty;
                // Sem dado: sem consulta encerrada.
                fixture.NoClosedAppointments = true; vm.MetricsPeriodDays = 7; Settle(vm);
                Require(vm.GoalUnitRows.Single(row => row.Name == "UBS Alfa").Score is { Status: "no-data", StatusDisplay: "Sem dado" } && vm.GoalUnitRows.Single(row => row.Name == "UBS Alfa").Missed.Status == "no-data", "Sem consulta encerrada as metas deveriam aparecer como Sem dado.");
                content = Layout(window, 1280, 3000); Require(FullTexts(content).Contains("Sem dado"), "O estado Sem dado deveria estar escrito.");
                window.ViewModel.SetAppearance(Appearance.Dark); Layout(window, 1280, 3000); SaveScrolled(window, Path.Combine(previewRoot, "municipal-metas-dark.png"), 1280, 900);
                // Remover uma meta que outra pessoa já removeu: 404 com mensagem própria.
                fixture.Goals.Clear(); vm.GoalScope = Guid.Empty; Pump(vm.RemoveGoalAsync(GoalMetric.Score));
                Require(vm.MessageKind == MessageKind.Error && vm.Message.Contains("não existe mais"), "A meta já removida deveria ter mensagem própria: " + vm.Message);
            }
            finally { Confirmation.Handler = null; CloseWindow(window); }
        });
        Check("Secretaria Alertas: contador da rede surge no login, some ao abrir e volta só com novidade, independente dos filtros", () =>
        {
            using var fixture = new ApiFixture();
            var alfa = fixture.AddUnit("UBS Alfa", "Centro"); var beta = fixture.AddUnit("UBS Beta", "Sul");
            var initial = SampleAlerts(alfa)[0]; var next = SampleAlerts(beta)[1];
            fixture.AddAlert(alfa.Id, initial);
            var window = OpenMunicipal(fixture, storageRoot, "municipal-alert-badge", Appearance.Light);
            try
            {
                var vm = LoginObs(window); var nav = window.ViewModel.Navigation.Single(item => item.Id == "alerts");
                void Age() => typeof(WorkspaceViewModel).GetField("networkBadgeAttemptedAt", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(vm, (DateTimeOffset?)(DateTimeOffset.Now.AddMinutes(-6)));
                Require(nav.Badge == 1, "O alerta inicial deve aparecer no menu após o login.");
                window.ViewModel.Navigate("alerts"); Settle(vm);
                Require(nav.Badge == 0 && vm.NetworkAlerts.Count == 1, "Abrir a aba deve limpar o aviso, mantendo a lista.");
                vm.AlertsUnitFilter = alfa.Id; Settle(vm);
                window.ViewModel.Navigate("home"); Settle(vm);
                Age(); Pump(vm.RefreshAutomaticallyAsync());
                Require(nav.Badge == 0, "O mesmo alerta não deve voltar como novidade.");
                fixture.AddAlert(beta.Id, next); Age(); Pump(vm.RefreshAutomaticallyAsync());
                Require(nav.Badge == 1 && fixture.NetworkAlertQueries[^1]["unitId"] is null && fixture.NetworkAlertQueries[^1]["region"] is null, "O aviso deve incluir uma UBS fora do filtro da tabela.");
                fixture.FailAlerts = true; Age(); Pump(vm.RefreshAutomaticallyAsync());
                Require(nav.Badge == 1, "Falha na avaliação não deve apagar o aviso anterior.");
                fixture.FailAlerts = false; window.ViewModel.Navigate("alerts"); Settle(vm);
                Require(nav.Badge == 0, "A visita à aba deve dispensar o aviso da rede, mesmo com filtro de UBS.");
                window.ViewModel.Navigate("home"); Settle(vm);
                fixture.Reset(beta.Id, alerts: true); Age(); Pump(vm.RefreshAutomaticallyAsync());
                fixture.AddAlert(beta.Id, next); Age(); Pump(vm.RefreshAutomaticallyAsync());
                Require(nav.Badge == 1, "Um alerta que terminou e voltou deve ser uma novidade.");
                vm.LogoutCommand.Execute(null); Settle(vm);
                Require(nav.Badge == 0 && !vm.IsAuthenticated, "Ao sair o aviso deve ser limpo.");
            }
            finally { CloseWindow(window); }
        });
        Check("Secretaria Alertas: tabela real da rede com UBS, tipo, especialidade, valor, limite e desde; uma requisição por filtro; vazio e falha honestos", () =>
        {
            using var fixture = new ApiFixture();
            var alfa = fixture.AddUnit("UBS Alfa", "Centro"); var beta = fixture.AddUnit("UBS Beta", "Sul");
            var items = SampleAlerts(alfa); fixture.AddAlert(alfa.Id, items[0]); fixture.AddAlert(alfa.Id, items[1]); fixture.AddAlert(beta.Id, SampleAlerts(beta)[3]);
            var window = OpenMunicipal(fixture, storageRoot, "sec-alerts-real", Appearance.Light);
            try
            {
                var vm = LoginObs(window);
                var content = Show(window, "alerts", 1280, 1700);
                Require(fixture.NetworkAlertQueries.Count == 2 && vm.NetworkAlerts.Count == 3 && fixture.NetworkAlertQueries[^1]["unitId"] is null && fixture.NetworkAlertQueries[^1]["region"] is null, "O login deve avaliar o aviso e abrir Alertas deve ler a lista da rede.");
                var grid = GridNamed(content, "Alertas da rede");
                Require(grid.Columns.Select(column => column.Header?.ToString()).SequenceEqual(["UBS", "Tipo", "Especialidade", "Valor", "Limite", "Desde"]), "Colunas da tabela de alertas divergem do esperado.");
                var cells = VisibleTexts(grid);
                Require(new[] { "UBS Alfa", "UBS Beta", "Fila alta", "Espera longa", "Ocupação da grade acima do limite", "Cardiologia", "23 pessoas", "20 pessoas", "45 dias", "97,0%", "95,0%" }.All(cells.Contains), "A tabela não mostra tipo, valor e limite de cada alerta: " + string.Join(" | ", cells));
                Require(!FullTexts(content).Any(text => text.Contains("Nenhum alerta é avaliado ainda", StringComparison.Ordinal)), "Sobrou o texto antigo de critérios.");
                SaveScrolled(window, Path.Combine(previewRoot, "municipal-alertas-light.png"), 1280, 900);
                var reads = fixture.NetworkAlertQueries.Count;
                vm.AlertsUnitFilter = alfa.Id; Settle(vm);
                Require(fixture.NetworkAlertQueries.Count == reads + 1 && fixture.NetworkAlertQueries[^1]["unitId"] == alfa.Id.ToString() && vm.NetworkAlerts.Count == 2 && vm.NetworkAlerts.All(row => row.UnitName == "UBS Alfa"), "O filtro de UBS deveria fazer uma requisição e restringir a tabela.");
                vm.AlertsUnitFilter = Guid.Empty; vm.AlertsRegion = "Sul"; Settle(vm);
                Require(fixture.NetworkAlertQueries[^1]["region"] == "Sul" && vm.NetworkAlerts.Single().UnitName == "UBS Beta", "O filtro de região deveria restringir a tabela.");
                // A mais nova vence: duas mudanças seguidas deixam só o resultado da última.
                vm.AlertsRegion = "Centro"; vm.AlertsRegion = "Sul"; Settle(vm);
                Require(vm.NetworkAlerts.Single().UnitName == "UBS Beta" && vm.AlertsRegion == "Sul", "Só a última escolha deveria alterar a tela.");
                vm.AlertsRegion = "Norte"; Settle(vm);
                Require(vm.NetworkAlerts.Count == 0 && vm.NetworkAlertsState.StartsWith("Nenhum alerta neste filtro", StringComparison.Ordinal), "Sem alertas o estado vazio deveria dizer que nada passou do limite: " + vm.NetworkAlertsState);
                SavePreview(Layout(window, 1280, 1700), Path.Combine(previewRoot, "municipal-alertas-vazio-light.png"));
                vm.AlertsRegion = ""; Settle(vm);
                // Combos com nomes próprios (a seção Alertas não tem os filtros dos indicadores).
                content = Layout(window, 1280, 1700);
                Require(Combo(content, "Filtrar alertas por UBS").Items.Count == 3 && Combo(content, "Filtrar alertas por região").Items.Count == 3, "Os filtros de alertas deveriam listar as UBS e as regiões.");
                var afterFilters = fixture.NetworkAlertQueries.Count; for (var cycle = 0; cycle < 3; cycle++) Pump(vm.RefreshAutomaticallyAsync());
                Require(fixture.NetworkAlertQueries.Count == afterFilters, "O ciclo de 20 s não deveria reavaliar os alertas antes de 5 minutos.");
                vm.RefreshCommand.Execute(null); Settle(vm); Require(fixture.NetworkAlertQueries.Count == afterFilters + 1, "Atualizar dados deveria reavaliar os alertas uma vez.");
                fixture.FailAlerts = true; vm.RefreshCommand.Execute(null); Settle(vm);
                Require(vm.MessageKind == MessageKind.Error && vm.NetworkAlerts.Count == 3, "A falha deveria avisar e manter a última avaliação.");
            }
            finally { CloseWindow(window); }
            using var down = new ApiFixture(); down.AddUnit("UBS Falha", "Centro"); down.FailAlerts = true;
            var shell = OpenMunicipal(down, storageRoot, "sec-alerts-down", Appearance.Dark);
            try
            {
                var vm = LoginObs(shell); var content = Show(shell, "alerts", 1280, 1400);
                Require(vm.NetworkAlertsState.StartsWith("Não foi possível avaliar os alertas da rede agora", StringComparison.Ordinal) && !vm.NetworkAlertsState.Contains("Nenhum alerta") && FullTexts(content).Contains(vm.NetworkAlertsState), "Sem avaliação o estado deveria ser de falha, nunca 'Nenhum alerta': " + vm.NetworkAlertsState);
                SavePreview(content, Path.Combine(previewRoot, "municipal-alertas-falha-dark.png"));
            }
            finally { CloseWindow(shell); }
        });
        Check("Secretaria Capacidade: ofertados, ocupados e ocupação por UBS e região com os filtros dos indicadores; profissionais, consultórios e grade de uma UBS em leitura", () =>
        {
            using var fixture = new ApiFixture();
            var alfa = fixture.AddUnit("UBS Alfa", "Centro"); var beta = fixture.AddUnit("UBS Beta", "Sul"); var pending = fixture.AddUnit("UBS Sem Base", "Norte", databaseState: "pending");
            fixture.SetCapacity(alfa.Id, new CapacityMetrics(40, 30, 0.75)); fixture.SetCapacity(beta.Id, new CapacityMetrics(20, 5, 0.25));
            var cardio = fixture.AddSpecialty("Cardiologia"); var professional = fixture.AddProfessional(alfa.Id, "Dra. Ana Lima", [cardio.Id], acronym: "CRM", number: "123", state: "SP"); var room = fixture.AddRoom(alfa.Id, "Consultório 1");
            fixture.AddScheduleRow(alfa.Id, professional, room, cardio.Id, weekday: 2, start: "13:00", end: "17:00", minutes: 20, validFrom: "2026-11-01");
            var window = OpenMunicipal(fixture, storageRoot, "sec-capacity", Appearance.Light);
            try
            {
                var vm = LoginObs(window);
                var content = Show(window, "capacity", 1280, 3000);
                Require(fixture.MetricsQueries.Count >= 1 && vm.NetworkCapacityCards.ToDictionary(card => card.Title, card => card.Value) is { } cards && cards["Horários ofertados"] == "60" && cards["Horários ocupados"] == "35" && cards["Ocupação da grade"] == "58,3%", "Os totais de capacidade da rede estão incorretos.");
                var unitRows = vm.CapacityByUnit.ToDictionary(row => row.Name);
                Require(unitRows["UBS Alfa"] is { OfferedDisplay: "40", BookedDisplay: "30", OccupancyDisplay: "75,0%" } && unitRows["UBS Beta"] is { OfferedDisplay: "20", BookedDisplay: "5", OccupancyDisplay: "25,0%" } && !unitRows.ContainsKey("UBS Sem Base"), "Capacidade por UBS incorreta.");
                var regionRows = vm.CapacityByRegion.ToDictionary(row => row.Name);
                Require(regionRows["Centro"] is { OfferedDisplay: "40", OccupancyDisplay: "75,0%" } && regionRows["Sul"] is { BookedDisplay: "5", OccupancyDisplay: "25,0%" }, "Capacidade por região incorreta.");
                var unitCells = VisibleTexts(GridNamed(content, "Capacidade por UBS"));
                Require(new[] { "UBS Alfa", "UBS Beta", "40", "30", "75,0%", "20", "5", "25,0%" }.All(unitCells.Contains) && Combo(content, "Filtrar por UBS") is not null && Combo(content, "Filtrar por região") is not null, "A tabela e os filtros de capacidade não aparecem.");
                var reads = fixture.MetricsQueries.Count; vm.MetricsRegion = "Sul"; Settle(vm);
                Require(fixture.MetricsQueries.Count == reads + 1 && vm.NetworkCapacityCards.Single(card => card.Title == "Horários ofertados").Value == "20" && vm.CapacityByUnit.Single().Name == "UBS Beta", "O filtro de região deveria fazer uma requisição e restringir a capacidade.");
                vm.MetricsRegion = ""; Settle(vm);
                // Cadastro de uma UBS em leitura.
                Require(vm.CapacityCatalogState.StartsWith("Escolha uma UBS", StringComparison.Ordinal) && fixture.Count("GET", "/professionals") == 0, "Nada do cadastro deveria ser lido antes de escolher a UBS.");
                vm.CapacityUnit = vm.Units.Single(unit => unit.Name == "UBS Alfa"); Settle(vm);
                Require(fixture.Count("GET", "/professionals") == 1 && fixture.Count("GET", "/rooms") == 1 && fixture.Count("GET", "/schedule") == 1, "Escolher a UBS deveria ler profissionais, consultórios e grade uma vez.");
                Require(vm.CapacityProfessionals.Single() is { FullName: "Dra. Ana Lima", SpecialtiesDisplay: "Cardiologia", CouncilDisplay: "CRM 123/SP" } && vm.CapacityRooms.Single().Name == "Consultório 1" && vm.CapacitySchedule.Single() is { WeekdayDisplay: "Terça-feira", RangeDisplay: "13:00–17:00", SlotDisplay: "20 min", ProfessionalName: "Dra. Ana Lima", RoomName: "Consultório 1", SpecialtyName: "Cardiologia", ValidityDisplay: "A partir de 01/11/2026" }, "O cadastro da UBS em leitura está incorreto.");
                content = Layout(window, 1280, 3000);
                var catalogTexts = FullTexts(content);
                Require(new[] { "Dra. Ana Lima", "Consultório 1", "Terça-feira", "A partir de 01/11/2026" }.All(text => catalogTexts.Contains(text) || VisibleTexts(content).Contains(text) || Descendants<DataGrid>(content).Any(grid => VisibleTexts(grid).Contains(text))), "O cadastro da UBS não aparece na tela.");
                Require(!Descendants<Button>(content).Any(button => button.IsVisible && button.Content?.ToString() is "Salvar profissional" or "Novo horário" or "Criar bloqueio"), "A leitura da Secretaria não deveria ter botões de edição.");
                SaveScrolled(window, Path.Combine(previewRoot, "municipal-capacidade-light.png"), 1280, 900);
                var catalogReads = fixture.Count("GET", "/professionals"); for (var cycle = 0; cycle < 2; cycle++) Pump(vm.RefreshAutomaticallyAsync());
                Require(fixture.Count("GET", "/professionals") == catalogReads, "O ciclo automático não deveria reler o cadastro da UBS.");
                vm.CapacityUnit = vm.Units.Single(unit => unit.Name == "UBS Beta"); Settle(vm);
                Require(fixture.Count("GET", "/professionals") == catalogReads + 1 && vm.CapacityProfessionals.Count == 0 && vm.CapacityCatalogState.StartsWith("Esta UBS ainda não cadastrou", StringComparison.Ordinal), "Outra UBS sem cadastro deveria dizer isso.");
                var beforePending = fixture.Count("GET", "/professionals"); vm.CapacityUnit = vm.Units.Single(unit => unit.Name == "UBS Sem Base"); Settle(vm);
                Require(fixture.Count("GET", "/professionals") == beforePending && vm.CapacityCatalogState.Contains("base desta UBS ainda não foi criada"), "UBS sem base não deveria ser lida e deveria explicar.");
                window.ViewModel.SetAppearance(Appearance.Dark); vm.CapacityUnit = vm.Units.Single(unit => unit.Name == "UBS Alfa"); Settle(vm); Layout(window, 1280, 3000);
                SaveScrolled(window, Path.Combine(previewRoot, "municipal-capacidade-dark.png"), 1280, 900);
            }
            finally { CloseWindow(window); }
        });
        Check("Secretaria Histórico: 'Carregar mais' usa o cursor da API e não perde nem repete eventos empatados no mesmo instante", () =>
        {
            using var fixture = new ApiFixture(); var unit = fixture.AddUnit("UBS Cursor", "Centro");
            var tied = DateTimeOffset.UtcNow.AddHours(-2);
            for (var index = 0; index < 60; index++) fixture.History.Add(new(tied, "registry-read", "Gestor Fixture", unit.Id, unit.Name, "Empatado " + index.ToString("D3", CultureInfo.InvariantCulture)));
            for (var index = 0; index < 70; index++) fixture.History.Add(new(tied.AddSeconds(index + 1), "staff-granted", "Gestor Fixture", unit.Id, unit.Name, "Recente " + index.ToString("D3", CultureInfo.InvariantCulture)));
            var window = OpenMunicipal(fixture, storageRoot, "sec-history-cursor", Appearance.Light);
            try
            {
                var vm = LoginObs(window); Show(window, "users", 1280, 2200);
                Require(vm.HistoryEvents.Count == 100 && vm.CanLoadMoreHistory && vm.HistoryEvents.All(item => item.Cursor is { } cursor && cursor.StartsWith("c1_", StringComparison.Ordinal)), "A primeira página deveria trazer 100 ações, cada uma com seu cursor.");
                Pump(vm.LoadMoreHistoryAsync());
                var sent = fixture.HistoryQueries[^1]["before"];
                Require(sent == vm.HistoryEvents[99].Cursor && sent!.StartsWith("c1_", StringComparison.Ordinal), "A página seguinte deveria enviar o cursor do último item, não o instante: " + sent);
                Require(vm.HistoryEvents.Count == 130 && vm.HistoryEvents.Select(item => item.AccountName).Distinct().Count() == 130 && !vm.CanLoadMoreHistory, $"Eventos empatados no mesmo instante foram perdidos ou repetidos: {vm.HistoryEvents.Count} itens, {vm.HistoryEvents.Select(item => item.AccountName).Distinct().Count()} distintos.");
                Require(vm.HistoryEvents.Count(item => item.AccountName!.StartsWith("Empatado", StringComparison.Ordinal)) == 60, "Os 60 eventos empatados deveriam aparecer, com o instante antigo como cursor eles se perderiam.");
            }
            finally { CloseWindow(window); }
        });
    }
}
