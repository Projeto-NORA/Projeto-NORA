using System.Text;

namespace Nora.Desktop.Models;

/// <summary>
/// Um painel da grade de gráficos do Painel da UBS e dos Indicadores: o título, as linhas e as fatias que o <c>ChartPanel</c> desenha e as notas de origem.
/// Dois painéis com o mesmo conteúdo são iguais (<see cref="Signature"/>), para a tela trocar só o painel que mudou no ciclo de 20 s em vez de recriar a grade.
/// <see cref="Kind"/> é o desenho mostrado e fica fora da comparação: quem monta a lista devolve ao painel novo o desenho que a pessoa tinha escolhido.
/// </summary>
public sealed class UnitChartCard : IEquatable<UnitChartCard>
{
    public UnitChartCard(string title, IReadOnlyList<ChartLine> lines, IReadOnlyList<ChartPoint> slices, string lineNote = "", string pieNote = "", ChartKind kind = ChartKind.Line)
    {
        Title = title; Lines = lines; Slices = slices; LineNote = lineNote; PieNote = pieNote; Kind = kind;
        var text = new StringBuilder(title).Append('|').Append(lineNote).Append('|').Append(pieNote);
        foreach (var line in lines) { text.Append('#').Append(line.Name).Append(line.BrushKey); foreach (var point in line.Points) text.Append(';').Append(point.Label).Append('=').Append(point.Value); }
        foreach (var slice in slices) text.Append('@').Append(slice.Label).Append('=').Append(slice.Value);
        Signature = text.ToString();
    }

    public string Title { get; }
    public IReadOnlyList<ChartLine> Lines { get; }
    public IReadOnlyList<ChartPoint> Slices { get; }
    public string LineNote { get; }
    public string PieNote { get; }
    /// <summary>O desenho mostrado (ligado nos dois sentidos ao <c>ChartPanel.Kind</c>).</summary>
    public ChartKind Kind { get; set; }
    public string Signature { get; }
    /// <summary>Texto da pesquisa do Painel: título e rótulos que o painel mostra.</summary>
    public string SearchText => $"{Title} {string.Join(' ', Lines.Select(line => line.Name))} {string.Join(' ', Slices.Select(slice => slice.Label))}";

    public bool Equals(UnitChartCard? other) => other is not null && Signature == other.Signature;
    public override bool Equals(object? obj) => Equals(obj as UnitChartCard);
    public override int GetHashCode() => Signature.GetHashCode(StringComparison.Ordinal);
}

/// <summary>Uma linha da tabela de consultas do período do Painel da UBS (um dia, uma semana ou um mês, conforme o tamanho do período).</summary>
public sealed record PanelTableRow(string Period, long Completed, long Missed, long Cancelled)
{
    public long Total => Completed + Missed + Cancelled;
    /// <summary>Texto da pesquisa do Painel: o período e os números da linha.</summary>
    public string SearchText => $"{Period} {Completed} {Missed} {Cancelled} {Total}";
}
