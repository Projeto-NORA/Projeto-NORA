using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Nora.Desktop.Models;
using Nora.Desktop.Services;
using Nora.Desktop.ViewModels;
using Nora.Desktop.Views;

namespace Nora.Desktop.Checks;

internal static partial class Program
{
    private static ListBox AgendaDayList(FrameworkElement content) => Descendants<ListBox>(content).Single(box => AutomationProperties.GetName(box) == "Consultas da UBS");
    private static ListBox AgendaWeekList(FrameworkElement content) => Descendants<ListBox>(content).Single(box => AutomationProperties.GetName(box) == "Consultas da semana");
    private static SelectionBalloon VisibleBalloon(FrameworkElement content) => Descendants<SelectionBalloon>(content).Single(balloon => balloon.IsVisible);

    /// <summary>Seleciona consultas na lista como a pessoa faria (a seleção é a do WPF) e deixa o balão se ajustar.</summary>
    private static void SelectAppointments(ShellWindow window, ListBox list, params AppointmentInfo[] items)
    {
        list.SelectedItems.Clear();
        foreach (var item in items) list.SelectedItems.Add(item);
        window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        window.UpdateLayout();
    }

    /// <summary>Deixa passar o tempo da animação de entrada do pop-up (fade e zoom de ~180 ms) antes de uma captura.</summary>
    private static void WaitForDialogAnimation(Window dialog)
    {
        var end = DateTime.UtcNow.AddMilliseconds(400);
        PumpUntil(() => DateTime.UtcNow > end);
        dialog.UpdateLayout();
    }

    private static string[] VisibleActions(SelectionBalloon balloon) =>
        ((Panel)balloon.Content).Children.OfType<Button>().Where(button => button.Visibility == Visibility.Visible).Select(button => AutomationProperties.GetName(button)).ToArray();

    private static void RunAgendaAndQueueChecks(Application application, string storageRoot, string[] args)
    {
        var previewRoot = PreviewRoot(args);
        Check("Agenda OBS: carrega ao entrar na aba, lê o mês em uma requisição e marca os dias com consulta", () =>
        {
            using var fixture = new ApiFixture();
            var unit = fixture.AddUnit("UBS Agenda");
            var month = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
            DateTimeOffset At(int day, int hour) => new(month.AddDays(day - 1).AddHours(hour));
            fixture.AddAppointment(unit.Id, "scheduled", At(2, 10), "52998224725", "Ana Souza");
            fixture.AddAppointment(unit.Id, "called", At(2, 14), "11144477735", "Bruno Lima");
            fixture.AddAppointment(unit.Id, "cancelled", At(9, 11), "12345678909", "Carla Dias");
            fixture.AddAppointment(unit.Id, "scheduled", new DateTimeOffset(month.AddMonths(2).AddDays(19)));
            var window = OpenOperational(fixture, storageRoot, "agenda-month", Appearance.Light);
            try
            {
                var vm = LoginObs(window);
                var before = fixture.Count("GET", "/appointments");
                window.ViewModel.Navigate("agenda"); Settle(vm);
                Require(fixture.Count("GET", "/appointments") == before + 1, "Entrar na aba Agenda não carregou a agenda sozinha, sem botão.");
                var span = fixture.LastAppointmentRange.To - fixture.LastAppointmentRange.From;
                Require(span.TotalDays is >= 28 and <= 42, $"A leitura deveria cobrir o mês visível (semanas inteiras), mas cobriu {span.TotalDays:F0} dias.");
                Require(vm.IsAgendaMonth && vm.AgendaViewMode == "Mês", "A agenda deveria abrir na visão Mês.");
                var days = vm.AgendaDays;
                Require(days.Single(day => day.Date == month.AddDays(1)).AppointmentCount == 2 && days.Single(day => day.Date == month.AddDays(8)).AppointmentCount == 1 && days.Sum(day => day.AppointmentCount) == 3, "Calendário não marcou os dias com consulta e a quantidade.");
                var content = Layout(window, 1280, 740);
                var texts = VisibleTexts(content);
                Require(!texts.Contains("2 consultas") && !texts.Contains("1 consulta") && days.Single(day => day.Date == month.AddDays(1)) is { HasScheduled: true, HasCalled: true } && days.Single(day => day.Date == month.AddDays(8)).HasCancelled && days.Single(day => day.Date == month.AddDays(1)).AutomationLabel.Contains("2 consultas"), "Calendário deveria marcar situações por cor e manter a quantidade acessível.");
                foreach (var status in new[] { "Agendada", "Chamada", "Concluída", "Falta", "Cancelada" }) Require(texts.Contains(status), "Legenda sem o estado real: " + status);
                Require(!texts.Contains("Livre") && !texts.Contains("Ocupado"), "Legenda ainda mostra estado que não corresponde a nenhum dado.");
                Require(!Descendants<Button>(content).Any(button => button.IsVisible && (AutomationProperties.GetName(button).StartsWith("Atualizar", StringComparison.Ordinal) || button.Content?.ToString() == "Atualizar")), "A Agenda não deveria ter botão Atualizar.");
                SavePreview(content, Path.Combine(previewRoot, "obs-agenda-marcacoes-light.png"));
                var requests = fixture.Count("GET", "/appointments");
                vm.SelectCalendarDayCommand.Execute(days.Single(day => day.Date == month.AddDays(1)));
                Require(vm.IsAgendaMonth && days.Single(day => day.Date == month.AddDays(1)).IsSelected && vm.Appointments.Count == 2 && vm.Appointments[0].StartsAt < vm.Appointments[1].StartsAt, "Clicar no dia do Mês deveria escolher o dia e listar as consultas dele em ordem, sem sair da visão.");
                Require(vm.Appointments[0].PatientDisplay == "Ana Souza · CPF 529.982.247-25", "Lista não mostra o CPF formatado junto do nome: " + vm.Appointments[0].PatientDisplay);
                window.ViewModel.Navigate("agenda"); content = Layout(window, 1280, 740);
                texts = VisibleTexts(content);
                Require(texts.Contains("Ana Souza") && texts.Contains("Bruno Lima") && texts.Count(text => text.Contains(" às ", StringComparison.Ordinal) && text.Length == 14) == 2 && texts.Contains("Agendada") && texts.Contains("Chamada"), "A lista do dia deveria trazer o nome, o horário 'HH:mm às HH:mm' e a situação: " + string.Join(" | ", texts));
                Require(!texts.Any(text => text.StartsWith("CPF:", StringComparison.Ordinal) || text.StartsWith("Marcada por", StringComparison.Ordinal)), "A lista não deveria repetir o que está na ficha.");
                vm.AgendaViewMode = "Semana";
                Require(vm.IsAgendaWeek && vm.Appointments.Count == 2, "Visão semanal não derivou as consultas da semana.");
                vm.MoveAgenda(1);
                Require(vm.Appointments.Count == 1 && vm.Appointments[0].Status == "cancelled", "Semana seguinte não derivou a consulta de dia 9.");
                Require(fixture.Count("GET", "/appointments") == requests, "Trocar dia, modo ou semana dentro do mês fez nova requisição.");
                for (var step = 0; step < 5; step++) vm.MoveAgenda(1);
                Settle(vm);
                Require(fixture.Count("GET", "/appointments") == requests + 1, "Mudar de mês deveria fazer exatamente uma leitura.");
                Require(vm.Appointments.Count == 0 && days.All(day => day.AppointmentCount == 0) && vm.AgendaStateText == "Nenhuma consulta nesta semana.", "Mês sem consultas não limpou a lista e as marcações.");
                for (var step = 0; step < 5; step++) vm.MoveAgenda(-1);
                Settle(vm);
                Require(fixture.Count("GET", "/appointments") == requests + 2 && vm.AgendaDays.Sum(day => day.AppointmentCount) == 3, "Voltar ao mês anterior não recarregou as marcações.");
                Pump(vm.RefreshAutomaticallyAsync());
                Require(fixture.Count("GET", "/appointments") == requests + 3, "O ciclo automático deixou de reler a agenda.");
            }
            finally { window.ViewModel.ConfirmClose = false; window.Close(); }
        });
        Check("Agenda OBS: o balão mostra só as ações que valem para a situação (e para todas as consultas selecionadas) e a lixeira cancela", () =>
        {
            using var fixture = new ApiFixture();
            var unit = fixture.AddUnit("UBS Botões");
            var day = DateTime.Today;
            AppointmentInfo Add(string status, int hour, string name) => fixture.AddAppointment(unit.Id, status, new DateTimeOffset(day.AddHours(hour)), "52998224725", name);
            var scheduled = Add("scheduled", 8, "Ana Agendada"); var called = Add("called", 9, "Bia Chamada"); var completed = Add("completed", 10, "Caio Concluído"); var missed = Add("missed", 11, "Dani Falta"); var cancelled = Add("cancelled", 12, "Edu Cancelado");
            var window = OpenOperational(fixture, storageRoot, "agenda-buttons", Appearance.Light);
            try
            {
                var vm = LoginObs(window);
                window.ViewModel.Navigate("agenda"); Settle(vm);
                var content = Layout(window, 1280, 740);
                var list = AgendaDayList(content);
                Require(Descendants<SelectionBalloon>(content).All(balloon => !balloon.IsVisible), "Sem consulta selecionada não deveria haver balão.");
                AppointmentInfo Item(Guid id) => vm.Appointments.Single(item => item.Id == id);
                void Expect(string label, string[] expected, params AppointmentInfo[] chosen)
                {
                    SelectAppointments(window, list, [.. chosen.Select(item => Item(item.Id))]);
                    content = Layout(window, 1280, 740);
                    var balloon = VisibleBalloon(content);
                    var actual = VisibleActions(balloon);
                    Require(actual.SequenceEqual(expected), $"{label}: ações {string.Join("/", actual)} divergem do esperado {string.Join("/", expected)}.");
                    Require(balloon.HasActions == (expected.Length > 0) && ((Panel)balloon.Content).Children.OfType<Button>().Where(button => button.Visibility == Visibility.Visible).All(button => button.IsVisible && button.ActualWidth > 20), $"{label}: o balão deveria {(expected.Length > 0 ? "desenhar" : "esconder")} a área de ações.");
                }
                Expect("agendada", ["Chamar", "Registrar falta", "Cancelar consulta"], scheduled);
                Expect("chamada", ["Concluir", "Registrar falta", "Cancelar consulta"], called);
                Expect("concluída", [], completed);
                Expect("falta", [], missed);
                Expect("cancelada", [], cancelled);
                Expect("agendada e chamada", ["Registrar falta", "Cancelar consulta"], scheduled, called);
                Expect("agendada e concluída", [], scheduled, completed);
                SelectAppointments(window, list, Item(scheduled.Id));
                content = Layout(window, 1280, 740);
                var balloon = VisibleBalloon(content);
                var danger = ((Panel)balloon.Content).Children.OfType<Button>().Single(button => AutomationProperties.GetName(button) == "Cancelar consulta");
                Require(danger.Style.BasedOn == (Style)application.FindResource("ListActionDangerButton") && danger.IsEnabled && vm.CanCallAppointment && !vm.CanCompleteAppointment && vm.CanMissAppointment && vm.CanCancelAppointment, "A ação de cancelar deveria ser a lixeira de perigo e as flags do ViewModel deveriam seguir a seleção.");
                SavePreview(content, Path.Combine(previewRoot, "obs-agenda-balao-light.png"));
                SelectAppointments(window, list);
                Require(vm.SelectedAppointment is null && !vm.CanCallAppointment && !vm.CanMissAppointment, "Sem seleção as ações deveriam ficar desabilitadas.");
            }
            finally { window.ViewModel.ConfirmClose = false; window.Close(); }
        });
        Check("Agenda OBS: validação local antes de enviar o agendamento", () =>
        {
            using var fixture = new ApiFixture();
            var unit = fixture.AddUnit("UBS Validação");
            fixture.AddRegistry(unit.Id, "Ana Souza", "52998224725"); fixture.AddRegistry(unit.Id, "Bruno Inativo", "11144477735", active: false);
            var window = OpenOperational(fixture, storageRoot, "agenda-validation");
            try
            {
                var vm = LoginObs(window);
                string Attempt(Action configure)
                {
                    vm.AppointmentPatient = vm.ObsRegistry.Single(item => item.Cpf == "52998224725"); vm.AppointmentSpecialty = "Clínica"; vm.AppointmentProfessional = "Profissional"; vm.AppointmentStart = "09:00"; vm.AppointmentEnd = "09:30";
                    configure(); Pump(vm.CreateAppointmentAsync());
                    Require(vm.MessageKind == MessageKind.Error, "Entrada inválida não gerou erro: " + vm.Message);
                    return vm.Message;
                }
                // Hoje às 00:00 já passou; amanhã é futuro.
                Require(Attempt(() => { vm.AppointmentStart = "00:00"; vm.AppointmentEnd = "00:30"; }).Contains("no futuro"), "Início no passado foi aceito.");
                vm.AgendaViewMode = "Dia"; vm.MoveAgenda(1); Settle(vm);
                Require(Attempt(() => { vm.AppointmentStart = "10:00"; vm.AppointmentEnd = "09:00"; }).Contains("depois do inicial"), "Fim antes do início foi aceito.");
                Require(Attempt(() => { vm.AppointmentStart = "10:00"; vm.AppointmentEnd = "10:00"; }).Contains("depois do inicial"), "Fim igual ao início foi aceito.");
                Require(Attempt(() => { vm.AppointmentStart = "08:00"; vm.AppointmentEnd = "20:30"; }).Contains("12 horas"), "Duração acima de 12 h foi aceita.");
                Require(Attempt(() => vm.AppointmentSpecialty = new string('E', 121)).Contains("120"), "Especialidade acima de 120 foi aceita.");
                Require(Attempt(() => vm.AppointmentProfessional = new string('P', 161)).Contains("160"), "Profissional acima de 160 foi aceito.");
                Require(Attempt(() => vm.AppointmentSpecialty = "  ").Contains("especialidade"), "Especialidade vazia foi aceita.");
                Require(Attempt(() => vm.AppointmentStart = "9h").Contains("HH:mm"), "Horário fora do formato foi aceito.");
                Require(Attempt(() => vm.AppointmentPatient = vm.ObsRegistry.Single(item => item.Cpf == "11144477735")).Contains("cadastro ativo"), "Paciente de cadastro inativo foi aceito.");
                Require(Attempt(() => vm.AppointmentPatient = null).Contains("Selecione o paciente"), "Agendamento sem paciente foi aceito.");
                Require(fixture.Count("POST", "/appointments") == 0, "Alguma entrada inválida chegou ao servidor.");
                vm.AppointmentPatient = vm.ObsRegistry.Single(item => item.Cpf == "52998224725"); vm.AppointmentSpecialty = "Clínica"; vm.AppointmentProfessional = "Profissional"; vm.AppointmentStart = "09:00"; vm.AppointmentEnd = "09:30";
                var task = vm.CreateAppointmentAsync(); Pump(task); var created = task.Result;
                Require(created && fixture.Count("POST", "/appointments") == 1 && vm.MessageKind == MessageKind.Success && vm.Appointments.Count == 1, "Agendamento válido não foi enviado e exibido: " + vm.Message);
            }
            finally { window.ViewModel.ConfirmClose = false; window.Close(); }
        });
        RunAgendaViewChecks(application, storageRoot, args);
    }
}
