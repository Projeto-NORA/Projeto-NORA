using System.Collections.Specialized;
using System.Globalization;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace Nora.Desktop.Views;

/// <summary>
/// Campo "Pesquisar" em linha, no cabeçalho da tela (lupa à direita). A pesquisa usa somente campos que a listagem já apresenta e os itens
/// trazidos para ela. Largura preferida de 260 px; com pouco espaço encolhe até o mínimo (<see cref="FrameworkElement.MinWidth"/>) antes de qualquer botão sumir.
/// </summary>
internal sealed class ListSearchFilter : Grid
{
    private const double PreferredWidth = 260;
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
        MinWidth = 140;
        MaxWidth = PreferredWidth;
        HorizontalAlignment = HorizontalAlignment.Left;
        VerticalAlignment = VerticalAlignment.Center;
        queryBox = new TextBox { Tag = "Pesquisar" };
        queryBox.SetResourceReference(StyleProperty, "SearchField");
        AutomationProperties.SetName(queryBox, "Pesquisar");
        queryBox.TextChanged += (_, _) => ApplyQuery();
        Children.Add(queryBox);
        // Aviso de resultados para leitores de tela (e dica do campo): no cabeçalho não há linha de texto para mostrá-lo.
        resultCount = new TextBlock { Width = 1, Height = 1, Opacity = 0, IsHitTestVisible = false, Focusable = false, Visibility = Visibility.Collapsed, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom };
        AutomationProperties.SetLiveSetting(resultCount, AutomationLiveSetting.Polite);
        Children.Add(resultCount);

        var items = (INotifyCollectionChanged)target.Items;
        Loaded += (_, _) => { items.CollectionChanged += OnItemsChanged; UpdateResultCount(); };
        Unloaded += (_, _) => items.CollectionChanged -= OnItemsChanged;
    }

    /// <summary>Nome acessível do campo: diz qual lista é pesquisada ("Pesquisar: Profissionais").</summary>
    internal void Describe(string label) => AutomationProperties.SetName(queryBox, label.Length > 0 ? $"Pesquisar: {label}" : "Pesquisar");

    internal void Clear() => queryBox.Clear();

    protected override Size MeasureOverride(Size constraint)
    {
        var size = base.MeasureOverride(constraint);
        return new Size(double.IsInfinity(constraint.Width) ? PreferredWidth : Math.Min(constraint.Width, PreferredWidth), size.Height);
    }

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
        if (query.Length == 0) { queryBox.ClearValue(ToolTipProperty); return; }
        resultCount.Text = target.Items.Count switch
        {
            0 => "Nenhum resultado nesta lista.",
            1 => "1 resultado nesta lista.",
            var count => $"{count} resultados nesta lista."
        };
        queryBox.ToolTip = resultCount.Text;
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
