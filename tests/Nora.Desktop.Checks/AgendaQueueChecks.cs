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
                Require(texts.Contains("2 consultas") && texts.Contains("1 consulta"), "Quantidade de consultas não aparece nos dias do calendário.");
                foreach (var status in new[] { "Agendada", "Chamada", "Concluída", "Falta", "Cancelada" }) Require(texts.Contains(status), "Legenda sem o estado real: " + status);
                Require(!texts.Contains("Livre") && !texts.Contains("Ocupado"), "Legenda ainda mostra estado que não corresponde a nenhum dado.");
                SavePreview(content, Path.Combine(previewRoot, "obs-agenda-marcacoes-light.png"));
                var requests = fixture.Count("GET", "/appointments");
                vm.SelectCalendarDayCommand.Execute(days.Single(day => day.Date == month.AddDays(1)));
                Require(vm.IsAgendaDay && vm.Appointments.Count == 2 && vm.Appointments[0].StartsAt < vm.Appointments[1].StartsAt, "Visão diária não listou as consultas do dia em ordem.");
                Require(vm.Appointments[0].PatientDisplay == "Ana Souza · CPF 529.982.247-25", "Lista não mostra o CPF formatado junto do nome: " + vm.Appointments[0].PatientDisplay);
                window.ViewModel.Navigate("agenda"); content = Layout(window, 1280, 740);
                Require(VisibleTexts(content).Contains("Ana Souza · CPF 529.982.247-25"), "CPF formatado não aparece na tela.");
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
                string[] names = ["Chamar", "Concluir", "Registrar falta", "Cancelar consulta", "Gerar Vaga Relâmpago"];
                void Expect(string label, bool[] expected)
                {
                    content = Layout(window, 1280, 740);
                    var actual = names.Select(name => FindActionButton(content, name).IsEnabled).ToArray();
                    Require(actual.SequenceEqual(expected), $"{label}: botões {string.Join("/", actual)} divergem do esperado {string.Join("/", expected)}.");
                }
                Expect("sem seleção", [false, false, false, false, false]);
                AppointmentInfo Make(string status, DateTimeOffset start) => new(Guid.NewGuid(), "52998224725", "Ana Souza", "Clínica", "Profissional", start, start.AddMinutes(30), status);
                // Os itens entram na lista exibida para que a seleção da tela os aceite.
                var items = new Dictionary<string, AppointmentInfo> { ["scheduled"] = Make("scheduled", future), ["called"] = Make("called", future), ["completed"] = Make("completed", future), ["missed"] = Make("missed", future), ["cancelled-future"] = Make("cancelled", future), ["cancelled-past"] = Make("cancelled", past) };
                foreach (var item in items.Values) vm.Appointments.Add(item);
                vm.SelectedAppointment = items["scheduled"]; Expect("agendada", [true, false, true, true, false]);
                vm.SelectedAppointment = items["called"]; Expect("chamada", [false, true, true, true, false]);
                vm.SelectedAppointment = items["completed"]; Expect("concluída", [false, false, false, false, false]);
                vm.SelectedAppointment = items["missed"]; Expect("falta", [false, false, false, false, false]);
                vm.SelectedAppointment = items["cancelled-future"]; Expect("cancelada no futuro", [false, false, false, false, true]);
                vm.SelectedAppointment = items["cancelled-past"]; Expect("cancelada no passado", [false, false, false, false, false]);
                vm.SelectedAppointment = null; Expect("seleção limpa", [false, false, false, false, false]);
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
        Check("Fila OBS: posição por especialidade, tempo de espera recalculado a cada leitura e cancelamento só de entrada ativa", () =>
        {
            using var fixture = new ApiFixture();
            var unit = fixture.AddUnit("UBS Fila");
            var now = DateTimeOffset.UtcNow;
            fixture.AddWaiting(unit.Id, "52998224725", "Ana Souza", "Cardiologia", now.AddHours(-3).AddSeconds(-30));
            fixture.AddWaiting(unit.Id, "11144477735", "Bruno Lima", "cardiologia", now.AddHours(-2).AddSeconds(-30));
            fixture.AddWaiting(unit.Id, "12345678909", "Carla Dias", "Pediatria", now.AddMinutes(-90).AddSeconds(-30));
            fixture.AddWaiting(unit.Id, "52998224725", "Ana Souza", "Cardiologia", now.AddHours(-5), "cancelled");
            fixture.AddWaiting(unit.Id, "98765432100", "Diego Reis", "Cardiologia", now.AddHours(-1).AddSeconds(-30), "offered");
            fixture.AddWaiting(unit.Id, "12345678909", "Carla Dias", "Cardiologia", now.AddMinutes(-10), "completed");
            var window = OpenOperational(fixture, storageRoot, "queue-position", Appearance.Light);
            try
            {
                var vm = LoginObs(window);
                window.ViewModel.Navigate("queue"); Settle(vm);
                string Row(long id) { var entry = vm.UnitWaitingList.Single(item => item.Id == id); return $"{entry.PositionDisplay}/{entry.WaitDisplay}"; }
                Require(Row(1) == "1º/3h 0min" && Row(2) == "2º/2h 0min" && Row(5) == "3º/1h 0min" && Row(3) == "1º/1h 30min", $"Posição ou espera incorretas: {Row(1)} {Row(2)} {Row(5)} {Row(3)}.");
                Require(Row(4) == "—/—" && Row(6) == "—/—", "Entradas encerradas não deveriam ter posição nem tempo de espera.");
                Require(vm.QueueWaitingCount == "4", "Contagem de aguardando deveria considerar só entradas ativas.");
                var content = Layout(window, 1280, 740);
                var grid = Descendants<DataGrid>(content).Single(item => AutomationProperties.GetName(item) == "Fila por especialidade da UBS");
                Require(grid.Columns.Select(column => column.Header?.ToString()).SequenceEqual(["Posição", "Paciente", "Especialidade", "Espera", "Entrada", "Situação"]), "Colunas da fila divergem do esperado.");
                var firstRow = (DataGridRow)grid.ItemContainerGenerator.ContainerFromIndex(0);
                Require(Descendants<TextBlock>(firstRow).Any(text => text.Text == "1º") && Descendants<TextBlock>(firstRow).Any(text => text.Text == "3h 0min"), "Posição e espera não aparecem na linha.");
                var cancel = FindActionButton(content, "Cancelar entrada");
                Require(!cancel.IsEnabled, "Cancelar entrada habilitado sem seleção.");
                grid.SelectedItem = vm.UnitWaitingList.Single(item => item.Id == 4); Layout(window, 1280, 740);
                Require(!cancel.IsEnabled, "Cancelar entrada habilitado para entrada já encerrada.");
                grid.SelectedItem = vm.UnitWaitingList.Single(item => item.Id == 2); Layout(window, 1280, 740);
                Require(cancel.IsEnabled, "Cancelar entrada desabilitado para entrada ativa.");
                SavePreview(Layout(window, 1280, 740), Path.Combine(previewRoot, "obs-fila-posicao-espera-light.png"));
                // Atualização sem mudança: nada é recriado e a seleção fica.
                var changes = 0; vm.UnitWaitingList.CollectionChanged += (_, _) => changes++;
                Pump(vm.RefreshAutomaticallyAsync());
                Require(changes == 0 && vm.SelectedWaitingEntry?.Id == 2, "Atualização sem mudança recriou a fila ou perdeu a seleção.");
                // O tempo passa no servidor: o tempo de espera é recalculado na leitura, só a linha afetada é trocada e a seleção continua.
                fixture.ReplaceWaiting(unit.Id, 2, item => item with { EnteredAt = item.EnteredAt.AddMinutes(-5) });
                Pump(vm.RefreshAutomaticallyAsync()); Layout(window, 1280, 740);
                Require(Row(2) == "2º/2h 5min" && changes == 1 && vm.SelectedWaitingEntry?.Id == 2 && ReferenceEquals(grid.SelectedItem, vm.SelectedWaitingEntry), $"Espera não foi recalculada preservando a seleção: {Row(2)}, alterações={changes}.");
                // Cancelar a ativa pelo VM: a posição dos demais sobe.
                Pump(vm.CancelUnitWaitingEntryAsync());
                Require(vm.UnitWaitingList.Single(item => item.Id == 5).PositionDisplay == "2º" && vm.UnitWaitingList.Single(item => item.Id == 2).Status == "cancelled", "Cancelamento não reordenou as posições da especialidade.");
                grid.SelectedItem = vm.UnitWaitingList.Single(item => item.Id == 2); Layout(window, 1280, 740);
                Require(!cancel.IsEnabled, "Entrada recém-cancelada ainda pode ser cancelada.");
            }
            finally { window.ViewModel.ConfirmClose = false; window.Close(); }
        });
        Check("Fila e vagas OBS: ficha por duplo clique e Enter; vaga mostra a data de criação", () =>
        {
            using var fixture = new ApiFixture();
            var unit = fixture.AddUnit("UBS Fichas");
            fixture.AddWaiting(unit.Id, "52998224725", "Ana Souza", "Cardiologia", DateTimeOffset.UtcNow.AddHours(-2));
            var start = DateTimeOffset.UtcNow.AddDays(1); var created = DateTimeOffset.UtcNow.AddHours(-3);
            fixture.AddSlot(unit.Id, new UnitLightningSlot(Guid.NewGuid(), Guid.NewGuid(), "Cardiologia", "Dra. Teste", start, start.AddMinutes(30), "open", created));
            var window = OpenOperational(fixture, storageRoot, "ficha-fila-vaga", Appearance.Light);
            try
            {
                var vm = LoginObs(window);
                foreach (var (route, name) in new[] { ("queue", "Fila por especialidade da UBS"), ("slots", "Vagas da UBS") })
                {
                    window.ViewModel.Navigate(route); Settle(vm);
                    var content = Layout(window, 1280, 740);
                    var grid = Descendants<DataGrid>(content).Single(item => AutomationProperties.GetName(item) == name);
                    Require(grid.IsReadOnly && grid.Items.Count == 1, "Listagem sem o item semeado: " + route);
                    var item = grid.Items[0];
                    var expectedTask = vm.BuildDetailsAsync(item); Pump(expectedTask);
                    var expected = expectedTask.Result ?? throw new InvalidOperationException("Item sem ficha: " + route);
                    grid.SelectedItem = item; Layout(window, 1280, 740);
                    var row = (DataGridRow)grid.ItemContainerGenerator.ContainerFromIndex(0);
                    AssertModalDetails(window, expected, () => grid.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = Control.MouseDoubleClickEvent, Source = row }), useEscape: false);
                    AssertModalDetails(window, expected, () => grid.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(grid)!, Environment.TickCount, Key.Enter) { RoutedEvent = Keyboard.PreviewKeyDownEvent }), useEscape: true);
                    if (route == "queue") Require(expected.Fields.Any(field => field.Label == "Posição na especialidade" && field.Value == "1º") && expected.Fields.Any(field => field.Label == "Tempo de espera"), "Ficha da fila sem posição e espera.");
                    else
                    {
                        Require(expected.Fields.Any(field => field.Label == "Criada em" && field.Value == vm.UnitLightningSlots.Single().CreatedAtDisplay), "Ficha da vaga sem a data de criação.");
                        Require(grid.Columns.Any(column => column.Header?.ToString() == "Criada em") && VisibleTexts(grid).Contains(vm.UnitLightningSlots.Single().CreatedAtDisplay), "Listagem da vaga não mostra a data de criação.");
                    }
                    SavePreview(content, Path.Combine(previewRoot, $"obs-{route}-ficha-light.png"));
                }
            }
            finally { window.ViewModel.ConfirmClose = false; window.Close(); }
        });
    }
}
