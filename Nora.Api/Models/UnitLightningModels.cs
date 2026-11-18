using System.Text.Json.Serialization;

namespace Nora.Api.Models;

public sealed record CreateUnitLightningSlotRequest(Guid AppointmentId, [property: JsonRequired] bool Confirmed);
/// <summary>De onde a vaga veio: a consulta cancelada (só no cancelamento e na vaga gerada pela equipe; <c>cancelledAt/By</c> vêm do último evento <c>cancelled</c> dela, com o autor em patient, staff ou system).</summary>
public sealed record UnitSlotSource(Guid AppointmentId, string PatientName, DateTimeOffset StartsAt, DateTimeOffset EndsAt, DateTimeOffset? CancelledAt, string? CancelledBy);
/// <summary>
/// Vaga relâmpago. Horário, especialidade e profissional são da própria vaga. <c>AppointmentId</c> é nulo na vaga do "chamar próximo". Origem: <c>staff</c> (gerada pela equipe), <c>cancellation</c> (proposta
/// automática da aprovação) ou <c>call-next</c>. <c>ApprovalDeadline</c> (1 h antes da consulta) só enquanto a vaga aguarda aprovação. <c>ClosedReason</c>: too-late, approval-timeout, discarded ou conflict.
/// </summary>
public sealed record UnitLightningSlot(Guid Id, Guid? AppointmentId, string Specialty, string Professional, DateTimeOffset StartsAt, DateTimeOffset EndsAt, string Status, DateTimeOffset CreatedAt,
    string Origin = "staff", DateTimeOffset? ApprovalDeadline = null, DateTimeOffset? DecidedAt = null, string? DecidedByName = null, string? ClosedReason = null, UnitSlotSource? Source = null);
public sealed record UnitLightningOffer(Guid Id, Guid SlotId, string Specialty, string Professional, DateTimeOffset StartsAt, DateTimeOffset EndsAt, DateTimeOffset ExpiresAt, string Status);
public sealed record UnitLightningResponse([property: JsonRequired] bool Accept);
public sealed record UnitLightningResponseResult(bool Accepted, Guid? AppointmentId);
public sealed record UnitSlotOffer(Guid Id, string PatientName, string Status, DateTimeOffset ExpiresAt);
/// <summary>Corpo de aprovar e de descartar uma vaga aguardando aprovação.</summary>
public sealed record ConfirmedRequest([property: JsonRequired] bool Confirmed);
public sealed record SlotApprovalRequest([property: JsonRequired] bool Enabled, [property: JsonRequired] bool Confirmed);
public sealed record SlotApprovalState(bool Enabled, DateTimeOffset? Since, string? UpdatedByName);
/// <summary>"Chamar próximo": a atendente escolhe um horário livre da grade e a oferta vai ao primeiro elegível da fila daquela especialidade.</summary>
public sealed record CallNextRequest(Guid? SpecialtyId, Guid? ProfessionalId, DateTimeOffset? StartsAt, [property: JsonRequired] bool Confirmed);
public sealed record CallNextCandidate(long EntryId, string PatientName, DateTimeOffset EnteredAt);
public sealed record CallNextPreview(CallNextCandidate? Next);
public sealed record CallNextOffer(Guid Id, string PatientName, DateTimeOffset ExpiresAt);
public sealed record CallNextResult(UnitLightningSlot Slot, CallNextOffer Offer);
/// <summary>Resposta de <c>GET /api/obs/units/{unitId}/lightning-slots</c>: todas as vagas ativas mais uma página do histórico; <c>HistoryCursor</c> nulo = não há mais encerradas.</summary>
public sealed record UnitLightningSlotPage(List<UnitLightningSlot> Items, string? HistoryCursor);
