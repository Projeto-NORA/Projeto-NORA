using System.Text.Json.Serialization;

namespace Nora.Api.Models;
public sealed record QueueEntry(long Id, Guid PatientId, string PatientName, DateTimeOffset EnteredAt, string Status);
public sealed record SlotInfo(Guid Id, string Label, string Status, DateTimeOffset CreatedAt);
public sealed record OfferInfo(Guid Id, Guid SlotId, Guid PatientId, string Label, DateTimeOffset ExpiresAt, string Status);
public sealed record AddQueueRequest(Guid PatientId);
public sealed record CreateSlotRequest(string Label);
public sealed record RespondOfferRequest([property: JsonRequired] bool Accept);
public sealed record PatientOverview(UserInfo User, List<QueueEntry> Queue, List<OfferInfo> Offers);
public sealed record LightningCohort(int Days, long OpenSlots, long AcceptedOffers, long RegisteredSlots, long CreatedOffers, long DeclinedOffers, long ExpiredOffers, long PendingOffers, long CancelledOffers);
public sealed record MunicipalSummary(long Patients, long WaitingPatients, long OpenSlots, long AcceptedOffers, long RegisteredSlots, long CreatedOffers, long DeclinedOffers, long ExpiredOffers, long PendingOffers, long CancelledOffers, List<LightningCohort> LightningCohorts);
