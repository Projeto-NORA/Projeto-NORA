using System.Globalization;
using System.Windows.Data;
using Nora.Desktop.Models;

namespace Nora.Desktop.Views;

/// <summary>
/// Textos de uma consulta nas listas e na grade da Agenda, sempre na hora local. Sem parâmetro (ou "range"): "08:20 às 08:40". Com "summary": o nome acessível da linha,
/// "Ana Souza, 08:20 às 08:40, Agendada". Com "grid": "08:20 – 08:40", mais curto para o bloco da semana.
/// </summary>
public sealed class AppointmentTextConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not AppointmentInfo item) return "";
        var begins = item.StartsAt.ToLocalTime(); var ends = item.EndsAt.ToLocalTime();
        return (parameter as string) switch
        {
            "summary" => $"{item.PatientName}, {begins:HH:mm} às {ends:HH:mm}, {item.StatusDisplay}",
            "grid" => $"{begins:HH:mm} – {ends:HH:mm}",
            _ => $"{begins:HH:mm} às {ends:HH:mm}"
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>A situação da consulta no tom da etiqueta: Agendada azul, Chamada violeta, Concluída verde, Falta rosa e Cancelada amarelo (as mesmas cores da legenda e do calendário).</summary>
public sealed class AppointmentToneConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        "scheduled" => Tone.Blue,
        "called" => Tone.Violet,
        "completed" => Tone.Green,
        "missed" => Tone.Pink,
        "cancelled" => Tone.Yellow,
        _ => Tone.Neutral
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>O horário do bloco da semana só cabe quando o bloco tem largura para ele (com três consultas lado a lado a faixa fica estreita e só o nome aparece; o resto está na dica).</summary>
public sealed class WideBlockConverter : IValueConverter
{
    private const double MinWidth = 86;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is double width && width < MinWidth ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
