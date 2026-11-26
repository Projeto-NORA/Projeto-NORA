using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Nora.Desktop.Views;

/// <summary>
/// Lista aberta dentro de uma tela que rola (Configurações, a aba da ficha): a roda do mouse sobre a lista rola a lista enquanto ela tem para onde ir e,
/// no começo, no fim ou quando cabe inteira, passa para a rolagem da tela. Sem isto o <c>ScrollViewer</c> da lista engole a roda e a tela "trava" sobre ela.
/// Uso: <c>views:NestedListScroll.PassWheelToParent="True"</c> na lista.
/// </summary>
public static class NestedListScroll
{
    public static readonly DependencyProperty PassWheelToParentProperty = DependencyProperty.RegisterAttached("PassWheelToParent", typeof(bool), typeof(NestedListScroll), new PropertyMetadata(false, OnChanged));

    public static bool GetPassWheelToParent(DependencyObject element) => (bool)element.GetValue(PassWheelToParentProperty);
    public static void SetPassWheelToParent(DependencyObject element, bool value) => element.SetValue(PassWheelToParentProperty, value);

    private static void OnChanged(DependencyObject element, DependencyPropertyChangedEventArgs args)
    {
        if (element is not UIElement list) return;
        list.PreviewMouseWheel -= OnWheel;
        if (args.NewValue is true) list.PreviewMouseWheel += OnWheel;
    }

    private static void OnWheel(object sender, MouseWheelEventArgs args)
    {
        if (args.Handled || sender is not DependencyObject list || FindScrollViewer(list) is not { } scroll) return;
        var canScroll = scroll.ScrollableHeight > 0 && !(args.Delta > 0 && scroll.VerticalOffset <= 0) && !(args.Delta < 0 && scroll.VerticalOffset >= scroll.ScrollableHeight);
        if (canScroll || VisualTreeHelper.GetParent(list) is not UIElement parent) return;
        args.Handled = true;
        parent.RaiseEvent(new MouseWheelEventArgs(args.MouseDevice, args.Timestamp, args.Delta) { RoutedEvent = UIElement.MouseWheelEvent, Source = sender });
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject node)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(node); index++)
        {
            var child = VisualTreeHelper.GetChild(node, index);
            if (child is ScrollViewer viewer) return viewer;
            if (FindScrollViewer(child) is { } inner) return inner;
        }
        return null;
    }
}
