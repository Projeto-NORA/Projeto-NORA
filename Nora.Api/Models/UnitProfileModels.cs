namespace Nora.Api.Models;

/// <summary>Um intervalo de funcionamento: <c>weekday</c> 0 = domingo; <c>opens</c> e <c>closes</c> em "HH:mm" no fuso da API.</summary>
public sealed record OpeningHour(int Weekday, string Opens, string Closes);
public sealed record SpecialtyRef(Guid Id, string Name);
/// <summary>
/// Atendimento ao público da UBS. <c>Informed</c> falso = a UBS ainda não informou (diferente de "fechada": lista vazia com <c>Informed</c> verdadeiro). As especialidades vêm da grade vigente
/// (profissional e consultório ativos, especialidade ativa).
/// </summary>
public record UnitProfile(Guid UnitId, bool Informed, List<OpeningHour> OpeningHours, string? Services, List<SpecialtyRef> Specialties, DateTimeOffset? UpdatedAt);
/// <summary>Leitura do OBS: o perfil mais a situação da aprovação das vagas reaproveitadas.</summary>
public sealed record ObsUnitProfile(Guid UnitId, bool Informed, List<OpeningHour> OpeningHours, string? Services, List<SpecialtyRef> Specialties, DateTimeOffset? UpdatedAt, bool SlotApprovalRequired, DateTimeOffset? SlotApprovalSince)
    : UnitProfile(UnitId, Informed, OpeningHours, Services, Specialties, UpdatedAt);
public sealed record OpeningHourRequest(int? Weekday, string? Opens, string? Closes);
/// <summary><c>OpeningHours</c> substitui todos os intervalos (lista vazia = fechada todos os dias); <c>Services</c> é texto puro de até 1.000 caracteres (vazio = nenhum).</summary>
public sealed record UnitProfileRequest(List<OpeningHourRequest>? OpeningHours, string? Services);
