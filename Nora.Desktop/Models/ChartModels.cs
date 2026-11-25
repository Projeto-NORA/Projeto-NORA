namespace Nora.Desktop.Models;

/// <summary>Um ponto da linha (rótulo do eixo e valor) ou uma fatia da pizza.</summary>
public sealed record ChartPoint(string Label, double Value)
{
    public string ValueDisplay => Value % 1 == 0 ? Value.ToString("N0", Services.DisplayFormat.Culture) : Value.ToString("N1", Services.DisplayFormat.Culture);
}

/// <summary>Gráfico de um cartão de indicador: série da linha (evolução) e composição da pizza, com uma nota sobre a origem dos dados.</summary>
public sealed record IndicatorChart(string Title, string Value, IReadOnlyList<ChartPoint> Line, string LineNote, IReadOnlyList<ChartPoint> Pie, string PieNote);
