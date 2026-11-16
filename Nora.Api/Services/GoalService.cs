using Nora.Api.DAOs;
using Nora.Api.Models;

namespace Nora.Api.Services;

/// <summary>
/// Metas dos indicadores. A Secretaria define uma meta da rede por métrica e, se quiser, uma sobrescrita por UBS. Status de cada meta aplicável:
/// <c>met</c> (cumprida), <c>near</c> (a até 10% da meta) e <c>off</c> (longe); <c>no-data</c> quando o indicador ainda não tem valor.
/// </summary>
public sealed class GoalService(GoalDao dao)
{
    public const string AttendanceScore = "attendance_score";
    public const string MissedRate = "missed_rate";
    public const string AverageWaitMinutes = "average_wait_minutes";
    public static readonly string[] Metrics = [AttendanceScore, MissedRate, AverageWaitMinutes];

    public Task<List<Goal>> ListAsync(CancellationToken ct) => dao.ListAsync(ct);
    public Task<Goal> SetAsync(string metric, Guid? unitId, decimal target, Guid actorId, CancellationToken ct) => dao.UpsertAsync(metric, unitId, target, actorId, ct);
    public Task RemoveAsync(string metric, Guid? unitId, CancellationToken ct) => dao.DeleteAsync(metric, unitId, ct);

    public static string ValidMetric(string? metric) => metric is not null && Metrics.Contains(metric) ? metric : throw new ApiInputException();

    /// <summary>attendance_score e missed_rate: de 0 a 1; average_wait_minutes: maior que zero (até 1 000 000). Quatro casas decimais, como o banco.</summary>
    public static decimal ValidTarget(string metric, decimal? target)
    {
        if (target is not { } value) throw new ApiInputException();
        value = Math.Round(value, 4);
        var valid = metric == AverageWaitMinutes ? value is > 0 and <= 1_000_000m : value is >= 0 and <= 1;
        return valid ? value : throw new ApiInputException();
    }

    /// <summary>Taxa de faltas: faltas ÷ (concluídas + faltas). Nula sem consulta encerrada.</summary>
    public static double? MissedRateOf(long completed, long missed) => completed + missed == 0 ? null : MetricsService.Round((double)missed / (completed + missed));

    /// <summary>Score é meta mínima; taxa de faltas e espera média são máximas. "Perto" = a até 10% da meta, comparado em decimal para não errar por ponto flutuante.</summary>
    public static string StatusOf(string metric, decimal target, double? value)
    {
        if (value is not { } measured) return "no-data";
        var actual = (decimal)measured;
        if (metric == AttendanceScore) return actual >= target ? "met" : actual >= target * 0.9m ? "near" : "off";
        return actual <= target ? "met" : actual <= target * 1.1m ? "near" : "off";
    }

    /// <summary>Metas aplicáveis à UBS (a da UBS tem precedência sobre a da rede; <paramref name="unitId"/> nulo = só as da rede) com o valor e o status de cada uma.</summary>
    public static List<GoalStatus> Evaluate(IReadOnlyList<Goal> goals, Guid? unitId, double? score, double? missedRate, double? averageWait)
    {
        var result = new List<GoalStatus>();
        foreach (var metric in Metrics)
        {
            var goal = (unitId is { } id ? goals.FirstOrDefault(item => item.Metric == metric && item.UnitId == id) : null) ?? goals.FirstOrDefault(item => item.Metric == metric && item.UnitId is null);
            if (goal is null) continue;
            var value = metric switch { AttendanceScore => score, MissedRate => missedRate, _ => averageWait };
            result.Add(new(metric, goal.Target, value, StatusOf(metric, goal.Target, value)));
        }
        return result;
    }
}
