namespace Nora.Api.Models;

public sealed record UserInfo(Guid Id, string FullName, string Role, string Scope);
public sealed record Account(UserInfo User, string Cpf, string Email, string PasswordHash);
public sealed record LoginRequest(string Scope, string Identifier, string Password);
public sealed record LoginResponse(string Token, DateTimeOffset ExpiresAt, UserInfo User);
public sealed record CreatePatientRequest(string FullName, string Cpf, string Email, string Password, string? Phone = null);
public sealed record PatientInfo(Guid Id, string FullName, string Cpf, string Email, string? Phone = null, bool IsOnline = false);
public sealed record SessionInfo(UserInfo User, string TokenHash);
