namespace Nora.Desktop.Models;

public sealed record RetentionPolicy(int? Days);
public sealed record RetentionItem(Guid Id, string Name, bool Eligible, DateTimeOffset? ScheduledAt, bool Cancelled)
{
    public string Status => !Eligible ? "Protegido por vínculos ou histórico"
        : ScheduledAt is { } date ? $"Exclusão em {date.ToLocalTime():dd/MM/yyyy HH:mm}"
        : Cancelled ? "Exclusão cancelada" : "Sem exclusão programada";
    /// <summary>Texto da etiqueta de situação da lista: o estado nunca depende só da cor.</summary>
    public string Tag => !Eligible ? "Protegido" : ScheduledAt is not null ? "Programada" : Cancelled ? "Cancelada" : "Elegível";
    public Tone TagTone => !Eligible ? Tone.Neutral : ScheduledAt is not null ? Tone.Pink : Cancelled ? Tone.Yellow : Tone.Blue;
    public string AutomationLabel => $"{Name}. {Tag}. {Status}.";
}
public sealed record RetentionOverview(RetentionPolicy Policy, IReadOnlyList<RetentionItem> Items);
