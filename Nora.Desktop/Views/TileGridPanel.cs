using System.Windows;
using System.Windows.Controls;

namespace Nora.Desktop.Views;

/// <summary>
/// Grade de blocos (cards de indicador, painéis de gráfico) que escolhe o número de colunas pela largura disponível: quantas de <see cref="MinTileWidth"/>
/// couberem, no máximo <see cref="MaxColumns"/> e no mínimo uma. Todos os blocos de uma linha têm a mesma largura e a altura do mais alto da linha.
/// Quando a última linha ficaria quase vazia, usa uma coluna a menos se isso a deixar mais cheia (9 cards viram 3 x 3 em vez de 4 + 4 + 1).
/// Com menos blocos que colunas (o que sobra de uma pesquisa), eles ficam à esquerda com a largura de uma linha cheia, sem esticar.
/// </summary>
public sealed class TileGridPanel : Panel
{
    public static readonly DependencyProperty MinTileWidthProperty = DependencyProperty.Register(nameof(MinTileWidth), typeof(double), typeof(TileGridPanel),
        new FrameworkPropertyMetadata(240d, FrameworkPropertyMetadataOptions.AffectsMeasure));
    public static readonly DependencyProperty MaxColumnsProperty = DependencyProperty.Register(nameof(MaxColumns), typeof(int), typeof(TileGridPanel),
        new FrameworkPropertyMetadata(4, FrameworkPropertyMetadataOptions.AffectsMeasure));

    /// <summary>Largura mínima de um bloco: abaixo dela a grade perde uma coluna.</summary>
    public double MinTileWidth { get => (double)GetValue(MinTileWidthProperty); set => SetValue(MinTileWidthProperty, value); }
    /// <summary>Número máximo de colunas, por mais largo que seja o espaço.</summary>
    public int MaxColumns { get => (int)GetValue(MaxColumnsProperty); set => SetValue(MaxColumnsProperty, value); }

    /// <summary>Colunas para <paramref name="count"/> blocos em <paramref name="width"/> de largura (função pura, também usada pelas checagens).</summary>
    public static int ColumnsFor(int count, double width, double minTileWidth, int maxColumns)
    {
        var fit = double.IsInfinity(width) ? maxColumns : (int)Math.Floor(width / Math.Max(1, minTileWidth));
        var columns = Math.Clamp(fit, 1, Math.Max(1, maxColumns));
        // Menos blocos que colunas (por exemplo, o que sobrou da pesquisa): cada um mantém a largura de uma linha cheia, em vez de esticar pela tela toda.
        if (count <= columns || columns <= 2) return columns;
        // Uma coluna a menos só vale se deixar menos lugares vazios na última linha.
        var empty = columns * (int)Math.Ceiling(count / (double)columns) - count;
        var fewer = columns - 1;
        var emptyFewer = fewer * (int)Math.Ceiling(count / (double)fewer) - count;
        return emptyFewer < empty ? fewer : columns;
    }

    private int ColumnCount(Size available) => ColumnsFor(InternalChildren.Count, available.Width, MinTileWidth, MaxColumns);

    protected override Size MeasureOverride(Size available)
    {
        var count = InternalChildren.Count;
        if (count == 0) return new Size(0, 0);
        var columns = ColumnCount(available);
        var tileWidth = double.IsInfinity(available.Width) ? MinTileWidth : available.Width / columns;
        var total = 0d;
        for (var start = 0; start < count; start += columns)
        {
            var rowHeight = 0d;
            for (var index = start; index < Math.Min(count, start + columns); index++)
            {
                InternalChildren[index].Measure(new Size(tileWidth, double.PositiveInfinity));
                rowHeight = Math.Max(rowHeight, InternalChildren[index].DesiredSize.Height);
            }
            total += rowHeight;
        }
        return new Size(double.IsInfinity(available.Width) ? tileWidth * Math.Min(columns, count) : available.Width, total);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var count = InternalChildren.Count;
        if (count == 0) return finalSize;
        var columns = ColumnCount(finalSize);
        var tileWidth = finalSize.Width / columns;
        var top = 0d;
        for (var start = 0; start < count; start += columns)
        {
            var rowHeight = 0d;
            for (var index = start; index < Math.Min(count, start + columns); index++) rowHeight = Math.Max(rowHeight, InternalChildren[index].DesiredSize.Height);
            for (var index = start; index < Math.Min(count, start + columns); index++)
                InternalChildren[index].Arrange(new Rect((index - start) * tileWidth, top, tileWidth, rowHeight));
            top += rowHeight;
        }
        return new Size(finalSize.Width, top);
    }
}
