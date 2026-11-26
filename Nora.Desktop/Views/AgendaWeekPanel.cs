using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Nora.Desktop.Models;

namespace Nora.Desktop.Views;

/// <summary>
/// A grade da visão Semana: as horas na lateral esquerda, os sete dias em colunas iguais e cada consulta como um bloco no seu dia e horário. Consultas que se cruzam no
/// mesmo dia ficam lado a lado, dividindo a coluna. É o painel de itens da lista de consultas da semana: o bloco é o contêiner de cada <see cref="AppointmentInfo"/>.
/// As linhas e os rótulos de hora são desenhados aqui, com os pincéis do tema (propriedades ligadas por <c>DynamicResource</c>), então acompanham a troca de tema.
/// </summary>
public sealed class AgendaWeekPanel : Panel
{
    private const double MinBlockHeight = 24;
    public static readonly DependencyProperty WeekStartProperty = Register(nameof(WeekStart), DateTime.Today, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange | FrameworkPropertyMetadataOptions.AffectsRender);
    public static readonly DependencyProperty FirstHourProperty = Register(nameof(FirstHour), 8, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange | FrameworkPropertyMetadataOptions.AffectsRender);
    public static readonly DependencyProperty LastHourProperty = Register(nameof(LastHour), 18, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange | FrameworkPropertyMetadataOptions.AffectsRender);
    public static readonly DependencyProperty HourHeightProperty = Register(nameof(HourHeight), 64d, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange | FrameworkPropertyMetadataOptions.AffectsRender);
    public static readonly DependencyProperty GutterWidthProperty = Register(nameof(GutterWidth), 56d, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange | FrameworkPropertyMetadataOptions.AffectsRender);
    public static readonly DependencyProperty LineBrushProperty = Register<Brush?>(nameof(LineBrush), null, FrameworkPropertyMetadataOptions.AffectsRender);
    public static readonly DependencyProperty TextBrushProperty = Register<Brush?>(nameof(TextBrush), null, FrameworkPropertyMetadataOptions.AffectsRender);
    public static readonly DependencyProperty TodayBrushProperty = Register<Brush?>(nameof(TodayBrush), null, FrameworkPropertyMetadataOptions.AffectsRender);

    private static DependencyProperty Register<T>(string name, T value, FrameworkPropertyMetadataOptions options) =>
        DependencyProperty.Register(name, typeof(T), typeof(AgendaWeekPanel), new FrameworkPropertyMetadata(value, options));

    /// <summary>A segunda-feira da semana mostrada.</summary>
    public DateTime WeekStart { get => (DateTime)GetValue(WeekStartProperty); set => SetValue(WeekStartProperty, value.Date); }
    /// <summary>A primeira hora da grade (inclusive) e a última (o fim da grade); a faixa só cobre as horas com consulta ou de atendimento.</summary>
    public int FirstHour { get => (int)GetValue(FirstHourProperty); set => SetValue(FirstHourProperty, value); }
    public int LastHour { get => (int)GetValue(LastHourProperty); set => SetValue(LastHourProperty, value); }
    public double HourHeight { get => (double)GetValue(HourHeightProperty); set => SetValue(HourHeightProperty, value); }
    /// <summary>Largura da coluna dos rótulos de hora.</summary>
    public double GutterWidth { get => (double)GetValue(GutterWidthProperty); set => SetValue(GutterWidthProperty, value); }
    public Brush? LineBrush { get => (Brush?)GetValue(LineBrushProperty); set => SetValue(LineBrushProperty, value); }
    public Brush? TextBrush { get => (Brush?)GetValue(TextBrushProperty); set => SetValue(TextBrushProperty, value); }
    /// <summary>Fundo suave da coluna de hoje, quando a semana mostrada é a atual.</summary>
    public Brush? TodayBrush { get => (Brush?)GetValue(TodayBrushProperty); set => SetValue(TodayBrushProperty, value); }

    private int Hours => Math.Max(1, LastHour - FirstHour);

    protected override Size MeasureOverride(Size available)
    {
        var width = double.IsInfinity(available.Width) ? 760 : available.Width;
        var columnWidth = Math.Max(0, (width - GutterWidth) / 7);
        foreach (var (child, rect) in Place(columnWidth)) child.Measure(rect.Size);
        return new Size(width, Hours * HourHeight);
    }

    protected override Size ArrangeOverride(Size final)
    {
        var columnWidth = Math.Max(0, (final.Width - GutterWidth) / 7);
        foreach (var (child, rect) in Place(columnWidth)) child.Arrange(rect);
        return final;
    }

    /// <summary>Onde cada bloco fica: o dia é a coluna, a hora é a altura e os que se cruzam dividem a coluna em faixas iguais.</summary>
    private List<(UIElement Child, Rect Rect)> Place(double columnWidth)
    {
        var owner = ItemsControl.GetItemsOwner(this);
        var entries = new List<(UIElement Child, AppointmentInfo Item, int Day, double Top, double Bottom)>();
        var hidden = new List<(UIElement, Rect)>();
        foreach (UIElement child in InternalChildren)
        {
            if (owner?.ItemContainerGenerator.ItemFromContainer(child) is not AppointmentInfo item) { hidden.Add((child, new Rect(0, 0, 0, 0))); continue; }
            var begins = item.StartsAt.ToLocalTime(); var ends = item.EndsAt.ToLocalTime();
            var day = (begins.Date - WeekStart).Days;
            if (day is < 0 or > 6) { hidden.Add((child, new Rect(0, 0, 0, 0))); continue; }
            var top = Math.Max(0, (begins.TimeOfDay.TotalHours - FirstHour) * HourHeight);
            var endHours = ends.Date > begins.Date ? 24 : ends.TimeOfDay.TotalHours;
            var bottom = Math.Min(Hours * HourHeight, Math.Max(top + MinBlockHeight, (endHours - FirstHour) * HourHeight));
            entries.Add((child, item, day, top, bottom));
        }
        var placed = new List<(UIElement, Rect)>();
        foreach (var dayGroup in entries.GroupBy(entry => entry.Day))
        {
            // Agrupa em blocos de consultas que se cruzam em cadeia; cada bloco divide a coluna no número de faixas de que precisa.
            var lanesEnd = new List<double>();
            var cluster = new List<(UIElement Child, double Top, double Bottom, int Lane)>();
            var clusterEnd = double.MinValue;
            void Flush()
            {
                var lanes = Math.Max(1, lanesEnd.Count);
                foreach (var (child, top, bottom, lane) in cluster)
                {
                    var laneWidth = columnWidth / lanes;
                    placed.Add((child, new Rect(GutterWidth + dayGroup.Key * columnWidth + lane * laneWidth + 2, top + 1, Math.Max(0, laneWidth - 4), Math.Max(0, bottom - top - 2))));
                }
                cluster.Clear(); lanesEnd.Clear();
            }
            foreach (var entry in dayGroup.OrderBy(entry => entry.Top).ThenBy(entry => entry.Item.Id))
            {
                if (cluster.Count > 0 && entry.Top >= clusterEnd) Flush();
                var lane = lanesEnd.FindIndex(end => end <= entry.Top);
                if (lane < 0) { lane = lanesEnd.Count; lanesEnd.Add(entry.Bottom); } else lanesEnd[lane] = entry.Bottom;
                cluster.Add((entry.Child, entry.Top, entry.Bottom, lane));
                clusterEnd = cluster.Count == 1 ? entry.Bottom : Math.Max(clusterEnd, entry.Bottom);
            }
            Flush();
        }
        placed.AddRange(hidden);
        return placed;
    }

    protected override void OnRender(DrawingContext context)
    {
        base.OnRender(context);
        var width = ActualWidth; var columnWidth = Math.Max(0, (width - GutterWidth) / 7);
        var height = Hours * HourHeight;
        // Pincel nulo (ainda sem tema) desenha só o fundo transparente que garante o clique no espaço vazio.
        context.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, width, height));
        var line = new Pen(LineBrush ?? Brushes.Transparent, 1);
        var today = (DateTime.Today - WeekStart).Days;
        if (today is >= 0 and <= 6 && TodayBrush is { } tint) context.DrawRectangle(tint, null, new Rect(GutterWidth + today * columnWidth, 0, columnWidth, height));
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        for (var hour = 0; hour <= Hours; hour++)
        {
            var y = Math.Round(hour * HourHeight) + 0.5;
            context.DrawLine(line, new Point(GutterWidth - 6, y), new Point(width, y));
            if (hour == Hours || TextBrush is null) continue;
            var label = new FormattedText($"{FirstHour + hour:00}:00", CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 11, TextBrush, dpi);
            context.DrawText(label, new Point(GutterWidth - 10 - label.Width, Math.Max(0, y - label.Height / 2 + (hour == 0 ? label.Height / 2 : 0))));
        }
        for (var column = 0; column <= 7; column++)
        {
            var x = Math.Round(GutterWidth + column * columnWidth) + 0.5;
            context.DrawLine(line, new Point(x, 0), new Point(x, height));
        }
    }
}
