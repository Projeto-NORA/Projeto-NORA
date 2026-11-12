using System.Text.Json.Serialization;

namespace Nora.Api.Models;

public sealed record PatientAttendance(long Completed, long Missed, long CancelledByPatient, double CancellationWeight, double? Score);
public sealed record PatientAttendanceSelf(long Completed, long Missed, long CancelledByPatient, double CancellationWeight, double? Score, int WindowMonths);
/// <summary>Attendance só é preenchido na listagem do OBS; na da Secretaria e nas entradas de gravação o campo é omitido.</summary>
public sealed record UnitPatientRecord(string Cpf, string FullName, string Email, string? Phone = null, AddressInfo? Address = null, bool IsActive = true, bool IsOnline = false, DateTimeOffset? CreatedAt = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] PatientAttendance? Attendance = null);
public sealed record UnitPatientEmailRequest(string Cpf, string Email);
public sealed record UnitPatientActiveRequest(string Cpf, [property: System.Text.Json.Serialization.JsonRequired] bool IsActive, [property: System.Text.Json.Serialization.JsonRequired] bool Confirmed);
