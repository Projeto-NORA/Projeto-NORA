using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Nora.Desktop.Models;

namespace Nora.Desktop.Generics;

public static class ScreenSearch
{
    public static IEnumerable<FrameworkElement> VisibleElements(DependencyObject root, DependencyObject? skip = null)
    {
        if (ReferenceEquals(root, skip) || root is UIElement { IsVisible: false }) yield break;
        if (root is FrameworkElement element) yield return element;
        if (root is not Visual || root is DataGrid or ListBox) yield break;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            foreach (var child in VisibleElements(VisualTreeHelper.GetChild(root, index), skip)) yield return child;
    }

    public static IReadOnlyList<Func<FrameworkElement?>> Find(DependencyObject root, DependencyObject skip, string query)
    {
        var result = new List<Func<FrameworkElement?>>();
        if (string.IsNullOrWhiteSpace(query)) return result;
        foreach (var element in VisibleElements(root, skip))
        {
            switch (element)
            {
                // O texto-guia do campo Pesquisar e o aviso de resultados (invisível, só para leitor de tela) não são conteúdo da tela.
                case TextBlock text when text.Opacity > 0 && text.TemplatedParent is not TextBox && Matches(text.Text, query):
                    result.Add(() => text);
                    break;
                case TextBox { IsReadOnly: true } text when Matches(text.Text, query):
                    result.Add(() => text);
                    break;
                case DataGrid table:
                    FindTable(table, query, result);
                    break;
                case ListBox list:
                    FindList(list, query, result);
                    break;
            }
        }
        return result;
    }

    private static void FindTable(DataGrid table, string query, List<Func<FrameworkElement?>> result)
    {
        var columns = table.Columns.Where(column => column.Visibility == Visibility.Visible)
            .OfType<DataGridBoundColumn>()
            .Select(column => (Column: column, Path: (column.Binding as Binding)?.Path?.Path))
            .Where(column => !string.IsNullOrWhiteSpace(column.Path)).ToArray();
        if (table.Columns.Any(column => column.Visibility == Visibility.Visible && column.Header is string header && Matches(header, query)))
            result.Add(() => table);
        foreach (var item in table.Items)
        {
            if (item is null || ReferenceEquals(item, CollectionView.NewItemPlaceholder)) continue;
            foreach (var column in columns)
            {
                if (!Matches(ReadDisplayValue(item, column.Path!), query)) continue;
                result.Add(() =>
                {
                    if (!table.Items.Contains(item)) return null;
                    table.ScrollIntoView(item, column.Column);
                    table.UpdateLayout();
                    return column.Column.GetCellContent(item) as FrameworkElement ?? table.ItemContainerGenerator.ContainerFromItem(item) as FrameworkElement;
                });
            }
        }
    }

    private static void FindList(ListBox list, string query, List<Func<FrameworkElement?>> result)
    {
        foreach (var item in list.Items)
        {
            var container = list.ItemContainerGenerator.ContainerFromItem(item);
            var found = !string.IsNullOrWhiteSpace(list.DisplayMemberPath) && item is not null
                ? Matches(ReadDisplayValue(item, list.DisplayMemberPath), query)
                : item switch
                {
                    UnitPatientRecord patient => Matches(patient.FullName, query) || Matches(patient.Cpf, query) || Matches(patient.Email, query) || Matches(patient.Phone, query),
                    HealthUnit unit => Matches(unit.Name, query) || Matches(unit.Region, query) || Matches(unit.Address, query),
                    Specialty specialty => Matches(specialty.Name, query),
                    Professional professional => Matches(professional.FullName, query),
                    Room room => Matches(room.Name, query),
                    StaffAccount account => Matches(account.FullName, query) || Matches(account.Email, query),
                    string text => Matches(text, query),
                    _ => container is not null && VisibleElements(container).OfType<TextBlock>().Any(text => Matches(text.Text, query))
                };
            if (!found) continue;
            result.Add(() =>
            {
                if (!list.Items.Contains(item)) return null;
                list.ScrollIntoView(item);
                list.UpdateLayout();
                return list.ItemContainerGenerator.ContainerFromItem(item) as FrameworkElement;
            });
        }
    }

    private static string? ReadDisplayValue(object? item, string path)
    {
        foreach (var segment in path.Split('.'))
            item = item?.GetType().GetProperty(segment)?.GetValue(item);
        return item switch
        {
            null => null,
            string text => text,
            IFormattable value => value.ToString(null, CultureInfo.CurrentCulture),
            _ => null
        };
    }

    private static bool Matches(string? text, string query) => !string.IsNullOrWhiteSpace(text) &&
        CultureInfo.CurrentCulture.CompareInfo.IndexOf(text, query, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0;
}
