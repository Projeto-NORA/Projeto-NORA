using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Nora.Desktop.Models;
using Nora.Desktop.Services;
using Nora.Desktop.Views;
namespace Nora.Desktop.Checks;
internal static partial class Program
{
    private static int RunLiveMapPreview(string[] args)
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(app.Dispatcher));
        using var fixture = new ApiFixture { UseWorkspaceIdentity = true };
        var temp = Path.Combine(AppContext.BaseDirectory, "preview-map-test", Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("NORA_MAP_BROWSER_DATA", Path.Combine(temp, "browser"));
        var window = new ShellWindow(DesktopProfile.Municipal, Path.Combine(temp, "preferences"), new NoraApiClient(fixture));
        window.Title = args.Contains("--secretaria-live-dark") ? "NORA Secretaria · Prévia escura" : "NORA Secretaria · Prévia clara";
        window.ViewModel.ConfirmClose = false; window.ViewModel.Animations = false;
        window.WindowStartupLocation = WindowStartupLocation.CenterScreen; window.Show();
        window.ViewModel.SetAppearance(args.Contains("--secretaria-live-dark") ? Appearance.Dark : Appearance.Light);
        LoginFromControls(window, "https://fixture.invalid", "fixture@example.invalid", "isolated-test-password");
        var vm = window.ViewModel.Workspace; Pump(vm.LoadUnitsAsync());
        vm.UnitName = "UBS Exemplo"; vm.UnitRegion = "Região Central"; vm.UnitAddress = "Endereço ilustrativo, 100"; vm.UnitLatitude = "-23.55"; vm.UnitLongitude = "-46.63"; Pump(vm.SaveUnitAsync());
        window.ViewModel.Navigate("network");
        var content = Layout(window, 1280, 740);
        var map = Descendants<NetworkMapView>(content).Single();
        InvokeButton((Button)map.FindName("LoadButton"));
        var scroll = (ScrollViewer)window.FindName("PageScroll");
        scroll.ScrollToVerticalOffset(420);
        Layout(window, 1280, 740);
        window.Closed += (_, _) => app.Shutdown();
        return app.Run();
    }
}
