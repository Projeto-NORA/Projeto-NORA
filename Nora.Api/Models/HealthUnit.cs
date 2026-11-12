namespace Nora.Api.Models;
/// <summary>DatabaseState: "ready" (chave explícita ou database_ready_at) ou "pending" (banco da UBS ainda não criado).</summary>
public sealed record HealthUnit(Guid Id, string Name, string Region, string Address, double Latitude, double Longitude, string DatabaseState = "ready");
public sealed record HealthUnitRequest(string? Name, string? Region, string? Address, double? Latitude, double? Longitude);

public sealed record StaffAccount(Guid Id, string FullName, string Role, string Email, bool IsActive, bool MustChangePassword);
