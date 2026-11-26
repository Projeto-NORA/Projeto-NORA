using System.Windows;
using System.Windows.Controls;

namespace Nora.Desktop.Views;

/// <summary>
/// Painel vertical da Secretaria (a pilha de <c>MunicipalView</c>): empilha os filhos como um <c>StackPanel</c>, mas o filho marcado com
/// <c>PageLayout.Fill="True"</c> recebe a altura que sobra da área de conteúdo, para a listagem rolar sozinha e o balão de ações ficar no pé da tela.
/// Quando a página rola (altura infinita) ou nenhum filho tem Fill, é um <c>StackPanel</c> comum.
/// </summary>
public sealed class FillStackPanel : Panel
{
    private bool measuredWithFiniteHeight;

    protected override Size MeasureOverride(Size available)
    {
        measuredWithFiniteHeight = !double.IsInfinity(available.Height);
        double used = 0, width = 0;
        foreach (UIElement child in InternalChildren)
        {
            if (child.Visibility == Visibility.Collapsed) continue;
            var room = measuredWithFiniteHeight ? Math.Max(0, available.Height - used) : double.PositiveInfinity;
            child.Measure(new Size(available.Width, room));
            used += child.DesiredSize.Height;
            width = Math.Max(width, child.DesiredSize.Width);
        }
        return new Size(width, used);
    }

    protected override Size ArrangeOverride(Size final)
    {
        double top = 0;
        foreach (UIElement child in InternalChildren)
        {
            if (child.Visibility == Visibility.Collapsed) continue;
            var height = child.DesiredSize.Height;
            // Só o filho com Fill estica, e só quando a medida teve altura finita (a página não rola).
            if (measuredWithFiniteHeight && PageLayout.GetFill(child)) height = Math.Max(height, final.Height - top);
            child.Arrange(new Rect(0, top, final.Width, height));
            top += height;
        }
        return final;
    }
}
