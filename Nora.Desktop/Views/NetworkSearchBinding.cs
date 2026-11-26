using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using Nora.Desktop.ViewModels;

namespace Nora.Desktop.Views;

/// <summary>
/// Liga a pesquisa em linha do cabeçalho (<see cref="WorkspaceViewModel.NetworkSearch"/>) aos cartões, gráficos, listas e tabelas de uma tela da rede: cada
/// lista observada ganha um filtro pelos campos que a tela apresenta (sem diferenciar maiúsculas nem acentos) e um aviso diz quando nada combina com a busca.
/// Liga ao carregar a tela e desliga ao sair, sem deixar assinatura nem filtro para trás (a coleção de cartões é a mesma em telas diferentes, então só se
/// retira o filtro que foi esta ligação que pôs).
/// </summary>
internal sealed class NetworkSearchBinding
{
    private sealed class Target(ItemsControl list, string fieldNames)
    {
        public ItemsControl List { get; } = list;
        public string FieldNames { get; } = fieldNames;
        public Predicate<object>? Filter { get; set; }
    }

    private readonly FrameworkElement owner;
    private readonly string screen;
    private readonly TextBlock? emptyText;
    private readonly List<Target> targets = [];
    private WorkspaceViewModel? workspace;

    /// <param name="owner">A tela: a pesquisa vale enquanto ela está carregada.</param>
    /// <param name="screen">Chave da tela (a rota): cada tela guarda a própria pesquisa.</param>
    /// <param name="emptyText">Aviso mostrado quando há busca e nenhuma lista observada tem resultado.</param>
    public NetworkSearchBinding(FrameworkElement owner, string screen, TextBlock? emptyText = null)
    {
        this.owner = owner;
        this.screen = screen;
        this.emptyText = emptyText;
        owner.Loaded += (_, _) => Attach();
        owner.Unloaded += (_, _) => Detach();
        owner.DataContextChanged += (_, _) => { if (owner.IsLoaded) Attach(); };
    }

    /// <summary>Observa uma lista: <paramref name="fields"/> são as propriedades do item (separadas por vírgula) em que a busca procura.</summary>
    public NetworkSearchBinding Watch(ItemsControl list, string fields)
    {
        targets.Add(new Target(list, fields));
        if (owner.IsLoaded) Attach();
        return this;
    }

    private void Attach()
    {
        Detach();
        workspace = owner.DataContext as WorkspaceViewModel;
        if (workspace is null) return;
        workspace.EnterNetworkSearch(this, screen);
        workspace.PropertyChanged += OnWorkspaceChanged;
        foreach (var target in targets) ((INotifyCollectionChanged)target.List.Items).CollectionChanged += OnItemsChanged;
        Apply();
    }

    private void Detach()
    {
        foreach (var target in targets)
        {
            ((INotifyCollectionChanged)target.List.Items).CollectionChanged -= OnItemsChanged;
            if (target.Filter is not null && ReferenceEquals(target.List.Items.Filter, target.Filter)) target.List.Items.Filter = null;
            target.Filter = null;
        }
        if (workspace is not null) { workspace.PropertyChanged -= OnWorkspaceChanged; workspace.LeaveNetworkSearch(this); }
        workspace = null;
        if (emptyText is not null) emptyText.Visibility = Visibility.Collapsed;
    }

    private void OnWorkspaceChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(WorkspaceViewModel.NetworkSearch)) Apply();
    }

    private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs args) => UpdateEmptyText();

    private void Apply()
    {
        var text = workspace?.NetworkSearch.Trim() ?? "";
        foreach (var target in targets)
        {
            if (!target.List.Items.CanFilter) continue;
            var fields = new Dictionary<Type, PropertyInfo[]>();
            target.Filter = text.Length == 0 ? null : item => Matches(item, text, target.FieldNames, fields);
            target.List.Items.Filter = target.Filter;
        }
        UpdateEmptyText();
    }

    private void UpdateEmptyText()
    {
        if (emptyText is null) return;
        var searching = workspace?.NetworkSearch.Trim().Length > 0;
        var anySource = targets.Any(target => SourceCount(target.List) > 0);
        emptyText.Visibility = searching && anySource && targets.All(target => target.List.Items.Count == 0) ? Visibility.Visible : Visibility.Collapsed;
    }

    private static int SourceCount(ItemsControl list) => list.ItemsSource is ICollection collection ? collection.Count : list.Items.SourceCollection is ICollection source ? source.Count : 0;

    private static bool Matches(object item, string text, string fieldNames, Dictionary<Type, PropertyInfo[]> cache)
    {
        var type = item.GetType();
        if (!cache.TryGetValue(type, out var fields))
        {
            fields = [.. fieldNames.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(name => type.GetProperty(name)).OfType<PropertyInfo>()];
            cache[type] = fields;
        }
        var comparison = CultureInfo.CurrentCulture.CompareInfo;
        return fields.Any(field => field.GetValue(item) is { } value
            && comparison.IndexOf(Convert.ToString(value, CultureInfo.CurrentCulture) ?? "", text, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0);
    }
}
