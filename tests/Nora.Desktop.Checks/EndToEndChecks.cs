using System.IO;





using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Nora.Desktop.Models;
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
        Check("E2E por UBS: OBS (login → UBS → cadastro de referência com participação → fila → agenda → vaga e ofertas → Painel e indicadores) e Secretaria (resumo → indicadores → histórico) → menu logout", () =>
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
                    Pump(vm.LoadObsUnitsAsync());
                    vm.CandidateObsUnit = vm.ObsUnits.Single(u => u.Name == "UBS Cadastro E2E 1");
                    Pump(vm.ConfirmObsUnitAsync());
                    Require(vm.ObsRegistry.Any(p => p.Email == "first@example.test"), "OBS não consultou o cadastro real da UBS: " + vm.Message);
                    vm.ObsRegistryName = "Referência WPF " + suffix; vm.ObsRegistryCpf = SyntheticCpf(); vm.ObsRegistryEmail = "unit-" + suffix + "@example.test"; vm.ObsRegistryPhone = "(48) 99999-0028";
                    vm.ObsRegistryCep = "01001000"; vm.ObsRegistryStreet = "Praça da Sé"; vm.ObsRegistryCity = "São Paulo"; vm.ObsRegistryState = "SP";
                    Pump(vm.CreateObsRegistryAsync());
                    Require(vm.ObsRegistry.Any(p => p.Email == "unit-" + suffix + "@example.test" && p.Phone == "48999990028" && p.Address?.Cep == "01001000"), "OBS não gravou cadastro real com contato e endereço na UBS: " + vm.Message);
                    vm.SelectedObsRegistry = vm.ObsRegistry.Single(p => p.Email == "unit-" + suffix + "@example.test");
                    Pump(vm.SetObsRegistryActiveAsync()); Require(vm.SelectedObsRegistry?.IsActive == false, "OBS não desativou o paciente na UBS: " + vm.Message);
                    Pump(vm.SetObsRegistryActiveAsync()); Require(vm.SelectedObsRegistry?.IsActive == true, "OBS não reativou o paciente na UBS: " + vm.Message);
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
                    vm.WaitingPatient = vm.ObsRegistry.Single(p => p.Email == "unit-" + suffix + "@example.test"); vm.WaitingSpecialty = "Clínica";
                    Pump(vm.AddUnitWaitingEntryAsync());
                    Require(vm.UnitWaitingList.Any(item => item.PatientName == "Referência WPF " + suffix && item.Status == "waiting"), "OBS não incluiu paciente na fila da UBS: " + vm.Message);
                    Pump(vm.CreateUnitLightningSlotAsync());
                    Require(vm.UnitLightningSlots.Any(item => item.AppointmentId == consultation!.Id && item.Status == "open"), "OBS não criou vaga a partir da consulta cancelada: " + vm.Message);
                    vm.RefreshCommand.Execute(null); PumpUntil(() => !vm.IsBusy);
                    Require(vm.TodayScheduledCount != "—" && vm.TodayCompletedCount != "—" && vm.QueueWaitingCount == "2" && vm.SlotOpenCount == "2", "Painel não atualizou os dados da UBS pela API real: " + vm.Message);
                    // Painel: espera média e série de 7 dias vêm da API real (nenhuma oferta foi aceita, então a espera é —); participação vem junto do cadastro.
                    Require(vm.HasActiveObsUnit && vm.PanelChart.Days.Count == 7 && vm.PanelWaitValue == "—" && vm.PanelWaitHint.Contains("Nenhuma entrada saiu da fila"), $"Painel não leu os indicadores da UBS pela API real: {vm.PanelWaitValue} / {vm.PanelWaitHint}");
                    Require(vm.ObsRegistry.All(p => p.Attendance is not null) && vm.ObsRegistry.Single(p => p.Email == "unit-" + suffix + "@example.test").Attendance is { Completed: 0, Missed: 0, CancelledByPatient: 0, Score: null }, "O cadastro de referência não trouxe a participação de cada paciente (nula sem consulta encerrada).");
                    Require(vm.ObsRegistry.Single(p => p.Email == "unit-" + suffix + "@example.test").ParticipationDisplay == "—", "Participação sem dado deveria aparecer como —.");
                    // Ficha da vaga: as ofertas vêm da rota real.
                    var offersTask = vm.BuildDetailsAsync(vm.UnitLightningSlots.First(item => item.AppointmentId == consultation!.Id)); Pump(offersTask);
                    Require(offersTask.Result?.Fields.Any(field => field.Group == "Ofertas da vaga") == true, "A ficha da vaga não leu as ofertas pela API real: " + vm.Message);
                    // Indicadores da UBS: janela personalizada de hoje a 120 dias (a consulta da agenda está no futuro).
                    SetUnitPeriod(vm, DateTime.Today, DateTime.Today.AddDays(120));
                    Require(vm.HasUnitMetrics && UnitCard(vm, "Cancelamentos").Value == "2" && UnitCard(vm, "Cancelamentos").Hint == "0 feitos pelo paciente" && UnitCard(vm, "Consultas concluídas").Value == "0" && UnitCard(vm, "Comparecimento").Value == "—", "Indicadores da UBS não refletiram as consultas reais: " + string.Join(" | ", vm.UnitOverviewCards.Select(card => card.Title + "=" + card.Value)) + " " + vm.Message);
                    Require(vm.UnitLightningCards.Single(card => card.Title == "Vagas registradas").Value == "2" && vm.UnitSpecialtyPerformance.Any(row => row.Name == "Cardiologia") && vm.UnitChart.Rows.Count >= 2 && vm.UnitChart.Days.Count == 121, $"Vagas, especialidades ou série dos indicadores da UBS incorretas: {vm.UnitChart.Rows.Count} dias com dado, {vm.UnitChart.Days.Count} no eixo.");
                    LoginFromControls(municipal, address, identifier, password);
                    var network = municipal.ViewModel.Workspace;
                    Require(network.PatientCount == "3" && network.WaitingCount == "2", "Resumo da Secretaria não agregou a rede sem duplicar paciente: " + network.Message);
                    Pump(network.LoadUnitsAsync()); network.SummaryUnitFilter = network.Units.Single(u => u.Name == "UBS Cadastro E2E 2").Id;
                    PumpUntil(() => !network.IsBusy);
                    Require(network.PatientCount == "1" && network.WaitingCount == "0" && network.CreateSummaryCsv().Contains("UBS Cadastro E2E 2"), "Filtro de UBS ou escopo do CSV não refletiu a unidade: " + network.Message);
                    network.SelectedUnit = network.Units.Single(u => u.Name == "UBS Cadastro E2E 1");
                    Pump(network.LoadMunicipalAppointmentsAsync());
                    Require(network.MunicipalAppointments.Any(a => a.PatientName == "Referência WPF " + suffix && a.Status == "cancelled"), "Secretaria não consultou a agenda real da UBS: " + network.Message);
                    // Indicadores da rede com a mesma janela, e histórico com as ações que o próprio fluxo gerou.
                    network.SummaryUnitFilter = Guid.Empty; PumpUntil(() => !network.IsBusy);
                    network.MetricsPeriodDays = 0; network.MetricsFromText = DisplayFormat.Date(DateOnly.FromDateTime(DateTime.Today)); network.MetricsToText = DisplayFormat.Date(DateOnly.FromDateTime(DateTime.Today.AddDays(120)));
                    network.ApplyCustomPeriod(); PumpUntil(() => !network.IsBusy);
                    Require(network.HasMetrics && Card(network, "Cancelamentos").Value == "2" && Card(network, "Consultas concluídas").Value == "0" && network.UnitPerformance.Any(row => row.Name == "UBS Cadastro E2E 1") && network.UnitsWithoutDatabaseCount == 0, "Indicadores da rede não refletiram as consultas reais: " + network.MetricsScopeLabel + " | " + string.Join(" | ", network.OverviewCards.Select(card => card.Title + "=" + card.Value)) + " | UBS: " + string.Join(", ", network.UnitPerformance.Select(row => row.Name)) + " | sem base: " + network.UnitsWithoutDatabaseCount + " | " + network.MetricsStateText + " " + network.Message);
                    Pump(network.LoadHistoryAsync());
                    Require(network.HistoryEvents.Any(item => item.Kind == "appointment-read" && item.UnitName == "UBS Cadastro E2E 1") && network.HistoryEvents.Any(item => item.Kind == "staff-granted"), "O histórico não trouxe a consulta à agenda nem as concessões do fixture: " + network.Message);
                    network.SelectedUnit = null; Require(network.MunicipalAppointments.Count == 0, "Troca de UBS preservou consultas na Secretaria.");
                    foreach (var window in new[] { obs, municipal })
                    {
                        InvokeAccountAction(window, "Sair da conta"); PumpUntil(() => !window.ViewModel.Workspace.IsBusy);
                        Require(window.ViewModel.Workspace.IsSignedOut, "Logout real não limpou a janela.");
                    }
                    Console.WriteLine("E2E UBS: cadastro, agenda, fila, vaga, ofertas, indicadores, resumo e histórico conferidos em API/PostgreSQL descartáveis.");
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

    private static void LoginFromControls(ShellWindow window, string address, string identifier, string password)
    {
        var content = Layout(window, 980, 640);
        Input(content, "Endereço da API").Text = address;
        Input(content, "CPF ou e-mail").Text = identifier;
        Descendants<PasswordBox>(content).Single(input => AutomationProperties.GetName(input) == "Senha de acesso").Password = password;
        InvokeButton(FindActionButton(content, "Entrar")); PumpUntil(() => !window.ViewModel.Workspace.IsBusy);
    }
    private static TextBox Input(FrameworkElement content, string name) => Descendants<TextBox>(content).Single(input => AutomationProperties.GetName(input) == name);
    private static Button FindActionButton(FrameworkElement content, string name) => Descendants<Button>(content).Single(button => button.IsVisible && button.Content?.ToString() == name);
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
