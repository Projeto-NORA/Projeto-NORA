using System.Text.Json.Serialization;
using Nora.Desktop.Services;

namespace Nora.Desktop.Models;

// Contrato de GET /api/municipal/metrics (docs/api.md, seção Indicadores). Nenhuma resposta traz dado pessoal.
public sealed record AppointmentMetrics(long Scheduled, long Called, long Completed, long Missed, long Cancelled, long CancelledByPatient, double CancellationWeight, double? AttendanceScore);
public sealed record WaitingMetrics(long ActiveEntries, long WaitingPatients, long AcceptedEntries, double? AverageWaitMinutes, double? LongestActiveWaitMinutes);
public sealed record LightningMetrics(long RegisteredSlots, long AcceptedOffers, long DeclinedOffers, long ExpiredOffers);
public sealed record MetricsSummary(AppointmentMetrics Appointments, WaitingMetrics Waiting, LightningMetrics Lightning);
public sealed record SpecialtyMetrics(string Specialty, long Completed, long Missed, long Cancelled, double CancellationWeight, double? AttendanceScore, long ActiveEntries, double? AverageWaitMinutes);
public sealed record DailyMetrics(string Date, long Scheduled, long Completed, long Missed, long Cancelled);
public sealed record UnitMetricsSummary(Guid UnitId, string Name, string Region, AppointmentMetrics Appointments, WaitingMetrics Waiting, LightningMetrics Lightning);
public sealed record RegionMetricsSummary(string Region, AppointmentMetrics Appointments, WaitingMetrics Waiting, LightningMetrics Lightning);
public sealed record NetworkMetrics(DateTimeOffset From, DateTimeOffset To, MetricsSummary Totals, UnitMetricsSummary[] ByUnit, RegionMetricsSummary[] ByRegion, SpecialtyMetrics[] BySpecialty, DailyMetrics[] Daily, long UnitsWithoutDatabase);

/// <summary>Opção de um filtro: valor enviado à API (vazio = sem filtro) e rótulo mostrado.</summary>
public sealed record FilterOption(string Value, string Label);

/// <summary>Cartão de indicador: título, valor já formatado e uma linha de contexto.</summary>
public sealed record MetricCard(string Title, string Value, string Hint)
{
    [JsonIgnore] public string AutomationLabel => $"{Title}: {Value}. {Hint}";
}

/// <summary>Linha das tabelas de desempenho (unidade, região ou especialidade); as colunas numéricas ordenam pelo valor, não pelo texto.</summary>
public sealed record PerformanceRow(string Name, string Region, double? Score, long Completed, long Missed, long Cancelled, long ActiveEntries, double? AverageWait)
{
    public string ScoreDisplay => DisplayFormat.Percent(Score);
    public double ScoreSort => Score ?? -1;
    /// <summary>Largura (0 a 100) da barra do comparecimento; a porcentagem escrita ao lado continua sendo a informação.</summary>
    public double ScoreBar => Math.Clamp((Score ?? 0) * 100, 0, 100);
    public string WaitDisplay => DisplayFormat.Wait(AverageWait);
    public double WaitSort => AverageWait ?? -1;
}

/// <summary>Um dia da série: o gráfico e a tabela usam exatamente estes números.</summary>
public sealed record DailyPoint(DateOnly Date, long Completed, long Missed, long Cancelled)
{
    public string DateDisplay => DisplayFormat.Date(Date);
    public string Description => $"{DateDisplay}: {Completed} concluídas, {Missed} faltas, {Cancelled} canceladas";
}
