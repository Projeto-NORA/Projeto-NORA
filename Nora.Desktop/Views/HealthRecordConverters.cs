using System.Globalization;
using System.Windows.Data;
using Nora.Desktop.Models;

namespace Nora.Desktop.Views;

/// <summary>Junta com " | " só os textos informados (a linha de dados do item da lista: o que está vazio não deixa separador sobrando).</summary>
public sealed class RecordMetaLineConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) =>
        string.Join(" | ", values.OfType<string>().Where(text => !string.IsNullOrWhiteSpace(text)));

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Tom da etiqueta de situação de um exame ou de uma vacina (solicitado azul, realizado e aplicada verde, cancelado amarelo, anulado neutro).</summary>
public sealed class RecordStatusToneConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => (value as string) switch
    {
        "requested" => Tone.Blue,
        "done" or "applied" => Tone.Green,
        "cancelled" => Tone.Yellow,
        _ => Tone.Neutral
    };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
