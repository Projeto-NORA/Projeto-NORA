using Nora.Desktop.Models;

namespace Nora.Desktop.ViewModels;

public sealed partial class WorkspaceViewModel
{
    internal (Guid Account, Guid? Unit)? RetentionScope => user is { } account && !MustChangePassword
        ? IsMunicipal && account.Role == "manager" ? (account.Id, null)
        : IsOperational && IsAdministrator && ActiveObsUnit is { } unit ? (account.Id, unit.Id) : null
        : null;

    internal Task<RetentionOverview> ReadRetentionAsync(Guid? unit, CancellationToken cancellation) =>
        api.RetentionAsync(unit, cancellation);

    internal Task<RetentionPolicy> SaveRetentionAsync(Guid? unit, int? days) =>
        api.SaveRetentionAsync(unit, days);

    internal Task ScheduleDeletionAsync(Guid? unit, Guid item, int? days) =>
        api.ScheduleDeletionAsync(unit, item, days);
}
