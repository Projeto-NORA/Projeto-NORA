using System.Windows;
using Nora.Desktop.Models;
using Nora.Desktop.Services;
using Nora.Desktop.Views;
namespace OBS_NORA;
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        MainWindow = new ShellWindow(DesktopProfile.Operational, devices: new ObsDeviceStore());
        UnhandledErrorHandler.Install(this, () => (MainWindow as ShellWindow)?.ViewModel.Workspace);
        MainWindow.Show();
    }
}
