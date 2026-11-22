using System.Globalization;

namespace Nora.Desktop.Services;

/// <summary>Formatos de exibição em português do Brasil, independentes da cultura do Windows.</summary>
public static class DisplayFormat
{
    public static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>Score de 0 a 1 como percentual com uma casa; "—" quando não há consulta encerrada para calcular.</summary>
    public static string Percent(double? score) => score is { } value ? (value * 100).ToString("0.0", Culture) + "%" : "—";

    /// <summary>Score do paciente em pontos de 0 a 1000 (comparecimento × 1000, arredondado para cima no meio); quem não tem consulta encerrada começa em 1000.</summary>
    public static int PointsFromScore(double? score) => score is { } value ? (int)Math.Round(Math.Clamp(value, 0, 1) * 1000, MidpointRounding.AwayFromZero) : 1000;

    /// <summary>Faixa do score: baixo 0–299, regular 300–599, bom 600–799, excelente 800–1000 (mesmos nomes da API).</summary>
    public static string BandFor(int points) => points switch { < 300 => "low", < 600 => "regular", < 800 => "good", _ => "excellent" };

    public static string BandLabel(string? band) => band switch { "low" => "Baixo", "regular" => "Regular", "good" => "Bom", "excellent" => "Excelente", _ => "—" };

    /// <summary>"742 · Bom"; sem consulta encerrada, "1000 · sem consultas encerradas ainda".</summary>
    public static string ScoreText(int points, string? band, bool hasHistory) =>
        hasHistory ? $"{points} · {BandLabel(band ?? BandFor(points))}" : $"{points} · sem consultas encerradas ainda";

    public static string Count(long value) => value.ToString("N0", Culture);

    /// <summary>Minutos em unidades legíveis: "menos de 1 min", "45 min", "3 h 20 min", "2 d 4 h".</summary>
    public static string Wait(double? minutes)
    {
        if (minutes is not { } value || value < 0) return "—";
        var total = (long)Math.Round(value);
        if (total < 1) return "menos de 1 min";
        if (total < 60) return $"{total} min";
        if (total < 1440) return total % 60 == 0 ? $"{total / 60} h" : $"{total / 60} h {total % 60} min";
        var hours = total % 1440 / 60;
        return hours == 0 ? $"{total / 1440} d" : $"{total / 1440} d {hours} h";
    }

    public static string Date(DateOnly date) => date.ToString("dd/MM/yyyy", Culture);

    public static bool TryParseDate(string? text, out DateTime date) =>
        DateTime.TryParseExact(text?.Trim(), "dd/MM/yyyy", Culture, DateTimeStyles.None, out date);
}
