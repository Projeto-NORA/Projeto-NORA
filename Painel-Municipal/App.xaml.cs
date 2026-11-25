using System.Windows;
using System.Windows.Media.Imaging;
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
        MainWindow.Icon = BitmapFrame.Create(new Uri("pack://application:,,,/Painel-Municipal;component/Assets/nora.ico"));
        UnhandledErrorHandler.Install(this, () => (MainWindow as ShellWindow)?.ViewModel.Workspace);
        try { MainWindow.Show(); }
        catch (Exception exception)
        {
            // O tratador de erros engole exceções; sem isto o programa ficava rodando sem janela e sem explicação.
            try { System.IO.Directory.CreateDirectory(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NORA")); System.IO.File.WriteAllText(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NORA", "erro-abertura.txt"), exception.ToString()); } catch { }
            MessageBox.Show("Não foi possível abrir o Painel Municipal." + Environment.NewLine + Environment.NewLine + exception.Message + Environment.NewLine + Environment.NewLine + "O detalhe foi salvo em erro-abertura.txt, na pasta NORA dentro de AppData/Local.", "Painel Municipal", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }
}