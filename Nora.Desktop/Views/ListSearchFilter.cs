using System.Collections.Specialized;
using System.Globalization;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace Nora.Desktop.Views;

/// <summary>A pesquisa usa somente campos que a listagem já apresenta e os itens trazidos para ela.</summary>
internal sealed class ListSearchFilter : StackPanel
{
    private readonly ItemsControl target;
    private readonly string[] searchFields;
    private readonly Dictionary<Type, PropertyInfo[]> fieldCache = [];
    private readonly TextBox queryBox;
    private readonly TextBlock resultCount;
    private Predicate<object>? ownFilter;
    private Predicate<object>? previousFilter;
    private string query = "";

    public ListSearchFilter(ItemsControl target, string fields)
    {
        this.target = target;
        searchFields = fields.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var label = new TextBlock { Text = "Buscar na lista", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) };
        Children.Add(label);
        queryBox = new TextBox();
        AutomationProperties.SetLabeledBy(queryBox, label);
        queryBox.TextChanged += (_, _) => ApplyQuery();
        Children.Add(queryBox);
        resultCount = new TextBlock { Margin = new Thickness(0, 6, 0, 0), Visibility = Visibility.Collapsed };
        AutomationProperties.SetLiveSetting(resultCount, AutomationLiveSetting.Polite);
        Children.Add(resultCount);

        var items = (INotifyCollectionChanged)target.Items;
        Loaded += (_, _) => { items.CollectionChanged += OnItemsChanged; UpdateResultCount(); };
        Unloaded += (_, _) => items.CollectionChanged -= OnItemsChanged;
    }

    internal void Clear() => queryBox.Clear();

    private void ApplyQuery()
    {
        query = queryBox.Text.Trim();
        if (query.Length == 0)
        {
            if (ownFilter is not null && ReferenceEquals(target.Items.Filter, ownFilter)) target.Items.Filter = previousFilter;
            ownFilter = null;
            previousFilter = null;
        }
        else if (target.Items.CanFilter)
        {
            if (ownFilter is null || !ReferenceEquals(target.Items.Filter, ownFilter))
                previousFilter = target.Items.Filter;
            var text = query;
            ownFilter = item => Matches(item, text);
            target.Items.Filter = ownFilter;
        }
        UpdateResultCount();
    }

    private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs args) => UpdateResultCount();

    private void UpdateResultCount()
    {
        resultCount.Visibility = query.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        if (query.Length == 0) return;
        resultCount.Text = target.Items.Count switch
        {
            0 => "Nenhum resultado nesta lista.",
            1 => "1 resultado nesta lista.",
            var count => $"{count} resultados nesta lista."
        };
    }

    private bool Matches(object item, string text)
    {
        if (previousFilter?.Invoke(item) == false || item is null) return false;
        var type = item.GetType();
        if (!fieldCache.TryGetValue(type, out var fields))
        {
            fields = searchFields.Select(name => type.GetProperty(name)).OfType<PropertyInfo>().ToArray();
            fieldCache[type] = fields;
        }
        var comparison = CultureInfo.CurrentCulture.CompareInfo;
        return fields.Any(field => field.GetValue(item) is { } value &&
            comparison.IndexOf(Convert.ToString(value, CultureInfo.CurrentCulture) ?? "", text, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0);
    }
}
