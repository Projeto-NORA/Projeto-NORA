namespace Nora.Desktop.Models;

public sealed record RetentionPolicy(int? Days);
public sealed record RetentionItem(Guid Id, string Name, bool Eligible, DateTimeOffset? ScheduledAt, bool Cancelled)
{
    public string Status => !Eligible ? "Protegido por vínculos ou histórico"
        : ScheduledAt is { } date ? $"Exclusão em {date.ToLocalTime():dd/MM/yyyy HH:mm}"
        : Cancelled ? "Exclusão cancelada" : "Sem exclusão programada";
}
public sealed record RetentionOverview(RetentionPolicy Policy, IReadOnlyList<RetentionItem> Items);
