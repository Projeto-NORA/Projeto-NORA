using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Nora.Desktop.Models;

namespace Nora.Desktop.Views;

/// <summary>Pop-up do mini botão de gráfico dos cartões: o número do cartão como linha (evolução) ou pizza (composição), com cores do tema.</summary>
public partial class IndicatorChartWindow : Window
{
    private readonly IndicatorChart chart;
    private bool showingPie;

    public IndicatorChartWindow(IndicatorChart chart)
    {
        this.chart = chart;
        InitializeComponent();
        TitleText.Text = chart.Title;
        ValueText.Text = "Valor atual: " + chart.Value;
        Pie.Slices = chart.Pie;
        System.Windows.Automation.AutomationProperties.SetName(Pie, "Composição: " + string.Join(", ", chart.Pie.Select(p => $"{p.Label} {p.ValueDisplay}")));
        BuildLegend();
        Show(pie: chart.Line.Count == 0 && chart.Pie.Count > 0);
    }

    private void Show(bool pie)
    {
        showingPie = pie;
        LineTab.Tag = !pie; PieTab.Tag = pie;
        LinePlot.Visibility = pie ? Visibility.Collapsed : Visibility.Visible;
        PiePanel.Visibility = pie && chart.Pie.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        var empty = pie ? chart.Pie.Count == 0 : chart.Line.Count == 0;
        EmptyText.Text = pie ? chart.PieNote : chart.LineNote;
        EmptyText.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        NoteText.Text = empty ? "" : pie ? chart.PieNote : chart.LineNote;
        UIElement target = pie ? PiePanel : LinePlot;
        target.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
        if (!pie) DrawLine();
    }

    private void BuildLegend()
    {
        PieLegend.Children.Clear();
        var total = Math.Max(1, chart.Pie.Sum(p => p.Value));
        for (var i = 0; i < chart.Pie.Count; i++)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
            var dot = new Ellipse { Width = 12, Height = 12, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
            dot.SetResourceReference(Shape.FillProperty, $"ChartBrush{i % 6 + 1}");
            row.Children.Add(dot);
            row.Children.Add(new TextBlock { Text = $"{chart.Pie[i].Label}: {chart.Pie[i].ValueDisplay} ({chart.Pie[i].Value / total:P0})", VerticalAlignment = VerticalAlignment.Center });
            PieLegend.Children.Add(row);
        }
    }

    private void DrawLine()
    {
        LinePlot.Children.Clear();
        var points = chart.Line;
        double width = LinePlot.ActualWidth, height = LinePlot.ActualHeight;
        if (points.Count == 0 || width < 120 || height < 100) return;
        const double left = 48, bottom = 30, top = 10, right = 12;
        var plotWidth = width - left - right; var plotHeight = height - top - bottom;
        var max = Math.Max(1, points.Max(p => p.Value));
        double X(int i) => left + (points.Count == 1 ? plotWidth / 2 : i * plotWidth / (points.Count - 1));
        double Y(double v) => top + plotHeight - v / max * plotHeight;
        for (var tick = 0; tick <= 4; tick++)
        {
            var value = max * tick / 4;
            var grid = new Line { X1 = left, X2 = left + plotWidth, Y1 = Y(value), Y2 = Y(value), StrokeThickness = 1 };
            grid.SetResourceReference(Shape.StrokeProperty, "DividerBrush"); LinePlot.Children.Add(grid);
            LinePlot.Children.Add(Label(value.ToString(value < 10 ? "N1" : "N0", Services.DisplayFormat.Culture), 0, Y(value) - 8, left - 8, TextAlignment.Right));
        }
        var every = Math.Max(1, (int)Math.Ceiling(points.Count * 64 / plotWidth));
        for (var i = 0; i < points.Count; i += every) LinePlot.Children.Add(Label(points[i].Label, X(i) - 40, top + plotHeight + 6, 80, TextAlignment.Center));
        var coordinates = points.Select((p, i) => new Point(X(i), Y(p.Value))).ToList();
        var area = new Polygon { Points = new PointCollection(coordinates.Prepend(new Point(coordinates[0].X, top + plotHeight)).Append(new Point(coordinates[^1].X, top + plotHeight))), Opacity = 0.14 };
        area.SetResourceReference(Shape.FillProperty, "ChartBrush1"); LinePlot.Children.Add(area);
        var line = new Polyline { Points = new PointCollection(coordinates), StrokeThickness = 2.5, StrokeLineJoin = PenLineJoin.Round };
        line.SetResourceReference(Shape.StrokeProperty, "ChartBrush1"); LinePlot.Children.Add(line);
        for (var i = 0; i < coordinates.Count; i++)
        {
            var dot = new Ellipse { Width = 8, Height = 8, ToolTip = $"{points[i].Label}: {points[i].ValueDisplay}" };
            dot.SetResourceReference(Shape.FillProperty, "ChartBrush1");
            Canvas.SetLeft(dot, coordinates[i].X - 4); Canvas.SetTop(dot, coordinates[i].Y - 4); LinePlot.Children.Add(dot);
        }
    }

    private static TextBlock Label(string text, double x, double y, double width, TextAlignment alignment)
    {
        var block = new TextBlock { Text = text, Width = width, TextAlignment = alignment, FontSize = 11, TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis };
        block.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryBrush");
        Canvas.SetLeft(block, x); Canvas.SetTop(block, y);
        return block;
    }

    private void OnPlotSize(object sender, SizeChangedEventArgs e) { if (!showingPie) DrawLine(); }
    private void OnLine(object sender, RoutedEventArgs e) => Show(pie: false);
    private void OnPie(object sender, RoutedEventArgs e) => Show(pie: true);
    private void OnClose(object sender, RoutedEventArgs e) => Close();
    private void OnDrag(object sender, MouseButtonEventArgs e) { if (e.LeftButton == MouseButtonState.Pressed) DragMove(); }
}
