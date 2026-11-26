using System.Collections.ObjectModel;
using System.Runtime.ExceptionServices;
using Nora.Desktop.Generics;
using Nora.Desktop.Models;
using Nora.Desktop.Services;

namespace Nora.Desktop.ViewModels;

/// <summary>Um comunicado recebido, com o estado de leitura que muda no lugar (marcar como lido não recria a linha). Não lido aparece como texto na etiqueta, nunca só em cor.</summary>
public sealed class AnnouncementRow : ObservableObject
{
    private bool isRead;
    private DateTimeOffset? readAt;
    public AnnouncementRow(ReceivedAnnouncement item)
    {
        Id = item.Id; Sequence = item.Sequence; Title = item.Title; Body = item.Body; PublishedAt = item.PublishedAt; ExpiresAt = item.ExpiresAt;
        isRead = item.IsRead; readAt = item.ReadAt;
    }
    public Guid Id { get; }
    public long Sequence { get; }
    /// <summary>Título e texto são texto puro: vão para a tela como estão, sem interpretar nada.</summary>
    public string Title { get; }
    public string Body { get; }
    public DateTimeOffset PublishedAt { get; }
    public DateTimeOffset? ExpiresAt { get; }
    public bool IsRead => isRead;
    public bool IsUnread => !isRead;
    public string StateDisplay => isRead ? (readAt is { } at ? "Lido em " + at.ToLocalTime().ToString("dd/MM/yyyy HH:mm", DisplayFormat.Culture) : "Lido") : "Não lido";
    /// <summary>Tom da etiqueta de situação: o não lido chama a atenção (azul), o lido fica tranquilo (verde).</summary>
    public Tone StateTone => isRead ? Tone.Green : Tone.Blue;
    public string PublishedAtDisplay => "Publicado em " + PublishedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm", DisplayFormat.Culture);
    public string ValidityDisplay => ExpiresAt is { } expires ? "Vale até " + expires.ToLocalTime().ToString("dd/MM/yyyy HH:mm", DisplayFormat.Culture) : "";
    /// <summary>Linha de dados do item: publicação e validade, só o que está informado, separados por " | ".</summary>
    public string MetaLine => string.Join(" | ", new[] { PublishedAtDisplay, ValidityDisplay }.Where(text => text.Length > 0));
    public string AutomationLabel => $"{StateDisplay}. {Title}. {PublishedAtDisplay}.";
    public void MarkRead(DateTimeOffset at)
    {
        if (isRead) return;
        isRead = true; readAt = at;
        foreach (var name in new[] { nameof(IsRead), nameof(IsUnread), nameof(StateDisplay), nameof(StateTone), nameof(AutomationLabel) }) Notify(name);
    }
    /// <summary>Traz uma leitura feita em outro computador, sem desmarcar o que já foi lido aqui.</summary>
    public void Sync(ReceivedAnnouncement item) { if (item.IsRead && !isRead) MarkRead(item.ReadAt ?? DateTimeOffset.Now); }
}

/// <summary>
/// OBS: comunicados da Secretaria para a equipe, das UBS concedidas à conta (não dependem da UBS ativa). A primeira leitura e a leitura completa (ao abrir a aba
/// e quando a lista de UBS muda) trazem tudo o que está em vigor; o ciclo automático faz uma única chamada incremental com o cursor da API, que sem novidade devolve lista vazia e não mexe na tela.
/// </summary>
public sealed partial class WorkspaceViewModel
{
    private const int AnnouncementPage = 100, AnnouncementMaxPages = 20;
    private long? announcementCursor;
    private bool announcementsLoaded, announcementsFailed, announcementsFullDue;
    private int announcementsVersion, unreadAnnouncements;
    private readonly HashSet<Guid> viewedAnnouncementIds = [];
    private DateTimeOffset? announcementsReadAt;

    public ObservableCollection<AnnouncementRow> Announcements { get; } = [];
    /// <summary>Contador de não lidos da navegação.</summary>
    public int UnreadAnnouncementCount => unreadAnnouncements;
    /// <summary>Novidades ainda não vistas na aba; não altera a confirmação de leitura no servidor.</summary>
    public int AnnouncementBadgeCount => Announcements.Count(row => row.IsUnread && !viewedAnnouncementIds.Contains(row.Id));
    private void MarkAnnouncementsViewed() { viewedAnnouncementIds.UnionWith(Announcements.Select(row => row.Id)); Notify(nameof(AnnouncementBadgeCount)); }
    public bool ShowAnnouncements => IsAuthenticated && IsOperational && Section == "announcements";
    /// <summary>A primeira leitura ainda não chegou: a tela mostra o indicador e o passo escrito.</summary>
    public bool IsAnnouncementsLoading => IsAuthenticated && !announcementsLoaded && !announcementsFailed;
    /// <summary>Texto da lista vazia (ou da leitura em andamento / que falhou); vazio quando há comunicados para mostrar.</summary>
    public string AnnouncementsState => !IsAuthenticated ? "Entre na conta para ler os comunicados."
        : !announcementsLoaded ? (announcementsFailed ? "Não foi possível ler os comunicados agora. A tela tenta de novo sozinha." : "Carregando os comunicados…")
        : Announcements.Count == 0 ? "Nenhum comunicado em vigor para as UBS desta conta." : "";
    /// <summary>Só aparece quando a última leitura falhou e a lista mostrada é a de uma leitura anterior.</summary>
    public string AnnouncementsFootnote => announcementsReadAt is { } at && announcementsFailed ? $"A última leitura não concluiu; a lista é da leitura das {at:HH:mm}." : "";

    private static readonly string[] AnnouncementNames = [nameof(UnreadAnnouncementCount), nameof(AnnouncementsState), nameof(AnnouncementsFootnote), nameof(IsAnnouncementsLoading)];
    private void NotifyAnnouncements()
    {
        if (Section == "announcements") viewedAnnouncementIds.UnionWith(Announcements.Select(row => row.Id));
        unreadAnnouncements = Announcements.Count(row => row.IsUnread);
        foreach (var name in AnnouncementNames) Notify(name);
        Notify(nameof(AnnouncementBadgeCount));
    }

    /// <summary>Leitura tolerante e silenciosa (ciclo, abertura da aba e login): falha vira aviso na própria aba; sessão encerrada e troca de senha continuam subindo.</summary>
    private async Task ReadAnnouncementsAsync(bool full)
    {
        if (!IsOperational || !IsAuthenticated) return;
        var version = announcementsVersion;
        if (!announcementsLoaded || announcementCursor is null) full = true;
        var collected = new List<ReceivedAnnouncement>();
        long? after = full ? null : announcementCursor;
        long cursor = announcementCursor ?? 0;
        try
        {
            for (var page = 0; page < AnnouncementMaxPages; page++)
            {
                var feed = await api.ObsAnnouncementsAsync(after, AnnouncementPage);
                collected.AddRange(feed.Announcements); cursor = feed.Cursor; after = feed.Cursor;
                if (!feed.HasMore) break;
            }
        }
        catch (NoraApiException error) when (!error.Unauthorized && error.Code != "password-change-required")
        {
            if (disposed || version != announcementsVersion) return;
            announcementsFailed = true; Notify(nameof(AnnouncementsState)); Notify(nameof(AnnouncementsFootnote)); Notify(nameof(IsAnnouncementsLoading));
            return;
        }
        if (disposed || !IsAuthenticated || version != announcementsVersion) return;
        if (full) announcementsFullDue = false;
        ApplyAnnouncements(collected, cursor, full);
    }

    private void ApplyAnnouncements(IReadOnlyList<ReceivedAnnouncement> items, long cursor, bool full)
    {
        var wasFailed = announcementsFailed;
        announcementCursor = cursor; announcementsLoaded = true; announcementsFailed = false; announcementsReadAt = DateTimeOffset.Now;
        var now = DateTimeOffset.Now;
        var live = items.Where(item => item.ExpiresAt is null || item.ExpiresAt > now);
        var changed = false;
        if (full)
        {
            var existing = Announcements.ToDictionary(row => row.Id);
            var rows = live.OrderByDescending(item => item.Sequence).Select(item =>
            {
                if (existing.TryGetValue(item.Id, out var row)) { row.Sync(item); return row; }
                return new AnnouncementRow(item);
            }).ToArray();
            if (!Announcements.SequenceEqual(rows)) { Announcements.Clear(); foreach (var row in rows) Announcements.Add(row); changed = true; }
        }
        else
        {
            var known = Announcements.Select(row => row.Id).ToHashSet();
            foreach (var item in live.Where(item => !known.Contains(item.Id)).OrderBy(item => item.Sequence)) { Announcements.Insert(0, new AnnouncementRow(item)); changed = true; }
        }
        changed |= DropExpiredAnnouncements(now);
        // Sem novidade nada é recalculado: só o horário da leitura mudou.
        if (changed || wasFailed || full) NotifyAnnouncements(); else Notify(nameof(AnnouncementsFootnote));
    }

    /// <summary>A validade vence sozinha com o tempo: some da lista sem nova requisição.</summary>
    private bool DropExpiredAnnouncements(DateTimeOffset now)
    {
        var expired = Announcements.Where(row => row.ExpiresAt is { } expires && expires <= now).ToArray();
        foreach (var row in expired) Announcements.Remove(row);
        return expired.Length > 0;
    }

    /// <summary>Parte do ciclo da UBS e da atualização geral: leitura incremental por padrão; completa ao abrir a aba, quando a lista de UBS mudou e na atualização geral.</summary>
    private Task RefreshAnnouncementsAsync(bool manual) => ReadAnnouncementsAsync(manual || announcementsFullDue || !announcementsLoaded);

    /// <summary>
    /// Marca como lidos os comunicados escolhidos (um ou vários, numa só ação): só os ainda não lidos vão à API. Um comunicado que a API já não reconhece (encerrado ou vencido
    /// desde a última leitura) sai da lista; se nenhum pôde ser marcado por isso, o erro da API é mostrado. A lista não é recriada: as linhas mudam no lugar.
    /// </summary>
    public Task MarkAnnouncementsReadAsync(IReadOnlyList<AnnouncementRow> rows) => RunAsync(async () =>
    {
        var chosen = rows.Where(Announcements.Contains).Distinct().ToArray();
        if (chosen.Length == 0) throw new NoraApiException("Selecione um comunicado da lista.");
        var marked = 0; var gone = 0;
        NoraApiException? notFound = null;
        try
        {
            foreach (var row in chosen.Where(row => row.IsUnread))
            {
                try { await api.MarkObsAnnouncementReadAsync(row.Id); }
                catch (NoraApiException error) when (error.Status == 404) { Announcements.Remove(row); gone++; notFound ??= error; continue; }
                row.MarkRead(DateTimeOffset.Now);
                marked++;
            }
        }
        finally { NotifyAnnouncements(); }
        if (marked == 0 && notFound is not null) ExceptionDispatchInfo.Capture(notFound).Throw();
        if (marked == 0) return;
        Say(MessageKind.Success, (marked == 1 ? "Comunicado marcado como lido." : $"{marked} comunicados marcados como lidos.") + (gone > 0 ? $" {gone} já não estava em vigor e saiu da lista." : ""));
    });

    private void ClearAnnouncements()
    {
        announcementsVersion++; announcementCursor = null; announcementsLoaded = announcementsFailed = false; announcementsFullDue = false; announcementsReadAt = null;
        Announcements.Clear(); viewedAnnouncementIds.Clear(); NotifyAnnouncements();
    }
}
