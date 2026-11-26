using System.Windows;
using System.Windows.Controls;

namespace Nora.Desktop.Views;

/// <summary>
/// Tela que ocupa a altura inteira da área de conteúdo (<see cref="PageLayout.FillProperty"/>): enquanto alguma está visível, a rolagem da página fica
/// desligada e a marca d'água sai, para a listagem rolar sozinha e o balão de ações ficar no pé da tela. Sem nenhuma, a página rola como sempre.
/// </summary>
public partial class ShellWindow
{
    private readonly List<FrameworkElement> fillPages = [];

    internal void RegisterFillPage(FrameworkElement page)
    {
        if (!fillPages.Contains(page)) fillPages.Add(page);
        UpdatePageFill();
    }

    internal void UnregisterFillPage(FrameworkElement page)
    {
        fillPages.Remove(page);
        UpdatePageFill();
    }

    internal void UpdatePageFill()
    {
        if (PageScroll is null) return;
        var fill = fillPages.Any(page => page.IsVisible);
        PageScroll.VerticalScrollBarVisibility = fill ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;
        // Linha Auto: a tela vale o que o conteúdo pede (como antes, com a rolagem). Linha *: a tela recebe a altura inteira da área de conteúdo.
        PageRow.Height = fill ? new GridLength(1, GridUnitType.Star) : GridLength.Auto;
        // A marca d'água fica no fim da página rolável; sem rolagem ela ocuparia a altura da lista e não teria como ser vista.
        Watermark.Visibility = fill ? Visibility.Collapsed : Visibility.Visible;
    }
}
