using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Nora.Desktop.Models;
using Nora.Desktop.Services;
using Nora.Desktop.ViewModels;

namespace Nora.Desktop.Views;

/// <summary>
/// Evolução diária (concluídas, faltas, canceladas) com formas WPF simples: barras agrupadas até 31 dias e linhas acima disso.
/// Eixos e valores escritos, dica com o número exato em cada dia e a tabela equivalente logo abaixo. Cores vêm dos tokens do tema.
/// </summary>
public partial class EvolutionChartView : UserControl
{
    private const int BarLimit = 31;
    private const double LeftMargin = 46, BottomMargin = 30, TopMargin = 10, RightMargin = 10;
    private static readonly (string Name, string Brush, Func<DailyPoint, long> Value, DoubleCollection? Dash)[] Series =
    [
        ("Concluídas", "SuccessBrush", point => point.Completed, null),
        ("Faltas", "ErrorBrush", point => point.Missed, new DoubleCollection { 5, 3 }),
        ("Canceladas", "WarningBrush", point => point.Cancelled, new DoubleCollection { 1.5, 3 })
    ];
    private WorkspaceViewModel? workspace;

    public EvolutionChartView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Attach();
        Loaded += (_, _) => Attach();
        Unloaded += (_, _) => Detach();
        SizeChanged += (_, _) => Redraw();
    }

    private void Attach() { Detach(); workspace = DataContext as WorkspaceViewModel; if (workspace != null) workspace.PropertyChanged += OnChanged; Redraw(); }
    private void Detach() { if (workspace != null) workspace.PropertyChanged -= OnChanged; workspace = null; }
    private void OnChanged(object? sender, PropertyChangedEventArgs args) { if (args.PropertyName == nameof(WorkspaceViewModel.ChartDays)) Redraw(); }

    private static void Fill(Shape shape, string key) => shape.SetResourceReference(Shape.FillProperty, key);
    private static void Stroke(Shape shape, string key) => shape.SetResourceReference(Shape.StrokeProperty, key);

    private TextBlock Label(string text, double left, double top, double width, TextAlignment alignment)
    {
        var block = new TextBlock { Text = text, Width = width, TextAlignment = alignment, FontSize = 11, TextWrapping = TextWrapping.NoWrap };
        block.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryBrush");
        Canvas.SetLeft(block, left); Canvas.SetTop(block, top);
        return block;
    }

    private static double NiceStep(double raw)
    {
        foreach (var step in new double[] { 1, 2, 5 }.SelectMany(unit => new[] { 1d, 10d, 100d, 1000d, 10000d }.Select(scale => unit * scale)).Order()) if (step >= raw) return step;
        return Math.Ceiling(raw);
    }

    private void BuildLegend(bool lines)
    {
        Legend.Children.Clear();
        foreach (var (name, brush, _, dash) in Series)
        {
            var item = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 18, 4) };
            if (lines)
            {
                var sample = new Line { X1 = 0, X2 = 26, Y1 = 6, Y2 = 6, StrokeThickness = 2.5, Width = 26, Height = 12, StrokeDashArray = dash, Margin = new Thickness(0, 0, 6, 0) };
                Stroke(sample, brush); item.Children.Add(sample);
            }
            else
            {
                var sample = new Rectangle { Width = 12, Height = 12, RadiusX = 2, RadiusY = 2, Margin = new Thickness(0, 0, 6, 0) };
                Fill(sample, brush); item.Children.Add(sample);
            }
            item.Children.Add(new TextBlock { Text = name, FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
            Legend.Children.Add(item);
        }
    }

    private void Redraw()
    {
        Plot.Children.Clear();
        var days = workspace?.ChartDays ?? [];
        var hasData = workspace?.HasChartData == true && days.Count > 0;
        EmptyText.Text = workspace?.ChartState ?? "";
        EmptyText.Visibility = hasData ? Visibility.Collapsed : Visibility.Visible;
        var lines = days.Count > BarLimit;
        BuildLegend(lines);
        AutomationProperties.SetName(this, hasData ? $"Gráfico de evolução diária de {days.Count} dias: {days.Sum(day => day.Completed)} concluídas, {days.Sum(day => day.Missed)} faltas e {days.Sum(day => day.Cancelled)} canceladas. Os mesmos valores estão na tabela abaixo." : "Gráfico de evolução diária sem dados");
        var width = Plot.ActualWidth; var height = Plot.ActualHeight;
        if (!hasData || width < LeftMargin + RightMargin + 40 || height < TopMargin + BottomMargin + 40) return;

        var plotWidth = width - LeftMargin - RightMargin; var plotHeight = height - TopMargin - BottomMargin;
        var maximum = Math.Max(1, days.Max(day => Series.Max(series => series.Value(day))));
        var step = NiceStep(maximum / 4d); var top = Math.Ceiling(maximum / step) * step;
        double Y(double value) => TopMargin + plotHeight - value / top * plotHeight;
        var slot = plotWidth / days.Count;

        for (var tick = 0d; tick <= top + 0.001; tick += step)
        {
            var line = new Line { X1 = LeftMargin, X2 = LeftMargin + plotWidth, Y1 = Y(tick), Y2 = Y(tick), StrokeThickness = tick == 0 ? 1.5 : 1 };
            Stroke(line, "BorderBrush"); Plot.Children.Add(line);
            Plot.Children.Add(Label(DisplayFormat.Count((long)tick), 0, Y(tick) - 8, LeftMargin - 8, TextAlignment.Right));
        }

        var every = Math.Max(1, (int)Math.Ceiling(58 / slot));
        for (var index = 0; index < days.Count; index += every)
            Plot.Children.Add(Label(days[index].Date.ToString("dd/MM", DisplayFormat.Culture), LeftMargin + (index + 0.5) * slot - 28, TopMargin + plotHeight + 6, 56, TextAlignment.Center));

        if (!lines)
        {
            var barWidth = Math.Min(16, slot * 0.8 / Series.Length);
            for (var index = 0; index < days.Count; index++)
            for (var s = 0; s < Series.Length; s++)
            {
                var value = Series[s].Value(days[index]);
                if (value == 0) continue;
                var x = LeftMargin + index * slot + slot / 2 - barWidth * Series.Length / 2 + s * barWidth;
                var bar = new Rectangle { Width = Math.Max(1, barWidth - 1), Height = Math.Max(1, TopMargin + plotHeight - Y(value)), RadiusX = 1.5, RadiusY = 1.5 };
                Fill(bar, Series[s].Brush); Canvas.SetLeft(bar, x); Canvas.SetTop(bar, Y(value)); Plot.Children.Add(bar);
            }
        }
        else
        {
            foreach (var (_, brush, value, dash) in Series)
            {
                var points = new PointCollection(days.Select((day, index) => new Point(LeftMargin + (index + 0.5) * slot, Y(value(day)))));
                var polyline = new Polyline { Points = points, StrokeThickness = 2, StrokeLineJoin = PenLineJoin.Round, StrokeDashArray = dash };
                Stroke(polyline, brush); Plot.Children.Add(polyline);
            }
        }

        // Uma área sensível por dia, com a dica do número exato.
        for (var index = 0; index < days.Count; index++)
        {
            var hit = new Rectangle { Width = slot, Height = plotHeight, Fill = Brushes.Transparent, ToolTip = days[index].Description };
            Canvas.SetLeft(hit, LeftMargin + index * slot); Canvas.SetTop(hit, TopMargin); Plot.Children.Add(hit);
        }
    }
}
