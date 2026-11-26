using System.Collections.ObjectModel;
using System.Text.Json;
using Nora.Desktop.Models;
using Nora.Desktop.Services;

namespace Nora.Desktop.ViewModels;

/// <summary>Janela (fim exclusivo) e período que geraram os números de uma leitura de indicadores da UBS.</summary>
public sealed record UnitMetricsRequest(DateTimeOffset From, DateTimeOffset To, int PeriodDays);

/// <summary>Leitura de indicadores já com o pedido que a originou; <see cref="Failed"/> só existe na leitura tolerante do Painel.</summary>
public sealed record UnitMetricsRead(UnitMetrics? Data, UnitMetricsRequest Request, bool Failed = false);

/// <summary>
/// Indicadores da UBS ativa (GET /api/obs/units/{id}/metrics). Duas leituras independentes:
/// a aba Indicadores, com o filtro de período do usuário (uma requisição por mudança, a anterior é cancelada e resposta atrasada é descartada),
/// e o Painel, com o período do cabeçalho (os últimos 30 dias no Dia) e a série do gráfico, lidos na troca de UBS, na mudança de período
/// (<see cref="ReloadPanelMetrics"/>) e, no ciclo de 20 s, só quando a seção está aberta e a última tentativa tem mais de 5 minutos.
/// </summary>
public sealed partial class WorkspaceViewModel
{
    private const int PanelDays = 30, PanelChartDays = 7;
    /// <summary>Intervalo máximo do período personalizado dos Indicadores da UBS: 20 anos, o limite da API (a validação de 366 dias de <c>Metrics.cs</c> é da Secretaria).</summary>
    private const int UnitMaxRangeDays = 7305;
    private static readonly TimeSpan MetricsRefreshAge = TimeSpan.FromMinutes(5);

    private int unitPeriodDays = 30;
    private string unitFromText = "", unitToText = "", unitRangeError = "", unitSpecialtySearch = "", unitSpecialtyFilter = "Todas";
    private (DateTime From, DateTime To)? appliedUnitRange;
    private UnitMetrics? unitMetrics;
    private UnitMetricsRequest? appliedUnitMetrics;
    private bool unitMetricsFailed;
    private int unitMetricsLoading, unitMetricsVersion;
    private CancellationTokenSource? unitMetricsCts;
    private DateTimeOffset? unitMetricsAttemptedAt;
    private UnitMetrics? panelMetrics;
    private UnitMetricsRequest? panelRequest;
    private bool panelFailed;
    private DateTimeOffset? panelAttemptedAt;
    private ViewMode unitViewMode = ViewMode.Cards;
    private readonly Dictionary<string, ChartKind> unitChartKinds = [];

    public static IReadOnlyList<PeriodOption> UnitPeriodOptions { get; } =
        [new(1, "Dia"), new(7, "Semana"), new(30, "Mês"), new(365, "Ano"), new(1826, "5 anos"), new(7305, "Máximo"), new(0, "Personalizado")];
    /// <summary>Os seis cards resumidos dos Indicadores: comparecimento, concluídas, faltas, canceladas, espera média e ocupação da agenda.</summary>
    public ObservableCollection<MetricCard> UnitOverviewCards { get; } = [];
    /// <summary>A grade do modo gráficos dos Indicadores.</summary>
    public ObservableCollection<UnitChartCard> UnitChartCards { get; } = [];
    public ObservableCollection<PerformanceRow> UnitSpecialtyPerformance { get; } = [];
    /// <summary>Cards ou gráficos nos Indicadores (o <c>ViewModeToggle</c> do cabeçalho liga aqui nos dois sentidos).</summary>
    public ViewMode UnitViewMode
    {
        get => unitViewMode;
        set { if (unitViewMode == value) return; unitViewMode = value; Notify(); Notify(nameof(ShowUnitCards)); Notify(nameof(ShowUnitCharts)); }
    }
    public bool ShowUnitCards => unitViewMode == ViewMode.Cards;
    public bool ShowUnitCharts => unitViewMode == ViewMode.Charts;
    public string UnitChartsEmptyText => UnitChartCards.Count > 0 || unitMetrics is null ? "" : "Nenhum gráfico para mostrar neste período.";
    public static string[] UnitSpecialtyFilterOptions { get; } = ["Todas", "Com pessoas na fila", "Com faltas"];
    public string UnitSpecialtySearch { get => unitSpecialtySearch; set { unitSpecialtySearch = value ?? ""; Notify(); Notify(nameof(FilteredUnitSpecialtyPerformance)); Notify(nameof(UnitSpecialtyState)); } }
    public string UnitSpecialtyFilter { get => unitSpecialtyFilter; set { unitSpecialtyFilter = value ?? "Todas"; Notify(); Notify(nameof(FilteredUnitSpecialtyPerformance)); Notify(nameof(UnitSpecialtyState)); } }
    public IEnumerable<PerformanceRow> FilteredUnitSpecialtyPerformance => UnitSpecialtyPerformance.Where(row => row.Name.Contains(UnitSpecialtySearch.Trim(), StringComparison.CurrentCultureIgnoreCase) && (UnitSpecialtyFilter switch { "Com pessoas na fila" => row.ActiveEntries > 0, "Com faltas" => row.Missed > 0, _ => true }));
    public ChartSeries UnitChart { get; } = new();
    public ChartSeries PanelChart { get; } = new();

    public int UnitPeriodDays
    {
        get => unitPeriodDays;
        set
        {
            if (unitPeriodDays == value) return;
            unitPeriodDays = value;
            if (value == 0 && unitFromText.Length == 0 && appliedUnitMetrics is { } last)
            {
                unitFromText = DisplayFormat.Date(DateOnly.FromDateTime(last.From.LocalDateTime));
                unitToText = DisplayFormat.Date(DateOnly.FromDateTime(last.To.LocalDateTime.AddDays(-1)));
                Notify(nameof(UnitFromText)); Notify(nameof(UnitToText));
            }
            Notify(); Notify(nameof(IsUnitCustomPeriod)); Notify(nameof(UnitMetricsStateText));
            // Personalizado só consulta ao aplicar as datas; os períodos prontos consultam na hora.
            if (value != 0) ReloadUnitMetrics();
        }
    }
    public bool IsUnitCustomPeriod => unitPeriodDays == 0;
    public string UnitFromText { get => unitFromText; set { unitFromText = value ?? ""; Notify(); ClearUnitRangeError(); } }
    public string UnitToText { get => unitToText; set { unitToText = value ?? ""; Notify(); ClearUnitRangeError(); } }
    public string UnitRangeError { get => unitRangeError; private set { if (unitRangeError == value) return; unitRangeError = value; Notify(); Notify(nameof(HasUnitRangeError)); } }
    public bool HasUnitRangeError => unitRangeError.Length > 0;
    private void ClearUnitRangeError() { if (unitRangeError.Length > 0) UnitRangeError = ""; }
    public bool CanFilterUnitMetrics => IsAuthenticated && IsOperational && HasActiveObsUnit;
    public bool HasUnitMetrics => unitMetrics is not null;
    public bool UnitMetricsLoading => unitMetricsLoading > 0;
    public string UnitMetricsLoadingLabel => unitMetricsLoading > 0 ? "Atualizando os indicadores…" : "";
    public string UnitMetricsStateText => !IsAuthenticated ? "Entre na conta para carregar os indicadores da UBS."
        : !HasActiveObsUnit ? "Escolha uma UBS no Painel da UBS para ver os indicadores."
        // As datas do personalizado ficam no botão Filtros: enquanto os números na tela não são os do personalizado, a tela diz onde informá-las.
        : unitPeriodDays == 0 && appliedUnitMetrics is not { PeriodDays: 0 } && unitMetricsLoading == 0 && !unitMetricsFailed ? "Informe as datas no botão Filtros e aplique o período personalizado."
        : unitMetrics is not null ? ""
        : unitMetricsLoading > 0 ? "Carregando os indicadores…"
        : unitMetricsFailed ? "Os indicadores não puderam ser carregados. Os valores aparecem como — até uma nova tentativa."
        : unitPeriodDays == 0 && appliedUnitRange is null ? "Informe as datas e aplique o período personalizado."
        : "Os indicadores ainda não foram carregados.";
    public string UnitSpecialtyState => unitMetrics is null ? "" : unitMetrics.BySpecialty.Length == 0 ? "Nenhuma especialidade com consulta ou fila neste período." : !FilteredUnitSpecialtyPerformance.Any() ? "Nenhuma especialidade corresponde aos filtros." : "";
    /// <summary>Período e UBS a que os números mostrados se referem (a faixa de dados desatualizados avisa quando a última consulta falhou).</summary>
    public string UnitMetricsScopeLabel
    {
        get
        {
            if (appliedUnitMetrics is not { } request) return "";
            var first = DateOnly.FromDateTime(request.From.LocalDateTime); var last = DateOnly.FromDateTime(request.To.LocalDateTime.AddDays(-1));
            return $"Números de {DisplayFormat.Date(first)} a {DisplayFormat.Date(last)} · {ActiveObsUnitLabel}";
        }
    }

    public string PanelWaitValue => PanelData is not { } data ? "—" : DisplayFormat.Wait(data.Waiting.AverageWaitMinutes);
    /// <summary>A espera média do Painel olha o período escolhido; no Dia, os últimos 30 dias (um dia só quase nunca tem oferta aceita).</summary>
    public string PanelWaitHint
    {
        get
        {
            var span = panelPeriodDays == 1 ? $"nos últimos {PanelDays} dias" : "no período";
            return !HasActiveObsUnit ? "Aguardando a escolha da UBS"
                : panelMetrics is null ? (panelFailed ? "Não foi possível ler os indicadores agora." : "Aguardando a leitura dos indicadores")
                : PanelData is not { } data ? (panelFailed ? "Não foi possível ler os indicadores agora." : "Atualizando o período…")
                : data.Waiting.AverageWaitMinutes is null ? $"Nenhuma entrada saiu da fila por oferta aceita {span}."
                : panelFailed ? "A última leitura não concluiu; este é o valor da leitura anterior."
                : panelPeriodDays == 1 ? $"Média dos últimos {PanelDays} dias, da entrada na fila até a oferta aceita" : "Média do período, da entrada na fila até a oferta aceita";
        }
    }

    private static readonly string[] UnitMetricsNames =
    [
        nameof(HasUnitMetrics), nameof(UnitMetricsStateText), nameof(UnitMetricsScopeLabel), nameof(UnitSpecialtyState), nameof(CanFilterUnitMetrics), nameof(FilteredUnitSpecialtyPerformance), nameof(UnitChartsEmptyText)
    ];
    private static readonly string[] PanelNames = [nameof(PanelWaitValue), nameof(PanelWaitHint)];
    private void NotifyUnitMetrics() { foreach (var name in UnitMetricsNames) Notify(name); foreach (var name in PanelNames) Notify(name); }

    private bool UnitMetricsDue => unitMetricsAttemptedAt is null || DateTimeOffset.Now - unitMetricsAttemptedAt >= MetricsRefreshAge;
    private bool PanelMetricsDue => panelAttemptedAt is null || DateTimeOffset.Now - panelAttemptedAt >= MetricsRefreshAge;

    /// <summary>
    /// O que o ciclo automático e a entrada numa seção leem: os dados da seção e, só no Painel e nos Indicadores, os indicadores
    /// quando a última tentativa tem mais de 5 minutos. Atualização manual e filtros não passam por aqui.
    /// </summary>
    private UnitScope AutoScopeFor(string name)
    {
        var scope = ScopeFor(name);
        // Os alertas são avaliados em qualquer aba, no máximo a cada cinco minutos, para avisar sem exigir a abertura prévia da central.
        if (IsOperational && HasActiveObsUnit && AlertsDue) scope |= UnitScope.Alerts;
        if (scope.HasFlag(UnitScope.Metrics) && !UnitMetricsDue) scope &= ~UnitScope.Metrics;
        if (name == "home" && PanelMetricsDue) scope |= UnitScope.PanelMetrics;
        // Catálogo (profissionais e especialidades): lido na troca de UBS; as abas que o usam só o repetem depois de 5 minutos.
        if (name == "agenda" && CatalogDue) scope |= UnitScope.Catalog;
        if (name == "unit-settings")
        {
            if (!CatalogDue) scope &= ~UnitScope.Catalog;
            if (setupLoaded && !CatalogDue) scope &= ~UnitScope.Setup;
        }
        // Atendimento ao público e computadores mudam pouco: depois da primeira leitura bem-sucedida só são lidos de novo a cada 5 minutos (e no botão Atualizar).
        if (profileLoaded && !CatalogDue) scope &= ~UnitScope.Profile;
        if (devicesLoaded && !CatalogDue) scope &= ~UnitScope.Devices;
        if (scope.HasFlag(UnitScope.Alerts) && !AlertsDue) scope &= ~UnitScope.Alerts;
        return scope;
    }

    /// <summary>Valida as datas digitadas (dd/mm/aaaa) e consulta o período personalizado.</summary>
    public void ApplyUnitCustomPeriod()
    {
        if (!CanFilterUnitMetrics) return;
        var error = ValidateUnitRange(unitFromText, unitToText, out var from, out var to);
        UnitRangeError = error;
        if (error.Length > 0) return;
        appliedUnitRange = (from, to);
        ReloadUnitMetrics();
    }

    /// <summary>Datas inicial e final (inclusivas) em dd/mm/aaaa: início ≤ fim e no máximo 20 anos (o limite da API para os indicadores).</summary>
    internal static string ValidateUnitRange(string fromText, string toText, out DateTime from, out DateTime to)
    {
        to = default;
        if (!DisplayFormat.TryParseDate(fromText, out from) || !DisplayFormat.TryParseDate(toText, out to)) return "Informe as duas datas no formato dd/mm/aaaa.";
        if (from > to) return "A data inicial não pode ser posterior à data final.";
        if ((to - from).TotalDays + 1 > UnitMaxRangeDays) return "O período pode ter no máximo 20 anos.";
        return "";
    }

    private bool TryBuildUnitRequest(out UnitMetricsRequest request)
    {
        request = default!;
        DateTime start, endExclusive;
        if (unitPeriodDays == 0)
        {
            if (appliedUnitRange is not { } range) return false;
            start = range.From; endExclusive = range.To.AddDays(1);
        }
        else { endExclusive = DateTime.Today.AddDays(1); start = endExclusive.AddDays(-unitPeriodDays); }
        var from = new DateTimeOffset(start); var to = new DateTimeOffset(endExclusive);
        request = new(ClampWindowStart(from, to), to, unitPeriodDays);
        return true;
    }

    /// <summary>
    /// A API recusa janela maior que 7.305 dias em tempo corrido. Meia-noite local de 20 anos atrás pode ter um deslocamento diferente do de hoje (horário de verão
    /// antigo), e a janela passaria de 7.305 dias por uma hora: nesse caso o início é recuado/avançado para valer exatamente o limite.
    /// </summary>
    internal static DateTimeOffset ClampWindowStart(DateTimeOffset from, DateTimeOffset to) =>
        to - from > TimeSpan.FromDays(UnitMaxRangeDays) ? to.AddDays(-UnitMaxRangeDays) : from;

    /// <summary>Uma requisição por mudança de filtro: a anterior é cancelada e só a mais nova pode alterar a tela.</summary>
    private void ReloadUnitMetrics()
    {
        if (disposed || !CanFilterUnitMetrics || mustChangePassword || ActiveObsUnit is not { } unit) return;
        if (!TryBuildUnitRequest(out var request)) { NotifyUnitMetrics(); return; }
        var cancellation = new CancellationTokenSource();
        Interlocked.Exchange(ref unitMetricsCts, cancellation)?.Cancel();
        var version = ++unitMetricsVersion;
        unitMetricsAttemptedAt = DateTimeOffset.Now;
        _ = RunUnitMetricsLoadAsync(unit, request, version, cancellation.Token);
    }

    private async Task RunUnitMetricsLoadAsync(HealthUnit unit, UnitMetricsRequest request, int version, CancellationToken cancellation)
    {
        backgroundOperations++; unitMetricsLoading++; NotifyUnitLoading();
        try
        {
            await gate.WaitAsync(cancellation);
            try
            {
                if (cancellation.IsCancellationRequested || disposed || !IsAuthenticated) return;
                var result = await api.UnitMetricsAsync(unit.Id, request.From, request.To, cancellation);
                if (disposed || !IsAuthenticated || version != unitMetricsVersion || ActiveObsUnit?.Id != unit.Id || cancellation.IsCancellationRequested) return;
                ApplyUnitMetrics(result, request);
                RecoverFromRefreshFailure(); MarkFresh();
            }
            catch (NoraApiException error) { if (version == unitMetricsVersion) ReportUnitMetricsFailure(error); }
            finally { gate.Release(); }
        }
        catch (OperationCanceledException) { }
        finally
        {
            backgroundOperations--; unitMetricsLoading--;
            if (!disposed) { NotifyUnitLoading(); Notify(nameof(UnitMetricsStateText)); }
        }
    }

    private void NotifyUnitLoading() { Notify(nameof(IsBusy)); Notify(nameof(ShowProgress)); Notify(nameof(UnitMetricsLoading)); Notify(nameof(UnitMetricsLoadingLabel)); Notify(nameof(UnitMetricsStateText)); }

    /// <summary>Falha de uma consulta pedida por filtro: sempre aparece na faixa comum e marca os números anteriores como desatualizados.</summary>
    private void ReportUnitMetricsFailure(NoraApiException error)
    {
        if (error.Unauthorized) { SignOutRejected(error.Message); return; }
        if (error.Code == "password-change-required") { EnterPasswordChange(); return; }
        unitMetricsFailed = true;
        refreshFailed = true; refreshErrorText = error.Message;
        MarkStale(); Say(MessageKind.Error, error.Message);
        Notify(nameof(UnitMetricsStateText));
    }

    /// <summary>Aplica os indicadores do filtro. Sem alteração de conteúdo nada é recriado.</summary>
    private void ApplyUnitMetrics(UnitMetrics result, UnitMetricsRequest request)
    {
        var scopeChanged = appliedUnitMetrics != request;
        var contentChanged = unitMetrics is null || JsonSerializer.Serialize(unitMetrics) != JsonSerializer.Serialize(result);
        appliedUnitMetrics = request; unitMetricsFailed = false;
        if (!scopeChanged && !contentChanged) return;
        unitMetrics = result;
        Replace(UnitOverviewCards, BuildUnitSummaryCards(result.Appointments, result.Waiting, result.Capacity));
        Replace(UnitSpecialtyPerformance, result.BySpecialty.Select(item => new PerformanceRow(item.Specialty, "", item.AttendanceScore, item.Completed, item.Missed, item.Cancelled, item.ActiveEntries, item.AverageWaitMinutes))
            .OrderBy(row => row.Name, StringComparer.CurrentCultureIgnoreCase).ToArray());
        var sparse = SparseDays(result.Daily);
        UnitChart.Apply(BuildAxis(sparse, DateOnly.FromDateTime(request.From.LocalDateTime), DateOnly.FromDateTime(request.To.LocalDateTime.AddDays(-1))), sparse, result.Daily.Length == 0 ? "Nenhuma consulta com data marcada no período." : "");
        ApplyUnitGoals(result);
        RebuildUnitChartCards();
        NotifyUnitMetrics();
    }

    /// <summary>
    /// Os seis cards resumidos dos Indicadores, cada um no seu tom: comparecimento (turquesa), concluídas (verde), faltas (rosa), canceladas (amarelo),
    /// espera média (violeta) e ocupação da agenda (azul). Sem leitura, todos mostram — e a razão.
    /// </summary>
    private static MetricCard[] BuildUnitSummaryCards(AppointmentMetrics? appointments, WaitingMetrics? waiting, CapacityMetrics? capacity)
    {
        static string Number(long? value) => value is { } number ? DisplayFormat.Count(number) : "—";
        return
        [
            new("Comparecimento", appointments is null ? "—" : DisplayFormat.Percent(appointments.AttendanceScore),
                appointments is null ? "Aguardando os indicadores" : appointments.AttendanceScore is null ? "Sem consulta encerrada no período para calcular." : "Concluídas ÷ (concluídas + faltas + cancelamentos do paciente ponderados)", Tone.Teal),
            new("Consultas concluídas", Number(appointments?.Completed), "Consultas com data marcada no período", Tone.Green),
            new("Faltas", Number(appointments?.Missed), "Consultas registradas como falta", Tone.Pink),
            new("Cancelamentos", Number(appointments?.Cancelled), appointments is null ? "Aguardando os indicadores" : appointments.CancelledByPatient == 1 ? "1 feito pelo paciente" : $"{DisplayFormat.Count(appointments.CancelledByPatient)} feitos pelo paciente", Tone.Yellow),
            new("Espera média da fila", waiting is null ? "—" : DisplayFormat.Wait(waiting.AverageWaitMinutes),
                waiting is null ? "Aguardando os indicadores" : waiting.AverageWaitMinutes is null ? "Nenhuma entrada saiu da fila por oferta aceita no período." : "Da entrada na fila até a oferta aceita", Tone.Violet),
            new("Ocupação da agenda", capacity is null ? "—" : DisplayFormat.Percent(capacity.Occupancy),
                capacity is null ? "Aguardando os indicadores" : capacity.Occupancy is null ? "Sem horários ofertados para calcular" : "Horários ocupados ÷ ofertados no período", Tone.Blue)
        ];
    }

    /// <summary>Monta a grade do modo gráficos dos Indicadores com o que a API devolveu para o período.</summary>
    private void RebuildUnitChartCards()
    {
        var cards = new List<UnitChartCard>();
        if (unitMetrics is { } data)
        {
            const string span = "no período";
            cards.Add(AppointmentsChartCard(UnitChart.Days, data.Appointments.Completed, data.Appointments.Missed, data.Appointments.Cancelled,
                "Consultas pela data marcada, por situação.", "Composição das consultas encerradas no período.", UnitChart.State.Length > 0 ? UnitChart.State : "Nenhuma consulta com data marcada no período."));
            if (AttendanceChartCard(UnitChart.Days) is { } attendance) cards.Add(attendance);
            if (LightningChartCard(data.Lightning, span) is { } lightning) cards.Add(lightning);
            if (WaitChartCard(data.BySpecialty) is { } wait) cards.Add(wait);
            if (OccupancyChartCard(data.Capacity, "Horários da grade com início no período.") is { } occupancy) cards.Add(occupancy);
            if (SpecialtyTotalsChartCard(data.BySpecialty, span) is { } bySpecialty) cards.Add(bySpecialty);
        }
        ReplaceChartCards(UnitChartCards, cards, unitChartKinds);
        Notify(nameof(UnitChartsEmptyText));
    }

    /// <summary>
    /// Leitura tolerante do Painel: se só os indicadores falharem (por exemplo, base sem a migração mais nova), a UBS ainda abre e o cartão avisa.
    /// Sessão encerrada e troca obrigatória de senha continuam subindo como erro.
    /// </summary>
    private async Task<UnitMetricsRead> ReadPanelMetricsAsync(HealthUnit unit, UnitMetricsRequest request, CancellationToken cancellation = default)
    {
        try { return new(await api.UnitMetricsAsync(unit.Id, request.From, request.To, cancellation), request); }
        catch (NoraApiException error) when (!error.Unauthorized && error.Code != "password-change-required") { return new(null, request, true); }
    }

    private static async Task<UnitMetricsRead> ShareAsync(Task<UnitMetrics> read, UnitMetricsRequest request) => new(await read, request);
    private static async Task<UnitMetricsRead> SharePanelAsync(Task<UnitMetricsRead> shared, UnitMetricsRequest request) => (await shared) with { Request = request };

    /// <summary>Guarda os indicadores do Painel (a janela do período) e refaz a série, a tabela, os cards e os gráficos a partir da mesma resposta, sem outra requisição.</summary>
    private void ApplyPanelMetrics(UnitMetricsRead read)
    {
        if (read.Data is not { } data) panelFailed = true;
        else
        {
            var unchanged = panelRequest == read.Request && panelMetrics is not null && JsonSerializer.Serialize(panelMetrics) == JsonSerializer.Serialize(data);
            panelFailed = false;
            if (!unchanged) { panelMetrics = data; panelRequest = read.Request; }
        }
        RebuildPanelSeries();
        NotifyUnitMetrics();
        RebuildPanelOverview();
    }

    private void ClearUnitMetrics()
    {
        Interlocked.Exchange(ref unitMetricsCts, null)?.Cancel();
        unitMetricsVersion++;
        unitMetrics = null; appliedUnitMetrics = null; appliedUnitRange = null; unitMetricsFailed = false; unitMetricsAttemptedAt = null;
        panelMetrics = null; panelRequest = null; panelFailed = false; panelAttemptedAt = null;
        unitPeriodDays = 30; unitFromText = unitToText = unitRangeError = "";
        Replace(UnitOverviewCards, BuildUnitSummaryCards(null, null, null)); Replace(UnitSpecialtyPerformance, Array.Empty<PerformanceRow>());
        UnitChart.Clear("Sem indicadores carregados."); PanelChart.Clear("Sem indicadores carregados.");
        RebuildUnitChartCards();
        foreach (var name in new[] { nameof(UnitPeriodDays), nameof(IsUnitCustomPeriod), nameof(UnitFromText), nameof(UnitToText), nameof(UnitRangeError), nameof(HasUnitRangeError) }) Notify(name);
        NotifyUnitMetrics();
        ClearPanelOverview();
    }
}
