using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Nora.Desktop.Views;

internal static class ListingInteraction
{
    public static DataGridRow? FindRow(DependencyObject? source, DataGrid table)
    {
        while (source != null && source != table)
        {
            if (source is DataGridRow row) return row;
            source = source is Visual ? VisualTreeHelper.GetParent(source) : LogicalTreeHelper.GetParent(source);
        }
        return null;
    }

    /// <summary>Duplo clique em uma linha abre a ficha do item.</summary>
    public static void OpenOnDoubleClick(UserControl owner, object sender, MouseButtonEventArgs args)
    {
        if (args.ChangedButton != MouseButton.Left || sender is not DataGrid table) return;
        var row = FindRow(args.OriginalSource as DependencyObject, table);
        if (row == null || Window.GetWindow(owner) is not ShellWindow shell) return;
        shell.OpenItemDetails(row.Item);
        args.Handled = true;
    }

    /// <summary>Enter abre a ficha do item selecionado.</summary>
    public static void OpenOnEnter(UserControl owner, object sender, KeyEventArgs args)
    {
        if (args.Key != Key.Enter || sender is not DataGrid { SelectedItem: { } item } || Window.GetWindow(owner) is not ShellWindow shell) return;
        shell.OpenItemDetails(item);
        args.Handled = true;
    }
}
