using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Nora.Desktop.Generics;

/// <summary>Texto não vazio aparece; vazio some (mensagens por campo e avisos).</summary>
public sealed class TextVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is string { Length: > 0 } ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Verdadeiro some, falso aparece (o inverso de <see cref="System.Windows.Controls.BooleanToVisibilityConverter"/>).</summary>
public sealed class InverseBoolVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is true ? Visibility.Collapsed : Visibility.Visible;
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
