using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using Nora.Desktop.Generics;
using Nora.Desktop.Models;
using Nora.Desktop.Services;

namespace Nora.Desktop.ViewModels;

/// <summary>O que a aba mostra: as duas listas, um dos formulários (no lugar das listas: a ficha já é um pop-up e só pode haver um por vez) ou a ficha em linhas de um registro.</summary>
public enum HealthPanel { Lists, NewExam, NewVaccine, ExamDone, Void, Detail }

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
    /// <summary>Pode reler agora (nenhuma gravação nem leitura em andamento): a releitura automática de 20 s só roda assim.</summary>
    public bool CanRefresh => !IsBusy;
    /// <summary>Um formulário ou uma ação do balão pode ser acionado: o cadastro é ativo e nada está em andamento.</summary>
    public bool CanSubmit => CanRegister && !IsBusy;
    private void NotifyBusy() { Notify(nameof(IsBusy)); Notify(nameof(CanRefresh)); foreach (var name in ActionNames) Notify(name); }
    private static readonly string[] ActionNames = [nameof(CanSubmit), nameof(CanCreateExam), nameof(CanCreateVaccine), nameof(CanMarkExamDone), nameof(CanCancelExam), nameof(CanVoid)];

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
    public string ListState => !loaded ? (failed ? "Não foi possível ler a Jornada agora. O NORA tenta de novo sozinho." : "Carregando a Jornada…")
        : failed && loadedWithVoided != includeVoided ? $"A leitura com o filtro novo não concluiu: a lista abaixo ainda é a da leitura anterior, que {(loadedWithVoided ? "inclui os anulados" : "não inclui os anulados")}. O NORA tenta de novo sozinho."
        : Exams.Count + Vaccines.Count == 0 ? (loadedWithVoided ? "Nenhum exame ou vacina registrado, nem anulado." : "Nenhum exame ou vacina registrado. Os registros anulados ficam ocultos: marque \"Mostrar anulados\" para vê-los.") : "";
    public string ExamsState => loaded && Exams.Count == 0 ? "Nenhum exame registrado." : "";
    public string VaccinesState => loaded && Vaccines.Count == 0 ? "Nenhuma vacina registrada." : "";

    public bool IncludeVoided
    {
        get => includeVoided;
        set { if (includeVoided == value) return; includeVoided = value; Notify(); _ = ReloadAsync(); }
    }

    // ---- Leitura ----

    /// <summary>Primeira leitura (ao abrir a aba); as seguintes vêm do ciclo de 20 s (<see cref="AutoRefreshAsync"/>), do "Mostrar anulados" e de cada ação. Não há botão Atualizar.</summary>
    public Task EnsureLoadedAsync() => loaded || loadCts is not null ? Task.CompletedTask : ReloadAsync();

    /// <summary>Releitura automática da aba (a cada 20 s enquanto ela está à vista): só com as listas à mostra e a ficha livre, e sem mensagem de erro a cada falha (a lista na tela continua valendo).</summary>
    public Task AutoRefreshAsync() => (loaded || failed) && CanRefresh && panel == HealthPanel.Lists ? ReloadAsync(quiet: true) : Task.CompletedTask;

    public Task ReloadAsync() => ReloadAsync(quiet: false);

    /// <summary>Uma requisição por leitura: a anterior é cancelada e só a mais nova altera a tela.</summary>
    private async Task ReloadAsync(bool quiet)
    {
        var cancellation = new CancellationTokenSource();
        Interlocked.Exchange(ref loadCts, cancellation)?.Cancel();
        var version = ++loadVersion;
        // A releitura automática (quiet) não liga o indicador de leitura: os botões do balão não podem piscar desabilitados a cada 20 s. Ela só começa com a ficha livre e qualquer leitura ou gravação nova a cancela.
        if (!quiet) SetLoading(true);
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
            if (!quiet) Say(MessageKind.Error, error.Message);
            onApiError?.Invoke(error);
        }
        finally { if (!quiet && version == loadVersion) SetLoading(false); }
    }

    private void Apply(HealthRecords records, bool withVoided)
    {
        loadedWithVoided = withVoided;
        var examId = selectedExam?.Id; var vaccineId = selectedVaccine?.Id;
        // A lista só é refeita quando mudou: a releitura de 20 s não pode piscar nem derrubar a seleção e o balão.
        if (!Exams.SequenceEqual(records.Exams)) { Exams.Clear(); foreach (var exam in records.Exams) Exams.Add(exam); }
        if (!Vaccines.SequenceEqual(records.Vaccines)) { Vaccines.Clear(); foreach (var vaccine in records.Vaccines) Vaccines.Add(vaccine); }
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

    // ---- Painel da aba: listas, formulários e ficha em linhas ----

    private HealthPanel panel;
    private string detailTitle = "", detailSubtitle = "";
    private IReadOnlyList<DetailField> detailFields = [];
    private TaskCompletionSource? detailClosed;
    private static readonly string[] PanelNames = [nameof(Panel), nameof(ShowLists), nameof(ShowExamForm), nameof(ShowVaccineForm), nameof(ShowDoneForm), nameof(ShowVoidForm), nameof(ShowDetail), nameof(ShowForm), nameof(PanelTitle), nameof(PanelPrimary)];

    public HealthPanel Panel { get => panel; private set { if (panel == value) return; panel = value; foreach (var name in PanelNames) Notify(name); } }
    public bool ShowLists => panel == HealthPanel.Lists;
    public bool ShowExamForm => panel == HealthPanel.NewExam;
    public bool ShowVaccineForm => panel == HealthPanel.NewVaccine;
    public bool ShowDoneForm => panel == HealthPanel.ExamDone;
    public bool ShowVoidForm => panel == HealthPanel.Void;
    public bool ShowDetail => panel == HealthPanel.Detail;
    /// <summary>Um dos quatro formulários está aberto (o primário e o Cancelar do rodapé valem para ele).</summary>
    public bool ShowForm => panel is HealthPanel.NewExam or HealthPanel.NewVaccine or HealthPanel.ExamDone or HealthPanel.Void;
    public string PanelTitle => panel switch
    {
        HealthPanel.NewExam => "Novo exame", HealthPanel.NewVaccine => "Nova vacina", HealthPanel.ExamDone => "Marcar exame como realizado",
        HealthPanel.Void => "Anular registro", HealthPanel.Detail => detailTitle, _ => ""
    };
    /// <summary>Texto do botão primário do formulário aberto (à direita do rodapé).</summary>
    public string PanelPrimary => panel switch
    {
        HealthPanel.NewExam => "Registrar exame", HealthPanel.NewVaccine => "Registrar vacina", HealthPanel.ExamDone => "Marcar como realizado", HealthPanel.Void => "Anular registro", _ => ""
    };
    public string DetailSubtitle => detailSubtitle;
    /// <summary>As linhas da ficha do registro aberto pelo duplo clique (rótulo à esquerda, valor à direita).</summary>
    public IReadOnlyList<DetailField> DetailFields => detailFields;

    /// <summary>Abre um dos formulários no lugar das listas. Marcar como realizado e Anular pedem o registro certo selecionado; sem cadastro ativo nada abre.</summary>
    public bool OpenForm(HealthPanel target)
    {
        if (IsBusy || target is HealthPanel.Lists or HealthPanel.Detail) return false;
        if (!CanRegister) { Say(MessageKind.Error, "O cadastro deste paciente está desativado nesta UBS: não é possível registrar exames nem vacinas."); return false; }
        if (target == HealthPanel.ExamDone && !CanMarkExamDone) { Say(MessageKind.Error, "Selecione um exame solicitado."); return false; }
        if (target == HealthPanel.Void && !CanVoid) { Say(MessageKind.Error, "Selecione o exame ou a vacina que será anulado."); return false; }
        Say(MessageKind.Info, "");
        ResetDrafts();
        Panel = target;
        return true;
    }

    /// <summary>Volta às listas descartando o que foi digitado; fecha também a ficha em linhas e libera a fila de fichas que esperava por ela.</summary>
    public void CloseForm()
    {
        ResetDrafts();
        Panel = HealthPanel.Lists;
        Interlocked.Exchange(ref detailClosed, null)?.TrySetResult();
    }

    /// <summary>A ficha em linhas de um exame ou de uma vacina (duplo clique, segundo clique ou Enter). Só termina quando a ficha fecha: com vários selecionados as fichas abrem uma por vez.</summary>
    public Task OpenDetailAsync(object item)
    {
        if (panel != HealthPanel.Lists) return Task.CompletedTask;
        static string Moment(DateTimeOffset? value) => value?.ToLocalTime().ToString("dd/MM/yyyy HH:mm", DisplayFormat.Culture) ?? "—";
        var fields = new List<DetailField>();
        void Add(string label, string? value) { if (!string.IsNullOrWhiteSpace(value)) fields.Add(new DetailField(label, value)); }
        switch (item)
        {
            case ExamInfo exam:
                detailTitle = exam.Name; detailSubtitle = "Exame";
                Add("Situação", exam.StatusDisplay); Add("Solicitado em", Moment(exam.RequestedAt));
                if (exam.PerformedOn is { } done) Add("Realizado em", DisplayFormat.Date(done));
                Add("Observação", exam.Note);
                if (exam.IsVoided) { Add("Anulado em", Moment(exam.VoidedAt)); Add("Motivo da anulação", exam.VoidReason); }
                break;
            case VaccineInfo vaccine:
                detailTitle = vaccine.Name; detailSubtitle = "Vacina";
                Add("Situação", vaccine.StatusDisplay); Add("Dose", vaccine.DoseDisplay); Add("Aplicada em", DisplayFormat.Date(vaccine.AppliedOn));
                Add("Observação", vaccine.Note);
                if (vaccine.IsVoided) { Add("Anulada em", Moment(vaccine.VoidedAt)); Add("Motivo da anulação", vaccine.VoidReason); }
                break;
            default: return Task.CompletedTask;
        }
        detailFields = fields;
        Notify(nameof(DetailFields)); Notify(nameof(DetailSubtitle));
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        detailClosed = closed;
        Panel = HealthPanel.Detail;
        return closed.Task;
    }

    /// <summary>Descarta os rascunhos dos formulários (nome, observação, data, dose e motivo) e os avisos de campo.</summary>
    private void ResetDrafts()
    {
        examName = examNote = examDoneDate = vaccineName = vaccineDate = vaccineNote = voidReason = "";
        vaccineDose = VaccineDose.Options[0];
        submittedExam = submittedVaccine = submittedVoid = submittedDone = false;
        foreach (var name in new[] { nameof(ExamName), nameof(ExamNote), nameof(ExamDoneDate), nameof(VaccineName), nameof(VaccineDate), nameof(VaccineNote), nameof(VaccineDoseChoice), nameof(VoidReason),
            nameof(ExamNameError), nameof(ExamNoteError), nameof(ExamDoneError), nameof(VaccineNameError), nameof(VaccineDateError), nameof(VaccineNoteError), nameof(VaccineDoseError), nameof(VoidReasonError) }) Notify(name);
    }

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

    public Task<bool> CreateExamAsync() => RunThenCloseAsync(async () =>
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

    public Task<bool> MarkExamDoneAsync() => RunThenCloseAsync(async () =>
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

    public Task<bool> CancelExamAsync() => RunAsync(async () =>
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

    public Task<bool> CreateVaccineAsync() => RunThenCloseAsync(async () =>
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
        var valid = VoidReasonError.Length == 0;
        // Motivo corrigido: o erro da tentativa anterior sai da faixa antes da confirmação (recusar a confirmação não deixa um erro velho na tela).
        if (valid && messageKind == MessageKind.Error) Say(MessageKind.Info, "");
        return valid;
    }

    public Task<bool> VoidSelectedAsync() => RunThenCloseAsync(async () =>
    {
        if (selectedExam is null && selectedVaccine is null) throw new NoraApiException("Selecione o exame ou a vacina que será anulado.");
        if (selectedExam is { IsVoided: true } || selectedVaccine is { IsVoided: true }) throw new NoraApiException("Este registro já foi anulado.");
        submittedVoid = true; Notify(nameof(VoidReasonError));
        if (VoidReasonError.Length > 0) throw new NoraApiException(VoidReasonError);
        var reason = voidReason.Trim();
        string what;
        if (selectedExam is { } exam) { await api.VoidExamAsync(unitId, exam.Id, reason); what = $"Exame \"{exam.Name}\" anulado"; }
        else { var vaccine = selectedVaccine!; await api.VoidVaccineAsync(unitId, vaccine.Id, reason); what = $"Vacina \"{vaccine.Name}\" anulada"; }
        voidReason = ""; submittedVoid = false; Notify(nameof(VoidReason)); Notify(nameof(VoidReasonError));
        await ReloadAfterChangeAsync($"{what}, com o motivo registrado. O registro permanece no histórico.");
    });

    // ---- Apoio ----

    /// <summary>Uma operação de escrita por vez; a falha vira mensagem dentro da ficha e, se a sessão acabou, avisa a janela principal.</summary>
    private async Task<bool> RunAsync(Func<Task> operation)
    {
        if (IsBusy) return false;
        SetBusy(true);
        Say(MessageKind.Info, "");
        var reload = false; var done = false;
        try { await operation(); done = true; }
        catch (NoraApiException error)
        {
            Say(MessageKind.Error, error.Message); onApiError?.Invoke(error);
            // Registro alterado ou anulado por outra pessoa (409/404): a lista é lida de novo para mostrar o que ficou valendo.
            reload = error.Status is 409 or 404 && !error.Unauthorized;
        }
        finally { SetBusy(false); }
        if (reload) await ReloadSilentlyAsync();
        return done;
    }

    /// <summary>Roda a operação de um formulário: dando certo, o formulário aberto se fecha e a aba volta às listas; dando erro, ele continua aberto com os avisos dos campos.</summary>
    private async Task<bool> RunThenCloseAsync(Func<Task> operation)
    {
        var done = await RunAsync(operation);
        if (done && panel != HealthPanel.Lists) CloseForm();
        return done;
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
            Say(MessageKind.Success, success + " A lista será lida de novo em instantes.");
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
