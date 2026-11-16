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
/// Regressões da segunda rodada de revisão da Fase 3 (desktop): cada verificação reproduz o defeito descrito no achado e confere a correção.
/// </summary>
internal static partial class Program
{
    private static void RunReviewLoop2Checks(string storageRoot, string[] args)
    {
        var previewRoot = PreviewRoot(args);

        Check("Revisão 2.1: Família segue as páginas da API pelo cursor, o resumo conta tudo e, no teto de páginas, a aba avisa que a lista foi cortada", () =>
        {
            using var fixture = new ApiFixture { UseWorkspaceIdentity = true };
            var unit = fixture.AddUnit("UBS Família Paginada", "Centro");
            fixture.AddRegistry(unit.Id, "Dependente Um", "52998224725");
            var start = DateTimeOffset.UtcNow.AddDays(-400);
            for (var index = 0; index < 250; index++) fixture.AddFamilyRequest(unit.Id, $"Responsável {index:D3}", "11144477735", "52998224725", "approved", start.AddHours(index));
            for (var index = 0; index < 3; index++) fixture.AddFamilyRequest(unit.Id, $"Pedido {index}", "11144477735", "52998224725", requestedAt: start.AddHours(300 + index));
            var window = OpenOperational(fixture, storageRoot, "loop2-family-pages", Appearance.Light);
            try
            {
                var vm = LoginObs(window);
                var content = Show(window, "family", 1280, 2200);
                Require(vm.FamilyApproved.Count == 250 && vm.FamilyApproved.Any(link => link.GuardianName == "Responsável 249"), $"Os vínculos depois do 200º deveriam aparecer ({vm.FamilyApproved.Count} na tela).");
                Require(vm.FamilySummary == "3 pedidos aguardando · 250 vínculos aprovados" && vm.FamilyApprovedState == "" && vm.FamilyPendingState == "", "O resumo deveria contar todos os vínculos, sem aviso: " + vm.FamilySummary + " / " + vm.FamilyApprovedState);
                var approved = fixture.FamilyLinkQueries.Where(query => query["status"] == "approved").ToArray();
                Require(fixture.FamilyLinkQueries.Count == 3 && approved.Length == 2 && approved[0]["after"] is null && approved[1]["after"] is { Length: > 0 } && approved.All(query => query["limit"] == "200"),
                    "A segunda página deveria partir do cursor da primeira (uma chamada por página): " + string.Join(" | ", fixture.FamilyLinkQueries.Select(query => query.ToString())));
                // Teto de segurança: com páginas pequenas (a API pode devolver menos que o limit) a leitura para em 50 páginas e avisa.
                fixture.FamilyPageCap = 2;
                for (var index = 0; index < 120; index++) fixture.AddFamilyRequest(unit.Id, $"Pedido extra {index:D3}", "11144477735", "52998224725", requestedAt: start.AddHours(400 + index));
                var before = fixture.FamilyLinkQueries.Count;
                Pump(vm.LoadFamilyAsync()); Settle(vm); content = Layout(window, 1280, 2200);
                Require(fixture.FamilyLinkQueries.Count == before + 100, $"Cada lista deveria parar em 50 páginas ({fixture.FamilyLinkQueries.Count - before} chamadas).");
                Require(vm.FamilyPending.Count == 100 && vm.FamilyPendingState.Contains("mostrando os 100 mais antigos", StringComparison.Ordinal) && vm.FamilyApprovedState.Contains("mostrando os 100 mais antigos", StringComparison.Ordinal),
                    $"No teto a aba deveria avisar que a lista foi cortada: \"{vm.FamilyPendingState}\" / \"{vm.FamilyApprovedState}\".");
                Require(vm.FamilySummary == "100 ou mais pedidos aguardando · 100 ou mais vínculos aprovados" && FullTexts(content).Contains(vm.FamilyPendingState) && FullTexts(content).Contains(vm.FamilySummary), "O resumo não pode afirmar um total exato quando a lista foi cortada: " + vm.FamilySummary);
                SaveScrolled(window, Path.Combine(previewRoot, "revisao2-familia-teto-light.png"), 1280, 900);
                window.ViewModel.SetAppearance(Appearance.Dark); Layout(window, 1280, 2200); SaveScrolled(window, Path.Combine(previewRoot, "revisao2-familia-teto-dark.png"), 1280, 900);
            }
            finally { CloseWindow(window); }
        });

        Check("Revisão 2.2: editar um profissional cuja especialidade foi desativada mantém a especialidade e a grade; a mensagem do 409 não manda apagar a grade", () =>
        {
            using var fixture = new ApiFixture();
            var unit = fixture.AddUnit("UBS Especialidade Desativada", "Centro");
            var clinica = fixture.AddSpecialty("Clínica geral"); var pediatria = fixture.AddSpecialty("Pediatria");
            var professional = fixture.AddProfessional(unit.Id, "Dra. Paula", [clinica.Id, pediatria.Id]); var room = fixture.AddRoom(unit.Id, "Consultório 1");
            fixture.AddScheduleRow(unit.Id, professional, room, pediatria.Id);
            fixture.Specialties[fixture.Specialties.FindIndex(item => item.Id == pediatria.Id)] = pediatria with { IsActive = false };
            var window = OpenOperational(fixture, storageRoot, "loop2-inactive-specialty", Appearance.Light);
            try
            {
                var vm = LoginObs(window);
                var content = Show(window, "unit-settings", 1280, 3400);
                vm.SelectedProfessional = vm.Professionals.Single(); vm.EditSelectedProfessional(); content = Layout(window, 1280, 3400);
                var kept = vm.ProfessionalSpecialtyChoices.SingleOrDefault(choice => choice.Specialty.Id == pediatria.Id);
                Require(kept is { IsChecked: true } && kept.Name.Contains("desativada", StringComparison.Ordinal), "A especialidade desativada já vinculada deveria continuar marcada no formulário, com o aviso: " + string.Join(", ", vm.ProfessionalSpecialtyChoices.Select(choice => choice.Name)));
                vm.CouncilAcronym = "CRM"; vm.CouncilNumber = "4321"; vm.CouncilState = "SP";
                SaveScrolled(window, Path.Combine(previewRoot, "revisao2-profissional-especialidade-desativada-light.png"), 1280, 900);
                window.ViewModel.SetAppearance(Appearance.Dark); Layout(window, 1280, 3400); SaveScrolled(window, Path.Combine(previewRoot, "revisao2-profissional-especialidade-desativada-dark.png"), 1280, 900);
                Pump(vm.SaveProfessionalAsync()); Settle(vm);
                var stored = fixture.ProfessionalsOf(unit.Id).Single();
                Require(vm.MessageKind == MessageKind.Success && stored.CouncilNumber == "4321" && stored.SpecialtyIds.Contains(pediatria.Id) && stored.SpecialtyIds.Contains(clinica.Id), "Corrigir o conselho deveria ser gravado mantendo a especialidade desativada: " + vm.Message);
                // Desmarcar uma especialidade que tem grade: o 409 explica sem mandar apagar a grade.
                vm.SelectedProfessional = vm.Professionals.Single(); vm.EditSelectedProfessional();
                var dropped = vm.ProfessionalSpecialtyChoices.Single(choice => choice.Specialty.Id == pediatria.Id);
                dropped.IsChecked = false; dropped.IsChecked = true;
                Require(!dropped.IsChecked && !dropped.CanCheck, "Desmarcada, a especialidade desativada não pode ser marcada de novo (vínculo novo exige especialidade ativa).");
                Pump(vm.SaveProfessionalAsync()); Settle(vm);
                Require(vm.MessageKind == MessageKind.Error && !vm.Message.Contains("remova a grade", StringComparison.OrdinalIgnoreCase) && !vm.Message.Contains("apague", StringComparison.OrdinalIgnoreCase) && vm.Message.Contains("marcada", StringComparison.Ordinal), "O 409 não pode mandar apagar a grade: " + vm.Message);
                Require(fixture.ProfessionalsOf(unit.Id).Single().SpecialtyIds.Contains(pediatria.Id), "A especialidade não pode ter saído do profissional.");
                // Cadastro novo continua oferecendo só especialidades ativas.
                vm.NewProfessionalDraft();
                Require(vm.ProfessionalSpecialtyChoices.Select(choice => choice.Name).SequenceEqual(["Clínica geral"]), "Um profissional novo só escolhe especialidades ativas.");
            }
            finally { CloseWindow(window); }
        });

        Check("Revisão 2.3: a confirmação de aprovação da Família só promete o que o responsável faz no aplicativo (não diz \"entrar na fila\")", () =>
        {
            using var fixture = new ApiFixture { UseWorkspaceIdentity = true };
            var unit = fixture.AddUnit("UBS Família Texto", "Centro");
            fixture.AddRegistry(unit.Id, "Lia Santos", "52998224725");
            fixture.AddFamilyRequest(unit.Id, "Maria Santos", "12345678909", "52998224725");
            var asked = new List<string>(); Confirmation.Handler = (_, _, text) => { asked.Add(text); return false; };
            var window = OpenOperational(fixture, storageRoot, "loop2-family-approve-text", Appearance.Light);
            try
            {
                var vm = LoginObs(window);
                var content = Show(window, "family", 1280, 1600);
                vm.SelectedPendingFamily = vm.FamilyPending.Single(); content = Layout(window, 1280, 1600);
                Click(window, content, "Aprovar pedido", 1280, 1600);
                var text = asked.Single();
                Require(!text.Contains("entrar na fila", StringComparison.OrdinalIgnoreCase) && text.Contains("sair dela", StringComparison.Ordinal) && text.Contains("agendar e cancelar consultas", StringComparison.Ordinal) && text.Contains("responder ofertas", StringComparison.Ordinal),
                    "A confirmação promete ao responsável o que ele não pode fazer: " + text);
            }
            finally { Confirmation.Handler = null; CloseWindow(window); }
        });

        Check("Revisão 2.4: alertas filtrados por UBS sem base (ou região só com UBS sem base) dizem que nada foi avaliado, não \"nenhum critério passou do limite\"", () =>
        {
            using var fixture = new ApiFixture();
            var alfa = fixture.AddUnit("UBS Alfa", "Centro"); var pending = fixture.AddUnit("UBS Sem Base", "Norte", databaseState: "pending");
            var window = OpenMunicipal(fixture, storageRoot, "loop2-alerts-pending", Appearance.Light);
            try
            {
                var vm = LoginObs(window);
                var content = Show(window, "alerts", 1280, 1400);
                vm.AlertsUnitFilter = pending.Id; Settle(vm); content = Layout(window, 1280, 1400);
                Require(vm.NetworkAlerts.Count == 0 && !vm.NetworkAlertsState.Contains("Nenhum critério passou", StringComparison.Ordinal) && vm.NetworkAlertsState.Contains("não foram avaliados", StringComparison.Ordinal) && FullTexts(content).Contains(vm.NetworkAlertsState),
                    "UBS sem base não foi avaliada e não pode aparecer como \"nenhum alerta\": " + vm.NetworkAlertsState);
                SaveScrolled(window, Path.Combine(previewRoot, "revisao2-alertas-sem-base-light.png"), 1280, 900);
                window.ViewModel.SetAppearance(Appearance.Dark); Layout(window, 1280, 1400); SaveScrolled(window, Path.Combine(previewRoot, "revisao2-alertas-sem-base-dark.png"), 1280, 900);
                vm.AlertsUnitFilter = Guid.Empty; vm.AlertsRegion = "Norte"; Settle(vm);
                Require(vm.NetworkAlertsState.Contains("não foram avaliados", StringComparison.Ordinal), "Região só com UBS sem base também não foi avaliada: " + vm.NetworkAlertsState);
                vm.AlertsRegion = "Centro"; Settle(vm);
                Require(vm.NetworkAlertsState.StartsWith("Nenhum alerta neste filtro", StringComparison.Ordinal), "UBS com base avaliada e sem alerta continua dizendo que nada passou do limite: " + vm.NetworkAlertsState);
                vm.AlertsRegion = ""; Settle(vm);
                Require(vm.NetworkAlertsState.StartsWith("Nenhum alerta neste filtro", StringComparison.Ordinal) && vm.NetworkAlertsState.Contains("1 UBS sem base pronta ficou de fora", StringComparison.Ordinal), "A rede inteira deveria dizer que a UBS sem base ficou de fora: " + vm.NetworkAlertsState);
            }
            finally { CloseWindow(window); }
        });

        Check("Revisão 2.5: falhas de alertas da rede, do cadastro da Capacidade e de Meus comunicados apontam para um botão que existe na página e refaz a leitura", () =>
        {
            using var fixture = new ApiFixture();
            var alfa = fixture.AddUnit("UBS Alfa", "Centro");
            var cardio = fixture.AddSpecialty("Cardiologia"); fixture.AddProfessional(alfa.Id, "Dra. Ana Lima", [cardio.Id]);
            fixture.FailAlerts = true; fixture.FailCatalogUnits.Add(alfa.Id); fixture.FailMyAnnouncements = true;
            var window = OpenMunicipal(fixture, storageRoot, "loop2-retry-buttons", Appearance.Light);
            try
            {
                var vm = LoginObs(window);
                // Alertas da rede.
                var content = Show(window, "alerts", 1280, 1400);
                Require(vm.NetworkAlertsState.Contains("Use Atualizar alertas", StringComparison.Ordinal) && ButtonIn(content, "Atualizar alertas") is not null, "A falha dos alertas deveria apontar para o botão Atualizar alertas da página: " + vm.NetworkAlertsState);
                SaveScrolled(window, Path.Combine(previewRoot, "revisao2-alertas-falha-light.png"), 1280, 900);
                window.ViewModel.SetAppearance(Appearance.Dark); content = Layout(window, 1280, 1400); SaveScrolled(window, Path.Combine(previewRoot, "revisao2-alertas-falha-dark.png"), 1280, 900); window.ViewModel.SetAppearance(Appearance.Light); content = Layout(window, 1280, 1400);
                fixture.FailAlerts = false; var reads = fixture.NetworkAlertQueries.Count;
                Click(window, content, "Atualizar alertas", 1280, 1400);
                Require(fixture.NetworkAlertQueries.Count == reads + 1 && vm.NetworkAlertsState.StartsWith("Nenhum alerta neste filtro", StringComparison.Ordinal) && vm.MessageKind == MessageKind.Success, "O botão deveria avaliar de novo: " + vm.NetworkAlertsState);
                // Cadastro em leitura da Capacidade.
                content = Show(window, "capacity", 1280, 3000);
                vm.CapacityUnit = vm.Units.Single(); Settle(vm); content = Layout(window, 1280, 3000);
                Require(vm.CapacityCatalogState.Contains("Use Ler o cadastro de novo", StringComparison.Ordinal) && ButtonIn(content, "Ler o cadastro de novo") is not null, "A falha do cadastro deveria apontar para o botão da própria página: " + vm.CapacityCatalogState);
                SaveScrolled(window, Path.Combine(previewRoot, "revisao2-capacidade-falha-light.png"), 1280, 900);
                window.ViewModel.SetAppearance(Appearance.Dark); content = Layout(window, 1280, 3000); SaveScrolled(window, Path.Combine(previewRoot, "revisao2-capacidade-falha-dark.png"), 1280, 900); window.ViewModel.SetAppearance(Appearance.Light); content = Layout(window, 1280, 3000);
                fixture.FailCatalogUnits.Clear();
                Click(window, content, "Ler o cadastro de novo", 1280, 3000);
                Require(vm.CapacityProfessionals.Single().FullName == "Dra. Ana Lima" && vm.CapacityCatalogState == "", "O botão deveria ler o cadastro de novo: " + vm.CapacityCatalogState);
                // Meus comunicados.
                content = Show(window, "communication", 1280, 2600);
                Require(vm.MyAnnouncementsState.Contains("Use Atualizar lista", StringComparison.Ordinal) && ButtonIn(content, "Atualizar lista") is not null, "A falha de Meus comunicados deveria apontar para o botão Atualizar lista: " + vm.MyAnnouncementsState);
                fixture.FailMyAnnouncements = false;
                Click(window, content, "Atualizar lista", 1280, 2600);
                Require(vm.MyAnnouncementsState == "Você ainda não publicou comunicados.", "O botão deveria ler a lista de novo: " + vm.MyAnnouncementsState);
            }
            finally { CloseWindow(window); }
        });

        Check("Revisão 2.6: o texto da exportação em Relatórios diz o escopo verdadeiro do resumo (rede inteira ou só a UBS escolhida)", () =>
        {
            using var fixture = new ApiFixture();
            var alfa = fixture.AddUnit("UBS Alfa", "Centro"); fixture.AddUnit("UBS Beta", "Sul");
            var window = OpenMunicipal(fixture, storageRoot, "loop2-report-scope", Appearance.Light);
            try
            {
                var vm = LoginObs(window);
                var content = Show(window, "reports", 1280, 3000);
                Require(!FullTexts(content).Any(text => text.Contains("permanecem totais atuais da rede", StringComparison.Ordinal)) || vm.AppliedSummaryUnitLabel == "Toda a rede", "Texto antigo com escopo fixo.");
                Require(FullTexts(content).Contains(vm.SummaryExportNote) && vm.SummaryExportNote.Contains("toda a rede", StringComparison.Ordinal), "Sem UBS escolhida o texto deveria falar da rede inteira: " + vm.SummaryExportNote);
                vm.SummaryUnitFilter = alfa.Id; Settle(vm); content = Layout(window, 1280, 3000);
                Require(vm.AppliedSummaryUnitLabel == "UBS Alfa" && FullTexts(content).Contains(vm.SummaryExportNote) && vm.SummaryExportNote.Contains("só da UBS Alfa", StringComparison.Ordinal)
                    && !FullTexts(content).Any(text => text.Contains("totais atuais da rede", StringComparison.Ordinal)), "Com uma UBS escolhida o texto não pode dizer que pacientes e fila são da rede: " + vm.SummaryExportNote);
                Require(vm.CreateSummaryCsv().Contains("Unidade;UBS Alfa;", StringComparison.Ordinal), "O CSV deveria ser da UBS escolhida.");
                SaveScrolled(window, Path.Combine(previewRoot, "revisao2-relatorios-escopo-light.png"), 1280, 900);
                window.ViewModel.SetAppearance(Appearance.Dark); Layout(window, 1280, 3000); SaveScrolled(window, Path.Combine(previewRoot, "revisao2-relatorios-escopo-dark.png"), 1280, 900);
            }
            finally { CloseWindow(window); }
        });

        Check("Revisão 2.7: busca de horários que falha depois de trocar o profissional ou a especialidade não deixa o horário do profissional anterior escolhido", () =>
        {
            using var fixture = new ApiFixture();
            var unit = fixture.AddUnit("UBS Troca Profissional", "Centro");
            var clinica = fixture.AddSpecialty("Clínica geral"); var cardio = fixture.AddSpecialty("Cardiologia");
            var ana = fixture.AddProfessional(unit.Id, "Dra. Ana", [clinica.Id]); var bruno = fixture.AddProfessional(unit.Id, "Dr. Bruno", [clinica.Id]); var carlos = fixture.AddProfessional(unit.Id, "Dr. Carlos", [cardio.Id]);
            var room = fixture.AddRoom(unit.Id, "Consultório 1");
            fixture.AddRegistry(unit.Id, "Paciente Um", "52998224725");
            var day = DateTime.Today.AddDays(3);
            fixture.AddAvailableSlot(unit.Id, ana, clinica, room, LocalAt(day, 14)); fixture.AddAvailableSlot(unit.Id, bruno, clinica, room, LocalAt(day, 11)); fixture.AddAvailableSlot(unit.Id, carlos, cardio, room, LocalAt(day, 9));
            var window = OpenOperational(fixture, storageRoot, "loop2-booking-switch", Appearance.Light);
            try
            {
                var vm = LoginObs(window);
                var content = Show(window, "agenda", 1280, 1600);
                vm.BookingSpecialty = vm.BookingSpecialties.Single(item => item.Name == "Clínica geral");
                vm.BookingProfessional = vm.BookingProfessionals.Single(item => item.FullName == "Dra. Ana"); Settle(vm);
                var anaSlot = vm.BookingSlots.Single(); vm.BookingSlot = anaSlot; vm.AppointmentPatient = vm.ObsRegistry.Single();
                Require(vm.BookingSelectionLabel.StartsWith("Horário escolhido", StringComparison.Ordinal), "O horário da Dra. Ana deveria estar escolhido.");
                fixture.FailAvailability = true;
                vm.BookingProfessional = vm.BookingProfessionals.Single(item => item.FullName == "Dr. Bruno"); Settle(vm);
                Require(vm.BookingSlots.Count == 0 && vm.BookingSlot is null && vm.BookingSelectionLabel == "Nenhum horário escolhido.", $"Com a busca do Dr. Bruno falha, os horários da Dra. Ana continuaram na tela ({vm.BookingSlots.Count}, \"{vm.BookingSelectionLabel}\").");
                Pump(vm.CreateAppointmentAsync()); Settle(vm);
                Require(fixture.AppointmentsOf(unit.Id).Count == 0 && vm.MessageKind == MessageKind.Error, "Agendar não pode gravar a consulta com o profissional anterior: " + vm.Message);
                // Mesmo que um horário de outro profissional chegue à escolha, Agendar recusa antes de chamar a API.
                vm.BookingSlot = anaSlot; Pump(vm.CreateAppointmentAsync()); Settle(vm);
                Require(fixture.AppointmentsOf(unit.Id).Count == 0 && vm.MessageKind == MessageKind.Error && vm.Message.Contains("outro profissional", StringComparison.Ordinal), "Agendar gravou um horário de outro profissional: " + vm.Message);
                // Trocar a especialidade com um só profissional (escolha automática) também limpa os horários do anterior.
                fixture.FailAvailability = false;
                vm.BookingProfessional = vm.BookingProfessionals.Single(item => item.FullName == "Dra. Ana"); Settle(vm);
                vm.BookingSlot = vm.BookingSlots.Single();
                fixture.FailAvailability = true;
                vm.BookingSpecialty = vm.BookingSpecialties.Single(item => item.Name == "Cardiologia"); Settle(vm);
                Require(vm.BookingProfessional?.FullName == "Dr. Carlos" && vm.BookingSlots.Count == 0 && vm.BookingSlot is null, "Trocar a especialidade com a busca falha deixou o horário da Dra. Ana escolhido.");
                Pump(vm.CreateAppointmentAsync()); Settle(vm);
                Require(fixture.AppointmentsOf(unit.Id).Count == 0, "Agendar depois da troca de especialidade não pode usar o horário anterior.");
                // Sem falha, a troca mostra só os horários do profissional novo.
                fixture.FailAvailability = false; vm.BookingSpecialty = vm.BookingSpecialties.Single(item => item.Name == "Clínica geral"); vm.BookingProfessional = vm.BookingProfessionals.Single(item => item.FullName == "Dr. Bruno"); Settle(vm);
                Require(vm.BookingSlots.Single().ProfessionalId == bruno.Id, "Os horários livres deveriam ser só do Dr. Bruno.");
            }
            finally { CloseWindow(window); }
        });

        Check("Revisão 2.8: Exames e vacinas avisam quando a leitura do \"Mostrar anulados\" falha e a lista na tela ainda é a do filtro anterior", () =>
        {
            using var fixture = new ApiFixture { UseWorkspaceIdentity = true };
            var unit = fixture.AddUnit("UBS Jornada Filtro", "Centro");
            var patient = fixture.AddRegistry(unit.Id, "Ana Souza", "52998224725");
            fixture.AddVaccine(unit.Id, patient.Cpf, "Tétano antigo", "1", new DateOnly(2020, 1, 5), "voided", "Registro duplicado");
            var window = OpenOperational(fixture, storageRoot, "loop2-health-filter", Appearance.Light);
            ItemDetailsWindow? popup = null;
            try
            {
                var vm = LoginObs(window);
                var record = vm.ObsRegistry.Single();
                var health = vm.CreateHealthRecords(record) ?? throw new InvalidOperationException("A ficha deveria oferecer a Jornada.");
                popup = new ItemDetailsWindow(vm.BuildDetails(record)!, new ClipboardFixture(), health) { Owner = window, WindowStartupLocation = WindowStartupLocation.Manual, Left = -10000, Top = -10000, ShowActivated = false };
                popup.Show(); popup.UpdateLayout();
                InvokeButton(Descendants<Button>(popup).Single(button => AutomationProperties.GetName(button) == "Aba Exames e vacinas")); PumpUntil(() => !health.IsBusy); popup.UpdateLayout();
                Require(health.ListState.Contains("ficam ocultos", StringComparison.Ordinal), "Sem anulados na leitura a ficha deveria orientar a marcar \"Mostrar anulados\": " + health.ListState);
                fixture.FailHealthRecords = true;
                health.IncludeVoided = true; PumpUntil(() => !health.IsBusy); popup.UpdateLayout();
                Require(!health.ListState.Contains("nem anulado", StringComparison.Ordinal) && health.ListState.Contains("não concluiu", StringComparison.Ordinal) && health.ListState.Contains("anulados", StringComparison.Ordinal),
                    "A leitura com anulados falhou: a ficha não pode afirmar que não há anulado: " + health.ListState);
                SavePopupScrolled(popup, Path.Combine(previewRoot, "revisao2-jornada-filtro-falha-light"));
                window.ViewModel.SetAppearance(Appearance.Dark); popup.UpdateLayout(); SavePopupScrolled(popup, Path.Combine(previewRoot, "revisao2-jornada-filtro-falha-dark")); window.ViewModel.SetAppearance(Appearance.Light);
                // Com registros: a lista do filtro anterior (com anulados) fica sob o filtro "ocultos" e precisa de aviso.
                fixture.FailHealthRecords = false; fixture.AddExam(unit.Id, patient.Cpf, "Hemograma");
                Pump(health.ReloadAsync());
                Require(health.ListState == "" && health.Vaccines.Count == 1 && health.Exams.Count == 1, "A leitura que volta deveria mostrar os registros com os anulados: " + health.ListState);
                fixture.FailHealthRecords = true;
                health.IncludeVoided = false; PumpUntil(() => !health.IsBusy);
                Require(health.Vaccines.Any(item => item.IsVoided) && health.ListState.Contains("não concluiu", StringComparison.Ordinal) && health.ListState.Contains("inclui os anulados", StringComparison.Ordinal),
                    "A lista com anulados ficou sob \"ocultos\" sem aviso: " + health.ListState);
                // A leitura que conclui tira o aviso.
                fixture.FailHealthRecords = false; Pump(health.ReloadAsync());
                Require(health.ListState == "" && health.Vaccines.Count == 0, "A leitura sem anulados que conclui deveria tirar o aviso: " + health.ListState);
            }
            finally { popup?.Close(); CloseWindow(window); }
        });

        Check("Revisão 2.9: Família avisa a falha só da leitura que falhou; a outra lista, lida agora, não aparece como \"da leitura anterior\"", () =>
        {
            using var fixture = new ApiFixture { UseWorkspaceIdentity = true };
            var unit = fixture.AddUnit("UBS Família Parcial", "Centro");
            fixture.AddRegistry(unit.Id, "Dependente Um", "52998224725");
            fixture.AddFamilyRequest(unit.Id, "Carla Lima", "12345678909", "52998224725", "approved");
            fixture.FailFamilyStatuses.Add("pending");
            var window = OpenOperational(fixture, storageRoot, "loop2-family-partial", Appearance.Light);
            try
            {
                var vm = LoginObs(window);
                var content = Show(window, "family", 1280, 1600);
                Require(vm.FamilyPendingState.Contains("Não foi possível ler os pedidos", StringComparison.Ordinal) && vm.FamilyApproved.Count == 1 && vm.FamilyApprovedState == "",
                    $"Só os pedidos falharam: os vínculos lidos agora não podem aparecer como da leitura anterior (\"{vm.FamilyPendingState}\" / \"{vm.FamilyApprovedState}\").");
                SaveScrolled(window, Path.Combine(previewRoot, "revisao2-familia-falha-parcial-light.png"), 1280, 900);
                window.ViewModel.SetAppearance(Appearance.Dark); Layout(window, 1280, 1600); SaveScrolled(window, Path.Combine(previewRoot, "revisao2-familia-falha-parcial-dark.png"), 1280, 900);
                Pump(vm.LoadFamilyAsync()); Settle(vm);
                Require(vm.MessageKind == MessageKind.Error && vm.Message.Contains("pedidos", StringComparison.Ordinal) && !vm.Message.Contains("vínculos", StringComparison.Ordinal), "A mensagem deveria falar só dos pedidos: " + vm.Message);
                // Lista aprovada vazia: a mensagem verdadeira continua.
                fixture.FamilyLinks.Clear(); Pump(vm.LoadFamilyAsync()); Settle(vm);
                Require(vm.FamilyApprovedState == "Nenhum vínculo aprovado nesta UBS.", "A falha dos pedidos não pode esconder que não há vínculo aprovado: " + vm.FamilyApprovedState);
                // Ao contrário: só os vínculos falham.
                fixture.FailFamilyStatuses.Clear(); fixture.FailFamilyStatuses.Add("approved"); fixture.AddFamilyRequest(unit.Id, "João Souza", "11144477735", "52998224725");
                Pump(vm.LoadFamilyAsync()); Settle(vm);
                Require(vm.FamilyPending.Count == 1 && vm.FamilyPendingState == "" && vm.FamilyApprovedState.Contains("não concluiu", StringComparison.Ordinal), $"Só os vínculos falharam (\"{vm.FamilyPendingState}\" / \"{vm.FamilyApprovedState}\").");
                Require(vm.MessageKind == MessageKind.Error && vm.Message.Contains("vínculos", StringComparison.Ordinal) && !vm.Message.Contains("pedidos de família", StringComparison.Ordinal), "A mensagem deveria falar só dos vínculos: " + vm.Message);
            }
            finally { CloseWindow(window); }
        });
    }
}
