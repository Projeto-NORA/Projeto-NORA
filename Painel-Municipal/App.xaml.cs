using System.Windows;
using Nora.Desktop.Models;
using Nora.Desktop.Views;
namespace Painel_Municipal;
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        MainWindow = new ShellWindow(DesktopProfile.Municipal);
        MainWindow.Show();
    }
}
