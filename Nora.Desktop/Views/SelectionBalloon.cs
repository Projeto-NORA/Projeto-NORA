using System.Windows;
using System.Windows.Controls;

namespace Nora.Desktop.Views;

/// <summary>
/// Balão de ações de uma listagem: rótulo (o nome do item ou "N itens selecionados"), complemento opcional, fio, as ações (o Content), fio e o X que
/// desfaz a seleção. Flutua sobre a lista, na mesma célula do <c>Grid</c>, embaixo, e nunca reserva espaço nela. Quem o mostra e o esconde é o
/// <see cref="ListingBehavior"/>; o desenho (números e cores da skill list-generator) está em <c>Controls.xaml</c>.
/// </summary>
/// <remarks>
/// O Content é um painel com os botões (<c>ListActionButton</c>, <c>ListActionEditButton</c>, <c>ListActionDangerButton</c>). Botão que não se aplica ao
/// que está selecionado fica <c>Collapsed</c> e o balão esconde o fio junto: <see cref="SingleItemOnlyProperty"/> marca o que só vale com um item.
/// </remarks>
public sealed class SelectionBalloon : ContentControl
{
    public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(nameof(Label), typeof(string), typeof(SelectionBalloon), new PropertyMetadata(""));
    public static readonly DependencyProperty ComplementProperty = DependencyProperty.Register(nameof(Complement), typeof(string), typeof(SelectionBalloon), new PropertyMetadata(""));
    private static readonly DependencyPropertyKey HasActionsPropertyKey = DependencyProperty.RegisterReadOnly(nameof(HasActions), typeof(bool), typeof(SelectionBalloon), new PropertyMetadata(false));
    public static readonly DependencyProperty HasActionsProperty = HasActionsPropertyKey.DependencyProperty;
    public static readonly DependencyProperty SingleItemOnlyProperty = DependencyProperty.RegisterAttached("SingleItemOnly", typeof(bool), typeof(SelectionBalloon), new PropertyMetadata(false));
    public static readonly RoutedEvent CloseRequestedEvent = EventManager.RegisterRoutedEvent(nameof(CloseRequested), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(SelectionBalloon));

    private Button? closeButton;
    private readonly List<FrameworkElement> watched = [];

    static SelectionBalloon() => DefaultStyleKeyProperty.OverrideMetadata(typeof(SelectionBalloon), new FrameworkPropertyMetadata(typeof(SelectionBalloon)));

    public SelectionBalloon() => Loaded += (_, _) => { SyncWatchers(); RefreshActions(); };

    /// <summary>O nome do item selecionado, ou "N itens selecionados".</summary>
    public string Label { get => (string)GetValue(LabelProperty); set => SetValue(LabelProperty, value ?? ""); }
    /// <summary>Texto secundário opcional ao lado do rótulo (ex.: o CPF); some quando vazio.</summary>
    public string Complement { get => (string)GetValue(ComplementProperty); set => SetValue(ComplementProperty, value ?? ""); }
    /// <summary>Há pelo menos uma ação visível? Sem ação visível, o balão não mostra o fio nem a área de ações.</summary>
    public bool HasActions => (bool)GetValue(HasActionsProperty);

    /// <summary>O X (ou Esc) pediu para desfazer a seleção.</summary>
    public event RoutedEventHandler CloseRequested { add => AddHandler(CloseRequestedEvent, value); remove => RemoveHandler(CloseRequestedEvent, value); }

    /// <summary>Ação que só vale com um item selecionado: some com vários (o fio some junto se não sobrar ação).</summary>
    public static bool GetSingleItemOnly(DependencyObject element) => (bool)element.GetValue(SingleItemOnlyProperty);
    public static void SetSingleItemOnly(DependencyObject element, bool value) => element.SetValue(SingleItemOnlyProperty, value);

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        if (closeButton is not null) closeButton.Click -= OnCloseClick;
        closeButton = GetTemplateChild("PART_Close") as Button;
        if (closeButton is not null) closeButton.Click += OnCloseClick;
    }

    private void OnCloseClick(object sender, RoutedEventArgs args) => RaiseEvent(new RoutedEventArgs(CloseRequestedEvent, this));

    protected override void OnContentChanged(object oldContent, object newContent)
    {
        base.OnContentChanged(oldContent, newContent);
        SyncWatchers();
        RefreshActions();
    }

    /// <summary>Acompanha a visibilidade de cada botão: quando um aparece ou some, o fio e a área de ações se ajustam. O XAML pode montar o painel depois de atribuí-lo ao Content, por isso a lista é refeita quando preciso.</summary>
    private void SyncWatchers()
    {
        var current = ActionElements(Content).ToList();
        foreach (var gone in watched.Except(current).ToList()) { gone.IsVisibleChanged -= OnActionVisibilityChanged; watched.Remove(gone); }
        foreach (var added in current.Except(watched).ToList()) { added.IsVisibleChanged += OnActionVisibilityChanged; watched.Add(added); }
    }

    private void OnActionVisibilityChanged(object sender, DependencyPropertyChangedEventArgs args) => RefreshActions();

    /// <summary>Recalcula se há ação visível (e, com isso, o fio e a área de ações).</summary>
    public void RefreshActions() => SetValue(HasActionsPropertyKey, Content is not null && (Content is not FrameworkElement root || AnyVisible(root)));

    /// <summary>Esconde as ações que só valem com um item quando há vários selecionados; chamado pelo <see cref="ListingBehavior"/>.</summary>
    internal void ApplySelectionCount(int count)
    {
        SyncWatchers();
        foreach (var element in ActionElements(Content))
            if (GetSingleItemOnly(element)) element.Visibility = count == 1 ? Visibility.Visible : Visibility.Collapsed;
        RefreshActions();
    }

    /// <summary>Os botões do Content: os filhos do painel (e dos painéis dentro dele) ou o próprio Content.</summary>
    private static IEnumerable<FrameworkElement> ActionElements(object? content)
    {
        if (content is Panel panel)
        {
            foreach (var child in panel.Children.OfType<FrameworkElement>())
                if (child is Panel) { foreach (var inner in ActionElements(child)) yield return inner; }
                else yield return child;
        }
        else if (content is FrameworkElement element) yield return element;
    }

    private static bool AnyVisible(FrameworkElement root)
    {
        if (root.Visibility != Visibility.Visible) return false;
        return root is Panel panel ? panel.Children.OfType<FrameworkElement>().Any(AnyVisible) : true;
    }
}
