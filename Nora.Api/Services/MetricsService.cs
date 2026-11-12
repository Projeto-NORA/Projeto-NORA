using System.Globalization;
using Nora.Api.DAOs;
using Nora.Api.Models;

namespace Nora.Api.Services;

/// <summary>Soma de várias unidades (região, rede); CPFs de espera são unidos para contar pessoas, não entradas.</summary>
internal sealed class MetricsAggregate
{
    public readonly MetricsCounters Counters = new();
    public readonly HashSet<string> WaitingCpfs = new(StringComparer.Ordinal);
    public double? LongestActiveWaitMinutes;
    public long RegisteredSlots, AcceptedOffers, DeclinedOffers, ExpiredOffers;
    public void Add(UnitMetricsData unit)
    {
        Counters.Add(unit.Total); WaitingCpfs.UnionWith(unit.WaitingCpfs);
        if (unit.LongestActiveWaitMinutes is { } longest) LongestActiveWaitMinutes = Math.Max(LongestActiveWaitMinutes ?? 0, longest);
        RegisteredSlots += unit.RegisteredSlots; AcceptedOffers += unit.AcceptedOffers; DeclinedOffers += unit.DeclinedOffers; ExpiredOffers += unit.ExpiredOffers;
    }
}

public sealed class MetricsService(HealthUnitService units, UnitMetricsDao dao)
{
    private const int MaxConcurrentUnits = 4;
    public const int MaxWindowDays = 366;

    /// <summary>Indicadores de uma UBS (o chamador já autorizou o OBS pela concessão da unidade).</summary>
    public async Task<UnitMetrics> ReadUnitAsync(Guid unitId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        ValidateWindow(from, to);
        var data = await dao.ReadAsync(unitId, from, to, "", ct);
        var all = new MetricsAggregate(); all.Add(data);
        var summary = Summary(all);
        return new(from, to, summary.Appointments, summary.Waiting, summary.Lightning, Specialties(data.Specialties), Daily(data.Daily));
    }

    /// <summary>Rede: unidades "pending" ficam fora e são contadas em unitsWithoutDatabase; banco pronto e indisponível propaga 503 (sem total parcial).</summary>
    public async Task<NetworkMetrics> ReadNetworkAsync(DateTimeOffset from, DateTimeOffset to, Guid? unitId, string? region, string? specialty, CancellationToken ct)
    {
        ValidateWindow(from, to);
        var filter = NormalizeSpecialty(specialty);
        var all = await units.ListAsync(ct);
        if (unitId is { } selected && all.All(unit => unit.Id != selected)) throw new ApiNotFoundException();
        var scoped = all.Where(unit => (unitId is null || unit.Id == unitId) && (string.IsNullOrWhiteSpace(region) || string.Equals(unit.Region, region.Trim(), StringComparison.OrdinalIgnoreCase))).ToList();
        var ready = scoped.Where(unit => unit.DatabaseState == "ready").ToList();
        using var gate = new SemaphoreSlim(MaxConcurrentUnits);
        var data = await Task.WhenAll(ready.Select(async unit =>
        {
            await gate.WaitAsync(ct);
            try { return await dao.ReadAsync(unit.Id, from, to, filter, ct); }
            finally { gate.Release(); }
        }));
        var total = new MetricsAggregate(); var regions = new Dictionary<string, (string Name, MetricsAggregate Aggregate)>(StringComparer.OrdinalIgnoreCase);
        var specialties = new Dictionary<string, SpecialtyData>(StringComparer.Ordinal); var daily = new SortedDictionary<DateOnly, MetricsCounters>();
        var byUnit = new List<UnitMetricsSummary>();
        for (var i = 0; i < ready.Count; i++)
        {
            var unit = ready[i]; var item = data[i];
            total.Add(item);
            if (!regions.TryGetValue(unit.Region, out var entry)) regions[unit.Region] = entry = (unit.Region, new());
            entry.Aggregate.Add(item);
            foreach (var (key, group) in item.Specialties)
            {
                if (!specialties.TryGetValue(key, out var merged)) specialties[key] = merged = new();
                merged.Add(group);
            }
            foreach (var (day, counters) in item.Daily)
            {
                if (!daily.TryGetValue(day, out var merged)) daily[day] = merged = new();
                merged.Add(counters);
            }
            var one = new MetricsAggregate(); one.Add(item); var summary = Summary(one);
            byUnit.Add(new(unit.Id, unit.Name, unit.Region, summary.Appointments, summary.Waiting, summary.Lightning));
        }
        var byRegion = regions.Values.OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase).Select(entry =>
        {
            var summary = Summary(entry.Aggregate);
            return new RegionMetricsSummary(entry.Name, summary.Appointments, summary.Waiting, summary.Lightning);
        }).ToList();
        return new(from, to, Summary(total), byUnit, byRegion, Specialties(specialties), Daily(daily), scoped.Count - ready.Count);
    }

    internal static void ValidateWindow(DateTimeOffset from, DateTimeOffset to)
    {
        if (to <= from || to - from > TimeSpan.FromDays(MaxWindowDays)) throw new ApiInputException();
    }

    private static string NormalizeSpecialty(string? value)
    {
        var text = value?.Trim() ?? "";
        if (text.Length > 120 || text.Any(char.IsControl)) throw new ApiInputException();
        return text;
    }

    private static MetricsSummary Summary(MetricsAggregate aggregate) => new(Appointments(aggregate.Counters),
        Waiting(aggregate.Counters, aggregate.WaitingCpfs.Count, aggregate.LongestActiveWaitMinutes),
        new(aggregate.RegisteredSlots, aggregate.AcceptedOffers, aggregate.DeclinedOffers, aggregate.ExpiredOffers));

    internal static AppointmentMetrics Appointments(MetricsCounters c) => new(c.Scheduled, c.Called, c.Completed, c.Missed, c.Cancelled, c.CancelledByPatient, Round(c.Weight), Score(c.Completed, c.Missed, c.Weight));
    private static WaitingMetrics Waiting(MetricsCounters c, int patients, double? longest) => new(c.ActiveEntries, patients, c.AcceptedEntries, AverageWait(c), longest is { } value ? Math.Round(value, 1) : null);
    private static double? AverageWait(MetricsCounters c) => c.AcceptedEntries == 0 ? null : Math.Round(c.WaitMinutes / c.AcceptedEntries, 1);

    private static List<SpecialtyMetrics> Specialties(Dictionary<string, SpecialtyData> groups) => groups.Values
        .Select(group => new SpecialtyMetrics(group.Representative(), group.Counters.Completed, group.Counters.Missed, group.Counters.Cancelled, Round(group.Counters.Weight),
            Score(group.Counters.Completed, group.Counters.Missed, group.Counters.Weight), group.Counters.ActiveEntries, AverageWait(group.Counters)))
        .OrderBy(item => item.Specialty, StringComparer.OrdinalIgnoreCase).ToList();

    private static List<DailyMetrics> Daily(SortedDictionary<DateOnly, MetricsCounters> days) => days
        .Select(day => new DailyMetrics(day.Key.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), day.Value.Scheduled, day.Value.Completed, day.Value.Missed, day.Value.Cancelled)).ToList();

    /// <summary>Quatro casas evitam ruído de ponto flutuante (0,02 + 0,1) sem esconder diferença real.</summary>
    internal static double Round(double value) => Math.Round(value, 4);
    internal static double? Score(long completed, long missed, double weight) => AttendanceScore.Compute(completed, missed, weight) is { } score ? Round(score) : null;
}
