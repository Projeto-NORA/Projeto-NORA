using System.Windows;
using System.Windows.Controls.Primitives;

namespace Nora.Desktop.Views;

/// <summary>
/// Grade de células do mesmo tamanho (gráficos e cartões das telas de números): tantas colunas quantas de <see cref="ColumnWidth"/> couberem, de 1 até
/// <see cref="MaxColumns"/>. Prefere um número de colunas que divida a quantidade de itens (4 cartões ficam 4 ou 2 por linha, nunca 3 e 1 sozinho), e em
/// janela estreita o item passa a ocupar a linha toda em vez de ficar espremido.
/// </summary>
public sealed class AdaptiveGridPanel : UniformGrid
{
    public static readonly DependencyProperty ColumnWidthProperty = DependencyProperty.Register(nameof(ColumnWidth), typeof(double), typeof(AdaptiveGridPanel), new FrameworkPropertyMetadata(430.0, FrameworkPropertyMetadataOptions.AffectsMeasure));
    public static readonly DependencyProperty MaxColumnsProperty = DependencyProperty.Register(nameof(MaxColumns), typeof(int), typeof(AdaptiveGridPanel), new FrameworkPropertyMetadata(2, FrameworkPropertyMetadataOptions.AffectsMeasure));

    /// <summary>Largura mínima de uma coluna.</summary>
    public double ColumnWidth { get => (double)GetValue(ColumnWidthProperty); set => SetValue(ColumnWidthProperty, value); }
    /// <summary>Quantas colunas, no máximo.</summary>
    public int MaxColumns { get => (int)GetValue(MaxColumnsProperty); set => SetValue(MaxColumnsProperty, value); }

    protected override Size MeasureOverride(Size constraint)
    {
        var columns = double.IsInfinity(constraint.Width) ? MaxColumns : (int)Math.Clamp(Math.Floor(constraint.Width / ColumnWidth), 1, Math.Max(1, MaxColumns));
        var count = InternalChildren.Count;
        // Com mais de uma linha, procura (de cima para baixo) o número de colunas que deixa as linhas completas.
        for (var candidate = columns; count > columns && candidate > 1; candidate--)
            if (count % candidate == 0) { columns = candidate; break; }
        if (Columns != columns) Columns = columns;
        return base.MeasureOverride(constraint);
    }
}
