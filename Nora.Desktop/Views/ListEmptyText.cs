using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace Nora.Desktop.Views;

/// <summary>
/// Texto do estado vazio de uma listagem aberta. Sem itens na fonte mostra <see cref="EmptyText"/> (o que a tela diz: "Nenhuma UBS cadastrada.",
/// carregando, falha); com itens na fonte e nenhum depois da pesquisa mostra <see cref="NoMatchText"/> ("Nenhuma UBS encontrada para esta busca.");
/// com itens à vista some. Fica na mesma célula da lista, no alto, e não recebe clique.
/// </summary>
public sealed class ListEmptyText : TextBlock
{
    public static readonly DependencyProperty ListProperty = DependencyProperty.Register(nameof(List), typeof(ItemsControl), typeof(ListEmptyText), new PropertyMetadata(null, (sender, args) => ((ListEmptyText)sender).Rewire((ItemsControl?)args.OldValue, (ItemsControl?)args.NewValue)));
    public static readonly DependencyProperty EmptyTextProperty = DependencyProperty.Register(nameof(EmptyText), typeof(string), typeof(ListEmptyText), new PropertyMetadata("", (sender, _) => ((ListEmptyText)sender).Update()));
    public static readonly DependencyProperty NoMatchTextProperty = DependencyProperty.Register(nameof(NoMatchText), typeof(string), typeof(ListEmptyText), new PropertyMetadata("Nenhum resultado para esta busca.", (sender, _) => ((ListEmptyText)sender).Update()));

    private static readonly DependencyPropertyDescriptor ItemsSourceDescriptor = DependencyPropertyDescriptor.FromProperty(ItemsControl.ItemsSourceProperty, typeof(ItemsControl));
    private INotifyCollectionChanged? watched;
    private ItemsControl? wiredList;

    public ListEmptyText()
    {
        IsHitTestVisible = false;
        Focusable = false;
        TextWrapping = TextWrapping.Wrap;
        Visibility = Visibility.Collapsed;
        System.Windows.Automation.AutomationProperties.SetLiveSetting(this, System.Windows.Automation.AutomationLiveSetting.Polite);
        Loaded += (_, _) => Rewire(null, List);
        Unloaded += (_, _) => { Unwire(); Spin(false); };
    }

    /// <summary>A listagem acompanhada.</summary>
    public ItemsControl? List { get => (ItemsControl?)GetValue(ListProperty); set => SetValue(ListProperty, value); }
    /// <summary>Texto quando a fonte está vazia (o estado que o ViewModel conta).</summary>
    public string EmptyText { get => (string)GetValue(EmptyTextProperty); set => SetValue(EmptyTextProperty, value ?? ""); }
    /// <summary>Texto quando há itens, mas a pesquisa escondeu todos.</summary>
    public string NoMatchText { get => (string)GetValue(NoMatchTextProperty); set => SetValue(NoMatchTextProperty, value ?? ""); }

    private void Rewire(ItemsControl? previous, ItemsControl? current)
    {
        Unwire();
        if (current is null || !IsLoaded) { Update(); return; }
        wiredList = current;
        watched = current.Items;
        watched.CollectionChanged += OnItemsChanged;
        ItemsSourceDescriptor.AddValueChanged(current, OnItemsSourceChanged);
        current.IsVisibleChanged += OnListVisibleChanged;
        Update();
    }

    private void Unwire()
    {
        if (watched is not null) watched.CollectionChanged -= OnItemsChanged;
        watched = null;
        if (wiredList is not { } list) return;
        wiredList = null;
        ItemsSourceDescriptor.RemoveValueChanged(list, OnItemsSourceChanged);
        list.IsVisibleChanged -= OnListVisibleChanged;
    }

    private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs args) => Update();
    private void OnItemsSourceChanged(object? sender, EventArgs args) => Update();
    private void OnListVisibleChanged(object sender, DependencyPropertyChangedEventArgs args) => Update();

    private void Update()
    {
        // A listagem escondida (a outra aba de uma tela com duas listagens) leva o texto junto.
        if (List is { IsVisible: false } || (List?.Items.Count ?? 0) > 0) { Visibility = Visibility.Collapsed; Spin(false); return; }
        var text = HasSource(List) ? NoMatchText : EmptyText;
        Show(text);
        Visibility = text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // ----- carregando: o passo escrito com o indicador girando na cor da marca -----

    private Path? spinner;
    private string shown = "";
    private bool spinning;

    /// <summary>O estado de carga do ViewModel ("Carregando as contas…", "Lendo …") ganha o indicador girando à esquerda; os demais textos aparecem sozinhos.</summary>
    private static bool IsLoading(string text) => text.StartsWith("Carregando", StringComparison.Ordinal) || text.StartsWith("Lendo", StringComparison.Ordinal);

    private void Show(string text)
    {
        var loading = IsLoading(text);
        if (text == shown && loading == spinning) return;
        shown = text;
        if (!loading) { Spin(false); Text = text; return; }
        spinner ??= CreateSpinner();
        Inlines.Clear();
        Inlines.Add(new InlineUIContainer(spinner) { BaselineAlignment = BaselineAlignment.Center });
        Inlines.Add(new Run(text));
        Spin(true);
    }

    private void Spin(bool on)
    {
        if (spinner?.RenderTransform is not RotateTransform rotation || on == spinning) return;
        spinning = on;
        rotation.BeginAnimation(RotateTransform.AngleProperty, on ? new DoubleAnimation(0, 360, TimeSpan.FromSeconds(1)) { RepeatBehavior = RepeatBehavior.Forever } : null);
    }

    private static Path CreateSpinner()
    {
        var path = new Path
        {
            Data = Geometry.Parse("M 8,1.5 A 6.5,6.5 0 1 1 1.5,8"), StrokeThickness = 2.2, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
            Width = 16, Height = 16, Margin = new Thickness(0, 0, 8, 0), RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new RotateTransform()
        };
        path.SetResourceReference(Shape.StrokeProperty, "BrandBrush");
        return path;
    }

    /// <summary>A fonte tem itens (antes da pesquisa)?</summary>
    private static bool HasSource(ItemsControl? list)
    {
        if (list?.Items.SourceCollection is not { } source) return false;
        if (source is ICollection collection) return collection.Count > 0;
        foreach (var _ in source) return true;
        return false;
    }
}
