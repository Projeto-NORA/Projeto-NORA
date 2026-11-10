namespace Nora.Api.Models;
public sealed record HealthUnit(Guid Id, string Name, string Region, string Address, double Latitude, double Longitude);
public sealed record HealthUnitRequest(string? Name, string? Region, string? Address, double? Latitude, double? Longitude);

public sealed record StaffAccount(Guid Id, string FullName, string Role);
