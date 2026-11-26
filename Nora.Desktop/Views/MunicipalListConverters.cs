using System.Globalization;
using System.Windows.Data;
using Nora.Desktop.Models;
using Nora.Desktop.Services;

namespace Nora.Desktop.Views;

/// <summary>
/// Linha de dados do item da listagem: junta com " | " só os campos informados (vazios ficam de fora). Uso em XAML:
/// <c>MultiBinding Converter="{StaticResource MetaJoin}"</c> com um <c>Binding</c> por campo.
/// </summary>
public sealed class ListMetaJoinConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) =>
        ListText.Join(values.Select(value => value is null || ReferenceEquals(value, System.Windows.DependencyProperty.UnsetValue) ? null : System.Convert.ToString(value, culture)));

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Texto de uma etiqueta que depende de um booleano (ex.: "Desativado" só para quem está inativo); texto vazio esconde a etiqueta.</summary>
public sealed class ListBoolTextConverter : IValueConverter
{
    public string TrueText { get; set; } = "";
    public string FalseText { get; set; } = "";

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is true ? TrueText : FalseText;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Tom da etiqueta de uma consulta pela chave da situação da API (agendada, chamada, concluída, falta, cancelada).</summary>
public sealed class ListAppointmentToneConverter : IValueConverter
{
    public static Tone For(string? status) => status switch
    {
        "scheduled" => Tone.Blue,
        "called" => Tone.Violet,
        "completed" => Tone.Green,
        "missed" => Tone.Pink,
        "cancelled" => Tone.Yellow,
        _ => Tone.Neutral
    };

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => For(value as string);
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
