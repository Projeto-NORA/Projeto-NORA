namespace Nora.Desktop.Models;

/// <summary>
/// Um ponto da linha (rótulo do eixo e valor) ou uma fatia da pizza. <paramref name="BrushKey"/> é a chave de cor do tema da fatia (sem ela vale a paleta
/// <c>ChartBrush1..6</c> pela posição); <paramref name="Tip"/> é o texto da dica ao passar o mouse (sem ele a dica é montada com o rótulo e o valor).
/// </summary>
public sealed record ChartPoint(string Label, double Value, string? BrushKey = null, string? Tip = null)
{
    public string ValueDisplay => Value % 1 == 0 ? Value.ToString("N0", Services.DisplayFormat.Culture) : Value.ToString("N1", Services.DisplayFormat.Culture);
}

/// <summary>Uma linha do gráfico: o nome (legenda), a chave de cor do tema, os pontos e, quando a linha é tracejada, o padrão do traço (reforço para quem não distingue cores).</summary>
public sealed record ChartLine(string Name, string BrushKey, IReadOnlyList<ChartPoint> Points, double[]? Dash = null);

/// <summary>Qual desenho um <c>ChartPanel</c> mostra.</summary>
public enum ChartKind { Line, Pie }

/// <summary>Gráfico de um cartão de indicador: série da linha (evolução) e composição da pizza, com uma nota sobre a origem dos dados.</summary>
public sealed record IndicatorChart(string Title, IReadOnlyList<ChartPoint> Line, string LineNote, IReadOnlyList<ChartPoint> Pie, string PieNote);
