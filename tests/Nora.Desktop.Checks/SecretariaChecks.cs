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

/// <summary>Secretaria e troca de senha: cadastro/agenda por UBS, indicadores da rede, histórico, contas OBS, base da UBS e senhas, sempre contra o transporte sintético.</summary>
internal static partial class Program
{
    private static ShellWindow OpenMunicipal(ApiFixture fixture, string storageRoot, string name, Appearance? mode = null)
    {
        var window = new ShellWindow(DesktopProfile.Municipal, Path.Combine(storageRoot, name), new NoraApiClient(fixture));
        window.ViewModel.ConfirmClose = false; window.ViewModel.Animations = false;
        if (mode is { } appearance) window.ViewModel.SetAppearance(appearance);
        PrepareWindow(window);
        return window;
    }
    private static void CloseWindow(ShellWindow window) { window.ViewModel.ConfirmClose = false; window.Close(); }
    private static FrameworkElement Show(ShellWindow window, string route, double width = 1280, double height = 900)
    {
        window.ViewModel.Navigate(route); var vm = window.ViewModel.Workspace;
        Layout(window, width, height); Settle(vm);
        return Layout(window, width, height);
    }
    private static ComboBox Combo(FrameworkElement content, string name) => PageField<ComboBox>(content, box => AutomationProperties.GetName(box) == name);
    private static DataGrid GridNamed(FrameworkElement content, string name) => Descendants<DataGrid>(content).Single(grid => AutomationProperties.GetName(grid) == name);
    private static PasswordBox Secret(FrameworkElement content, string name) => Descendants<PasswordBox>(content).Single(box => box.IsVisible && AutomationProperties.GetName(box) == name);
    private static void Click(ShellWindow window, FrameworkElement content, string label, double width = 1280, double height = 900)
    {
        InvokeButton(FindActionButton(content, label)); Settle(window.ViewModel.Workspace); Layout(window, width, height);
    }
    private static bool HasText(FrameworkElement root, string text) => VisibleTexts(root).Any(item => item == text);
    /// <summary>Salva a página inteira em partes (-01, -02…), rolando a área de conteúdo; recusa capturas com senha preenchida.</summary>
    private static void SaveScrolled(ShellWindow window, string path, double width = 1280, double height = 900)
    {
        var content = Layout(window, width, height);
        Require(Descendants<PasswordBox>(content).All(box => box.Password.Length == 0), "Captura recusada: campo de senha preenchido.");
        var scroll = (ScrollViewer)window.FindName("PageScroll");
        scroll.ScrollToTop(); content = Layout(window, width, height);
        var offsets = new List<double> { 0 }; var step = Math.Max(1, scroll.ViewportHeight - 60);
        for (var next = step; next < scroll.ScrollableHeight; next += step) offsets.Add(next);
        if (scroll.ScrollableHeight > 1) offsets.Add(scroll.ScrollableHeight);
        for (var index = 0; index < offsets.Count; index++)
        {
            scroll.ScrollToVerticalOffset(offsets[index]); content = Layout(window, width, height);
            SavePreview(content, Path.Combine(Path.GetDirectoryName(path)!, Path.GetFileNameWithoutExtension(path) + $"-{index + 1:D2}.png"));
        }
        scroll.ScrollToTop(); Layout(window, width, height);
    }

    private static void RunSecretariaChecks(Application application, string storageRoot, string[] args)
    {
        var previewRoot = PreviewRoot(args);
        Check("Secretaria: cadastro de referência mostra CPF formatado, telefone, situação, presença e data de cadastro", () =>
        {
            using var fixture = new ApiFixture();
            var unit = fixture.AddUnit("UBS Cadastro", "Centro");
            var window = OpenMunicipal(fixture, storageRoot, "sec-registry", Appearance.Light);
            try
            {
                var vm = LoginObs(window);
                vm.SelectedUnit = vm.Units.Single();
                var content = Show(window, "units:reference");
                Click(window, content, "Consultar pacientes da UBS");
                content = Layout(window, 1280, 900);
                var list = ListNamed(content, "Pacientes da UBS selecionada");
                // Listagem aberta: nome; "CPF | e-mail | telefone" só com o que foi informado; etiqueta "Desativado" só para o inativo.
                var cells = VisibleTexts(list);
                Require(cells.Any(text => text.StartsWith("529.982.247-25 | ", StringComparison.Ordinal) && text.EndsWith(" | +5511999990000", StringComparison.Ordinal)), "Linha de dados do primeiro cadastro incorreta: " + string.Join(" || ", cells));
                Require(cells.Any(text => text.StartsWith("111.444.777-35 | ", StringComparison.Ordinal) && !text.Contains("não informado", StringComparison.Ordinal) && text.Count(letter => letter == '|') == 1), "Campo vazio deveria ficar de fora da linha de dados: " + string.Join(" || ", cells));
                Require(cells.Count(text => text == "Desativado") == 1 && !cells.Contains("Ativo nesta UBS") && !cells.Contains("52998224725"), "Só o cadastro desativado leva a etiqueta, e o CPF vai formatado.");
                // Ficha em linhas: situação, presença e data de cadastro.
                var created = new DateTimeOffset(2026, 3, 12, 15, 30, 0, TimeSpan.Zero).ToLocalTime().ToString("dd/MM/yyyy HH:mm");
                var ficha = vm.UnitRegistry.Select(record => vm.BuildMunicipalDetails(record)!).SelectMany(details => details.Fields).Select(field => field.Value).ToArray();
                foreach (var expected in new[] { "529.982.247-25", "+5511999990000", "Ativo nesta UBS", "Online no aplicativo", created, "111.444.777-35", "Telefone não informado", "Desativado nesta UBS", "Offline no aplicativo" })
                    Require(ficha.Contains(expected), "Linha ausente na ficha do cadastro de referência: " + expected);
            }
            finally { CloseWindow(window); }
        });
        Check("Secretaria: agenda da UBS filtra por período (validação e uma requisição), situação local, CPF formatado e ordena por data", () =>
        {
            using var fixture = new ApiFixture();
            var unit = fixture.AddUnit("UBS Agenda", "Centro");
            var noon = new DateTimeOffset(DateTime.Today.AddHours(12));
            fixture.AddAppointment(unit.Id, "completed", noon.AddDays(3), name: "Carla Dias");
            fixture.AddAppointment(unit.Id, "missed", noon.AddDays(-2), name: "Bruno Lima");
            fixture.AddAppointment(unit.Id, "scheduled", noon.AddDays(1), name: "Ana Souza");
            fixture.AddAppointment(unit.Id, "completed", noon.AddDays(-90), name: "Fora do padrão");
            var window = OpenMunicipal(fixture, storageRoot, "sec-agenda", Appearance.Dark);
            try
            {
                var vm = LoginObs(window);
                vm.SelectedUnit = vm.Units.Single();
                var content = Show(window, "units:reference");
                Descendants<UnitRegistryView>(content).Single().ModeToggle.SelectedIndex = 1; content = Layout(window, 1280, 900);
                var today = DateTime.Today;
                Require(vm.UnitAgendaFromText == today.AddDays(-30).ToString("dd/MM/yyyy") && vm.UnitAgendaToText == today.AddDays(30).ToString("dd/MM/yyyy"), "Período padrão não é −30/+30 dias.");
                var before = fixture.Count("GET", "/appointments");
                Click(window, content, "Consultar agenda da UBS");
                Require(fixture.Count("GET", "/appointments") == before + 1, "A consulta da agenda fez mais de uma requisição.");
                Require(fixture.LastAppointmentRange.From == new DateTimeOffset(today.AddDays(-30)) && fixture.LastAppointmentRange.To == new DateTimeOffset(today.AddDays(31)), "A API recebeu um intervalo diferente do informado (fim inclusivo).");
                Require(vm.MunicipalAppointments.Count == 3 && vm.MunicipalAppointments.Select(item => item.PatientName).SequenceEqual(["Bruno Lima", "Ana Souza", "Carla Dias"]), "Consultas não vieram em ordem de data real.");
                content = Layout(window, 1280, 900);
                var agenda = ListNamed(content, "Consultas da UBS selecionada");
                Require(agenda.Items.Cast<AppointmentInfo>().Select(item => item.PatientName).SequenceEqual(["Bruno Lima", "Ana Souza", "Carla Dias"]), "A listagem deveria seguir a ordem de data real.");
                Require(VisibleTexts(agenda).Any(text => text.Contains(" | ", StringComparison.Ordinal)) && new[] { "Falta", "Agendada", "Concluída" }.All(VisibleTexts(agenda).Contains), "A listagem deveria trazer a linha de dados e a etiqueta da situação.");
                Require(vm.BuildMunicipalDetails(vm.MunicipalAppointments[0])!.Fields.Any(field => field.Value == "529.982.247-25"), "CPF formatado não aparece na ficha da consulta.");
                // Situação filtra só o que já foi carregado: nenhuma nova requisição.
                var requests = fixture.Requests.Count;
                Combo(content, "Filtrar agenda por situação").SelectedValue = "missed";
                Layout(window, 1280, 900);
                Require(vm.VisibleMunicipalAppointments.Single().PatientName == "Bruno Lima" && fixture.Requests.Count == requests, "O filtro de situação não foi local.");
                vm.UnitAgendaStatus = "cancelled"; Require(vm.MunicipalAppointmentsState == "Nenhuma consulta com esta situação no período.", "Estado de filtro vazio não explicado.");
                vm.UnitAgendaStatus = "";
                // Validação do período: início ≤ fim e no máximo 366 dias; nada é enviado quando inválido.
                requests = fixture.Requests.Count;
                (string From, string To, string Fragment)[] invalid = [("31/12/2026", "01/01/2026", "inicial não pode ser posterior"), ("01/01/2026", "02/01/2027", "no máximo 366 dias"), ("ontem", "hoje", "formato dd/mm/aaaa")];
                foreach (var (from, to, fragment) in invalid)
                {
                    vm.UnitAgendaFromText = from; vm.UnitAgendaToText = to;
                    Pump(vm.LoadMunicipalAppointmentsAsync());
                    Require(vm.UnitAgendaRangeError.Contains(fragment) && fixture.Requests.Count == requests, $"Período inválido ({from}–{to}) não foi barrado: {vm.UnitAgendaRangeError}");
                }
                vm.UnitAgendaFromText = "01/01/2026"; vm.UnitAgendaToText = "01/01/2027";
                Pump(vm.LoadMunicipalAppointmentsAsync());
                Require(vm.UnitAgendaRangeError == "" && fixture.Requests.Count == requests + 1, "Período de exatamente 366 dias deveria ser aceito: " + vm.UnitAgendaRangeError);
                content = Layout(window, 1280, 900);
                SaveScrolled(window, Path.Combine(previewRoot, "municipal-units-agenda-dark.png"));
            }
            finally { CloseWindow(window); }
        });
        Check("Secretaria: Visão geral mostra os totais novos e avisa quantas UBS ficaram fora por não terem base", () =>
        {
            using var fixture = new ApiFixture();
            fixture.AddUnit("UBS Pronta", "Centro"); fixture.AddUnit("UBS Sem Base", "Sul", databaseState: "pending");
            var window = OpenMunicipal(fixture, storageRoot, "sec-overview", Appearance.Light);
            try
            {
                var vm = LoginObs(window);
                var content = Show(window, "home");
                var cards = vm.SummaryCards.ToDictionary(card => card.Title, card => card.Value);
                Require(cards["Pacientes cadastrados"] == "1" && cards["Desativados"] == "2", "Totais novos do resumo incorretos.");
                foreach (var title in new[] { "Pacientes cadastrados", "Desativados", "Vagas abertas", "Ofertas aceitas" }) Require(HasText(content, title), "Cartão ausente: " + title);
                Require(vm.UnitsWithoutDatabaseCount == 1 && VisibleTexts(content).Any(text => text.StartsWith("1 UBS está fora destes números", StringComparison.Ordinal)), "Aviso de UBS sem base não apareceu.");
                var ready = fixture.AddUnit("UBS Nova Pronta", "Norte"); Pump(vm.LoadUnitsAsync());
                vm.SelectedUnit = vm.Units.Single(item => item.Name == "UBS Sem Base"); Pump(vm.ProvisionSelectedUnitDatabaseAsync());
                Settle(vm); // a releitura da rede depois de criar a base roda em segundo plano: espera terminar antes de conferir o aviso
                content = Layout(window, 1280, 900);
                Require(vm.UnitsWithoutDatabaseCount == 0 && !VisibleTexts(content).Any(text => text.Contains("fora destes números")), "O aviso continuou depois de a base ser criada.");
                _ = ready;
            }
            finally { CloseWindow(window); }
        });
        Check("Secretaria: CSV identifica a UBS pelo nome e inclui os campos novos do resumo", () =>
        {
            using var fixture = new ApiFixture();
            var unit = fixture.AddUnit("UBS Alfa", "Centro"); fixture.AddUnit("UBS Beta", "Sul", databaseState: "pending");
            var window = OpenMunicipal(fixture, storageRoot, "sec-csv");
            try
            {
                var vm = LoginObs(window);
                var wholeNetwork = vm.CreateSummaryCsv();
                Require(wholeNetwork.Contains("Unidade;Toda a rede;") && wholeNetwork.Contains("Pacientes desativados;2;") && !wholeNetwork.Contains("Solicitações na fila") && wholeNetwork.Contains("UBS sem base de dados;1;"), "CSV da rede sem os campos novos: " + wholeNetwork);
                vm.SummaryUnitFilter = unit.Id; Settle(vm);
                var csv = vm.CreateSummaryCsv();
                Require(csv.Contains("Unidade;UBS Alfa;") && !csv.Contains(unit.Id.ToString()), "CSV deveria identificar a UBS pelo nome, nunca pelo GUID.");
            }
            finally { CloseWindow(window); }
        });
        Check("Secretaria: um único seletor de UBS carrega sozinho, aplica ao escolher e só aparece onde tem efeito", () =>
        {
            using var fixture = new ApiFixture();
            var alfa = fixture.AddUnit("UBS Alfa", "Centro"); fixture.AddUnit("UBS Beta", "Sul");
            var window = OpenMunicipal(fixture, storageRoot, "sec-unit-filter");
            try
            {
                var vm = LoginObs(window);
                Require(vm.Units.Count == 2 && vm.UnitsLoaded, "As UBS não carregaram sozinhas depois do login.");
                var content = Show(window, "home");
                Require(Descendants<Button>(content).All(button => button.Content?.ToString() is not ("Carregar UBS" or "Aplicar UBS")), "Os botões Carregar/Aplicar UBS ainda existem.");
                var selector = Combo(content, "Filtrar por UBS");
                Require(selector.Items.Count == 3 && ((HealthUnit)selector.Items[0]).SelectorLabel == "Toda a rede", "O seletor deveria ter 'Toda a rede' e as duas UBS.");
                var metrics = fixture.MetricsQueries.Count; var summaries = fixture.SummaryQueries.Count;
                selector.SelectedValue = alfa.Id; Settle(vm);
                Require(fixture.SummaryQueries.Count == summaries + 1 && fixture.MetricsQueries.Count == metrics + 1, "Escolher a UBS deveria fazer uma requisição de resumo e uma de indicadores.");
                Require(fixture.SummaryQueries[^1]["unitId"] == alfa.Id.ToString() && fixture.MetricsQueries[^1]["unitId"] == alfa.Id.ToString(), "A UBS escolhida não foi enviada.");
                Require(vm.UnitPerformance.Single().Name == "UBS Alfa" && vm.AppliedSummaryUnitLabel == "UBS Alfa", "Os números não refletiram a UBS escolhida.");
                selector.SelectedValue = Guid.Empty; Settle(vm);
                Require(fixture.SummaryQueries[^1]["unitId"] is null && vm.UnitPerformance.Count == 2, "Voltar para Toda a rede não removeu o filtro.");
                foreach (var route in new[] { "units", "specialties", "indicators", "slots", "reports", "capacity" })
                {
                    content = Show(window, route);
                    Require(!Descendants<ComboBox>(content).Any(box => AutomationProperties.GetName(box) is "Filtrar por UBS" or "Filtrar resumo por UBS"), "O filtro de UBS continua no corpo de " + route);
                    var fields = Descendants<ComboBox>(OpenPageFilters(window)).ToArray();
                    Require(fields.Any(box => AutomationProperties.GetName(box) is "Filtrar por UBS" or "Filtrar resumo por UBS"), "O filtro de UBS está ausente do painel de " + route);
                    Require(!fields.Any(box => box.Items.Count == 1 && box.Items[0] is string), "Combo 'Indisponível' ainda aparece em " + route);
                }
                foreach (var route in new[] { "alerts", "communication", "network" })
                {
                    content = Show(window, route);
                    Require(!Descendants<ComboBox>(content).Any(box => AutomationProperties.GetName(box) is "Filtrar por UBS" or "Filtrar resumo por UBS" or "Filtrar por região" or "Filtrar por especialidade"), "Filtro sem efeito aparece em " + route);
                }
            }
            finally { CloseWindow(window); }
        });
        Check("Secretaria: Usuários e histórico carrega ao abrir, mostra carregamento e vazio e desabilita botões sem seleção", () =>
        {
            using var fixture = new ApiFixture();
            fixture.AddUnit("UBS Vínculo");
            var window = OpenMunicipal(fixture, storageRoot, "sec-users", Appearance.Light);
            try
            {
                var vm = LoginObs(window);
                Require(vm.Staff.Count == 0 && vm.StaffState == "As contas ainda não foram carregadas.", "Contas não deveriam carregar antes de abrir a seção.");
                fixture.PauseReads = new(TaskCreationOptions.RunContinuationsAsynchronously);
                window.ViewModel.Navigate("users:accounts"); PumpUntil(() => vm.IsBusy);
                Require(vm.StaffState == "Carregando as contas…", "Estado de carregamento não apareceu: " + vm.StaffState);
                fixture.PauseReads.TrySetResult(); Settle(vm);
                var content = Layout(window, 1280, 900);
                Require(vm.Staff.Count == 1 && vm.StaffState == "" && fixture.Count("GET", "/api/municipal/staff") == 1, "As contas não carregaram sozinhas ao abrir a seção.");
                // Sem seleção não há balão nem ação; com a conta selecionada o balão traz as ações dela.
                var accounts = ListNamed(content, "Contas OBS");
                Require(!ListingBehavior.GetBalloon(accounts)!.IsVisible, "Sem seleção o balão de ações deveria estar escondido.");
                SelectRows(window, accounts, vm.Staff.Single());
                Require(ListingBehavior.GetBalloon(accounts)!.IsVisible && BalloonButton(accounts, "Redefinir senha provisória").IsVisible && BalloonButton(accounts, "Desativar conta").IsVisible && !BalloonButton(accounts, "Reativar").IsVisible, "O balão deveria trazer Redefinir senha e Desativar para a conta ativa.");
                accounts.UnselectAll(); Layout(window, 1280, 900);
                window.ViewModel.Navigate("users:history"); content = Layout(window, 1280, 900);
                Require(HasText(content, "Nenhuma ação registrada para este período e esta UBS."), "Estado vazio do histórico ausente.");
                window.ViewModel.Navigate("users:accounts"); content = Layout(window, 1280, 900);
                SavePreview(content, Path.Combine(previewRoot, "municipal-users-light.png"));
                fixture.StaffAccounts.Clear(); Pump(vm.RefreshAutomaticallyAsync());
                Require(vm.StaffState == "Nenhuma conta OBS cadastrada." && HasText(Layout(window, 1280, 900), "Nenhuma conta OBS cadastrada."), "Estado vazio das contas não apareceu.");
            }
            finally { fixture.PauseReads?.TrySetResult(); CloseWindow(window); }
        });
        Check("Secretaria: 503 de uma base de UBS vira uma mensagem única, mantém os números e os marca como desatualizados", () =>
        {
            using var fixture = new ApiFixture();
            fixture.AddUnit("UBS Falha");
            var window = OpenMunicipal(fixture, storageRoot, "sec-503", Appearance.Light);
            try
            {
                var vm = LoginObs(window);
                var shown = new List<string>();
                vm.PropertyChanged += (_, change) => { if (change.PropertyName == nameof(WorkspaceViewModel.Message) && vm.Message.Length > 0) shown.Add(vm.Message); };
                Require(vm.PatientCount == "1" && vm.OverviewCards[0].Value != "—", "Pré-condição: números carregados.");
                fixture.FailNetwork503 = true;
                for (var cycle = 0; cycle < 3; cycle++) Pump(vm.RefreshAutomaticallyAsync());
                Require(shown.Count == 1 && vm.Message.Contains("base de unidade está indisponível"), $"O 503 deveria aparecer uma vez com a causa: {shown.Count} / {vm.Message}");
                Require(vm.IsDataStale && vm.PatientCount == "1" && vm.OverviewCards[0].Value != "—", "Os números anteriores deveriam ficar, marcados como desatualizados.");
                var content = Layout(window, 1280, 900);
                Require(Descendants<Border>(content).Single(border => AutomationProperties.GetName(border) == "Aviso de dados desatualizados").IsVisible, "Aviso de dados desatualizados ausente.");
                vm.MetricsPeriodDays = 7; Settle(vm);
                Require(vm.MessageKind == MessageKind.Error && vm.Message.Contains("indisponível") && vm.OverviewCards[0].Value != "—", "Falha de filtro deveria avisar e manter os números anteriores.");
                fixture.FailNetwork503 = false; vm.MetricsPeriodDays = 30; Settle(vm);
                Require(!vm.IsDataStale && !vm.HasMessage, "Recuperar a leitura deveria limpar o erro e o aviso de dados antigos.");
            }
            finally { CloseWindow(window); }
        });
        Check("Secretaria: UBS reatribuída mantém cadastro e agenda, UBS salva entra ordenada por nome e a seleção sobrevive à pesquisa", () =>
        {
            using var fixture = new ApiFixture();
            fixture.AddUnit("UBS Alfa", "Centro"); fixture.AddUnit("UBS Zeta", "Sul");
            var window = OpenMunicipal(fixture, storageRoot, "sec-unit-fixes", Appearance.Light);
            try
            {
                var vm = LoginObs(window);
                vm.SelectedUnit = vm.Units.Single(item => item.Name == "UBS Zeta");
                Pump(vm.LoadUnitRegistryAsync()); Pump(vm.LoadMunicipalAppointmentsAsync());
                Require(vm.UnitRegistry.Count == 2 && vm.MunicipalAppointments.Count == 1, "Pré-condição: cadastro e agenda carregados.");
                Pump(vm.LoadUnitsAsync()); vm.SelectedUnit = vm.Units.Single(item => item.Name == "UBS Zeta");
                Require(vm.UnitRegistry.Count == 2 && vm.UnitRegistryLoaded && vm.MunicipalAppointments.Count == 1 && vm.MunicipalAppointmentsLoaded, "Reatribuir a mesma UBS apagou cadastro ou agenda.");
                void Save(string name) { vm.NewUnitDraft(); vm.UnitName = name; vm.UnitRegion = "Leste"; vm.UnitAddress = "Rua Nova, 1"; vm.UnitLatitude = "-23,5"; vm.UnitLongitude = "-46,6"; Pump(vm.SaveUnitAsync()); }
                Save("UBS Beta");
                Require(vm.Units.Select(item => item.Name).SequenceEqual(["UBS Alfa", "UBS Beta", "UBS Zeta"]), "UBS nova não entrou na posição ordenada por nome: " + string.Join(", ", vm.Units.Select(item => item.Name)));
                vm.EditSelectedUnit(); vm.UnitName = "Aaa Primeira"; Pump(vm.SaveUnitAsync());
                Require(vm.Units[0].Name == "Aaa Primeira" && vm.Units.Count == 3, "UBS renomeada não foi movida para a posição ordenada.");
                var content = Show(window, "units:registry");
                var list = ListNamed(content, "UBS cadastradas");
                var zeta = vm.Units.Single(item => item.Name == "UBS Zeta"); var first = vm.Units.Single(item => item.Name == "Aaa Primeira");
                SelectRows(window, list, zeta, first);
                Require(ListingBehavior.GetSelectedItems<HealthUnit>(list).Select(item => item.Name).SequenceEqual(["Aaa Primeira", "UBS Zeta"]) && ListingBehavior.GetBalloon(list)!.IsVisible, "A lista não mostrou as UBS selecionadas.");
                // A pesquisa esconde o que não combina (e isso sai da seleção); o que continua à vista continua selecionado, sem o balão piscar.
                Input(content, "Pesquisar: Cadastro de UBS").Text = "Aaa"; Layout(window, 1280, 900);
                Require(list.Items.Count == 1 && ListingBehavior.GetSelectedItems<HealthUnit>(list).Select(item => item.Name).SequenceEqual(["Aaa Primeira"]) && ListingBehavior.GetBalloon(list)!.IsVisible && ListingBehavior.GetBalloon(list)!.Label == "Aaa Primeira", "A pesquisa não deveria desfazer a seleção da UBS que continua à vista.");
                Input(content, "Pesquisar: Cadastro de UBS").Text = ""; Layout(window, 1280, 900);
                Require(list.Items.Count == 3 && ListingBehavior.GetSelectedItems<HealthUnit>(list).Select(item => item.Name).SequenceEqual(["Aaa Primeira"]), "A seleção da UBS à vista deveria sobreviver à pesquisa.");
            }
            finally { CloseWindow(window); }
        });
        Check("Secretaria: formulário de UBS (pop-up) valida nome, região e endereço sem exibir coordenadas", () =>
        {
            using var fixture = new ApiFixture();
            var window = OpenMunicipal(fixture, storageRoot, "sec-unit-form", Appearance.Light);
            try
            {
                var vm = LoginObs(window);
                Show(window, "units:registry");
                InspectHeaderForm(window, "Nova UBS", dialog =>
                {
                    var primary = Descendants<Button>(dialog).Single(button => button.Content as string == "Salvar UBS");
                    InvokeButton(primary); PumpUntil(() => !dialog.IsBusy);
                    Require(dialog.IsVisible && !dialog.Saved && vm.MessageKind == MessageKind.Error && fixture.Count("POST", "/api/municipal/units") == 0, "Formulário vazio deveria ser barrado antes da API, com o pop-up aberto.");
                    Require(dialog.ErrorMessage.Contains("Corrija os campos") && vm.UnitNameError.Contains("nome da UBS") && vm.UnitRegionError.Contains("região") && vm.UnitAddressError.Contains("endereço") && vm.UnitLatitudeError.Contains("Latitude") && vm.UnitLongitudeError.Contains("Longitude"), "Faltou mensagem em algum campo ou no pop-up.");
                    dialog.UpdateLayout();
                    Require(VisibleTexts(dialog).Count(text => text.Contains("Informe") || text.Contains("inválida")) >= 3, "As mensagens dos campos exibidos não aparecem no pop-up.");
                    Require(!Descendants<TextBox>(dialog).Any(box => box.IsVisible && (AutomationProperties.GetName(box) == "Latitude da UBS" || AutomationProperties.GetName(box) == "Longitude da UBS")), "O formulário ainda mostra coordenadas manuais.");
                    Input(dialog, "Nome da UBS").Text = "UBS Teste"; Input(dialog, "Região da UBS").Text = "Centro"; Input(dialog, "Endereço da UBS").Text = "Rua A, 1";
                    (string Latitude, string Longitude, string Fragment)[] warnings = [("-46,63", "-23,55", "trocadas"), ("0", "0", "oceano"), ("48,85", "2,35", "fora do Brasil"), ("-23,55", "-46,63", "")];
                    foreach (var (latitude, longitude, fragment) in warnings)
                    {
                        vm.UnitLatitude = latitude; vm.UnitLongitude = longitude;
                        Require(fragment.Length == 0 ? vm.UnitCoordinateWarning == "" : vm.UnitCoordinateWarning.Contains(fragment), $"Aviso incorreto para {latitude}/{longitude}: {vm.UnitCoordinateWarning}");
                    }
                    vm.UnitLatitude = "-46,63"; vm.UnitLongitude = "-23,55";
                    dialog.UpdateLayout(); SavePreview(dialog, Path.Combine(previewRoot, "municipal-unit-form-warning-light.png"));
                    InvokeButton(primary); PumpUntil(() => !dialog.IsVisible);
                    Require(dialog.Saved && vm.MessageKind == MessageKind.Success && vm.Units.Count == 1 && fixture.Count("POST", "/api/municipal/units") == 1, "O aviso de coordenadas não deveria bloquear o salvamento: " + vm.Message);
                });
            }
            finally { CloseWindow(window); }
        });
        Check("Secretaria: estado da base da UBS, nova tentativa com falha e sucesso no pop-up com os passos, e aviso ao cadastrar UBS sem base", () =>
        {
            using var fixture = new ApiFixture { FailProvisioning = true };
            fixture.AddUnit("UBS Pronta", "Centro"); fixture.AddUnit("UBS Sem Base", "Sul", databaseState: "pending");
            var window = OpenMunicipal(fixture, storageRoot, "sec-database", Appearance.Dark);
            try
            {
                var vm = LoginObs(window);
                var content = Show(window, "units:registry");
                var list = ListNamed(content, "UBS cadastradas");
                Require(VisibleTexts(list).Contains("Base pronta") && VisibleTexts(list).Contains("Base não criada"), "A lista não mostra o estado da base de cada UBS.");
                SelectRows(window, list, vm.Units.Single(unit => unit.Name == "UBS Pronta"));
                Require(!BalloonButton(list, "Tentar criar a base de novo").IsVisible, "O botão de nova tentativa só deveria aparecer para a UBS sem base.");
                var pending = vm.Units.Single(unit => unit.Name == "UBS Sem Base");
                SelectRows(window, list, pending);
                Require(BalloonButton(list, "Tentar criar a base de novo").IsVisible && BalloonButton(list, "Editar").IsVisible, "UBS sem base deveria oferecer Editar e Tentar criar a base de novo.");
                vm.SelectedUnit = pending;
                Require(!vm.CanReadUnitRegistry, "A consulta nominal deveria ficar bloqueada para a UBS sem base.");
                InspectModal<FormDialog>(window, dialog =>
                {
                    Require(VisibleTexts(dialog).Any(text => text.Contains("permissão para criar bancos")), "A explicação da permissão não apareceu no pop-up.");
                    var primary = Descendants<Button>(dialog).Single(button => button.Content as string == "Criar a base");
                    dialog.UpdateLayout(); SavePreview(dialog, Path.Combine(previewRoot, "municipal-units-database-dark.png"));
                    InvokeButton(primary); PumpUntil(() => !dialog.IsBusy);
                    Require(dialog.IsVisible && vm.MessageKind == MessageKind.Error && vm.Message.Contains("permissão para criar bancos") && !vm.Message.Contains("private-server-secret") && vm.SelectedUnit!.DatabasePending && vm.UnitProvisionSteps.Single().State == StepState.Failed, "Falha database-provisioning-failed não foi explicada no pop-up, com o passo marcado como falho.");
                    fixture.FailProvisioning = false;
                    InvokeButton(primary); PumpUntil(() => !dialog.IsVisible);
                    Require(dialog.Saved && vm.MessageKind == MessageKind.Success && !vm.SelectedUnit!.DatabasePending && vm.SelectedUnit.DatabaseLabel == "Base pronta" && vm.CanReadUnitRegistry && vm.UnitProvisionSteps.Single().State == StepState.Done, "A nova tentativa não deixou a base pronta.");
                }, () => InvokeButton(BalloonButton(list, "Tentar criar a base de novo")));
                Require(fixture.History.Any(item => item.Kind == "unit-database-created"), "A API não registrou a criação da base.");
                fixture.NewUnitsPending = true;
                vm.NewUnitDraft(); vm.UnitName = "UBS Nova"; vm.UnitRegion = "Leste"; vm.UnitAddress = "Rua Nova, 1"; vm.UnitLatitude = "-23,5"; vm.UnitLongitude = "-46,6"; Pump(vm.SaveUnitAsync());
                Require(vm.Units.Single(unit => unit.Name == "UBS Nova").DatabasePending && vm.MessageKind == MessageKind.Info && vm.Message.Contains("PostgreSQL") && vm.Message.Contains("Tentar criar a base"), "O cadastro de UBS sem base não informou o estado retornado: " + vm.Message);
            }
            finally { CloseWindow(window); }
        });
    }
}
