using System.Globalization;
using Nora.Api.DAOs;
using Nora.Api.Models;

namespace Nora.Api.Services;

/// <summary>Limites dos alertas, de <c>Nora:Alerts:*</c> (padrões decididos pelo usuário em 2026-11-11). Valor inválido impede a inicialização.</summary>
public sealed class AlertOptions
{
    /// <summary>Fila alta: este número de pessoas, ou mais, aguardando numa especialidade da UBS (<c>Nora:Alerts:HighQueue</c>, padrão 20).</summary>
    public int HighQueue { get; }
    /// <summary>Espera longa: alguém aguardando há mais que isto (<c>Nora:Alerts:LongWaitDays</c>, padrão 30).</summary>
    public int LongWaitDays { get; }
    /// <summary>Faltas: taxa acima deste valor, de 0 a 1 (<c>Nora:Alerts:MissedRate</c>, padrão 0,20).</summary>
    public double MissedRate { get; }
    /// <summary>Janela, em dias para trás, dos alertas de faltas e de metas (<c>Nora:Alerts:WindowDays</c>, padrão 30).</summary>
    public int WindowDays { get; }
    /// <summary>Ocupação: acima deste valor, de 0 a 1, da grade dos próximos dias (<c>Nora:Alerts:Occupancy</c>, padrão 0,95).</summary>
    public double Occupancy { get; }
    /// <summary>Dias à frente da ocupação (<c>Nora:Alerts:OccupancyDays</c>, padrão 7).</summary>
    public int OccupancyDays { get; }

    public AlertOptions(IConfiguration configuration)
    {
        HighQueue = Integer(configuration, "HighQueue", 20, 1, 100_000);
        LongWaitDays = Integer(configuration, "LongWaitDays", 30, 1, 36_500);
        WindowDays = Integer(configuration, "WindowDays", 30, 1, MetricsService.MaxWindowDays);
        OccupancyDays = Integer(configuration, "OccupancyDays", 7, 1, SchedulingService.MaxAvailabilityDays);
        MissedRate = Fraction(configuration, "MissedRate", 0.20);
        Occupancy = Fraction(configuration, "Occupancy", 0.95);
    }

    private static int Integer(IConfiguration configuration, string key, int fallback, int minimum, int maximum)
    {
        var text = configuration[$"Nora:Alerts:{key}"];
        if (string.IsNullOrWhiteSpace(text)) return fallback;
        return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value >= minimum && value <= maximum ? value : throw new InvalidOperationException($"Nora:Alerts:{key} inválido: informe um inteiro de {minimum} a {maximum}.");
    }

    private static double Fraction(IConfiguration configuration, string key, double fallback)
    {
        var text = configuration[$"Nora:Alerts:{key}"];
        if (string.IsNullOrWhiteSpace(text)) return fallback;
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && value is >= 0 and <= 1 ? value : throw new InvalidOperationException($"Nora:Alerts:{key} inválido: informe um número de 0 a 1.");
    }
}

/// <summary>
/// Alertas por UBS: fila alta, espera longa, faltas, ocupação da grade e meta fora. Calculados na hora a partir dos bancos das UBS; nenhum dado pessoal sai daqui
/// (só unidade, especialidade, número, limite e, na espera longa, desde quando).
/// </summary>
public sealed class AlertService(HealthUnitDao units, UnitMetricsDao metrics, SchedulingDao schedulingDao, SchedulingService scheduling, GoalService goals, AlertOptions options, TimeProvider time)
{
    private const int MaxConcurrentUnits = 4;
    public const string HighQueue = "high-queue", LongWait = "long-wait", MissedRate = "missed-rate", Occupancy = "occupancy", GoalOff = "goal-off";

    /// <summary>Alertas de uma UBS (o chamador já autorizou o OBS pela concessão da unidade). UBS sem banco pronto → lista vazia.</summary>
    public async Task<List<AlertItem>> ReadUnitAsync(Guid unitId, CancellationToken ct)
    {
        var unit = await units.FindAsync(unitId, ct) ?? throw new ApiNotFoundException();
        return unit.DatabaseState == "ready" ? Sort(await EvaluateAsync(unit, await goals.ListAsync(ct), time.GetUtcNow(), ct)) : [];
    }

    /// <summary>Rede: unidades "pending" ficam fora; banco pronto e indisponível propaga 503 (sem lista parcial). Filtros opcionais por UBS (404 se inexistente) e região.</summary>
    public async Task<List<AlertItem>> ReadNetworkAsync(Guid? unitId, string? region, CancellationToken ct)
    {
        var all = await units.ListAsync(ct);
        if (unitId is { } selected && all.All(unit => unit.Id != selected)) throw new ApiNotFoundException();
        var wanted = region?.Trim();
        var ready = all.Where(unit => unit.DatabaseState == "ready" && (unitId is null || unit.Id == unitId) && (string.IsNullOrEmpty(wanted) || string.Equals(unit.Region, wanted, StringComparison.OrdinalIgnoreCase))).ToList();
        var goalList = await goals.ListAsync(ct); var now = time.GetUtcNow();
        using var gate = new SemaphoreSlim(MaxConcurrentUnits);
        var results = await Task.WhenAll(ready.Select(async unit =>
        {
            await gate.WaitAsync(ct);
            try { return await EvaluateAsync(unit, goalList, now, ct); }
            finally { gate.Release(); }
        }));
        return Sort(results.SelectMany(items => items));
    }

    private static List<AlertItem> Sort(IEnumerable<AlertItem> items) => items.OrderBy(item => item.UnitName, StringComparer.OrdinalIgnoreCase).ThenBy(item => item.Kind, StringComparer.Ordinal)
        .ThenBy(item => item.Specialty, StringComparer.OrdinalIgnoreCase).ThenBy(item => item.Metric, StringComparer.Ordinal).ToList();

    private async Task<List<AlertItem>> EvaluateAsync(HealthUnit unit, IReadOnlyList<Goal> goalList, DateTimeOffset now, CancellationToken ct)
    {
        var alerts = new List<AlertItem>();
        foreach (var group in await schedulingDao.ReadWaitingBySpecialtyAsync(unit.Id, ct))
        {
            if (group.Count >= options.HighQueue) alerts.Add(new(HighQueue, unit.Id, unit.Name, group.Name, group.Count, options.HighQueue, null, null));
            var waitedDays = (now - group.Oldest).TotalDays;
            if (waitedDays > options.LongWaitDays) alerts.Add(new(LongWait, unit.Id, unit.Name, group.Name, Math.Round(waitedDays, 1), options.LongWaitDays, group.Oldest, null));
        }
        var data = await metrics.ReadAsync(unit.Id, now.AddDays(-options.WindowDays), now, "", ct);
        var counters = data.Total;
        if (GoalService.MissedRateOf(counters.Completed, counters.Missed) is { } missedRate && missedRate > options.MissedRate)
            alerts.Add(new(MissedRate, unit.Id, unit.Name, null, missedRate, options.MissedRate, now.AddDays(-options.WindowDays), null));
        var capacity = await scheduling.ReadCapacityAsync(unit.Id, now, now.AddDays(options.OccupancyDays), null, ct);
        if (capacity.Occupancy is { } occupancy && occupancy > options.Occupancy) alerts.Add(new(Occupancy, unit.Id, unit.Name, null, occupancy, options.Occupancy, null, null));
        var score = MetricsService.Appointments(counters).AttendanceScore;
        foreach (var goal in GoalService.Evaluate(goalList, unit.Id, score, GoalService.MissedRateOf(counters.Completed, counters.Missed), MetricsService.AverageWait(counters)))
            if (goal.Status == "off") alerts.Add(new(GoalOff, unit.Id, unit.Name, null, goal.Value ?? 0, (double)goal.Target, null, goal.Metric));
        return alerts;
    }
}
