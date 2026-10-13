using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
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
        Check("E2E OBS cadastro/telefone nativo → tela fila/oferta → Municipal agregado → menu logout", () =>
        {
            var identifier = Environment.GetEnvironmentVariable("NORA_E2E_IDENTIFIER") ?? throw new InvalidOperationException("Falta identificador E2E.");
            var password = Environment.GetEnvironmentVariable("NORA_E2E_PASSWORD") ?? throw new InvalidOperationException("Falta senha E2E.");
            var endpoint = NoraApiClient.ValidateEndpoint(address);
            Require(endpoint.IsLoopback, "E2E requer API descartável no loopback.");
            var previewArgument = Array.IndexOf(args, "--preview-dir");
            var previewRoot = previewArgument >= 0 && previewArgument + 1 < args.Length ? args[previewArgument + 1] : Path.Combine(AppContext.BaseDirectory, "previews");
            Directory.CreateDirectory(previewRoot);
            var suffix = Guid.NewGuid().ToString("N")[..10];
            var patientName = "Paciente WPF E2E " + suffix;
            var patientEmail = "wpf-" + suffix + "@example.test";
            var patientCpf = SyntheticCpf();
            const string patientPassword = "Disposable-patient-check!29";
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
                obs.ViewModel.Navigate("patients");
                var content = Layout(obs, 980, 640);
                Descendants<Expander>(content).Single().IsExpanded = true;
                Layout(obs, 980, 640);
                Input(content, "Nome completo do paciente").Text = patientName;
                Input(content, "CPF do paciente").Text = patientCpf;
                Input(content, "E-mail do paciente").Text = patientEmail;
                Input(content, "Telefone do paciente").Text = "(48) 99900-0029";
                Descendants<PasswordBox>(content).Single(input => AutomationProperties.GetName(input) == "Senha inicial do paciente").Password = patientPassword;
                InvokeButton(FindActionButton(content, "Cadastrar paciente")); PumpUntil(() => !vm.IsBusy);
                var patient = vm.Patients.SingleOrDefault(p => p.Email == patientEmail) ?? throw new InvalidOperationException("Cadastro real não voltou à listagem: " + vm.Message);
                Require(vm.SelectedPatient?.Id == patient.Id && patient.Phone == "48999000029", "Cadastro real não preservou seleção/telefone normalizado: " + vm.Message);
                SavePreview(Layout(obs, 1280, 740), Path.Combine(previewRoot, "obs-e2e-patient-persisted.png"));
                obs.ViewModel.Navigate("queue");
                content = Layout(obs, 980, 640);
                Descendants<ComboBox>(content).Single(input => AutomationProperties.GetName(input) == "Paciente para incluir na fila").SelectedItem = patient;
                InvokeButton(FindActionButton(content, "Incluir selecionado na fila")); PumpUntil(() => !vm.IsBusy);
                Require(vm.Queue.Any(entry => entry.PatientId == patient!.Id), "Paciente não foi persistido na fila: " + vm.Message);
                obs.ViewModel.Navigate("slots");
                content = Layout(obs, 980, 640);
                var slotLabel = "Vaga WPF E2E " + suffix;
                Input(content, "Descrição da vaga").Text = slotLabel;
                Layout(obs, 980, 640);
                InvokeButton(FindActionButton(content, "Abrir vaga")); PumpUntil(() => !vm.IsBusy);
                Require(vm.Slots.Any(slot => slot.Label == slotLabel), "Vaga real não foi persistida: " + vm.Message);
                SavePreview(Layout(obs, 1280, 740), Path.Combine(previewRoot, "obs-e2e-slot-persisted.png"));
                obs.ViewModel.Navigate("queue");
                SavePreview(Layout(obs, 1280, 740), Path.Combine(previewRoot, "obs-e2e-queue-persisted.png"));
                // A new patient session verifies the persisted account and data through its own HTTP contract.
                Pump(Task.Run(async () =>
                {
                    using var api = new NoraApiClient(); api.Configure(address);
                    var session = await api.LoginAsync("patient", patientCpf, patientPassword);
                    Require(session.User.Id == patient!.Id && session.User.FullName == patientName, "CPF não autenticou a conta persistida.");
                    using var http = new HttpClient { BaseAddress = endpoint, Timeout = TimeSpan.FromSeconds(20) };
                    http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.Token);
                    using var response = await http.GetAsync("api/patient/overview");
                    Require(response.StatusCode == HttpStatusCode.OK, "Paciente não consultou sua jornada.");
                    using var overview = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                    var entries = overview.RootElement.GetProperty("queue").EnumerateArray().ToArray();
                    Require(entries.Any(entry => entry.GetProperty("patientId").GetGuid() == patient.Id) && entries.All(entry => entry.GetProperty("patientId").GetGuid() == patient.Id), "Jornada perdeu fila própria ou expôs outro paciente.");
                    var offer = overview.RootElement.GetProperty("offers").EnumerateArray().Single(item => item.GetProperty("label").GetString() == slotLabel);
                    var expiresAt = offer.GetProperty("expiresAt").GetDateTimeOffset();
                    Require(offer.GetProperty("status").GetString() == "pending" && expiresAt > DateTimeOffset.UtcNow.AddMinutes(9) && expiresAt <= DateTimeOffset.UtcNow.AddMinutes(10), "Oferta real perdeu prazo de 10 minutos.");
                    using var accepted = await http.PostAsJsonAsync("api/offers/" + offer.GetProperty("id").GetGuid() + "/respond", new { accept = true });
                    Require(accepted.IsSuccessStatusCode, "Paciente não aceitou oferta real.");
                    await api.LogoutAsync();
                    var emailSession = await api.LoginAsync("patient", patientEmail, patientPassword);
                    Require(emailSession.User.Id == patient.Id, "E-mail não autenticou a mesma conta.");
                    await api.LogoutAsync();
                }));
                Pump(vm.RefreshAutomaticallyAsync());
                Require(vm.Slots.Single(slot => slot.Label == slotLabel).Status == "accepted" && vm.Queue.Single(entry => entry.PatientId == patient!.Id).Status == "completed", "OBS não refletiu aceite persistido pelo paciente.");
                obs.ViewModel.Navigate("patients");
                content = Layout(obs, 980, 640);
                Descendants<DataGrid>(content).Single(grid => AutomationProperties.GetName(grid) == "Pacientes cadastrados").SelectedItem = vm.Patients.Single(p => p.Id == patient!.Id);
                Layout(obs, 980, 640);
                obs.ViewModel.Navigate("queue");
                content = Layout(obs, 980, 640);
                Descendants<ComboBox>(content).Single(input => AutomationProperties.GetName(input) == "Paciente para incluir na fila").SelectedItem = vm.Patients.Single(item => item.Id == patient.Id);
                InvokeButton(FindActionButton(content, "Incluir selecionado na fila")); PumpUntil(() => !vm.IsBusy);
                Descendants<DataGrid>(content).Single(grid => AutomationProperties.GetName(grid) == "Fila de Vaga Relâmpago").SelectedItem = vm.Queue.Single(entry => entry.PatientId == patient!.Id && entry.Status == "waiting");
                Layout(obs, 980, 640);
                InvokeButton(FindActionButton(content, "Cancelar entrada selecionada")); PumpUntil(() => !vm.IsBusy);
                Require(vm.Queue.Any(entry => entry.PatientId == patient!.Id && entry.Status == "cancelled"), "Cancelamento nativo não foi persistido.");
                SavePreview(Layout(obs, 1280, 740), Path.Combine(previewRoot, "obs-e2e-queue-completed-cancelled.png"));
                LoginFromControls(municipal, address, identifier, password);
                var manager = municipal.ViewModel.Workspace;
                Require(manager.IsAuthenticated && long.Parse(manager.PatientCount) > 0 && long.Parse(manager.AcceptedCount) > 0 && manager.Patients.Count == 0 && manager.Queue.Count == 0 && manager.Slots.Count == 0, "Municipal não carregou agregados isolados: " + manager.Message);
                SavePreview(Layout(municipal, 1280, 740), Path.Combine(previewRoot, "municipal-e2e-aggregates.png"));
                foreach (var window in new[] { obs, municipal })
                {
                    InvokeAccountAction(window, "Sair da conta");
                    PumpUntil(() => !window.ViewModel.Workspace.IsBusy);
                    Require(window.ViewModel.Workspace.IsSignedOut && window.ViewModel.Workspace.Patients.Count == 0, "Logout real não limpou a janela.");
                }
                Console.WriteLine("E2E persistência real conferida em API/PostgreSQL descartáveis; prévias nativas gravadas.");
            }
            finally { obs.Close(); municipal.Close(); }
        });
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
