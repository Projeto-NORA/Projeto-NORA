using System.Windows;
using Nora.Desktop.Models;
using Nora.Desktop.Services;
using Nora.Desktop.Views;
namespace Painel_Municipal;
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var preferencesFolder = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NORA", "municipal");
        MainWindow = new ShellWindow(DesktopProfile.Municipal, devices: new ObsDeviceStore(preferencesFolder), compactAccess: true);
        UnhandledErrorHandler.Install(this, () => (MainWindow as ShellWindow)?.ViewModel.Workspace);
        MainWindow.Show();
    }
}
