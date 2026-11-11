using System.Text.Json.Serialization;

namespace Nora.Api.Models;

public sealed record CreateUnitLightningSlotRequest(Guid AppointmentId, [property: JsonRequired] bool Confirmed);
public sealed record UnitLightningSlot(Guid Id, Guid AppointmentId, string Specialty, string Professional, DateTimeOffset StartsAt, DateTimeOffset EndsAt, string Status, DateTimeOffset CreatedAt);
public sealed record UnitLightningOffer(Guid Id, Guid SlotId, string Specialty, string Professional, DateTimeOffset StartsAt, DateTimeOffset EndsAt, DateTimeOffset ExpiresAt, string Status);
public sealed record UnitLightningResponse([property: JsonRequired] bool Accept);
public sealed record UnitLightningResponseResult(bool Accepted, Guid? AppointmentId);
