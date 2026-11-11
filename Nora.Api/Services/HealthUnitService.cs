using Nora.Api.DAOs;
using Nora.Api.Models;

namespace Nora.Api.Services;

public sealed class HealthUnitService(HealthUnitDao dao)
{
    public Task<List<HealthUnit>> ListAsync(CancellationToken ct) => dao.ListAsync(ct);
    public Task<List<HealthUnit>> ListForStaffAsync(Guid accountId, CancellationToken ct) => dao.ListForStaffAsync(accountId, ct);
    public Task<List<StaffAccount>> ListStaffAsync(CancellationToken ct) => dao.ListStaffAsync(ct);
    public Task SetStaffAccessAsync(Guid accountId, Guid unitId, Guid actorId, bool grant, CancellationToken ct) => dao.SetStaffAccessAsync(accountId, unitId, actorId, grant, ct);
    public Task<List<HealthUnit>> ListForPatientAsync(Guid patientId, CancellationToken ct) => dao.ListForPatientAsync(patientId, ct);
    public Task<HealthUnit> CreateAsync(HealthUnitRequest request, CancellationToken ct) => dao.CreateAsync(Validate(Guid.NewGuid(), request), ct);
    public Task<HealthUnit> UpdateAsync(Guid id, HealthUnitRequest request, CancellationToken ct) => dao.UpdateAsync(Validate(id, request), ct);
    public static HealthUnit Validate(Guid id, HealthUnitRequest request)
    {
        var name = request.Name?.Trim() ?? "";
        var region = request.Region?.Trim() ?? "";
        var address = request.Address?.Trim() ?? "";
        if (name.Length is < 2 or > 160 || region.Length is < 2 or > 120 || address.Length is < 2 or > 240 ||
            name.Any(char.IsControl) || region.Any(char.IsControl) || address.Any(char.IsControl) ||
            request.Latitude is not { } latitude || request.Longitude is not { } longitude ||
            !double.IsFinite(latitude) || !double.IsFinite(longitude) || latitude is < -85 or > 85 || longitude is < -180 or > 180)
            throw new ApiInputException();
        return new HealthUnit(id, name, region, address, latitude, longitude);
    }
}
