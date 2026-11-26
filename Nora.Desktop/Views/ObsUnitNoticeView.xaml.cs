using System.Windows;
using System.Windows.Controls;

namespace Nora.Desktop.Views;

public partial class ObsUnitNoticeView : UserControl
{
    public ObsUnitNoticeView() => InitializeComponent();
    private void OnChoose(object sender, RoutedEventArgs e) { if (Window.GetWindow(this) is ShellWindow shell) shell.OpenSwitchProfile(); }
}
