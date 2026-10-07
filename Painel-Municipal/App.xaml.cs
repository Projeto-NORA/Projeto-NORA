using System.Windows;
namespace Painel_Municipal;
public partial class App : Application { protected override void OnStartup(StartupEventArgs e) { base.OnStartup(e); new MainWindow().Show(); } }
