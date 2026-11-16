using System.Text.Json.Serialization;

namespace Nora.Api.Models;

// Fase 3 — agenda e metas. Horários da grade são "HH:mm" e datas de vigência "yyyy-MM-dd", no fuso Nora:TimeZone.
public sealed record Specialty(Guid Id, string Name, bool IsActive);
public sealed record SpecialtyRequest(string? Name);
public sealed record SetActiveRequest([property: JsonRequired] bool IsActive);

public sealed record Professional(Guid Id, string FullName, string? CouncilAcronym, string? CouncilNumber, string? CouncilState, bool IsActive, List<Guid> SpecialtyIds);
public sealed record ProfessionalRequest(string? FullName, List<Guid>? SpecialtyIds, string? CouncilAcronym, string? CouncilNumber, string? CouncilState);
public sealed record Room(Guid Id, string Name, bool IsActive);
public sealed record RoomRequest(string? Name);

public sealed record ScheduleTemplate(Guid Id, Guid ProfessionalId, int Weekday, string StartTime, string EndTime, int SlotMinutes, Guid RoomId, Guid SpecialtyId, string? ValidFrom, string? ValidUntil);
public sealed record ScheduleTemplateRequest(Guid? ProfessionalId, int? Weekday, string? StartTime, string? EndTime, int? SlotMinutes, Guid? RoomId, Guid? SpecialtyId, string? ValidFrom, string? ValidUntil);
public sealed record ScheduleBlock(Guid Id, Guid? ProfessionalId, DateTimeOffset StartsAt, DateTimeOffset EndsAt, string Reason);
public sealed record ScheduleBlockRequest(Guid? ProfessionalId, DateTimeOffset? StartsAt, DateTimeOffset? EndsAt, string? Reason);

public sealed record AvailableSlot(DateTimeOffset StartsAt, DateTimeOffset EndsAt, Guid ProfessionalId, string ProfessionalName, Guid SpecialtyId, string SpecialtyName, Guid RoomId, string RoomName);
public sealed record PatientBookingRequest(Guid? ProfessionalId, Guid? SpecialtyId, DateTimeOffset? StartsAt);
public sealed record ActiveRooms(int RoomsInUse, int RoomsPlannedToday);

public sealed record CapacityMetrics(long OfferedSlots, long BookedSlots, double? Occupancy);
public sealed record Goal(string Metric, Guid? UnitId, decimal Target);
public sealed record GoalRequest(decimal? Target, Guid? UnitId);
public sealed record GoalStatus(string Metric, decimal Target, double? Value, string Status);
public sealed record AlertItem(string Kind, Guid UnitId, string UnitName, string? Specialty, double Value, double Threshold, DateTimeOffset? Since, string? Metric);
