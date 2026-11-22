using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Nora.Desktop.Generics;
using Nora.Desktop.Models;
using Nora.Desktop.Services;
using Nora.Desktop.ViewModels;

namespace Nora.Desktop.Checks;

/// <summary>
/// Fechamento de 2026-11-17 no desktop: score em pontos (0 a 1000) e ordem de oferta da Vaga Relâmpago, histórico paginado da Fila e das Vagas (P4) e contas de equipe sem CPF, entrada só por e-mail (P-053).
/// </summary>
internal static partial class Program
{
    private const string FixtureCpf = "52998224725";

    private static void RunScoreHistoryChecks(string storageRoot, string[] args)
    {
        var previewRoot = PreviewRoot(args);
        Check("Score em pontos: arredondamento, faixas (0–299, 300–599, 600–799, 800–1000) e texto sem histórico", () =>
        {
            Require(DisplayFormat.PointsFromScore(0.9) == 900 && DisplayFormat.PointsFromScore(0.4286) == 429 && DisplayFormat.PointsFromScore(0.9995) == 1000 && DisplayFormat.PointsFromScore(0) == 0 && DisplayFormat.PointsFromScore(1) == 1000, "Pontos = score × 1000, meio para cima.");
            Require(DisplayFormat.PointsFromScore(null) == 1000, "Sem consulta encerrada o score começa em 1000.");
            Require(new[] { 0, 299, 300, 599, 600, 799, 800, 1000 }.Select(DisplayFormat.BandFor).SequenceEqual(["low", "low", "regular", "regular", "good", "good", "excellent", "excellent"]), "Limites das faixas incorretos.");
            Require(new[] { "low", "regular", "good", "excellent", null }.Select(DisplayFormat.BandLabel).SequenceEqual(["Baixo", "Regular", "Bom", "Excelente", "—"]), "Rótulos das faixas incorretos.");
            Require(DisplayFormat.ScoreText(742, "good", true) == "742 · Bom" && DisplayFormat.ScoreText(1000, "excellent", false) == "1000 · sem consultas encerradas ainda", "Texto do score incorreto.");
            var fromApi = new UnitAttendance(9, 1, 0, 0, 0.9, 905, "excellent", true);
            Require(fromApi.PointsValue == 905 && fromApi.ScoreDisplay == "905 · Excelente", "O valor da API deveria prevalecer sobre o cálculo local.");
            var legacy = new UnitAttendance(0, 0, 0, 0, null);
            Require(legacy.PointsValue == 1000 && !legacy.HasHistoryValue && legacy.ScoreDisplay == "1000 · sem consultas encerradas ainda", "API antiga sem consulta encerrada: 1000, sem histórico.");
        });

        Check("Histórico paginado: o ciclo preserva o que foi carregado, sem duplicar nem perder item; sem contato com a página nova recomeça com aviso", () =>
        {
            var pager = new HistoryPager<UnitWaitingEntry, long>(entry => entry.Id, entry => entry.IsActive);
            UnitWaitingEntry Item(long id, bool active = false) => new(id, FixtureCpf, "Paciente " + id, "Cardiologia", DateTimeOffset.UtcNow, active ? "waiting" : "completed");
            long[] Ids(IEnumerable<UnitWaitingEntry> items) => items.Select(item => item.Id).Order().ToArray();
            var shown = pager.ApplyPage([Item(10, true), Item(9), Item(8)], "c8", out var restarted);
            Require(!restarted && Ids(shown).SequenceEqual([8, 9, 10]) && pager.HasMore && pager.Cursor == "c8", "Primeira página incorreta.");
            shown = pager.ApplyOlder([Item(7), Item(6), Item(8)], "c6");
            Require(Ids(shown).SequenceEqual([6, 7, 8, 9, 10]) && pager.Cursor == "c6", "Mais histórico deveria acrescentar só o que não estava (sem repetir o 8).");
            // Ciclo: um item foi encerrado, a página desliza (o 8 sai dela) e o carregado continua.
            shown = pager.ApplyPage([Item(11), Item(10), Item(9)], "c9", out restarted);
            Require(!restarted && Ids(shown).SequenceEqual([6, 7, 8, 9, 10, 11]) && pager.Cursor == "c6" && shown.Select(item => item.Id).Distinct().Count() == shown.Length, "O ciclo deveria preservar o histórico carregado e manter o cursor do mais antigo: " + string.Join(",", Ids(shown)));
            // Sem nenhum encerrado em comum: não há como garantir que não falta nada entre a página e o carregado.
            shown = pager.ApplyPage([Item(30), Item(29), Item(28)], "c28", out restarted);
            Require(restarted && Ids(shown).SequenceEqual([28, 29, 30]) && pager.Cursor == "c28", "Sem contato com o carregado o histórico deveria recomeçar, avisando.");
            // Página que já é o histórico todo (sem cursor): não sobra nada fora dela.
            shown = pager.ApplyPage([Item(30), Item(29)], null, out restarted);
            Require(!restarted && Ids(shown).SequenceEqual([29, 30]) && !pager.HasMore, "Página sem cursor é o histórico inteiro.");
            pager.Clear();
            Require(pager.Shown.Count == 0 && !pager.HasMore, "Limpar deveria esquecer tudo.");
            // Entrada ativa e antiga que é encerrada: sai da primeira página (mais antiga que as encerradas dela) e não pode sumir; vira "Encerrada" e continua depois do histórico carregado.
            var closing = new HistoryPager<UnitWaitingEntry, long>(entry => entry.Id, entry => entry.IsActive, entry => entry with { Status = "closed" });
            closing.ApplyPage([Item(2, true), Item(10), Item(9)], "c9", out restarted);
            closing.ApplyOlder([Item(8), Item(7)], "c7");
            shown = closing.ApplyPage([Item(11), Item(10), Item(9)], "c9", out restarted);
            Require(!restarted && Ids(shown).SequenceEqual([2, 7, 8, 9, 10, 11]) && shown.Single(item => item.Id == 2) is { IsActive: false, Status: "closed", StatusDisplay: "Encerrada" } && closing.Cursor == "c7", "A entrada ativa encerrada não pode sumir da lista: " + string.Join(",", Ids(shown)));
            shown = closing.ApplyPage([Item(11), Item(10), Item(9)], "c9", out restarted);
            Require(Ids(shown).SequenceEqual([2, 7, 8, 9, 10, 11]) && shown.Count(item => item.Id == 2) == 1, "O ciclo seguinte deveria manter a entrada encerrada, sem duplicar.");
        });

        Check("Fila OBS: ordem de oferta da API (mais pontos primeiro, empate pela entrada), coluna Score, texto da prioridade e ficha; sem os campos novos o cliente calcula", () =>
        {
            using var fixture = new ApiFixture();
            var unit = fixture.AddUnit("UBS Prioridade");
            var now = DateTimeOffset.UtcNow;
            fixture.AddWaiting(unit.Id, FixtureCpf, "Ana Souza", "Cardiologia", now.AddHours(-5), points: 500);
            fixture.AddWaiting(unit.Id, "11144477735", "Bruno Lima", "Cardiologia", now.AddHours(-3), points: 900);
            fixture.AddWaiting(unit.Id, "12345678909", "Carla Dias", "Cardiologia", now.AddHours(-4), points: 900);
            fixture.AddWaiting(unit.Id, "39053344705", "Diego Reis", "Pediatria", now.AddHours(-1));
            fixture.AddWaiting(unit.Id, "98765432100", "Edu Santos", "Cardiologia", now.AddHours(-9), "completed", 100);
            var window = OpenOperational(fixture, storageRoot, "queue-score", Appearance.Light);
            try
            {
                var vm = LoginObs(window);
                var content = Show(window, "queue", 1280, 1100);
                string Row(string name) { var entry = vm.UnitWaitingList.Single(item => item.PatientName == name); return $"{entry.PositionDisplay}/{entry.ScoreDisplay}"; }
                Require(Row("Carla Dias") == "1º/900 · Excelente" && Row("Bruno Lima") == "2º/900 · Excelente" && Row("Ana Souza") == "3º/500 · Regular" && Row("Diego Reis") == "1º/1000 · Excelente", $"Ordem de oferta ou score incorretos: {Row("Carla Dias")} {Row("Bruno Lima")} {Row("Ana Souza")} {Row("Diego Reis")}.");
                Require(vm.UnitWaitingList.Single(item => item.PatientName == "Edu Santos") is { PositionDisplay: "—", ScoreDisplay: "100 · Baixo" }, "A entrada encerrada não tem ordem de oferta, mas mostra o score.");
                var grid = GridNamed(content, "Fila por especialidade da UBS");
                Require(grid.Columns.Select(column => column.Header?.ToString()).SequenceEqual(["Ordem de oferta", "Paciente", "Especialidade", "Score", "Espera", "Entrada", "Situação"]), "Colunas da fila divergem do esperado.");
                Require(grid.Columns.Single(column => column.Header?.ToString() == "Score").SortMemberPath == "ScoreSort" && grid.Columns.Single(column => column.Header?.ToString() == "Ordem de oferta").SortMemberPath == "OfferSort", "Score e Ordem de oferta ordenariam pelo texto.");
                var byScore = vm.UnitWaitingList.OrderBy(item => item.ScoreSort).Select(item => item.PatientName).ToArray();
                Require(byScore[0] == "Edu Santos" && byScore[^1] == "Diego Reis" && vm.UnitWaitingList.Single(item => item.PatientName == "Edu Santos").OfferSort == int.MaxValue, "A chave numérica do score deveria ordenar 100 antes de 1000 (e a encerrada sem ordem vai por último na ordem de oferta): " + string.Join(",", byScore));
                var texts = VisibleTexts(content);
                Require(texts.Contains("Quem tem score maior recebe antes as ofertas de Vaga Relâmpago; empate segue a ordem de entrada na fila. O score não muda a prioridade clínica nem o direito ao atendimento. A ordem de oferta, o score e o tempo de espera são atualizados a cada leitura."), "A Fila deveria explicar a prioridade pelo score.");
                Require(!texts.Any(text => text.Contains("pela ordem de entrada entre as entradas ativas", StringComparison.Ordinal)), "O texto antigo (só ordem de entrada) continua na tela.");
                var details = vm.BuildDetails(vm.UnitWaitingList.Single(item => item.PatientName == "Bruno Lima"))!;
                Require(details.Fields.Single(field => field.Label == "Ordem de oferta na especialidade").Value == "2º" && details.Fields.Single(field => field.Label == "Score do paciente").Value == "900 · Excelente", "A ficha da entrada deveria trazer a ordem de oferta e o score.");
                SavePreview(Layout(window, 1280, 1100), System.IO.Path.Combine(previewRoot, "obs-fila-score-ordem-light.png"));
            }
            finally { CloseWindow(window); }
            // API antiga: sem points, band nem offerOrder o cliente calcula pela ordem de entrada e o Score aparece como —.
            using var legacy = new ApiFixture { OmitWaitingScore = true };
            var legacyUnit = legacy.AddUnit("UBS Antiga");
            legacy.AddWaiting(legacyUnit.Id, FixtureCpf, "Ana Souza", "Cardiologia", now.AddHours(-1));
            legacy.AddWaiting(legacyUnit.Id, "11144477735", "Bruno Lima", "Cardiologia", now.AddHours(-3));
            var oldWindow = OpenOperational(legacy, storageRoot, "queue-score-legacy", Appearance.Light);
            try
            {
                var vm = LoginObs(oldWindow); Show(oldWindow, "queue", 1280, 1100);
                Require(vm.UnitWaitingList.Single(item => item.PatientName == "Bruno Lima") is { PositionDisplay: "1º", ScoreDisplay: "—" } && vm.UnitWaitingList.Single(item => item.PatientName == "Ana Souza").PositionDisplay == "2º", "Sem os campos novos a ordem deveria ser a de entrada e o score —.");
            }
            finally { CloseWindow(oldWindow); }
        });

        Check("Fila OBS: Carregar mais histórico acrescenta as encerradas mais antigas; o ciclo de 20 s preserva o carregado, sem duplicar nem perder item; esgotado, o botão some", () =>
        {
            using var fixture = new ApiFixture { HistoryPageSize = 3 };
            var unit = fixture.AddUnit("UBS Histórico da fila");
            var now = DateTimeOffset.UtcNow;
            for (var index = 1; index <= 8; index++) fixture.AddWaiting(unit.Id, FixtureCpf, "Encerrada " + index, "Cardiologia", now.AddDays(index - 20), index % 2 == 0 ? "completed" : "cancelled");
            fixture.AddWaiting(unit.Id, "11144477735", "Ativa Um", "Cardiologia", now.AddHours(-2));
            fixture.AddWaiting(unit.Id, "12345678909", "Ativa Dois", "Cardiologia", now.AddHours(-1), "offered");
            var window = OpenOperational(fixture, storageRoot, "queue-history", Appearance.Light);
            try
            {
                var vm = LoginObs(window);
                var content = Show(window, "queue", 1280, 1500);
                long[] Ids() => vm.UnitWaitingList.Select(item => item.Id).Order().ToArray();
                void NoDuplicates(string when) => Require(vm.UnitWaitingList.Select(item => item.Id).Distinct().Count() == vm.UnitWaitingList.Count, "Item duplicado na fila " + when + ".");
                Require(Ids().SequenceEqual([6, 7, 8, 9, 10]) && vm.CanLoadMoreQueueHistory && vm.QueueHistoryState == "Há entradas encerradas mais antigas: use Carregar mais histórico.", "A primeira leitura deveria trazer as 2 ativas e só as 3 encerradas mais recentes: " + string.Join(",", Ids()));
                Require(fixture.HistoryReads[^1] == "waiting-list" && vm.QueueWaitingCount == "2", "A leitura inicial não leva cursor, e a contagem de ativas ignora as encerradas.");
                var button = FindActionButton(content, "Carregar mais histórico");
                Require(button.IsEnabled && AutomationProperties.GetName(button) == "Carregar mais histórico da fila", "O botão deveria existir e estar habilitado.");
                var firstPageIds = Ids();
                // Carregar mais só acrescenta no fim: sem Reset, a rolagem, o foco e a seleção da tabela ficam onde estavam.
                var loadActions = new List<System.Collections.Specialized.NotifyCollectionChangedAction>();
                System.Collections.Specialized.NotifyCollectionChangedEventHandler recordLoad = (_, args) => loadActions.Add(args.Action);
                vm.UnitWaitingList.CollectionChanged += recordLoad;
                Click(window, content, "Carregar mais histórico", 1280, 1500);
                vm.UnitWaitingList.CollectionChanged -= recordLoad;
                Require(loadActions.Contains(System.Collections.Specialized.NotifyCollectionChangedAction.Add) && !loadActions.Contains(System.Collections.Specialized.NotifyCollectionChangedAction.Reset), "Carregar mais deveria acrescentar linhas sem Reset (que leva a tabela ao topo): " + string.Join(",", loadActions));
                Require(FindActionButton(content, "Carregar mais histórico").IsEnabled, "Depois de carregar mais, o botão deveria continuar habilitado (havia mais histórico), sem esperar o ciclo de 20 s.");
                Require(Ids().SequenceEqual([3, 4, 5, 6, 7, 8, 9, 10]) && fixture.HistoryReads[^1].StartsWith("waiting-list?historyBefore=", StringComparison.Ordinal) && vm.MessageKind == MessageKind.Success && vm.Message == "Histórico da fila ampliado.", "Carregar mais deveria acrescentar as 3 encerradas seguintes: " + string.Join(",", Ids()));
                NoDuplicates("depois de carregar mais");
                // Ciclo de 20 s sem mudança: nada é recriado e o histórico carregado continua.
                var changes = 0; vm.UnitWaitingList.CollectionChanged += (_, _) => changes++;
                Pump(vm.RefreshAutomaticallyAsync());
                Require(changes == 0 && Ids().SequenceEqual([3, 4, 5, 6, 7, 8, 9, 10]), "O ciclo não deveria mexer na lista nem descartar o histórico carregado.");
                // Uma entrada ativa é encerrada: a primeira página desliza, mas nada que estava na lista some e nada repete.
                fixture.ReplaceWaiting(unit.Id, 9, item => item with { Status = "completed" });
                Pump(vm.RefreshAutomaticallyAsync());
                Require(Ids().SequenceEqual([3, 4, 5, 6, 7, 8, 9, 10]) && vm.UnitWaitingList.Single(item => item.Id == 9).Status == "completed" && vm.QueueWaitingCount == "1", "A entrada encerrada deveria passar a histórico sem sumir nenhuma das já carregadas: " + string.Join(",", Ids()));
                NoDuplicates("depois do ciclo");
                Require(vm.CanLoadMoreQueueHistory && vm.QueueHistoryState == "Há entradas encerradas mais antigas: use Carregar mais histórico.", "Ainda há encerradas mais antigas.");
                Click(window, content, "Carregar mais histórico", 1280, 1500);
                Require(Ids().SequenceEqual([1, 2, 3, 4, 5, 6, 7, 8, 9, 10]) && !vm.CanLoadMoreQueueHistory && vm.QueueHistoryState == "" && vm.Message == "Todo o histórico da fila está na lista.", "Esgotado o histórico o botão deveria sumir: " + string.Join(",", Ids()));
                NoDuplicates("no fim do histórico");
                Require(!Descendants<Button>(content).Any(candidate => candidate.IsVisible && candidate.Content?.ToString() == "Carregar mais histórico"), "O botão ainda aparece sem mais histórico.");
                // Trocar de UBS (ou sair) esquece o histórico carregado.
                vm.LogoutCommand.Execute(null); Settle(vm);
                Require(vm.UnitWaitingList.Count == 0 && !vm.CanLoadMoreQueueHistory && vm.QueueHistoryState == "", "Sair deveria esquecer a fila e o histórico.");
            }
            finally { CloseWindow(window); }
        });

        Check("Fila OBS: se a página nova perde o contato com o histórico carregado (muitos encerramentos entre dois ciclos), o histórico recomeça e a tela avisa", () =>
        {
            using var fixture = new ApiFixture { HistoryPageSize = 2 };
            var unit = fixture.AddUnit("UBS Recomeço");
            var now = DateTimeOffset.UtcNow;
            for (var index = 1; index <= 6; index++) fixture.AddWaiting(unit.Id, FixtureCpf, "Encerrada " + index, "Cardiologia", now.AddDays(index - 20), "completed");
            fixture.AddWaiting(unit.Id, "11144477735", "Ativa", "Cardiologia", now.AddHours(-1));
            var window = OpenOperational(fixture, storageRoot, "queue-history-restart", Appearance.Light);
            try
            {
                var vm = LoginObs(window);
                var content = Show(window, "queue", 1280, 1500);
                Click(window, content, "Carregar mais histórico", 1280, 1500);
                Require(vm.UnitWaitingList.Count == 5, "Pré-condição: duas páginas de histórico carregadas (4 encerradas + 1 ativa).");
                for (var index = 8; index <= 10; index++) fixture.AddWaiting(unit.Id, FixtureCpf, "Encerrada " + index, "Cardiologia", now.AddMinutes(-index), "completed");
                Pump(vm.RefreshAutomaticallyAsync());
                Require(vm.UnitWaitingList.Count == 3 && vm.UnitWaitingList.Any(item => item.PatientName == "Ativa") && vm.QueueHistoryState.StartsWith("O histórico carregado foi reiniciado", StringComparison.Ordinal) && vm.QueueHistoryState.Contains("Carregar mais histórico"), "O histórico deveria recomeçar da página nova, com aviso: " + vm.QueueHistoryState);
                Require(vm.UnitWaitingList.Select(item => item.Id).Distinct().Count() == vm.UnitWaitingList.Count, "Item duplicado depois do recomeço.");
                Click(window, content, "Carregar mais histórico", 1280, 1500);
                Require(!vm.QueueHistoryState.StartsWith("O histórico carregado foi reiniciado", StringComparison.Ordinal), "O aviso deveria sair quando a pessoa pede mais histórico.");
            }
            finally { CloseWindow(window); }
        });

        Check("Vagas Relâmpago OBS: Carregar mais histórico no mesmo formato da fila; vagas em andamento aparecem sempre; o ciclo preserva o carregado", () =>
        {
            using var fixture = new ApiFixture { HistoryPageSize = 2 };
            var unit = fixture.AddUnit("UBS Histórico das vagas");
            var start = DateTimeOffset.UtcNow.AddDays(1); var created = DateTimeOffset.UtcNow.AddDays(-3);
            for (var index = 1; index <= 5; index++)
                fixture.AddSlot(unit.Id, new UnitLightningSlot(Guid.NewGuid(), null, "Cardiologia", "Dra. " + index, start.AddHours(index), start.AddHours(index).AddMinutes(30), "closed", created.AddHours(index), ClosedReason: "discarded"));
            fixture.AddSlot(unit.Id, new UnitLightningSlot(Guid.NewGuid(), null, "Pediatria", "Dr. Aberta", start, start.AddMinutes(30), "open", created));
            fixture.AddSlot(unit.Id, new UnitLightningSlot(Guid.NewGuid(), null, "Pediatria", "Dr. Aprovar", start.AddHours(9), start.AddHours(9).AddMinutes(30), "awaiting_approval", created, ApprovalDeadline: DateTimeOffset.UtcNow.AddHours(3)));
            var window = OpenOperational(fixture, storageRoot, "slots-history", Appearance.Light);
            try
            {
                var vm = LoginObs(window);
                var content = Show(window, "slots", 1280, 1800);
                Require(vm.UnitLightningSlots.Count == 4 && vm.UnitLightningSlots.Count(item => item.IsActive) == 2 && vm.AwaitingSlots.Count == 1 && vm.CanLoadMoreSlotsHistory && vm.SlotsHistoryState == "Há vagas encerradas mais antigas: use Carregar mais histórico.", "A primeira leitura deveria trazer as 2 vagas em andamento e só 2 encerradas: " + vm.UnitLightningSlots.Count);
                Require(fixture.HistoryReads[^1] == "lightning-slots" && vm.SlotOpenCount == "1", "A leitura inicial das vagas não leva cursor.");
                var slotActions = new List<System.Collections.Specialized.NotifyCollectionChangedAction>();
                System.Collections.Specialized.NotifyCollectionChangedEventHandler recordSlots = (_, args) => slotActions.Add(args.Action);
                vm.UnitLightningSlots.CollectionChanged += recordSlots;
                Click(window, content, "Carregar mais histórico", 1280, 1800);
                vm.UnitLightningSlots.CollectionChanged -= recordSlots;
                Require(slotActions.Contains(System.Collections.Specialized.NotifyCollectionChangedAction.Add) && !slotActions.Contains(System.Collections.Specialized.NotifyCollectionChangedAction.Reset), "Carregar mais deveria acrescentar vagas sem Reset (que leva a tabela ao topo): " + string.Join(",", slotActions));
                Require(vm.UnitLightningSlots.Count == 6 && vm.CanLoadMoreSlotsHistory && fixture.HistoryReads[^1].StartsWith("lightning-slots?historyBefore=", StringComparison.Ordinal) && vm.Message == "Histórico das vagas ampliado.", "Carregar mais deveria acrescentar 2 vagas encerradas: " + vm.UnitLightningSlots.Count);
                Pump(vm.RefreshAutomaticallyAsync());
                Require(vm.UnitLightningSlots.Count == 6 && vm.UnitLightningSlots.Select(item => item.Id).Distinct().Count() == 6 && vm.AwaitingSlots.Count == 1, "O ciclo deveria preservar o histórico carregado das vagas.");
                Click(window, content, "Carregar mais histórico", 1280, 1800);
                Require(vm.UnitLightningSlots.Count == 7 && !vm.CanLoadMoreSlotsHistory && vm.SlotsHistoryState == "" && vm.Message == "Todo o histórico das vagas está na lista.", "Esgotado o histórico o botão deveria sumir: " + vm.UnitLightningSlots.Count);
                Require(!Descendants<Button>(content).Any(candidate => candidate.IsVisible && candidate.Content?.ToString() == "Carregar mais histórico") && VisibleTexts(content).Any(text => text.Contains("vai primeiro a quem tem score maior", StringComparison.Ordinal)), "O botão deveria sumir e o texto da prioridade aparecer na aprovação.");
            }
            finally { CloseWindow(window); }
        });

        Check("Entrada por e-mail (P-053): a tela do OBS e a do Painel pedem E-mail, conferem o formato antes de enviar e recusam CPF sem chamar a API", () =>
        {
            foreach (var municipal in new[] { false, true })
            {
                using var fixture = new ApiFixture();
                fixture.AddUnit("UBS Entrada");
                var window = municipal ? OpenMunicipal(fixture, storageRoot, "login-email-municipal", Appearance.Light) : OpenOperational(fixture, storageRoot, "login-email-obs", Appearance.Light);
                try
                {
                    var vm = window.ViewModel.Workspace; vm.Endpoint = "https://fixture.invalid";
                    var login = Layout(window, 980, 700);
                    var texts = VisibleTexts(login);
                    Require(texts.Contains("E-mail") && !texts.Any(text => text.Contains("CPF", StringComparison.Ordinal)), "A tela de entrada deveria dizer E-mail e não falar de CPF: " + string.Join(" | ", texts.Where(text => text.Contains("CPF"))));
                    Require(Descendants<TextBox>(login).Any(input => AutomationProperties.GetName(input) == "E-mail"), "O campo deveria se chamar E-mail.");
                    var before = fixture.Requests.Count;
                    vm.Identifier = FixtureCpf; Pump(vm.LoginAsync("isolated-test-password"));
                    Require(!vm.IsAuthenticated && vm.Message == "Informe um e-mail válido para entrar (nome@dominio.com)." && fixture.Requests.Count == before, "Um CPF deveria ser recusado antes do envio: " + vm.Message);
                    vm.Identifier = "sem-arroba"; Pump(vm.LoginAsync("isolated-test-password"));
                    Require(!vm.IsAuthenticated && vm.Message.StartsWith("Informe um e-mail válido", StringComparison.Ordinal) && fixture.Requests.Count == before, "E-mail sem formato deveria ser recusado antes do envio.");
                    vm.Identifier = "  "; Pump(vm.LoginAsync("isolated-test-password"));
                    Require(vm.Message == "Informe seu e-mail e sua senha." && fixture.Requests.Count == before, "Campo vazio: " + vm.Message);
                    Require(BrazilianDataValidator.IsLoginEmail("admin@localhost") && !BrazilianDataValidator.IsValidEmail("admin@localhost") && !BrazilianDataValidator.IsLoginEmail(FixtureCpf) && !BrazilianDataValidator.IsLoginEmail("sem-arroba"), "A entrada aceita o e-mail sem ponto no domínio (como a API); o cadastro continua exigindo.");
                    // O OBS entra com e-mail de domínio completo; o Painel, com o sem ponto que o --provision e a API aceitam.
                    var identifier = municipal ? "admin@localhost" : "fixture@example.invalid";
                    vm.Identifier = identifier; Pump(vm.LoginAsync("isolated-test-password"));
                    Require(vm.IsAuthenticated && fixture.LoginIdentifier == identifier, "O e-mail válido deveria entrar: " + vm.Message);
                }
                finally { CloseWindow(window); }
            }
        });

        Check("Secretaria > Usuários (P-053): nova conta só com nome, e-mail, perfil e senha; a lista não tem coluna de CPF; o servidor não recebe CPF", () =>
        {
            using var fixture = new ApiFixture();
            fixture.AddUnit("UBS Equipe");
            var window = OpenMunicipal(fixture, storageRoot, "staff-no-cpf", Appearance.Light);
            try
            {
                var vm = LoginObs(window);
                var content = Show(window, "users", 1280, 1900);
                Require(!Descendants<TextBox>(content).Any(input => AutomationProperties.GetName(input).Contains("CPF", StringComparison.Ordinal)) && !VisibleTexts(content).Any(text => text == "CPF"), "O formulário não deveria ter campo de CPF.");
                Require(!GridNamed(content, "Contas OBS").Columns.Any(column => column.Header?.ToString()?.Contains("CPF", StringComparison.Ordinal) == true), "A lista de contas não deveria ter coluna de CPF.");
                Require(VisibleTexts(content).Any(text => text.Contains("sem CPF", StringComparison.Ordinal)), "O formulário deveria dizer que a conta é só de acesso, sem CPF.");
                Input(content, "Nome da nova conta").Text = "Paulo Equipe"; Input(content, "E-mail da nova conta").Text = "paulo@example.test";
                Secret(content, "Senha provisória da nova conta").Password = "provisoria-123"; Secret(content, "Confirmação da senha provisória da nova conta").Password = "provisoria-123";
                Click(window, content, "Criar conta", 1280, 1900);
                Require(fixture.LastStaffCreate is { Email: "paulo@example.test", Role: "attendant" } && fixture.LastStaffCreateBody is { } body && !body.Contains("cpf", StringComparison.OrdinalIgnoreCase) && vm.Staff.Any(account => account.FullName == "Paulo Equipe"), "A conta deveria ser criada sem CPF no corpo: " + fixture.LastStaffCreateBody);
                // Duplicidade só por e-mail.
                Input(content, "Nome da nova conta").Text = "Outro Nome"; Input(content, "E-mail da nova conta").Text = "paulo@example.test";
                Secret(content, "Senha provisória da nova conta").Password = "provisoria-123"; Secret(content, "Confirmação da senha provisória da nova conta").Password = "provisoria-123";
                Click(window, content, "Criar conta", 1280, 1900);
                Require(vm.MessageKind == MessageKind.Error && vm.Message == "Já existe uma conta OBS com este e-mail.", "A duplicidade deveria citar só o e-mail: " + vm.Message);
                foreach (var box in Descendants<PasswordBox>(content)) box.Clear();
            }
            finally { CloseWindow(window); }
        });
    }
}
