using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;

namespace Nora.Desktop.Views;

/// <summary>
/// Declara os filtros de uma tela. A janela principal os leva para o cabeçalho: grupo só de pesquisa (<see cref="ItemsTarget"/>) e grupo
/// com <see cref="Inline"/> ficam em linha; os demais ficam no pop-up do botão Filtros.
/// </summary>
[ContentProperty(nameof(Fields))]
public sealed class PageFilterGroup : Decorator
{
    private ShellWindow? shell;

    public string Label { get; set; } = "";
    public string FilterKey { get; set; } = "";
    public FrameworkElement? Fields { get; set; }
    public string SearchFields { get; set; } = "";
    /// <summary>Mostra o conteúdo do grupo direto no cabeçalho em vez de no pop-up do botão Filtros (ex.: o período).</summary>
    public bool Inline { get; set; }
    /// <summary>Em linha: o grupo declarado como tal ou o de pesquisa, que é sempre um campo "Pesquisar" no cabeçalho.</summary>
    internal bool IsInline => Inline || Fields is ListSearchFilter;

    public ItemsControl? ItemsTarget
    {
        get => (ItemsControl?)GetValue(ItemsTargetProperty);
        set => SetValue(ItemsTargetProperty, value);
    }
    public static readonly DependencyProperty ItemsTargetProperty = DependencyProperty.Register(
        nameof(ItemsTarget), typeof(ItemsControl), typeof(PageFilterGroup),
        new PropertyMetadata(null, (sender, args) => ((PageFilterGroup)sender).ChangeSearchTarget((ItemsControl?)args.NewValue)));

    public PageFilterGroup()
    {
        Loaded += (_, _) =>
        {
            shell = Window.GetWindow(this) as ShellWindow;
            Child = shell is null ? Fields : null;
            (Fields as ListSearchFilter)?.Describe(Label);
            shell?.RegisterPageFilter(this);
        };
        Unloaded += (_, _) =>
        {
            if (Fields is ListSearchFilter search) search.Clear();
            shell?.UnregisterPageFilter(this);
            shell = null;
        };
        IsVisibleChanged += (_, _) => shell?.UpdatePageFilters();
    }

    protected override void OnInitialized(EventArgs args)
    {
        base.OnInitialized(args);
        // Sem a janela principal, o controle também funciona nas prévias e nas janelas de teste.
        Child = Fields;
    }

    private void ChangeSearchTarget(ItemsControl? target)
    {
        if (Fields is ListSearchFilter previous) previous.Clear();
        Fields = target is null ? null : new ListSearchFilter(target, SearchFields);
        (Fields as ListSearchFilter)?.Describe(Label);
        if (shell is null) Child = Fields;
        else shell.UpdatePageFilters();
    }
}
