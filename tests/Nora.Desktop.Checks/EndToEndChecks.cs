using System.IO;
using System.Net.Http.Json;





using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Nora.Desktop.Models;
using Nora.Desktop.ViewModels;
using Nora.Desktop.Services;
using Nora.Desktop.Views;

namespace Nora.Desktop.Checks;

internal static partial class Program
{
    private static void RunEndToEndChecks(string storageRoot, string[] args)
    {
        var address = Environment.GetEnvironmentVariable("NORA_E2E_URL");
        if (string.IsNullOrWhiteSpace(address))
        {
            Console.WriteLine("E2E WPF/PostgreSQL não solicitado: defina NORA_E2E_URL com API descartável.");
            return;
        }
        if (Environment.GetEnvironmentVariable("NORA_E2E_UNIT_REGISTRY") != "1")
        {
            Console.WriteLine("E2E WPF por UBS não solicitado: defina NORA_E2E_UNIT_REGISTRY=1 com a fixture que prepara UBS, bancos por UBS e concessões da conta OBS. O fluxo global antigo foi removido dos clientes.");
            return;
        }
        Check("E2E por UBS: Secretaria (especialidade) → OBS (login → UBS → cadastro com participação → fila → agenda → vaga e ofertas → Painel e indicadores → profissional, consultório, grade e bloqueio → agenda pelo catálogo → consultórios ativos, capacidade e alertas) → Secretaria (resumo, indicadores, capacidade, metas, alertas, histórico) → menu logout", () =>
        {
            var identifier = Environment.GetEnvironmentVariable("NORA_E2E_IDENTIFIER") ?? throw new InvalidOperationException("Falta identificador E2E.");
            var password = Environment.GetEnvironmentVariable("NORA_E2E_PASSWORD") ?? throw new InvalidOperationException("Falta senha E2E.");
            var endpoint = NoraApiClient.ValidateEndpoint(address);
            Require(endpoint.IsLoopback, "E2E requer API descartável no loopback.");
            var suffix = Guid.NewGuid().ToString("N")[..10];
            var obs = new ShellWindow(DesktopProfile.Operational, Path.Combine(storageRoot, "live-obs"));
            var municipal = new ShellWindow(DesktopProfile.Municipal, Path.Combine(storageRoot, "live-municipal"));
            try
            {
                foreach (var window in new[] { obs, municipal })
                {
                    window.ViewModel.ConfirmClose = false; window.ViewModel.Animations = false;
                    PrepareWindow(window);
                }
                LoginFromControls(obs, address, identifier, password);
                var vm = obs.ViewModel.Workspace;
                Require(vm.IsAuthenticated, "OBS não autenticou na API descartável: " + vm.Message);
                {
                    // Secretaria primeiro: a lista de especialidades da rede (banco central) vem dela, e o OBS escolhe dessa lista.
                    LoginFromControls(municipal, address, identifier, password);
                    var network = municipal.ViewModel.Workspace;
                    Require(network.IsAuthenticated, "A Secretaria não autenticou na API descartável: " + network.Message);
                    var specialtyName = "Cardiologia E2E " + suffix;
                    network.SpecialtyDraftName = specialtyName; Pump(network.SaveSpecialtyAsync());
                    Require(network.SpecialtyCatalog.Any(item => item.Name == specialtyName && item.IsActive), "A Secretaria não cadastrou a especialidade na API real: " + network.Message);
                    network.SpecialtyDraftName = specialtyName.ToLowerInvariant(); Pump(network.SaveSpecialtyAsync());
                    Require(network.SpecialtyNameError.Contains("Já existe") && network.SpecialtyCatalog.Count(item => item.Name == specialtyName) == 1, "O nome duplicado deveria dar 409 no campo (API real): " + network.Message);
                    network.NewSpecialtyDraft();
                    Pump(vm.LoadObsUnitsAsync());
                    vm.CandidateObsUnit = vm.ObsUnits.Single(u => u.Name == "UBS Cadastro E2E 1");
                    Pump(vm.ConfirmObsUnitAsync());
                    Require(vm.ObsRegistry.Any(p => p.Email == "first@example.test"), "OBS não consultou o cadastro real da UBS: " + vm.Message);
                    // Comunicados reais: a Secretaria publica para as equipes de toda a rede, o OBS lê, marca como lido e a Secretaria vê a leitura.
                    var announcementTitle = "Comunicado E2E " + suffix;
                    network.AnnouncementTitle = announcementTitle; network.AnnouncementBodyText = "Texto puro <b>E2E</b>\nsegunda linha"; network.AnnouncementAudienceKey = "staff"; network.AnnouncementScopeKey = "all";
                    Pump(network.PublishAnnouncementAsync());
                    Require(network.MyAnnouncements.Any(item => item.Title == announcementTitle && item.StatusDisplay == "Em vigor"), "A Secretaria não publicou o comunicado na API real: " + network.Message);
                    Pump(vm.LoadAnnouncementsAsync());
                    var receivedAnnouncement = vm.Announcements.SingleOrDefault(row => row.Title == announcementTitle) ?? throw new InvalidOperationException("O OBS não recebeu o comunicado real: " + vm.Message);
                    Require(receivedAnnouncement.IsUnread && receivedAnnouncement.Body == "Texto puro <b>E2E</b>\nsegunda linha" && vm.UnreadAnnouncementCount == 1 && obs.ViewModel.Navigation.Single(item => item.Id == "announcements").Badge == 1, "O comunicado real deveria chegar não lido, com o texto puro intacto e o contador em 1.");
                    var announcementReads = obs.ViewModel.Workspace.Announcements.Count; Pump(vm.RefreshAutomaticallyAsync());
                    Require(vm.Announcements.Count == announcementReads, "A leitura incremental real sem novidade não deveria mudar a lista.");
                    Pump(vm.MarkAnnouncementReadAsync(receivedAnnouncement));
                    Require(vm.UnreadAnnouncementCount == 0 && receivedAnnouncement.StateDisplay.StartsWith("Lido em", StringComparison.Ordinal), "Marcar como lido na API real não baixou o contador: " + vm.Message);
                    network.Section = "communication"; Pump(network.RefreshAutomaticallyAsync());
                    network.SelectedMyAnnouncement = network.MyAnnouncements.Single(item => item.Title == announcementTitle);
                    Require(network.SelectedMyAnnouncement.StaffReadsDisplay == "1" && network.SelectedMyAnnouncement.PatientReadsDisplay == "—", "A Secretaria deveria ver 1 leitura de equipe: " + network.SelectedMyAnnouncement.StaffReadsDisplay);
                    Pump(network.LoadAnnouncementReadersAsync());
                    Require(network.AnnouncementReaders.Count == 1 && network.AnnouncementReaders[0].FullName == "Operador de teste", "A lista de quem leu deveria trazer o operador: " + network.AnnouncementReadersState);
                    Pump(network.CloseMyAnnouncementsAsync([network.SelectedMyAnnouncement!]));
                    Require(network.MyAnnouncements.Single(item => item.Title == announcementTitle).StatusDisplay == "Encerrado", "A Secretaria não encerrou o comunicado real: " + network.Message);
                    Pump(vm.LoadAnnouncementsAsync());
                    Require(vm.Announcements.All(row => row.Title != announcementTitle), "O comunicado encerrado deveria sair da leitura completa do OBS.");
                    vm.ObsRegistryName = "Referência WPF " + suffix; vm.ObsRegistryCpf = SyntheticCpf(); vm.ObsRegistryEmail = "unit-" + suffix + "@example.test"; vm.ObsRegistryPhone = "(48) 99999-0028";
                    vm.ObsRegistryCep = "01001000"; vm.ObsRegistryStreet = "Praça da Sé"; vm.ObsRegistryCity = "São Paulo"; vm.ObsRegistryState = "SP";
                    Pump(vm.CreateObsRegistryAsync());
                    Require(vm.ObsRegistry.Any(p => p.Email == "unit-" + suffix + "@example.test" && p.Phone == "48999990028" && p.Address?.Cep == "01001000"), "OBS não gravou cadastro real com contato e endereço na UBS: " + vm.Message);
                    vm.SelectedObsRegistry = vm.ObsRegistry.Single(p => p.Email == "unit-" + suffix + "@example.test");
                    Pump(vm.SetObsRegistryActiveAsync(vm.SelectedObsRegistry!.Cpf, false)); Require(vm.SelectedObsRegistry?.IsActive == false, "OBS não desativou o paciente na UBS: " + vm.Message);
                    Pump(vm.SetObsRegistryActiveAsync(vm.SelectedObsRegistry!.Cpf, true)); Require(vm.SelectedObsRegistry?.IsActive == true, "OBS não reativou o paciente na UBS: " + vm.Message);
                    vm.EditSelectedObsRegistry(); vm.ObsRegistryNumber = "20"; Pump(vm.CreateObsRegistryAsync());
                    Require(vm.ObsRegistry.Any(p => p.Email == "unit-" + suffix + "@example.test" && p.Address?.Number == "20"), "OBS não atualizou o endereço do paciente na UBS: " + vm.Message);
                    vm.MoveAgenda(1); vm.AppointmentPatient = vm.ObsRegistry.Single(p => p.Email == "unit-" + suffix + "@example.test");
                    vm.AppointmentSpecialty = "Clínica"; vm.AppointmentProfessional = "Profissional de teste";
                    Pump(vm.CreateAppointmentAsync());
                    var consultation = vm.Appointments.SingleOrDefault(a => a.PatientName == "Referência WPF " + suffix);
                    Require(consultation?.Status == "scheduled", "OBS não criou consulta real: " + vm.Message);
                    vm.SelectedAppointment = consultation; Pump(vm.ChangeAppointmentStatusAsync("called", false));
                    Require(vm.SelectedAppointment?.Status == "called", "OBS não chamou a consulta: " + vm.Message);
                    Pump(vm.ChangeAppointmentStatusAsync("cancelled", true));
                    Require(vm.SelectedAppointment?.Status == "cancelled", "OBS não cancelou a consulta: " + vm.Message);
                    vm.RefreshCommand.Execute(null); PumpUntil(() => !vm.IsBusy);
                    Require(vm.TodayScheduledCount != "—" && vm.TodayCompletedCount != "—" && vm.OffersRunningCount == "2" && vm.SlotOpenCount == "2", "Painel não atualizou os dados da UBS pela API real: " + vm.Message);
                    // Painel: espera média e série de 7 dias vêm da API real (nenhuma oferta foi aceita, então a espera é —); participação vem junto do cadastro.
                    Require(vm.HasActiveObsUnit && vm.PanelChart.Days.Count == 7 && vm.PanelWaitValue == "—" && vm.PanelWaitHint.Contains("Nenhuma entrada saiu da fila"), $"Painel não leu os indicadores da UBS pela API real: {vm.PanelWaitValue} / {vm.PanelWaitHint}");
                    Require(vm.ObsRegistry.All(p => p.Attendance is not null) && vm.ObsRegistry.Single(p => p.Email == "unit-" + suffix + "@example.test").Attendance is { Completed: 0, Missed: 0, CancelledByPatient: 0, Score: null }, "O cadastro de referência não trouxe a participação de cada paciente (nula sem consulta encerrada).");
                    Require(vm.ObsRegistry.Single(p => p.Email == "unit-" + suffix + "@example.test").Attendance is { Points: 1000, Band: "excellent", HasHistory: false }, "O cadastro sem consulta encerrada deveria vir com 1000 pontos, faixa excelente e sem histórico.");
                    Require(vm.ObsRegistry.Single(p => p.Email == "unit-" + suffix + "@example.test").ScoreDisplay == "1000 · sem consultas encerradas ainda", "Score sem consulta encerrada deveria aparecer como 1000 · sem consultas encerradas ainda.");
                    // Ficha da vaga: as ofertas vêm da rota real.
                    var offersTask = vm.BuildDetailsAsync(vm.UnitLightningSlots.First(item => item.AppointmentId == consultation!.Id)); Pump(offersTask);
                    Require(offersTask.Result?.Fields.Any(field => field.Group == "Ofertas da vaga") == true, "A ficha da vaga não leu as ofertas pela API real: " + vm.Message);
                    // Indicadores da UBS: janela personalizada de hoje a 120 dias (a consulta da agenda está no futuro).
                    SetUnitPeriod(vm, DateTime.Today, DateTime.Today.AddDays(120));
                    Require(vm.HasUnitMetrics && UnitCard(vm, "Cancelamentos").Value == "2" && UnitCard(vm, "Cancelamentos").Hint == "0 feitos pelo paciente" && UnitCard(vm, "Consultas concluídas").Value == "0" && UnitCard(vm, "Comparecimento").Value == "—", "Indicadores da UBS não refletiram as consultas reais: " + string.Join(" | ", vm.UnitOverviewCards.Select(card => card.Title + "=" + card.Value)) + " " + vm.Message);
                    Require(vm.UnitLightningCards.Single(card => card.Title == "Vagas registradas").Value == "2" && vm.UnitSpecialtyPerformance.Any(row => row.Name == "Cardiologia") && vm.UnitChart.Rows.Count >= 2 && vm.UnitChart.Days.Count == 121, $"Vagas, especialidades ou série dos indicadores da UBS incorretas: {vm.UnitChart.Rows.Count} dias com dado, {vm.UnitChart.Days.Count} no eixo.");
                    // Fase 3: catálogo da UBS (administrador), agenda pelo catálogo, consultórios ativos, capacidade e alertas, tudo contra a API real.
                    var zone = TimeZoneInfo.TryFindSystemTimeZoneById("America/Sao_Paulo", out var found) ? found : TimeZoneInfo.Local;
                    var sameZone = zone.GetUtcOffset(DateTime.Now) == TimeZoneInfo.Local.GetUtcOffset(DateTime.Now);
                    int WeekdayIn(DateTime day) => (int)TimeZoneInfo.ConvertTime(new DateTimeOffset(day.Date.AddHours(12)), zone).DayOfWeek;
                    var useToday = DateTime.Now < DateTime.Today.AddHours(22);
                    var targetDay = useToday ? DateTime.Today : DateTime.Today.AddDays(1);
                    var professionalName = "Dra. E2E " + suffix; var roomName = "Consultório E2E " + suffix;
                    obs.ViewModel.Navigate("unit-settings"); PumpUntil(() => !vm.IsBusy);
                    Require(vm.CanEditCatalog && vm.ObsSpecialties.Any(item => item.Name == specialtyName && item.IsActive), "O administrador deveria ver a especialidade criada pela Secretaria: " + vm.Message);
                    vm.ProfessionalName = professionalName; vm.CouncilAcronym = "CRM"; vm.CouncilNumber = "123456"; vm.CouncilState = "SC";
                    vm.ProfessionalSpecialtyChoices.Single(choice => choice.Name == specialtyName).IsChecked = true; Pump(vm.SaveProfessionalAsync());
                    var professional = vm.Professionals.SingleOrDefault(item => item.FullName == professionalName && item.IsActive && item.CouncilDisplay == "CRM 123456/SC") ?? throw new InvalidOperationException("O profissional não foi gravado na API real: " + vm.Message);
                    vm.RoomName = roomName; Pump(vm.SaveRoomAsync());
                    Require(vm.Rooms.Any(item => item.Name == roomName && item.IsActive), "O consultório não foi gravado na API real: " + vm.Message);
                    vm.RoomName = roomName.ToLowerInvariant(); Pump(vm.SaveRoomAsync());
                    Require(vm.RoomNameError.Contains("Já existe"), "O consultório repetido deveria dar 409 no campo (API real): " + vm.Message);
                    vm.NewRoomDraft();
                    // Grade: um atendimento por semana, às 23:00, no dia da semana alvo; a ocupação de 100% vira alerta e a sobreposição dá 409.
                    vm.ScheduleProfessional = vm.ActiveProfessionalOptions.Single(item => item.Id == professional.Id); vm.ScheduleRoom = vm.ActiveRoomOptions.Single(item => item.Name == roomName);
                    vm.ScheduleWeekday = WeekdayIn(targetDay); vm.ScheduleStart = "23:00"; vm.ScheduleEnd = "23:30"; vm.ScheduleSlotMinutes = "30"; Pump(vm.SaveScheduleAsync());
                    Require(vm.ScheduleRows.Count == 1 && vm.ScheduleRows[0].SpecialtyName == specialtyName && vm.ScheduleRows[0].RoomName == roomName, "A grade não foi gravada na API real: " + vm.Message + " " + vm.ScheduleError);
                    vm.ScheduleProfessional = vm.ActiveProfessionalOptions.Single(item => item.Id == professional.Id); vm.ScheduleRoom = vm.ActiveRoomOptions.Single(item => item.Name == roomName);
                    vm.ScheduleWeekday = WeekdayIn(targetDay); vm.ScheduleStart = "23:10"; vm.ScheduleEnd = "23:40"; Pump(vm.SaveScheduleAsync());
                    Require(vm.ScheduleOverlapError.Contains("Sobreposição") && vm.ScheduleRows.Count == 1, "A sobreposição deveria dar 409 no campo (API real): " + vm.Message);
                    vm.NewScheduleDraft();
                    vm.BlockScope = professional.Id; vm.BlockFullDay = true; vm.BlockFromText = DisplayFormat.Date(DateOnly.FromDateTime(targetDay.AddDays(7))); vm.BlockReason = "Folga E2E"; Pump(vm.SaveBlockAsync());
                    Require(vm.ScheduleBlocks.Any(item => item.Reason == "Folga E2E" && item.ScopeDisplay == professionalName && item.PeriodDisplay.EndsWith("(dia inteiro)", StringComparison.Ordinal)), "O bloqueio do profissional não foi gravado na API real: " + vm.Message);
                    // Agenda pelo catálogo: o horário bloqueado da semana seguinte não é oferecido; só o do dia alvo.
                    obs.ViewModel.Navigate("agenda"); PumpUntil(() => !vm.IsBusy);
                    vm.SelectCalendarDayCommand.Execute(new AgendaCalendarDay(targetDay, true)); PumpUntil(() => !vm.IsBusy);
                    Require(vm.UsesCatalogBooking && !vm.UsesFreeTextBooking, "Com profissional ativo a agenda deveria marcar só pelo catálogo.");
                    vm.BookingSpecialty = vm.BookingSpecialties.Single(item => item.Name == specialtyName); PumpUntil(() => !vm.IsBusy);
                    Require(vm.BookingProfessional?.Id == professional.Id && vm.BookingSlots.Count == 1 && vm.BookingSlots[0].RoomName == roomName, $"A disponibilidade real deveria oferecer 1 horário (o da semana seguinte está bloqueado): {vm.BookingSlots.Count} / {vm.BookingState} / {vm.Message}");
                    var takenSlot = vm.BookingSlots[0];
                    vm.AppointmentPatient = vm.ObsRegistry.Single(p => p.Email == "unit-" + suffix + "@example.test"); vm.BookingSlot = takenSlot; Pump(vm.CreateAppointmentAsync());
                    var catalogAppointment = vm.Appointments.SingleOrDefault(a => a.ProfessionalId == professional.Id) ?? throw new InvalidOperationException("A consulta pelo catálogo não foi criada na API real: " + vm.Message);
                    Require(catalogAppointment.Status == "scheduled" && catalogAppointment.BookedBy == "staff" && catalogAppointment.BookedByDisplay == "Equipe da UBS" && catalogAppointment.SpecialtyId == vm.ObsSpecialties.Single(item => item.Name == specialtyName).Id && catalogAppointment.Specialty == specialtyName && catalogAppointment.Professional == professionalName, "A consulta pelo catálogo veio sem os dados do catálogo.");
                    Require(catalogAppointment.ConfirmedAt is null && catalogAppointment.ConfirmationDisplay == "Sem confirmação", "A consulta próxima deveria aparecer sem confirmação (API real): " + catalogAppointment.ConfirmationDisplay);
                    PumpUntil(() => !vm.IsBusy); Require(vm.BookingSlots.Count == 0, "O horário marcado deveria sair da lista de disponíveis.");
                    vm.AppointmentPatient = vm.ObsRegistry.Single(p => p.Email == "unit-" + suffix + "@example.test"); vm.BookingSlot = takenSlot; Pump(vm.CreateAppointmentAsync());
                    Require(vm.MessageKind == MessageKind.Error && (vm.Message.Contains("não está mais livre") || vm.Message.Contains("já tem uma consulta que cruza")), "O horário já ocupado deveria dar 409 com mensagem própria (API real): " + vm.Message);
                    // Painel: consultórios ativos reais, capacidade dos próximos 7 dias e alerta de ocupação.
                    obs.ViewModel.Navigate("home"); vm.RefreshCommand.Execute(null); PumpUntil(() => !vm.IsBusy);
                    Require(vm.ActiveRoomsInUseValue == "0" && vm.ActiveRoomsPlannedValue == (useToday ? "1" : "0"), $"Consultórios ativos reais: {vm.ActiveRoomsInUseValue} em atendimento, {vm.ActiveRoomsPlannedValue} com grade hoje.");
                    if (sameZone) Require(vm.PanelOfferedValue == "1" && vm.PanelBookedValue == "1" && vm.PanelOccupancyValue == "100,0%", $"Capacidade real dos próximos 7 dias: {vm.PanelOfferedValue}/{vm.PanelBookedValue}/{vm.PanelOccupancyValue}.");
                    else Require(vm.PanelOfferedValue != "—" && vm.PanelBookedValue != "—", "A capacidade real deveria ter sido lida.");
                    obs.ViewModel.Navigate("alerts"); PumpUntil(() => !vm.IsBusy);
                    Require(vm.UnitAlerts.Any(item => item.Title == "Ocupação da grade acima do limite" && item.Value == "100,0%" && item.Threshold == "95,0%"), "A ocupação de 100% deveria virar alerta (API real): " + string.Join(" | ", vm.UnitAlerts.Select(item => item.Description)) + " / " + vm.UnitAlertsState);
                    obs.ViewModel.Navigate("home");
                    Require(vm.PanelAlertsTitle.Contains("alerta") && !vm.PanelAlertsTitle.StartsWith("Nenhum", StringComparison.Ordinal), "O Painel deveria resumir os alertas reais.");
                    // Jornada real: exame (solicitado → realizado), vacina com dose e anulação com motivo.
                    var journeyRecord = vm.ObsRegistry.Single(p => p.Email == "unit-" + suffix + "@example.test");
                    var journey = vm.CreateHealthRecords(journeyRecord) ?? throw new InvalidOperationException("A ficha deveria oferecer a Jornada.");
                    Pump(journey.EnsureLoadedAsync());
                    Require(journey.IsLoaded && journey.Exams.Count == 0 && journey.Vaccines.Count == 0, "A Jornada real do paciente novo deveria estar vazia: " + journey.Message);
                    journey.ExamName = "Hemograma E2E " + suffix; journey.ExamNote = "Jejum de 8 h"; Pump(journey.CreateExamAsync());
                    Require(journey.Exams.Single() is { Status: "requested", Note: "Jejum de 8 h" }, "O exame não foi registrado como solicitado na API real: " + journey.Message);
                    journey.ExamDoneDate = DisplayFormat.Date(DateOnly.FromDateTime(DateTime.Today)); Pump(journey.MarkExamDoneAsync());
                    Require(journey.Exams.Single() is { Status: "done" } doneExam && doneExam.PerformedOn == DateOnly.FromDateTime(DateTime.Today), "O exame não foi marcado como realizado na API real: " + journey.Message);
                    Pump(journey.MarkExamDoneAsync());
                    Require(journey.MessageKind == MessageKind.Error, "Marcar de novo um exame realizado deveria ser recusado.");
                    journey.VaccineName = "Gripe E2E"; journey.VaccineDate = "01/02/2026"; journey.VaccineDoseChoice = HealthRecordsViewModel.DoseOptions.Single(option => option.Value == "booster"); Pump(journey.CreateVaccineAsync());
                    Require(journey.Vaccines.Single() is { Dose: "booster", DoseDisplay: "Reforço" } && journey.Vaccines.Single().AppliedOn == new DateOnly(2026, 2, 1), "A vacina não foi registrada com dose e data na API real: " + journey.Message);
                    journey.SelectedExam = journey.Exams.Single(); journey.VoidReason = "Teste E2E de anulação"; Pump(journey.VoidSelectedAsync());
                    Require(journey.Exams.Count == 0, "O exame anulado deveria sair da lista da API real: " + journey.Message);
                    journey.IncludeVoided = true; PumpUntil(() => !journey.IsBusy);
                    Require(journey.Exams.Single() is { IsVoided: true } voidedExam && voidedExam.VoidReason == "Teste E2E de anulação", "Mostrar anulados deveria trazer o motivo da API real.");
                    // Família real: o pedido nasce no app (conta de paciente criada pelo cadastro e pedido pela API); a equipe aprova e depois encerra.
                    var familyUnit = vm.ObsUnits.Single(u => u.Name == "UBS Cadastro E2E 1");
                    var guardianToken = RegisterE2EPatient(address, vm.ObsUnits.Select(u => u.Id).ToArray());
                    var dependentCpf = journeyRecord.Cpf;
                    Await(RequestDependentAsync(address, guardianToken, dependentCpf, familyUnit.Id));
                    obs.ViewModel.Navigate("family"); PumpUntil(() => !vm.IsBusy);
                    var familyRequest = vm.FamilyPending.SingleOrDefault(link => link.DependentCpf == dependentCpf) ?? throw new InvalidOperationException("O pedido de família real não apareceu na UBS: " + vm.Message + " / " + vm.FamilyPendingState);
                    Require(familyRequest.GuardianCpf == "52998224725" && familyRequest.DependentName == "Referência WPF " + suffix, "O pedido deveria trazer o responsável e o nome do dependente do cadastro.");
                    vm.SelectedPendingFamily = familyRequest; Pump(vm.ApproveFamilyAsync());
                    Require(vm.FamilyPending.All(link => link.Id != familyRequest.Id) && vm.FamilyApproved.Any(link => link.Id == familyRequest.Id && link.DecidedByName == "Operador de teste"), "A aprovação real deveria mover o pedido para os vínculos: " + vm.Message);
                    Require(Await(ReadDependentStatusAsync(address, guardianToken, familyRequest.Id)) == "approved", "O responsável deveria ver o vínculo aprovado pela API.");
                    vm.SelectedApprovedFamily = vm.FamilyApproved.Single(link => link.Id == familyRequest.Id); Pump(vm.EndFamilyAsync());
                    Require(vm.FamilyApproved.All(link => link.Id != familyRequest.Id) && Await(ReadDependentStatusAsync(address, guardianToken, familyRequest.Id)) == "ended", "Encerrar deveria refletir na API real: " + vm.Message);
                    obs.ViewModel.Navigate("home"); PumpUntil(() => !vm.IsBusy);
                    network.RefreshCommand.Execute(null); PumpUntil(() => !network.IsBusy);
                    Require(network.PatientCount == "3", "Resumo da Secretaria não agregou a rede sem duplicar paciente: " + network.Message);
                    Pump(network.LoadUnitsAsync()); network.SummaryUnitFilter = network.Units.Single(u => u.Name == "UBS Cadastro E2E 2").Id;
                    PumpUntil(() => !network.IsBusy);
                    Require(network.PatientCount == "1" && network.CreateSummaryCsv().Contains("UBS Cadastro E2E 2"), "Filtro de UBS ou escopo do CSV não refletiu a unidade: " + network.Message);
                    network.SelectedUnit = network.Units.Single(u => u.Name == "UBS Cadastro E2E 1");
                    Pump(network.LoadMunicipalAppointmentsAsync());
                    Require(network.MunicipalAppointments.Any(a => a.PatientName == "Referência WPF " + suffix && a.Status == "cancelled"), "Secretaria não consultou a agenda real da UBS: " + network.Message);
                    // Indicadores da rede com a mesma janela, e histórico com as ações que o próprio fluxo gerou.
                    network.SummaryUnitFilter = Guid.Empty; PumpUntil(() => !network.IsBusy);
                    network.MetricsPeriodDays = 0; network.MetricsFromText = DisplayFormat.Date(DateOnly.FromDateTime(DateTime.Today)); network.MetricsToText = DisplayFormat.Date(DateOnly.FromDateTime(DateTime.Today.AddDays(120)));
                    network.ApplyCustomPeriod(); PumpUntil(() => !network.IsBusy);
                    Require(network.HasMetrics && Card(network, "Cancelamentos").Value == "2" && Card(network, "Consultas concluídas").Value == "0" && network.UnitPerformance.Any(row => row.Name == "UBS Cadastro E2E 1") && network.UnitsWithoutDatabaseCount == 0, "Indicadores da rede não refletiram as consultas reais: " + network.MetricsScopeLabel + " | " + string.Join(" | ", network.OverviewCards.Select(card => card.Title + "=" + card.Value)) + " | UBS: " + string.Join(", ", network.UnitPerformance.Select(row => row.Name)) + " | sem base: " + network.UnitsWithoutDatabaseCount + " | " + network.MetricsStateText + " " + network.Message);
                    // Capacidade da rede (a mesma janela de 120 dias): ofertados = ocorrências do dia da semana menos o dia bloqueado; ocupados = a consulta pelo catálogo.
                    var expectedOffered = Enumerable.Range(0, 121).Count(offset => WeekdayIn(DateTime.Today.AddDays(offset)) == WeekdayIn(targetDay)) - 1;
                    municipal.ViewModel.Navigate("capacity"); PumpUntil(() => !network.IsBusy);
                    var capacityRow = network.CapacityByUnit.SingleOrDefault(row => row.Name == "UBS Cadastro E2E 1") ?? throw new InvalidOperationException("A capacidade da UBS não veio da API real: " + network.CapacityState);
                    Require(capacityRow.Booked == 1 && capacityRow.Offered > 0 && capacityRow.Occupancy is > 0 && network.CapacityByRegion.Any(row => row.Offered >= capacityRow.Offered), $"Capacidade real da rede: {capacityRow.Offered} ofertados, {capacityRow.Booked} ocupados.");
                    if (sameZone) Require(capacityRow.Offered == expectedOffered, $"Ofertados reais {capacityRow.Offered}, esperados {expectedOffered}.");
                    network.CapacityUnit = network.Units.Single(unit => unit.Name == "UBS Cadastro E2E 1"); PumpUntil(() => !network.IsBusy);
                    Require(network.CapacityProfessionals.Any(item => item.FullName == professionalName && item.SpecialtiesDisplay == specialtyName) && network.CapacityRooms.Any(item => item.Name == roomName) && network.CapacitySchedule.Any(item => item.ProfessionalName == professionalName && item.RangeDisplay == "23:00–23:30"), "A Secretaria deveria ler profissionais, consultórios e grade da UBS (API real): " + network.CapacityCatalogState);
                    // Metas reais: define, aparece nos indicadores (sem consulta encerrada = sem dado), remove.
                    municipal.ViewModel.Navigate("reports"); PumpUntil(() => !network.IsBusy);
                    network.GoalEditors.Single(editor => editor.Metric == GoalMetric.Score).Input = "90"; Pump(network.SaveGoalAsync(GoalMetric.Score)); PumpUntil(() => !network.IsBusy);
                    Require(network.GoalEditors.Single(editor => editor.Metric == GoalMetric.Score).HasCurrent && network.GoalUnitRows.Single(row => row.Name == "UBS Cadastro E2E 1").Score is { Status: "no-data" } score && score.Detail.EndsWith("meta mín. 90,0%", StringComparison.Ordinal), "A meta real deveria aparecer nos indicadores como Sem dado (nenhuma consulta encerrada): " + network.Message);
                    Pump(network.RemoveGoalAsync(GoalMetric.Score)); PumpUntil(() => !network.IsBusy);
                    Require(!network.GoalEditors.Single(editor => editor.Metric == GoalMetric.Score).HasCurrent && network.GoalUnitRows.Single(row => row.Name == "UBS Cadastro E2E 1").Score.Status == "none", "Remover a meta real deveria devolver Sem meta: " + network.Message);
                    // Alertas da rede reais.
                    municipal.ViewModel.Navigate("alerts"); PumpUntil(() => !network.IsBusy);
                    Require(network.NetworkAlerts.Any(item => item.UnitName == "UBS Cadastro E2E 1" && item.Title == "Ocupação da grade acima do limite"), "A Secretaria deveria ver o alerta de ocupação da UBS (API real): " + network.NetworkAlertsState);
                    network.AlertsUnitFilter = network.Units.Single(unit => unit.Name == "UBS Cadastro E2E 2").Id; PumpUntil(() => !network.IsBusy);
                    Require(network.NetworkAlerts.All(item => item.UnitName == "UBS Cadastro E2E 2"), "O filtro de UBS dos alertas reais não restringiu a lista."); network.AlertsUnitFilter = Guid.Empty; PumpUntil(() => !network.IsBusy);
                    Pump(network.LoadHistoryAsync());
                    Require(network.HistoryEvents.Any(item => item.Kind == "appointment-read" && item.UnitName == "UBS Cadastro E2E 1") && network.HistoryEvents.Any(item => item.Kind == "staff-granted"), "O histórico não trouxe a consulta à agenda nem as concessões do fixture: " + network.Message);
                    Require(network.HistoryEvents.Count > 0 && network.HistoryEvents.All(item => item.Cursor is { } cursor && cursor.StartsWith("c1_", StringComparison.Ordinal)), "A API real deveria devolver o cursor em cada evento do histórico.");
                    // Desativação real: a prévia conta a consulta futura do catálogo e a entrada de fila; desativar cancela como unidade.
                    vm.SelectedObsRegistry = vm.ObsRegistry.Single(p => p.Email == "unit-" + suffix + "@example.test");
                    var previewTask = vm.PreviewObsDeactivationAsync(); Pump(previewTask);
                    Require(previewTask.Result is { FutureAppointments: 1, ActiveWaitingEntries: 1 }, "A prévia real deveria contar 1 consulta futura e 1 entrada de fila: " + previewTask.Result + " / " + vm.Message);
                    Require(WorkspaceViewModel.DeactivationConfirmationText("Paciente", previewTask.Result!).Contains("1 consulta futura será cancelada"), "O texto da confirmação deveria citar a consulta futura.");
                    Pump(vm.SetObsRegistryActiveAsync(vm.SelectedObsRegistry!.Cpf, false));
                    Require(vm.SelectedObsRegistry?.IsActive == false && vm.Message.Contains("1 consulta futura foi cancelada", StringComparison.Ordinal), "Desativar na API real deveria cancelar a consulta futura: " + vm.Message);
                    network.SelectedUnit = network.Units.Single(u => u.Name == "UBS Cadastro E2E 1"); Pump(network.LoadMunicipalAppointmentsAsync());
                    Require(network.MunicipalAppointments.Single(a => a.Id == catalogAppointment.Id).Status == "cancelled", "A consulta futura deveria estar cancelada na agenda da Secretaria.");
                    network.SelectedUnit = null; Require(network.MunicipalAppointments.Count == 0, "Troca de UBS preservou consultas na Secretaria.");
                    foreach (var window in new[] { obs, municipal })
                    {
                        InvokeAccountAction(window, "Sair da conta"); PumpUntil(() => !window.ViewModel.Workspace.IsBusy);
                        Require(window.ViewModel.Workspace.IsSignedOut, "Logout real não limpou a janela.");
                    }
                    Console.WriteLine("E2E UBS: comunicado da Secretaria lido no OBS, exame, vacina, família, desativação, especialidade, cadastro, agenda, fila, vaga, ofertas, indicadores, profissional, grade, bloqueio, agenda pelo catálogo, consultórios ativos, capacidade, metas, alertas, resumo e histórico conferidos em API/PostgreSQL descartáveis.");
                }
            }
            finally { obs.Close(); municipal.Close(); }
        });
    }

    /// <summary>Aplica um período personalizado (datas inclusivas) nos Indicadores da UBS e espera a leitura terminar.</summary>
    private static void SetUnitPeriod(Nora.Desktop.ViewModels.WorkspaceViewModel vm, DateTime from, DateTime to)
    {
        vm.UnitPeriodDays = 0; vm.UnitFromText = DisplayFormat.Date(DateOnly.FromDateTime(from)); vm.UnitToText = DisplayFormat.Date(DateOnly.FromDateTime(to));
        vm.ApplyUnitCustomPeriod(); PumpUntil(() => !vm.IsBusy);
    }

    private static T Await<T>(Task<T> task) { Pump(task); return task.Result; }
    private static void Await(Task task) => Pump(task);

    /// <summary>Cria a conta de paciente pelo cadastro real (código lido da fixture) e devolve o token de uma sessão de paciente.</summary>
    private static string RegisterE2EPatient(string address, Guid[] unitIds)
    {
        using var http = new System.Net.Http.HttpClient { BaseAddress = NoraApiClient.ValidateEndpoint(address), Timeout = TimeSpan.FromSeconds(20) };
        const string password = "Disposable-patient-registration!29";
        using var start = System.Text.Json.JsonDocument.Parse(Await(Await(http.PostAsJsonAsync("api/registration/start", new { fullName = "Paciente Sintético", cpf = "52998224725", password, unitIds })).Content.ReadAsStringAsync()));
        var challenge = start.RootElement.GetProperty("challengeId").GetGuid();
        var email = start.RootElement.GetProperty("emails").EnumerateArray().First(item => item.GetProperty("maskedEmail").GetString()!.StartsWith("f", StringComparison.Ordinal)).GetProperty("id").GetString();
        Require(Await(http.PostAsJsonAsync("api/registration/send-code", new { challengeId = challenge, emailOptionId = email })).IsSuccessStatusCode, "O envio do código de cadastro falhou.");
        using var codeDocument = System.Text.Json.JsonDocument.Parse(Await(http.GetStringAsync("__checks/registration-code")));
        using var confirm = System.Text.Json.JsonDocument.Parse(Await(Await(http.PostAsJsonAsync("api/registration/confirm", new { challengeId = challenge, code = codeDocument.RootElement.GetProperty("code").GetString() })).Content.ReadAsStringAsync()));
        Require(confirm.RootElement.GetProperty("codeValid").GetBoolean(), "O código de cadastro deveria ser válido.");
        using var login = System.Text.Json.JsonDocument.Parse(Await(Await(http.PostAsJsonAsync("api/auth/login", new { scope = "patient", identifier = "52998224725", password })).Content.ReadAsStringAsync()));
        return login.RootElement.GetProperty("token").GetString()!;
    }
    private static async Task RequestDependentAsync(string address, string token, string cpf, Guid unitId)
    {
        using var http = new System.Net.Http.HttpClient { BaseAddress = NoraApiClient.ValidateEndpoint(address), Timeout = TimeSpan.FromSeconds(20) };
        http.DefaultRequestHeaders.Authorization = new("Bearer", token);
        using var response = await http.PostAsJsonAsync("api/patient/dependents", new { cpf, unitId });
        Require(response.StatusCode == System.Net.HttpStatusCode.Created, "O pedido de família pela API deveria dar 201: " + (int)response.StatusCode);
    }
    private static async Task<string?> ReadDependentStatusAsync(string address, string token, Guid linkId)
    {
        using var http = new System.Net.Http.HttpClient { BaseAddress = NoraApiClient.ValidateEndpoint(address), Timeout = TimeSpan.FromSeconds(20) };
        http.DefaultRequestHeaders.Authorization = new("Bearer", token);
        using var document = System.Text.Json.JsonDocument.Parse(await http.GetStringAsync("api/patient/dependents"));
        return document.RootElement.GetProperty("dependents").EnumerateArray().FirstOrDefault(item => item.GetProperty("id").GetGuid() == linkId).GetProperty("status").GetString();
    }

    private static void LoginFromControls(ShellWindow window, string address, string identifier, string password)
    {
        var content = Layout(window, 980, 640);
        Input(content, "Endereço da API").Text = address;
        Input(content, "E-mail").Text = identifier;
        Descendants<PasswordBox>(content).Single(input => AutomationProperties.GetName(input) == "Senha de acesso").Password = password;
        InvokeButton(FindActionButton(content, "Entrar")); PumpUntil(() => !window.ViewModel.Workspace.IsBusy);
    }
    private static TextBox Input(FrameworkElement content, string name) => PageField<TextBox>(content, input => AutomationProperties.GetName(input) == name);
    private static Button FindActionButton(FrameworkElement content, string name) => PageField<Button>(content, button => button.IsVisible && (button.Content?.ToString() == name || AutomationProperties.GetName(button) == name));
    private static string SyntheticCpf()
    {
        var digits = Random.Shared.NextInt64(100000000, 999999999).ToString();
        for (var count = 9; count <= 10; count++)
        {
            var sum = 0;
            for (var i = 0; i < count; i++) sum += (digits[i] - '0') * (count + 1 - i);
            var digit = sum * 10 % 11;
            digits += digit == 10 ? "0" : digit.ToString();
        }
        return digits;
    }
}
