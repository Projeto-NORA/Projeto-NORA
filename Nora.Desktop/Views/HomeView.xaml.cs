using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
namespace Nora.Desktop.Views;
public partial class HomeView : UserControl
{
    public HomeView() => InitializeComponent();
    private void OnListDoubleClick(object sender, MouseButtonEventArgs args)
    {
        if (args.ChangedButton != MouseButton.Left || sender is not DataGrid table) return;
        var row = ListingInteraction.FindRow(args.OriginalSource as DependencyObject, table);
        if (row == null || Window.GetWindow(this) is not ShellWindow shell) return;
        shell.OpenItemDetails(row.Item);
        args.Handled = true;
    }
    private void OnListKeyDown(object sender, KeyEventArgs args)
    {
        if (args.Key != Key.Enter || sender is not DataGrid { SelectedItem: { } item } || Window.GetWindow(this) is not ShellWindow shell) return;
        shell.OpenItemDetails(item);
        args.Handled = true;
    }
}
