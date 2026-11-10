using System.Text.Json.Serialization;

namespace Nora.Api.Models;

public sealed record CreateAppointmentRequest(string Cpf, string Specialty, string Professional, DateTimeOffset StartsAt, DateTimeOffset EndsAt);
public sealed record AppointmentStatusRequest(string Status, [property: JsonRequired] bool Confirmed);
public sealed record CancelAppointmentRequest([property: JsonRequired] bool Confirmed);
public sealed record AppointmentInfo(Guid Id, string Cpf, string PatientName, string Specialty, string Professional, DateTimeOffset StartsAt, DateTimeOffset EndsAt, string Status);
