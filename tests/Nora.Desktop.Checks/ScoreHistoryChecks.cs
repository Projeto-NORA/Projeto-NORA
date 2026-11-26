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
            var pager = new HistoryPager<WaitingSeed, long>(entry => entry.Id, entry => entry.IsActive);
            WaitingSeed Item(long id, bool active = false) => new(id, FixtureCpf, "Paciente " + id, "Cardiologia", DateTimeOffset.UtcNow, active ? "waiting" : "completed");
            long[] Ids(IEnumerable<WaitingSeed> items) => items.Select(item => item.Id).Order().ToArray();
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
            var closing = new HistoryPager<WaitingSeed, long>(entry => entry.Id, entry => entry.IsActive, entry => entry with { Status = "closed" });
            closing.ApplyPage([Item(2, true), Item(10), Item(9)], "c9", out restarted);
            closing.ApplyOlder([Item(8), Item(7)], "c7");
            shown = closing.ApplyPage([Item(11), Item(10), Item(9)], "c9", out restarted);
            Require(!restarted && Ids(shown).SequenceEqual([2, 7, 8, 9, 10, 11]) && shown.Single(item => item.Id == 2) is { IsActive: false, Status: "closed", StatusDisplay: "Encerrada" } && closing.Cursor == "c7", "A entrada ativa encerrada não pode sumir da lista: " + string.Join(",", Ids(shown)));
            shown = closing.ApplyPage([Item(11), Item(10), Item(9)], "c9", out restarted);
            Require(Ids(shown).SequenceEqual([2, 7, 8, 9, 10, 11]) && shown.Count(item => item.Id == 2) == 1, "O ciclo seguinte deveria manter a entrada encerrada, sem duplicar.");
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

        Check("Secretaria > Usuários (P-053): nova conta só com nome, e-mail, perfil e senha; a lista não mostra CPF; o servidor não recebe CPF", () =>
        {
            using var fixture = new ApiFixture();
            fixture.AddUnit("UBS Equipe");
            var window = OpenMunicipal(fixture, storageRoot, "staff-no-cpf", Appearance.Light);
            try
            {
                var vm = LoginObs(window);
                var content = Show(window, "users:accounts", 1280, 900);
                Require(!VisibleTexts(content).Any(text => text == "CPF" || text.Contains("CPF", StringComparison.Ordinal)), "A lista de contas não deveria mostrar CPF.");
                InspectHeaderForm(window, "Nova conta", dialog =>
                {
                    Require(!Descendants<TextBox>(dialog).Any(input => AutomationProperties.GetName(input).Contains("CPF", StringComparison.Ordinal)) && !VisibleTexts(dialog).Any(text => text == "CPF"), "O formulário não deveria ter campo de CPF.");
                    var primary = Descendants<Button>(dialog).Single(button => button.Content as string == "Criar conta");
                    Input(dialog, "Nome da nova conta").Text = "Paulo Equipe"; Input(dialog, "E-mail da nova conta").Text = "paulo@example.test";
                    Secret(dialog, "Senha provisória da nova conta").Password = "provisoria-123"; Secret(dialog, "Confirmação da senha provisória da nova conta").Password = "provisoria-123";
                    InvokeButton(primary); PumpUntil(() => !dialog.IsVisible);
                    Require(dialog.Saved && fixture.LastStaffCreate is { Email: "paulo@example.test", Role: "attendant" } && fixture.LastStaffCreateBody is { } body && !body.Contains("cpf", StringComparison.OrdinalIgnoreCase) && vm.Staff.Any(account => account.FullName == "Paulo Equipe"), "A conta deveria ser criada sem CPF no corpo: " + fixture.LastStaffCreateBody);
                });
                // Duplicidade só por e-mail: o erro aparece dentro do pop-up.
                InspectHeaderForm(window, "Nova conta", dialog =>
                {
                    Input(dialog, "Nome da nova conta").Text = "Outro Nome"; Input(dialog, "E-mail da nova conta").Text = "paulo@example.test";
                    Secret(dialog, "Senha provisória da nova conta").Password = "provisoria-123"; Secret(dialog, "Confirmação da senha provisória da nova conta").Password = "provisoria-123";
                    InvokeButton(Descendants<Button>(dialog).Single(button => button.Content as string == "Criar conta")); PumpUntil(() => !dialog.IsBusy);
                    Require(dialog.IsVisible && vm.MessageKind == MessageKind.Error && vm.Message == "Já existe uma conta OBS com este e-mail." && dialog.ErrorMessage == vm.Message, "A duplicidade deveria citar só o e-mail: " + vm.Message);
                    foreach (var box in Descendants<PasswordBox>(dialog)) box.Clear();
                });
            }
            finally { CloseWindow(window); }
        });
    }
}
