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
/// <summary>Oferta ao paciente. <c>ReplacesAppointmentId</c>/<c>ReplacesStartsAt</c>: a consulta marcada que o aceite antecipa (cancelada sem pesar no score); nulos nas ofertas anteriores a 2026-11-24.</summary>
public sealed record UnitLightningOffer(Guid Id, Guid SlotId, string Specialty, string Professional, DateTimeOffset StartsAt, DateTimeOffset EndsAt, DateTimeOffset ExpiresAt, string Status, Guid? ReplacesAppointmentId = null, DateTimeOffset? ReplacesStartsAt = null);
public sealed record UnitLightningResponse([property: JsonRequired] bool Accept);
public sealed record UnitLightningResponseResult(bool Accepted, Guid? AppointmentId);
/// <summary>Oferta de uma vaga para o painel do OBS; <c>ReplacesStartsAt</c> = início da consulta que seria antecipada.</summary>
public sealed record UnitSlotOffer(Guid Id, string PatientName, string Status, DateTimeOffset ExpiresAt, DateTimeOffset? ReplacesStartsAt = null);
/// <summary>Resposta de <c>GET /api/obs/units/{unitId}/lightning-slots</c>: todas as vagas ativas mais uma página do histórico; <c>HistoryCursor</c> nulo = não há mais encerradas.</summary>
public sealed record UnitLightningSlotPage(List<UnitLightningSlot> Items, string? HistoryCursor);
