using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using Nora.Desktop.Generics;
using Nora.Desktop.Models;
using Nora.Desktop.Services;

namespace Nora.Desktop.ViewModels;

/// <summary>Opção de seletor por chave de texto (público e alcance do comunicado).</summary>
public sealed record AnnouncementOption(string Key, string Label);

/// <summary>Uma região ou UBS marcável no alcance do comunicado.</summary>
public sealed class AnnouncementChoice(string key, string label, Action? changed = null) : ObservableObject
{
    private bool selected;
    public string Key { get; } = key;
    public string Label { get; } = label;
    public bool IsChecked { get => selected; set { if (selected == value) return; selected = value; Notify(); changed?.Invoke(); } }
}

/// <summary>
/// Secretaria, seção Comunicação: cria comunicados (título e texto em texto puro, público equipes/pacientes/ambos, alcance rede/regiões/UBS, validade opcional) com pré-visualização,
/// lista os da própria conta com a contagem de leituras por público, mostra quem da equipe leu e encerra com confirmação (a confirmação é pedida na tela).
/// </summary>
public sealed partial class WorkspaceViewModel
{
    public const int AnnouncementTitleMax = 120, AnnouncementBodyMax = 2000, AnnouncementRegionsMax = 50, AnnouncementUnitsMax = 200;
    private static readonly TimeSpan MyAnnouncementsRefreshAge = TimeSpan.FromMinutes(5);
    private string announcementTitle = "", announcementBody = "", announcementExpiryDate = "", announcementExpiryTime = "23:59";
    private string? announcementAudience, announcementScope;
    private bool announcementSubmitted, myAnnouncementsLoaded, myAnnouncementsFailed, loadingMoreAnnouncements;
    private long? myAnnouncementsNext;
    private DateTimeOffset? myAnnouncementsAttemptedAt;
    private AnnouncementSummary? selectedMyAnnouncement;
    private Guid? readersFor;
    private AnnouncementReads? readers;

    public static IReadOnlyList<AnnouncementOption> AudienceOptions { get; } = [new("staff", "Equipes (OBS das UBS)"), new("patients", "Pacientes vinculados"), new("both", "Equipes e pacientes")];
    public static IReadOnlyList<AnnouncementOption> ScopeOptions { get; } = [new("all", "Toda a rede"), new("region", "Regiões escolhidas"), new("units", "UBS escolhidas")];
    public ObservableCollection<AnnouncementChoice> AnnouncementRegionChoices { get; } = [];
    public ObservableCollection<AnnouncementChoice> AnnouncementUnitChoices { get; } = [];
    public ObservableCollection<AnnouncementSummary> MyAnnouncements { get; } = [];
    public ObservableCollection<AnnouncementReader> AnnouncementReaders { get; } = [];
    public bool CanPublishAnnouncement => IsMunicipal && CanOperate;
    public bool CanChangeMyAnnouncement => IsMunicipal && CanOperate && SelectedMyAnnouncement is not null;
    public bool CanCloseMyAnnouncement => CanChangeMyAnnouncement && SelectedMyAnnouncement is { CanClose: true };
    public bool CanLoadMoreAnnouncements => IsMunicipal && CanOperate && myAnnouncementsNext is not null && !loadingMoreAnnouncements;

    // ---- Formulário ----

    public string AnnouncementTitle { get => announcementTitle; set { announcementTitle = value ?? ""; NotifyAnnouncementDraft(); } }
    public string AnnouncementBodyText { get => announcementBody; set { announcementBody = value ?? ""; NotifyAnnouncementDraft(); } }
    public string? AnnouncementAudienceKey { get => announcementAudience; set { announcementAudience = value; NotifyAnnouncementDraft(); } }
    public string? AnnouncementScopeKey
    {
        get => announcementScope;
        set { announcementScope = value; Notify(nameof(ShowRegionChoices)); Notify(nameof(ShowUnitChoices)); NotifyAnnouncementDraft(); }
    }
    public string AnnouncementExpiryDate { get => announcementExpiryDate; set { announcementExpiryDate = value ?? ""; NotifyAnnouncementDraft(); } }
    public string AnnouncementExpiryTime { get => announcementExpiryTime; set { announcementExpiryTime = value ?? ""; NotifyAnnouncementDraft(); } }
    public bool ShowRegionChoices => announcementScope == "region";
    public bool ShowUnitChoices => announcementScope == "units";

    private bool ShowAnnouncementError(string value) => announcementSubmitted || value.Length > 0;
    public string AnnouncementTitleError => !ShowAnnouncementError(announcementTitle) ? ""
        : announcementTitle.Trim().Length == 0 ? "Informe o título do comunicado."
        : announcementTitle.Length > AnnouncementTitleMax ? $"O título pode ter até {AnnouncementTitleMax} caracteres (hoje tem {announcementTitle.Length})."
        : announcementTitle.Any(char.IsControl) || !Encodable(announcementTitle) ? "O título não aceita quebra de linha nem caracteres de controle." : "";
    public string AnnouncementBodyError => !ShowAnnouncementError(announcementBody) ? ""
        : announcementBody.Trim().Length == 0 ? "Informe o texto do comunicado."
        : announcementBody.Length > AnnouncementBodyMax ? $"O texto pode ter até {DisplayFormat.Count(AnnouncementBodyMax)} caracteres (hoje tem {DisplayFormat.Count(announcementBody.Length)})."
        : announcementBody.Any(c => char.IsControl(c) && c is not ('\n' or '\r' or '\t')) || !Encodable(announcementBody) ? "O texto tem caracteres de controle que o comunicado não aceita." : "";
    public string AnnouncementAudienceError => announcementSubmitted && announcementAudience is null ? "Escolha para quem é o comunicado." : "";
    public string AnnouncementScopeError => announcementSubmitted && announcementScope is null ? "Escolha o alcance do comunicado."
        : announcementSubmitted && announcementScope == "region" && !AnnouncementRegionChoices.Any(choice => choice.IsChecked) ? "Marque ao menos uma região."
        : announcementSubmitted && announcementScope == "units" && !AnnouncementUnitChoices.Any(choice => choice.IsChecked) ? "Marque ao menos uma UBS."
        : announcementScope == "region" && AnnouncementRegionChoices.Count(choice => choice.IsChecked) > AnnouncementRegionsMax ? $"Escolha no máximo {AnnouncementRegionsMax} regiões."
        : announcementScope == "units" && AnnouncementUnitChoices.Count(choice => choice.IsChecked) > AnnouncementUnitsMax ? $"Escolha no máximo {AnnouncementUnitsMax} UBS." : "";
    public string AnnouncementExpiryError => ExpiryProblem(out _) ?? "";
    public string AnnouncementChoicesState => !IsAuthenticated ? "" : !UnitsLoaded ? "Carregando as unidades da rede…" : Units.Count == 0 ? "Nenhuma UBS cadastrada." : "";

    /// <summary>Validade opcional: data (dd/mm/aaaa) e hora (HH:mm, padrão 23:59) no horário local; precisa ser futura.</summary>
    private string? ExpiryProblem(out DateTimeOffset? expiresAt)
    {
        expiresAt = null;
        var date = announcementExpiryDate.Trim();
        if (date.Length == 0) return null;
        if (!DisplayFormat.TryParseDate(date, out var day)) return "Informe a data de validade no formato dd/mm/aaaa ou deixe em branco.";
        var time = announcementExpiryTime.Trim().Length == 0 ? "23:59" : announcementExpiryTime.Trim();
        if (!TimeSpan.TryParseExact(time, @"hh\:mm", CultureInfo.InvariantCulture, out var clock)) return "Informe a hora de validade no formato HH:mm.";
        var local = new DateTimeOffset(day.Date.Add(clock));
        if (local <= DateTimeOffset.Now) return "A validade precisa estar no futuro.";
        expiresAt = local;
        return null;
    }

    private static bool Encodable(string text)
    {
        try { new UTF8Encoding(false, true).GetByteCount(text); return true; }
        catch (EncoderFallbackException) { return false; }
    }

    private static readonly string[] AnnouncementDraftNames =
    [
        nameof(AnnouncementTitle), nameof(AnnouncementBodyText), nameof(AnnouncementAudienceKey), nameof(AnnouncementScopeKey), nameof(AnnouncementExpiryDate), nameof(AnnouncementExpiryTime),
        nameof(AnnouncementTitleError), nameof(AnnouncementBodyError), nameof(AnnouncementAudienceError), nameof(AnnouncementScopeError), nameof(AnnouncementExpiryError),
        nameof(AnnouncementPreviewTitle), nameof(AnnouncementPreviewBody), nameof(AnnouncementPreviewMeta), nameof(AnnouncementBodyCounter), nameof(AnnouncementTitleCounter)
    ];
    private void NotifyAnnouncementDraft()
    {
        foreach (var name in AnnouncementDraftNames) Notify(name);
        // A faixa "Corrija os campos destacados" sai assim que o rascunho deixa de ter erro, em vez de acusar um campo que já está certo.
        if (messageKind == MessageKind.Error && message.StartsWith("Corrija os campos destacados do comunicado", StringComparison.Ordinal)
            && AnnouncementTitleError.Length + AnnouncementBodyError.Length + AnnouncementAudienceError.Length + AnnouncementScopeError.Length + AnnouncementExpiryError.Length == 0) ClearMessage();
    }

    // ---- Pré-visualização em texto puro ----

    public string AnnouncementPreviewTitle => announcementTitle.Length == 0 ? "(título em branco)" : announcementTitle;
    public string AnnouncementPreviewBody => announcementBody.Length == 0 ? "(texto em branco)" : announcementBody;
    public string AnnouncementPreviewMeta
    {
        get
        {
            var audience = announcementAudience is null ? "público não escolhido" : AnnouncementText.Audience(announcementAudience);
            var scope = announcementScope switch
            {
                "all" => "toda a rede",
                "region" => AnnouncementRegionChoices.Where(choice => choice.IsChecked).Select(choice => choice.Label).ToArray() is { Length: > 0 } regions ? "região " + string.Join(", ", regions) : "nenhuma região marcada",
                "units" => AnnouncementUnitChoices.Where(choice => choice.IsChecked).Select(choice => choice.Label).ToArray() is { Length: > 0 } units ? "UBS " + string.Join(", ", units) : "nenhuma UBS marcada",
                _ => "alcance não escolhido"
            };
            var validity = ExpiryProblem(out var expires) is null && expires is { } at ? "vale até " + at.ToString("dd/MM/yyyy HH:mm", DisplayFormat.Culture) : "sem validade";
            return $"Para {audience} · {scope} · {validity}";
        }
    }
    public string AnnouncementTitleCounter => $"{announcementTitle.Length}/{AnnouncementTitleMax}";
    public string AnnouncementBodyCounter => $"{DisplayFormat.Count(announcementBody.Length)}/{DisplayFormat.Count(AnnouncementBodyMax)}";

    /// <summary>Regiões e UBS da rede viram opções marcáveis; o que já estava marcado continua marcado depois de uma releitura das unidades.</summary>
    private void RebuildAnnouncementChoices()
    {
        var checkedRegions = AnnouncementRegionChoices.Where(choice => choice.IsChecked).Select(choice => choice.Key).ToHashSet(StringComparer.CurrentCultureIgnoreCase);
        var checkedUnits = AnnouncementUnitChoices.Where(choice => choice.IsChecked).Select(choice => choice.Key).ToHashSet();
        var regions = Units.Select(unit => unit.Region.Trim()).Where(region => region.Length > 0).Distinct(StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(region => region, StringComparer.CurrentCultureIgnoreCase).ToArray();
        if (!AnnouncementRegionChoices.Select(choice => choice.Key).SequenceEqual(regions, StringComparer.CurrentCultureIgnoreCase))
        {
            AnnouncementRegionChoices.Clear();
            foreach (var region in regions) AnnouncementRegionChoices.Add(new(region, region, NotifyAnnouncementDraft) { IsChecked = checkedRegions.Contains(region) });
        }
        var unitKeys = Units.Select(unit => unit.Id.ToString()).ToArray();
        if (!AnnouncementUnitChoices.Select(choice => choice.Key).SequenceEqual(unitKeys))
        {
            AnnouncementUnitChoices.Clear();
            foreach (var unit in Units) AnnouncementUnitChoices.Add(new(unit.Id.ToString(), unit.SelectorLabel, NotifyAnnouncementDraft) { IsChecked = checkedUnits.Contains(unit.Id.ToString()) });
        }
        Notify(nameof(AnnouncementChoicesState)); NotifyAnnouncementDraft();
        if (MyAnnouncements.Count > 0) ReplaceMyAnnouncements(MyAnnouncements.Select(DecorateAnnouncement).ToArray());
    }

    private AnnouncementSummary DecorateAnnouncement(AnnouncementSummary item) =>
        item with { ScopeDisplay = AnnouncementText.Scope(item.Scope, item.Regions, item.UnitIds, id => Units.FirstOrDefault(unit => unit.Id == id)?.Name) };

    public void NewAnnouncementDraft()
    {
        announcementSubmitted = false; announcementTitle = announcementBody = announcementExpiryDate = ""; announcementExpiryTime = "23:59"; announcementAudience = announcementScope = null;
        foreach (var choice in AnnouncementRegionChoices) choice.IsChecked = false;
        foreach (var choice in AnnouncementUnitChoices) choice.IsChecked = false;
        Notify(nameof(ShowRegionChoices)); Notify(nameof(ShowUnitChoices)); NotifyAnnouncementDraft();
    }

    /// <summary>Descrição curta do que será publicado, para a pergunta de confirmação.</summary>
    public string AnnouncementConfirmationText => $"Publicar o comunicado \"{announcementTitle.Trim()}\"?\n\n{AnnouncementPreviewMeta}.\n\nDepois de publicado, quem estiver no alcance o vê na hora; você poderá encerrá-lo, mas não editá-lo.";

    /// <summary>Marca o formulário como enviado (mostra os erros de cada campo) e diz se já pode ser publicado; a tela só pergunta a confirmação quando é verdadeiro.</summary>
    public bool IsAnnouncementDraftValid()
    {
        announcementSubmitted = true; NotifyAnnouncementDraft();
        return AnnouncementTitleError.Length + AnnouncementBodyError.Length + AnnouncementAudienceError.Length + AnnouncementScopeError.Length + AnnouncementExpiryError.Length == 0;
    }

    public Task PublishAnnouncementAsync() => RunAsync(async () =>
    {
        if (!IsMunicipal || !IsAuthenticated) throw new NoraApiException("Entre na Secretaria para publicar comunicados.");
        announcementSubmitted = true; NotifyAnnouncementDraft();
        var problem = AnnouncementTitleError.Length > 0 ? AnnouncementTitleError : AnnouncementBodyError.Length > 0 ? AnnouncementBodyError : AnnouncementAudienceError.Length > 0 ? AnnouncementAudienceError
            : AnnouncementScopeError.Length > 0 ? AnnouncementScopeError : AnnouncementExpiryError;
        if (problem.Length > 0) throw new NoraApiException("Corrija os campos destacados do comunicado. " + problem);
        ExpiryProblem(out var expiresAt);
        string[]? regions = announcementScope == "region" ? [.. AnnouncementRegionChoices.Where(choice => choice.IsChecked).Select(choice => choice.Key)] : null;
        Guid[]? unitIds = announcementScope == "units" ? [.. AnnouncementUnitChoices.Where(choice => choice.IsChecked).Select(choice => Guid.Parse(choice.Key))] : null;
        var created = await api.CreateAnnouncementAsync(new(announcementTitle, announcementBody, announcementAudience!, announcementScope!, regions, unitIds, expiresAt));
        NewAnnouncementDraft();
        await ReadMyAnnouncementsAfterChangeAsync($"Comunicado \"{created.Title}\" publicado para {AnnouncementText.Audience(created.Audience).ToLowerInvariant()}.");
    });

    // ---- Lista dos meus comunicados ----

    public AnnouncementSummary? SelectedMyAnnouncement
    {
        get => selectedMyAnnouncement;
        set
        {
            // A grade grava null quando a lista é trocada por baixo dela; isso não é uma escolha da pessoa e não pode apagar a lista de leitores carregada.
            if (keepingMyAnnouncementSelection && value is null) return;
            var same = selectedMyAnnouncement?.Id == value?.Id;
            selectedMyAnnouncement = value;
            if (!same) ClearAnnouncementReaders();
            Notify(); Notify(nameof(CanChangeMyAnnouncement)); Notify(nameof(CanCloseMyAnnouncement)); Notify(nameof(AnnouncementReadersState));
        }
    }
    public string MyAnnouncementsState => !IsAuthenticated ? "Entre na Secretaria para ver seus comunicados."
        : !myAnnouncementsLoaded ? (myAnnouncementsFailed ? "Não foi possível ler seus comunicados agora. Use Atualizar lista para tentar de novo." : "Carregando seus comunicados…")
        : MyAnnouncements.Count == 0 ? "Você ainda não publicou comunicados." : "";
    public string MyAnnouncementsFootnote => !myAnnouncementsLoaded || MyAnnouncements.Count == 0 ? ""
        : $"{MyAnnouncements.Count} {(MyAnnouncements.Count == 1 ? "comunicado" : "comunicados")} carregado(s){(myAnnouncementsNext is null ? "." : "; há mais antigos.")}";
    private static readonly string[] MyAnnouncementNames = [nameof(MyAnnouncementsState), nameof(MyAnnouncementsFootnote), nameof(CanChangeMyAnnouncement), nameof(CanCloseMyAnnouncement), nameof(CanLoadMoreAnnouncements)];
    private void NotifyMyAnnouncements() { foreach (var name in MyAnnouncementNames) Notify(name); }
    private bool MyAnnouncementsDue => myAnnouncementsAttemptedAt is null || DateTimeOffset.Now - myAnnouncementsAttemptedAt >= MyAnnouncementsRefreshAge;

    /// <summary>Primeira página (carga da seção, atualização manual e depois de publicar ou encerrar).</summary>
    private async Task LoadMyAnnouncementsCoreAsync(bool keepLoaded = false)
    {
        myAnnouncementsAttemptedAt = DateTimeOffset.Now;
        MyAnnouncementPageResult page;
        try { var result = await api.MunicipalAnnouncementsAsync(null); page = new(result.Announcements, result.NextBefore); }
        catch (NoraApiException) { myAnnouncementsFailed = true; NotifyMyAnnouncements(); throw; }
        if (disposed || !IsAuthenticated) return;
        // A releitura traz só a primeira página. Atualizar manualmente volta à primeira página; o ciclo automático (keepLoaded) não desfaz o que a pessoa carregou:
        // as páginas mais antigas já carregadas ("Carregar mais antigos") continuam na lista, com o cursor delas.
        var fresh = page.Items.Select(DecorateAnnouncement).ToArray();
        var freshIds = fresh.Select(item => item.Id).ToHashSet();
        var older = keepLoaded ? MyAnnouncements.Where(item => !freshIds.Contains(item.Id)).ToArray() : [];
        ReplaceMyAnnouncements([.. fresh, .. older]);
        if (older.Length == 0) myAnnouncementsNext = page.Next;
        myAnnouncementsLoaded = true; myAnnouncementsFailed = false;
        NotifyMyAnnouncements();
    }

    private bool keepingMyAnnouncementSelection;
    /// <summary>Troca a lista mantendo a seleção (e os leitores já carregados) quando o comunicado escolhido continua nela.</summary>
    private void ReplaceMyAnnouncements(IReadOnlyList<AnnouncementSummary> items)
    {
        var selected = selectedMyAnnouncement?.Id;
        keepingMyAnnouncementSelection = true;
        try { Replace(MyAnnouncements, items); }
        finally { keepingMyAnnouncementSelection = false; }
        SelectedMyAnnouncement = selected is null ? null : MyAnnouncements.FirstOrDefault(item => item.Id == selected);
    }
    private readonly record struct MyAnnouncementPageResult(AnnouncementSummary[] Items, long? Next);

    public Task LoadMyAnnouncementsAsync() => RunAsync(async () =>
    {
        if (!IsMunicipal || !IsAuthenticated) throw new NoraApiException("Entre na Secretaria para ver seus comunicados.");
        await LoadMyAnnouncementsCoreAsync();
        Say(MessageKind.Success, "Comunicados atualizados.");
    });

    public Task LoadMoreMyAnnouncementsAsync() => RunAsync(async () =>
    {
        if (myAnnouncementsNext is not { } before) throw new NoraApiException("Não há comunicados mais antigos.");
        loadingMoreAnnouncements = true; NotifyMyAnnouncements();
        try
        {
            var result = await api.MunicipalAnnouncementsAsync(before);
            if (disposed || !IsAuthenticated) return;
            var known = MyAnnouncements.Select(item => item.Id).ToHashSet();
            foreach (var item in result.Announcements.Where(item => !known.Contains(item.Id))) MyAnnouncements.Add(DecorateAnnouncement(item));
            myAnnouncementsNext = result.NextBefore;
        }
        finally { loadingMoreAnnouncements = false; NotifyMyAnnouncements(); }
    });

    private async Task ReadMyAnnouncementsAfterChangeAsync(string success)
    {
        try { await LoadMyAnnouncementsCoreAsync(); Say(MessageKind.Success, success); }
        catch (NoraApiException error) when (!error.Unauthorized)
        {
            MarkStale();
            Say(MessageKind.Success, success + " Não foi possível atualizar a lista; use Atualizar lista.");
        }
    }

    // ---- Leituras e encerramento ----

    public string AnnouncementReadersState => SelectedMyAnnouncement is null ? "Selecione um comunicado para ver quem da equipe leu."
        : readersFor != SelectedMyAnnouncement.Id || readers is null ? (SelectedMyAnnouncement.Audience == "patients" ? "Este comunicado é só para pacientes: o app informa a contagem de leituras, sem nomes." : "Use \"Ver quem da equipe leu\" para carregar a lista.")
        : readers.Staff.Length == 0 ? "Nenhuma conta de equipe marcou este comunicado como lido ainda." : "";
    public string AnnouncementReadersSummary => readersFor is null || readers is null ? ""
        : $"Equipes: {DisplayFormat.Count(readers.StaffReads)} · Pacientes: {DisplayFormat.Count(readers.PatientReads)} (só a contagem, sem nomes).{(readers.Staff.Length < readers.StaffReads ? $" A lista mostra {DisplayFormat.Count(readers.Staff.Length)} contas." : "")}";
    public bool CanLoadAnnouncementReaders => CanChangeMyAnnouncement && SelectedMyAnnouncement is { Audience: not "patients" };

    public Task LoadAnnouncementReadersAsync() => RunAsync(async () =>
    {
        var item = SelectedMyAnnouncement ?? throw new NoraApiException("Selecione um comunicado.");
        var result = await api.AnnouncementReadsAsync(item.Id);
        if (disposed || !IsAuthenticated || SelectedMyAnnouncement?.Id != item.Id) return;
        readers = result; readersFor = item.Id;
        Replace(AnnouncementReaders, result.Staff.OrderByDescending(reader => reader.ReadAt).ToArray());
        NotifyReaders();
    });

    private void NotifyReaders() { Notify(nameof(AnnouncementReadersState)); Notify(nameof(AnnouncementReadersSummary)); Notify(nameof(CanLoadAnnouncementReaders)); }
    private void ClearAnnouncementReaders() { readers = null; readersFor = null; AnnouncementReaders.Clear(); NotifyReaders(); }

    public Task CloseMyAnnouncementAsync() => RunAsync(async () =>
    {
        var item = SelectedMyAnnouncement ?? throw new NoraApiException("Selecione um comunicado.");
        if (!item.CanClose) throw new NoraApiException("Este comunicado não está em vigor: só um comunicado em vigor pode ser encerrado.");
        try { await api.CloseAnnouncementAsync(item.Id); }
        catch (NoraApiException error) when (error.Status is 409 or 404 && !error.Unauthorized)
        {
            // Já encerrado (ou removido) por outra pessoa: a lista é lida de novo para mostrar o que ficou valendo, e a mensagem do erro continua.
            try { await LoadMyAnnouncementsCoreAsync(); } catch (NoraApiException reading) when (!reading.Unauthorized) { MarkStale(); }
            throw;
        }
        await ReadMyAnnouncementsAfterChangeAsync($"Comunicado \"{item.Title}\" encerrado.");
    });

    private void ClearCommunication()
    {
        NewAnnouncementDraft();
        myAnnouncementsLoaded = myAnnouncementsFailed = loadingMoreAnnouncements = false; myAnnouncementsNext = null; myAnnouncementsAttemptedAt = null;
        MyAnnouncements.Clear(); selectedMyAnnouncement = null; ClearAnnouncementReaders();
        AnnouncementRegionChoices.Clear(); AnnouncementUnitChoices.Clear();
        Notify(nameof(SelectedMyAnnouncement)); NotifyMyAnnouncements(); Notify(nameof(AnnouncementChoicesState)); NotifyAnnouncementDraft();
    }
}
