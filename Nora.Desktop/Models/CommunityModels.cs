using System.Globalization;
using System.Text.Json.Serialization;
using Nora.Desktop.Services;

namespace Nora.Desktop.Models;

// Contratos de comunicados, Jornada (exames e vacinas), Família e prévia da desativação
// (docs/api.md, seções "Comunicação e Jornada" e "Família, e-mail da conta e manutenção").

/// <summary>Comunicado como a equipe do OBS o recebe. Título e texto são texto puro e voltam exatamente como a Secretaria enviou.</summary>
public sealed record ReceivedAnnouncement(Guid Id, long Sequence, string Title, string Body, DateTimeOffset PublishedAt, DateTimeOffset? ExpiresAt, bool IsRead, DateTimeOffset? ReadAt);

/// <summary>Rota incremental: em ordem crescente de <c>sequence</c>; <c>cursor</c> é opaco e vai no próximo <c>after</c>; <c>hasMore</c> pede nova chamada com ele.</summary>
public sealed record ReceivedAnnouncementPage(ReceivedAnnouncement[] Announcements, long Cursor, bool HasMore);

/// <summary>Visão da Secretaria. <c>status</c>: <c>active</c>, <c>expired</c> ou <c>closed</c>; as contagens são de contas que marcaram como lido.</summary>
public sealed record AnnouncementSummary(Guid Id, long Sequence, string Title, string Body, string Audience, string Scope, string[] Regions, Guid[] UnitIds,
    DateTimeOffset CreatedAt, DateTimeOffset? ExpiresAt, DateTimeOffset? ClosedAt, string Status, long StaffReads, long PatientReads)
{
    [JsonIgnore] public string AudienceDisplay { get; init; } = AnnouncementText.Audience(Audience);
    [JsonIgnore] public string ScopeDisplay { get; init; } = AnnouncementText.Scope(Scope, Regions, UnitIds, null);
    [JsonIgnore] public string StatusDisplay => Status switch { "active" => "Em vigor", "expired" => "Vencido", "closed" => "Encerrado", _ => Status };
    [JsonIgnore] public string StatusGlyph => Status switch { "active" => "", "expired" => "", "closed" => "", _ => "" };
    /// <summary>Segunda linha da situação: até quando vale, quando venceu ou quando foi encerrado.</summary>
    [JsonIgnore] public string StatusDetail => Status switch
    {
        "closed" when ClosedAt is { } closed => "em " + closed.ToLocalTime().ToString("dd/MM/yyyy HH:mm", DisplayFormat.Culture),
        "expired" when ExpiresAt is { } expired => "em " + expired.ToLocalTime().ToString("dd/MM/yyyy HH:mm", DisplayFormat.Culture),
        "active" when ExpiresAt is { } until => "até " + until.ToLocalTime().ToString("dd/MM/yyyy HH:mm", DisplayFormat.Culture),
        _ => ""
    };
    [JsonIgnore] public bool CanClose => Status == "active";
    [JsonIgnore] public string CreatedAtDisplay => CreatedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm", DisplayFormat.Culture);
    [JsonIgnore] public string ValidityDisplay => ExpiresAt is { } expires ? expires.ToLocalTime().ToString("dd/MM/yyyy HH:mm", DisplayFormat.Culture) : "Sem validade";
    [JsonIgnore] public string StaffReadsDisplay => AnnouncementText.Reads(StaffReads, Audience != "patients");
    [JsonIgnore] public string PatientReadsDisplay => AnnouncementText.Reads(PatientReads, Audience != "staff");
    [JsonIgnore] public string AutomationLabel => $"{Title}. {StatusDisplay}. Para {AudienceDisplay}. {ScopeDisplay}.";

    // Igualdade pelo conteúdo (os vetores de regiões e UBS vêm novos a cada leitura): uma releitura sem mudança não troca nenhuma linha da lista.
    public bool Equals(AnnouncementSummary? other) => other is not null && Id == other.Id && Sequence == other.Sequence && Title == other.Title && Body == other.Body && Audience == other.Audience && Scope == other.Scope
        && Regions.AsSpan().SequenceEqual(other.Regions) && UnitIds.AsSpan().SequenceEqual(other.UnitIds) && CreatedAt == other.CreatedAt && ExpiresAt == other.ExpiresAt && ClosedAt == other.ClosedAt
        && Status == other.Status && StaffReads == other.StaffReads && PatientReads == other.PatientReads && AudienceDisplay == other.AudienceDisplay && ScopeDisplay == other.ScopeDisplay;
    public override int GetHashCode() => HashCode.Combine(Id, Sequence, Status, StaffReads, PatientReads, Regions.Length, UnitIds.Length);
}
public sealed record AnnouncementSummaryPage(AnnouncementSummary[] Announcements, long? NextBefore);
public sealed record AnnouncementReader(string FullName, DateTimeOffset ReadAt)
{
    [JsonIgnore] public string ReadAtDisplay => ReadAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm", DisplayFormat.Culture);
}
/// <summary><c>staff</c> traz só as contas de equipe que leram (nome e instante, sem e-mail); as leituras de pacientes entram só na contagem.</summary>
public sealed record AnnouncementReads(long StaffReads, long PatientReads, AnnouncementReader[] Staff);

/// <summary>Corpo do POST de um comunicado. <c>regions</c> só com <c>scope</c> igual a <c>region</c>; <c>unitIds</c> só com <c>units</c>.</summary>
public sealed record AnnouncementDraft(string Title, string Body, string Audience, string Scope, string[]? Regions, Guid[]? UnitIds, DateTimeOffset? ExpiresAt);

/// <summary>Textos de exibição do público e do alcance de um comunicado.</summary>
public static class AnnouncementText
{
    public static string Audience(string audience) => audience switch { "staff" => "Equipes (OBS)", "patients" => "Pacientes", "both" => "Equipes e pacientes", _ => audience };
    public static string Scope(string scope, string[] regions, Guid[] unitIds, Func<Guid, string?>? unitName) => scope switch
    {
        "all" => "Toda a rede",
        "region" => regions.Length == 1 ? "Região: " + regions[0] : "Regiões: " + string.Join(", ", regions),
        "units" => unitName is null ? Plural(unitIds.Length, "UBS específica", "UBS específicas") : "UBS: " + string.Join(", ", unitIds.Select(id => unitName(id) ?? "UBS removida")),
        _ => scope
    };
    /// <summary>Contagem de leituras de um público; "—" quando o comunicado não é para ele.</summary>
    public static string Reads(long count, bool applies) => !applies ? "—" : DisplayFormat.Count(count);
    public static string Plural(long count, string singular, string plural) => $"{DisplayFormat.Count(count)} {(count == 1 ? singular : plural)}";
}

/// <summary>Exame da Jornada. <c>status</c>: <c>requested</c>, <c>done</c>, <c>cancelled</c> ou <c>voided</c>; <c>voidReason</c> só existe na visão da equipe.</summary>
public sealed record ExamInfo(Guid Id, string Name, string Status, DateTimeOffset RequestedAt, DateOnly? PerformedOn, string? Note, DateTimeOffset? VoidedAt, string? VoidReason = null)
{
    [JsonIgnore] public bool IsVoided => Status == "voided";
    [JsonIgnore] public bool CanChangeStatus => Status == "requested";
    [JsonIgnore] public string StatusDisplay => Status switch { "requested" => "Solicitado", "done" => "Realizado", "cancelled" => "Cancelado", "voided" => "Anulado", _ => Status };
    [JsonIgnore] public string StatusGlyph => Status switch { "requested" => "", "done" => "", "cancelled" => "", "voided" => "", _ => "" };
    [JsonIgnore] public string DateDisplay => Status == "done" && PerformedOn is { } done ? "Realizado em " + DisplayFormat.Date(done) : "Solicitado em " + RequestedAt.ToLocalTime().ToString("dd/MM/yyyy", DisplayFormat.Culture);
    [JsonIgnore] public string NoteDisplay => string.IsNullOrWhiteSpace(Note) ? "" : "Observação: " + Note;
    [JsonIgnore] public string VoidDisplay => !IsVoided ? "" : $"Anulado em {VoidedAt?.ToLocalTime().ToString("dd/MM/yyyy HH:mm", DisplayFormat.Culture)}{(string.IsNullOrWhiteSpace(VoidReason) ? "" : " · motivo: " + VoidReason)}";
    [JsonIgnore] public string AutomationLabel => $"Exame {Name}. {StatusDisplay}. {DateDisplay}.";
}

/// <summary>Vacina aplicada. <c>dose</c>: <c>1</c>, <c>2</c>, <c>3</c>, <c>booster</c> ou <c>single</c>; <c>status</c>: <c>applied</c> ou <c>voided</c>.</summary>
public sealed record VaccineInfo(Guid Id, string Name, string Dose, DateOnly AppliedOn, string Status, string? Note, DateTimeOffset? VoidedAt, string? VoidReason = null)
{
    [JsonIgnore] public bool IsVoided => Status == "voided";
    [JsonIgnore] public string StatusDisplay => IsVoided ? "Anulada" : "Aplicada";
    [JsonIgnore] public string StatusGlyph => IsVoided ? "" : "";
    [JsonIgnore] public string DoseDisplay => VaccineDose.Display(Dose);
    [JsonIgnore] public string DateDisplay => "Aplicada em " + DisplayFormat.Date(AppliedOn);
    [JsonIgnore] public string NoteDisplay => string.IsNullOrWhiteSpace(Note) ? "" : "Observação: " + Note;
    [JsonIgnore] public string VoidDisplay => !IsVoided ? "" : $"Anulada em {VoidedAt?.ToLocalTime().ToString("dd/MM/yyyy HH:mm", DisplayFormat.Culture)}{(string.IsNullOrWhiteSpace(VoidReason) ? "" : " · motivo: " + VoidReason)}";
    [JsonIgnore] public string AutomationLabel => $"Vacina {Name}, {DoseDisplay}. {StatusDisplay}. {DateDisplay}.";
}
public sealed record HealthRecords(ExamInfo[] Exams, VaccineInfo[] Vaccines);

/// <summary>Doses aceitas pela API, na ordem de exibição.</summary>
public sealed record VaccineDoseOption(string Value, string Label);
public static class VaccineDose
{
    public static readonly VaccineDoseOption[] Options = [new("1", "1ª dose"), new("2", "2ª dose"), new("3", "3ª dose"), new("booster", "Reforço"), new("single", "Dose única")];
    public static string Display(string dose) => Options.FirstOrDefault(option => option.Value == dose)?.Label ?? dose;
}

/// <summary>Pedido ou vínculo de família na visão da equipe: traz o que precisa para conferir os documentos (nome e CPF do responsável e do dependente).</summary>
public sealed record FamilyLink(Guid Id, string Status, string GuardianName, string GuardianCpf, string DependentCpf, string? DependentName, DateTimeOffset RequestedAt,
    string? DecidedByName, DateTimeOffset? DecidedAt, string? DecisionReason)
{
    [JsonIgnore] public string GuardianCpfDisplay => BrazilianDataValidator.FormatCpf(GuardianCpf);
    [JsonIgnore] public string GuardianDisplay => $"{GuardianName} · CPF {GuardianCpfDisplay}";
    [JsonIgnore] public string DependentDisplay => string.IsNullOrWhiteSpace(DependentName) ? "Dependente sem nome no cadastro" : DependentName;
    [JsonIgnore] public string DependentCpfDisplay => BrazilianDataValidator.FormatCpf(DependentCpf);
    [JsonIgnore] public string RequestedAtDisplay => RequestedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm", DisplayFormat.Culture);
    [JsonIgnore] public string DecidedDisplay => DecidedAt is { } at ? $"{at.ToLocalTime().ToString("dd/MM/yyyy HH:mm", DisplayFormat.Culture)}{(string.IsNullOrWhiteSpace(DecidedByName) ? "" : " por " + DecidedByName)}" : "—";
    [JsonIgnore] public string AutomationLabel => $"Responsável {GuardianName}, dependente {DependentDisplay}, pedido em {RequestedAtDisplay}.";
}
/// <summary>Uma página da lista da equipe: <see cref="Cursor"/> é opaco e vai em <c>after</c> para a página seguinte; <see cref="HasMore"/> diz se ela existe (API antiga sem paginação: falso).</summary>
public sealed record FamilyLinkList(FamilyLink[] Links, string? Cursor = null, bool HasMore = false);
/// <summary>Todas as páginas lidas de uma situação; <see cref="Truncated"/> diz que a leitura parou no teto de páginas e ainda havia mais.</summary>
public sealed record FamilyLinkPages(FamilyLink[] Links, bool Truncated);

/// <summary>Quantas consultas futuras seriam canceladas e quantas entradas de fila seriam encerradas se o cadastro fosse desativado.</summary>
public sealed record DeactivationPreview(int FutureAppointments, int ActiveWaitingEntries);
