using System.Windows;
using System.Windows.Controls;

namespace Nora.Desktop.Views;

/// <summary>
/// Painel dos cadastros: campos lado a lado, todos com a mesma largura. A quantidade de colunas vem da largura disponível
/// (<see cref="ColumnsFor"/>); a última linha incompleta fica centralizada, com os campos do mesmo tamanho dos demais.
/// <c>FormGrid.FullRow="True"</c> faz o campo ocupar a linha inteira (texto longo, seção, linha de botões).
/// </summary>
public sealed class FormGrid : Panel
{
    public static readonly DependencyProperty MinColumnWidthProperty = Register(nameof(MinColumnWidth), 300d);
    public static readonly DependencyProperty MaxColumnsProperty = DependencyProperty.Register(nameof(MaxColumns), typeof(int), typeof(FormGrid),
        new FrameworkPropertyMetadata(3, FrameworkPropertyMetadataOptions.AffectsMeasure));
    public static readonly DependencyProperty ColumnSpacingProperty = Register(nameof(ColumnSpacing), 16d);
    public static readonly DependencyProperty RowSpacingProperty = Register(nameof(RowSpacing), 12d);
    public static readonly DependencyProperty FullRowProperty = DependencyProperty.RegisterAttached("FullRow", typeof(bool), typeof(FormGrid),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsParentMeasure));

    public double MinColumnWidth { get => (double)GetValue(MinColumnWidthProperty); set => SetValue(MinColumnWidthProperty, value); }
    public int MaxColumns { get => (int)GetValue(MaxColumnsProperty); set => SetValue(MaxColumnsProperty, value); }
    public double ColumnSpacing { get => (double)GetValue(ColumnSpacingProperty); set => SetValue(ColumnSpacingProperty, value); }
    public double RowSpacing { get => (double)GetValue(RowSpacingProperty); set => SetValue(RowSpacingProperty, value); }
    public static bool GetFullRow(DependencyObject element) => (bool)element.GetValue(FullRowProperty);
    public static void SetFullRow(DependencyObject element, bool value) => element.SetValue(FullRowProperty, value);

    private static DependencyProperty Register(string name, double value) => DependencyProperty.Register(name, typeof(double), typeof(FormGrid),
        new FrameworkPropertyMetadata(value, FrameworkPropertyMetadataOptions.AffectsMeasure));

    /// <summary>
    /// Quantas colunas cabem numa largura. A regra é do usuário: cada campo quer cerca de <paramref name="minColumnWidth"/> px;
    /// se couberem 2,5 campos ficam 2 (maiores), nunca 3 apertados; nunca menos de 1 nem mais de <paramref name="maxColumns"/>.
    /// </summary>
    public static int ColumnsFor(double width, double minColumnWidth, double spacing, int maxColumns)
    {
        // TODO(human): calcular as colunas a partir da largura. Enquanto isso, uma coluna só (funciona, mas não fica lado a lado).
        return 1;
    }

    protected override Size MeasureOverride(Size available)
    {
        var width = double.IsInfinity(available.Width) ? MinColumnWidth * MaxColumns + ColumnSpacing * (MaxColumns - 1) : available.Width;
        var (columnWidth, rows) = Layout(width);
        var height = 0d;
        foreach (var row in rows)
        {
            var rowHeight = 0d;
            foreach (var child in row.Items)
            {
                child.Measure(new Size(row.Full ? width : columnWidth, double.PositiveInfinity));
                rowHeight = Math.Max(rowHeight, child.DesiredSize.Height);
            }
            height += rowHeight;
        }
        return new Size(width, height + RowSpacing * Math.Max(0, rows.Count - 1));
    }

    protected override Size ArrangeOverride(Size final)
    {
        var (columnWidth, rows) = Layout(final.Width);
        var top = 0d;
        foreach (var row in rows)
        {
            var rowHeight = row.Items.Max(child => child.DesiredSize.Height);
            // Linha incompleta: os campos mantêm a largura da coluna e o conjunto fica no meio.
            var used = row.Full ? final.Width : row.Items.Count * columnWidth + ColumnSpacing * (row.Items.Count - 1);
            var left = (final.Width - used) / 2;
            foreach (var child in row.Items)
            {
                var childWidth = row.Full ? final.Width : columnWidth;
                child.Arrange(new Rect(left, top, childWidth, rowHeight));
                left += childWidth + ColumnSpacing;
            }
            top += rowHeight + RowSpacing;
        }
        return final;
    }

    private (double ColumnWidth, List<Row> Rows) Layout(double width)
    {
        var columns = Math.Clamp(ColumnsFor(width, MinColumnWidth, ColumnSpacing, MaxColumns), 1, Math.Max(1, MaxColumns));
        var columnWidth = Math.Max(0, (width - ColumnSpacing * (columns - 1)) / columns);
        var rows = new List<Row>();
        Row? open = null;
        foreach (UIElement child in InternalChildren)
        {
            if (child.Visibility == Visibility.Collapsed) continue;
            if (GetFullRow(child)) { rows.Add(new Row(true, [child])); open = null; continue; }
            if (open is null || open.Items.Count == columns) rows.Add(open = new Row(false, []));
            open.Items.Add(child);
        }
        return (columnWidth, rows);
    }

    private sealed record Row(bool Full, List<UIElement> Items);
}
