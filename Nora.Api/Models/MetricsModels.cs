namespace Nora.Api.Models;

public sealed record AppointmentMetrics(long Scheduled, long Called, long Completed, long Missed, long Cancelled, long CancelledByPatient, double CancellationWeight, double? AttendanceScore);
public sealed record WaitingMetrics(long ActiveEntries, long WaitingPatients, long AcceptedEntries, double? AverageWaitMinutes, double? LongestActiveWaitMinutes);
public sealed record LightningMetrics(long RegisteredSlots, long AcceptedOffers, long DeclinedOffers, long ExpiredOffers);
public sealed record MetricsSummary(AppointmentMetrics Appointments, WaitingMetrics Waiting, LightningMetrics Lightning);
public sealed record SpecialtyMetrics(string Specialty, long Completed, long Missed, long Cancelled, double CancellationWeight, double? AttendanceScore, long ActiveEntries, double? AverageWaitMinutes);
public sealed record DailyMetrics(string Date, long Scheduled, long Completed, long Missed, long Cancelled);
public sealed record UnitMetrics(DateTimeOffset From, DateTimeOffset To, AppointmentMetrics Appointments, WaitingMetrics Waiting, LightningMetrics Lightning, List<SpecialtyMetrics> BySpecialty, List<DailyMetrics> Daily);
public sealed record UnitMetricsSummary(Guid UnitId, string Name, string Region, AppointmentMetrics Appointments, WaitingMetrics Waiting, LightningMetrics Lightning);
public sealed record RegionMetricsSummary(string Region, AppointmentMetrics Appointments, WaitingMetrics Waiting, LightningMetrics Lightning);
public sealed record NetworkMetrics(DateTimeOffset From, DateTimeOffset To, MetricsSummary Totals, List<UnitMetricsSummary> ByUnit, List<RegionMetricsSummary> ByRegion, List<SpecialtyMetrics> BySpecialty, List<DailyMetrics> Daily, long UnitsWithoutDatabase);
