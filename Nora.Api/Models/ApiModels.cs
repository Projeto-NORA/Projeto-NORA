using System.Text.Json.Serialization;

namespace Nora.Api.Models;

public sealed record UserInfo(Guid Id, string FullName, string Role, string Scope);
public sealed record Account(UserInfo User, string Cpf, string Email, string PasswordHash);
public sealed record LoginRequest(string Scope, string Identifier, string Password);
public sealed record LoginResponse(string Token, DateTimeOffset ExpiresAt, UserInfo User);
public sealed record AddressInfo(string Cep, string Street, string? Number, string? Complement, string? Neighborhood, string City, string State, string? Ibge = null);
public sealed record CreatePatientRequest(string FullName, string Cpf, string Email, string Password, string? Phone = null, AddressInfo? Address = null);
public sealed record PatientInfo(Guid Id, string FullName, string Cpf, string Email, string? Phone = null, bool IsOnline = false, bool IsActive = true, DateTimeOffset? CreatedAt = null, AddressInfo? Address = null);
public sealed record SetPatientActiveRequest([property: JsonRequired] bool IsActive);
public sealed record SessionInfo(UserInfo User, string TokenHash);
public sealed record PasswordResetStartRequest(string Cpf);
public sealed record PasswordResetChallengeResponse(Guid ChallengeId, int ExpiresInSeconds);
public sealed record PasswordResetFinishRequest(Guid ChallengeId, string Code, string NewPassword);
public sealed record PasswordResetFinishResponse(bool CodeValid);
public sealed record PasswordResetTarget(Guid Id, string FullName, string Email);
