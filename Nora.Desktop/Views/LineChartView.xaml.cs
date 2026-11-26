using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Nora.Desktop.Models;
using Nora.Desktop.Services;

namespace Nora.Desktop.Views;

/// <summary>
/// Gráfico de linhas das telas e do pop-up do indicador: linha grossa de pontas arredondadas, área em degradê sob a linha, pontos marcados (até 31 pontos),
/// grade discreta, eixos escritos e dica ao passar o mouse com o número exato de cada ponto. Todas as cores vêm das chaves do tema (a troca de tema
/// vale na hora) e o estado nunca depende só da cor: cada linha tem nome na legenda e as secundárias são tracejadas.
/// </summary>
public partial class LineChartView : UserControl
{
    private const int DotLimit = 31;
    private const double LeftMargin = 46, BottomMargin = 30, TopMargin = 12, RightMargin = 14;
    /// <summary>Degradê da área sob a linha: usado como máscara de opacidade, então a cor continua sendo a da chave do tema.</summary>
    private static readonly LinearGradientBrush AreaFade = CreateAreaFade();

    public static readonly DependencyProperty LinesProperty = DependencyProperty.Register(nameof(Lines), typeof(IReadOnlyList<ChartLine>), typeof(LineChartView), new PropertyMetadata(null, (owner, _) => ((LineChartView)owner).RedrawUnlessGrouped()));
    public static readonly DependencyProperty EmptyMessageProperty = DependencyProperty.Register(nameof(EmptyMessage), typeof(string), typeof(LineChartView), new PropertyMetadata("", (owner, _) => ((LineChartView)owner).RedrawUnlessGrouped()));
    public static readonly DependencyProperty SummaryProperty = DependencyProperty.Register(nameof(Summary), typeof(string), typeof(LineChartView), new PropertyMetadata("", (owner, _) => ((LineChartView)owner).RedrawUnlessGrouped()));
    public static readonly DependencyProperty PlotHeightProperty = DependencyProperty.Register(nameof(PlotHeight), typeof(double), typeof(LineChartView), new PropertyMetadata(double.NaN, (owner, args) => ((LineChartView)owner).PlotHost.Height = (double)args.NewValue));

    /// <summary>As linhas do gráfico (todas com o mesmo eixo: o rótulo do ponto i vem da linha mais longa).</summary>
    public IReadOnlyList<ChartLine>? Lines { get => (IReadOnlyList<ChartLine>?)GetValue(LinesProperty); set => SetValue(LinesProperty, value); }
    /// <summary>Texto mostrado no lugar do gráfico quando não há o que desenhar.</summary>
    public string EmptyMessage { get => (string)GetValue(EmptyMessageProperty); set => SetValue(EmptyMessageProperty, value ?? ""); }
    /// <summary>Descrição do gráfico para leitores de tela; vazia, o componente descreve o maior valor de cada linha.</summary>
    public string Summary { get => (string)GetValue(SummaryProperty); set => SetValue(SummaryProperty, value ?? ""); }
    /// <summary>Altura fixa do desenho; sem valor, ele ocupa a altura que sobra (mínimo de 180).</summary>
    public double PlotHeight { get => (double)GetValue(PlotHeightProperty); set => SetValue(PlotHeightProperty, value); }

    public LineChartView()
    {
        InitializeComponent();
        Loaded += (_, _) => Redraw();
        Plot.SizeChanged += (_, _) => Redraw();
    }

    private bool grouping;

    /// <summary>Troca as linhas, o texto de vazio e a descrição de uma vez só, com um único redesenho (em vez de um por propriedade).</summary>
    public void Show(IReadOnlyList<ChartLine>? lines, string emptyMessage, string summary)
    {
        grouping = true;
        try { Lines = lines; EmptyMessage = emptyMessage; Summary = summary; }
        finally { grouping = false; }
        Redraw();
    }

    private void RedrawUnlessGrouped() { if (!grouping) Redraw(); }

    private static LinearGradientBrush CreateAreaFade()
    {
        var brush = new LinearGradientBrush(Color.FromArgb(96, 0, 0, 0), Color.FromArgb(0, 0, 0, 0), 90);
        brush.Freeze();
        return brush;
    }

    private static void Fill(Shape shape, string key) => shape.SetResourceReference(Shape.FillProperty, key);
    private static void Stroke(Shape shape, string key) => shape.SetResourceReference(Shape.StrokeProperty, key);

    private static TextBlock Label(string text, double left, double top, double width, TextAlignment alignment)
    {
        var block = new TextBlock { Text = text, Width = width, TextAlignment = alignment, FontSize = 11, TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis, IsHitTestVisible = false };
        block.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryBrush");
        Canvas.SetLeft(block, left); Canvas.SetTop(block, top);
        return block;
    }

    private static double NiceStep(double raw)
    {
        foreach (var step in new double[] { 1, 2, 5 }.SelectMany(unit => new[] { 1d, 10d, 100d, 1000d, 10000d, 100000d }.Select(scale => unit * scale)).Order()) if (step >= raw) return step;
        return Math.Ceiling(raw);
    }

    private void BuildLegend(IReadOnlyList<ChartLine> lines)
    {
        Legend.Children.Clear();
        Legend.Visibility = lines.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        if (lines.Count < 2) return;
        foreach (var line in lines)
        {
            var item = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 18, 4) };
            var sample = new Line { X1 = 0, X2 = 26, Y1 = 6, Y2 = 6, StrokeThickness = 3, Width = 26, Height = 12, StrokeDashArray = line.Dash is { } dash ? new DoubleCollection(dash) : null, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, Margin = new Thickness(0, 0, 6, 0) };
            Stroke(sample, line.BrushKey); item.Children.Add(sample);
            var name = new TextBlock { Text = line.Name, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
            name.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            item.Children.Add(name);
            Legend.Children.Add(item);
        }
    }

    private static string TipFor(IReadOnlyList<ChartLine> lines, int index, string label)
    {
        var explicitTip = lines.Select(line => index < line.Points.Count ? line.Points[index].Tip : null).FirstOrDefault(tip => !string.IsNullOrEmpty(tip));
        if (explicitTip is not null) return explicitTip;
        if (lines.Count == 1) return $"{label}: {lines[0].Points[index].ValueDisplay}";
        return label + string.Concat(lines.Where(line => index < line.Points.Count).Select(line => $"\n{line.Name}: {line.Points[index].ValueDisplay}"));
    }

    private void Redraw()
    {
        if (Plot is null) return;
        Plot.Children.Clear();
        var lines = (Lines ?? []).Where(line => line.Points.Count > 0).ToList();
        var count = lines.Count == 0 ? 0 : lines.Max(line => line.Points.Count);
        BuildLegend(lines);
        EmptyText.Text = EmptyMessage;
        EmptyText.Visibility = count == 0 && EmptyMessage.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        AutomationProperties.SetName(this, Summary.Length > 0 ? Summary : count == 0 ? "Gráfico de linhas sem dados" : $"Gráfico de linhas com {count} pontos. " + string.Join(" ", lines.Select(line => { var top = line.Points.MaxBy(point => point.Value)!; return $"{line.Name}: maior valor {top.ValueDisplay} em {top.Label}."; })));
        double width = Plot.ActualWidth, height = Plot.ActualHeight;
        if (count == 0 || width < LeftMargin + RightMargin + 40 || height < TopMargin + BottomMargin + 40) return;

        var plotWidth = width - LeftMargin - RightMargin; var plotHeight = height - TopMargin - BottomMargin;
        var maximum = Math.Max(1, lines.Max(line => line.Points.Max(point => point.Value)));
        var step = NiceStep(maximum / 4d); var top = Math.Ceiling(maximum / step) * step;
        var baseline = TopMargin + plotHeight;
        double Y(double value) => baseline - Math.Max(0, value) / top * plotHeight;
        var slot = plotWidth / count;
        double X(int index) => LeftMargin + (index + 0.5) * slot;

        for (var tick = 0d; tick <= top + 0.001; tick += step)
        {
            var grid = new Line { X1 = LeftMargin, X2 = LeftMargin + plotWidth, Y1 = Y(tick), Y2 = Y(tick), StrokeThickness = tick == 0 ? 1.5 : 1, IsHitTestVisible = false };
            Stroke(grid, tick == 0 ? "BorderBrush" : "DividerBrush"); Plot.Children.Add(grid);
            Plot.Children.Add(Label(DisplayFormat.Count((long)Math.Round(tick)), 0, Y(tick) - 8, LeftMargin - 8, TextAlignment.Right));
        }

        var labels = lines.OrderByDescending(line => line.Points.Count).First().Points;
        var every = Math.Max(1, (int)Math.Ceiling(58 / slot));
        for (var index = 0; index < labels.Count; index += every) Plot.Children.Add(Label(labels[index].Label, X(index) - 28, baseline + 6, 56, TextAlignment.Center));

        foreach (var line in lines)
        {
            var coordinates = line.Points.Select((point, index) => new Point(X(index), Y(point.Value))).ToList();
            var area = new Polygon { Points = new PointCollection(coordinates.Prepend(new Point(coordinates[0].X, baseline)).Append(new Point(coordinates[^1].X, baseline))), OpacityMask = AreaFade, IsHitTestVisible = false };
            Fill(area, line.BrushKey); Plot.Children.Add(area);
        }
        foreach (var line in lines)
        {
            var polyline = new Polyline { Points = new PointCollection(line.Points.Select((point, index) => new Point(X(index), Y(point.Value)))), StrokeThickness = 3, StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeDashArray = line.Dash is { } dash ? new DoubleCollection(dash) : null, IsHitTestVisible = false };
            Stroke(polyline, line.BrushKey); Plot.Children.Add(polyline);
        }
        if (count <= DotLimit)
            foreach (var line in lines)
                for (var index = 0; index < line.Points.Count; index++)
                {
                    var dot = new Ellipse { Width = 9, Height = 9, StrokeThickness = 2.5, IsHitTestVisible = false };
                    Fill(dot, "SurfaceBrush"); Stroke(dot, line.BrushKey);
                    Canvas.SetLeft(dot, X(index) - 4.5); Canvas.SetTop(dot, Y(line.Points[index].Value) - 4.5); Plot.Children.Add(dot);
                }

        // Guia vertical do ponto sob o mouse e uma área sensível por ponto, com a dica do número exato.
        var guide = new Line { Y1 = TopMargin, Y2 = baseline, StrokeThickness = 1, StrokeDashArray = [3, 3], Visibility = Visibility.Collapsed, IsHitTestVisible = false };
        Stroke(guide, "SecondaryBrush"); Plot.Children.Add(guide);
        for (var index = 0; index < count; index++)
        {
            var at = X(index);
            var hit = new Rectangle { Width = slot, Height = plotHeight, Fill = Brushes.Transparent, ToolTip = TipFor(lines, index, labels[Math.Min(index, labels.Count - 1)].Label) };
            hit.MouseEnter += (_, _) => { guide.X1 = guide.X2 = at; guide.Visibility = Visibility.Visible; };
            hit.MouseLeave += (_, _) => guide.Visibility = Visibility.Collapsed;
            Canvas.SetLeft(hit, LeftMargin + index * slot); Canvas.SetTop(hit, TopMargin); Plot.Children.Add(hit);
        }
    }
}
