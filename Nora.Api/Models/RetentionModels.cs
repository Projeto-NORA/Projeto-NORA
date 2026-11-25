namespace Nora.Api.Models;

public sealed record RetentionPolicy(int? Days, DateTimeOffset? EffectiveAt);
public sealed record RetentionItem(Guid Id, string Name, bool Eligible, DateTimeOffset? ScheduledAt, bool Cancelled);
public sealed record RetentionOverview(RetentionPolicy Policy, IReadOnlyList<RetentionItem> Items);
public sealed record RetentionChange(int? Days, bool Confirmed);
