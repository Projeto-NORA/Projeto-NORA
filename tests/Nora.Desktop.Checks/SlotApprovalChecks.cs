using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Nora.Desktop.Models;
using Nora.Desktop.Services;
using Nora.Desktop.ViewModels;
using Nora.Desktop.Views;

namespace Nora.Desktop.Checks;

/// <summary>Aprovação clínica opcional das vagas reaproveitadas: a chave do administrador, a lista aguardando aprovação com a origem, os cartões da Secretaria e o CSV.</summary>
internal static partial class Program
{
    private static void RunSlotApprovalChecks(string storageRoot, string[] args)
    {
        var previewRoot = PreviewRoot(args);
        Check("Aprovação das vagas: o administrador liga e desliga com confirmação; a atendente vê o estado em leitura", () =>
        {
            using var fixture = new ApiFixture { UseWorkspaceIdentity = true };
            var unit = fixture.AddUnit("UBS Aprovação", "Centro");
            var window = OpenOperational(fixture, storageRoot, "approval-switch", Appearance.Light);
            var asked = new List<string>(); var answer = false; Confirmation.Handler = (_, _, text) => { asked.Add(text); return answer; };
            try
            {
                var vm = LoginObs(window);
                var content = Show(window, "unit-settings", 1280, 5200);
                Require(!vm.SlotApprovalEnabled && vm.SlotApprovalToggleLabel == "Ligar aprovação das vagas" && vm.SlotApprovalState.StartsWith("Desligada.", StringComparison.Ordinal) && vm.CanToggleSlotApproval, "Por padrão a aprovação vem desligada e o administrador pode ligá-la: " + vm.SlotApprovalState);
                Require(VisibleTexts(content).Contains("Desligada"), "O selo deveria dizer Desligada com a aprovação desligada.");
                Click(window, content, "Ligar aprovação das vagas", 1280, 5200);
                Require(asked.Count == 1 && asked[0].Contains("sem aprovação até 1 h antes da consulta, ela fecha", StringComparison.Ordinal) && fixture.Count("PUT", "/slot-approval") == 0, "Sem confirmar nada pode ser enviado.");
                answer = true;
                Click(window, content, "Ligar aprovação das vagas", 1280, 5200);
                Require(fixture.Count("PUT", "/slot-approval") == 1 && vm.SlotApprovalEnabled && vm.SlotApprovalToggleLabel == "Desligar aprovação das vagas" && vm.SlotApprovalState.StartsWith("Ligada desde ", StringComparison.Ordinal) && vm.SlotApprovalSteps.Single() is { Text: "Aprovação das vagas salva", IsDone: true } && vm.ProfileSteps.Count == 0 && vm.MessageKind == MessageKind.Success, "Ligar deveria gravar e mostrar desde quando: " + vm.SlotApprovalState);
                content = Layout(window, 1280, 5200);
                Require(VisibleTexts(content).Contains("Ligada") && !VisibleTexts(content).Contains("Desligada"), "O selo deveria dizer Ligada com a aprovação ligada.");
                SaveScrolled(window, Path.Combine(previewRoot, "obs-aprovacao-ligada-light.png"), 1280, 900);
                window.ViewModel.SetAppearance(Appearance.Dark); Layout(window, 1280, 5200); SaveScrolled(window, Path.Combine(previewRoot, "obs-aprovacao-ligada-dark.png"), 1280, 900); window.ViewModel.SetAppearance(Appearance.Light);
                content = Layout(window, 1280, 5200);
                Click(window, content, "Desligar aprovação das vagas", 1280, 5200);
                Require(asked[^1].Contains("continuam aprováveis até o prazo", StringComparison.Ordinal) && !vm.SlotApprovalEnabled && fixture.ProfileOf(unit.Id)!.SlotApprovalRequired == false, "Desligar deveria pedir confirmação e gravar.");
            }
            finally { Confirmation.Handler = null; CloseWindow(window); }
            using var attendantFixture = new ApiFixture { UseWorkspaceIdentity = true, ObsRole = "attendant" };
            attendantFixture.AddUnit("UBS Aprovação", "Centro");
            var attendantWindow = OpenOperational(attendantFixture, storageRoot, "approval-switch-attendant", Appearance.Light);
            try
            {
                var vm = LoginObs(attendantWindow);
                var content = Show(attendantWindow, "unit-settings", 1280, 5200);
                Require(!vm.CanToggleSlotApproval && !ButtonIn(content, "Ligar aprovação das vagas").IsEnabled && FullTexts(content).Contains(vm.SlotApprovalState), "A atendente vê o estado, mas não liga a chave.");
            }
            finally { CloseWindow(attendantWindow); }
        });

        Check("Aprovação das vagas: a lista aguardando mostra a origem e o prazo; aprovar e descartar pedem confirmação; prazo vencido e horário ocupado fecham a vaga com a mensagem certa", () =>
        {
            using var fixture = new ApiFixture { UseWorkspaceIdentity = true };
            var unit = fixture.AddUnit("UBS Vagas", "Centro");
            fixture.AddSpecialty("Cardiologia");
            var day = DateTime.Today.AddDays(3);
            var forApproval = fixture.AddAwaitingSlot(unit.Id, "Cardiologia", "Dra. Ana Lima", LocalAt(day, 10), "Maria Souza");
            var forDiscard = fixture.AddAwaitingSlot(unit.Id, "Cardiologia", "Dra. Ana Lima", LocalAt(day, 11), "João Pereira", cancelledBy: "staff");
            var expired = fixture.AddAwaitingSlot(unit.Id, "Cardiologia", "Dr. Bruno", LocalAt(day, 14), "Ana Costa", deadline: DateTimeOffset.UtcNow.AddMinutes(-5));
            var occupied = fixture.AddAwaitingSlot(unit.Id, "Cardiologia", "Dr. Bruno", LocalAt(day, 15), "Pedro Lima");
            fixture.AddSlot(unit.Id, new UnitLightningSlot(Guid.NewGuid(), Guid.NewGuid(), "Cardiologia", "Dr. Carlos", LocalAt(day, 9), LocalAt(day, 9).AddMinutes(30), "open", DateTimeOffset.UtcNow, "staff"));
            var window = OpenOperational(fixture, storageRoot, "approval-list", Appearance.Light);
            var asked = new List<(string Title, string Text)>(); Confirmation.Handler = (_, title, text) => { asked.Add((title, text)); return true; };
            try
            {
                var vm = LoginObs(window);
                var content = Show(window, "slots", 1280, 3200);
                Require(vm.ShowAwaitingSlots && vm.AwaitingSlotsTitle == "Aguardando aprovação (4)" && FullTexts(content).Contains("Aguardando aprovação (4)"), "A seção deveria aparecer com a contagem, mesmo com a chave desligada (há vagas aguardando).");
                var grid = GridNamed(content, "Vagas aguardando aprovação da equipe");
                Require(grid.Items.Count == 4 && new[] { "Especialidade", "Profissional", "Horário", "Origem", "Aprovar até" }.SequenceEqual(grid.Columns.Select(column => column.Header?.ToString() ?? "")), "A lista deveria mostrar especialidade, profissional, horário, origem e prazo.");
                Require(vm.AwaitingSlots.Single(item => item.Id == forApproval.Id).SourceDisplay.StartsWith("Veio da consulta de Maria Souza, cancelada em ", StringComparison.Ordinal) && vm.AwaitingSlots.Single(item => item.Id == forApproval.Id).SourceDisplay.EndsWith(" pelo paciente", StringComparison.Ordinal) && vm.AwaitingSlots.Single(item => item.Id == forDiscard.Id).SourceDisplay.EndsWith(" pela equipe", StringComparison.Ordinal), "A origem deveria dizer quem cancelou: " + vm.AwaitingSlots.Single(item => item.Id == forApproval.Id).SourceDisplay);
                Require(System.Text.RegularExpressions.Regex.IsMatch(vm.AwaitingSlots.Single(item => item.Id == forApproval.Id).ApprovalDeadlineDisplay, @"^\d{2}/\d{2} \d{2}:\d{2}$") && vm.AwaitingSlots.Single(item => item.Id == forApproval.Id).StatusDisplay == "Aguardando aprovação" && vm.AwaitingSlots.Single(item => item.Id == forApproval.Id).OriginDisplay == "Cancelamento", "O prazo e a situação deveriam estar escritos.");
                Require(!vm.CanDecideSlotApproval && !ButtonIn(content, "Aprovar e ofertar").IsEnabled && !ButtonIn(content, "Descartar").IsEnabled, "Sem vaga selecionada os dois botões ficam desabilitados.");
                SaveScrolled(window, Path.Combine(previewRoot, "obs-vagas-aguardando-light.png"), 1280, 900);
                window.ViewModel.SetAppearance(Appearance.Dark); Layout(window, 1280, 3200); SaveScrolled(window, Path.Combine(previewRoot, "obs-vagas-aguardando-dark.png"), 1280, 900); window.ViewModel.SetAppearance(Appearance.Light);
                content = Layout(window, 1280, 3200);
                // Aprovar.
                vm.SelectedAwaitingSlot = vm.AwaitingSlots.Single(item => item.Id == forApproval.Id); content = Layout(window, 1280, 3200);
                Click(window, content, "Aprovar e ofertar", 1280, 3200);
                Require(asked.Last().Title == "Aprovar e ofertar vaga" && asked.Last().Text.Contains("1º da fila", StringComparison.Ordinal) && vm.UnitLightningSlots.Single(item => item.Id == forApproval.Id).Status == "open" && vm.AwaitingSlots.All(item => item.Id != forApproval.Id) && vm.SlotSteps.Single() is { Text: "Vaga aprovada", IsDone: true } && vm.MessageKind == MessageKind.Success, "Aprovar deveria pedir confirmação, abrir a vaga e concluir o passo: " + vm.Message);
                // Descartar.
                vm.SelectedAwaitingSlot = vm.AwaitingSlots.Single(item => item.Id == forDiscard.Id); content = Layout(window, 1280, 3200);
                Click(window, content, "Descartar", 1280, 3200);
                Require(asked.Last().Title == "Descartar vaga" && vm.UnitLightningSlots.Single(item => item.Id == forDiscard.Id).StatusDisplay == "Descartada" && vm.SlotSteps.Single().Text == "Vaga descartada" && vm.SlotSteps.Single().IsDone, "Descartar deveria pedir confirmação e fechar a vaga como descartada.");
                // Prazo vencido: 409 approval-expired, a vaga fecha e a lista é relida.
                vm.SelectedAwaitingSlot = vm.AwaitingSlots.Single(item => item.Id == expired.Id); content = Layout(window, 1280, 3200);
                Click(window, content, "Aprovar e ofertar", 1280, 3200);
                Require(vm.Message == "Falta menos de 1 hora para a consulta; a vaga foi fechada sem ser ofertada." && vm.MessageKind == MessageKind.Error && vm.SlotSteps.Single().IsFailed && vm.UnitLightningSlots.Single(item => item.Id == expired.Id).StatusDisplay == "Fechada sem aprovação" && vm.AwaitingSlots.All(item => item.Id != expired.Id), "approval-expired deveria fechar a vaga com a mensagem certa: " + vm.Message);
                // Horário ocupado no meio do caminho: slot-unavailable na aprovação.
                fixture.NextApprovalConflict = "slot-unavailable";
                vm.SelectedAwaitingSlot = vm.AwaitingSlots.Single(item => item.Id == occupied.Id); content = Layout(window, 1280, 3200);
                Click(window, content, "Aprovar e ofertar", 1280, 3200);
                Require(vm.Message == "O horário já foi ocupado; a vaga foi fechada." && vm.UnitLightningSlots.Single(item => item.Id == occupied.Id).StatusDisplay == "Fechada: horário ocupado" && vm.AwaitingSlots.Count == 0, "slot-unavailable na aprovação deveria dizer que a vaga foi fechada: " + vm.Message);
                Require(vm.AwaitingSlotsState == "Nenhuma vaga aguardando aprovação." && !vm.HasAwaitingSlots, "Sem vagas aguardando o estado vazio deveria aparecer: " + vm.AwaitingSlotsState);
                // Lista geral: coluna Origem e as situações novas.
                var general = GridNamed(content, "Vagas da UBS");
                Require(general.Columns.Any(column => column.Header?.ToString() == "Origem") && vm.UnitLightningSlots.Select(item => item.OriginDisplay).Distinct().Order().SequenceEqual(["Cancelamento", "Gerada pela equipe"]), "A coluna Origem deveria mostrar Cancelamento e Gerada pela equipe.");
                new[] { "Fechada sem aprovação", "Descartada", "Fechada: horário ocupado" }.ToList().ForEach(status => Require(vm.UnitLightningSlots.Any(item => item.StatusDisplay == status), "Situação ausente da lista: " + status));
                SaveScrolled(window, Path.Combine(previewRoot, "obs-vagas-situacoes-light.png"), 1280, 900);
                // Ficha da vaga mostra a consulta de origem.
                var details = vm.BuildDetails(vm.UnitLightningSlots.Single(item => item.Id == forApproval.Id)) ?? throw new InvalidOperationException("Sem ficha da vaga.");
                Require(details.Fields.Any(field => field.Label == "Origem" && field.Value == "Cancelamento") && details.Fields.Any(field => field.Label == "Consulta de origem" && field.Value.StartsWith("Veio da consulta de Maria Souza", StringComparison.Ordinal)), "A ficha da vaga deveria dizer de qual consulta ela veio.");
            }
            finally { Confirmation.Handler = null; CloseWindow(window); }
        });

        Check("Aprovação das vagas: a seção só aparece com a chave ligada ou vaga aguardando; o Painel avisa quando há vagas aguardando", () =>
        {
            using var fixture = new ApiFixture { UseWorkspaceIdentity = true };
            var unit = fixture.AddUnit("UBS Painel", "Centro");
            var window = OpenOperational(fixture, storageRoot, "approval-panel", Appearance.Light);
            try
            {
                var vm = LoginObs(window);
                var content = Show(window, "slots", 1280, 3200);
                Require(!vm.ShowAwaitingSlots && !Descendants<Border>(content).Any(border => AutomationProperties.GetName(border) == "Vagas aguardando aprovação" && border.IsVisible), "Com a chave desligada e sem vagas aguardando a seção some.");
                content = Show(window, "home", 1280, 3600);
                Require(!Descendants<Border>(content).Any(border => AutomationProperties.GetName(border) == "Vagas aguardando aprovação" && border.IsVisible), "O Painel não mostra o cartão sem vagas aguardando.");
                fixture.AddAwaitingSlot(unit.Id, "Cardiologia", "Dra. Ana Lima", LocalAt(DateTime.Today.AddDays(3), 10));
                Pump(vm.LoadUnitLightningSlotsAsync());
                content = Show(window, "home", 1280, 3600);
                Require(vm.HasAwaitingSlots && vm.PanelAwaitingText == "1 vaga aguardando aprovação da equipe" && Descendants<Border>(content).Any(border => AutomationProperties.GetName(border) == "Vagas aguardando aprovação" && border.IsVisible), "O Painel deveria avisar que há 1 vaga aguardando aprovação.");
                SaveScrolled(window, Path.Combine(previewRoot, "obs-painel-vagas-aguardando-light.png"), 1280, 900);
                content = Show(window, "slots", 1280, 3200);
                Require(vm.ShowAwaitingSlots && Descendants<Border>(content).Any(border => AutomationProperties.GetName(border) == "Vagas aguardando aprovação" && border.IsVisible), "Com uma vaga aguardando a seção aparece mesmo com a chave desligada.");
            }
            finally { CloseWindow(window); }
        });

        Check("Secretaria: Vaga Relâmpago mostra Aprovadas pela equipe e Fechadas sem aprovação (e a frase nova do acompanhamento), com Indisponível para API antiga, e o CSV traz as quatro linhas", () =>
        {
            using var fixture = new ApiFixture { UseWorkspaceIdentity = true };
            fixture.AddUnit("UBS Alfa", "Centro");
            var window = OpenMunicipal(fixture, storageRoot, "approval-municipal", Appearance.Light);
            try
            {
                var vm = LoginObs(window);
                var content = Show(window, "slots", 1280, 3000);
                var legacy = FullTexts(content);
                Require(legacy.Contains("Aprovadas pela equipe") && legacy.Contains("Fechadas sem aprovação") && !legacy.Contains("Confirmadas") && vm.ApprovedSlotsCount == "Indisponível" && vm.ApprovalTimeoutSlotsCount == "Indisponível", "Com a API antiga os cartões novos mostram Indisponível, nunca zero.");
                Require(legacy.Any(text => text.Contains("não confirmam o atendimento. Ofertas criadas não comprovam entrega de notificações. A taxa de reaproveitamento ainda não é calculada.", StringComparison.Ordinal)) && !legacy.Any(text => text.Contains("Confirmação clínica", StringComparison.Ordinal)), "O quadro de acompanhamento deveria usar a frase nova.");
                Require(vm.CreateSummaryCsv().Contains("Vagas aprovadas pela equipe;Indisponível;", StringComparison.Ordinal) && vm.CreateSummaryCsv().Contains("Vagas aguardando aprovação;Indisponível;", StringComparison.Ordinal), "O CSV com a API antiga deveria escrever Indisponível.");
                fixture.ApprovalCounters = (2, 5, 1, 3);
                vm.RefreshCommand.Execute(null); Settle(vm);
                content = Show(window, "slots", 1280, 3000);
                var texts = FullTexts(content);
                Require(vm.ApprovedSlotsCount == "5" && vm.ApprovalTimeoutSlotsCount == "1" && vm.AwaitingApprovalSlotsCount == "2" && vm.DiscardedSlotsCount == "3" && texts.Contains("5") && texts.Contains("1"), "Os cartões deveriam mostrar os números da API: aprovadas " + vm.ApprovedSlotsCount + ", fechadas " + vm.ApprovalTimeoutSlotsCount);
                var csv = vm.CreateSummaryCsv();
                Require(csv.Contains("Vagas aguardando aprovação;2;", StringComparison.Ordinal) && csv.Contains("Vagas aprovadas pela equipe;5;", StringComparison.Ordinal) && csv.Contains("Vagas fechadas sem aprovação;1;", StringComparison.Ordinal) && csv.Contains("Vagas descartadas pela equipe;3;", StringComparison.Ordinal), "O CSV deveria acrescentar as quatro linhas: " + csv);
                SaveScrolled(window, Path.Combine(previewRoot, "secretaria-vaga-aprovacao-light.png"), 1280, 900);
                window.ViewModel.SetAppearance(Appearance.Dark); Layout(window, 1280, 3000); SaveScrolled(window, Path.Combine(previewRoot, "secretaria-vaga-aprovacao-dark.png"), 1280, 900);
            }
            finally { CloseWindow(window); }
        });
    }
}
