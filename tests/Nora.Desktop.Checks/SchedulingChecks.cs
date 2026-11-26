using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Nora.Desktop.Models;
using Nora.Desktop.Services;
using Nora.Desktop.ViewModels;
using Nora.Desktop.Views;

namespace Nora.Desktop.Checks;

/// <summary>OBS: configurações da UBS (profissionais, consultórios, grade e bloqueios), agenda por catálogo, presença, Painel, Indicadores e alertas.</summary>
internal static partial class Program
{
    private static DateTimeOffset LocalAt(DateTime day, int hour, int minute = 0) => new(day.Date.AddHours(hour).AddMinutes(minute));
    private static string DateText(DateTime day) => DisplayFormat.Date(DateOnly.FromDateTime(day));

    private static void RunSchedulingChecks(Application application, string storageRoot, string[] args)
    {
        var previewRoot = PreviewRoot(args);
        Check("OBS Configurações da UBS: administrador cadastra profissional, consultório, grade e bloqueio, com validação, aviso de sobreposição no campo e confirmação", () =>
        {
            using var fixture = new ApiFixture();
            var unit = fixture.AddUnit("UBS Config", "Centro");
            fixture.AddSpecialty("Cardiologia"); fixture.AddSpecialty("Pediatria"); fixture.AddSpecialty("Dermatologia", active: false);
            var window = OpenOperational(fixture, storageRoot, "settings-admin", Appearance.Light);
            var asked = new List<string>(); Confirmation.Handler = (_, title, _) => { asked.Add(title); return true; };
            try
            {
                var vm = LoginObs(window);
                Require(fixture.Count("GET", "/professionals") == 1 && fixture.Count("GET", "/rooms") == 0 && fixture.Count("GET", "/schedule") == 0, "A abertura da UBS deveria ler só profissionais e especialidades (base do agendamento).");
                var content = Show(window, "unit-settings", 1280, 3400);
                // Sem botão Atualizar, entrar nas Informações da UBS relê tudo (profissionais inclusive); a leitura do login já tinha trazido os profissionais.
                Require(fixture.Count("GET", "/professionals") == 2 && fixture.Count("GET", "/rooms") == 1 && fixture.Count("GET", "/schedule") == 1 && fixture.Count("GET", "/schedule-blocks") == 1, "Entrar em Informações da UBS deveria ler profissionais, consultórios, grade e bloqueios.");
                Require(vm.CanEditCatalog && !vm.ShowCatalogReadOnlyNotice && window.ViewModel.Page == "unit-settings" && vm.ShowUnitSettings, "O administrador deveria poder editar, sem o aviso de somente leitura.");
                Require(vm.ProfessionalSpecialtyChoices.Select(choice => choice.Name).SequenceEqual(["Cardiologia", "Pediatria"]), "Só as especialidades ativas da rede entram no formulário do profissional.");
                // Profissional: validação antes de enviar.
                Pump(vm.SaveProfessionalAsync());
                Require(vm.MessageKind == MessageKind.Error && vm.ProfessionalNameError.Contains("nome") && vm.ProfessionalSpecialtyError.Contains("Marque") && fixture.Count("POST", "/professionals") == 0, "Profissional sem nome nem especialidade foi enviado.");
                vm.ProfessionalName = "Dra. Ana Lima"; vm.CouncilAcronym = "CRM"; vm.ProfessionalSpecialtyChoices.First(choice => choice.Name == "Cardiologia").IsChecked = true;
                Pump(vm.SaveProfessionalAsync());
                Require(vm.CouncilError.Contains("juntos") && fixture.Count("POST", "/professionals") == 0, "Conselho incompleto (só a sigla) foi enviado.");
                vm.CouncilNumber = "12345"; vm.CouncilState = "sp";
                Pump(vm.SaveProfessionalAsync());
                Require(fixture.Count("POST", "/professionals") == 1 && vm.MessageKind == MessageKind.Success && vm.Professionals.Single() is { CouncilDisplay: "CRM 12345/SP", SpecialtiesDisplay: "Cardiologia", IsActive: true } && vm.ProfessionalName == "", "O profissional válido não foi gravado e listado: " + vm.Message);
                vm.ProfessionalName = "dra. ana lima"; vm.ProfessionalSpecialtyChoices.First(choice => choice.Name == "Pediatria").IsChecked = true;
                Pump(vm.SaveProfessionalAsync());
                Require(vm.ProfessionalNameError.Contains("Já existe") && vm.Professionals.Count == 1, "O nome repetido (409) deveria aparecer no campo do nome: " + vm.ProfessionalNameError);
                vm.NewProfessionalDraft();
                vm.SelectedProfessional = vm.Professionals.Single(); vm.EditSelectedProfessional();
                Require(vm.ProfessionalName == "Dra. Ana Lima" && vm.ProfessionalSpecialtyChoices.Single(choice => choice.IsChecked).Name == "Cardiologia" && vm.ProfessionalDraftTitle == "Editar profissional", "Editar não preencheu o formulário com o profissional.");
                vm.ProfessionalName = "Dra. Ana Lima Souza"; Pump(vm.SaveProfessionalAsync());
                Require(vm.Professionals.Single().FullName == "Dra. Ana Lima Souza" && fixture.Count("PUT", "/professionals/" + vm.Professionals.Single().Id) == 1, "A edição do profissional não foi gravada.");
                // Consultório.
                vm.RoomName = ""; Pump(vm.SaveRoomAsync());
                Require(vm.RoomNameError.Contains("nome") && fixture.Count("POST", "/rooms") == 0, "Consultório sem nome foi enviado.");
                vm.RoomName = "Consultório 1"; Pump(vm.SaveRoomAsync());
                Require(vm.Rooms.Single().Name == "Consultório 1" && vm.Rooms.Single().IsActive, "O consultório não foi gravado.");
                vm.RoomName = "consultório 1"; Pump(vm.SaveRoomAsync());
                Require(vm.RoomNameError.Contains("Já existe") && vm.Rooms.Count == 1, "O nome de consultório repetido (409) deveria aparecer no campo.");
                vm.NewRoomDraft();
                // Grade semanal.
                vm.ScheduleProfessional = vm.ActiveProfessionalOptions.Single(); vm.ScheduleRoom = vm.ActiveRoomOptions.Single();
                Require(vm.ScheduleSpecialty?.Name == "Cardiologia", "A única especialidade do profissional deveria vir escolhida.");
                string Problem(Action configure) { configure(); Pump(vm.SaveScheduleAsync()); return vm.ScheduleError; }
                Require(Problem(() => vm.ScheduleStart = "9h").Contains("HH:mm"), "Horário fora do formato foi aceito.");
                vm.ScheduleStart = "08:00";
                Require(Problem(() => vm.ScheduleEnd = "07:00").Contains("depois do inicial"), "Fim antes do início foi aceito.");
                Require(Problem(() => { vm.ScheduleEnd = "12:00"; vm.ScheduleSlotMinutes = "300"; }).Contains("5 a 240"), "Duração fora de 5 a 240 foi aceita.");
                Require(Problem(() => { vm.ScheduleSlotMinutes = "30"; vm.ScheduleValidFrom = "10/10/2030"; vm.ScheduleValidUntil = "01/10/2030"; }).Contains("vigência inicial"), "Vigência invertida foi aceita.");
                Require(fixture.Count("POST", "/schedule") == 0, "Alguma grade inválida chegou ao servidor.");
                vm.ScheduleValidFrom = ""; vm.ScheduleValidUntil = ""; Pump(vm.SaveScheduleAsync());
                Require(fixture.Count("POST", "/schedule") == 1 && vm.ScheduleRows.Single() is { WeekdayDisplay: "Segunda-feira", RangeDisplay: "08:00–12:00", SlotDisplay: "30 min", ProfessionalName: "Dra. Ana Lima Souza", RoomName: "Consultório 1", SpecialtyName: "Cardiologia", ValidityDisplay: "Sem limite" }, "A grade não foi gravada com os nomes e a duração: " + vm.Message);
                // Sobreposição (409) no campo; encostar não é sobrepor.
                vm.ScheduleProfessional = vm.ActiveProfessionalOptions.Single(); vm.ScheduleRoom = vm.ActiveRoomOptions.Single(); vm.ScheduleStart = "10:00"; vm.ScheduleEnd = "11:00";
                Pump(vm.SaveScheduleAsync());
                Require(vm.ScheduleOverlapError.Contains("Sobreposição") && vm.ScheduleRows.Count == 1 && vm.MessageKind == MessageKind.Error, "A sobreposição (409) não foi avisada no campo: " + vm.ScheduleOverlapError);
                // O aviso aparece junto aos campos, dentro do pop-up da grade.
                OpenUnitForm(window, content, "Novo horário", dialog =>
                {
                    vm.ScheduleProfessional = vm.ActiveProfessionalOptions.Single(); vm.ScheduleRoom = vm.ActiveRoomOptions.Single(); vm.ScheduleStart = "10:00"; vm.ScheduleEnd = "11:00";
                    SubmitUnitForm(dialog, "Salvar horário na grade");
                    Require(Descendants<Border>(dialog).Any(border => AutomationProperties.GetName(border) == "Aviso de sobreposição na grade" && border.IsVisible) && FullTexts(dialog).Contains(vm.ScheduleOverlapError), "O aviso de sobreposição não aparece junto aos campos da grade.");
                    SavePreview(dialog, Path.Combine(previewRoot, "obs-configuracoes-sobreposicao-light.png"));
                });
                vm.ScheduleStart = "12:00"; vm.ScheduleEnd = "13:00"; Pump(vm.SaveScheduleAsync());
                Require(vm.ScheduleOverlapError == "" && vm.ScheduleRows.Count == 2, "Horários que apenas encostam deveriam ser aceitos: " + vm.Message);
                vm.SelectedSchedule = vm.ScheduleRows.Single(row => row.StartTime == "12:00"); vm.EditSelectedSchedule();
                Require(vm.ScheduleStart == "12:00" && vm.ScheduleDraftTitle == "Editar horário da grade", "Editar não preencheu a grade.");
                vm.ScheduleSlotMinutes = "20"; Pump(vm.SaveScheduleAsync());
                Require(vm.ScheduleRows.Count == 2 && vm.ScheduleRows.Single(row => row.StartTime == "12:00").SlotMinutes == 20, "A edição da grade não foi gravada.");
                content = SelectUnitRows<ScheduleTemplate>(window, "Grade semanal da UBS", row => row.StartTime == "12:00", 1280, 3400);
                Click(window, content, "Remover horário", 1280, 3400);
                Require(asked.Contains("Remover horário da grade") && vm.ScheduleRows.Count == 1, "Remover a grade deveria pedir confirmação e remover.");
                // Bloqueios: dia inteiro da UBS (meia-noite local) e intervalo de um profissional.
                var holiday = DateTime.Today.AddDays(30);
                vm.BlockReason = ""; Pump(vm.SaveBlockAsync());
                Require(vm.BlockError.Contains("motivo") && fixture.Count("POST", "/schedule-blocks") == 0, "Bloqueio sem motivo foi enviado.");
                vm.BlockReason = "Feriado municipal"; vm.BlockFromText = "xx"; Pump(vm.SaveBlockAsync());
                Require(vm.BlockError.Contains("dd/mm/aaaa") && fixture.Count("POST", "/schedule-blocks") == 0, "Data fora do formato foi aceita.");
                vm.BlockFromText = DateText(holiday); Pump(vm.SaveBlockAsync());
                var dayBlock = fixture.BlocksOf(unit.Id).Single();
                Require(dayBlock.ProfessionalId is null && dayBlock.StartsAt.LocalDateTime == holiday.Date && dayBlock.EndsAt.LocalDateTime == holiday.Date.AddDays(1), $"O feriado deveria ir da meia-noite local à meia-noite seguinte: {dayBlock.StartsAt:O} a {dayBlock.EndsAt:O}.");
                Require(vm.ScheduleBlocks.Single().ScopeDisplay == "UBS inteira" && vm.ScheduleBlocks.Single().PeriodDisplay == DateText(holiday) + " (dia inteiro)", "O bloqueio do dia inteiro não aparece como tal: " + vm.ScheduleBlocks.Single().PeriodDisplay);
                vm.BlockScope = vm.ActiveProfessionalOptions.Single().Id; vm.BlockFullDay = false; vm.BlockFromText = DateText(holiday.AddDays(1)); vm.BlockStartTime = "10:00"; vm.BlockEndTime = "08:00"; vm.BlockReason = "Reunião";
                Pump(vm.SaveBlockAsync());
                Require(vm.BlockError.Contains("depois do início") && fixture.Count("POST", "/schedule-blocks") == 1, "Fim antes do início foi aceito no bloqueio.");
                vm.BlockEndTime = "12:00"; Pump(vm.SaveBlockAsync());
                var partial = fixture.BlocksOf(unit.Id).Single(block => block.ProfessionalId is not null);
                Require(partial.EndsAt - partial.StartsAt == TimeSpan.FromHours(2) && vm.ScheduleBlocks.Count == 2 && vm.ScheduleBlocks.Any(block => block.ScopeDisplay == "Dra. Ana Lima Souza"), "O bloqueio parcial do profissional não foi gravado.");
                content = SelectUnitRows<ScheduleBlock>(window, "Bloqueios da UBS", block => block.Reason == "Feriado municipal", 1280, 3400);
                Click(window, content, "Remover bloqueio", 1280, 3400);
                Require(asked.Contains("Remover bloqueio") && vm.ScheduleBlocks.Count == 1, "Remover o bloqueio deveria pedir confirmação e remover.");
                // Renderização e temas: as quatro listagens abertas (sem tabela) e sem títulos repetidos.
                content = Layout(window, 1280, 3400);
                Require(new[] { "Profissionais da UBS", "Consultórios da UBS", "Grade semanal da UBS", "Bloqueios da UBS" }.All(name => Descendants<ListBox>(content).Any(list => AutomationProperties.GetName(list) == name && list.IsVisible)) && !Descendants<DataGrid>(content).Any(grid => grid.IsVisible), "Alguma lista das informações da UBS não aparece, ou continua sendo tabela.");
                SaveScrolled(window, Path.Combine(previewRoot, "obs-configuracoes-admin-light.png"), 1280, 900);
                window.ViewModel.SetAppearance(Appearance.Dark); Layout(window, 1280, 3400);
                SaveScrolled(window, Path.Combine(previewRoot, "obs-configuracoes-admin-dark.png"), 1280, 900);
                window.ViewModel.SetAppearance(Appearance.Light);
                // Desativar o último profissional ativo devolve a agenda ao texto livre (com confirmação).
                content = SelectUnitRows<Professional>(window, "Profissionais da UBS", _ => true, 1280, 3400);
                Require(vm.UsesCatalogBooking, "Com profissional ativo a UBS deveria marcar pelo catálogo.");
                Click(window, content, "Desativar profissional", 1280, 3400);
                Require(asked.Contains("Desativar profissional") && !vm.Professionals.Single().IsActive && vm.UsesFreeTextBooking && !vm.UsesCatalogBooking, "Desativar o último profissional deveria voltar ao texto livre.");
            }
            finally { Confirmation.Handler = null; CloseWindow(window); }
        });
        Check("OBS Configurações da UBS: atendente consulta tudo com os controles desabilitados e a explicação; a API recusa a escrita com mensagem própria", () =>
        {
            using var fixture = new ApiFixture { ObsRole = "attendant" };
            var unit = fixture.AddUnit("UBS Atendente", "Sul");
            var cardio = fixture.AddSpecialty("Cardiologia");
            var professional = fixture.AddProfessional(unit.Id, "Dr. Leitura", [cardio.Id], acronym: "CRM", number: "99", state: "RS"); var room = fixture.AddRoom(unit.Id, "Sala A");
            fixture.AddScheduleRow(unit.Id, professional, room, cardio.Id, weekday: 3);
            fixture.AddBlockFor(unit.Id, null, DateTime.Today.AddDays(5), "Folga geral");
            var window = OpenOperational(fixture, storageRoot, "settings-attendant", Appearance.Light);
            try
            {
                var vm = LoginObs(window);
                var content = Show(window, "unit-settings", 1280, 3400);
                Require(!vm.IsAdministrator && !vm.CanEditCatalog && vm.ShowCatalogReadOnlyNotice && FullTexts(content).Contains(vm.CatalogReadOnlyNotice), "O atendente deveria ver o aviso de somente leitura.");
                Require(vm.Professionals.Count == 1 && vm.Rooms.Count == 1 && vm.ScheduleRows.Count == 1 && vm.ScheduleBlocks.Count == 1, "O atendente deveria ler profissionais, consultórios, grade e bloqueios.");
                foreach (var label in new[] { "Novo profissional", "Novo consultório", "Novo horário", "Novo bloqueio" })
                    Require(!FindActionButton(content, label).IsEnabled, "Controle de edição habilitado para o atendente: " + label);
                content = SelectUnitRows<Professional>(window, "Profissionais da UBS", _ => true, 1280, 3400);
                Require(!FindActionButton(content, "Editar profissional").IsEnabled && !FindActionButton(content, "Desativar profissional").IsEnabled, "Editar e desativar deveriam ficar desabilitados para o atendente.");
                vm.ProfessionalName = "Tentativa"; Pump(vm.SaveProfessionalAsync());
                Require(vm.MessageKind == MessageKind.Error && vm.Message.Contains("administrador da UBS") && fixture.Count("POST", "/professionals") == 0, "A escrita do atendente deveria ser recusada antes de sair do cliente: " + vm.Message);
                SaveScrolled(window, Path.Combine(previewRoot, "obs-configuracoes-atendente-light.png"), 1280, 900);
                // Concessão trocada no meio da sessão: o servidor responde 403 e o cliente explica sem exibir o corpo do servidor.
                fixture.ObsRole = "administrator";
                var adminWindow = OpenOperational(fixture, storageRoot, "settings-demoted", Appearance.Light);
                try
                {
                    var adminVm = LoginObs(adminWindow); Require(adminVm.CanEditCatalog, "A conta administradora deveria poder editar.");
                    fixture.ObsRole = "attendant";
                    adminVm.RoomName = "Sala B"; Pump(adminVm.SaveRoomAsync());
                    Require(adminVm.MessageKind == MessageKind.Error && adminVm.Message.Contains("Somente o administrador da UBS") && !adminVm.Message.Contains("private-server-secret"), "O 403 do servidor deveria virar a explicação do perfil: " + adminVm.Message);
                }
                finally { CloseWindow(adminWindow); }
            }
            finally { CloseWindow(window); }
        });
        Check("Agenda OBS: com profissional ativo marca pelo catálogo (especialidade → profissional → horário livre), uma requisição por mudança, e cada 409 tem mensagem própria", () =>
        {
            using var fixture = new ApiFixture();
            var unit = fixture.AddUnit("UBS Catálogo", "Centro");
            var cardio = fixture.AddSpecialty("Cardiologia"); var clinica = fixture.AddSpecialty("Clínica geral");
            var ana = fixture.AddProfessional(unit.Id, "Dra. Ana", [cardio.Id, clinica.Id]); var bruno = fixture.AddProfessional(unit.Id, "Dr. Bruno", [clinica.Id]); fixture.AddProfessional(unit.Id, "Dr. Inativo", [cardio.Id], active: false);
            var room = fixture.AddRoom(unit.Id, "Consultório 1");
            fixture.AddRegistry(unit.Id, "Paciente Um", "52998224725"); fixture.AddRegistry(unit.Id, "Paciente Dois", "11144477735");
            var day = DateTime.Today.AddDays(3);
            fixture.AddAvailableSlot(unit.Id, ana, cardio, room, LocalAt(day, 9)); fixture.AddAvailableSlot(unit.Id, ana, cardio, room, LocalAt(day, 9, 30)); fixture.AddAvailableSlot(unit.Id, ana, cardio, room, LocalAt(day, 10));
            fixture.AddAvailableSlot(unit.Id, ana, clinica, room, LocalAt(day, 14)); fixture.AddAvailableSlot(unit.Id, bruno, clinica, room, LocalAt(day, 11));
            var window = OpenOperational(fixture, storageRoot, "booking-catalog", Appearance.Light);
            try
            {
                var vm = LoginObs(window);
                var content = Show(window, "agenda", 1280, 1600);
                Require(vm.UsesCatalogBooking && !vm.UsesFreeTextBooking && fixture.AvailabilityQueries.Count == 0, "Sem escolhas, nenhum horário deveria ter sido buscado.");
                Require(vm.BookingSpecialties.Select(item => item.Name).SequenceEqual(["Cardiologia", "Clínica geral"]), "As especialidades do agendamento deveriam ser as que algum profissional ativo atende.");
                InspectBooking(window, dialog =>
                {
                    Require(Descendants<TextBox>(dialog).All(box => AutomationProperties.GetName(box) is not ("Horário inicial da consulta" or "Horário final da consulta" or "Profissional da consulta") || !box.IsVisible), "O texto livre não deveria aparecer numa UBS com profissional ativo.");
                    Require(Descendants<ComboBox>(dialog).Count(box => box.IsVisible) == 3 && Descendants<ListBox>(dialog).Any(box => AutomationProperties.GetName(box) == "Horários livres" && box.IsVisible), "O pop-up do catálogo deveria ter paciente, especialidade, profissional e a lista de horários livres.");
                });
                int Requests() => fixture.AvailabilityQueries.Count;
                var before = Requests();
                vm.BookingSpecialty = vm.BookingSpecialties.Single(item => item.Name == "Cardiologia"); Settle(vm);
                Require(vm.BookingProfessionals.Select(item => item.FullName).SequenceEqual(["Dra. Ana"]) && vm.BookingProfessional?.FullName == "Dra. Ana" && Requests() == before + 1, "Escolher a especialidade com um só profissional deveria escolhê-lo e buscar horários uma vez.");
                var query = fixture.AvailabilityQueries[^1].Query;
                Require(query["specialtyId"] == cardio.Id.ToString() && query["professionalId"] == ana.Id.ToString() && WindowDays(query) == 14, "A busca deveria enviar especialidade, profissional e 14 dias.");
                Require(vm.BookingSlots.Count == 3 && vm.BookingSlots[0].Display.EndsWith("Consultório 1", StringComparison.Ordinal), "Os horários livres não foram listados com o consultório.");
                vm.BookingSpecialty = vm.BookingSpecialties.Single(item => item.Name == "Clínica geral"); Settle(vm);
                Require(vm.BookingProfessionals.Count == 2 && vm.BookingProfessional is null && vm.BookingSlots.Count == 0 && Requests() == before + 1 && vm.BookingState.Contains("Escolha o profissional"), "Com dois profissionais nada deveria ser buscado antes de escolher um.");
                vm.BookingProfessional = vm.BookingProfessionals.Single(item => item.FullName == "Dr. Bruno"); Settle(vm);
                Require(Requests() == before + 2 && vm.BookingSlots.Single().StartsAt == LocalAt(day, 11), "Escolher o profissional deveria buscar os horários dele (uma requisição).");
                vm.BookingProfessional = vm.BookingProfessionals.Single(item => item.FullName == "Dr. Bruno"); Settle(vm);
                Require(Requests() == before + 2, "Reescolher o mesmo profissional não deveria buscar de novo.");
                vm.BookingSpecialty = vm.BookingSpecialties.Single(item => item.Name == "Cardiologia"); Settle(vm);
                var beforeDay = Requests();
                vm.SelectCalendarDayCommand.Execute(new AgendaCalendarDay(day.Date, true)); Settle(vm);
                Require(Requests() == beforeDay + 1 && DateTimeOffset.Parse(fixture.AvailabilityQueries[^1].Query["from"]!, CultureInfo.InvariantCulture).LocalDateTime == day.Date, "Trocar o dia deveria buscar uma vez, a partir do dia escolhido.");
                Require(vm.BookingWindowLabel.Contains(DateText(day)), "O rótulo da janela não mostra o dia escolhido.");
                // Marcar: corpo com professionalId e specialtyId; o horário sai da lista.
                InspectBooking(window, dialog => { WaitForDialogAnimation(dialog); SavePreview(dialog, Path.Combine(previewRoot, "obs-agenda-catalogo-light.png")); });
                vm.AppointmentPatient = vm.ObsRegistry.Single(item => item.Cpf == "52998224725"); vm.BookingSlot = vm.BookingSlots[0];
                var beforeBook = Requests(); Pump(vm.CreateAppointmentAsync()); Settle(vm);
                var body = fixture.AppointmentBodies.Last();
                Require(vm.MessageKind == MessageKind.Success && body.GetProperty("professionalId").GetString() == ana.Id.ToString() && body.GetProperty("specialtyId").GetString() == cardio.Id.ToString(), "A marcação pelo catálogo deveria enviar professionalId e specialtyId: " + vm.Message);
                Require(fixture.AppointmentsOf(unit.Id).Count == 1 && fixture.AppointmentsOf(unit.Id)[0].ProfessionalId == ana.Id && vm.BookingSlots.Count == 2 && vm.BookingSlot is null && Requests() == beforeBook + 1, "Depois de marcar, o horário deveria sair da lista (uma busca) e a consulta existir.");
                // 409 slot-unavailable: horário ocupado no meio do caminho → mensagem própria e a lista é lida de novo.
                vm.AppointmentPatient = vm.ObsRegistry.Single(item => item.Cpf == "11144477735"); vm.BookingSlot = vm.BookingSlots[0]; fixture.NextBookingConflict = "slot-unavailable"; var beforeConflict = Requests();
                Pump(vm.CreateAppointmentAsync()); Settle(vm);
                Require(vm.MessageKind == MessageKind.Error && vm.Message.Contains("não está mais livre") && Requests() == beforeConflict + 1 && fixture.AppointmentsOf(unit.Id).Count == 1, "O horário ocupado deveria ter mensagem própria e reler os horários: " + vm.Message);
                // 409 patient-overlap: o paciente já tem consulta que cruza o horário.
                var overlapping = vm.BookingSlots[0]; fixture.AddAppointment(unit.Id, "scheduled", overlapping.StartsAt.AddMinutes(-10), "11144477735", "Paciente Dois");
                vm.AppointmentPatient = vm.ObsRegistry.Single(item => item.Cpf == "11144477735"); vm.BookingSlot = overlapping; Pump(vm.CreateAppointmentAsync()); Settle(vm);
                Require(vm.MessageKind == MessageKind.Error && vm.Message.Contains("já tem uma consulta que cruza"), "A consulta no mesmo horário deveria ter mensagem própria: " + vm.Message);
                // catalog-required: o catálogo mudou; ele é lido de novo.
                var catalogReads = fixture.Count("GET", "/professionals"); fixture.NextBookingConflict = "catalog-required"; vm.BookingSlot = vm.BookingSlots[0];
                Pump(vm.CreateAppointmentAsync()); Settle(vm);
                Require(vm.Message.Contains("pelo catálogo") && fixture.Count("GET", "/professionals") == catalogReads + 1, "catalog-required deveria explicar e reler o catálogo: " + vm.Message);
                // Validações locais.
                vm.AppointmentPatient = null; Pump(vm.CreateAppointmentAsync()); Require(vm.Message.Contains("Selecione o paciente"), "Sem paciente a marcação foi aceita.");
                vm.AppointmentPatient = vm.ObsRegistry.First(); vm.BookingSlot = null; Pump(vm.CreateAppointmentAsync()); Require(vm.Message.Contains("Escolha um horário livre"), "Sem horário escolhido a marcação foi aceita.");
                InspectBooking(window, dialog =>
                {
                    Require(Descendants<Button>(dialog).Any(button => button.IsVisible && button.Content?.ToString() == "Agendar") && Descendants<ListBox>(dialog).Any(box => AutomationProperties.GetName(box) == "Horários livres" && box.IsVisible), "O agendamento pelo catálogo não aparece.");
                    window.ViewModel.SetAppearance(Appearance.Dark); WaitForDialogAnimation(dialog); SavePreview(dialog, Path.Combine(previewRoot, "obs-agenda-catalogo-dark.png")); window.ViewModel.SetAppearance(Appearance.Light);
                });
            }
            finally { CloseWindow(window); }
        });
        Check("Agenda OBS: sem profissional ativo mantém o texto livre; ao surgir profissional passa ao catálogo; leitura do catálogo falha → agendamento indisponível sem derrubar a UBS", () =>
        {
            using var fixture = new ApiFixture();
            var unit = fixture.AddUnit("UBS Transição", "Centro"); fixture.AddRegistry(unit.Id, "Paciente Um", "52998224725");
            var window = OpenOperational(fixture, storageRoot, "booking-switch", Appearance.Light);
            try
            {
                var vm = LoginObs(window);
                var content = Show(window, "agenda", 1280, 1600);
                InspectBooking(window, dialog =>
                {
                    Require(vm.UsesFreeTextBooking && !vm.UsesCatalogBooking && Descendants<ComboBox>(dialog).Single(box => AutomationProperties.GetName(box) == "Especialidade da consulta").IsVisible && Descendants<Button>(dialog).Any(button => button.IsVisible && button.Content?.ToString() == "Agendar"), "Sem profissional ativo o texto livre deveria aparecer.");
                    Require(Descendants<ComboBox>(dialog).All(box => AutomationProperties.GetName(box) != "Especialidade da consulta pelo catálogo" || !box.IsVisible), "O catálogo não deveria aparecer sem profissional ativo.");
                    WaitForDialogAnimation(dialog); SavePreview(dialog, Path.Combine(previewRoot, "obs-agenda-texto-livre-light.png"));
                });
                var cardio = fixture.AddSpecialty("Cardiologia"); fixture.AddProfessional(unit.Id, "Dra. Nova", [cardio.Id]);
                vm.RefreshCommand.Execute(null); Settle(vm);
                InspectBooking(window, dialog => Require(vm.UsesCatalogBooking && !vm.UsesFreeTextBooking && !Descendants<ComboBox>(dialog).Any(box => AutomationProperties.GetName(box) == "Especialidade da consulta" && box.IsVisible), "Com profissional ativo só o catálogo deveria valer."));
                fixture.FailCatalogUnits.Add(unit.Id); vm.RefreshCommand.Execute(null); Settle(vm);
                InspectBooking(window, dialog =>
                {
                    Require(vm.BookingCatalogUnavailable && !vm.CanBook && vm.HasActiveObsUnit && vm.ObsRegistry.Count == 1 && FullTexts(dialog).Any(text => text.Contains("indisponível", StringComparison.Ordinal)), "A falha do catálogo deveria explicar e manter a UBS aberta.");
                    WaitForDialogAnimation(dialog); SavePreview(dialog, Path.Combine(previewRoot, "obs-agenda-catalogo-indisponivel-light.png"));
                });
                vm.AppointmentPatient = vm.ObsRegistry.Single(); Pump(vm.CreateAppointmentAsync());
                Require(vm.MessageKind == MessageKind.Error && vm.Message.Contains("cadastro de profissionais") && fixture.Count("POST", "/appointments") == 0, "Sem o catálogo a marcação não deveria sair: " + vm.Message);
            }
            finally { CloseWindow(window); }
        });
        Check("Agenda OBS: a ficha da consulta mostra quem marcou (equipe, paciente pelo aplicativo, Vaga Relâmpago) e a confirmação de presença nas próximas 48 h, e a lista não repete esses dados", () =>
        {
            using var fixture = new ApiFixture();
            var unit = fixture.AddUnit("UBS Presença", "Centro");
            var now = DateTimeOffset.Now;
            AppointmentInfo Make(string status, DateTimeOffset start, string by, DateTimeOffset? confirmed, string patient) => new(Guid.NewGuid(), "52998224725", patient, "Clínica", "Dra. Ana", start, start.AddMinutes(30), status, confirmed, by);
            var unconfirmed = Make("scheduled", now.AddHours(20), "staff", null, "Sem Confirmação");
            var confirmedToday = Make("scheduled", now.AddHours(30), "patient", now.AddSeconds(-5), "Confirmada Hoje");
            var confirmedBefore = Make("scheduled", now.AddHours(30), "lightning", now.AddDays(-1), "Confirmada Ontem");
            var farAway = Make("scheduled", now.AddDays(6), "staff", null, "Longe Demais");
            var done = Make("completed", now.AddHours(-3), "patient", now.AddDays(-1), "Já Concluída");
            Require(unconfirmed.ConfirmationDisplay == "Sem confirmação" && unconfirmed.HasConfirmationInfo && !unconfirmed.IsConfirmed && unconfirmed.BookedByDisplay == "Equipe da UBS", "Consulta próxima sem confirmação deveria dizer Sem confirmação.");
            Require(confirmedToday.ConfirmationDisplay == $"Presença confirmada às {confirmedToday.ConfirmedAt!.Value.ToLocalTime():HH:mm}" && confirmedToday.IsConfirmed && confirmedToday.BookedByDisplay == "Paciente pelo aplicativo", "A confirmação de hoje deveria mostrar o horário: " + confirmedToday.ConfirmationDisplay);
            Require(confirmedBefore.ConfirmationDisplay.StartsWith("Presença confirmada em ", StringComparison.Ordinal) && confirmedBefore.BookedByDisplay == "Vaga Relâmpago", "A confirmação de outro dia deveria trazer a data: " + confirmedBefore.ConfirmationDisplay);
            Require(farAway.ConfirmationDisplay == "" && !farAway.HasConfirmationInfo && done.ConfirmationDisplay == "", "Fora das próximas 48 h e sem ser agendada não há o que informar.");
            var window = OpenOperational(fixture, storageRoot, "presence", Appearance.Light);
            try
            {
                var vm = LoginObs(window);
                var content = Show(window, "agenda", 1280, 1400);
                foreach (var item in new[] { unconfirmed, confirmedToday, confirmedBefore, farAway }) vm.Appointments.Add(item);
                content = Layout(window, 1280, 1400);
                var list = Descendants<ListBox>(content).Single(box => AutomationProperties.GetName(box) == "Consultas da UBS");
                var texts = VisibleTexts(list);
                Require(texts.Contains("Sem Confirmação") && texts.Contains("Confirmada Hoje") && !texts.Any(text => text.StartsWith("Marcada por", StringComparison.Ordinal) || text.StartsWith("Presença confirmada", StringComparison.Ordinal) || text == "Sem confirmação"), "A lista só traz nome, horário e situação; quem marcou e a confirmação ficam na ficha: " + string.Join(" | ", texts));
                var details = vm.BuildDetails(unconfirmed)!;
                Require(details.Fields.Single(field => field.Label == "Marcada por").Value == "Equipe da UBS" && details.Fields.Single(field => field.Label == "Confirmação de presença").Value == "Sem confirmação" && details.Title == "Sem Confirmação", "A ficha da consulta não traz quem marcou e a confirmação.");
                Require(vm.BuildDetails(farAway)!.Fields.Single(field => field.Label == "Confirmação de presença").Value.StartsWith("Sem informação", StringComparison.Ordinal), "Fora da janela a ficha deveria explicar a ausência da informação.");
                Require(vm.BuildDetails(confirmedToday)!.Fields.Single(field => field.Label == "Marcada por").Value == "Paciente pelo aplicativo" && vm.BuildDetails(confirmedToday)!.Fields.Single(field => field.Label == "Confirmação de presença").Value == confirmedToday.ConfirmationDisplay, "A ficha da consulta confirmada pelo paciente está errada.");
                Require(vm.BuildDetails(confirmedBefore)!.Fields.Single(field => field.Label == "Marcada por").Value == "Vaga Relâmpago" && vm.BuildDetails(confirmedBefore)!.Fields.Single(field => field.Label == "Confirmação de presença").Value.StartsWith("Presença confirmada em ", StringComparison.Ordinal), "A ficha da Vaga Relâmpago confirmada está errada.");
                // A ficha em linhas mostra as três seções com os textos (o ícone antigo não existe mais: o estado é dito em palavras).
                foreach (var item in new[] { unconfirmed, confirmedToday })
                    InspectModal<ItemDetailsWindow>(window, dialog =>
                    {
                        var shown = FullTexts(dialog);
                        Require(new[] { "Paciente", "Consulta", "Registro", "Marcada por", item.BookedByDisplay, "Confirmação de presença", item.ConfirmationDisplay, "Situação", "Agendada" }.All(shown.Contains), "A ficha não mostra as seções e os textos da consulta: " + string.Join(" | ", shown));
                        WaitForDialogAnimation(dialog); SavePreview(dialog, Path.Combine(previewRoot, item == unconfirmed ? "obs-agenda-ficha-light.png" : "obs-agenda-ficha-confirmada-light.png"));
                        if (item == unconfirmed) { window.ViewModel.SetAppearance(Appearance.Dark); dialog.UpdateLayout(); SavePreview(dialog, Path.Combine(previewRoot, "obs-agenda-ficha-dark.png")); window.ViewModel.SetAppearance(Appearance.Light); }
                    }, () => window.OpenItemDetails(item));
            }
            finally { CloseWindow(window); }
        });
    }

    /// <summary>Abre o pop-up "Agendar nova consulta" pelo botão do cabeçalho e roda <paramref name="inspect"/> dentro dele; o pop-up fecha sem salvar.</summary>
    private static void InspectBooking(ShellWindow window, Action<FormDialog> inspect)
    {
        var content = Layout(window, 1280, 900);
        InspectModal<FormDialog>(window, dialog => { Require(dialog.Title == "Agendar nova consulta", "O pop-up deveria se chamar Agendar nova consulta."); inspect(dialog); }, () => InvokeButton(FindActionButton(content, "Agendar nova consulta")));
        window.ViewModel.Workspace.DismissMessageCommand.Execute(null);
    }

}
