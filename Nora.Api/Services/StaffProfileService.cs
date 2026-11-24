using Nora.Api.DAOs;
using Nora.Api.Models;

namespace Nora.Api.Services;

public sealed class StaffProfileService(StaffProfileDao profiles)
{
    public Task<StaffSelfProfile> GetAsync(SessionInfo session, CancellationToken ct)
    {
        RequireStaff(session);
        return profiles.ReadAsync(session, updatedName: null, ct);
    }

    public Task<StaffSelfProfile> ChangeNameAsync(SessionInfo session, ProfileNameRequest request, CancellationToken ct)
    {
        RequireStaff(session);
        var name = request.FullName?.Trim();
        if (name is not { Length: >= 2 and <= 160 } || name.Any(char.IsControl)) throw new ApiInputException();
        return profiles.ReadAsync(session, name, ct);
    }

    private static void RequireStaff(SessionInfo session)
    {
        if (session.User.Scope is not ("obs" or "municipal")) throw new ApiForbiddenException();
    }
}
