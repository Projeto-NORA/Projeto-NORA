using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Nora.Desktop.Views;

/// <summary>
/// Alternador de segmentos (Linha | Pizza, Dia | Semana | Mês | Ano): um segmento sempre selecionado, escolhido por clique ou pelas setas.
/// <c>ItemsSource</c> traz os segmentos (textos, ou objetos com <c>DisplayMemberPath</c>) e <c>SelectedItem</c> / <c>SelectedIndex</c> ligam nos dois sentidos.
/// É uma lista de seleção única: o leitor de tela anuncia o segmento e se está selecionado. O desenho está em <c>Controls.xaml</c>.
/// </summary>
public sealed class SegmentedToggle : ListBox
{
    static SegmentedToggle() => DefaultStyleKeyProperty.OverrideMetadata(typeof(SegmentedToggle), new FrameworkPropertyMetadata(typeof(SegmentedToggle)));

    public SegmentedToggle() => SelectionMode = SelectionMode.Single;

    /// <summary>Setas esquerda e direita (e Home e End) trocam o segmento selecionado e levam o foco junto; no último, a seta para fora não faz nada.</summary>
    protected override void OnKeyDown(KeyEventArgs args)
    {
        var step = args.Key switch { Key.Left or Key.Up => -1, Key.Right or Key.Down => 1, _ => 0 };
        var target = args.Key switch { Key.Home => 0, Key.End => Items.Count - 1, _ => step == 0 ? -1 : Math.Clamp(Math.Max(SelectedIndex, 0) + step, 0, Items.Count - 1) };
        if (target < 0 || Items.Count == 0) { base.OnKeyDown(args); return; }
        args.Handled = true;
        if (target == SelectedIndex) return;
        SelectedIndex = target;
        (ItemContainerGenerator.ContainerFromIndex(target) as UIElement)?.Focus();
    }
}
