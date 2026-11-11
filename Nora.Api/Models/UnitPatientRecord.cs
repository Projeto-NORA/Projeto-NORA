namespace Nora.Api.Models;

public sealed record UnitPatientRecord(string Cpf, string FullName, string Email, string? Phone = null, AddressInfo? Address = null, bool IsActive = true, bool IsOnline = false, DateTimeOffset? CreatedAt = null);
public sealed record UnitPatientEmailRequest(string Cpf, string Email);
public sealed record UnitPatientActiveRequest(string Cpf, [property: System.Text.Json.Serialization.JsonRequired] bool IsActive, [property: System.Text.Json.Serialization.JsonRequired] bool Confirmed);
