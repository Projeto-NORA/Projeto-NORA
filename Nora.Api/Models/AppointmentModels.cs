using System.Text.Json.Serialization;

namespace Nora.Api.Models;

/// <summary>ProfessionalId e SpecialtyId são obrigatórios quando a UBS já tem profissional ativo cadastrado (nesse caso Specialty/Professional em texto são ignorados e EndsAt é opcional).</summary>
public sealed record CreateAppointmentRequest(string Cpf, string Specialty, string Professional, DateTimeOffset StartsAt, DateTimeOffset EndsAt, Guid? ProfessionalId = null, Guid? SpecialtyId = null);
public sealed record AppointmentStatusRequest(string Status, [property: JsonRequired] bool Confirmed);
public sealed record CancelAppointmentRequest([property: JsonRequired] bool Confirmed);
/// <summary>BookedBy: staff, patient ou lightning. ConfirmedAt: confirmação de presença pelo paciente (nulo enquanto não confirmada).</summary>
public sealed record AppointmentInfo(Guid Id, string Cpf, string PatientName, string Specialty, string Professional, DateTimeOffset StartsAt, DateTimeOffset EndsAt, string Status, DateTimeOffset? ConfirmedAt = null, string BookedBy = "staff", Guid? ProfessionalId = null, Guid? SpecialtyId = null);
