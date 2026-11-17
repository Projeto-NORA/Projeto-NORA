using Nora.Api.DAOs;
using Nora.Api.Models;

namespace Nora.Api.Services;

/// <summary>Validação e regras dos comunicados; o texto é guardado e devolvido exatamente como chegou (texto puro, nunca interpretado).</summary>
public sealed class AnnouncementService(AnnouncementDao dao, TimeProvider time)
{
    public const int TitleMax = 120, BodyMax = 2000, RegionMax = 120, RegionsMax = 50, UnitsMax = 200, DefaultPage = 50, MaxPage = 100;

    public Task<AnnouncementSummary> CreateAsync(Guid actorId, CreateAnnouncementRequest request, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        return dao.CreateAsync(actorId, Validate(request, now), now, ct);
    }

    public Task<AnnouncementSummaryPage> ListOwnAsync(Guid actorId, long? before, int? limit, CancellationToken ct)
    {
        if (before is <= 0) throw new ApiInputException();
        return dao.ListOwnAsync(actorId, before, Limit(limit), time.GetUtcNow(), ct);
    }

    public async Task CloseAsync(Guid actorId, Guid id, CloseAnnouncementRequest request, CancellationToken ct)
    {
        if (id == Guid.Empty || !request.Confirmed) throw new ApiInputException();
        await dao.CloseAsync(actorId, id, time.GetUtcNow(), ct);
    }

    public Task<AnnouncementReads> ReadsAsync(Guid actorId, Guid id, CancellationToken ct)
        => id == Guid.Empty ? throw new ApiInputException() : dao.ReadsAsync(actorId, id, time.GetUtcNow(), ct);

    /// <param name="unitFilter">Atendente ligada a um computador: só os comunicados da UBS dele.</param>
    public Task<AnnouncementPage> ListForAccountAsync(Guid accountId, bool staff, long? after, int? limit, CancellationToken ct, Guid? unitFilter = null)
    {
        if (after is < 0) throw new ApiInputException();
        return dao.ListForAccountAsync(accountId, staff, after ?? 0, Limit(limit), time.GetUtcNow(), ct, unitFilter);
    }

    public Task MarkReadAsync(Guid accountId, bool staff, Guid id, CancellationToken ct, Guid? unitFilter = null)
        => id == Guid.Empty ? throw new ApiInputException() : dao.MarkReadAsync(accountId, staff, id, time.GetUtcNow(), ct, unitFilter);

    private static int Limit(int? limit) => limit is null ? DefaultPage : limit is >= 1 and <= MaxPage ? limit.Value : throw new ApiInputException();

    public static AnnouncementDraft Validate(CreateAnnouncementRequest request, DateTimeOffset now)
    {
        var title = request.Title ?? ""; var body = request.Body ?? "";
        if (title.Trim().Length == 0 || title.Length > TitleMax || title.Any(char.IsControl) || !CommunityText.Encodable(title) ||
            body.Trim().Length == 0 || body.Length > BodyMax || body.Any(c => char.IsControl(c) && c is not ('\n' or '\r' or '\t')) || !CommunityText.Encodable(body) ||
            request.Audience is not ("staff" or "patients" or "both") || request.Scope is not ("all" or "region" or "units") ||
            request.ExpiresAt is { } expires && expires <= now)
            throw new ApiInputException();
        var regions = request.Regions ?? []; var units = request.UnitIds ?? [];
        switch (request.Scope)
        {
            case "all" when regions.Length > 0 || units.Length > 0:
            case "region" when regions.Length is 0 or > RegionsMax || units.Length > 0 || regions.Any(r => r is null || r.Trim().Length is < 2 or > RegionMax || r.Any(char.IsControl) || !CommunityText.Encodable(r)):
            case "units" when units.Length is 0 or > UnitsMax || regions.Length > 0 || units.Any(u => u == Guid.Empty):
                throw new ApiInputException();
        }
        return new(title, body, request.Audience, request.Scope, [.. regions.Select(r => r.Trim())], [.. units.Distinct()], request.ExpiresAt);
    }
}
