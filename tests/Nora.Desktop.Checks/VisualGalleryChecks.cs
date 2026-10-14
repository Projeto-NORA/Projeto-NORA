using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Nora.Desktop.Models;
using Nora.Desktop.Services;
using Nora.Desktop.Views;

namespace Nora.Desktop.Checks;

internal static partial class Program
{
    private sealed record VisualCapture(string File, string Application, string Screen, string Appearance,
        string Position, string DataSource, double Width, double Height, double ScrollOffset);
    private static readonly List<VisualCapture> visualCaptures = [];

    private static string VisualGalleryDirectory(string[] args)
    {
        var argument = Array.IndexOf(args, "--preview-dir");
        return argument >= 0 && argument + 1 < args.Length
            ? Path.GetFullPath(args[argument + 1])
            : Path.Combine(AppContext.BaseDirectory, "previews");
    }

    private static void RunVisualGalleryChecks(string storageRoot, string[] args)
    {
        foreach (var profile in new[] { DesktopProfile.Operational, DesktopProfile.Municipal })
        Check($"{profile.Name}: galeria visual nativa sem credenciais e com preferências isoladas", () =>
        {
            var window = new ShellWindow(profile, Path.Combine(storageRoot, "visual-gallery", profile.Id));
            try
            {
                window.ViewModel.ConfirmClose = false;
                window.ViewModel.Animations = false;
                window.ViewModel.Workspace.Endpoint = "";
                PrepareWindow(window);
                foreach (var mode in new[] { Appearance.Light, Appearance.Dark, Appearance.System })
                {
                    window.ViewModel.SetAppearance(mode);
                    window.ViewModel.Navigate("home");
                    CaptureVisualPage(window, args, "login", "topo", 0, false);
                    CaptureVisualPage(window, args, "home-desconectada", "fim", 1, false);
                    window.ViewModel.Navigate("settings");
                    CaptureVisualPage(window, args, "configuracoes", "topo", 0, false);
                    CaptureVisualPage(window, args, "configuracoes", "meio", 0.5, false);
                    CaptureVisualPage(window, args, "configuracoes", "fim", 1, false);
                }
                window.ViewModel.SidebarExpanded = false;
                window.ViewModel.SetAppearance(Appearance.Dark);
                window.ViewModel.Navigate("settings");
                CaptureVisualPage(window, args, "configuracoes-sidebar-recolhida", "topo", 0, false);
                window.ViewModel.Navigate("home");
                CaptureVisualPage(window, args, "login-sidebar-recolhida", "topo", 0, false);
                window.ViewModel.SidebarExpanded = true;
                CaptureVisualPage(window, args, "login-janela-minima", "topo", 0, false, 980, 640);
                window.ViewModel.Navigate("settings");
                CaptureVisualPage(window, args, "configuracoes-janela-minima", "fim", 1, false, 980, 640);
                Require(visualCaptures.Count(capture => capture.Application == profile.Name) >= 18, "Galeria incompleta para o perfil.");
            }
            finally { window.ViewModel.ConfirmClose = false; window.Close(); }
        });
        RunAuthenticatedVisualGallery(storageRoot, args);
    }

    private const string VisualHttpSource = "HTTP simulado exclusivamente no harness visual; dados sintéticos; sem PostgreSQL/integracao real";

    private static void RunAuthenticatedVisualGallery(string storageRoot, string[] args)
    {
        foreach (var profile in new[] { DesktopProfile.Operational, DesktopProfile.Municipal })
        Check($"{profile.Name}: galeria de fluxos com HTTP simulado somente no harness visual", () =>
        {
            using var fixture = new ApiFixture { UseWorkspaceIdentity = true };
            var window = new ShellWindow(profile, Path.Combine(storageRoot, "visual-gallery-http", profile.Id), new NoraApiClient(fixture));
            try
            {
                window.ViewModel.ConfirmClose = false;
                window.ViewModel.Animations = false;
                PrepareWindow(window);
                LoginFromControls(window, "https://fixture.invalid", "fixture@example.invalid", "isolated-test-password");
                var vm = window.ViewModel.Workspace;
                Require(vm.IsAuthenticated, "Fixture visual não autenticou a UI nativa.");
                if (profile.IsMunicipal)
                {
                    CaptureVisualThemes(window, args, "totais-agregados", dataSource: VisualHttpSource);
                    window.ViewModel.Navigate("indicators");
                    CaptureVisualThemes(window, args, "indicadores", dataSource: VisualHttpSource);
                    window.ViewModel.SidebarExpanded = false;
                    CaptureVisualThemes(window, args, "indicadores-sidebar-recolhida", dataSource: VisualHttpSource);
                    return;
                }
                CaptureVisualThemes(window, args, "home-conectada", dataSource: VisualHttpSource);
                CaptureVisualThemes(window, args, "home-conectada", "fim", 1, VisualHttpSource);
                window.ViewModel.Navigate("patients");
                var content = Layout(window, 1280, 740);
                var table = Descendants<DataGrid>(content).Single(grid => AutomationProperties.GetName(grid) == "Pacientes cadastrados");
                table.SelectedItem = vm.Patients.Single();
                CaptureVisualThemes(window, args, "pacientes-selecao", dataSource: VisualHttpSource);
                var registration = Descendants<Expander>(content).Single();
                registration.IsExpanded = true;
                Layout(window, 1280, 740);
                Input(content, "Nome completo do paciente").Text = "Paciente Novo Fixture";
                Input(content, "CPF do paciente").Text = "00000000001";
                Input(content, "E-mail do paciente").Text = "new@example.invalid";
                CaptureVisualThemes(window, args, "cadastro-paciente", "fim", 1, VisualHttpSource);
                Descendants<PasswordBox>(content).Single(input => AutomationProperties.GetName(input) == "Senha inicial do paciente").Password = "isolated-initial-password";
                InvokeButton(FindActionButton(content, "Cadastrar paciente")); PumpUntil(() => !vm.IsBusy);
                Require(fixture.Registered && vm.Patients.Count == 2, "Cadastro fixture não publicou paciente.");
                registration.IsExpanded = false;
                CaptureVisualThemes(window, args, "paciente-cadastrado", dataSource: VisualHttpSource);
                // The integrated OBS now keeps inclusion on the queue screen.
                window.ViewModel.Navigate("queue");
                content = Layout(window, 1280, 740);
                InvokeButton(FindActionButton(content, "Incluir selecionado na fila")); PumpUntil(() => !vm.IsBusy);
                window.ViewModel.Navigate("queue");
                content = Layout(window, 1280, 740);
                Descendants<DataGrid>(content).Single(grid => AutomationProperties.GetName(grid) == "Fila de Vaga Relâmpago").SelectedItem = vm.Queue.Single(entry => entry.Id == 2);
                CaptureVisualThemes(window, args, "fila-selecionada-acao", dataSource: VisualHttpSource);
                window.ViewModel.Navigate("slots");
                content = Layout(window, 1280, 740);
                Input(content, "Descrição da vaga").Text = "Vaga nova fixture";
                CaptureVisualThemes(window, args, "vaga-formulario", dataSource: VisualHttpSource);
                InvokeButton(FindActionButton(content, "Abrir vaga")); PumpUntil(() => !vm.IsBusy);
                Require(fixture.CreatedSlotLabel != null && vm.Slots.Count == 2, "Fixture visual não criou vaga.");
                CaptureVisualThemes(window, args, "vagas-abertas", dataSource: VisualHttpSource);
                window.ViewModel.Navigate("queue");
                content = Layout(window, 1280, 740);
                var queueTable = Descendants<DataGrid>(content).Single(grid => AutomationProperties.GetName(grid) == "Fila de Vaga Relâmpago");
                queueTable.SelectedItem = null;
                queueTable.SelectedItem = vm.Queue.Single(entry => entry.Id == 2);
                Layout(window, 1280, 740);
                Require(vm.HasQueueActionSelection, "Seleção retornada à Fila não apresentou ações para a captura.");
                InvokeButton(FindActionButton(content, "Cancelar entrada selecionada")); PumpUntil(() => !vm.IsBusy);
                Require(vm.Queue.Single(entry => entry.Id == 2).Status == "cancelled", "Cancelamento visual não atualizou fila.");
                CaptureVisualThemes(window, args, "fila-cancelada", dataSource: VisualHttpSource);
                window.ViewModel.SidebarExpanded = false;
                CaptureVisualThemes(window, args, "fila-sidebar-recolhida", dataSource: VisualHttpSource);
                window.ViewModel.SidebarExpanded = true;
                window.ViewModel.Navigate("settings");
                CaptureVisualThemes(window, args, "configuracoes-conta-conectada", dataSource: VisualHttpSource);
            }
            finally { window.ViewModel.ConfirmClose = false; window.Close(); }
        });
    }

    private static void CaptureVisualPage(ShellWindow window, string[] args, string screen, string position,
        double scrollRatio, bool connectedFixture, double width = 1280, double height = 740, string? dataSource = null)
    {
        if (!args.Contains("--visual-gallery")) return;
        var content = Layout(window, width, height);
        // Prevent any credential field from entering the image, including hidden controls.
        Require(Descendants<PasswordBox>(content).All(input => input.Password.Length == 0), "Uma senha está preenchida; captura recusada.");
        var scrolling = Descendants<ScrollViewer>(content).First();
        scrolling.ScrollToVerticalOffset(scrolling.ScrollableHeight * scrollRatio);
        Layout(window, width, height);
        Require(content is Grid shell && Math.Abs(shell.ColumnDefinitions[0].ActualWidth - window.ViewModel.SidebarWidth) < 1,
            "A captura não refletiu a largura atual da barra lateral.");
        var appearance = window.ViewModel.Appearance.ToString().ToLowerInvariant();
        var name = $"visual-{window.ViewModel.Profile.Id}-{screen}-{appearance}-{position}.png";
        var directory = VisualGalleryDirectory(args);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, name);
        SavePreview(content, path);
        Require(new FileInfo(path).Length > 1000, "Captura não produziu PNG utilizável.");
        RequireVisualCaptureBounds(path, window, content);
        visualCaptures.Add(new(name, window.ViewModel.Profile.Name, screen, appearance, position,
            dataSource ?? (connectedFixture ? VisualHttpSource : "Interface desconectada; sem dados operacionais"), content.ActualWidth, content.ActualHeight, scrolling.VerticalOffset));
        File.WriteAllText(Path.Combine(directory, "visual-captures.json"), JsonSerializer.Serialize(new
        {
            generatedAtUtc = DateTimeOffset.UtcNow,
            captureMethod = "WPF RenderTargetBitmap de controles nativos, sem redesenhar a interface",
            systemAppearance = ThemeService.SystemIsDark() ? "dark" : "light",
            captures = visualCaptures
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"CAPTURE {path}");
    }

    private static void RequireVisualCaptureBounds(string path, ShellWindow window, FrameworkElement content)
    {
        using var image = File.OpenRead(path);
        var frame = BitmapDecoder.Create(image, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
        Require(frame.PixelWidth == (int)content.ActualWidth && frame.PixelHeight == (int)content.ActualHeight,
            "Dimensões do PNG divergem do conteúdo nativo e dos metadados da captura.");
        var pixels = new FormatConvertedBitmap(frame, System.Windows.Media.PixelFormats.Bgra32, null, 0);
        var sample = new byte[4];
        pixels.CopyPixels(new Int32Rect(frame.PixelWidth / 2, frame.PixelHeight - 2, 1, 1), sample, 4, 0);
        Require(sample[3] == 255,
            $"Captura tem faixa transparente no rodapé (alpha={sample[3]}); PNG={frame.PixelWidth}x{frame.PixelHeight}, janela={window.ActualWidth:F0}x{window.ActualHeight:F0}, conteúdo={content.ActualWidth:F0}x{content.ActualHeight:F0}.");
    }

    private static void CaptureVisualThemes(ShellWindow window, string[] args, string screen,
        string position = "topo", double scrollRatio = 0, string dataSource = VisualHttpSource)
    {
        if (!args.Contains("--visual-gallery")) return;
        foreach (var mode in new[] { Appearance.Light, Appearance.Dark })
        {
            window.ViewModel.SetAppearance(mode);
            CaptureVisualPage(window, args, screen, position, scrollRatio, true, dataSource: dataSource);
        }
    }
}
