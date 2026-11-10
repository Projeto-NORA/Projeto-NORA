using System.Text.Json.Serialization;

namespace Nora.Api.Models;

public sealed record UnitWaitingEntry(long Id, string Cpf, string PatientName, string Specialty, DateTimeOffset EnteredAt, string Status);
public sealed record AddUnitWaitingEntryRequest(string Cpf, string Specialty);
public sealed record CancelUnitWaitingEntryRequest([property: JsonRequired] bool Confirmed);
