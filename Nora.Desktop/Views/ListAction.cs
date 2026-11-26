using System.Windows;
using System.Windows.Media;

namespace Nora.Desktop.Views;

/// <summary>Ícone desenhado dos botões de ação do balão (<c>ListActionButton</c>, <c>ListActionEditButton</c> e <c>ListActionDangerButton</c>). O rótulo curto é o Content do botão.</summary>
public static class ListAction
{
    public static readonly DependencyProperty IconProperty = DependencyProperty.RegisterAttached("Icon", typeof(Geometry), typeof(ListAction), new PropertyMetadata(null));

    public static Geometry? GetIcon(DependencyObject element) => (Geometry?)element.GetValue(IconProperty);
    public static void SetIcon(DependencyObject element, Geometry? value) => element.SetValue(IconProperty, value);
}
