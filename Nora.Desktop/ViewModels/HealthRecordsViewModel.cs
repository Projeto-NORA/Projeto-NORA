using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using Nora.Desktop.Generics;
using Nora.Desktop.Models;
using Nora.Desktop.Services;

namespace Nora.Desktop.ViewModels;

/// <summary>
/// Aba "Exames e vacinas" da ficha do paciente (OBS): lista a Jornada do paciente na UBS e permite registrar exame (solicitado), marcar como realizado ou cancelado,
/// registrar vacina com dose e data e anular com motivo. Cada operação lê de novo o que ficou gravado. As validações repetem as do servidor (docs/api.md, "Jornada").
/// A ficha é uma janela própria, então o retorno das ações aparece dentro dela (não na faixa da janela principal).
/// </summary>
public sealed class HealthRecordsViewModel : ObservableObject
{
    public const int NameMax = 160, NoteMax = 280, ReasonMin = 3;
    private static readonly DateTime EarliestDate = new(1900, 1, 1);
    private readonly NoraApiClient api;
    private readonly Guid unitId;
    private readonly string cpf;
    private readonly Func<NoraApiException, bool>? onApiError;
    private bool includeVoided, busy, loading, loaded, failed, submittedExam, submittedVaccine, submittedVoid, submittedDone;
    /// <summary>Filtro "Mostrar anulados" com que a lista na tela foi lida: depois de uma leitura falha ele pode ser diferente do marcado.</summary>
    private bool loadedWithVoided;
    private int loadVersion;
    private CancellationTokenSource? loadCts;
    private string message = "", examName = "", examNote = "", examDoneDate = "", vaccineName = "", vaccineDate = "", vaccineNote = "", voidReason = "";
    private MessageKind messageKind = MessageKind.Info;
    private VaccineDoseOption? vaccineDose;
    private ExamInfo? selectedExam;
    private VaccineInfo? selectedVaccine;

    public HealthRecordsViewModel(NoraApiClient api, Guid unitId, string cpf, string patientName, bool canRegister, Func<NoraApiException, bool>? onApiError = null)
    {
        this.api = api; this.unitId = unitId; this.cpf = cpf; PatientName = patientName; CanRegister = canRegister; this.onApiError = onApiError;
        vaccineDose = VaccineDose.Options[0];
    }

    public string PatientName { get; }
    /// <summary>Cadastro ativo na UBS: só então a API aceita registros novos (cadastro desativado = só consulta).</summary>
    public bool CanRegister { get; }
    public ObservableCollection<ExamInfo> Exams { get; } = [];
    public ObservableCollection<VaccineInfo> Vaccines { get; } = [];
    public static IReadOnlyList<VaccineDoseOption> DoseOptions => VaccineDose.Options;
    public bool IsLoaded => loaded;
    public bool IsBusy => busy || loading;
    private void SetBusy(bool value) { busy = value; NotifyBusy(); }
    private void SetLoading(bool value) { loading = value; NotifyBusy(); }
    public bool CanRefresh => !IsBusy;
    private void NotifyBusy() { Notify(nameof(IsBusy)); Notify(nameof(CanRefresh)); foreach (var name in ActionNames) Notify(name); }
    private static readonly string[] ActionNames = [nameof(CanCreateExam), nameof(CanCreateVaccine), nameof(CanMarkExamDone), nameof(CanCancelExam), nameof(CanVoid)];

    public string Message => message;
    public bool HasMessage => message.Length > 0;
    public MessageKind MessageKind => messageKind;
    public string MessageKindName => messageKind switch { MessageKind.Success => "success", MessageKind.Error => "error", _ => "info" };
    public string MessageKindLabel => messageKind switch { MessageKind.Success => "Sucesso", MessageKind.Error => "Erro", _ => "Informação" };
    public string MessageIcon => messageKind switch { MessageKind.Success => "", MessageKind.Error => "", _ => "" };
    private void Say(MessageKind kind, string text)
    {
        message = text; messageKind = kind;
        foreach (var name in new[] { nameof(Message), nameof(HasMessage), nameof(MessageKind), nameof(MessageKindName), nameof(MessageKindLabel), nameof(MessageIcon) }) Notify(name);
    }

    public string RegisterNotice => CanRegister ? "" : "O cadastro deste paciente está desativado nesta UBS: a Jornada só pode ser consultada. Reative o cadastro para registrar exames e vacinas.";
    public bool HasRegisterNotice => !CanRegister;
    public string ListState => !loaded ? (failed ? "Não foi possível ler a Jornada agora. Use Atualizar para tentar de novo." : "Carregando a Jornada…")
        : failed && loadedWithVoided != includeVoided ? $"A leitura com o filtro novo não concluiu: a lista abaixo ainda é a da leitura anterior, que {(loadedWithVoided ? "inclui os anulados" : "não inclui os anulados")}. Use Atualizar para tentar de novo."
        : Exams.Count + Vaccines.Count == 0 ? (loadedWithVoided ? "Nenhum exame ou vacina registrado, nem anulado." : "Nenhum exame ou vacina registrado. Os registros anulados ficam ocultos: marque \"Mostrar anulados\" para vê-los.") : "";
    public string ExamsState => loaded && Exams.Count == 0 ? "Nenhum exame." : "";
    public string VaccinesState => loaded && Vaccines.Count == 0 ? "Nenhuma vacina." : "";

    public bool IncludeVoided
    {
        get => includeVoided;
        set { if (includeVoided == value) return; includeVoided = value; Notify(); _ = ReloadAsync(); }
    }

    // ---- Leitura ----

    /// <summary>Primeira leitura (ao abrir a aba); as seguintes vêm do botão Atualizar, do "Mostrar anulados" e de cada ação.</summary>
    public Task EnsureLoadedAsync() => loaded || loadCts is not null ? Task.CompletedTask : ReloadAsync();

    /// <summary>Uma requisição por leitura: a anterior é cancelada e só a mais nova altera a tela.</summary>
    public async Task ReloadAsync()
    {
        var cancellation = new CancellationTokenSource();
        Interlocked.Exchange(ref loadCts, cancellation)?.Cancel();
        var version = ++loadVersion;
        SetLoading(true);
        try
        {
            var withVoided = includeVoided;
            var records = await api.HealthRecordsAsync(unitId, cpf, withVoided, cancellation.Token);
            if (version != loadVersion || cancellation.IsCancellationRequested) return;
            Apply(records, withVoided);
        }
        catch (OperationCanceledException) { return; }
        catch (NoraApiException error)
        {
            if (version != loadVersion) return;
            failed = true; Notify(nameof(ListState));
            Say(MessageKind.Error, error.Message); onApiError?.Invoke(error);
        }
        finally { if (version == loadVersion) SetLoading(false); }
    }

    private void Apply(HealthRecords records, bool withVoided)
    {
        loadedWithVoided = withVoided;
        var examId = selectedExam?.Id; var vaccineId = selectedVaccine?.Id;
        Exams.Clear(); foreach (var exam in records.Exams) Exams.Add(exam);
        Vaccines.Clear(); foreach (var vaccine in records.Vaccines) Vaccines.Add(vaccine);
        loaded = true; failed = false;
        SelectedExam = examId is null ? null : Exams.FirstOrDefault(item => item.Id == examId);
        SelectedVaccine = vaccineId is null ? null : Vaccines.FirstOrDefault(item => item.Id == vaccineId);
        Notify(nameof(IsLoaded)); Notify(nameof(ListState)); Notify(nameof(ExamsState)); Notify(nameof(VaccinesState));
    }

    // ---- Seleção ----

    public ExamInfo? SelectedExam
    {
        get => selectedExam;
        set
        {
            selectedExam = value; if (value is not null && selectedVaccine is not null) { selectedVaccine = null; Notify(nameof(SelectedVaccine)); }
            Notify(); NotifySelection();
        }
    }
    public VaccineInfo? SelectedVaccine
    {
        get => selectedVaccine;
        set
        {
            selectedVaccine = value; if (value is not null && selectedExam is not null) { selectedExam = null; Notify(nameof(SelectedExam)); }
            Notify(); NotifySelection();
        }
    }
    private void NotifySelection() { Notify(nameof(SelectedRecordLabel)); Notify(nameof(SelectedRecordName)); foreach (var name in ActionNames) Notify(name); }
    public string SelectedRecordLabel => selectedExam is { } exam ? $"Exame selecionado: {exam.Name} ({exam.StatusDisplay})"
        : selectedVaccine is { } vaccine ? $"Vacina selecionada: {vaccine.Name}, {vaccine.DoseDisplay} ({vaccine.StatusDisplay})" : "Nenhum registro selecionado.";

    // ---- Exames ----

    public string ExamName { get => examName; set { examName = value ?? ""; Notify(); Notify(nameof(ExamNameError)); } }
    public string ExamNote { get => examNote; set { examNote = value ?? ""; Notify(); Notify(nameof(ExamNoteError)); } }
    public string ExamDoneDate { get => examDoneDate; set { examDoneDate = value ?? ""; submittedDone = false; Notify(); Notify(nameof(ExamDoneError)); } }
    public string ExamNameError => LineProblem(examName, 1, NameMax, "o nome do exame", submittedExam);
    public string ExamNoteError => OptionalProblem(examNote, NoteMax, "A observação");
    public string ExamDoneError => submittedDone ? DateProblem(examDoneDate, "a data de realização") ?? "" : "";
    public bool CanCreateExam => CanRegister && !IsBusy;
    public bool CanMarkExamDone => CanRegister && !IsBusy && selectedExam is { CanChangeStatus: true };
    public bool CanCancelExam => CanRegister && !IsBusy && selectedExam is { CanChangeStatus: true };

    public Task CreateExamAsync() => RunAsync(async () =>
    {
        if (!CanRegister) throw new NoraApiException("O cadastro deste paciente está desativado nesta UBS: não é possível registrar exames.");
        submittedExam = true; Notify(nameof(ExamNameError)); Notify(nameof(ExamNoteError));
        if (ExamNameError.Length + ExamNoteError.Length > 0) throw new NoraApiException("Corrija os campos destacados do exame.");
        var name = examName.Trim();
        var created = await api.CreateExamAsync(unitId, cpf, name, NullIfBlank(examNote));
        examName = examNote = ""; submittedExam = false;
        foreach (var property in new[] { nameof(ExamName), nameof(ExamNote), nameof(ExamNameError), nameof(ExamNoteError) }) Notify(property);
        await ReloadAfterChangeAsync($"Exame \"{created.Name}\" registrado como solicitado.");
        SelectedExam = Exams.FirstOrDefault(item => item.Id == created.Id);
    });

    public Task MarkExamDoneAsync() => RunAsync(async () =>
    {
        var exam = selectedExam ?? throw new NoraApiException("Selecione um exame solicitado.");
        if (!exam.CanChangeStatus) throw new NoraApiException("Só um exame solicitado pode ser marcado como realizado.");
        submittedDone = true; Notify(nameof(ExamDoneError));
        if (DateProblem(examDoneDate, "a data de realização") is { } problem) throw new NoraApiException(problem);
        DisplayFormat.TryParseDate(examDoneDate, out var date);
        await api.SetExamStatusAsync(unitId, exam.Id, "done", date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        examDoneDate = ""; submittedDone = false; Notify(nameof(ExamDoneDate)); Notify(nameof(ExamDoneError));
        await ReloadAfterChangeAsync($"Exame \"{exam.Name}\" marcado como realizado em {DisplayFormat.Date(DateOnly.FromDateTime(date))}.");
    });

    public Task CancelExamAsync() => RunAsync(async () =>
    {
        var exam = selectedExam ?? throw new NoraApiException("Selecione um exame solicitado.");
        if (!exam.CanChangeStatus) throw new NoraApiException("Só um exame solicitado pode ser cancelado.");
        await api.SetExamStatusAsync(unitId, exam.Id, "cancelled", null);
        await ReloadAfterChangeAsync($"Exame \"{exam.Name}\" cancelado.");
    });

    // ---- Vacinas ----

    public string VaccineName { get => vaccineName; set { vaccineName = value ?? ""; Notify(); Notify(nameof(VaccineNameError)); } }
    public string VaccineDate { get => vaccineDate; set { vaccineDate = value ?? ""; Notify(); Notify(nameof(VaccineDateError)); } }
    public string VaccineNote { get => vaccineNote; set { vaccineNote = value ?? ""; Notify(); Notify(nameof(VaccineNoteError)); } }
    public VaccineDoseOption? VaccineDoseChoice { get => vaccineDose; set { vaccineDose = value; Notify(); Notify(nameof(VaccineDoseError)); } }
    public string VaccineNameError => LineProblem(vaccineName, 1, NameMax, "o nome da vacina", submittedVaccine);
    public string VaccineDateError => submittedVaccine || vaccineDate.Length > 0 ? DateProblem(vaccineDate, "a data de aplicação") ?? "" : "";
    public string VaccineNoteError => OptionalProblem(vaccineNote, NoteMax, "A observação");
    public string VaccineDoseError => submittedVaccine && vaccineDose is null ? "Escolha a dose." : "";
    public bool CanCreateVaccine => CanRegister && !IsBusy;

    public Task CreateVaccineAsync() => RunAsync(async () =>
    {
        if (!CanRegister) throw new NoraApiException("O cadastro deste paciente está desativado nesta UBS: não é possível registrar vacinas.");
        submittedVaccine = true;
        foreach (var property in new[] { nameof(VaccineNameError), nameof(VaccineDateError), nameof(VaccineNoteError), nameof(VaccineDoseError) }) Notify(property);
        if (VaccineNameError.Length + VaccineDateError.Length + VaccineNoteError.Length + VaccineDoseError.Length > 0) throw new NoraApiException("Corrija os campos destacados da vacina.");
        DisplayFormat.TryParseDate(vaccineDate, out var date);
        var created = await api.CreateVaccineAsync(unitId, cpf, vaccineName.Trim(), vaccineDose!.Value, date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), NullIfBlank(vaccineNote));
        vaccineName = vaccineDate = vaccineNote = ""; submittedVaccine = false; vaccineDose = VaccineDose.Options[0];
        foreach (var property in new[] { nameof(VaccineName), nameof(VaccineDate), nameof(VaccineNote), nameof(VaccineDoseChoice), nameof(VaccineNameError), nameof(VaccineDateError), nameof(VaccineNoteError), nameof(VaccineDoseError) }) Notify(property);
        await ReloadAfterChangeAsync($"Vacina \"{created.Name}\" ({created.DoseDisplay}) registrada em {DisplayFormat.Date(created.AppliedOn)}.");
        SelectedVaccine = Vaccines.FirstOrDefault(item => item.Id == created.Id);
    });

    // ---- Anulação ----

    public string VoidReason { get => voidReason; set { voidReason = value ?? ""; submittedVoid = false; Notify(); Notify(nameof(VoidReasonError)); } }
    public string VoidReasonError => submittedVoid || voidReason.Length > 0 ? LineProblem(voidReason, ReasonMin, NoteMax, "o motivo da anulação", true) : "";
    public bool CanVoid => CanRegister && !IsBusy && (selectedExam is { IsVoided: false } || selectedVaccine is { IsVoided: false });
    /// <summary>Nome do registro que será anulado, para a pergunta de confirmação.</summary>
    public string? SelectedRecordName => selectedExam is { IsVoided: false } exam ? $"o exame \"{exam.Name}\"" : selectedVaccine is { IsVoided: false } vaccine ? $"a vacina \"{vaccine.Name}\" ({vaccine.DoseDisplay})" : null;

    /// <summary>Marca o motivo como enviado (mostra o erro, se houver) e diz se já pode anular; a tela só pergunta a confirmação quando é verdadeiro.</summary>
    public bool IsVoidReasonValid()
    {
        submittedVoid = true; Notify(nameof(VoidReasonError));
        return VoidReasonError.Length == 0;
    }

    public Task VoidSelectedAsync() => RunAsync(async () =>
    {
        if (selectedExam is null && selectedVaccine is null) throw new NoraApiException("Selecione o exame ou a vacina que será anulado.");
        if (selectedExam is { IsVoided: true } || selectedVaccine is { IsVoided: true }) throw new NoraApiException("Este registro já foi anulado.");
        submittedVoid = true; Notify(nameof(VoidReasonError));
        if (VoidReasonError.Length > 0) throw new NoraApiException(VoidReasonError);
        var reason = voidReason.Trim();
        string what;
        if (selectedExam is { } exam) { await api.VoidExamAsync(unitId, exam.Id, reason); what = $"Exame \"{exam.Name}\""; }
        else { var vaccine = selectedVaccine!; await api.VoidVaccineAsync(unitId, vaccine.Id, reason); what = $"Vacina \"{vaccine.Name}\""; }
        voidReason = ""; submittedVoid = false; Notify(nameof(VoidReason)); Notify(nameof(VoidReasonError));
        await ReloadAfterChangeAsync($"{what} anulado, com o motivo registrado. O registro permanece no histórico.");
    });

    // ---- Apoio ----

    /// <summary>Uma operação de escrita por vez; a falha vira mensagem dentro da ficha e, se a sessão acabou, avisa a janela principal.</summary>
    private async Task RunAsync(Func<Task> operation)
    {
        if (IsBusy) return;
        SetBusy(true);
        Say(MessageKind.Info, "");
        var reload = false;
        try { await operation(); }
        catch (NoraApiException error)
        {
            Say(MessageKind.Error, error.Message); onApiError?.Invoke(error);
            // Registro alterado ou anulado por outra pessoa (409/404): a lista é lida de novo para mostrar o que ficou valendo.
            reload = error.Status is 409 or 404 && !error.Unauthorized;
        }
        finally { SetBusy(false); }
        if (reload) await ReloadSilentlyAsync();
    }

    private async Task ReloadSilentlyAsync()
    {
        try { await ReloadAsync(); }
        catch (OperationCanceledException) { }
    }

    /// <summary>Depois de uma escrita confirmada pela API a lista é lida de novo; se a leitura falhar, a escrita continua confirmada e a ficha avisa.</summary>
    private async Task ReloadAfterChangeAsync(string success)
    {
        var cancellation = new CancellationTokenSource();
        Interlocked.Exchange(ref loadCts, cancellation)?.Cancel();
        var version = ++loadVersion;
        // Quem cancela a leitura anterior assume o indicador de leitura: a anterior (por exemplo, a do "Mostrar anulados" marcado durante a gravação)
        // sai sem desligá-lo por não ser mais a mais nova, e sem isto a ficha ficaria ocupada para sempre.
        SetLoading(true);
        try
        {
            var withVoided = includeVoided;
            Apply(await api.HealthRecordsAsync(unitId, cpf, withVoided, cancellation.Token), withVoided);
            Say(MessageKind.Success, success);
        }
        catch (NoraApiException error) when (!error.Unauthorized)
        {
            failed = true; Notify(nameof(ListState));
            Say(MessageKind.Success, success + " Não foi possível atualizar a lista; use Atualizar.");
        }
        catch (OperationCanceledException) { }
        finally { if (version == loadVersion) SetLoading(false); }
    }

    private static string? NullIfBlank(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>Texto de uma linha como o servidor confere: aparado, de <paramref name="min"/> a <paramref name="max"/> caracteres, sem controle e codificável.</summary>
    private static string LineProblem(string value, int min, int max, string what, bool show)
    {
        var text = value.Trim();
        if (!show && text.Length == 0) return "";
        if (text.Length < min || text.Length > max) return min <= 1 ? $"Informe {what}, de 1 a {max} caracteres." : $"Informe {what}, de {min} a {max} caracteres.";
        if (text.Any(char.IsControl) || !Encodable(text)) return $"Há caracteres inválidos em {what}.";
        return "";
    }
    private static string OptionalProblem(string value, int max, string what)
    {
        var text = value.Trim();
        if (text.Length == 0) return "";
        if (text.Length > max) return $"{what} pode ter até {max} caracteres.";
        return text.Any(char.IsControl) || !Encodable(text) ? $"Há caracteres inválidos. {what} não aceita quebra de linha." : "";
    }
    private static bool Encodable(string text)
    {
        try { new UTF8Encoding(false, true).GetByteCount(text); return true; }
        catch (EncoderFallbackException) { return false; }
    }
    /// <summary>Data dd/mm/aaaa de 01/01/1900 até hoje (a API aceita no máximo o dia seguinte em UTC; uma data realmente futura não faz sentido para exame realizado ou vacina aplicada).</summary>
    private static string? DateProblem(string value, string what)
    {
        if (!DisplayFormat.TryParseDate(value, out var date)) return $"Informe {what} no formato dd/mm/aaaa.";
        return date < EarliestDate || date > DateTime.Today ? $"Informe {what} entre 01/01/1900 e hoje." : null;
    }
}
