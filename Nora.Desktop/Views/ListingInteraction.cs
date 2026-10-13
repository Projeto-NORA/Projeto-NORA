using System.Windows;
using System.Windows.Controls;
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
}
