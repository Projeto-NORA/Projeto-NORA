using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Nora.Desktop.Models;
using Nora.Desktop.Services;
using Nora.Desktop.ViewModels;
using Nora.Desktop.Views;

namespace Nora.Desktop.Checks;

/// <summary>OBS: pedidos de família (aprovar, recusar, encerrar) e a confirmação de desativar o cadastro com a prévia da API.</summary>
internal static partial class Program
{
    private static void RunFamilyChecks(string storageRoot, string previewRoot)
    {
        Check("OBS Família: lista pedidos pendentes e vínculos aprovados, aprova (lembrando dos documentos), recusa com ou sem motivo e encerra, sempre com confirmação e tratando decisão de outra pessoa", () =>
        {
            using var fixture = new ApiFixture { UseWorkspaceIdentity = true, ObsRole = "attendant" };
            var unit = fixture.AddUnit("UBS Família", "Centro");
            var lia = fixture.AddRegistry(unit.Id, "Lia Santos", "52998224725"); var davi = fixture.AddRegistry(unit.Id, "Davi Souza", "11144477735");
            var ines = fixture.AddRegistry(unit.Id, "Inês Costa", "12345678909", active: false); var caio = fixture.AddRegistry(unit.Id, "Caio Lima", "98765432100");
            var now = DateTimeOffset.UtcNow;
            var second = fixture.AddFamilyRequest(unit.Id, "João Souza", "98765432100", davi.Cpf, requestedAt: now.AddDays(-1));
            var first = fixture.AddFamilyRequest(unit.Id, "Maria Santos", "12345678909", lia.Cpf, requestedAt: now.AddDays(-2));
            var inactiveRequest = fixture.AddFamilyRequest(unit.Id, "Rosa Costa", "11144477735", ines.Cpf, requestedAt: now.AddHours(-3));
            var approvedLink = fixture.AddFamilyRequest(unit.Id, "Carla Lima", "52998224725", caio.Cpf, "approved", now.AddDays(-9));
            fixture.FamilyLinks[fixture.FamilyLinks.FindIndex(entry => entry.Link.Id == approvedLink.Id)] = (unit.Id, approvedLink with { DecidedAt = now.AddDays(-8), DecidedByName = "Operador Fixture" });
            var asked = new List<(string Title, string Text)>(); var answer = false;
            Confirmation.Handler = (_, title, text) => { asked.Add((title, text)); return answer; };
            var window = OpenOperational(fixture, storageRoot, "family", Appearance.Light);
            try
            {
                var vm = LoginObs(window);
                Require(fixture.Count("GET", "/family-links") == 0, "Os pedidos de família não deveriam ser lidos antes de abrir a aba.");
                var content = Show(window, "family", 1280, 2200);
                Require(fixture.Count("GET", "/family-links") == 2 && vm.FamilyPending.Select(link => link.GuardianName).SequenceEqual(["Maria Santos", "João Souza", "Rosa Costa"]) && vm.FamilyApproved.Single().GuardianName == "Carla Lima", "Abrir a aba deveria ler pendentes e aprovados (duas chamadas), do pedido mais antigo ao mais novo.");
                Require(vm.FamilySummary == "3 pedidos aguardando · 1 vínculo aprovado" && FullTexts(content).Contains(vm.FamilySummary), "O resumo da aba deveria contar pedidos e vínculos: " + vm.FamilySummary);
                Require(Descendants<DataGrid>(content).Any(grid => AutomationProperties.GetName(grid) == "Pedidos de família pendentes" && grid.Items.Count == 3) && Descendants<DataGrid>(content).Any(grid => AutomationProperties.GetName(grid) == "Vínculos de família aprovados" && grid.Items.Count == 1), "As duas tabelas deveriam estar na tela.");
                Require(vm.FamilyPending[0].GuardianCpfDisplay == "123.456.789-09" && vm.FamilyPending[0].DependentDisplay == "Lia Santos" && vm.FamilyPending[0].DependentCpfDisplay == "529.982.247-25", "A equipe deveria ver nome e CPF do responsável e do dependente para conferir os documentos.");
                SaveScrolled(window, Path.Combine(previewRoot, "obs-familia-light.png"), 1280, 900);
                // Aprovar: pergunta lembrando de conferir os documentos; recusar a pergunta não aprova.
                vm.SelectedPendingFamily = vm.FamilyPending[0]; content = Layout(window, 1280, 2200);
                Click(window, content, "Aprovar pedido", 1280, 2200);
                Require(asked.Count == 1 && asked[0].Title == "Aprovar vínculo de família" && asked[0].Text.Contains("Confira antes os documentos", StringComparison.Ordinal) && asked[0].Text.Contains("Maria Santos", StringComparison.Ordinal) && fixture.Count("POST", "/approve") == 0, "A aprovação deveria perguntar, lembrando dos documentos, e recusar não aprova.");
                answer = true; vm.SelectedPendingFamily = vm.FamilyPending[0]; content = Layout(window, 1280, 2200); Click(window, content, "Aprovar pedido", 1280, 2200);
                Require(fixture.Count("POST", "/approve") == 1 && fixture.FamilyBodies.Last().Body.GetProperty("confirmed").GetBoolean() && vm.FamilyPending.Count == 2 && vm.FamilyApproved.Count == 2 && vm.FamilyApproved.Any(link => link.GuardianName == "Maria Santos") && vm.MessageKind == MessageKind.Success && vm.Message.Contains("Maria Santos"), "Aprovar deveria enviar a confirmação e mover o pedido para os vínculos: " + vm.Message);
                // Recusar: motivo opcional de até 280 caracteres.
                vm.SelectedPendingFamily = vm.FamilyPending.Single(link => link.GuardianName == "João Souza"); vm.FamilyRejectReason = new string('m', 281); Pump(vm.RejectFamilyAsync());
                Require(vm.FamilyRejectReasonError.Contains("280") && fixture.Count("POST", "/reject") == 0, "Motivo com mais de 280 caracteres foi enviado.");
                vm.FamilyRejectReason = "Documento ilegível"; content = Layout(window, 1280, 2200); asked.Clear(); Click(window, content, "Recusar pedido", 1280, 2200);
                Require(asked.Count == 1 && asked[0].Title == "Recusar pedido de família" && asked[0].Text.Contains("motivo informado") && fixture.Count("POST", "/reject") == 1 && fixture.FamilyBodies.Last().Body.GetProperty("reason").GetString() == "Documento ilegível" && vm.FamilyRejectReason == "" && vm.FamilyPending.All(link => link.GuardianName != "João Souza"), "Recusar com motivo deveria pedir confirmação e enviar o motivo.");
                vm.SelectedPendingFamily = vm.FamilyPending.Single(); asked.Clear(); content = Layout(window, 1280, 2200); answer = false; Click(window, content, "Recusar pedido", 1280, 2200);
                Require(asked.Count == 1 && asked[0].Text.Contains("Nenhum motivo") && fixture.Count("POST", "/reject") == 1, "Recusar sem motivo deveria avisar que nenhum motivo será informado e respeitar a recusa da pergunta.");
                // Dependente com cadastro desativado: 409 com mensagem própria; o pedido continua pendente.
                answer = true; vm.SelectedPendingFamily = vm.FamilyPending.Single(); Pump(vm.ApproveFamilyAsync());
                Require(vm.MessageKind == MessageKind.Error && vm.Message.Contains("desativado", StringComparison.Ordinal) && vm.FamilyPending.Count == 1 && fixture.FamilyLinkOf(inactiveRequest.Id)!.Status == "pending", "Aprovar com o cadastro do dependente desativado deveria dar a mensagem própria e manter o pedido: " + vm.Message);
                // Outra pessoa já decidiu: 409, mensagem própria e a lista volta a refletir o servidor.
                var pendingIndex = fixture.FamilyLinks.FindIndex(entry => entry.Link.Id == inactiveRequest.Id);
                fixture.FamilyLinks[pendingIndex] = (unit.Id, fixture.FamilyLinks[pendingIndex].Link with { Status = "rejected" });
                vm.SelectedPendingFamily = vm.FamilyPending.Single(); Pump(vm.ApproveFamilyAsync());
                Require(vm.Message.Contains("já foi decidido", StringComparison.Ordinal) && vm.FamilyPending.Count == 0 && vm.FamilyPendingState.StartsWith("Nenhum pedido", StringComparison.Ordinal), "A decisão de outra pessoa deveria avisar e esvaziar a lista: " + vm.Message);
                // Encerrar vínculo aprovado, com confirmação.
                vm.SelectedApprovedFamily = vm.FamilyApproved.Single(link => link.GuardianName == "Maria Santos"); content = Layout(window, 1280, 2200); answer = false; asked.Clear();
                Click(window, content, "Encerrar vínculo selecionado", 1280, 2200);
                Require(asked.Count == 1 && asked[0].Title == "Encerrar vínculo de família" && asked[0].Text.Contains("perde na hora", StringComparison.Ordinal) && fixture.Count("POST", "/end") == 0, "Encerrar deveria perguntar e respeitar a recusa.");
                answer = true; vm.SelectedApprovedFamily = vm.FamilyApproved.Single(link => link.GuardianName == "Maria Santos"); content = Layout(window, 1280, 2200); Click(window, content, "Encerrar vínculo selecionado", 1280, 2200);
                Require(fixture.Count("POST", "/end") == 1 && vm.FamilyApproved.Single().GuardianName == "Carla Lima" && vm.Message.Contains("Vínculo encerrado", StringComparison.Ordinal), "Encerrar deveria tirar o vínculo da lista.");
                // Ciclo: só a aba Família lê os pedidos, uma vez por tabela.
                var reads = fixture.Count("GET", "/family-links"); Pump(vm.RefreshAutomaticallyAsync());
                Require(fixture.Count("GET", "/family-links") == reads + 2, "Na aba Família cada ciclo deveria ler pendentes e aprovados (duas chamadas).");
                window.ViewModel.Navigate("queue"); Settle(vm); reads = fixture.Count("GET", "/family-links"); Pump(vm.RefreshAutomaticallyAsync());
                Require(fixture.Count("GET", "/family-links") == reads, "Fora da aba Família o ciclo não deveria ler os pedidos.");
                window.ViewModel.Navigate("family"); Settle(vm); window.ViewModel.SetAppearance(Appearance.Dark); content = Layout(window, 1280, 2200);
                SaveScrolled(window, Path.Combine(previewRoot, "obs-familia-dark.png"), 1280, 900);
                vm.LogoutCommand.Execute(null); Settle(vm);
                Require(vm.FamilyPending.Count == 0 && vm.FamilyApproved.Count == 0, "Sair deveria limpar os pedidos de família.");
            }
            finally { Confirmation.Handler = null; CloseWindow(window); }
        });
    }

    private static void RunDeactivationChecks(string storageRoot, string previewRoot)
    {
        Check("OBS Desativação: a confirmação mostra quantas consultas futuras serão canceladas (prévia da API) e que não pesam na participação; sem a prévia não há confirmação", () =>
        {
            using var fixture = new ApiFixture { UseWorkspaceIdentity = true };
            var unit = fixture.AddUnit("UBS Desativar", "Centro");
            var ana = fixture.AddRegistry(unit.Id, "Ana Souza", "52998224725"); var bia = fixture.AddRegistry(unit.Id, "Bia Lima", "11144477735");
            var tomorrow = new DateTimeOffset(DateTime.Today.AddDays(1).AddHours(9));
            fixture.AddAppointment(unit.Id, "scheduled", tomorrow, ana.Cpf, ana.FullName); fixture.AddAppointment(unit.Id, "called", tomorrow.AddDays(2), ana.Cpf, ana.FullName);
            fixture.AddAppointment(unit.Id, "cancelled", tomorrow.AddDays(3), ana.Cpf, ana.FullName); fixture.AddAppointment(unit.Id, "completed", tomorrow.AddDays(-30), ana.Cpf, ana.FullName);
            fixture.AddWaiting(unit.Id, ana.Cpf, ana.FullName, "Clínica", DateTimeOffset.UtcNow.AddHours(-2));
            var asked = new List<(string Title, string Text)>(); var answer = false;
            Confirmation.Handler = (_, title, text) => { asked.Add((title, text)); return answer; };
            var window = OpenOperational(fixture, storageRoot, "deactivation", Appearance.Light);
            try
            {
                var vm = LoginObs(window);
                window.ViewModel.Navigate("patients"); Settle(vm); var content = Layout(window, 1280, 1400);
                vm.SelectedObsRegistry = vm.ObsRegistry.Single(item => item.Cpf == ana.Cpf); content = Layout(window, 1280, 1400);
                InvokeButton(ButtonIn(content, "Desativar nesta UBS")); PumpUntil(() => asked.Count == 1 && !vm.IsBusy);
                Require(fixture.Count("POST", "/registry/deactivation-preview") == 1 && fixture.Count("PATCH", "/registry/active") == 0 && fixture.Requests.All(request => !request.Contains(ana.Cpf, StringComparison.Ordinal)), "A confirmação deveria vir depois de uma só prévia (com o CPF no corpo) e antes de qualquer alteração.");
                var text = asked[0].Text;
                Require(asked[0].Title == "Confirmar situação na UBS" && text.Contains("2 consultas futuras serão canceladas") && text.Contains("não pesam na participação do paciente") && text.Contains("1 entrada ativa na fila será encerrada") && text.Contains("Ana Souza"), "A confirmação deveria dizer 2 consultas futuras, que não pesam na participação e a fila: " + text);
                Require(vm.ObsRegistry.Single(item => item.Cpf == ana.Cpf).IsActive && fixture.AppointmentsOf(unit.Id).Count(item => item.Status == "cancelled") == 1, "Recusar a confirmação não deveria desativar nem cancelar.");
                answer = true; asked.Clear(); InvokeButton(ButtonIn(content, "Desativar nesta UBS")); PumpUntil(() => asked.Count == 1 && !vm.IsBusy); Settle(vm);
                Require(fixture.Count("POST", "/registry/deactivation-preview") == 2 && fixture.Count("PATCH", "/registry/active") == 1 && !vm.ObsRegistry.Single(item => item.Cpf == ana.Cpf).IsActive, "Confirmar deveria desativar o cadastro.");
                Require(fixture.AppointmentsOf(unit.Id).Count(item => item.Status == "cancelled") == 3 && fixture.AppointmentsOf(unit.Id).Count(item => item.Status == "completed") == 1 && vm.Message.Contains("2 consultas futuras foram canceladas", StringComparison.Ordinal) && vm.Message.Contains("não pesam na participação", StringComparison.Ordinal), "As duas consultas futuras deveriam ficar canceladas (a concluída intacta) e a faixa dizer quantas: " + vm.Message);
                // Reativar: sem prévia e com a pergunta curta.
                asked.Clear(); vm.SelectedObsRegistry = vm.ObsRegistry.Single(item => item.Cpf == ana.Cpf); content = Layout(window, 1280, 1400);
                InvokeButton(ButtonIn(content, "Reativar nesta UBS")); PumpUntil(() => asked.Count == 1 && !vm.IsBusy); Settle(vm);
                Require(fixture.Count("POST", "/registry/deactivation-preview") == 2 && asked[0].Text.StartsWith("Reativar Ana Souza") && vm.ObsRegistry.Single(item => item.Cpf == ana.Cpf).IsActive, "Reativar não deveria pedir prévia e deveria reativar.");
                // Sem consultas futuras: a frase muda e o aviso de participação não aparece.
                asked.Clear(); vm.SelectedObsRegistry = vm.ObsRegistry.Single(item => item.Cpf == bia.Cpf); content = Layout(window, 1280, 1400); answer = false;
                InvokeButton(ButtonIn(content, "Desativar nesta UBS")); PumpUntil(() => asked.Count == 1 && !vm.IsBusy);
                Require(asked[0].Text.Contains("não tem consultas futuras") && !asked[0].Text.Contains("pesam") && asked[0].Text.Contains("Ele não tem entrada ativa na fila"), "Sem consultas nem fila a confirmação deveria dizer isso: " + asked[0].Text);
                // Prévia indisponível: nada de confirmação e nada de alteração.
                fixture.FailDeactivationPreview = true; asked.Clear(); var patches = fixture.Count("PATCH", "/registry/active");
                InvokeButton(ButtonIn(content, "Desativar nesta UBS")); PumpUntil(() => !vm.IsBusy); Settle(vm);
                Require(asked.Count == 0 && fixture.Count("PATCH", "/registry/active") == patches && vm.MessageKind == MessageKind.Error && vm.ObsRegistry.Single(item => item.Cpf == bia.Cpf).IsActive, "Sem a prévia a desativação não deveria ser confirmada nem feita: " + vm.Message);
                fixture.FailDeactivationPreview = false;
                // Aparência da confirmação: o texto cabe em uma linha de aviso legível (a caixa é a do Windows; conferimos o texto).
                Require(DeactivationTextLines(WorkspaceViewModel.DeactivationConfirmationText("Ana Souza", new(1, 0))).Contains("1 consulta futura será cancelada, e o horário volta a ficar livre."), "O singular deveria ser 1 consulta futura será cancelada.");
            }
            finally { Confirmation.Handler = null; CloseWindow(window); }
        });
    }

    private static string[] DeactivationTextLines(string text) => text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
}
