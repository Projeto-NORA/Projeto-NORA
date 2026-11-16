using System.Text.Json.Serialization;

namespace Nora.Api.Models;

public sealed record CreateExamRequest(string? Cpf, string? Name, string? Note);
/// <summary>Status destino: done (exige PerformedOn) ou cancelled.</summary>
public sealed record ExamStatusRequest(string? Status, DateOnly? PerformedOn);
/// <summary>Dose: 1, 2, 3, booster (reforço) ou single (única).</summary>
public sealed record CreateVaccineRequest(string? Cpf, string? Name, string? Dose, DateOnly? AppliedOn, string? Note);
public sealed record VoidHealthRecordRequest(string? Reason, [property: JsonRequired] bool Confirmed);
public sealed record HealthRecordsQueryRequest(string? Cpf, bool IncludeVoided = false);

/// <summary>Status: requested, done, cancelled ou voided. VoidReason só existe na visão da equipe (o paciente nunca recebe o motivo).</summary>
public sealed record ExamInfo(Guid Id, string Name, string Status, DateTimeOffset RequestedAt, DateOnly? PerformedOn, string? Note, DateTimeOffset? VoidedAt,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? VoidReason = null);
/// <summary>Status: applied ou voided.</summary>
public sealed record VaccineInfo(Guid Id, string Name, string Dose, DateOnly AppliedOn, string Status, string? Note, DateTimeOffset? VoidedAt,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? VoidReason = null);
public sealed record HealthRecords(List<ExamInfo> Exams, List<VaccineInfo> Vaccines);
public sealed record UnitHealthRecords(Guid UnitId, string UnitName, List<ExamInfo> Exams, List<VaccineInfo> Vaccines);
