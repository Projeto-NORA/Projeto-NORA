using System.Text.Json.Serialization;

namespace Nora.Api.Models;

/// <summary>ProfessionalId e SpecialtyId são obrigatórios quando a UBS já tem profissional ativo cadastrado (nesse caso Specialty/Professional em texto são ignorados e EndsAt é opcional).</summary>
public sealed record CreateAppointmentRequest(string Cpf, string Specialty, string Professional, DateTimeOffset StartsAt, DateTimeOffset EndsAt, Guid? ProfessionalId = null, Guid? SpecialtyId = null);
public sealed record AppointmentStatusRequest(string Status, [property: JsonRequired] bool Confirmed);
public sealed record CancelAppointmentRequest([property: JsonRequired] bool Confirmed);
/// <summary>
/// BookedBy: staff, patient ou lightning. ConfirmedAt: confirmação de presença pelo paciente (nulo enquanto não confirmada).
/// BookedByYou e BookedByGuardianName (quem marcou) só vêm preenchidos nas listas do paciente e do responsável e na resposta de agendar: se a conta que pede marcou a consulta pelo app
/// (para si ou como responsável) e o nome do responsável quando ela foi marcada por um vínculo de Família. Nas demais respostas ficam nulos.
/// </summary>
public sealed record AppointmentInfo(Guid Id, string Cpf, string PatientName, string Specialty, string Professional, DateTimeOffset StartsAt, DateTimeOffset EndsAt, string Status, DateTimeOffset? ConfirmedAt = null, string BookedBy = "staff", Guid? ProfessionalId = null, Guid? SpecialtyId = null,
    bool? BookedByYou = null, string? BookedByGuardianName = null);
