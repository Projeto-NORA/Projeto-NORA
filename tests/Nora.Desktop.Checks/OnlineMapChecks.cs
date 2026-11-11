using System.IO;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Wpf;
using Nora.Desktop.Models;
using Nora.Desktop.Services;
using Nora.Desktop.Views;
namespace Nora.Desktop.Checks;
internal static partial class Program
{
    private static void RunOnlineMapCheck(string root)
    {
        Check("Secretaria: mapa online mostra coordenadas da unidade selecionada", () =>
        {
            var old = Environment.GetEnvironmentVariable("NORA_MAP_BROWSER_DATA");
            Environment.SetEnvironmentVariable("NORA_MAP_BROWSER_DATA", Path.Combine(AppContext.BaseDirectory, "map-browser-tests", Guid.NewGuid().ToString("N")));
            using var fixture = new ApiFixture { UseWorkspaceIdentity = true };
            var window = new ShellWindow(DesktopProfile.Municipal, Path.Combine(root, "map-online"), new NoraApiClient(fixture));
            try
            {
                window.ViewModel.ConfirmClose = false; window.ViewModel.Animations = false; PrepareWindow(window);
                LoginFromControls(window, "https://fixture.invalid", "fixture@example.invalid", "isolated-test-password");
                var vm = window.ViewModel.Workspace;
                vm.Units.Add(new HealthUnit(Guid.NewGuid(), "UBS sintética", "Teste", "Rua sintética", -23.55, -46.63));
                vm.SelectedUnit = vm.Units[0];
                window.ViewModel.Navigate("network");
                var content = Layout(window, 1280, 740);
                var map = Descendants<NetworkMapView>(content).Single();
                PumpUntil(() => !vm.IsBusy);
                if (vm.SelectedUnit == null) { vm.Units.Add(new HealthUnit(Guid.NewGuid(), "UBS sintética", "Teste", "Rua sintética", -23.55, -46.63)); vm.SelectedUnit = vm.Units[0]; }
                var button = (Button)map.FindName("LoadButton");
                InvokeButton(button);
                var status = (TextBlock)map.FindName("MapStatus");
                PumpUntil(() => status.Text.StartsWith("Mapa carregado", StringComparison.Ordinal) || status.Text.StartsWith("Não foi possível", StringComparison.Ordinal));
                Require(status.Text.StartsWith("Mapa carregado com a UBS selecionada", StringComparison.Ordinal), "Mapa não confirmou a navegação: " + status.Text);
                var browser = (WebView2)map.FindName("Browser");
                Require(browser.CoreWebView2.Source.Contains("marker=-23.55%2C-46.63"), "O navegador não recebeu as coordenadas selecionadas.");
            }
            finally { window.ViewModel.ConfirmClose = false; window.Close(); Environment.SetEnvironmentVariable("NORA_MAP_BROWSER_DATA", old); }
        });
    }
}
