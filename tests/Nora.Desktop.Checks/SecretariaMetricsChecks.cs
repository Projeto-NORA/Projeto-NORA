using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Nora.Desktop.Models;
using Nora.Desktop.Services;
using Nora.Desktop.ViewModels;
using Nora.Desktop.Views;

namespace Nora.Desktop.Checks;

/// <summary>Indicadores da rede (filtros, gráfico, tabelas), histórico, contas OBS, trocas de senha e limpeza no logout.</summary>
internal static partial class Program
{
    private static MetricCard Card(WorkspaceViewModel vm, string title) => vm.OverviewCards.Single(card => card.Title == title);

    private static void RunSecretariaMetricsChecks(Application application, string storageRoot, string[] args)
    {
        var previewRoot = PreviewRoot(args);
        Check("Secretaria: cada filtro dos indicadores (período, UBS, região, especialidade, personalizado) faz uma única requisição", () =>
        {
            using var fixture = new ApiFixture();
            var alfa = fixture.AddUnit("UBS Alfa", "Centro"); var beta = fixture.AddUnit("UBS Beta", "Sul"); fixture.AddUnit("UBS Gama", "Sul");
            var window = OpenMunicipal(fixture, storageRoot, "sec-filters", Appearance.Light);
            try
            {
                var vm = LoginObs(window);
                var content = Show(window, "indicators");
                Require(fixture.MetricsQueries.Count == 1, "O login deveria fazer uma única leitura de indicadores: " + fixture.MetricsQueries.Count);
                var count = 1;
                void ExpectOneMore(string what) { count++; Require(fixture.MetricsQueries.Count == count, $"{what}: esperada 1 requisição a mais, houve {fixture.MetricsQueries.Count - count + 1}."); }
                static double Days(System.Collections.Specialized.NameValueCollection query) => (DateTimeOffset.Parse(query["to"]!, CultureInfo.InvariantCulture) - DateTimeOffset.Parse(query["from"]!, CultureInfo.InvariantCulture)).TotalDays;
                Combo(content, "Período dos indicadores").SelectedValue = 7; Settle(vm); ExpectOneMore("Período 7 dias");
                Require(Days(fixture.MetricsQueries[^1]) == 7, "O período de 7 dias não foi enviado.");
                Combo(content, "Período dos indicadores").SelectedValue = 90; Settle(vm); ExpectOneMore("Período 90 dias");
                Require(Days(fixture.MetricsQueries[^1]) == 90, "O período de 90 dias não foi enviado.");
                Combo(content, "Filtrar por região").SelectedValue = "Sul"; Settle(vm); ExpectOneMore("Região");
                Require(fixture.MetricsQueries[^1]["region"] == "Sul" && vm.UnitPerformance.Select(row => row.Name).SequenceEqual(["UBS Beta", "UBS Gama"]) && vm.RegionPerformance.Single().Name == "Sul", "A região não restringiu os números.");
                Require(Combo(content, "Filtrar por região").Items.Cast<FilterOption>().Select(option => option.Label).SequenceEqual(["Todas as regiões", "Centro", "Sul"]), "As regiões do seletor deveriam vir das UBS cadastradas.");
                Combo(content, "Filtrar por região").SelectedValue = ""; Settle(vm); ExpectOneMore("Região (todas)");
                var specialties = Combo(content, "Filtrar por especialidade").Items.Cast<FilterOption>().Select(option => option.Label).ToArray();
                Require(specialties.SequenceEqual(["Todas as especialidades", "Clínica geral", "Pediatria"]), "As especialidades do seletor deveriam vir do retorno: " + string.Join(", ", specialties));
                Combo(content, "Filtrar por especialidade").SelectedValue = "Pediatria"; Settle(vm); ExpectOneMore("Especialidade");
                Require(fixture.MetricsQueries[^1]["specialty"] == "Pediatria" && vm.SpecialtyPerformance.Single().Name == "Pediatria", "A especialidade não restringiu os números.");
                vm.MetricsSpecialty = ""; Settle(vm); ExpectOneMore("Especialidade (todas)");
                var summaries = fixture.SummaryQueries.Count;
                Combo(content, "Filtrar por UBS").SelectedValue = beta.Id; Settle(vm); ExpectOneMore("UBS");
                Require(fixture.MetricsQueries[^1]["unitId"] == beta.Id.ToString() && fixture.SummaryQueries.Count == summaries + 1, "A UBS deveria fazer uma leitura de indicadores e uma do resumo.");
                vm.SummaryUnitFilter = Guid.Empty; Settle(vm); ExpectOneMore("UBS (toda a rede)");
                // Personalizado: escolher não consulta; aplicar valida e consulta uma vez.
                Combo(content, "Período dos indicadores").SelectedValue = 0; Settle(vm);
                Require(fixture.MetricsQueries.Count == count && vm.IsCustomPeriod, "Escolher 'Personalizado' não deveria consultar.");
                content = Layout(window, 1280, 900);
                (string From, string To, string Fragment)[] invalid = [("31/10/2026", "01/10/2026", "inicial não pode ser posterior"), ("01/01/2026", "03/01/2027", "no máximo 366 dias"), ("abc", "01/10/2026", "formato dd/mm/aaaa")];
                foreach (var (from, to, fragment) in invalid)
                {
                    Input(content, "Data inicial do período").Text = from; Input(content, "Data final do período").Text = to;
                    Click(window, content, "Aplicar período");
                    Require(fixture.MetricsQueries.Count == count && vm.MetricsRangeError.Contains(fragment) && HasText(Layout(window, 1280, 900), vm.MetricsRangeError), $"Período inválido {from}–{to} não foi barrado com a mensagem: {vm.MetricsRangeError}");
                }
                Input(content, "Data inicial do período").Text = "01/10/2026"; Input(content, "Data final do período").Text = "31/10/2026";
                Click(window, content, "Aplicar período"); ExpectOneMore("Período personalizado");
                Require(vm.MetricsRangeError == "" && DateTimeOffset.Parse(fixture.MetricsQueries[^1]["from"]!, CultureInfo.InvariantCulture) == new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeZoneInfo.Local.GetUtcOffset(new DateTime(2026, 10, 1))) && Days(fixture.MetricsQueries[^1]) == 31, "O período personalizado não enviou o intervalo informado (fim inclusivo).");
                Require(vm.MetricsScopeLabel.StartsWith("Números de 01/10/2026 a 31/10/2026", StringComparison.Ordinal), "O escopo exibido não reflete o período aplicado: " + vm.MetricsScopeLabel);
                _ = alfa;
            }
            finally { CloseWindow(window); }
        });
        Check("Secretaria: resposta atrasada de indicadores é descartada e só a consulta mais nova altera a tela", () =>
        {
            using var fixture = new ApiFixture();
            fixture.AddUnit("UBS Alfa", "Centro");
            var window = OpenMunicipal(fixture, storageRoot, "sec-stale", Appearance.Light);
            try
            {
                var vm = LoginObs(window);
                var baseline = fixture.MetricsQueries.Count; var initialScope = vm.MetricsScopeLabel;
                fixture.HoldMetrics = true;
                vm.MetricsPeriodDays = 7; PumpUntil(() => fixture.HeldMetrics.Count == 1);
                vm.MetricsPeriodDays = 90;
                fixture.HeldMetrics[0].SetResult(true);
                PumpUntil(() => fixture.HeldMetrics.Count == 2);
                Require(vm.MetricsScopeLabel == initialScope, "A resposta atrasada (7 dias) foi aplicada depois de o filtro mudar.");
                fixture.HeldMetrics[1].SetResult(true); Settle(vm);
                var expectedFirst = DateOnly.FromDateTime(DateTime.Today.AddDays(-89)).ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
                Require(fixture.MetricsQueries.Count == baseline + 2 && vm.MetricsScopeLabel.StartsWith("Números de " + expectedFirst, StringComparison.Ordinal), "Só a consulta de 90 dias deveria ficar na tela: " + vm.MetricsScopeLabel);
                Require(vm.MetricsPeriodDays == 90 && !vm.MetricsLoading, "O estado de carregamento ficou preso.");
            }
            finally { foreach (var held in fixture.HeldMetrics) held.TrySetResult(true); fixture.HoldMetrics = false; CloseWindow(window); }
        });
        Check("Secretaria: Visão geral mostra os indicadores do período, o score com explicação, o gráfico e a tabela equivalente com os mesmos valores", () =>
        {
            using var fixture = new ApiFixture();
            fixture.AddUnit("UBS Alfa", "Centro"); fixture.AddUnit("UBS Beta", "Sul");
            var window = OpenMunicipal(fixture, storageRoot, "sec-chart", Appearance.Light);
            try
            {
                var vm = LoginObs(window);
                var content = Show(window, "home", 1280, 1900);
                Require(Card(vm, "Consultas concluídas").Value == "30" && Card(vm, "Faltas").Value == "6" && Card(vm, "Cancelamentos").Value == "3" && Card(vm, "Cancelamentos").Hint == "1 feito pelo paciente", "Cartões de consultas incorretos.");
                Require(Card(vm, "Comparecimento").Value == "82,2%", "Score deveria ter uma casa decimal em pt-BR: " + Card(vm, "Comparecimento").Value);
                Require(Card(vm, "Espera média da fila").Value == "2 h 15 min" && Card(vm, "Maior espera atual").Value == "1 d 1 h", "Esperas não ficaram legíveis: " + Card(vm, "Espera média da fila").Value + " / " + Card(vm, "Maior espera atual").Value);
                foreach (var card in vm.OverviewCards) Require(HasText(content, card.Title) && HasText(content, card.Value), "Cartão não renderizado: " + card.Title);
                Require(VisibleTexts(content).Any(text => text.StartsWith("Comparecimento = concluídas ÷", StringComparison.Ordinal)) && Descendants<Expander>(content).Any(expander => expander.Header?.ToString() == "Saiba mais sobre o comparecimento"), "A explicação do score ou o 'saiba mais' não aparece.");
                // Gráfico e tabela: mesmos números.
                var days = vm.ChartDays; Require(days.Count == 30, "A série deveria cobrir os 30 dias do período: " + days.Count);
                var chart = Descendants<EvolutionChartView>(content).First();
                var rectangles = Descendants<System.Windows.Shapes.Rectangle>(chart).ToArray();
                var tips = rectangles.Where(rectangle => rectangle.ToolTip is string).Select(rectangle => (string)rectangle.ToolTip).ToArray();
                Require(tips.SequenceEqual(days.Select(day => day.Description)), "As dicas do gráfico não trazem o número exato de cada dia.");
                var expectedBars = days.Sum(day => (day.Completed > 0 ? 1 : 0) + (day.Missed > 0 ? 1 : 0) + (day.Cancelled > 0 ? 1 : 0));
                var bars = rectangles.Count(rectangle => rectangle.ToolTip is null && rectangle.Fill is SolidColorBrush) - 3;
                Require(bars == expectedBars && expectedBars > 0, $"O gráfico desenhou {bars} barras; esperadas {expectedBars}.");
                var expander = Descendants<Expander>(chart).Single(item => AutomationProperties.GetName(item) == "Tabela dos dados do gráfico"); expander.IsExpanded = true; content = Layout(window, 1280, 1900);
                var table = GridNamed(content, "Consultas por dia");
                var rows = table.Items.Cast<DailyPoint>().ToArray();
                Require(rows.Length == 5 && rows.Sum(row => row.Completed) == days.Sum(day => day.Completed) && rows.Sum(row => row.Missed) == days.Sum(day => day.Missed) && rows.Sum(row => row.Cancelled) == days.Sum(day => day.Cancelled) && rows.All(row => days.Contains(row)), "A tabela não tem os mesmos valores do gráfico.");
                Require(VisibleTexts(table).Contains(rows[0].DateDisplay) && VisibleTexts(table).Contains(rows[0].Completed.ToString()), "A tabela não mostra os valores do primeiro dia.");
                Require(Descendants<TextBlock>(chart).Any(text => text.Text == "Concluídas") && Descendants<TextBlock>(chart).Any(text => text.Text == "Faltas") && Descendants<TextBlock>(chart).Any(text => text.Text == "Canceladas"), "A legenda do gráfico está incompleta.");
                SaveScrolled(window, Path.Combine(previewRoot, "municipal-overview-light.png"));
                // Período longo: o mesmo gráfico vira linhas, com a mesma tabela.
                vm.MetricsPeriodDays = 90; Settle(vm); content = Layout(window, 1280, 1900);
                Require(vm.ChartDays.Count == 90 && Descendants<System.Windows.Shapes.Polyline>(Descendants<EvolutionChartView>(content).First()).Count() == 3, "Acima de 31 dias o gráfico deveria usar três linhas.");
                // Score nulo.
                fixture.NoClosedAppointments = true; vm.MetricsPeriodDays = 7; Settle(vm); content = Layout(window, 1280, 1900);
                Require(Card(vm, "Comparecimento").Value == "—" && HasText(content, "Sem consulta encerrada no período para calcular.") && vm.ChartState == "Nenhuma consulta com data marcada no período." && HasText(content, "Nenhuma consulta com data marcada no período."), "Score nulo e gráfico vazio deveriam explicar o motivo.");
                SaveScrolled(window, Path.Combine(previewRoot, "municipal-overview-empty-light.png"));
            }
            finally { CloseWindow(window); }
        });
        Check("Secretaria: tabelas por unidade, por especialidade e por região trazem os números dos indicadores e ordenam pelo valor", () =>
        {
            using var fixture = new ApiFixture();
            fixture.AddUnit("UBS Alfa", "Centro"); fixture.AddUnit("UBS Beta", "Sul");
            var window = OpenMunicipal(fixture, storageRoot, "sec-tables", Appearance.Dark);
            try
            {
                var vm = LoginObs(window);
                var content = Show(window, "units");
                var units = GridNamed(content, "Desempenho por unidade");
                var cells = VisibleTexts(units);
                Require(new[] { "UBS Alfa", "UBS Beta", "83,3%", "81,6%", "1 h 30 min", "3 h" }.All(cells.Contains), "Tabela por unidade sem os números esperados: " + string.Join(" | ", cells));
                Require(units.Columns.Single(column => column.Header?.ToString() == "Comparecimento").SortMemberPath == "ScoreSort" && units.Columns.Single(column => column.Header?.ToString() == "Espera média").SortMemberPath == "WaitSort", "Colunas de score e espera ordenariam por texto.");
                units.Items.SortDescriptions.Add(new SortDescription("ScoreSort", ListSortDirection.Descending));
                Require(units.Items.Cast<PerformanceRow>().First().Name == "UBS Alfa", "Ordenação decrescente por comparecimento falhou.");
                units.Items.SortDescriptions.Clear(); units.Items.SortDescriptions.Add(new SortDescription("ScoreSort", ListSortDirection.Ascending));
                Require(units.Items.Cast<PerformanceRow>().First().Name == "UBS Beta", "Ordenação crescente por comparecimento falhou.");
                SaveScrolled(window, Path.Combine(previewRoot, "municipal-units-performance-dark.png"));
                content = Show(window, "specialties");
                var specialties = GridNamed(content, "Indicadores por especialidade");
                Require(new[] { "Clínica geral", "Pediatria", "80,0%", "85,7%", "2 h" }.All(VisibleTexts(specialties).Contains) && VisibleTexts(specialties).Count(text => text == "—") == 1, "Tabela por especialidade sem os números esperados (a espera nula deve aparecer como —).");
                SaveScrolled(window, Path.Combine(previewRoot, "municipal-specialties-dark.png"));
                content = Show(window, "indicators", 1280, 2300);
                var regions = GridNamed(content, "Comparativo por região");
                Require(new[] { "Centro", "Sul", "83,3%", "81,6%" }.All(VisibleTexts(regions).Contains), "Comparativo por região sem os números esperados.");
                Require(Descendants<EvolutionChartView>(content).Any() && vm.OverviewCards.All(card => HasText(content, card.Title)), "Indicadores deveria trazer os cartões e a série no tempo.");
                SaveScrolled(window, Path.Combine(previewRoot, "municipal-indicators-dark.png"));
            }
            finally { CloseWindow(window); }
        });
        Check("Secretaria: histórico de ações é paginado (carregar mais), traduzido, local e filtrado por período e UBS", () =>
        {
            using var fixture = new ApiFixture();
            var unit = fixture.AddUnit("UBS Histórico", "Centro"); var other = fixture.AddUnit("UBS Outra", "Sul");
            var baseTime = DateTimeOffset.UtcNow.AddHours(-10);
            for (var index = 0; index < 150; index++)
                fixture.History.Add(new(baseTime.AddMinutes(index), index % 2 == 0 ? "staff-granted" : "registry-read", "Gestor Fixture", index < 20 ? other.Id : unit.Id, index < 20 ? "UBS Outra" : "UBS Histórico", "Equipe Fixture"));
            var window = OpenMunicipal(fixture, storageRoot, "sec-history", Appearance.Light);
            try
            {
                var vm = LoginObs(window);
                var content = Show(window, "users", 1280, 1900);
                Require(vm.HistoryEvents.Count == 100 && fixture.HistoryQueries.Count == 1 && fixture.HistoryQueries[0]["limit"] == "100" && vm.CanLoadMoreHistory, "A primeira página deveria trazer 100 ações com 'carregar mais'.");
                Require(vm.HistoryEvents[0].OccurredAt == baseTime.AddMinutes(149) && vm.HistoryEvents[0].ActionDisplay == "Consultou os cadastros da UBS", "As ações deveriam vir da mais recente para a mais antiga e em português.");
                var grid = GridNamed(content, "Histórico de ações");
                Require(VisibleTexts(grid).Contains(vm.HistoryEvents[0].OccurredAtDisplay) && vm.HistoryEvents[0].OccurredAtDisplay == baseTime.AddMinutes(149).ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss"), "Data e hora não aparecem no horário local.");
                Click(window, content, "Carregar mais", 1280, 1900);
                Require(vm.HistoryEvents.Count == 150 && !vm.CanLoadMoreHistory && fixture.HistoryQueries.Count == 2 && fixture.HistoryQueries[1]["before"] is not null, "'Carregar mais' deveria trazer as 50 restantes pelo cursor.");
                Require(vm.HistoryEvents.Select(item => item.OccurredAt).Distinct().Count() == 150 && vm.HistoryEvents.Zip(vm.HistoryEvents.Skip(1)).All(pair => pair.First.OccurredAt > pair.Second.OccurredAt), "A lista paginada ficou com repetições ou fora de ordem.");
                // Filtros: UBS e período, uma requisição ao consultar.
                vm.HistoryUnitFilter = other.Id; Settle(vm);
                Require(fixture.HistoryQueries.Count == 2, "Escolher a UBS não deveria consultar antes do botão.");
                Click(window, content, "Consultar histórico", 1280, 1900);
                Require(fixture.HistoryQueries.Count == 3 && fixture.HistoryQueries[2]["unitId"] == other.Id.ToString() && vm.HistoryEvents.Count == 20 && !vm.CanLoadMoreHistory, "O filtro de UBS não restringiu o histórico.");
                vm.HistoryUnitFilter = Guid.Empty; vm.HistoryFromText = "xx/yy"; Click(window, content, "Consultar histórico", 1280, 1900);
                Require(fixture.HistoryQueries.Count == 3 && vm.HistoryRangeError.Contains("dd/mm/aaaa"), "Data inválida deveria ser barrada sem requisição.");
                vm.HistoryFromText = baseTime.ToLocalTime().AddMinutes(100).ToString("dd/MM/yyyy", CultureInfo.InvariantCulture); vm.HistoryToText = vm.HistoryFromText;
                Click(window, content, "Consultar histórico", 1280, 1900);
                Require(fixture.HistoryQueries.Count == 4 && fixture.HistoryQueries[3]["from"] is not null && fixture.HistoryQueries[3]["to"] is not null && vm.HistoryRangeError == "", "O período não foi enviado.");
                SaveScrolled(window, Path.Combine(previewRoot, "municipal-history-light.png"));
            }
            finally { CloseWindow(window); }
        });
        Check("Secretaria: criar, desativar, reativar e redefinir senha de conta OBS pedem confirmação e nunca mostram a senha", () =>
        {
            using var fixture = new ApiFixture();
            fixture.AddUnit("UBS Contas");
            var window = OpenMunicipal(fixture, storageRoot, "sec-staff", Appearance.Light);
            var asked = new List<string>(); var answer = true;
            Confirmation.Handler = (_, title, text) => { asked.Add(title + "|" + text); return answer; };
            try
            {
                var vm = LoginObs(window);
                var content = Show(window, "users", 1280, 1900);
                void Fill(string cpf, string email, string password, string confirm)
                {
                    Input(content, "Nome da nova conta").Text = "Marina Costa"; Input(content, "CPF da nova conta").Text = cpf; Input(content, "E-mail da nova conta").Text = email;
                    Combo(content, "Perfil da nova conta").SelectedValue = "administrator";
                    Secret(content, "Senha provisória da nova conta").Password = password; Secret(content, "Confirmação da senha provisória da nova conta").Password = confirm;
                }
                Fill("529.982.247-25", "marina@example.test", "provisoria-123", "provisoria-124");
                Click(window, content, "Criar conta", 1280, 1900);
                Require(fixture.Count("POST", "/api/municipal/staff") == 0 && vm.StaffConfirmError.Contains("confirmação") && HasText(Layout(window, 1280, 1900), vm.StaffConfirmError), "Confirmação diferente deveria ser barrada junto ao campo.");
                Fill("111.111.111-11", "invalido", "curta", "curta");
                Click(window, content, "Criar conta", 1280, 1900);
                Require(fixture.Count("POST", "/api/municipal/staff") == 0 && vm.StaffCpfError.Length > 0 && vm.StaffEmailError.Length > 0 && vm.StaffPasswordError.Length > 0, "CPF, e-mail e senha inválidos deveriam ter mensagem própria.");
                foreach (var box in Descendants<PasswordBox>(content)) box.Clear();
                SaveScrolled(window, Path.Combine(previewRoot, "municipal-staff-form-errors-light.png"));
                Fill("529.982.247-25", "marina@example.test", "provisoria-123", "provisoria-123");
                Require(Descendants<PasswordBox>(content).Count(box => box.Password.Length > 0) == 2, "Pré-condição: campos de senha preenchidos.");
                foreach (var box in Descendants<PasswordBox>(content)) box.Clear();
                Secret(content, "Senha provisória da nova conta").Password = "provisoria-123"; Secret(content, "Confirmação da senha provisória da nova conta").Password = "provisoria-123";
                Click(window, content, "Criar conta", 1280, 1900);
                Require(fixture.Count("POST", "/api/municipal/staff") == 1 && fixture.LastStaffCreate is { Cpf: "52998224725", Role: "administrator", Email: "marina@example.test", Password: "provisoria-123" }, "A conta não foi criada com os dados informados (CPF só com dígitos).");
                var created = vm.Staff.Single(account => account.FullName == "Marina Costa");
                Require(created.PendingDisplay == "Pendente" && created.RoleDisplay == "Administrador" && vm.Message.Contains("obrigada a trocar a senha"), "A conta nova deveria aparecer com troca de senha pendente e a explicação.");
                Require(Descendants<PasswordBox>(content).All(box => box.Password.Length == 0) && vm.StaffName == "", "O formulário não foi limpo depois de criar.");
                Fill("111.444.777-35", "marina@example.test", "provisoria-123", "provisoria-123");
                Click(window, content, "Criar conta", 1280, 1900);
                Require(vm.MessageKind == MessageKind.Error && vm.Message.Contains("Já existe uma conta OBS"), "Duplicidade deveria virar mensagem específica: " + vm.Message);
                foreach (var box in Descendants<PasswordBox>(content)) box.Clear();
                // Desativar / reativar com confirmação e cancelamento.
                GridNamed(content, "Contas OBS").SelectedItem = created; Layout(window, 1280, 1900);
                answer = false; Click(window, content, "Desativar conta", 1280, 1900);
                Require(fixture.Count("PATCH", "/active") == 0 && created.IsActive && asked[^1].Contains("sessões") && asked[^1].Contains("concessões de UBS são preservadas"), "Cancelar a confirmação não deveria desativar, e o texto deve explicar o efeito.");
                answer = true; Click(window, content, "Desativar conta", 1280, 1900);
                Require(fixture.Count("PATCH", "/active") == 1 && vm.SelectedStaff is { IsActive: false } && vm.StaffActiveActionLabel == "Reativar conta" && vm.Staff.Single(account => account.Id == created.Id).StatusDisplay == "Desativada", "A conta não foi desativada, ou a seleção se perdeu ao recarregar a lista.");
                Click(window, content, "Reativar conta", 1280, 1900);
                Require(fixture.Count("PATCH", "/active") == 2 && vm.SelectedStaff is { IsActive: true }, "A conta não foi reativada.");
                // Redefinir senha provisória.
                Secret(content, "Nova senha provisória da conta").Password = "senha-nova-123"; Secret(content, "Confirmação da senha provisória da conta").Password = "senha-nova-999";
                Click(window, content, "Redefinir senha provisória", 1280, 1900);
                Require(fixture.Count("POST", "/password") == 0 && vm.StaffResetConfirmError.Length > 0, "Confirmação diferente deveria ser barrada antes de perguntar.");
                Secret(content, "Confirmação da senha provisória da conta").Password = "senha-nova-123"; answer = false;
                Click(window, content, "Redefinir senha provisória", 1280, 1900);
                Require(fixture.Count("POST", "/password") == 0 && asked[^1].Contains("obrigada a trocar a senha"), "Cancelar não deveria redefinir.");
                answer = true; Click(window, content, "Redefinir senha provisória", 1280, 1900);
                Require(fixture.Count("POST", "/password") == 1 && fixture.LastStaffPassword == "senha-nova-123" && vm.SelectedStaff!.MustChangePassword && Descendants<PasswordBox>(content).All(box => box.Password.Length == 0), "A senha provisória não foi redefinida ou os campos não foram limpos.");
                Require(!vm.Message.Contains("senha-nova-123") && !VisibleTexts(content).Any(text => text.Contains("senha-nova-123") || text.Contains("provisoria-123")), "Uma senha apareceu em texto na tela.");
                // Concessão de UBS também confirma.
                vm.SelectedStaffUnit = vm.Units.Single(); answer = false; Click(window, content, "Conceder vínculo", 1280, 1900);
                Require(vm.StaffUnits.Count == 0, "Cancelar a concessão não deveria conceder.");
                answer = true; Click(window, content, "Conceder vínculo", 1280, 1900);
                Require(vm.StaffUnits.Count == 1, "A concessão confirmada não foi feita.");
            }
            finally { Confirmation.Handler = null; CloseWindow(window); }
        });
    }
}
