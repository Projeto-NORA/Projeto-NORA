using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using Nora.Desktop.Models;
using Nora.Desktop.Services;
using Nora.Desktop.ViewModels;

namespace Nora.Desktop.Checks;

/// <summary>
/// Regressões dos achados da revisão da Fase 3 (desktop): cada verificação reproduz o defeito descrito e confere a correção.
/// </summary>
internal static partial class Program
{
    private static void AgeField(object target, string field, TimeSpan age) =>
        target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, (DateTimeOffset?)(DateTimeOffset.Now - age));

    /// <summary>Deixa o Dispatcher processar um número fixo de quadros (continuações assíncronas pendentes).</summary>
    private static void PumpFrames(int count) { var done = 0; PumpUntil(() => ++done > count); }

    private static void RunReviewFixesChecks(Application application, string storageRoot, string[] args)
    {
        var previewRoot = PreviewRoot(args);

        Check("Revisão 1: alerta só dispara acima do limite e o texto nunca mostra o valor igual ao limite (espera longa e percentuais)", () =>
        {
            var unit = Guid.NewGuid();
            AlertRow Row(string kind, double value, double threshold) => AlertRow.From(new(kind, unit, "UBS", null, value, threshold, null, null));
            var exact = Row("long-wait", 30.0, 30);
            Require(exact.Value == "mais de 30 dias" && exact.Threshold == "30 dias" && !exact.Description.Contains(": 30 dias (limite 30 dias)", StringComparison.Ordinal), "Espera de 30,0 (acima de 30 por minutos) não pode aparecer como \"30 dias, limite 30 dias\": " + exact.Description);
            Require(Row("long-wait", 30.4, 30).Value == "30,4 dias" && Row("long-wait", 45, 30).Value == "45 dias" && Row("long-wait", 31.0, 30).Value == "31 dias", "Espera acima do limite deveria mostrar o valor com a casa decimal quando houver: " + Row("long-wait", 30.4, 30).Value);
            var missed = Row("missed-rate", 0.2004, 0.2);
            Require(missed.Value == "20,04%" && missed.Threshold == "20,0%", "Faltas 20,04% com limite 20,0% não pode ser mostrada como 20,0%: " + missed.Value);
            var occupancy = Row("occupancy", 0.95005, 0.95);
            Require(occupancy.Value.StartsWith("95,0", StringComparison.Ordinal) && occupancy.Value != "95,0%" && occupancy.Threshold == "95,0%", "Ocupação 95,005% com limite 95,0% não pode ser mostrada como 95,0%: " + occupancy.Value);
            Require(Row("missed-rate", 0.25, 0.2).Value == "25,0%" && Row("occupancy", 0.97, 0.95).Value == "97,0%" && Row("occupancy", 0.2, 0.2).Value == "mais de 20,0%", "Valores claramente acima do limite continuam com uma casa; igual ao limite diz \"mais de\".");
            Require(Row("long-wait", 1.4, 1).Value == "1,4 dias" && Row("long-wait", 1.0, 0.5).Value == "1 dia", "Singular e plural dos dias.");
        });

        Check("Revisão 2: salvar a meta de espera sem mexer no campo não muda o valor gravado (a tela mostra o valor arredondado)", () =>
        {
            using var fixture = new ApiFixture();
            fixture.AddUnit("UBS Metas Revisão", "Centro");
            fixture.Goals.Add(new Goal(GoalMetric.Score, null, 0.70m)); fixture.Goals.Add(new Goal(GoalMetric.MissedRate, null, 0.05m));
            var window = OpenMunicipal(fixture, storageRoot, "review-goals", Appearance.Light);
            try
            {
                var vm = LoginObs(window);
                var content = Show(window, "reports", 1280, 3000);
                GoalEditor Editor(string metric) => vm.GoalEditors.Single(item => item.Metric == metric);
                // 2: 0,8333 h grava 50 min; a tela mostra o valor sem perder precisão e salvar de novo não pode virar 49,8 min.
                var wait = Editor(GoalMetric.AverageWait); wait.Input = "0,8333"; wait.WaitUnit = "h"; Pump(vm.SaveGoalAsync(GoalMetric.AverageWait)); Settle(vm);
                Require(fixture.Goals.Single(goal => goal.Metric == GoalMetric.AverageWait).Target == 50m, "A meta de 0,8333 h deveria gravar 50 min: " + fixture.Goals.Single(goal => goal.Metric == GoalMetric.AverageWait).Target);
                Pump(vm.SaveGoalAsync(GoalMetric.AverageWait)); Settle(vm);
                Require(fixture.Goals.Single(goal => goal.Metric == GoalMetric.AverageWait).Target == 50m, $"Salvar sem alterar o campo (\"{wait.Input}\") mudou a meta para {fixture.Goals.Single(goal => goal.Metric == GoalMetric.AverageWait).Target} min.");
                wait.Input = "1,333"; wait.WaitUnit = "d"; Pump(vm.SaveGoalAsync(GoalMetric.AverageWait)); Settle(vm);
                var stored = fixture.Goals.Single(goal => goal.Metric == GoalMetric.AverageWait).Target;
                Pump(vm.SaveGoalAsync(GoalMetric.AverageWait)); Settle(vm);
                Require(fixture.Goals.Single(goal => goal.Metric == GoalMetric.AverageWait).Target == stored && stored == 1919.52m, $"Meta de 1,333 d gravada como {stored} min e depois alterada para {fixture.Goals.Single(goal => goal.Metric == GoalMetric.AverageWait).Target} min sem a pessoa mexer.");
                // Meta alterada de verdade continua sendo gravada.
                wait.Input = "2"; wait.WaitUnit = "h"; Pump(vm.SaveGoalAsync(GoalMetric.AverageWait)); Settle(vm);
                Require(fixture.Goals.Single(goal => goal.Metric == GoalMetric.AverageWait).Target == 120m && wait.Input == "2", "Digitar outro valor deveria gravá-lo.");
            }
            finally { CloseWindow(window); }
        });

        Check("Revisão 13: salvar uma meta não descarta o valor digitado e ainda não salvo nas outras metas", () =>
        {
            using var fixture = new ApiFixture();
            fixture.AddUnit("UBS Metas Rascunho", "Centro");
            fixture.Goals.Add(new Goal(GoalMetric.Score, null, 0.70m)); fixture.Goals.Add(new Goal(GoalMetric.MissedRate, null, 0.05m));
            var window = OpenMunicipal(fixture, storageRoot, "review-goals-draft", Appearance.Light);
            try
            {
                var vm = LoginObs(window);
                var content = Show(window, "reports", 1280, 3000);
                GoalEditor Editor(string metric) => vm.GoalEditors.Single(item => item.Metric == metric);
                Editor(GoalMetric.Score).Input = "85"; Editor(GoalMetric.MissedRate).Input = "10";
                Pump(vm.SaveGoalAsync(GoalMetric.Score)); Settle(vm);
                Require(fixture.Goals.Single(goal => goal.Metric == GoalMetric.Score).Target == 0.85m && fixture.Goals.Single(goal => goal.Metric == GoalMetric.MissedRate).Target == 0.05m, "Só a meta de comparecimento deveria ter sido gravada.");
                Require(Editor(GoalMetric.MissedRate).Input == "10" && Editor(GoalMetric.Score).Input == "85", $"O valor digitado e ainda não salvo em Faltas voltou ao antigo ({Editor(GoalMetric.MissedRate).Input}).");
                content = Layout(window, 1280, 3000);
                SaveScrolled(window, Path.Combine(previewRoot, "revisao-metas-light.png"), 1280, 900);
                Pump(vm.SaveGoalAsync(GoalMetric.MissedRate)); Settle(vm);
                Require(fixture.Goals.Single(goal => goal.Metric == GoalMetric.MissedRate).Target == 0.10m && Editor(GoalMetric.MissedRate).Input == "10", "Salvar Faltas deveria gravar o valor digitado.");
                // Remover uma meta também não apaga o rascunho das outras.
                Editor(GoalMetric.Score).Input = "90"; var asked = false; Confirmation.Handler = (_, _, _) => { asked = true; return true; };
                try { Pump(vm.RemoveGoalAsync(GoalMetric.MissedRate)); Settle(vm); } finally { Confirmation.Handler = null; }
                Require(!fixture.Goals.Any(goal => goal.Metric == GoalMetric.MissedRate) && Editor(GoalMetric.MissedRate).Input == "" && Editor(GoalMetric.Score).Input == "90" && !asked, "Remover Faltas não deveria apagar o 90 digitado em Comparecimento.");
                window.ViewModel.SetAppearance(Appearance.Dark); Layout(window, 1280, 3000); SaveScrolled(window, Path.Combine(previewRoot, "revisao-metas-dark.png"), 1280, 900);
            }
            finally { CloseWindow(window); }
        });

        Check("Revisão 3: a prévia de alertas do Painel diz que são os primeiros da avaliação, não os mais recentes", () =>
        {
            using var fixture = new ApiFixture();
            var unit = fixture.AddUnit("UBS Prévia Alertas", "Centro");
            foreach (var kind in new[] { "occupancy", "long-wait", "goal-off", "high-queue" })
                fixture.AddAlert(unit.Id, kind switch
                {
                    "occupancy" => new("occupancy", unit.Id, unit.Name, null, 0.97, 0.95, null, null),
                    "long-wait" => new("long-wait", unit.Id, unit.Name, "Pediatria", 45, 30, null, null),
                    "goal-off" => new("goal-off", unit.Id, unit.Name, null, 0.6, 0.8, null, GoalMetric.Score),
                    _ => new("high-queue", unit.Id, unit.Name, "Cardiologia", 23, 20, null, null)
                });
            var window = OpenOperational(fixture, storageRoot, "review-preview-alerts", Appearance.Light);
            try
            {
                var vm = LoginObs(window);
                var content = Layout(window, 1280, 1800);
                Require(vm.UnitAlerts.Count == 4 && vm.UnitAlertPreview.Count == 3, "A prévia deveria ter os três primeiros de quatro alertas.");
                Require(!vm.PanelAlertsText.Contains("recentes", StringComparison.OrdinalIgnoreCase) && vm.PanelAlertsText.Contains("primeiros", StringComparison.OrdinalIgnoreCase) && vm.PanelAlertsText.Contains("central de alertas", StringComparison.OrdinalIgnoreCase),
                    "O texto não pode afirmar recência que a API não fornece e deve mandar para a central de alertas: " + vm.PanelAlertsText);
                Require(FullTexts(content).Contains(vm.PanelAlertsText), "O texto da prévia não aparece no cartão.");
                SaveScrolled(window, Path.Combine(previewRoot, "revisao-painel-previa-alertas-light.png"), 1280, 900);
                window.ViewModel.SetAppearance(Appearance.Dark); Layout(window, 1280, 1800); SaveScrolled(window, Path.Combine(previewRoot, "revisao-painel-previa-alertas-dark.png"), 1280, 900);
            }
            finally { CloseWindow(window); }
        });

        Check("Revisão 10 (OBS): falha de leitura dos alertas nunca vira \"nenhum alerta\"", () =>
        {
            using var fixture = new ApiFixture();
            var unit = fixture.AddUnit("UBS Alertas Falham", "Centro");
            var window = OpenOperational(fixture, storageRoot, "review-alerts-obs", Appearance.Light);
            try
            {
                var vm = LoginObs(window);
                var content = Show(window, "alerts", 1280, 1400);
                Require(vm.UnitAlerts.Count == 0 && vm.PanelAlertsTitle == "Nenhum alerta" && vm.UnitAlertsAllClear, "Uma avaliação concluída e sem alertas diz que não há alerta.");
                fixture.FailAlerts = true; Pump(vm.RefreshAlertsAsync()); Settle(vm); content = Layout(window, 1280, 1400);
                Require(vm.PanelAlertsTitle != "Nenhum alerta" && vm.PanelAlertsText.Contains("não concluiu", StringComparison.Ordinal) && vm.UnitAlertsState.Contains("não concluiu", StringComparison.Ordinal) && !vm.UnitAlertsState.StartsWith("Nenhum alerta", StringComparison.Ordinal) && !vm.UnitAlertsAllClear,
                    $"Depois de uma leitura que falhou o Painel não pode dizer \"nenhum alerta\": título \"{vm.PanelAlertsTitle}\", texto \"{vm.PanelAlertsText}\", estado \"{vm.UnitAlertsState}\".");
                Require(FullTexts(content).Contains(vm.UnitAlertsState), "O estado da falha não aparece na tela de alertas.");
                SaveScrolled(window, Path.Combine(previewRoot, "revisao-alertas-obs-falha-light.png"), 1280, 900);
                window.ViewModel.SetAppearance(Appearance.Dark); Layout(window, 1280, 1400); SaveScrolled(window, Path.Combine(previewRoot, "revisao-alertas-obs-falha-dark.png"), 1280, 900);
                // Alerta presente e leitura seguinte falha: a lista fica com aviso.
                fixture.FailAlerts = false; fixture.AddAlert(unit.Id, new("high-queue", unit.Id, unit.Name, "Cardiologia", 23, 20, null, null)); Pump(vm.RefreshAlertsAsync()); Settle(vm);
                Require(vm.UnitAlertsState == "" && vm.UnitAlerts.Count == 1, "A leitura que deu certo deveria mostrar o alerta, sem aviso.");
                fixture.FailAlerts = true; Pump(vm.RefreshAlertsAsync()); Settle(vm);
                Require(vm.UnitAlertsState.Contains("não concluiu", StringComparison.Ordinal) && vm.UnitAlerts.Count == 1, "Com a lista anterior mantida, a tela precisa avisar que a última avaliação falhou: " + vm.UnitAlertsState);
            }
            finally { CloseWindow(window); }
        });

        Check("Revisão 10 (Secretaria): falha de leitura dos alertas ao trocar o filtro não deixa linhas de outro filtro nem vira \"nenhum alerta\"", () =>
        {
            using var network = new ApiFixture();
            var alfa = network.AddUnit("UBS Alfa", "Centro"); var beta = network.AddUnit("UBS Beta", "Sul");
            network.AddAlert(alfa.Id, SampleAlerts(alfa)[0]); network.AddAlert(alfa.Id, SampleAlerts(alfa)[1]);
            var shell = OpenMunicipal(network, storageRoot, "review-alerts-municipal", Appearance.Light);
            try
            {
                var vm = LoginObs(shell);
                var content = Show(shell, "alerts", 1280, 1700);
                Require(vm.NetworkAlerts.Count == 2, "A avaliação da rede deveria listar os alertas da UBS Alfa.");
                network.FailAlerts = true; vm.AlertsUnitFilter = beta.Id; Settle(vm); content = Layout(shell, 1280, 1700);
                Require(vm.NetworkAlerts.Count == 0 && vm.NetworkAlertsState.Contains("Não foi possível", StringComparison.Ordinal) && !vm.NetworkAlertsState.StartsWith("Nenhum alerta", StringComparison.Ordinal),
                    $"A falha ao trocar o filtro não pode manter as linhas da UBS Alfa nem dizer \"nenhum alerta\": {vm.NetworkAlerts.Count} linhas, estado \"{vm.NetworkAlertsState}\".");
                Require(FullTexts(content).Contains(vm.NetworkAlertsState), "O estado da falha não aparece na tela.");
                SaveScrolled(shell, Path.Combine(previewRoot, "revisao-alertas-secretaria-falha-light.png"), 1280, 900);
                network.FailAlerts = false; vm.AlertsUnitFilter = Guid.Empty; Settle(vm);
                Require(vm.NetworkAlerts.Count == 2 && vm.NetworkAlertsState == "", "Sem a falha a avaliação deveria voltar.");
                network.FailAlerts = true; vm.RefreshCommand.Execute(null); Settle(vm);
                Require(vm.NetworkAlerts.Count == 2 && vm.NetworkAlertsState.Contains("não concluiu", StringComparison.Ordinal), "Uma releitura que falha mantém as linhas, mas precisa avisar que não concluiu: " + vm.NetworkAlertsState);
                shell.ViewModel.SetAppearance(Appearance.Dark); Layout(shell, 1280, 1700); SaveScrolled(shell, Path.Combine(previewRoot, "revisao-alertas-secretaria-falha-dark.png"), 1280, 900);
            }
            finally { CloseWindow(shell); }
        });

        Check("Revisão 4: confirmar a desativação (ou a reativação) com o cadastro relido durante a pergunta nunca faz o contrário do confirmado", () =>
        {
            using var fixture = new ApiFixture();
            var unit = fixture.AddUnit("UBS Cadastro Concorrente", "Centro");
            var patient = fixture.AddRegistry(unit.Id, "Ana Souza", "52998224725");
            var window = OpenOperational(fixture, storageRoot, "review-registry-race", Appearance.Light);
            var asked = 0; Action? whileAsking = null;
            Confirmation.Handler = (_, _, _) => { asked++; whileAsking?.Invoke(); return true; };
            try
            {
                var vm = LoginObs(window);
                var content = Show(window, "patients", 1280, 1400);
                vm.SelectedObsRegistry = vm.ObsRegistry.Single(); content = Layout(window, 1280, 1400);
                // Outro atendente desativa o mesmo paciente enquanto a pergunta de desativar está aberta e o ciclo relê o cadastro.
                whileAsking = () => { fixture.SetRegistryActive(unit.Id, patient.Cpf, false); Pump(vm.RefreshAutomaticallyAsync()); };
                InvokeButton(FindActionButton(content, "Desativar nesta UBS")); PumpUntil(() => asked == 1 && !vm.IsBusy); Settle(vm);
                Require(fixture.Count("PATCH", "/registry/active") == 0 && !fixture.RegistryOf(unit.Id).Single().IsActive, "O paciente já desativado por outra pessoa foi reativado ao confirmar a DESATIVAÇÃO (PATCH enviado: " + fixture.Count("PATCH", "/registry/active") + ").");
                Require(vm.Message.Contains("já está desativado", StringComparison.Ordinal) && vm.MessageKind == MessageKind.Info, "A tela deveria explicar que outra pessoa já desativou: " + vm.Message);
                SaveScrolled(window, Path.Combine(previewRoot, "revisao-cadastro-concorrente-light.png"), 1280, 900);
                // O inverso: reativar com outra pessoa reativando no meio não pode desativar o paciente sem a prévia.
                vm.SelectedObsRegistry = vm.ObsRegistry.Single(); content = Layout(window, 1280, 1400);
                whileAsking = () => { fixture.SetRegistryActive(unit.Id, patient.Cpf, true); Pump(vm.RefreshAutomaticallyAsync()); };
                InvokeButton(FindActionButton(content, "Reativar nesta UBS")); PumpUntil(() => asked == 2 && !vm.IsBusy); Settle(vm);
                Require(fixture.Count("PATCH", "/registry/active") == 0 && fixture.RegistryOf(unit.Id).Single().IsActive && vm.Message.Contains("já está ativo", StringComparison.Ordinal), "Reativar com o cadastro já reativado desativou o paciente sem a prévia: " + vm.Message);
                // Sem concorrência a desativação continua funcionando, com a prévia.
                whileAsking = null; vm.SelectedObsRegistry = vm.ObsRegistry.Single(); content = Layout(window, 1280, 1400);
                InvokeButton(FindActionButton(content, "Desativar nesta UBS")); PumpUntil(() => asked == 3 && !vm.IsBusy); Settle(vm);
                Require(fixture.Count("PATCH", "/registry/active") == 1 && !fixture.RegistryOf(unit.Id).Single().IsActive, "Sem concorrência a desativação deveria ser enviada.");
            }
            finally { Confirmation.Handler = null; CloseWindow(window); }
        });

        Check("Revisão 5a: desativar profissional com o catálogo relido durante a pergunta nunca o reativa", () =>
        {
            using var fixture = new ApiFixture();
            var unit = fixture.AddUnit("UBS Catálogo Concorrente", "Centro");
            var cardio = fixture.AddSpecialty("Cardiologia"); var ana = fixture.AddProfessional(unit.Id, "Dra. Ana", [cardio.Id]); var room = fixture.AddRoom(unit.Id, "Consultório 1");
            var window = OpenOperational(fixture, storageRoot, "review-catalog-race", Appearance.Light);
            Action? whileAsking = null;
            Confirmation.Handler = (_, _, _) => { whileAsking?.Invoke(); return true; };
            try
            {
                var vm = LoginObs(window);
                var content = Show(window, "unit-settings", 1280, 3400);
                vm.SelectedProfessional = vm.Professionals.Single(); content = Layout(window, 1280, 3400);
                whileAsking = () => { fixture.SetProfessionalActive(unit.Id, ana.Id, false); Pump(vm.LoadCatalogAsync()); };
                InvokeButton(FindActionButton(content, "Desativar profissional")); Settle(vm);
                Require(fixture.Count("PATCH", "/active") == 0 && !fixture.ProfessionalsOf(unit.Id).Single().IsActive, "O profissional desativado por outra pessoa foi reativado ao confirmar a DESATIVAÇÃO.");
                Require(vm.Message.Contains("já está desativado", StringComparison.Ordinal), "A tela deveria explicar que outra pessoa já desativou: " + vm.Message);
                // Sem concorrência, reativar e desativar continuam funcionando.
                whileAsking = null; vm.SelectedProfessional = vm.Professionals.Single(); content = Layout(window, 1280, 3400);
                InvokeButton(FindActionButton(content, "Reativar profissional")); Settle(vm);
                Require(fixture.ProfessionalsOf(unit.Id).Single().IsActive && fixture.Count("PATCH", "/active") == 1, "Reativar sem concorrência deveria ser enviado.");
                vm.SelectedProfessional = vm.Professionals.Single(); content = Layout(window, 1280, 3400);
                InvokeButton(FindActionButton(content, "Desativar profissional")); Settle(vm);
                Require(!fixture.ProfessionalsOf(unit.Id).Single().IsActive && fixture.Count("PATCH", "/active") == 2, "Desativar sem concorrência deveria ser enviado.");
            }
            finally { Confirmation.Handler = null; CloseWindow(window); }
        });

        Check("Revisão 5b: desativar consultório com o catálogo relido durante a pergunta nunca o reativa", () =>
        {
            using var fixture = new ApiFixture();
            var unit = fixture.AddUnit("UBS Consultório Concorrente", "Centro");
            var room = fixture.AddRoom(unit.Id, "Consultório 1");
            var window = OpenOperational(fixture, storageRoot, "review-room-race", Appearance.Light);
            Action? whileAsking = null;
            Confirmation.Handler = (_, _, _) => { whileAsking?.Invoke(); return true; };
            try
            {
                var vm = LoginObs(window);
                var content = Show(window, "unit-settings", 1280, 3400);
                vm.SelectedRoom = vm.Rooms.Single(); content = Layout(window, 1280, 3400);
                whileAsking = () => { fixture.SetRoomActive(unit.Id, room.Id, false); Pump(vm.LoadCatalogAsync()); };
                InvokeButton(FindActionButton(content, "Desativar consultório")); Settle(vm);
                Require(fixture.Count("PATCH", "/active") == 0 && !fixture.RoomsOf(unit.Id).Single().IsActive && vm.Message.Contains("já está desativado", StringComparison.Ordinal), "O consultório desativado por outra pessoa foi reativado ao confirmar a DESATIVAÇÃO: " + vm.Message);
                // Sem concorrência, reativar e desativar continuam funcionando.
                whileAsking = null; vm.SelectedRoom = vm.Rooms.Single(); content = Layout(window, 1280, 3400);
                InvokeButton(FindActionButton(content, "Reativar consultório")); Settle(vm);
                Require(fixture.RoomsOf(unit.Id).Single().IsActive && fixture.Count("PATCH", "/active") == 1, "Reativar sem concorrência deveria ser enviado.");
                vm.SelectedRoom = vm.Rooms.Single(); content = Layout(window, 1280, 3400);
                InvokeButton(FindActionButton(content, "Desativar consultório")); Settle(vm);
                Require(!fixture.RoomsOf(unit.Id).Single().IsActive && fixture.Count("PATCH", "/active") == 2, "Desativar sem concorrência deveria ser enviado.");
            }
            finally { Confirmation.Handler = null; CloseWindow(window); }
        });

        Check("Revisão 6: marcar \"Mostrar anulados\" durante uma gravação não deixa a ficha ocupada para sempre", () =>
        {
            using var fixture = new ApiFixture { UseWorkspaceIdentity = true };
            var unit = fixture.AddUnit("UBS Jornada Concorrente", "Centro");
            var patient = fixture.AddRegistry(unit.Id, "Ana Souza", "52998224725");
            var window = OpenOperational(fixture, storageRoot, "review-health-busy", Appearance.Light);
            try
            {
                var vm = LoginObs(window);
                var health = vm.CreateHealthRecords(vm.ObsRegistry.Single(item => item.Cpf == patient.Cpf)) ?? throw new InvalidOperationException("A ficha deveria oferecer a Jornada.");
                Pump(health.EnsureLoadedAsync());
                Require(!health.IsBusy, "A ficha deveria estar livre depois da primeira leitura.");
                health.ExamName = "Hemograma";
                // A gravação fica em andamento (POST retido), a leitura do "Mostrar anulados" também (retida); só a gravação é liberada.
                fixture.HoldNextHealthWrites = 1; fixture.HoldNextHealthQueries = 1;
                var write = health.CreateExamAsync();
                health.IncludeVoided = true;
                Require(health.IsBusy && fixture.HeldHealthWrites.Count == 1 && fixture.HeldHealthQueries.Count == 1, "A gravação e a leitura do filtro deveriam estar em andamento ao mesmo tempo.");
                fixture.HeldHealthWrites.Single().TrySetResult(true);
                Pump(write);
                PumpFrames(30);
                Require(health.Exams.Any(item => item.Name == "Hemograma") && fixture.Count("POST", "/health-records/exams") == 1, "O exame deveria ter sido gravado e listado.");
                Require(!health.IsBusy && health.CanRefresh && health.CanCreateExam, "A ficha ficou ocupada depois da gravação concluída (leitura do \"Mostrar anulados\" sobreposta).");
                Require(fixture.HeldHealthQueries.Count == 1, "A leitura do \"Mostrar anulados\" deveria estar retida pela fixture.");
                foreach (var held in fixture.HeldHealthQueries) held.TrySetResult(true);
                PumpFrames(30);
                Require(!health.IsBusy && health.CanRefresh, "Mesmo depois de a leitura atrasada responder a ficha precisa continuar livre.");
                // A leitura mais nova ainda vence e o indicador desliga ao fim dela.
                health.IncludeVoided = false; PumpUntil(() => !health.IsBusy);
                Require(!health.IsBusy && health.Exams.Any(item => item.Name == "Hemograma"), "A leitura do filtro deveria terminar livre.");
            }
            finally { CloseWindow(window); }
        });

        Check("Revisão 7a: atualizar o cadastro não apaga a especialidade, o profissional e o horário escolhidos no agendamento", () =>
        {
            using var fixture = new ApiFixture();
            var unit = fixture.AddUnit("UBS Escolhas Preservadas", "Centro");
            var clinica = fixture.AddSpecialty("Clínica geral"); var cardio = fixture.AddSpecialty("Cardiologia");
            var ana = fixture.AddProfessional(unit.Id, "Dra. Ana", [clinica.Id, cardio.Id]); var bruno = fixture.AddProfessional(unit.Id, "Dr. Bruno", [clinica.Id]);
            var room = fixture.AddRoom(unit.Id, "Consultório 1"); fixture.AddRoom(unit.Id, "Consultório 2");
            fixture.AddRegistry(unit.Id, "Paciente Um", "52998224725");
            var day = DateTime.Today.AddDays(3);
            fixture.AddAvailableSlot(unit.Id, bruno, clinica, room, LocalAt(day, 9)); fixture.AddAvailableSlot(unit.Id, bruno, clinica, room, LocalAt(day, 9, 30));
            var window = OpenOperational(fixture, storageRoot, "review-selection", Appearance.Light);
            try
            {
                var vm = LoginObs(window);
                var content = Show(window, "agenda", 1280, 1600);
                Click(window, content, "+ Agendar nova consulta", 1280, 1600); content = Layout(window, 1280, 1600);
                vm.BookingSpecialty = vm.BookingSpecialties.Single(item => item.Name == "Clínica geral");
                vm.BookingProfessional = vm.BookingProfessionals.Single(item => item.FullName == "Dr. Bruno"); Settle(vm);
                vm.BookingSlot = vm.BookingSlots[1]; vm.AppointmentPatient = vm.ObsRegistry.Single(); content = Layout(window, 1280, 1600);
                var slotsReads = fixture.AvailabilityQueries.Count; var chosenStart = vm.BookingSlot!.StartsAt;
                vm.RefreshCommand.Execute(null); Settle(vm); content = Layout(window, 1280, 1600);
                Require(vm.BookingSpecialty?.Name == "Clínica geral" && vm.BookingProfessional?.FullName == "Dr. Bruno", $"Atualizar dados apagou a especialidade ou o profissional escolhidos ({vm.BookingSpecialty?.Name} / {vm.BookingProfessional?.FullName}).");
                Require(vm.BookingSlot?.StartsAt == chosenStart && vm.BookingSlots.Count == 2, "Atualizar dados apagou o horário escolhido.");
                Require(fixture.AvailabilityQueries.Count == slotsReads, "A releitura do cadastro sem mudança não deveria buscar os horários de novo.");
                // Um profissional novo cadastrado em outro computador muda a contagem da lista: a escolha continua.
                fixture.AddProfessional(unit.Id, "Dr. Carlos", [clinica.Id]); vm.RefreshCommand.Execute(null); Settle(vm); content = Layout(window, 1280, 1600);
                Require(vm.BookingProfessionals.Count == 3 && vm.BookingProfessional?.FullName == "Dr. Bruno" && vm.BookingSlot?.StartsAt == chosenStart, "Um profissional novo na lista apagou a escolha em andamento.");
                SaveScrolled(window, Path.Combine(previewRoot, "revisao-agenda-escolhas-light.png"), 1280, 900);
                Pump(vm.CreateAppointmentAsync()); Settle(vm);
                Require(vm.MessageKind == MessageKind.Success && fixture.AppointmentsOf(unit.Id).Single().ProfessionalId == bruno.Id, "Agendar depois de atualizar deveria funcionar: " + vm.Message);
            }
            finally { CloseWindow(window); }
        });

        Check("Revisão 7b: atualizar o cadastro não apaga profissional, consultório e especialidade do rascunho da grade", () =>
        {
            using var fixture = new ApiFixture();
            var unit = fixture.AddUnit("UBS Grade Preservada", "Centro");
            var clinica = fixture.AddSpecialty("Clínica geral"); var cardio = fixture.AddSpecialty("Cardiologia");
            fixture.AddProfessional(unit.Id, "Dra. Ana", [clinica.Id, cardio.Id]); fixture.AddProfessional(unit.Id, "Dr. Bruno", [clinica.Id]);
            fixture.AddRoom(unit.Id, "Consultório 1"); fixture.AddRoom(unit.Id, "Consultório 2");
            var window = OpenOperational(fixture, storageRoot, "review-selection-grid", Appearance.Light);
            try
            {
                var vm = LoginObs(window);
                var content = Show(window, "unit-settings", 1280, 3400);
                vm.ScheduleProfessional = vm.ActiveProfessionalOptions.Single(item => item.FullName == "Dra. Ana"); vm.ScheduleRoom = vm.ActiveRoomOptions.Single(item => item.Name == "Consultório 2");
                vm.ScheduleSpecialty = vm.ScheduleSpecialtyOptions.Single(item => item.Name == "Cardiologia"); content = Layout(window, 1280, 3400);
                vm.RefreshCommand.Execute(null); Settle(vm); content = Layout(window, 1280, 3400);
                Require(vm.ScheduleProfessional?.FullName == "Dra. Ana" && vm.ScheduleRoom?.Name == "Consultório 2" && vm.ScheduleSpecialty?.Name == "Cardiologia", $"Atualizar dados apagou o rascunho da grade ({vm.ScheduleProfessional?.FullName} / {vm.ScheduleRoom?.Name} / {vm.ScheduleSpecialty?.Name}).");
                fixture.AddRoom(unit.Id, "Consultório 3"); vm.RefreshCommand.Execute(null); Settle(vm); content = Layout(window, 1280, 3400);
                Require(vm.ScheduleProfessional?.FullName == "Dra. Ana" && vm.ScheduleRoom?.Name == "Consultório 2" && vm.ActiveRoomOptions.Count == 3, "Um consultório novo na lista apagou a escolha do rascunho da grade.");
            }
            finally { CloseWindow(window); }
        });

        Check("Revisão 8: catalog-required no texto livre relê o cadastro e a tela passa ao catálogo", () =>
        {
            using var fixture = new ApiFixture();
            var unit = fixture.AddUnit("UBS Agenda Atualiza", "Centro");
            var cardio = fixture.AddSpecialty("Cardiologia");
            fixture.AddRegistry(unit.Id, "Paciente Um", "52998224725");
            var window = OpenOperational(fixture, storageRoot, "review-booking-refresh", Appearance.Light);
            try
            {
                var vm = LoginObs(window);
                var content = Show(window, "agenda", 1280, 1600);
                Require(vm.UsesFreeTextBooking && !vm.UsesCatalogBooking, "Sem profissional ativo a agenda aceita texto livre.");
                // 8: outro computador cadastra o primeiro profissional; o texto livre recebe catalog-required e a tela passa ao catálogo.
                var ana = fixture.AddProfessional(unit.Id, "Dra. Ana", [cardio.Id]); var room = fixture.AddRoom(unit.Id, "Consultório 1");
                var day = DateTime.Today.AddDays(3); fixture.AddAvailableSlot(unit.Id, ana, cardio, room, LocalAt(day, 9)); fixture.AddAvailableSlot(unit.Id, ana, cardio, room, LocalAt(day, 10));
                vm.SelectAgendaDayCommand.Execute(null); vm.MoveAgenda(1); Settle(vm);
                vm.AppointmentPatient = vm.ObsRegistry.Single(); vm.AppointmentSpecialty = "Cardiologia"; vm.AppointmentProfessional = "Dra. Ana"; vm.AppointmentStart = "09:00"; vm.AppointmentEnd = "09:30";
                var reads = fixture.Count("GET", "/professionals");
                Pump(vm.CreateAppointmentAsync()); Settle(vm);
                Require(vm.MessageKind == MessageKind.Error && vm.Message.Contains("pelo catálogo", StringComparison.Ordinal), "O erro catalog-required deveria explicar: " + vm.Message);
                Require(fixture.Count("GET", "/professionals") == reads + 1 && vm.UsesCatalogBooking && !vm.UsesFreeTextBooking, "Depois do catalog-required a tela deveria reler o cadastro e passar ao catálogo (hoje fica no texto livre até 5 minutos).");
            }
            finally { CloseWindow(window); }
        });

        Check("Revisão 9a: \"Atualizar agenda\" relê o cadastro quando a leitura dele falhou, sem esperar o ciclo de 5 minutos", () =>
        {
            using var fixture = new ApiFixture();
            var unit = fixture.AddUnit("UBS Agenda Atualiza 2", "Centro");
            var cardio = fixture.AddSpecialty("Cardiologia"); fixture.AddProfessional(unit.Id, "Dra. Ana", [cardio.Id]);
            var window = OpenOperational(fixture, storageRoot, "review-booking-refresh-catalog", Appearance.Light);
            try
            {
                var vm = LoginObs(window);
                var content = Show(window, "agenda", 1280, 1600);
                fixture.FailCatalogUnits.Add(unit.Id); vm.RefreshCommand.Execute(null); Settle(vm); content = Layout(window, 1280, 1600);
                Require(vm.BookingCatalogUnavailable && !vm.CanBook && vm.BookingIntro.Contains("Atualize a agenda", StringComparison.Ordinal), "Com a leitura falha o agendamento deveria ficar indisponível, mandando atualizar a agenda.");
                SaveScrolled(window, Path.Combine(previewRoot, "revisao-agenda-indisponivel-light.png"), 1280, 900);
                fixture.FailCatalogUnits.Clear();
                Pump(vm.LoadAppointmentsAsync()); Settle(vm); content = Layout(window, 1280, 1600);
                Require(vm.UsesCatalogBooking && !vm.BookingCatalogUnavailable && vm.CanBook && vm.MessageKind == MessageKind.Success, "\"Atualizar agenda\" deveria reler o cadastro e liberar o agendamento: " + vm.Message);
            }
            finally { CloseWindow(window); }
        });

        Check("Revisão 9b: \"Atualizar agenda\" busca de novo os horários livres quando a busca falhou", () =>
        {
            using var fixture = new ApiFixture();
            var unit = fixture.AddUnit("UBS Agenda Atualiza 3", "Centro");
            var cardio = fixture.AddSpecialty("Cardiologia"); var ana = fixture.AddProfessional(unit.Id, "Dra. Ana", [cardio.Id]); var room = fixture.AddRoom(unit.Id, "Consultório 1");
            var day = DateTime.Today.AddDays(3); fixture.AddAvailableSlot(unit.Id, ana, cardio, room, LocalAt(day, 9)); fixture.AddAvailableSlot(unit.Id, ana, cardio, room, LocalAt(day, 10));
            var window = OpenOperational(fixture, storageRoot, "review-booking-refresh-slots", Appearance.Light);
            try
            {
                var vm = LoginObs(window);
                var content = Show(window, "agenda", 1280, 1600);
                vm.BookingSpecialty = vm.BookingSpecialties.Single(); Settle(vm);
                fixture.FailAvailability = true; vm.BookingProfessional = null; vm.BookingSpecialty = null; vm.BookingSpecialty = vm.BookingSpecialties.Single(); Settle(vm);
                Require(vm.BookingSlots.Count == 0 && vm.BookingState.Contains("Não foi possível buscar os horários livres", StringComparison.Ordinal), "Com a busca falha a lista de horários deveria avisar: " + vm.BookingState);
                fixture.FailAvailability = false; var queries = fixture.AvailabilityQueries.Count;
                Pump(vm.LoadAppointmentsAsync()); Settle(vm);
                Require(vm.BookingSlots.Count == 2 && fixture.AvailabilityQueries.Count > queries && vm.BookingState == "", $"\"Atualizar agenda\" deveria buscar os horários livres de novo ({vm.BookingSlots.Count} horários, estado \"{vm.BookingState}\").");
            }
            finally { CloseWindow(window); }
        });

        Check("Revisão 11: o ciclo de 5 minutos da Comunicação mantém as páginas carregadas, a seleção e a lista de quem leu", () =>
        {
            using var fixture = new ApiFixture();
            fixture.AddUnit("UBS Comunicação", "Centro");
            for (var index = 0; index < 60; index++) fixture.AddAnnouncement("Aviso " + index, "texto", "staff");
            fixture.Announcements.Single(item => item.Title == "Aviso 3").OtherStaff.Add(new("Carla Dias", DateTimeOffset.UtcNow.AddMinutes(-3)));
            var window = OpenMunicipal(fixture, storageRoot, "review-communication-cycle", Appearance.Light);
            try
            {
                var vm = LoginObs(window);
                var content = Show(window, "communication", 1280, 2600);
                Require(vm.MyAnnouncements.Count == 50 && vm.CanLoadMoreAnnouncements, "A primeira página deveria ter 50 comunicados.");
                Click(window, content, "Carregar mais antigos", 1280, 2600);
                Require(vm.MyAnnouncements.Count == 60 && !vm.CanLoadMoreAnnouncements, "A segunda página deveria completar os 60.");
                vm.SelectedMyAnnouncement = vm.MyAnnouncements.Single(item => item.Title == "Aviso 3"); content = Layout(window, 1280, 2600);
                Click(window, content, "Ver quem da equipe leu", 1280, 2600);
                Require(vm.AnnouncementReaders.Count == 1, "A lista de quem leu deveria estar carregada.");
                // Passam 5 minutos: o ciclo automático relê a lista.
                AgeField(vm, "myAnnouncementsAttemptedAt", TimeSpan.FromMinutes(6)); var reads = fixture.Count("GET", "/api/municipal/announcements");
                Pump(vm.RefreshAutomaticallyAsync()); Settle(vm); content = Layout(window, 1280, 2600);
                Require(fixture.Count("GET", "/api/municipal/announcements") == reads + 1, "Passados 5 minutos o ciclo deveria reler a lista uma vez.");
                Require(vm.MyAnnouncements.Count == 60 && !vm.CanLoadMoreAnnouncements, $"O ciclo automático desfez as páginas já carregadas ({vm.MyAnnouncements.Count} comunicados).");
                Require(vm.SelectedMyAnnouncement?.Title == "Aviso 3" && vm.AnnouncementReaders.Count == 1 && vm.AnnouncementReadersSummary.Length > 0, "O ciclo automático apagou a seleção ou a lista de quem leu.");
                // Um comunicado novo entra no topo sem perder o resto.
                fixture.AddAnnouncement("Aviso novo", "texto", "staff"); AgeField(vm, "myAnnouncementsAttemptedAt", TimeSpan.FromMinutes(6)); Pump(vm.RefreshAutomaticallyAsync()); Settle(vm);
                Require(vm.MyAnnouncements.Count == 61 && vm.MyAnnouncements[0].Title == "Aviso novo" && vm.SelectedMyAnnouncement?.Title == "Aviso 3" && vm.AnnouncementReaders.Count == 1, "O comunicado novo deveria entrar no topo, mantendo as páginas, a seleção e os leitores.");
                // Atualizar dados (manual) continua voltando à primeira página.
                vm.RefreshCommand.Execute(null); Settle(vm);
                Require(vm.MyAnnouncements.Count == 50 && vm.CanLoadMoreAnnouncements, "A atualização manual volta à primeira página.");
                SaveScrolled(window, Path.Combine(previewRoot, "revisao-comunicacao-ciclo-light.png"), 1280, 900);
            }
            finally { CloseWindow(window); }
        });

        Check("Revisão 12: se a leitura da Família falha, a aba diz que não conseguiu ler em vez de ficar em \"Carregando…\"", () =>
        {
            using var fixture = new ApiFixture();
            var unit = fixture.AddUnit("UBS Família Falha", "Centro");
            fixture.AddRegistry(unit.Id, "Dependente Um", "52998224725");
            fixture.FailFamily = true;
            var window = OpenOperational(fixture, storageRoot, "review-family-fail", Appearance.Light);
            try
            {
                var vm = LoginObs(window);
                var content = Show(window, "family", 1280, 1400);
                Require(!vm.FamilyPendingState.Contains("Carregando", StringComparison.Ordinal) && vm.FamilyPendingState.Contains("Não foi possível ler os pedidos", StringComparison.Ordinal) && vm.FamilyApprovedState.Contains("Não foi possível ler os vínculos", StringComparison.Ordinal),
                    $"Com a leitura falha a aba não pode ficar em \"Carregando…\": \"{vm.FamilyPendingState}\" / \"{vm.FamilyApprovedState}\".");
                Require(FullTexts(content).Contains(vm.FamilyPendingState), "O aviso de falha não aparece na tela.");
                SaveScrolled(window, Path.Combine(previewRoot, "revisao-familia-falha-light.png"), 1280, 900);
                window.ViewModel.SetAppearance(Appearance.Dark); Layout(window, 1280, 1400); SaveScrolled(window, Path.Combine(previewRoot, "revisao-familia-falha-dark.png"), 1280, 900); window.ViewModel.SetAppearance(Appearance.Light);
                // Sem botão Atualizar: a releitura é a automática (a mesma do ciclo de 20 s), e a aba continua dizendo que não conseguiu ler.
                Pump(vm.RefreshAutomaticallyAsync()); Settle(vm);
                Require(vm.FamilyPendingState.Contains("Não foi possível ler os pedidos", StringComparison.Ordinal) && vm.FamilyApprovedState.Contains("Não foi possível ler os vínculos", StringComparison.Ordinal), "A releitura com a leitura falha não pode ficar em \"Carregando…\" nem dizer que atualizou: " + vm.FamilyPendingState);
                fixture.FailFamily = false; fixture.AddFamilyRequest(unit.Id, "Responsável", "11144477735", "52998224725");
                Pump(vm.RefreshAutomaticallyAsync()); Settle(vm);
                Require(vm.FamilyPending.Count == 1 && vm.FamilyApprovedState.Contains("Nenhum vínculo", StringComparison.Ordinal) && vm.FamilyPendingState == "", "A leitura que volta deveria preencher as listas: " + vm.FamilyPendingState);
                fixture.FailFamily = true; Pump(vm.RefreshAutomaticallyAsync()); Settle(vm);
                Require(vm.FamilyPending.Count == 1 && vm.FamilyPendingState.Contains("não concluiu", StringComparison.Ordinal), "Uma falha depois de carregar mantém a lista e avisa: " + vm.FamilyPendingState);
            }
            finally { CloseWindow(window); }
        });

        Check("Revisão 14: 409 da grade por profissional ou consultório desativado não diz que há sobreposição de horário", () =>
        {
            using var fixture = new ApiFixture();
            var unit = fixture.AddUnit("UBS Grade Concorrente", "Centro");
            var cardio = fixture.AddSpecialty("Cardiologia"); var ana = fixture.AddProfessional(unit.Id, "Dra. Ana", [cardio.Id]); var bruno = fixture.AddProfessional(unit.Id, "Dr. Bruno", [cardio.Id]);
            var room1 = fixture.AddRoom(unit.Id, "Consultório 1"); var room2 = fixture.AddRoom(unit.Id, "Consultório 2");
            var window = OpenOperational(fixture, storageRoot, "review-schedule-409", Appearance.Light);
            try
            {
                var vm = LoginObs(window);
                var content = Show(window, "unit-settings", 1280, 3400);
                vm.ScheduleProfessional = vm.ActiveProfessionalOptions.Single(item => item.FullName == "Dra. Ana"); vm.ScheduleRoom = vm.ActiveRoomOptions.Single(item => item.Name == "Consultório 1");
                fixture.SetProfessionalActive(unit.Id, ana.Id, false);
                var reads = fixture.Count("GET", "/professionals");
                Pump(vm.SaveScheduleAsync()); Settle(vm); content = Layout(window, 1280, 3400);
                Require(vm.ScheduleOverlapError.StartsWith("O profissional escolhido foi desativado", StringComparison.Ordinal) && !vm.ScheduleOverlapError.StartsWith("Sobreposição", StringComparison.Ordinal),
                    "O 409 de um profissional desativado não pode ser descrito como sobreposição de horário: " + vm.ScheduleOverlapError);
                Require(fixture.Count("GET", "/professionals") == reads + 1 && !vm.ActiveProfessionalOptions.Any(item => item.Id == ana.Id) && FullTexts(content).Contains(vm.ScheduleOverlapError), "O cadastro deveria ser relido no erro e o aviso aparecer na tela.");
                SaveScrolled(window, Path.Combine(previewRoot, "revisao-grade-desativado-light.png"), 1280, 900);
                // Consultório desativado.
                vm.ScheduleProfessional = vm.ActiveProfessionalOptions.Single(item => item.FullName == "Dr. Bruno"); vm.ScheduleRoom = vm.ActiveRoomOptions.Single(item => item.Name == "Consultório 2");
                fixture.SetRoomActive(unit.Id, room2.Id, false); Pump(vm.SaveScheduleAsync()); Settle(vm);
                Require(vm.ScheduleOverlapError.StartsWith("O consultório escolhido foi desativado", StringComparison.Ordinal), "O 409 de um consultório desativado deveria dizer isso: " + vm.ScheduleOverlapError);
                // Sobreposição verdadeira continua com o aviso de sobreposição.
                vm.ScheduleProfessional = vm.ActiveProfessionalOptions.Single(item => item.FullName == "Dr. Bruno"); vm.ScheduleRoom = vm.ActiveRoomOptions.Single(item => item.Name == "Consultório 1");
                fixture.AddScheduleRow(unit.Id, bruno, room1, cardio.Id, 1, "08:00", "12:00");
                vm.ScheduleSpecialty = vm.ScheduleSpecialtyOptions.Single(); vm.ScheduleWeekday = 1; vm.ScheduleStart = "09:00"; vm.ScheduleEnd = "10:00";
                Pump(vm.SaveScheduleAsync()); Settle(vm);
                Require(vm.ScheduleOverlapError.StartsWith("Sobreposição", StringComparison.Ordinal), "A sobreposição de verdade continua avisada como tal: " + vm.ScheduleOverlapError);
            }
            finally { CloseWindow(window); }
        });
    }
}
