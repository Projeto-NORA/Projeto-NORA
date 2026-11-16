using System.Text.Json.Serialization;

namespace Nora.Api.Models;

/// <summary>Audience: staff, patients ou both. Scope: all (rede), region (Regions) ou units (UnitIds). Title e Body são texto puro e voltam sem transformação.</summary>
public sealed record CreateAnnouncementRequest(string? Title, string? Body, string? Audience, string? Scope, string[]? Regions, Guid[]? UnitIds, DateTimeOffset? ExpiresAt);
public sealed record CloseAnnouncementRequest([property: JsonRequired] bool Confirmed);
/// <summary>Visão da Secretaria. Status: active, expired ou closed. StaffReads e PatientReads contam contas que marcaram como lido.</summary>
public sealed record AnnouncementSummary(Guid Id, long Sequence, string Title, string Body, string Audience, string Scope, string[] Regions, Guid[] UnitIds,
    DateTimeOffset CreatedAt, DateTimeOffset? ExpiresAt, DateTimeOffset? ClosedAt, string Status, long StaffReads, long PatientReads);
/// <summary>NextBefore é o cursor da página seguinte (mais antigos); nulo quando não há mais.</summary>
public sealed record AnnouncementSummaryPage(List<AnnouncementSummary> Announcements, long? NextBefore);
public sealed record AnnouncementReader(string FullName, DateTimeOffset ReadAt);
/// <summary>Staff traz só as contas de equipe (nome, sem e-mail); as leituras de pacientes entram apenas na contagem.</summary>
public sealed record AnnouncementReads(long StaffReads, long PatientReads, List<AnnouncementReader> Staff);
/// <summary>Visão da conta que recebe (equipe ou paciente).</summary>
public sealed record AnnouncementItem(Guid Id, long Sequence, string Title, string Body, DateTimeOffset PublishedAt, DateTimeOffset? ExpiresAt, bool IsRead, DateTimeOffset? ReadAt);
/// <summary>Cursor é o valor para o próximo after (trate como opaco). HasMore: há mais do que o limite; repita com o cursor devolvido.</summary>
public sealed record AnnouncementPage(List<AnnouncementItem> Announcements, long Cursor, bool HasMore);

/// <summary>Dados já validados e normalizados de um comunicado novo.</summary>
public sealed record AnnouncementDraft(string Title, string Body, string Audience, string Scope, string[] Regions, Guid[] UnitIds, DateTimeOffset? ExpiresAt);
