using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Nora.Desktop.Models;
using Nora.Desktop.Services;

namespace Nora.Desktop.Views;

/// <summary>
/// Pizza ou rosca (<see cref="HoleRatio"/>) das consultas encerradas ou de fatias genéricas, com as fatias separadas por um fio da cor da superfície, a fatia
/// sob o mouse um pouco maior (com dica de valor e percentual) e, na rosca, o total no centro. As cores vêm das chaves do tema e acompanham a troca de tema.
/// </summary>
public sealed class OutcomePie : FrameworkElement
{
    public static readonly DependencyProperty MetricsProperty = DependencyProperty.Register(nameof(Metrics), typeof(UnitMetrics), typeof(OutcomePie), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, (d, _) => ((OutcomePie)d).Describe()));
    public UnitMetrics? Metrics { get => (UnitMetrics?)GetValue(MetricsProperty); set => SetValue(MetricsProperty, value); }
    /// <summary>Fatias genéricas (gráfico dos cartões de indicador); quando definidas, substituem as consultas encerradas. Cada fatia usa a sua chave de cor ou a paleta ChartBrush do tema.</summary>
    public static readonly DependencyProperty SlicesProperty = DependencyProperty.Register(nameof(Slices), typeof(IReadOnlyList<ChartPoint>), typeof(OutcomePie), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, (d, _) => ((OutcomePie)d).Describe()));
    public IReadOnlyList<ChartPoint>? Slices { get => (IReadOnlyList<ChartPoint>?)GetValue(SlicesProperty); set => SetValue(SlicesProperty, value); }
    /// <summary>Cores das consultas encerradas: as chaves de situação do tema, ligadas por recurso dinâmico (a pizza se redesenha quando o tema troca).</summary>
    public static readonly DependencyProperty CompletedBrushProperty = DependencyProperty.Register(nameof(CompletedBrush), typeof(Brush), typeof(OutcomePie), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty MissedBrushProperty = DependencyProperty.Register(nameof(MissedBrush), typeof(Brush), typeof(OutcomePie), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty CancelledBrushProperty = DependencyProperty.Register(nameof(CancelledBrush), typeof(Brush), typeof(OutcomePie), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    /// <summary>A superfície do tema: separa as fatias e, ao trocar de tema, faz a pizza se redesenhar com as cores novas das fatias.</summary>
    private static readonly DependencyProperty SurfaceProperty = DependencyProperty.Register("Surface", typeof(Brush), typeof(OutcomePie), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    /// <summary>Raio interno em relação ao externo (0 = pizza cheia; maior que zero = rosca).</summary>
    public static readonly DependencyProperty HoleRatioProperty = DependencyProperty.Register(nameof(HoleRatio), typeof(double), typeof(OutcomePie), new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));
    public Brush? CompletedBrush { get => (Brush?)GetValue(CompletedBrushProperty); set => SetValue(CompletedBrushProperty, value); }
    public Brush? MissedBrush { get => (Brush?)GetValue(MissedBrushProperty); set => SetValue(MissedBrushProperty, value); }
    public Brush? CancelledBrush { get => (Brush?)GetValue(CancelledBrushProperty); set => SetValue(CancelledBrushProperty, value); }
    public double HoleRatio { get => (double)GetValue(HoleRatioProperty); set => SetValue(HoleRatioProperty, value); }

    private readonly ToolTip tip = new();
    private int hovered = -1;

    public OutcomePie()
    {
        SetResourceReference(CompletedBrushProperty, "StatusCompletedBrush");
        SetResourceReference(MissedBrushProperty, "StatusMissedBrush");
        SetResourceReference(CancelledBrushProperty, "StatusCancelledBrush");
        SetResourceReference(SurfaceProperty, "SurfaceBrush");
        ToolTip = tip;
        ToolTipService.SetIsEnabled(this, false);
    }

    /// <summary>A chave de cor do tema de uma fatia: a dela, ou a paleta ChartBrush1..6 pela posição.</summary>
    public static string BrushKeyFor(ChartPoint slice, int index) => slice.BrushKey ?? $"ChartBrush{index % 6 + 1}";

    private void Describe()
    {
        var (values, _) = Data();
        AutomationProperties.SetName(this, values.Length == 0 || Slices is { Count: > 0 } && values.Sum() == 0 ? "Gráfico sem dados" : Slices is { Count: > 0 }
            ? "Composição: " + string.Join(", ", Slices.Where(slice => slice.Value > 0).Select(slice => $"{slice.Label} {slice.ValueDisplay}"))
            : $"Consultas encerradas: {values[0]} concluídas, {values[1]} faltas, {values[2]} canceladas.");
    }

    /// <summary>Os valores e os nomes que o desenho usa agora (fatias genéricas ou as consultas encerradas). Barato: serve ao mouse a cada movimento.</summary>
    private (long[] Values, string[] Labels) Data()
    {
        if (Slices is { Count: > 0 } slices)
            return ([.. slices.Select(slice => (long)Math.Max(0, Math.Round(slice.Value)))], [.. slices.Select(slice => slice.Label)]);
        var a = Metrics?.Appointments;
        return a is null ? ([], []) : ([Math.Max(0, a.Completed), Math.Max(0, a.Missed), Math.Max(0, a.Cancelled)], ["Concluídas", "Faltas", "Canceladas"]);
    }

    /// <summary>As cores das fatias, buscadas no tema (só no desenho).</summary>
    private Brush[] SliceBrushes() => Slices is { Count: > 0 } slices
        ? [.. slices.Select((slice, index) => (TryFindResource(BrushKeyFor(slice, index)) as Brush) ?? System.Windows.Media.Brushes.SteelBlue)]
        : [CompletedBrush ?? System.Windows.Media.Brushes.SeaGreen, MissedBrush ?? System.Windows.Media.Brushes.IndianRed, CancelledBrush ?? System.Windows.Media.Brushes.Goldenrod];

    private double Radius => Math.Max(0, Math.Min(ActualWidth, ActualHeight) / 2 - 6);
    private Point Center => new(ActualWidth / 2, ActualHeight / 2);
    private double Hole => Math.Clamp(HoleRatio, 0, 0.9);

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var (values, _) = Data();
        var total = values.Sum();
        if (total == 0 || Radius <= 0) return;
        var brushes = SliceBrushes();
        var radius = Radius; var center = Center;
        var separator = new Pen(GetValue(SurfaceProperty) as Brush ?? System.Windows.Media.Brushes.White, 2) { LineJoin = PenLineJoin.Round };
        separator.Freeze();
        if (values.Count(value => value > 0) == 1)
        {
            var only = Array.FindIndex(values, value => value > 0);
            var outer = radius + (hovered == only ? 4 : 0);
            Geometry shape = new EllipseGeometry(center, outer, outer);
            if (Hole > 0) shape = new CombinedGeometry(GeometryCombineMode.Exclude, shape, new EllipseGeometry(center, radius * Hole, radius * Hole));
            dc.DrawGeometry(brushes[only], null, shape);
        }
        else
        {
            var angle = -Math.PI / 2;
            for (var i = 0; i < values.Length; i++)
            {
                if (values[i] == 0) continue;
                var sweep = 2 * Math.PI * values[i] / total;
                dc.DrawGeometry(brushes[i], separator, Sector(center, radius * Hole, radius + (hovered == i ? 4 : 0), angle, sweep));
                angle += sweep;
            }
        }
        if (Hole > 0 && radius * Hole >= 26)
        {
            var size = Math.Min(24, radius * Hole * 0.62);
            var text = new FormattedText(total.ToString("N0", DisplayFormat.Culture), DisplayFormat.Culture, FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal), size, TryFindResource("TextBrush") as Brush ?? System.Windows.Media.Brushes.Black, VisualTreeHelper.GetDpi(this).PixelsPerDip);
            dc.DrawText(text, new Point(center.X - text.Width / 2, center.Y - text.Height / 2));
        }
    }

    /// <summary>Setor da pizza (ou da rosca, quando o raio interno é maior que zero) entre o ângulo inicial e o varrido, no sentido horário.</summary>
    private static StreamGeometry Sector(Point center, double inner, double outer, double start, double sweep)
    {
        Point At(double radius, double theta) => new(center.X + radius * Math.Cos(theta), center.Y + radius * Math.Sin(theta));
        var geometry = new StreamGeometry();
        using (var path = geometry.Open())
        {
            var large = sweep > Math.PI;
            path.BeginFigure(inner > 0 ? At(inner, start) : center, true, true);
            path.LineTo(At(outer, start), true, false);
            path.ArcTo(At(outer, start + sweep), new Size(outer, outer), 0, large, SweepDirection.Clockwise, true, false);
            if (inner > 0)
            {
                path.LineTo(At(inner, start + sweep), true, false);
                path.ArcTo(At(inner, start), new Size(inner, inner), 0, large, SweepDirection.Counterclockwise, true, false);
            }
        }
        geometry.Freeze();
        return geometry;
    }

    /// <summary>A fatia sob o ponto (-1 fora da pizza e no buraco da rosca).</summary>
    private int SliceAt(Point point)
    {
        var (values, _) = Data();
        var total = values.Sum();
        if (total == 0 || Radius <= 0) return -1;
        var dx = point.X - Center.X; var dy = point.Y - Center.Y;
        var distance = Math.Sqrt(dx * dx + dy * dy);
        if (distance > Radius + 4 || distance < Radius * Hole) return -1;
        var angle = Math.Atan2(dy, dx) + Math.PI / 2;
        if (angle < 0) angle += 2 * Math.PI;
        var from = 0d;
        for (var i = 0; i < values.Length; i++)
        {
            var sweep = 2 * Math.PI * values[i] / total;
            if (values[i] > 0 && angle >= from && angle < from + sweep) return i;
            from += sweep;
        }
        return -1;
    }

    protected override void OnMouseMove(MouseEventArgs args)
    {
        base.OnMouseMove(args);
        Hover(SliceAt(args.GetPosition(this)));
    }

    protected override void OnMouseLeave(MouseEventArgs args)
    {
        base.OnMouseLeave(args);
        Hover(-1);
    }

    private void Hover(int index)
    {
        if (index == hovered) return;
        hovered = index;
        var (values, labels) = Data();
        if (index >= 0 && index < values.Length) tip.Content = $"{labels[index]}: {DisplayFormat.Count(values[index])} ({values[index] / (double)values.Sum():P0})";
        ToolTipService.SetIsEnabled(this, index >= 0);
        InvalidateVisual();
    }
}
