using Nora.Desktop.Services;

namespace Nora.Desktop.Models;

/// <summary>Quanto tempo cada ponto do eixo do gráfico representa.</summary>
public enum ChartGrouping { Day, Week, Month }

/// <summary>Um ponto do eixo já agrupado: o intervalo, os totais de consultas e os textos do rótulo e da dica.</summary>
public sealed record ChartBucket(DateOnly Start, DateOnly End, long Completed, long Missed, long Cancelled, ChartGrouping Grouping)
{
    /// <summary>Rótulo do eixo: o dia (e a semana, pelo dia em que ela começa) como dd/MM; o mês como "jun/26".</summary>
    public string Label => Grouping == ChartGrouping.Month ? Start.ToString("MMM/yy", DisplayFormat.Culture) : Start.ToString("dd/MM", DisplayFormat.Culture);

    private static string MonthName(DateOnly date) { var name = date.ToString("MMMM", DisplayFormat.Culture); return char.ToUpper(name[0], DisplayFormat.Culture) + name[1..]; }

    /// <summary>Dica ao passar o mouse, com os números exatos do intervalo. O dia tem o mesmo texto de <see cref="DailyPoint.Description"/>.</summary>
    public string Description
    {
        get
        {
            var totals = $"{Completed} concluídas, {Missed} faltas, {Cancelled} canceladas";
            return Grouping switch
            {
                ChartGrouping.Day => $"{DisplayFormat.Date(Start)}: {totals}",
                ChartGrouping.Week => $"Semana de {Start.ToString("dd/MM", DisplayFormat.Culture)} a {DisplayFormat.Date(End)}: {totals}",
                _ => $"{MonthName(Start)} de {Start.Year}: {totals}"
            };
        }
    }
}

/// <summary>
/// Monta as linhas e as fatias dos gráficos de consultas a partir da série diária. Em períodos longos o eixo agrupa os dias por semana ou por mês
/// (<see cref="For"/>) para o gráfico continuar legível; os totais nunca mudam, só o tamanho do passo.
/// </summary>
public static class ChartData
{
    /// <summary>Até este número de dias o eixo tem um ponto por dia (cobre o mês e o trimestre).</summary>
    public const int MaxDailyDays = 92;
    /// <summary>Até este número de dias o eixo tem um ponto por semana (cobre o ano); acima disso, um por mês.</summary>
    public const int MaxWeeklyDays = 400;

    public static ChartGrouping For(int dayCount) => dayCount <= MaxDailyDays ? ChartGrouping.Day : dayCount <= MaxWeeklyDays ? ChartGrouping.Week : ChartGrouping.Month;

    /// <summary>Agrupa a série diária (ordenada por data) em semanas (começando na segunda-feira) ou meses; sem o agrupamento, escolhe pelo tamanho da série.</summary>
    public static IReadOnlyList<ChartBucket> Group(IReadOnlyList<DailyPoint> days, ChartGrouping? grouping = null)
    {
        var mode = grouping ?? For(days.Count);
        if (days.Count == 0) return [];
        if (mode == ChartGrouping.Day) return [.. days.Select(day => new ChartBucket(day.Date, day.Date, day.Completed, day.Missed, day.Cancelled, ChartGrouping.Day))];
        var buckets = new List<ChartBucket>();
        foreach (var group in days.OrderBy(day => day.Date).GroupBy(day => mode == ChartGrouping.Week ? StartOfWeek(day.Date) : new DateOnly(day.Date.Year, day.Date.Month, 1)))
            buckets.Add(new ChartBucket(group.Key, group.Max(day => day.Date), group.Sum(day => day.Completed), group.Sum(day => day.Missed), group.Sum(day => day.Cancelled), mode));
        return buckets;
    }

    private static DateOnly StartOfWeek(DateOnly date) => date.AddDays(-(((int)date.DayOfWeek + 6) % 7));

    /// <summary>As três linhas de consultas por situação (concluídas, faltas e canceladas), com as cores de situação do tema. Faltas e canceladas também são tracejadas.</summary>
    public static IReadOnlyList<ChartLine> Evolution(IReadOnlyList<DailyPoint> days, ChartGrouping? grouping = null)
    {
        var buckets = Group(days, grouping);
        if (buckets.Count == 0) return [];
        ChartLine Line(string name, string brush, Func<ChartBucket, long> value, double[]? dash) =>
            new(name, brush, [.. buckets.Select(bucket => new ChartPoint(bucket.Label, value(bucket), null, bucket.Description))], dash);
        return
        [
            Line("Concluídas", "StatusCompletedBrush", bucket => bucket.Completed, null),
            Line("Faltas", "StatusMissedBrush", bucket => bucket.Missed, [5, 3]),
            Line("Canceladas", "StatusCancelledBrush", bucket => bucket.Cancelled, [1.5, 3])
        ];
    }

    /// <summary>A pizza das consultas encerradas no período (concluídas, faltas e canceladas); vazia quando não há nenhuma.</summary>
    public static IReadOnlyList<ChartPoint> Outcomes(IReadOnlyList<DailyPoint> days)
    {
        var slices = new ChartPoint[]
        {
            new("Concluídas", days.Sum(day => day.Completed), "StatusCompletedBrush"),
            new("Faltas", days.Sum(day => day.Missed), "StatusMissedBrush"),
            new("Canceladas", days.Sum(day => day.Cancelled), "StatusCancelledBrush")
        };
        return slices.All(slice => slice.Value <= 0) ? [] : slices;
    }

    /// <summary>Texto curto do passo do eixo, para a nota do gráfico ("Cada ponto é uma semana.").</summary>
    public static string StepNote(ChartGrouping grouping) => grouping switch
    {
        ChartGrouping.Week => "Cada ponto é uma semana (a partir da segunda-feira).",
        ChartGrouping.Month => "Cada ponto é um mês.",
        _ => ""
    };
}
