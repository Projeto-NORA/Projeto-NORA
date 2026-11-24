using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;

namespace Nora.Desktop.Views;

[ContentProperty(nameof(Fields))]
public sealed class PageFilterGroup : Decorator
{
    private ShellWindow? shell;

    public string Label { get; set; } = "";
    public string FilterKey { get; set; } = "";
    public FrameworkElement? Fields { get; set; }
    public string SearchFields { get; set; } = "";

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
        if (shell is null) Child = Fields;
        else shell.UpdatePageFilters();
    }
}
