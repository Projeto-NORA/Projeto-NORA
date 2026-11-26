using System.Windows;
using System.Windows.Controls;

namespace Nora.Desktop.Views;

/// <summary>
/// Painel das subtelas de uma página (cada uma com <see cref="SubPage.Key"/>). Com uma subtela só visível ela recebe a altura inteira disponível, para uma listagem com
/// <see cref="PageLayout.Fill"/> rolar sozinha e o balão ficar no pé da tela; com várias visíveis (página aberta sem subtela escolhida) elas ficam empilhadas, cada uma com a altura do conteúdo.
/// </summary>
public sealed class SubPageStack : Panel
{
    private List<UIElement> Visible() => [.. InternalChildren.Cast<UIElement>().Where(child => child.Visibility != Visibility.Collapsed)];

    protected override Size MeasureOverride(Size available)
    {
        var visible = Visible();
        if (visible.Count == 1 && !double.IsInfinity(available.Height) && !double.IsInfinity(available.Width))
        {
            visible[0].Measure(available);
            return available;
        }
        double width = 0, height = 0;
        foreach (var child in visible)
        {
            child.Measure(new Size(available.Width, double.PositiveInfinity));
            width = Math.Max(width, child.DesiredSize.Width);
            height += child.DesiredSize.Height;
        }
        return new Size(double.IsInfinity(available.Width) ? width : available.Width, height);
    }

    protected override Size ArrangeOverride(Size final)
    {
        var visible = Visible();
        if (visible.Count == 1)
        {
            // Um só visível: a altura da área inteira quando ela é finita (a tela com Fill); senão, a do conteúdo.
            visible[0].Arrange(new Rect(0, 0, final.Width, Math.Max(final.Height, 0)));
            return final;
        }
        var top = 0d;
        foreach (var child in visible)
        {
            child.Arrange(new Rect(0, top, final.Width, child.DesiredSize.Height));
            top += child.DesiredSize.Height;
        }
        return final;
    }
}
