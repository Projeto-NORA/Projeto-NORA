using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;

namespace Nora.Desktop.Views;

/// <summary>
/// Lado direito do cabeçalho da tela: os filtros em linha (<see cref="PageFilterGroup"/>) e as ações da tela (<see cref="PageActions"/>)
/// são hospedados enquanto a tela que os declara está visível e saem junto com ela. Ordem na linha: filtros em linha, botão Filtros, ações.
/// </summary>
public partial class ShellWindow
{
    private readonly List<PageActions> pageActions = [];
    private readonly List<(PageActions Owner, ContentControl Host)> actionHosts = [];
    private readonly List<(PageFilterGroup Owner, ContentControl Host)> inlineFilterHosts = [];

    internal void RegisterPageActions(PageActions actions)
    {
        if (!pageActions.Contains(actions)) pageActions.Add(actions);
        UpdatePageActions();
    }

    internal void UnregisterPageActions(PageActions actions)
    {
        pageActions.Remove(actions);
        UpdatePageActions();
    }

    internal void UpdatePageActions()
    {
        if (HeaderItems is null) return;
        var visible = pageActions.Where(item => item.IsVisible && item.Actions is not null).ToArray();
        SyncHeaderHosts(actionHosts, visible, item => item.Actions, _ => "Ações da tela", beforeFilters: false);
    }

    /// <summary>
    /// Deixa no cabeçalho exatamente os hospedeiros dos itens visíveis, na ordem de registro. Itens em linha entram antes do botão Filtros;
    /// as ações entram no fim. Se a lista muda, os hospedeiros são refeitos (o conteúdo é o mesmo elemento da tela, só muda de hospedeiro).
    /// </summary>
    private void SyncHeaderHosts<T>(List<(T Owner, ContentControl Host)> hosts, T[] wanted, Func<T, FrameworkElement?> content, Func<T, string> name, bool beforeFilters) where T : FrameworkElement
    {
        if (hosts.Select(pair => pair.Owner).SequenceEqual(wanted))
        {
            // Mesmos itens: só o conteúdo pode ter sido trocado (a pesquisa é recriada quando a lista de destino muda).
            foreach (var (owner, host) in hosts)
                if (!ReferenceEquals(host.Content, content(owner))) host.Content = content(owner);
            return;
        }
        var items = HeaderItems.Children;
        foreach (var (_, host) in hosts) { items.Remove(host); host.Content = null; BindingOperations.ClearAllBindings(host); }
        hosts.Clear();
        foreach (var owner in wanted)
        {
            var host = CreateHeaderHost(owner, content(owner)!, name(owner));
            hosts.Add((owner, host));
            if (beforeFilters) items.Insert(items.IndexOf(FiltersHost), host);
            else items.Add(host);
        }
    }

    private static ContentControl CreateHeaderHost(FrameworkElement owner, FrameworkElement content, string name)
    {
        var host = new ContentControl { Focusable = false, IsTabStop = false, VerticalAlignment = VerticalAlignment.Center, MinWidth = content.MinWidth };
        // O conteúdo é da tela: herda o contexto de dados e o estado de habilitação do elemento que o declarou.
        host.SetBinding(DataContextProperty, new Binding(nameof(DataContext)) { Source = owner });
        host.SetBinding(IsEnabledProperty, new Binding(nameof(IsEnabled)) { Source = owner });
        AutomationProperties.SetName(host, name);
        host.Content = content;
        return host;
    }
}
