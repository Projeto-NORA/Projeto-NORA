using System.Collections.ObjectModel;
using Nora.Desktop.Generics;
using Nora.Desktop.Models;
using Nora.Desktop.Services;

namespace Nora.Desktop.ViewModels;

/// <summary>
/// Um dia da semana no editor do horário de funcionamento: caixa marcada = aberto, com um único intervalo "abre às fecha" no formato HH:mm; desmarcada = fechado.
/// Um dia gravado com dois intervalos (de uma versão anterior) aparece como o primeiro início e o último fim, e a tela avisa que o intervalo do meio será desconsiderado ao salvar.
/// </summary>
public sealed class OpeningDayEditor(int weekday, Action changed) : ObservableObject
{
    private bool closed = true, hadGap;
    private string opens = "", closes = "", error = "";
    public int Weekday { get; } = weekday;
    public string Label => ScheduleTemplate.WeekdayName(Weekday);
    public bool IsClosed { get => closed; set { if (closed == value) return; closed = value; error = ""; Touched(nameof(IsClosed)); Notify(nameof(IsOpen)); Notify(nameof(HasIntervals)); Notify(nameof(Error)); } }
    /// <summary>A caixa de seleção do dia: marcada é aberto (o inverso de <see cref="IsClosed"/>).</summary>
    public bool IsOpen { get => !closed; set => IsClosed = !value; }
    public bool HasIntervals => !closed;
    public string Opens { get => opens; set { opens = value ?? ""; Touched(nameof(Opens)); } }
    public string Closes { get => closes; set { closes = value ?? ""; Touched(nameof(Closes)); } }
    public string Error { get => error; private set { error = value; Notify(); } }
    /// <summary>Linha discreta quando o dia foi gravado com dois intervalos; some quando o dia fica fechado.</summary>
    public string GapNotice => hadGap && !closed ? "Este dia tinha dois intervalos gravados. Ao salvar, vale um só, do primeiro horário de abrir ao último de fechar, e o intervalo do meio é desconsiderado." : "";
    private void Touched(string property) { Notify(property); Notify(nameof(GapNotice)); if (error.Length > 0) { error = ""; Notify(nameof(Error)); } changed(); }

    /// <summary>Preenche o editor pelo que a API devolveu (sem marcar como editado).</summary>
    public void Load(IEnumerable<OpeningInterval> intervals)
    {
        var ordered = intervals.OrderBy(item => item.Opens, StringComparer.Ordinal).ToArray();
        closed = ordered.Length == 0;
        opens = ordered.Length > 0 ? ordered[0].Opens : ""; closes = ordered.Length > 0 ? ordered.Max(item => item.Closes) ?? "" : "";
        hadGap = ordered.Length > 1;
        error = "";
        foreach (var name in new[] { nameof(IsClosed), nameof(IsOpen), nameof(HasIntervals), nameof(Opens), nameof(Closes), nameof(GapNotice), nameof(Error) }) Notify(name);
    }

    /// <summary>Confere o dia como a API confere: "HH:mm" nos dois campos e fechar depois de abrir. Devolve o intervalo (nenhum, se o dia está fechado) ou a mensagem do campo.</summary>
    public (OpeningInterval[] Intervals, string Error) Read()
    {
        if (closed) { Error = ""; return ([], ""); }
        if (opens.Trim().Length == 0 && closes.Trim().Length == 0) { Error = $"{Label}: informe o horário de abrir e o de fechar, ou desmarque o dia."; return ([], Error); }
        if (!OpeningHoursText.TryParseTime(opens, out var start) || !OpeningHoursText.TryParseTime(closes, out var end))
        { Error = $"{Label}: informe Abre e Fecha em HH:mm (por exemplo 07:30)."; return ([], Error); }
        if (end <= start) { Error = $"{Label}: o horário de fechar precisa ser depois do de abrir."; return ([], Error); }
        Error = "";
        return ([new OpeningInterval(Weekday, start.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture), end.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture))], "");
    }
}

/// <summary>
/// Atendimento ao público da UBS (OBS, Informações da UBS): horário de funcionamento por dia da semana (um intervalo por dia) e texto livre de serviços.
/// O administrador da UBS edita e salva tudo de uma vez; a equipe vê em leitura. O aplicativo do paciente mostra este conteúdo por UBS vinculada.
/// </summary>
public sealed partial class WorkspaceViewModel
{
    public const int ServicesMaxLength = 1000;
    private UnitProfile? unitProfile;
    private bool profileLoaded, profileFailed, profileDirty;
    private string unitServices = "", servicesError = "";

    public ObservableCollection<OpeningDayEditor> OpeningDays { get; } = [];
    public ObservableCollection<ProgressStep> ProfileSteps { get; } = [];

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
    /// <summary>Estado da leitura (sem UBS, carregando, falha); o aviso de horário ainda não informado é <see cref="ProfileNotInformedNotice"/>.</summary>
    public string ProfileState => !HasActiveObsUnit ? "Escolha uma UBS no Painel da UBS para ver o atendimento ao público."
        : profileFailed && !profileLoaded ? "Não foi possível ler o atendimento da UBS agora. A tela tenta de novo sozinha."
        : profileFailed ? "A última leitura não concluiu; os dados são da leitura anterior. A tela tenta de novo sozinha."
        : !profileLoaded ? "Lendo o atendimento da UBS…" : "";
    /// <summary>O aviso amarelo do alto da tela: enquanto o horário não foi informado, o aplicativo do paciente mostra "Horário não informado".</summary>
    public string ProfileNotInformedNotice => profileLoaded && unitProfile is { Informed: false } ? "Ainda não informado. O aplicativo mostra \"Horário não informado\"." : "";
    public string ProfileUpdatedLabel => unitProfile is { UpdatedAt: { } at } ? $"Última alteração em {at.ToLocalTime():dd/MM/yyyy HH:mm}." : "";

    private static readonly string[] ProfileNames =
    [
        nameof(CanEditProfile), nameof(CanSaveProfile), nameof(ShowProfileReadOnlyNotice), nameof(ProfileState), nameof(ProfileNotInformedNotice), nameof(ProfileUpdatedLabel)
    ];
    private void NotifyProfile() { foreach (var name in ProfileNames) Notify(name); }

    private void EnsureOpeningDays()
    {
        if (OpeningDays.Count == 7) return;
        OpeningDays.Clear();
        foreach (var day in Enumerable.Range(0, 7)) OpeningDays.Add(new OpeningDayEditor(day, () => profileDirty = true));
    }

    /// <summary>Aplica a leitura. Uma edição em andamento não é sobrescrita pelo ciclo automático; só o salvar refaz os campos.</summary>
    private void ApplyProfile(Tolerant<UnitProfile> read)
    {
        profileFailed = read.Failed;
        if (read.Value is { } value)
        {
            unitProfile = value; profileLoaded = true;
            if (!profileDirty) FillProfileEditors(value);
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
        if (!profileLoaded || profileFailed) throw new NoraApiException("Aguarde a leitura do atendimento da UBS antes de salvar, para não apagar o que já está gravado.");
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
        unitProfile = saved;
        profileLoaded = true; profileFailed = false;
        FillProfileEditors(unitProfile!);
        NotifyProfile();
        Say(MessageKind.Success, "Atendimento ao público salvo. O aplicativo passa a mostrar estes horários e serviços.");
    });

    private void ClearUnitProfile()
    {
        unitProfile = null; profileLoaded = profileFailed = profileDirty = false;
        EnsureOpeningDays();
        foreach (var day in OpeningDays) day.Load([]);
        ProfileSteps.Clear();
        unitServices = ""; servicesError = "";
        Notify(nameof(UnitServices)); Notify(nameof(ServicesCounter)); Notify(nameof(ServicesError));
        NotifyProfile();
    }
}
