using System.Windows;
using System.Windows.Automation;
using System.Windows.Media;
using Nora.Desktop.Models;

namespace Nora.Desktop.Views;

/// <summary>Distribuição das consultas encerradas; os totais vêm da mesma leitura dos indicadores.</summary>
public sealed class OutcomePie : FrameworkElement
{
    public static readonly DependencyProperty MetricsProperty = DependencyProperty.Register(nameof(Metrics), typeof(UnitMetrics), typeof(OutcomePie), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, (d, _) => ((OutcomePie)d).Describe()));
    public UnitMetrics? Metrics { get => (UnitMetrics?)GetValue(MetricsProperty); set => SetValue(MetricsProperty, value); }
    /// <summary>Fatias genéricas (gráfico dos cartões de indicador); quando definidas, substituem as consultas encerradas e usam a paleta ChartBrush do tema.</summary>
    public static readonly DependencyProperty SlicesProperty = DependencyProperty.Register(nameof(Slices), typeof(IReadOnlyList<ChartPoint>), typeof(OutcomePie), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public IReadOnlyList<ChartPoint>? Slices { get => (IReadOnlyList<ChartPoint>?)GetValue(SlicesProperty); set => SetValue(SlicesProperty, value); }
    /// <summary>Raio interno (0 = pizza cheia; maior que zero = rosca).</summary>
    public double HoleRatio { get; set; }
    private void Describe()
    {
        var a = Metrics?.Appointments;
        AutomationProperties.SetName(this, a is null ? "Gráfico sem dados" : $"Consultas encerradas: {a.Completed} concluídas, {a.Missed} faltas, {a.Cancelled} canceladas.");
    }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        long[] values; Brush[] brushes;
        if (Slices is { Count: > 0 } slices)
        {
            values = slices.Select(slice => (long)Math.Max(0, Math.Round(slice.Value))).ToArray();
            brushes = slices.Select((_, index) => (TryFindResource($"ChartBrush{index % 6 + 1}") as Brush) ?? Brushes.SteelBlue).ToArray();
        }
        else
        {
            var a = Metrics?.Appointments;
            if (a is null) return;
            values = [Math.Max(0, a.Completed), Math.Max(0, a.Missed), Math.Max(0, a.Cancelled)];
            brushes = new[] { "#137A52", "#B4233E", "#C68800" }.Select(color => (Brush)new BrushConverter().ConvertFromString(color)!).ToArray();
        }
        var total = values.Sum();
        if (total == 0) return;
        var radius = Math.Max(0, Math.Min(ActualWidth, ActualHeight) / 2 - 4);
        if (radius <= 0) return;
        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        if (values.Count(v => v > 0) == 1) { dc.DrawEllipse(brushes[Array.FindIndex(values, v => v > 0)], null, center, radius, radius); return; }
        double angle = -Math.PI / 2;
        for (var i = 0; i < values.Length; i++)
        {
            if (values[i] == 0) continue;
            var sweep = 2 * Math.PI * values[i] / total;
            Point At(double theta) => new(center.X + radius * Math.Cos(theta), center.Y + radius * Math.Sin(theta));
            var geometry = new StreamGeometry();
            using (var path = geometry.Open()) { path.BeginFigure(center, true, true); path.LineTo(At(angle), true, false); path.ArcTo(At(angle + sweep), new Size(radius, radius), 0, sweep > Math.PI, SweepDirection.Clockwise, true, false); }
            geometry.Freeze(); dc.DrawGeometry(brushes[i], null, geometry); angle += sweep;
        }
        if (HoleRatio > 0 && TryFindResource("SurfaceBrush") is Brush hole) dc.DrawEllipse(hole, null, center, radius * HoleRatio, radius * HoleRatio);
    }
}
