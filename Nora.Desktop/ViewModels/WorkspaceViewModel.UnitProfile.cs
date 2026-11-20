using System.Collections.ObjectModel;
using Nora.Desktop.Generics;
using Nora.Desktop.Models;
using Nora.Desktop.Services;

namespace Nora.Desktop.ViewModels;

/// <summary>Um dia da semana no editor do horário de funcionamento: "Fechado" ou até dois intervalos (manhã e tarde) no formato HH:mm.</summary>
public sealed class OpeningDayEditor(int weekday, Action changed) : ObservableObject
{
    private bool closed = true;
    private string opens1 = "", closes1 = "", opens2 = "", closes2 = "", error = "";
    public int Weekday { get; } = weekday;
    public string Label => ScheduleTemplate.WeekdayName(Weekday);
    public bool IsClosed { get => closed; set { if (closed == value) return; closed = value; error = ""; Touched(nameof(IsClosed)); Notify(nameof(HasIntervals)); Notify(nameof(Error)); } }
    public bool HasIntervals => !closed;
    public string Opens1 { get => opens1; set { opens1 = value ?? ""; Touched(nameof(Opens1)); } }
    public string Closes1 { get => closes1; set { closes1 = value ?? ""; Touched(nameof(Closes1)); } }
    public string Opens2 { get => opens2; set { opens2 = value ?? ""; Touched(nameof(Opens2)); } }
    public string Closes2 { get => closes2; set { closes2 = value ?? ""; Touched(nameof(Closes2)); } }
    public string Error { get => error; private set { error = value; Notify(); } }
    private void Touched(string property) { Notify(property); if (error.Length > 0) { error = ""; Notify(nameof(Error)); } changed(); }

    /// <summary>Preenche o editor pelo que a API devolveu (sem marcar como editado).</summary>
    public void Load(IEnumerable<OpeningInterval> intervals)
    {
        var ordered = intervals.OrderBy(item => item.Opens, StringComparer.Ordinal).Take(2).ToArray();
        closed = ordered.Length == 0;
        opens1 = ordered.Length > 0 ? ordered[0].Opens : ""; closes1 = ordered.Length > 0 ? ordered[0].Closes : "";
        opens2 = ordered.Length > 1 ? ordered[1].Opens : ""; closes2 = ordered.Length > 1 ? ordered[1].Closes : "";
        error = "";
        foreach (var name in new[] { nameof(IsClosed), nameof(HasIntervals), nameof(Opens1), nameof(Closes1), nameof(Opens2), nameof(Closes2), nameof(Error) }) Notify(name);
    }

    /// <summary>Confere o dia como a API confere: até dois intervalos, "HH:mm", fim depois do início, sem sobreposição (encostar pode). Devolve os intervalos ou a mensagem do campo.</summary>
    public (OpeningInterval[] Intervals, string Error) Read()
    {
        if (closed) { Error = ""; return ([], ""); }
        var list = new List<(TimeOnly Opens, TimeOnly Closes)>();
        foreach (var (opens, closes, label) in new[] { (opens1, closes1, "primeiro"), (opens2, closes2, "segundo") })
        {
            if (opens.Trim().Length == 0 && closes.Trim().Length == 0) continue;
            if (!OpeningHoursText.TryParseTime(opens, out var start) || !OpeningHoursText.TryParseTime(closes, out var end))
            { Error = $"{Label}: informe o {label} intervalo como Abre e Fecha em HH:mm (por exemplo 07:30)."; return ([], Error); }
            if (end <= start) { Error = $"{Label}: o horário de fechar precisa ser depois do de abrir."; return ([], Error); }
            list.Add((start, end));
        }
        if (list.Count == 0) { Error = $"{Label}: informe ao menos um intervalo ou marque Fechado."; return ([], Error); }
        list.Sort((left, right) => left.Opens.CompareTo(right.Opens));
        if (list.Count == 2 && list[1].Opens < list[0].Closes) { Error = $"{Label}: os dois intervalos não podem se sobrepor."; return ([], Error); }
        Error = "";
        return ([.. list.Select(item => new OpeningInterval(Weekday, item.Opens.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture), item.Closes.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture)))], "");
    }
}

/// <summary>
/// Atendimento ao público da UBS (OBS, Configurações): horário de funcionamento por dia da semana, texto livre de serviços e as especialidades que vêm da grade (só leitura).
/// O administrador da UBS edita e salva tudo de uma vez; a equipe vê em leitura. O aplicativo do paciente mostra este conteúdo por UBS vinculada.
/// </summary>
public sealed partial class WorkspaceViewModel
{
    public const int ServicesMaxLength = 1000;
    private UnitProfile? unitProfile;
    private bool profileLoaded, profileFailed, profileDirty, forceProfileEditors;
    private string unitServices = "", servicesError = "";

    public ObservableCollection<OpeningDayEditor> OpeningDays { get; } = [];
    public ObservableCollection<ProgressStep> ProfileSteps { get; } = [];
    /// <summary>Especialidades que têm horário na grade (lidas da API; só para consulta).</summary>
    public ObservableCollection<string> ProfileSpecialties { get; } = [];

    public bool CanEditProfile => CanEditCatalog;
    public bool CanSaveProfile => CanEditProfile && profileLoaded && !profileFailed;
    public bool ShowProfileReadOnlyNotice => IsAuthenticated && IsOperational && !IsAdministrator;
    public string ProfileReadOnlyNotice => "Só o administrador da UBS altera o atendimento ao público. Esta conta vê os horários e os serviços em leitura.";
    public string UnitServices
    {
        get => unitServices;
        set { unitServices = value ?? ""; servicesError = ""; profileDirty = true; Notify(); Notify(nameof(ServicesCounter)); Notify(nameof(ServicesError)); }
    }
    public string ServicesCounter => $"{unitServices.Length}/{ServicesMaxLength}";
    public string ServicesError => servicesError;
    public string ProfileSpecialtiesText => !profileLoaded ? "" : ProfileSpecialties.Count == 0 ? "Nenhuma especialidade com horário na grade ainda." : string.Join(", ", ProfileSpecialties);
    public string ProfileSpecialtiesHint => "Vem da grade; para mudar, edite a grade.";
    public string ProfileState => !HasActiveObsUnit ? "Escolha uma UBS no Painel da UBS para ver o atendimento ao público."
        : profileFailed && !profileLoaded ? "Não foi possível ler o atendimento da UBS agora. Use Atualizar configurações para tentar de novo."
        : profileFailed ? "Dados da leitura anterior. Use Atualizar configurações para tentar de novo."
        : !profileLoaded ? "Lendo o atendimento da UBS…"
        : unitProfile is { Informed: false } ? "Ainda não informado. O aplicativo mostra \"Horário não informado\"." : "";
    public string ProfileUpdatedLabel => unitProfile is { UpdatedAt: { } at } ? $"Última alteração em {at.ToLocalTime():dd/MM/yyyy HH:mm}." : "";

    private static readonly string[] ProfileNames =
    [
        nameof(CanEditProfile), nameof(CanSaveProfile), nameof(ShowProfileReadOnlyNotice), nameof(ProfileSpecialtiesText), nameof(ProfileState), nameof(ProfileUpdatedLabel),
        nameof(SlotApprovalEnabled), nameof(SlotApprovalKnown), nameof(SlotApprovalState), nameof(SlotApprovalToggleLabel), nameof(CanToggleSlotApproval), nameof(ShowAwaitingSlots), nameof(AwaitingSlotsTitle), nameof(AwaitingSlotsState), nameof(HasAwaitingSlots), nameof(PanelAwaitingText)
    ];
    private void NotifyProfile() { foreach (var name in ProfileNames) Notify(name); }

    private void EnsureOpeningDays()
    {
        if (OpeningDays.Count == 7) return;
        OpeningDays.Clear();
        foreach (var day in Enumerable.Range(0, 7)) OpeningDays.Add(new OpeningDayEditor(day, () => profileDirty = true));
    }

    /// <summary>Aplica a leitura. Uma edição em andamento não é sobrescrita pelo ciclo automático; só o botão Atualizar (ou o salvar) refaz os campos.</summary>
    private void ApplyProfile(Tolerant<UnitProfile> read)
    {
        profileFailed = read.Failed;
        if (read.Value is { } value)
        {
            unitProfile = value; profileLoaded = true;
            Replace(ProfileSpecialties, value.Specialties.Select(item => item.Name).OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase).ToArray());
            if (forceProfileEditors || !profileDirty) FillProfileEditors(value);
        }
        NotifyProfile();
    }

    private void FillProfileEditors(UnitProfile value)
    {
        EnsureOpeningDays();
        foreach (var day in OpeningDays) day.Load(value.OpeningHours.Where(item => item.Weekday == day.Weekday));
        unitServices = value.Services ?? ""; servicesError = "";
        profileDirty = false;
        Notify(nameof(UnitServices)); Notify(nameof(ServicesCounter)); Notify(nameof(ServicesError));
    }

    /// <summary>Confere o texto de serviços como a API: até 1.000 caracteres, quebras de linha permitidas, outros caracteres de controle não.</summary>
    private static (string? Services, string Error) ReadServices(string text)
    {
        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n').Trim();
        if (normalized.Length > ServicesMaxLength) return (null, $"Os serviços podem ter até {DisplayFormat.Count(ServicesMaxLength)} caracteres; hoje há {DisplayFormat.Count(normalized.Length)}.");
        if (normalized.Any(character => char.IsControl(character) && character != '\n')) return (null, "O texto dos serviços tem um caractere de controle (como Tab) que não pode ser salvo. Remova-o.");
        return (normalized.Length == 0 ? null : normalized, "");
    }

    /// <summary>Salva o atendimento ao público (substitui tudo numa operação). A conferência local aponta o campo e não envia nada se algo estiver errado.</summary>
    public Task SaveProfileAsync() => RunAsync(async () =>
    {
        var unit = ActiveObsUnit ?? throw new NoraApiException("Escolha uma UBS no Painel da UBS antes de salvar o atendimento ao público.");
        if (!IsAdministrator) throw new NoraApiException("Somente o administrador da UBS altera o atendimento ao público.");
        if (!profileLoaded || profileFailed) throw new NoraApiException("Leia o atendimento da UBS (Atualizar configurações) antes de salvar, para não apagar o que já está gravado.");
        EnsureOpeningDays();
        var intervals = new List<OpeningInterval>();
        var invalid = 0;
        foreach (var day in OpeningDays)
        {
            var (list, error) = day.Read();
            if (error.Length > 0) invalid++; else intervals.AddRange(list);
        }
        var (services, serviceError) = ReadServices(unitServices);
        servicesError = serviceError; Notify(nameof(ServicesError));
        if (invalid > 0 || serviceError.Length > 0)
            throw new NoraApiException(invalid > 0 ? "Corrija os horários marcados e salve de novo; nada foi enviado." : "Corrija o texto dos serviços e salve de novo; nada foi enviado.");
        UnitProfile? saved = null;
        await WithStepAsync(ProfileSteps, "Salvando horários…", async () => saved = await api.SaveUnitProfileAsync(unit.Id, new UnitProfileDraft([.. intervals], services)));
        if (disposed || ActiveObsUnit?.Id != unit.Id) return;
        // A resposta traz o que ficou gravado; a aprovação das vagas não é tocada por este PUT.
        unitProfile = saved! with { SlotApprovalRequired = unitProfile?.SlotApprovalRequired ?? false, SlotApprovalSince = unitProfile?.SlotApprovalSince };
        profileLoaded = true; profileFailed = false;
        Replace(ProfileSpecialties, saved!.Specialties.Select(item => item.Name).OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase).ToArray());
        FillProfileEditors(unitProfile);
        NotifyProfile();
        Say(MessageKind.Success, "Atendimento ao público salvo.");
    });

    private void ClearUnitProfile()
    {
        unitProfile = null; profileLoaded = profileFailed = profileDirty = forceProfileEditors = false;
        EnsureOpeningDays();
        foreach (var day in OpeningDays) day.Load([]);
        ProfileSpecialties.Clear(); ProfileSteps.Clear(); SlotApprovalSteps.Clear();
        unitServices = ""; servicesError = "";
        Notify(nameof(UnitServices)); Notify(nameof(ServicesCounter)); Notify(nameof(ServicesError));
        NotifyProfile();
    }
}
