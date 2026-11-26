using System.Windows;
using System.Windows.Controls;

namespace Nora.Desktop.Views;

/// <summary>
/// Linha do cabeçalho da tela. O primeiro filho é o título: fica à esquerda e corta com reticências. Os demais ficam alinhados à direita,
/// na ordem em que aparecem (filtros em linha, botão Filtros, ações da tela). Com a janela estreita, o que declara <c>MinWidth</c> (a
/// pesquisa) encolhe até o mínimo antes de o título ser cortado; os botões nunca encolhem nem somem.
/// </summary>
public sealed class HeaderRow : Panel
{
    public static readonly DependencyProperty GapProperty = DependencyProperty.Register(nameof(Gap), typeof(double), typeof(HeaderRow),
        new FrameworkPropertyMetadata(12.0, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange));

    private double[] widths = [];

    /// <summary>Espaço entre o título e o primeiro item e entre os itens da direita.</summary>
    public double Gap { get => (double)GetValue(GapProperty); set => SetValue(GapProperty, value); }

    protected override Size MeasureOverride(Size available)
    {
        var children = InternalChildren;
        if (children.Count == 0) return default;
        if (widths.Length < children.Count) widths = new double[children.Count];
        var unbounded = new Size(double.PositiveInfinity, available.Height);
        // Largura natural de cada item da direita (índice 0 é o título).
        double natural = 0, shrinkable = 0;
        var shown = 0;
        for (var index = 1; index < children.Count; index++)
        {
            var child = children[index];
            child.Measure(unbounded);
            var width = child.DesiredSize.Width;
            widths[index] = width;
            if (child.Visibility == Visibility.Collapsed || width <= 0) { widths[index] = 0; continue; }
            shown++;
            natural += width;
            shrinkable += Math.Max(0, width - MinimumOf(child, width));
        }
        children[0].Measure(unbounded);
        var title = children[0].DesiredSize.Width;
        var total = title + natural + shown * Gap;
        var titleWidth = title;
        if (!double.IsInfinity(available.Width) && total > available.Width)
        {
            var deficit = total - available.Width;
            var taken = Math.Min(deficit, shrinkable);
            if (taken > 0)
                for (var index = 1; index < children.Count; index++)
                    if (widths[index] > 0) widths[index] -= taken * Math.Max(0, widths[index] - MinimumOf(children[index], widths[index])) / shrinkable;
            titleWidth = Math.Max(0, title - (deficit - taken));
        }
        children[0].Measure(new Size(titleWidth, available.Height));
        for (var index = 1; index < children.Count; index++)
            if (widths[index] > 0) children[index].Measure(new Size(widths[index], available.Height));
        var height = 0.0;
        foreach (UIElement child in children) height = Math.Max(height, child.DesiredSize.Height);
        return new Size(double.IsInfinity(available.Width) ? total : Math.Min(total, available.Width), double.IsInfinity(available.Height) ? height : available.Height);
    }

    protected override Size ArrangeOverride(Size final)
    {
        var children = InternalChildren;
        if (children.Count == 0) return final;
        var x = final.Width;
        for (var index = children.Count - 1; index >= 1; index--)
        {
            if (widths[index] <= 0) { children[index].Arrange(default); continue; }
            x -= widths[index];
            children[index].Arrange(new Rect(x, 0, widths[index], final.Height));
            x -= Gap;
        }
        children[0].Arrange(new Rect(0, 0, Math.Max(0, Math.Min(children[0].DesiredSize.Width, x)), final.Height));
        return final;
    }

    /// <summary>Menor largura de um item: o <c>MinWidth</c> dele (a pesquisa); item sem mínimo é rígido.</summary>
    private static double MinimumOf(UIElement child, double natural) => child is FrameworkElement { MinWidth: > 0 } element ? Math.Min(element.MinWidth, natural) : natural;
}
