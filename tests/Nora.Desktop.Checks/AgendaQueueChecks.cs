using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using Nora.Desktop.Models;
using Nora.Desktop.ViewModels;
using Nora.Desktop.Views;

namespace Nora.Desktop.Checks;

internal static partial class Program
{
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
                var days = vm.AgendaDays;
                Require(days.Single(day => day.Date == month.AddDays(1)).AppointmentCount == 2 && days.Single(day => day.Date == month.AddDays(8)).AppointmentCount == 1 && days.Sum(day => day.AppointmentCount) == 3, "Calendário não marcou os dias com consulta e a quantidade.");
                var content = Layout(window, 1280, 740);
                var texts = VisibleTexts(content);
                Require(!texts.Contains("2 consultas") && !texts.Contains("1 consulta") && days.Single(day => day.Date == month.AddDays(1)) is { HasScheduled: true, HasCalled: true } && days.Single(day => day.Date == month.AddDays(8)).HasCancelled && days.Single(day => day.Date == month.AddDays(1)).AutomationLabel.Contains("2 consultas"), "Calendário deveria marcar situações por cor e manter a quantidade acessível.");
                foreach (var status in new[] { "Agendada", "Chamada", "Concluída", "Falta", "Cancelada" }) Require(texts.Contains(status), "Legenda sem o estado real: " + status);
                Require(!texts.Contains("Livre") && !texts.Contains("Ocupado"), "Legenda ainda mostra estado que não corresponde a nenhum dado.");
                SavePreview(content, Path.Combine(previewRoot, "obs-agenda-marcacoes-light.png"));
                var requests = fixture.Count("GET", "/appointments");
                vm.SelectCalendarDayCommand.Execute(days.Single(day => day.Date == month.AddDays(1)));
                Require(vm.IsAgendaDay && vm.Appointments.Count == 2 && vm.Appointments[0].StartsAt < vm.Appointments[1].StartsAt, "Visão diária não listou as consultas do dia em ordem.");
                Require(vm.Appointments[0].PatientDisplay == "Ana Souza · CPF 529.982.247-25", "Lista não mostra o CPF formatado junto do nome: " + vm.Appointments[0].PatientDisplay);
                window.ViewModel.Navigate("agenda"); content = Layout(window, 1280, 740);
                Require(VisibleTexts(content).Contains("Ana Souza") && VisibleTexts(content).Contains("CPF: 529.982.247-25"), "Nome e CPF formatado não aparecem separados na tela.");
                vm.SelectAgendaWeekCommand.Execute(null);
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
                Pump(vm.LoadAppointmentsAsync());
                Require(fixture.Count("GET", "/appointments") == requests + 3, "O botão Atualizar agenda deixou de funcionar.");
            }
            finally { window.ViewModel.ConfirmClose = false; window.Close(); }
        });
        Check("Agenda OBS: botões de situação seguem o estado real da consulta e a vaga só vale para consulta cancelada no futuro", () =>
        {
            using var fixture = new ApiFixture();
            fixture.AddUnit("UBS Botões");
            var window = OpenOperational(fixture, storageRoot, "agenda-buttons", Appearance.Light);
            try
            {
                var vm = LoginObs(window);
                window.ViewModel.Navigate("agenda"); Settle(vm);
                var future = DateTimeOffset.Now.AddDays(2); var past = DateTimeOffset.Now.AddDays(-2);
                var content = Layout(window, 1280, 740);
                string[] names = ["Chamar", "Concluir", "Registrar falta", "Cancelar consulta"];
                void Expect(string label, bool[] expected)
                {
                    content = Layout(window, 1280, 740);
                    var actual = names.Select(name => FindActionButton(content, name).IsEnabled).ToArray();
                    Require(actual.SequenceEqual(expected), $"{label}: botões {string.Join("/", actual)} divergem do esperado {string.Join("/", expected)}.");
                }
                Expect("sem seleção", [false, false, false, false]);
                AppointmentInfo Make(string status, DateTimeOffset start) => new(Guid.NewGuid(), "52998224725", "Ana Souza", "Clínica", "Profissional", start, start.AddMinutes(30), status);
                // Os itens entram na lista exibida para que a seleção da tela os aceite.
                var items = new Dictionary<string, AppointmentInfo> { ["scheduled"] = Make("scheduled", future), ["called"] = Make("called", future), ["completed"] = Make("completed", future), ["missed"] = Make("missed", future), ["cancelled-future"] = Make("cancelled", future), ["cancelled-past"] = Make("cancelled", past) };
                foreach (var item in items.Values) vm.Appointments.Add(item);
                vm.SelectedAppointment = items["scheduled"]; Expect("agendada", [true, false, true, true]);
                vm.SelectedAppointment = items["called"]; Expect("chamada", [false, true, true, true]);
                vm.SelectedAppointment = items["completed"]; Expect("concluída", [false, false, false, false]);
                vm.SelectedAppointment = items["missed"]; Expect("falta", [false, false, false, false]);
                vm.SelectedAppointment = items["cancelled-future"]; Expect("cancelada no futuro", [false, false, false, false]);
                vm.SelectedAppointment = items["cancelled-past"]; Expect("cancelada no passado", [false, false, false, false]);
                vm.SelectedAppointment = null; Expect("seleção limpa", [false, false, false, false]);
                SavePreview(Layout(window, 1280, 740), Path.Combine(previewRoot, "obs-agenda-botoes-light.png"));
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
                vm.SelectAgendaDayCommand.Execute(null); vm.MoveAgenda(1); Settle(vm);
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
                Pump(vm.CreateAppointmentAsync());
                Require(fixture.Count("POST", "/appointments") == 1 && vm.MessageKind == MessageKind.Success && vm.Appointments.Count == 1, "Agendamento válido não foi enviado e exibido: " + vm.Message);
            }
            finally { window.ViewModel.ConfirmClose = false; window.Close(); }
        });
    }
}
