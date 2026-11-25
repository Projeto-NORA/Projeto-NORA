using System.Windows;
using System.Windows.Controls;
namespace Nora.Desktop.Views;
public partial class SettingsView : UserControl
{
    public SettingsView() => InitializeComponent();
    private void OnPhotoMenu(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement target && DataContext is ViewModels.ShellViewModel shell) AccountAvatar.OpenPhotoMenu(target, shell.Workspace);
    }
}
