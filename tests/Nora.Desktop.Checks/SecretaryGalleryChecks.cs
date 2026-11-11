using System.IO;
using System.Text.Json;
using System.Windows.Controls;
using Nora.Desktop.Models;
using Nora.Desktop.Services;
using Nora.Desktop.Views;

namespace Nora.Desktop.Checks;

internal static partial class Program
{
    private sealed record SecretaryCapture(string File, string Screen, string Theme, int Part, double ScrollOffset, string Source);
    private static void RunSecretaryGallery(string root, string[] args)
    {
        Check("Secretaria: capturas de todas as telas em claro e escuro", () =>
        {
            var directory = VisualGalleryDirectory(args);
            Directory.CreateDirectory(directory);
            var captures = new List<SecretaryCapture>();
            using var fixture = new ApiFixture { UseWorkspaceIdentity = true };
            var window = new ShellWindow(DesktopProfile.Municipal, Path.Combine(root, "secretaria-capturas"), new NoraApiClient(fixture));
            try
            {
                window.ViewModel.ConfirmClose = false;
                window.ViewModel.Animations = false;
                PrepareWindow(window);
                foreach (var theme in new[] { Appearance.Dark, Appearance.Light })
                {
                    window.ViewModel.SetAppearance(theme);
                    window.ViewModel.Navigate("home");
                    Capture("login", "Login", theme, "Tela desconectada", window, directory, captures);
                }
                LoginFromControls(window, "https://fixture.invalid", "fixture@example.invalid", "isolated-test-password");
                Require(window.ViewModel.Workspace.IsAuthenticated, "Login da fixture falhou.");
                var pages = new (string Id, string Label)[] {
                    ("home", "Visão geral"), ("units", "Unidades"), ("specialties", "Especialidades"),
                    ("network", "Mapa da rede"), ("indicators", "Indicadores"), ("slots", "Vaga Relâmpago"),
                    ("capacity", "Capacidade"), ("alerts", "Alertas"), ("reports", "Relatórios e metas"),
                    ("communication", "Comunicação"), ("users", "Usuários e histórico"), ("settings", "Configurações") };
                foreach (var page in pages)
                foreach (var theme in new[] { Appearance.Dark, Appearance.Light })
                {
                    window.ViewModel.SetAppearance(theme);
                    window.ViewModel.Navigate(page.Id);
                    Capture(page.Id, page.Label, theme, "HTTP simulado; dados sintéticos", window, directory, captures);
                }
                Require(captures.Select(c => (c.Screen, c.Theme)).Distinct().Count() == 26, "Faltam telas ou temas.");
                File.WriteAllText(Path.Combine(directory, "manifest.json"), JsonSerializer.Serialize(captures, new JsonSerializerOptions { WriteIndented = true }));
            }
            finally { window.ViewModel.ConfirmClose = false; window.Close(); }
        });
    }
    private static void RunChangedSecretaryScreens(string root, string[] args)
    {
        Check("Secretaria: capturas das telas alteradas", () =>
        {
            var directory = VisualGalleryDirectory(args); Directory.CreateDirectory(directory);
            var captures = new List<SecretaryCapture>();
            using var fixture = new ApiFixture { UseWorkspaceIdentity = true };
            var window = new ShellWindow(DesktopProfile.Municipal, Path.Combine(root, "secretaria-alterada"), new NoraApiClient(fixture));
            try
            {
                window.ViewModel.ConfirmClose = false; window.ViewModel.Animations = false; PrepareWindow(window);
                LoginFromControls(window, "https://fixture.invalid", "fixture@example.invalid", "isolated-test-password");
                var vm = window.ViewModel.Workspace; Pump(vm.LoadUnitsAsync());
                vm.UnitName = "UBS Exemplo"; vm.UnitRegion = "Região Central"; vm.UnitAddress = "Endereço ilustrativo, 100"; vm.UnitLatitude = "-23.55"; vm.UnitLongitude = "-46.63"; Pump(vm.SaveUnitAsync());
                Require(vm.Units.Count == 1, "Fixture não cadastrou unidade.");
                foreach (var page in new[] { ("units", "Unidades"), ("network", "Mapa da rede"), ("slots", "Vaga Relâmpago"), ("reports", "Relatórios e metas") })
                foreach (var theme in new[] { Appearance.Dark, Appearance.Light })
                {
                    window.ViewModel.SetAppearance(theme); window.ViewModel.Navigate(page.Item1);
                    Capture(page.Item1, page.Item2, theme, "HTTP simulado; UBS sintética", window, directory, captures);
                }
                File.WriteAllText(Path.Combine(directory, "manifest.json"), JsonSerializer.Serialize(captures, new JsonSerializerOptions { WriteIndented = true }));
            }
            finally { window.ViewModel.ConfirmClose = false; window.Close(); }
        });
    }
    private static void Capture(string id, string label, Appearance theme, string source, ShellWindow window, string directory, List<SecretaryCapture> captures)
    {
        var content = Layout(window, 1280, 740);
        Require(Descendants<PasswordBox>(content).All(input => input.Password.Length == 0), "Captura recusada: campo de senha preenchido.");
        var scroll = (ScrollViewer)window.FindName("PageScroll");
        scroll.ScrollToTop();
        Layout(window, 1280, 740);
        var max = scroll.ScrollableHeight;
        var step = Math.Max(1, scroll.ViewportHeight - 100);
        var offsets = new List<double> { 0 };
        for (var next = step; next < max; next += step) offsets.Add(next);
        if (max > 1) offsets.Add(max);
        for (var i = 0; i < offsets.Count; i++)
        {
            scroll.ScrollToVerticalOffset(offsets[i]);
            Layout(window, 1280, 740);
            var filename = $"secretaria-{id}-{theme.ToString().ToLowerInvariant()}-{i + 1:D2}.png";
            var path = Path.Combine(directory, filename);
            SavePreview(content, path);
            RequireVisualCaptureBounds(path, window, content);
            captures.Add(new(filename, label, theme.ToString(), i + 1, scroll.VerticalOffset, source));
            Console.WriteLine("CAPTURE " + path);
        }
    }
}
