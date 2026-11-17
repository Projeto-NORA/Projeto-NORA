using Nora.Api.DAOs;
using Nora.Api.Models;

namespace Nora.Api.Services;

public sealed class HealthUnitService(HealthUnitDao dao, UnitDatabaseProvisioner provisioner)
{
    public Task<List<HealthUnit>> ListAsync(CancellationToken ct) => dao.ListAsync(ct);
    /// <summary>Unidades com o banco pronto: as únicas que entram no catálogo público, no vínculo, no resumo e nos indicadores.</summary>
    public async Task<List<HealthUnit>> ListReadyAsync(CancellationToken ct) => (await dao.ListAsync(ct)).Where(unit => unit.DatabaseState == "ready").ToList();
    public Task<List<HealthUnit>> ListForStaffAsync(Guid accountId, CancellationToken ct) => dao.ListForStaffAsync(accountId, ct);
    /// <summary>UBS do OBS: a atendente ligada a um computador só enxerga a UBS dele; o administrador, todas as que tem concessão.</summary>
    public async Task<List<HealthUnit>> ListForStaffAsync(SessionInfo session, CancellationToken ct)
    {
        var granted = await dao.ListForStaffAsync(session.User.Id, ct);
        return session.User.Role == "attendant" && session.DeviceUnitId is { } unit ? granted.Where(item => item.Id == unit).ToList() : granted;
    }
    public Task<List<StaffAccount>> ListStaffAsync(CancellationToken ct) => dao.ListStaffAsync(ct);
    public Task SetStaffAccessAsync(Guid accountId, Guid unitId, Guid actorId, bool grant, CancellationToken ct) => dao.SetStaffAccessAsync(accountId, unitId, actorId, grant, ct);
    public Task<List<HealthUnit>> ListForPatientAsync(Guid patientId, CancellationToken ct) => dao.ListForPatientAsync(patientId, ct);

    /// <summary>Grava a UBS e tenta criar o banco; se a criação falhar, a UBS continua cadastrada com databaseState "pending".</summary>
    public async Task<HealthUnit> CreateAsync(HealthUnitRequest request, Guid actorId, CancellationToken ct)
    {
        var created = await dao.CreateAsync(Validate(Guid.NewGuid(), request), ct);
        return await provisioner.TryProvisionAsync(created.Id, actorId, ct) ? await dao.FindAsync(created.Id, ct) ?? created : created;
    }
    public Task<HealthUnit> UpdateAsync(Guid id, HealthUnitRequest request, CancellationToken ct) => dao.UpdateAsync(Validate(id, request), ct);
    /// <summary>Nova tentativa idempotente de criar/migrar o banco; falha → 503 database-provisioning-failed.</summary>
    public async Task<HealthUnit> ProvisionDatabaseAsync(Guid id, Guid actorId, CancellationToken ct)
    {
        await provisioner.ProvisionAsync(id, actorId, ct);
        return await dao.FindAsync(id, ct) ?? throw new ApiNotFoundException();
    }
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
