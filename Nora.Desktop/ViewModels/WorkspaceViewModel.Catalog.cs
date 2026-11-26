using System.Collections.ObjectModel;
using System.Globalization;
using Nora.Desktop.Generics;
using Nora.Desktop.Models;
using Nora.Desktop.Services;

namespace Nora.Desktop.ViewModels;

/// <summary>Opção de seletor por identificador; <see cref="Guid.Empty"/> representa a opção "nenhum" (por exemplo, a UBS inteira).</summary>
public sealed record GuidChoice(Guid Id, string Label);
public sealed record WeekdayOption(int Day, string Label);

/// <summary>
/// Uma especialidade marcável no formulário do profissional. Uma especialidade desativada pela Secretaria só aparece na edição de um profissional que já a tem:
/// continua marcada (o PUT a mantém), mas, desmarcada, não pode ser marcada de novo (vínculo novo exige especialidade ativa).
/// </summary>
public sealed class SpecialtyChoice(Specialty specialty, bool isChecked) : ObservableObject
{
    private bool selected = isChecked;
    public Specialty Specialty { get; } = specialty;
    public string Name => Specialty.IsActive ? Specialty.Name : Specialty.Name + " (desativada pela Secretaria)";
    public bool CanCheck => Specialty.IsActive || selected;
    public bool IsChecked { get => selected; set { if (selected == value || (value && !CanCheck)) return; selected = value; Notify(); Notify(nameof(CanCheck)); } }
}

/// <summary>
/// Informações da UBS (OBS): profissionais, consultórios, grade semanal e bloqueios. Todos lêem; só o administrador da UBS grava (o atendente vê as ações desabilitadas
/// com a explicação). Depois de cada alteração a API é relida, então a tela mostra o que ficou gravado; sem botão Atualizar, a tela também relê sozinha ao entrar e no ciclo de 20 s.
/// </summary>
public sealed partial class WorkspaceViewModel
{
    private bool catalogLoaded, catalogFailed, setupLoaded, setupFailed;
    private DateTimeOffset? catalogAttemptedAt;
    public static readonly TimeSpan CatalogRefreshAge = TimeSpan.FromMinutes(5);
    private static readonly string[] BrazilianStates = ["AC", "AL", "AP", "AM", "BA", "CE", "DF", "ES", "GO", "MA", "MT", "MS", "MG", "PA", "PB", "PR", "PE", "PI", "RJ", "RN", "RS", "RO", "RR", "SC", "SP", "SE", "TO"];
    public static IReadOnlyList<WeekdayOption> WeekdayOptions { get; } = [.. Enumerable.Range(0, 7).Select(day => new WeekdayOption(day, ScheduleTemplate.WeekdayName(day)))];
    /// <summary>Os sete botões de dia da grade semanal, de segunda a domingo (o número do dia é o da API: 0 = domingo).</summary>
    public static IReadOnlyList<WeekdayOption> ScheduleDayOptions { get; } = [.. new[] { 1, 2, 3, 4, 5, 6, 0 }.Select(day => new WeekdayOption(day, ScheduleTemplate.WeekdayName(day).Split('-')[0]))];

    /// <summary>Todas as especialidades da rede (com as desativadas), lidas de GET /api/obs/specialties.</summary>
    public ObservableCollection<Specialty> ObsSpecialties { get; } = [];
    public ObservableCollection<Professional> Professionals { get; } = [];
    public ObservableCollection<Room> Rooms { get; } = [];
    public ObservableCollection<ScheduleTemplate> ScheduleRows { get; } = [];
    /// <summary>Os horários da grade só do dia escolhido nos botões de dia (a listagem da tela).</summary>
    public ObservableCollection<ScheduleTemplate> ScheduleDayRows { get; } = [];
    public ObservableCollection<ScheduleBlock> ScheduleBlocks { get; } = [];
    public ObservableCollection<SpecialtyChoice> ProfessionalSpecialtyChoices { get; } = [];
    public ObservableCollection<Professional> ActiveProfessionalOptions { get; } = [];
    public ObservableCollection<Room> ActiveRoomOptions { get; } = [];
    public ObservableCollection<Specialty> ScheduleSpecialtyOptions { get; } = [];
    public ObservableCollection<GuidChoice> BlockScopeOptions { get; } = [new(Guid.Empty, "UBS inteira")];

    public bool IsAdministrator => user?.Role == "administrator";
    /// <summary>Só o administrador da UBS grava profissionais, consultórios, grade e bloqueios; o atendente apenas lê.</summary>
    public bool CanEditCatalog => IsOperational && CanOperate && HasActiveObsUnit && IsAdministrator;
    public bool ShowCatalogReadOnlyNotice => IsAuthenticated && IsOperational && !IsAdministrator;
    public string CatalogReadOnlyNotice => "Somente o administrador da UBS cadastra e altera profissionais, consultórios, grade de horários e bloqueios. Esta conta pode consultar tudo, mas os botões de alteração ficam desabilitados.";
    public bool NoSpecialtyChoices => ProfessionalSpecialtyChoices.Count == 0;
    public string ProfessionalsState => !HasActiveObsUnit ? "Escolha uma UBS no Painel da UBS para ver os profissionais."
        : catalogFailed ? "Não foi possível ler os profissionais agora. A tela tenta de novo sozinha."
        : !catalogLoaded ? "Carregando os profissionais…"
        : Professionals.Count == 0 ? "Nenhum profissional cadastrado. Enquanto não houver profissional ativo, a agenda aceita especialidade e profissional em texto livre." : "";
    public string RoomsState => !HasActiveObsUnit ? "Escolha uma UBS para ver os consultórios."
        : setupFailed ? "Não foi possível ler os consultórios agora. A tela tenta de novo sozinha."
        : !setupLoaded ? "Carregando os consultórios…" : Rooms.Count == 0 ? "Nenhum consultório cadastrado." : "";
    public string ScheduleState => !HasActiveObsUnit ? "Escolha uma UBS para ver a grade."
        : setupFailed ? "Não foi possível ler a grade agora. A tela tenta de novo sozinha."
        : !setupLoaded ? "Carregando a grade…" : ScheduleRows.Count == 0 ? "Nenhum horário na grade. Sem grade, a UBS não oferece horários livres." : "";
    /// <summary>O que a lista do dia escolhido diz quando está vazia: o estado da grade inteira ou, com a grade preenchida, a ausência de horário naquele dia.</summary>
    public string ScheduleListState => ScheduleState.Length > 0 ? ScheduleState : ScheduleDayRows.Count == 0 ? $"Nenhum horário na grade de {ScheduleTemplate.WeekdayName(scheduleDay.Day).ToLowerInvariant()}." : "";
    public string BlocksState => !HasActiveObsUnit ? "Escolha uma UBS para ver os bloqueios."
        : setupFailed ? "Não foi possível ler os bloqueios agora. A tela tenta de novo sozinha."
        : !setupLoaded ? "Carregando os bloqueios…" : ScheduleBlocks.Count == 0 ? "Nenhum bloqueio nos próximos 12 meses." : "";
    private bool CatalogDue => catalogAttemptedAt is null || DateTimeOffset.Now - catalogAttemptedAt >= CatalogRefreshAge;

    private static readonly string[] CatalogNames =
    [
        nameof(NoSpecialtyChoices), nameof(ProfessionalsState), nameof(RoomsState), nameof(ScheduleState), nameof(ScheduleListState), nameof(BlocksState), nameof(CanEditCatalog), nameof(UsesCatalogBooking), nameof(BookingState), nameof(CanBook)
    ];
    private void NotifyCatalog() { foreach (var name in CatalogNames) Notify(name); }

    // ---- Leitura ----

    /// <summary>Aplica as leituras tolerantes do catálogo: uma falha mantém o que já estava na tela e marca a seção, sem impedir o resto da UBS.</summary>
    private void ApplyCatalog(UnitSnapshot data)
    {
        if (data.Specialties is { } specialties && !specialties.Failed && specialties.Value is { } specialtyList)
            Replace(ObsSpecialties, specialtyList.OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase).ToArray());
        if (data.Professionals is { } professionals)
        {
            catalogFailed = professionals.Failed || (data.Specialties?.Failed ?? false);
            if (professionals.Value is not null) catalogLoaded = true;
            catalogAttemptedAt = data.CatalogAt;
        }
        if (data.Specialties is not null || data.Professionals is not null)
        {
            var selectedId = SelectedProfessional?.Id;
            Replace(Professionals, DecorateProfessionals(data.Professionals?.Value is { } fresh ? fresh : [.. Professionals]));
            if (selectedId is { } id) SelectedProfessional = Professionals.FirstOrDefault(item => item.Id == id);
        }
        if (data.Rooms is not null || data.Schedule is not null || data.Blocks is not null)
        {
            setupFailed = (data.Rooms?.Failed ?? false) || (data.Schedule?.Failed ?? false) || (data.Blocks?.Failed ?? false);
            if (data.Rooms?.Value is { } rooms)
            {
                var selectedId = SelectedRoom?.Id;
                Replace(Rooms, rooms.OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase).ToArray());
                if (selectedId is { } id) SelectedRoom = Rooms.FirstOrDefault(item => item.Id == id);
            }
            if (!setupFailed) setupLoaded = true;
        }
        // Grade e bloqueios mostram nomes de profissionais, consultórios e especialidades: refazem-se sempre que uma dessas listas mudou.
        if (data.Schedule?.Value is not null || data.Rooms is not null || data.Professionals is not null || data.Specialties is not null)
        {
            var selectedId = SelectedSchedule?.Id;
            Replace(ScheduleRows, DecorateSchedule(data.Schedule?.Value is { } schedule ? schedule : [.. ScheduleRows]));
            if (selectedId is { } id) SelectedSchedule = ScheduleRows.FirstOrDefault(item => item.Id == id);
            RebuildScheduleDay();
        }
        if (data.Blocks?.Value is not null || data.Professionals is not null)
        {
            Replace(ScheduleBlocks, DecorateBlocks(data.Blocks?.Value is { } blocks ? blocks : [.. ScheduleBlocks]));
        }
        RebuildCatalogChoices();
        NotifyCatalog();
    }

    private Professional[] DecorateProfessionals(IEnumerable<Professional> source) =>
        [.. source.OrderBy(item => item.FullName, StringComparer.CurrentCultureIgnoreCase)
            .Select(item => item with { SpecialtiesDisplay = string.Join(", ", item.SpecialtyIds.Select(id => ObsSpecialties.FirstOrDefault(s => s.Id == id)?.Name ?? "Especialidade removida").OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)) })];
    private ScheduleTemplate[] DecorateSchedule(IEnumerable<ScheduleTemplate> source) =>
        [.. source.OrderBy(item => item.Weekday).ThenBy(item => item.StartTime, StringComparer.Ordinal).ThenBy(item => item.ProfessionalId)
            .Select(item => item with { ProfessionalName = Professionals.FirstOrDefault(p => p.Id == item.ProfessionalId)?.FullName ?? "—", RoomName = Rooms.FirstOrDefault(r => r.Id == item.RoomId)?.Name ?? "—", SpecialtyName = ObsSpecialties.FirstOrDefault(s => s.Id == item.SpecialtyId)?.Name ?? "—" })];

    private ScheduleBlock[] DecorateBlocks(IEnumerable<ScheduleBlock> source) =>
        [.. source.OrderBy(item => item.StartsAt).ThenBy(item => item.Id)
            .Select(item => item with { ScopeDisplay = item.ProfessionalId is { } id ? Professionals.FirstOrDefault(p => p.Id == id)?.FullName ?? "Profissional" : "UBS inteira" })];

    /// <summary>Listas dos formulários e do agendamento derivadas da leitura: só entram especialidades, profissionais e consultórios ativos.</summary>
    private void RebuildCatalogChoices()
    {
        RebuildProfessionalChoices(ProfessionalSpecialtyChoices.Where(choice => choice.IsChecked).Select(choice => choice.Specialty.Id).ToHashSet());
        // Trocar a lista por baixo de um ComboBox faz ele gravar null na escolha (o item sumiu da lista); isso não é uma escolha da pessoa.
        // As escolhas do rascunho da grade são guardadas antes da troca e voltam depois, pelo id.
        var professionalId = scheduleProfessional?.Id; var roomId = scheduleRoom?.Id;
        choiceSync++;
        try
        {
            Replace(ActiveProfessionalOptions, Professionals.Where(item => item.IsActive).ToArray());
            Replace(ActiveRoomOptions, Rooms.Where(item => item.IsActive).ToArray());
            Replace(BlockScopeOptions, new[] { new GuidChoice(Guid.Empty, "UBS inteira") }.Concat(ActiveProfessionalOptions.Select(item => new GuidChoice(item.Id, item.FullName))).ToArray());
        }
        finally { choiceSync--; }
        if (professionalId is { } keptProfessional) RestoreChoice(ActiveProfessionalOptions.FirstOrDefault(item => item.Id == keptProfessional), ref scheduleProfessional, value => ScheduleProfessional = value, nameof(ScheduleProfessional));
        if (roomId is { } keptRoom) RestoreChoice(ActiveRoomOptions.FirstOrDefault(item => item.Id == keptRoom), ref scheduleRoom, value => ScheduleRoom = value, nameof(ScheduleRoom));
        if (blockScope != Guid.Empty && BlockScopeOptions.All(item => item.Id != blockScope)) BlockScope = Guid.Empty;
        RebuildScheduleSpecialties();
        RebuildBookingOptions();
    }

    // ---- Resultado dos pop-ups de cadastro ----

    /// <summary>
    /// Os pop-ups de cadastro (<c>DialogService.ShowForm</c>) só fecham quando o salvamento deu certo. As operações de gravar marcam isto assim que a API aceita; a leitura de novo que vem
    /// depois não conta (se ela falha, a alteração continua gravada e a tela avisa que pode estar desatualizada).
    /// </summary>
    private bool formSaved;
    /// <summary>A mensagem de erro da última operação, para o pop-up mostrar dentro dele; vazia quando a última mensagem não era de erro.</summary>
    public string FormError => messageKind == MessageKind.Error ? message : "";

    // ---- Profissionais ----

    private Professional? selectedProfessional;
    private Guid? editingProfessionalId;
    /// <summary>Especialidades gravadas no profissional em edição: as desativadas entre elas continuam oferecidas no formulário.</summary>
    private Guid[] editingSpecialtyIds = [];
    private bool professionalSubmitted;
    private string professionalName = "", councilAcronym = "", councilNumber = "", councilState = "", professionalNameServerError = "";
    /// <summary>O profissional sobre o qual a ação do balão atua (e que "Editar" abre no pop-up).</summary>
    public Professional? SelectedProfessional { get => selectedProfessional; set { selectedProfessional = value; Notify(); } }
    public string ProfessionalDraftTitle => editingProfessionalId is null ? "Cadastrar profissional" : "Editar profissional";
    public string ProfessionalName { get => professionalName; set { professionalName = value; professionalNameServerError = ""; NotifyProfessionalErrors(); Notify(); } }
    public string CouncilAcronym { get => councilAcronym; set { councilAcronym = value ?? ""; NotifyProfessionalErrors(); Notify(); } }
    public string CouncilNumber { get => councilNumber; set { councilNumber = value ?? ""; NotifyProfessionalErrors(); Notify(); } }
    public string CouncilState { get => councilState; set { councilState = value ?? ""; NotifyProfessionalErrors(); Notify(); } }
    private bool ShowProfessionalError(string value) => professionalSubmitted || value.Length > 0;
    public string ProfessionalNameError => professionalNameServerError.Length > 0 ? professionalNameServerError
        : ShowProfessionalError(professionalName) && (professionalName.Trim().Length is < 2 or > 160 || professionalName.Any(char.IsControl)) ? "Informe o nome do profissional, de 2 a 160 caracteres." : "";
    public string ProfessionalSpecialtyError => professionalSubmitted && !ProfessionalSpecialtyChoices.Any(choice => choice.IsChecked) ? "Marque ao menos uma especialidade." : "";
    /// <summary>Conselho é opcional, mas sigla, número e UF vêm juntos (ou nenhum).</summary>
    public string CouncilError
    {
        get
        {
            var any = councilAcronym.Trim().Length + councilNumber.Trim().Length + councilState.Trim().Length > 0;
            if (!any || !professionalSubmitted) return "";
            var acronym = councilAcronym.Trim();
            if (acronym.Length == 0 || councilNumber.Trim().Length == 0 || councilState.Trim().Length == 0) return "Conselho: informe a sigla, o número e a UF juntos, ou deixe os três em branco.";
            if (acronym.Length is < 2 or > 10 || !acronym.All(char.IsLetter)) return "Conselho: a sigla precisa ter de 2 a 10 letras.";
            if (councilNumber.Trim().Length > 20) return "Conselho: o número pode ter até 20 caracteres.";
            return BrazilianStates.Contains(councilState.Trim().ToUpperInvariant()) ? "" : "Conselho: informe uma UF válida (por exemplo, SP).";
        }
    }
    private static readonly string[] ProfessionalErrorNames = [nameof(ProfessionalNameError), nameof(ProfessionalSpecialtyError), nameof(CouncilError)];
    private void NotifyProfessionalErrors() { foreach (var name in ProfessionalErrorNames) Notify(name); }
    /// <summary>Chamado pela tela quando uma especialidade é marcada ou desmarcada.</summary>
    public void SpecialtyChoiceChanged() => Notify(nameof(ProfessionalSpecialtyError));

    /// <summary>Oferece as especialidades ativas e, na edição, as desativadas que o profissional já tem; só recria as opções quando a lista muda.</summary>
    private void RebuildProfessionalChoices(IReadOnlySet<Guid> checkedIds)
    {
        var offered = ObsSpecialties.Where(item => item.IsActive || editingSpecialtyIds.Contains(item.Id)).ToArray();
        if (!ProfessionalSpecialtyChoices.Select(choice => choice.Specialty).SequenceEqual(offered)) Replace(ProfessionalSpecialtyChoices, offered.Select(item => new SpecialtyChoice(item, checkedIds.Contains(item.Id))).ToArray());
    }

    public void NewProfessionalDraft()
    {
        editingProfessionalId = null; editingSpecialtyIds = []; professionalSubmitted = false; professionalName = councilAcronym = councilNumber = councilState = professionalNameServerError = "";
        foreach (var choice in ProfessionalSpecialtyChoices) choice.IsChecked = false;
        RebuildProfessionalChoices(new HashSet<Guid>());
        foreach (var name in new[] { nameof(ProfessionalName), nameof(CouncilAcronym), nameof(CouncilNumber), nameof(CouncilState), nameof(ProfessionalDraftTitle) }) Notify(name);
        NotifyProfessionalErrors();
    }
    public void EditSelectedProfessional()
    {
        if (SelectedProfessional is not { } professional) return;
        NewProfessionalDraft();
        editingProfessionalId = professional.Id; editingSpecialtyIds = professional.SpecialtyIds; professionalName = professional.FullName; councilAcronym = professional.CouncilAcronym ?? ""; councilNumber = professional.CouncilNumber ?? ""; councilState = professional.CouncilState ?? "";
        var linked = professional.SpecialtyIds.ToHashSet();
        RebuildProfessionalChoices(linked);
        foreach (var choice in ProfessionalSpecialtyChoices) choice.IsChecked = linked.Contains(choice.Specialty.Id);
        foreach (var name in new[] { nameof(ProfessionalName), nameof(CouncilAcronym), nameof(CouncilNumber), nameof(CouncilState), nameof(ProfessionalDraftTitle) }) Notify(name);
        NotifyProfessionalErrors();
    }

    public Task SaveProfessionalAsync() => RunAsync(async () =>
    {
        var unit = ActiveObsUnit ?? throw new NoraApiException("Escolha uma UBS antes de cadastrar o profissional.");
        RequireAdministrator();
        professionalSubmitted = true; professionalNameServerError = ""; NotifyProfessionalErrors();
        if (ProfessionalNameError.Length + ProfessionalSpecialtyError.Length + CouncilError.Length > 0) throw new NoraApiException("Corrija os campos destacados no formulário do profissional.");
        var ids = ProfessionalSpecialtyChoices.Where(choice => choice.IsChecked).Select(choice => choice.Specialty.Id).ToArray();
        var noCouncil = councilAcronym.Trim().Length + councilNumber.Trim().Length + councilState.Trim().Length == 0;
        var editing = editingProfessionalId;
        try
        {
            if (editing is { } id) await api.UpdateProfessionalAsync(unit.Id, id, professionalName.Trim(), ids, noCouncil ? null : councilAcronym.Trim(), noCouncil ? null : councilNumber.Trim(), noCouncil ? null : councilState.Trim().ToUpperInvariant());
            else await api.CreateProfessionalAsync(unit.Id, professionalName.Trim(), ids, noCouncil ? null : councilAcronym.Trim(), noCouncil ? null : councilNumber.Trim(), noCouncil ? null : councilState.Trim().ToUpperInvariant());
        }
        catch (NoraApiException error) when (error.Status == 409) { professionalNameServerError = error.Message; NotifyProfessionalErrors(); throw; }
        formSaved = true;
        NewProfessionalDraft();
        await ReloadCatalogAfterChangeAsync(unit, UnitScope.Catalog | UnitScope.Setup, editing is null ? "Profissional cadastrado." : "Profissional atualizado.");
    });

    /// <summary>Grava o profissional do pop-up e diz se deu certo (o pop-up só fecha com verdadeiro); o motivo da recusa fica em <see cref="FormError"/> e nos campos.</summary>
    public async Task<bool> SaveProfessionalFormAsync() { formSaved = false; await SaveProfessionalAsync(); return formSaved; }

    /// <summary>
    /// Desativa ou reativa profissionais (um ou vários, com uma só leitura depois). Vale o estado que a pessoa escolheu na tela: quem já está nele não recebe nada
    /// (alternar a partir da leitura nova faria o contrário do que foi confirmado).
    /// </summary>
    public Task ChangeProfessionalsActiveAsync(IReadOnlyList<Guid> professionalIds, bool activate) => RunAsync(async () =>
    {
        var unit = ActiveObsUnit ?? throw new NoraApiException("Escolha uma UBS antes de alterar o profissional.");
        RequireAdministrator();
        var chosen = professionalIds.Select(id => Professionals.FirstOrDefault(item => item.Id == id) ?? throw new NoraApiException("O profissional não está mais na lista desta UBS.")).ToArray();
        var changing = chosen.Where(item => item.IsActive != activate).ToArray();
        if (changing.Length == 0)
        {
            Say(MessageKind.Info, chosen.Length == 1 ? AlreadyInStateText(chosen[0].FullName, "profissional", activate) : $"Os {chosen.Length} profissionais já estão {(activate ? "ativos" : "desativados")} (outra pessoa alterou). Nada foi enviado.");
            return;
        }
        await ChangeEachAsync(unit, changing, item => api.SetProfessionalActiveAsync(unit.Id, item.Id, activate), UnitScope.Catalog | UnitScope.Setup,
            changing.Length == 1 ? (activate ? "Profissional reativado." : "Profissional desativado. As consultas já marcadas continuam como estavam.")
            : activate ? $"{changing.Length} profissionais reativados." : $"{changing.Length} profissionais desativados. As consultas já marcadas continuam como estavam.");
    });

    /// <summary>Aplica a mesma alteração a vários itens e relê uma vez no fim. Se a API recusa no meio, o que já foi aceito fica gravado e a tela é relida antes de a recusa ser mostrada.</summary>
    private async Task ChangeEachAsync<T>(HealthUnit unit, IReadOnlyList<T> items, Func<T, Task> change, UnitScope scope, string success)
    {
        var done = 0;
        try
        {
            foreach (var item in items) { await change(item); done++; }
        }
        catch (NoraApiException) when (done > 0)
        {
            try { await LoadUnitScopeAsync(unit, scope); }
            catch (NoraApiException reading) when (!reading.Unauthorized) { MarkStale(); }
            throw;
        }
        await ReloadCatalogAfterChangeAsync(unit, scope, success);
    }

    private static string AlreadyInStateText(string name, string kind, bool activate) =>
        $"{name} já está {(activate ? "ativo" : "desativado")} (outra pessoa alterou este {kind}). Nada foi enviado.";

    // ---- Consultórios ----

    private Room? selectedRoom;
    private Guid? editingRoomId;
    private bool roomSubmitted;
    private string roomName = "", roomServerError = "";
    /// <summary>O consultório sobre o qual a ação do balão atua (e que "Renomear" abre no pop-up).</summary>
    public Room? SelectedRoom { get => selectedRoom; set { selectedRoom = value; Notify(); } }
    public string RoomDraftTitle => editingRoomId is null ? "Cadastrar consultório" : "Renomear consultório";
    public string RoomName { get => roomName; set { roomName = value; roomServerError = ""; Notify(); Notify(nameof(RoomNameError)); } }
    public string RoomNameError => roomServerError.Length > 0 ? roomServerError : (roomSubmitted || roomName.Length > 0) && roomName.Trim().Length is < 1 or > 80 ? "Informe o nome do consultório, de 1 a 80 caracteres." : "";
    public void NewRoomDraft() { editingRoomId = null; roomSubmitted = false; roomName = roomServerError = ""; Notify(nameof(RoomName)); Notify(nameof(RoomNameError)); Notify(nameof(RoomDraftTitle)); }
    public void EditSelectedRoom() { if (SelectedRoom is not { } room) return; NewRoomDraft(); editingRoomId = room.Id; roomName = room.Name; Notify(nameof(RoomName)); Notify(nameof(RoomDraftTitle)); }

    public Task SaveRoomAsync() => RunAsync(async () =>
    {
        var unit = ActiveObsUnit ?? throw new NoraApiException("Escolha uma UBS antes de cadastrar o consultório.");
        RequireAdministrator();
        roomSubmitted = true; roomServerError = ""; Notify(nameof(RoomNameError));
        if (RoomNameError.Length > 0) throw new NoraApiException("Corrija o nome do consultório.");
        var editing = editingRoomId;
        try
        {
            if (editing is { } id) await api.RenameRoomAsync(unit.Id, id, roomName.Trim()); else await api.CreateRoomAsync(unit.Id, roomName.Trim());
        }
        catch (NoraApiException error) when (error.Status == 409) { roomServerError = error.Message; Notify(nameof(RoomNameError)); throw; }
        formSaved = true;
        NewRoomDraft();
        await ReloadCatalogAfterChangeAsync(unit, UnitScope.Setup, editing is null ? "Consultório cadastrado." : "Consultório renomeado.");
    });

    /// <summary>Grava o consultório do pop-up e diz se deu certo.</summary>
    public async Task<bool> SaveRoomFormAsync() { formSaved = false; await SaveRoomAsync(); return formSaved; }

    /// <summary>Desativa ou reativa consultórios (um ou vários, com uma só leitura depois), pelo estado que a pessoa escolheu na tela.</summary>
    public Task ChangeRoomsActiveAsync(IReadOnlyList<Guid> roomIds, bool activate) => RunAsync(async () =>
    {
        var unit = ActiveObsUnit ?? throw new NoraApiException("Escolha uma UBS antes de alterar o consultório.");
        RequireAdministrator();
        var chosen = roomIds.Select(id => Rooms.FirstOrDefault(item => item.Id == id) ?? throw new NoraApiException("O consultório não está mais na lista desta UBS.")).ToArray();
        var changing = chosen.Where(item => item.IsActive != activate).ToArray();
        if (changing.Length == 0)
        {
            Say(MessageKind.Info, chosen.Length == 1 ? AlreadyInStateText(chosen[0].Name, "consultório", activate) : $"Os {chosen.Length} consultórios já estão {(activate ? "ativos" : "desativados")} (outra pessoa alterou). Nada foi enviado.");
            return;
        }
        await ChangeEachAsync(unit, changing, item => api.SetRoomActiveAsync(unit.Id, item.Id, activate), UnitScope.Setup,
            changing.Length == 1 ? (activate ? "Consultório reativado." : "Consultório desativado. Ele sai dos horários livres.")
            : activate ? $"{changing.Length} consultórios reativados." : $"{changing.Length} consultórios desativados. Eles saem dos horários livres.");
    });

    // ---- Grade semanal ----

    private ScheduleTemplate? selectedSchedule;
    private Guid? editingScheduleId;
    private bool scheduleSubmitted;
    private Professional? scheduleProfessional;
    private Room? scheduleRoom;
    private Specialty? scheduleSpecialty;
    private int scheduleWeekday = 1;
    private WeekdayOption scheduleDay = ScheduleDayOptions[0];
    private string scheduleStart = "08:00", scheduleEnd = "12:00", scheduleSlotMinutes = "30", scheduleValidFrom = "", scheduleValidUntil = "", scheduleServerError = "";
    /// <summary>O horário da grade sobre o qual a ação do balão atua (e que "Editar" abre no pop-up).</summary>
    public ScheduleTemplate? SelectedSchedule { get => selectedSchedule; set { selectedSchedule = value; Notify(); } }
    /// <summary>O dia dos botões da grade semanal (segunda-feira ao abrir). Trocar de dia troca a listagem e o dia que o próximo "Novo horário" já traz.</summary>
    public WeekdayOption ScheduleDay
    {
        get => scheduleDay;
        set { if (value is null || scheduleDay.Day == value.Day) return; scheduleDay = value; Notify(); RebuildScheduleDay(); }
    }
    private void RebuildScheduleDay()
    {
        Replace(ScheduleDayRows, ScheduleRows.Where(row => row.Weekday == scheduleDay.Day).ToArray());
        Notify(nameof(ScheduleListState));
    }
    public string ScheduleDraftTitle => editingScheduleId is null ? "Adicionar horário à grade" : "Editar horário da grade";
    public Professional? ScheduleProfessional { get => scheduleProfessional; set { if (choiceSync > 0 && value is null) return; scheduleProfessional = value; ClearScheduleServerError(); Notify(); RebuildScheduleSpecialties(); NotifyScheduleErrors(); } }
    public Room? ScheduleRoom { get => scheduleRoom; set { if (choiceSync > 0 && value is null) return; scheduleRoom = value; ClearScheduleServerError(); Notify(); NotifyScheduleErrors(); } }
    public Specialty? ScheduleSpecialty { get => scheduleSpecialty; set { if (choiceSync > 0 && value is null) return; scheduleSpecialty = value; ClearScheduleServerError(); Notify(); NotifyScheduleErrors(); } }
    public int ScheduleWeekday { get => scheduleWeekday; set { scheduleWeekday = value; ClearScheduleServerError(); Notify(); NotifyScheduleErrors(); } }
    public string ScheduleStart { get => scheduleStart; set { scheduleStart = value; ClearScheduleServerError(); Notify(); NotifyScheduleErrors(); } }
    public string ScheduleEnd { get => scheduleEnd; set { scheduleEnd = value; ClearScheduleServerError(); Notify(); NotifyScheduleErrors(); } }
    public string ScheduleSlotMinutes { get => scheduleSlotMinutes; set { scheduleSlotMinutes = value; ClearScheduleServerError(); Notify(); NotifyScheduleErrors(); } }
    public string ScheduleValidFrom { get => scheduleValidFrom; set { scheduleValidFrom = value ?? ""; ClearScheduleServerError(); Notify(); NotifyScheduleErrors(); } }
    public string ScheduleValidUntil { get => scheduleValidUntil; set { scheduleValidUntil = value ?? ""; ClearScheduleServerError(); Notify(); NotifyScheduleErrors(); } }
    private void ClearScheduleServerError() { if (scheduleServerError.Length > 0) { scheduleServerError = ""; Notify(nameof(ScheduleError)); } }
    private void NotifyScheduleErrors() { Notify(nameof(ScheduleError)); Notify(nameof(ScheduleOverlapError)); }
    /// <summary>Aviso de sobreposição (409) devolvido pela API, junto aos campos da grade.</summary>
    public string ScheduleOverlapError => scheduleServerError;
    public string ScheduleError => scheduleServerError.Length == 0 && scheduleSubmitted ? ScheduleFieldProblem(out _) ?? "" : "";

    private void RebuildScheduleSpecialties()
    {
        var allowed = scheduleProfessional is { } professional ? ObsSpecialties.Where(item => item.IsActive && professional.SpecialtyIds.Contains(item.Id)).ToArray() : [];
        var chosenId = scheduleSpecialty?.Id;
        choiceSync++;
        try { Replace(ScheduleSpecialtyOptions, allowed); }
        finally { choiceSync--; }
        if (chosenId is { } id) ScheduleSpecialty = ScheduleSpecialtyOptions.FirstOrDefault(item => item.Id == id);
        else if (ScheduleSpecialtyOptions.Count == 1) ScheduleSpecialty = ScheduleSpecialtyOptions[0];
    }

    /// <summary>
    /// Enquanto uma lista de opções é trocada, o ComboBox grava null na escolha ligada a ela; esse null não é decisão da pessoa e o rascunho o ignora.
    /// Depois da troca a escolha volta, pelo id, apontando para o item novo.
    /// </summary>
    private int choiceSync;
    private void RestoreChoice<T>(T? current, ref T? field, Action<T?> assign, string propertyName) where T : class
    {
        if (current is null || !Equals(current, field)) { assign(current); return; }
        // Mesmo conteúdo: só aponta para a instância nova e avisa a tela, sem refazer o que depende da escolha.
        if (!ReferenceEquals(current, field)) { field = current; Notify(propertyName); }
    }

    /// <summary>Confere a grade antes de enviar (a API repete as regras). Devolve a primeira mensagem ou nulo.</summary>
    private string? ScheduleFieldProblem(out ScheduleDraft? draft)
    {
        draft = null;
        if (scheduleProfessional is null) return "Escolha o profissional.";
        if (scheduleSpecialty is null) return "Escolha a especialidade do atendimento (uma das do profissional).";
        if (scheduleRoom is null) return "Escolha o consultório.";
        if (!TimeSpan.TryParseExact(scheduleStart.Trim(), @"hh\:mm", CultureInfo.InvariantCulture, out var start) || !TimeSpan.TryParseExact(scheduleEnd.Trim(), @"hh\:mm", CultureInfo.InvariantCulture, out var end)) return "Informe o início e o fim no formato HH:mm.";
        if (end <= start) return "O horário final precisa ser depois do inicial.";
        if (!int.TryParse(scheduleSlotMinutes.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var minutes) || minutes is < 5 or > 240) return "A duração do atendimento vai de 5 a 240 minutos.";
        if (end - start < TimeSpan.FromMinutes(minutes)) return "A duração precisa caber ao menos uma vez entre o início e o fim.";
        DateTime from = default, until = default;
        var hasFrom = scheduleValidFrom.Trim().Length > 0; var hasUntil = scheduleValidUntil.Trim().Length > 0;
        if ((hasFrom && !DisplayFormat.TryParseDate(scheduleValidFrom, out from)) || (hasUntil && !DisplayFormat.TryParseDate(scheduleValidUntil, out until))) return "Informe a vigência no formato dd/mm/aaaa ou deixe em branco.";
        if (hasFrom && hasUntil && from > until) return "A vigência inicial não pode ser posterior à final.";
        draft = new(scheduleProfessional.Id, scheduleWeekday, scheduleStart.Trim(), scheduleEnd.Trim(), minutes, scheduleRoom.Id, scheduleSpecialty.Id,
            hasFrom ? from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : null, hasUntil ? until.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : null);
        return null;
    }

    /// <summary>Rascunho novo da grade: já vem com o dia escolhido nos botões de dia.</summary>
    public void NewScheduleDraft()
    {
        editingScheduleId = null; scheduleSubmitted = false; scheduleServerError = "";
        scheduleProfessional = null; scheduleRoom = null; scheduleSpecialty = null; scheduleWeekday = scheduleDay.Day; scheduleStart = "08:00"; scheduleEnd = "12:00"; scheduleSlotMinutes = "30"; scheduleValidFrom = scheduleValidUntil = "";
        RebuildScheduleSpecialties();
        foreach (var name in new[] { nameof(ScheduleProfessional), nameof(ScheduleRoom), nameof(ScheduleSpecialty), nameof(ScheduleWeekday), nameof(ScheduleStart), nameof(ScheduleEnd), nameof(ScheduleSlotMinutes), nameof(ScheduleValidFrom), nameof(ScheduleValidUntil), nameof(ScheduleDraftTitle) }) Notify(name);
        NotifyScheduleErrors();
    }
    public void EditSelectedSchedule()
    {
        if (SelectedSchedule is not { } row) return;
        NewScheduleDraft();
        editingScheduleId = row.Id; scheduleWeekday = row.Weekday; scheduleStart = row.StartTime; scheduleEnd = row.EndTime; scheduleSlotMinutes = row.SlotMinutes.ToString(CultureInfo.InvariantCulture);
        scheduleValidFrom = ToDisplayDate(row.ValidFrom); scheduleValidUntil = ToDisplayDate(row.ValidUntil);
        scheduleProfessional = ActiveProfessionalOptions.FirstOrDefault(item => item.Id == row.ProfessionalId); scheduleRoom = ActiveRoomOptions.FirstOrDefault(item => item.Id == row.RoomId);
        RebuildScheduleSpecialties(); scheduleSpecialty = ScheduleSpecialtyOptions.FirstOrDefault(item => item.Id == row.SpecialtyId);
        foreach (var name in new[] { nameof(ScheduleProfessional), nameof(ScheduleRoom), nameof(ScheduleSpecialty), nameof(ScheduleWeekday), nameof(ScheduleStart), nameof(ScheduleEnd), nameof(ScheduleSlotMinutes), nameof(ScheduleValidFrom), nameof(ScheduleValidUntil), nameof(ScheduleDraftTitle) }) Notify(name);
    }
    private static string ToDisplayDate(string? value) => DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? DisplayFormat.Date(date) : "";

    public Task SaveScheduleAsync() => RunAsync(async () =>
    {
        var unit = ActiveObsUnit ?? throw new NoraApiException("Escolha uma UBS antes de alterar a grade.");
        RequireAdministrator();
        scheduleSubmitted = true; scheduleServerError = "";
        var problem = ScheduleFieldProblem(out var draft); NotifyScheduleErrors();
        if (problem is not null || draft is null) throw new NoraApiException(problem ?? "Confira os campos da grade.");
        var editing = editingScheduleId;
        try
        {
            if (editing is { } id) await api.UpdateScheduleAsync(unit.Id, id, draft); else await api.CreateScheduleAsync(unit.Id, draft);
        }
        catch (NoraApiException error) when (error.Status == 409)
        {
            // A API devolve o mesmo 409 para sobreposição e para profissional ou consultório desativado por outra pessoa. O cadastro é relido e, se foi a desativação, a mensagem diz isso.
            var professionalId = draft.ProfessionalId; var roomId = draft.RoomId;
            try { await LoadUnitScopeAsync(unit, UnitScope.Catalog | UnitScope.Setup); }
            catch (NoraApiException reading) when (!reading.Unauthorized) { MarkStale(); }
            var inactive = Professionals.FirstOrDefault(item => item.Id == professionalId) is { IsActive: false } ? "O profissional escolhido foi desativado por outra pessoa. Escolha outro profissional ou reative este em Profissionais."
                : Rooms.FirstOrDefault(item => item.Id == roomId) is { IsActive: false } ? "O consultório escolhido foi desativado por outra pessoa. Escolha outro consultório ou reative este em Consultórios." : null;
            scheduleServerError = inactive ?? error.Message; NotifyScheduleErrors();
            if (inactive is null) throw;
            throw new NoraApiException(inactive, code: error.Code, status: error.Status);
        }
        formSaved = true;
        NewScheduleDraft();
        await ReloadCatalogAfterChangeAsync(unit, UnitScope.Catalog | UnitScope.Setup, editing is null ? "Horário adicionado à grade." : "Horário da grade atualizado.");
    });

    /// <summary>Grava o horário do pop-up da grade e diz se deu certo.</summary>
    public async Task<bool> SaveScheduleFormAsync() { formSaved = false; await SaveScheduleAsync(); return formSaved; }

    /// <summary>Remove horários da grade (um ou vários, com uma só leitura depois). Consultas já marcadas não são afetadas.</summary>
    public Task DeleteSchedulesAsync(IReadOnlyList<Guid> scheduleIds) => RunAsync(async () =>
    {
        var unit = ActiveObsUnit ?? throw new NoraApiException("Escolha uma UBS antes de alterar a grade.");
        RequireAdministrator();
        if (scheduleIds.Count == 0) throw new NoraApiException("Selecione um horário da grade.");
        await ChangeEachAsync(unit, scheduleIds, id => api.DeleteScheduleAsync(unit.Id, id), UnitScope.Catalog | UnitScope.Setup,
            scheduleIds.Count == 1 ? "Horário removido da grade. Consultas já marcadas não foram afetadas." : $"{scheduleIds.Count} horários removidos da grade. Consultas já marcadas não foram afetadas.");
        SelectedSchedule = null;
    });

    // ---- Bloqueios ----

    private bool blockSubmitted, blockFullDay = true;
    private Guid blockScope = Guid.Empty;
    private string blockFrom = "", blockTo = "", blockStartTime = "08:00", blockEndTime = "12:00", blockReason = "", blockServerError = "";
    public Guid BlockScope { get => blockScope; set { blockScope = value; Notify(); } }
    public bool BlockFullDay { get => blockFullDay; set { blockFullDay = value; Notify(); Notify(nameof(BlockTimesEnabled)); Notify(nameof(BlockError)); } }
    public bool BlockTimesEnabled => !blockFullDay;
    public string BlockFromText { get => blockFrom; set { blockFrom = value ?? ""; blockServerError = ""; Notify(); Notify(nameof(BlockError)); } }
    public string BlockToText { get => blockTo; set { blockTo = value ?? ""; blockServerError = ""; Notify(); Notify(nameof(BlockError)); } }
    public string BlockStartTime { get => blockStartTime; set { blockStartTime = value; blockServerError = ""; Notify(); Notify(nameof(BlockError)); } }
    public string BlockEndTime { get => blockEndTime; set { blockEndTime = value; blockServerError = ""; Notify(); Notify(nameof(BlockError)); } }
    public string BlockReason { get => blockReason; set { blockReason = value; blockServerError = ""; Notify(); Notify(nameof(BlockError)); } }
    public string BlockError => blockServerError.Length > 0 ? blockServerError : blockSubmitted ? BlockProblem(out _, out _) ?? "" : "";

    /// <summary>Intervalo do bloqueio. Dia inteiro (feriado ou folga): da meia-noite local do primeiro dia à meia-noite local depois do último; senão, as datas com os horários informados.</summary>
    private string? BlockProblem(out DateTimeOffset startsAt, out DateTimeOffset endsAt)
    {
        startsAt = endsAt = default;
        if (blockReason.Trim().Length is < 1 or > 120) return "Informe o motivo do bloqueio, de 1 a 120 caracteres (por exemplo, feriado ou folga).";
        if (!DisplayFormat.TryParseDate(blockFrom, out var from)) return "Informe a data inicial no formato dd/mm/aaaa.";
        var last = blockTo.Trim().Length == 0 ? from : DisplayFormat.TryParseDate(blockTo, out var parsed) ? parsed : default;
        if (last == default) return "Informe a data final no formato dd/mm/aaaa ou deixe em branco para um único dia.";
        if (last < from) return "A data final não pode ser anterior à inicial.";
        if (blockFullDay) { startsAt = new DateTimeOffset(from.Date); endsAt = new DateTimeOffset(last.Date.AddDays(1)); return null; }
        if (!TimeSpan.TryParseExact(blockStartTime.Trim(), @"hh\:mm", CultureInfo.InvariantCulture, out var start) || !TimeSpan.TryParseExact(blockEndTime.Trim(), @"hh\:mm", CultureInfo.InvariantCulture, out var end)) return "Informe os horários no formato HH:mm.";
        startsAt = new DateTimeOffset(from.Date.Add(start)); endsAt = new DateTimeOffset(last.Date.Add(end));
        return endsAt <= startsAt ? "O fim do bloqueio precisa ser depois do início." : null;
    }

    public void NewBlockDraft()
    {
        blockSubmitted = false; blockServerError = ""; blockScope = Guid.Empty; blockFullDay = true; blockFrom = blockTo = blockReason = ""; blockStartTime = "08:00"; blockEndTime = "12:00";
        foreach (var name in new[] { nameof(BlockScope), nameof(BlockFullDay), nameof(BlockTimesEnabled), nameof(BlockFromText), nameof(BlockToText), nameof(BlockReason), nameof(BlockStartTime), nameof(BlockEndTime), nameof(BlockError) }) Notify(name);
    }

    public Task SaveBlockAsync() => RunAsync(async () =>
    {
        var unit = ActiveObsUnit ?? throw new NoraApiException("Escolha uma UBS antes de criar o bloqueio.");
        RequireAdministrator();
        blockSubmitted = true; blockServerError = "";
        var problem = BlockProblem(out var startsAt, out var endsAt); Notify(nameof(BlockError));
        if (problem is not null) throw new NoraApiException(problem);
        await api.CreateScheduleBlockAsync(unit.Id, blockScope == Guid.Empty ? null : blockScope, startsAt, endsAt, blockReason.Trim());
        formSaved = true;
        NewBlockDraft();
        await ReloadCatalogAfterChangeAsync(unit, UnitScope.Setup, "Bloqueio criado. Os horários que ele cruza saem da disponibilidade.");
    });

    /// <summary>Grava o bloqueio do pop-up e diz se deu certo.</summary>
    public async Task<bool> SaveBlockFormAsync() { formSaved = false; await SaveBlockAsync(); return formSaved; }

    /// <summary>Remove bloqueios (um ou vários, com uma só leitura depois); os horários que eles cobriam voltam a ser oferecidos.</summary>
    public Task DeleteBlocksAsync(IReadOnlyList<Guid> blockIds) => RunAsync(async () =>
    {
        var unit = ActiveObsUnit ?? throw new NoraApiException("Escolha uma UBS antes de remover o bloqueio.");
        RequireAdministrator();
        if (blockIds.Count == 0) throw new NoraApiException("Selecione um bloqueio.");
        await ChangeEachAsync(unit, blockIds, id => api.DeleteScheduleBlockAsync(unit.Id, id), UnitScope.Setup, blockIds.Count == 1 ? "Bloqueio removido." : $"{blockIds.Count} bloqueios removidos.");
    });

    // ---- Fichas (segundo clique e Enter) ----

    /// <summary>Ficha em linhas de um item das listagens de Informações da UBS; nulo para outro tipo de item.</summary>
    public ItemDetails? BuildCatalogDetails(object item) => item switch
    {
        Professional professional => new(professional.FullName, "Profissional da UBS",
        [
            new("Nome completo", professional.FullName, true, "Identificação"),
            new("Especialidades", professional.SpecialtiesDisplay.Length > 0 ? professional.SpecialtiesDisplay : "Nenhuma", Group: "Identificação"),
            new("Registro no conselho", professional.CouncilDisplay, !string.IsNullOrWhiteSpace(professional.CouncilAcronym), "Identificação"),
            new("Situação", professional.StatusDisplay, Group: "Identificação")
        ]),
        Room room => new(room.Name, "Consultório da UBS", [new("Nome", room.Name, true, "Identificação"), new("Situação", room.StatusDisplay, Group: "Identificação")]),
        ScheduleTemplate row => new($"{row.WeekdayDisplay} · {row.RangeDisplay}", "Horário da grade semanal",
        [
            new("Dia", row.WeekdayDisplay, Group: "Horário"), new("Horário", row.RangeDisplay, Group: "Horário"), new("Duração do atendimento", row.SlotDisplay, Group: "Horário"),
            new("Profissional", row.ProfessionalName, Group: "Atendimento"), new("Especialidade", row.SpecialtyName, Group: "Atendimento"), new("Consultório", row.RoomName, Group: "Atendimento"),
            new("Vigência", row.ValidityDisplay, Group: "Vigência")
        ]),
        ScheduleBlock block => new(block.Reason, "Bloqueio da agenda",
        [
            new("Motivo", block.Reason, true, "Bloqueio"), new("Vale para", block.ScopeDisplay, Group: "Bloqueio"), new("Período", block.PeriodDisplay, Group: "Bloqueio")
        ]),
        _ => null
    };

    /// <summary>Depois de uma alteração do catálogo: relê o que mudou e, se o agendamento tem especialidade e profissional escolhidos, busca os horários de novo (a grade ou os bloqueios podem ter mudado).</summary>
    private async Task ReloadCatalogAfterChangeAsync(HealthUnit unit, UnitScope scope, string success)
    {
        await ReloadAfterChangeAsync(unit, scope, success);
        if (!disposed && ActiveObsUnit?.Id == unit.Id) ReloadBookingSlots();
    }

    private void RequireAdministrator()
    {
        if (!IsAdministrator) throw new NoraApiException("Somente o administrador da UBS altera profissionais, consultórios, grade e bloqueios.");
    }

    private void ClearCatalog()
    {
        catalogLoaded = catalogFailed = setupLoaded = setupFailed = false; catalogAttemptedAt = null;
        ObsSpecialties.Clear(); Professionals.Clear(); Rooms.Clear(); ScheduleRows.Clear(); ScheduleDayRows.Clear(); ScheduleBlocks.Clear(); ProfessionalSpecialtyChoices.Clear(); ActiveProfessionalOptions.Clear(); ActiveRoomOptions.Clear(); ScheduleSpecialtyOptions.Clear();
        Replace(BlockScopeOptions, new[] { new GuidChoice(Guid.Empty, "UBS inteira") });
        SelectedProfessional = null; SelectedRoom = null; SelectedSchedule = null;
        NewProfessionalDraft(); NewRoomDraft(); NewScheduleDraft(); NewBlockDraft(); ClearBooking();
        NotifyCatalog();
    }
}
