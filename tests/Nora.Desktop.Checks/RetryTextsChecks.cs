using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Nora.Desktop.Models;
using Nora.Desktop.Services;
using Nora.Desktop.ViewModels;
using Nora.Desktop.Views;

namespace Nora.Desktop.Checks;

/// <summary>
/// Textos de nova tentativa: cada estado de falha cita um botão que existe na mesma tela e esse botão refaz a leitura que falhou, mesmo com o filtro igual, e a faixa de falha some ao concluir.
/// </summary>
internal static partial class Program
{
    private static void RunRetryTextsChecks(string storageRoot, string[] args)
    {
        Check("Textos de nova tentativa do OBS: comunicados, alertas da UBS e profissionais orientam sem citar botão (a tela relê sozinha) e a leitura é refeita", () =>
        {
            using var fixture = new ApiFixture { UseWorkspaceIdentity = true };
            var unit = fixture.AddUnit("UBS Tentar de novo", "Centro");
            fixture.FailAnnouncementFeed = true; fixture.FailAlerts = true; fixture.FailCatalogUnits.Add(unit.Id);
            var window = OpenOperational(fixture, storageRoot, "retry-obs", Appearance.Light);
            try
            {
                var vm = LoginObs(window);
                // Comunicados.
                var content = Show(window, "announcements", 1280, 1800);
                Require(vm.AnnouncementsState.Contains("tenta de novo sozinha", StringComparison.Ordinal) && !Descendants<Button>(content).Any(button => AutomationProperties.GetName(button) == "Atualizar comunicados"), "A falha dos comunicados deveria orientar sem citar um botão que não existe mais: " + vm.AnnouncementsState);
                // A tela tenta de novo sozinha: o ciclo de 20 s com a tela aberta refaz a leitura.
                fixture.FailAnnouncementFeed = false;
                Pump(vm.RefreshAutomaticallyAsync()); Settle(vm);
                Require(vm.AnnouncementsState == "Nenhum comunicado em vigor para as UBS desta conta.", "O ciclo da tela de comunicados deveria ler de novo depois da falha: " + vm.AnnouncementsState);
                // Reabrir a tela também relê tudo.
                fixture.FailAnnouncementFeed = true; window.ViewModel.Navigate("home"); Settle(vm); content = Show(window, "announcements", 1280, 1800);
                fixture.FailAnnouncementFeed = false;
                window.ViewModel.Navigate("home"); Settle(vm); content = Show(window, "announcements", 1280, 1800);
                Require(vm.AnnouncementsState == "Nenhum comunicado em vigor para as UBS desta conta.", "Reabrir a tela deveria ler os comunicados de novo: " + vm.AnnouncementsState);
                // Alertas da UBS.
                content = Show(window, "alerts", 1280, 1800);
                Require(vm.UnitAlertsState.Contains("tenta de novo sozinha", StringComparison.Ordinal) && !vm.UnitAlertsState.Contains("Atualizar", StringComparison.Ordinal) && !Descendants<Button>(content).Any(button => AutomationProperties.GetName(button) == "Atualizar alertas"), "A falha dos alertas deveria dizer que a tela tenta de novo sozinha: " + vm.UnitAlertsState);
                fixture.FailAlerts = false; AgeAlertsRead(vm, TimeSpan.FromSeconds(20));
                Pump(vm.RefreshAutomaticallyAsync());
                Require(vm.UnitAlertsState.StartsWith("Nenhum alerta.", StringComparison.Ordinal), "O ciclo da tela de alertas deveria avaliar os alertas de novo: " + vm.UnitAlertsState);
                // Profissionais, consultórios, grade e bloqueios das Configurações.
                // As Informações da UBS não têm botão Atualizar: a falha diz que a tela tenta de novo sozinha e a leitura automática seguinte refaz o catálogo.
                content = Show(window, "unit-settings:professionals", 1280, 900);
                Require(vm.ProfessionalsState == "Não foi possível ler os profissionais agora. A tela tenta de novo sozinha." && !Descendants<Button>(content).Any(button => button.IsVisible && (System.Windows.Automation.AutomationProperties.GetName(button) ?? "").StartsWith("Atualizar", StringComparison.Ordinal)), "A falha do catálogo deveria dizer que a tela tenta de novo, sem botão: " + vm.ProfessionalsState);
                fixture.FailCatalogUnits.Clear();
                Pump(vm.RefreshAutomaticallyAsync()); content = Layout(window, 1280, 900);
                Require(vm.ProfessionalsState.StartsWith("Nenhum profissional cadastrado.", StringComparison.Ordinal), "A leitura automática deveria ler o catálogo de novo: " + vm.ProfessionalsState);
            }
            finally { CloseWindow(window); }
        });

        Check("Textos de nova tentativa da Secretaria: alertas da rede e cadastro da Capacidade refazem a leitura com o filtro igual, em falhas seguidas, e a faixa de falha some ao concluir", () =>
        {
            using var fixture = new ApiFixture { UseWorkspaceIdentity = true };
            var alfa = fixture.AddUnit("UBS Alfa", "Centro");
            var cardio = fixture.AddSpecialty("Cardiologia"); fixture.AddProfessional(alfa.Id, "Dra. Ana Lima", [cardio.Id]);
            fixture.FailAlerts = true; fixture.FailCatalogUnits.Add(alfa.Id);
            var window = OpenMunicipal(fixture, storageRoot, "retry-municipal", Appearance.Light);
            try
            {
                var vm = LoginObs(window);
                var content = Show(window, "alerts", 1280, 1400);
                Require(vm.NetworkAlertsState.Contains("Use Atualizar alertas", StringComparison.Ordinal), "A falha dos alertas deveria citar o botão: " + vm.NetworkAlertsState);
                var reads = fixture.NetworkAlertQueries.Count;
                Click(window, content, "Atualizar alertas", 1280, 1400);
                Require(fixture.NetworkAlertQueries.Count == reads + 1 && vm.NetworkAlertsState.Contains("Use Atualizar alertas", StringComparison.Ordinal) && vm.MessageKind == MessageKind.Error, "Uma segunda falha, com o mesmo filtro, deveria refazer a leitura e continuar apontando para o botão.");
                fixture.FailAlerts = false;
                Click(window, content, "Atualizar alertas", 1280, 1400);
                Require(fixture.NetworkAlertQueries.Count == reads + 2 && vm.MessageKind == MessageKind.Success && !vm.IsDataStale && !vm.NetworkAlertsState.Contains("Use Atualizar", StringComparison.Ordinal), "Concluída a leitura, a faixa de falha e o aviso de dados antigos deveriam sumir: " + vm.Message + $" / consultas {fixture.NetworkAlertQueries.Count - reads} / antigos {vm.IsDataStale} / estado {vm.NetworkAlertsState}");
                content = Show(window, "capacity", 1280, 3000);
                vm.CapacityUnit = vm.Units.Single(); Settle(vm); content = Layout(window, 1280, 3000);
                Require(vm.CapacityCatalogState.Contains("Use Ler o cadastro de novo", StringComparison.Ordinal), "A falha do cadastro deveria citar o botão: " + vm.CapacityCatalogState);
                var catalogReads = fixture.Count("GET", "/professionals");
                Click(window, content, "Ler o cadastro de novo", 1280, 3000);
                Require(fixture.Count("GET", "/professionals") == catalogReads + 1 && vm.CapacityCatalogState.Contains("Use Ler o cadastro de novo", StringComparison.Ordinal), "A nova tentativa com a mesma UBS deveria ler de novo (a leitura ainda falha): " + vm.CapacityCatalogState);
                fixture.FailCatalogUnits.Clear();
                Click(window, content, "Ler o cadastro de novo", 1280, 3000);
                Require(fixture.Count("GET", "/professionals") == catalogReads + 2 && vm.CapacityProfessionals.Single().FullName == "Dra. Ana Lima" && vm.CapacityCatalogState == "" && !vm.IsDataStale, "Concluída a leitura o cadastro aparece e a falha some: " + vm.CapacityCatalogState);
            }
            finally { CloseWindow(window); }
        });

        Check("Exportação da Secretaria: com UBS escolhida nenhum texto diz totais da rede e o CSV traz a linha Unidade com o nome", () =>
        {
            using var fixture = new ApiFixture { UseWorkspaceIdentity = true };
            var alfa = fixture.AddUnit("UBS Alfa", "Centro"); fixture.AddUnit("UBS Beta", "Sul");
            var window = OpenMunicipal(fixture, storageRoot, "retry-report-scope", Appearance.Light);
            try
            {
                var vm = LoginObs(window);
                var content = Show(window, "reports", 1280, 3000);
                Require(vm.CreateSummaryCsv().Contains("Unidade;Toda a rede;", StringComparison.Ordinal) && vm.SummaryExportNote.Contains("toda a rede", StringComparison.Ordinal), "Sem UBS escolhida o resumo é da rede inteira.");
                vm.SummaryUnitFilter = alfa.Id; Settle(vm); content = Layout(window, 1280, 3000);
                var csv = vm.CreateSummaryCsv(); var texts = FullTexts(content);
                Require(csv.Contains("Unidade;UBS Alfa;", StringComparison.Ordinal) && !texts.Any(text => text.Contains("totais atuais da rede", StringComparison.OrdinalIgnoreCase) || text.Contains("totais atuais de toda a rede", StringComparison.OrdinalIgnoreCase)) && vm.SummaryExportNote.Contains("só dessa UBS", StringComparison.Ordinal), "Com a UBS escolhida o painel e o CSV falam só dela: " + vm.SummaryExportNote);
                content = Show(window, "slots", 1280, 3000);
                Require(!FullTexts(content).Any(text => text.Contains("totais atuais da rede", StringComparison.OrdinalIgnoreCase)), "O acompanhamento da Vaga Relâmpago também não pode falar de totais da rede.");
            }
            finally { CloseWindow(window); }
        });
    }
}
