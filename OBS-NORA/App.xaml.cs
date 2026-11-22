using System.Windows;
using System.Windows.Media.Imaging;
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
        MainWindow.Icon = BitmapFrame.Create(new Uri("pack://application:,,,/OBS-NORA;component/Assets/obs-nora.ico"));
        UnhandledErrorHandler.Install(this, () => (MainWindow as ShellWindow)?.ViewModel.Workspace);
        MainWindow.Show();
    }
}
