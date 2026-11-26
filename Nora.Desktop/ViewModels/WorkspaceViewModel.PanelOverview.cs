using System.Collections.ObjectModel;
using Nora.Desktop.Models;
using Nora.Desktop.Services;

namespace Nora.Desktop.ViewModels;

/// <summary>
/// Painel da UBS: o resumo que acompanha o período escolhido (Dia, Semana, Mês, Ano, 5 anos ou Máximo), a pesquisa do cabeçalho e o alternador cards/gráficos.
/// No Dia os quatro números de consulta vêm da lista de hoje (atualizada a cada 20 s) e a espera média, os gráficos de linha e a pizza de Vaga Relâmpago olham os últimos 30 dias
/// (o mesmo que o Painel sempre fez); nos demais períodos tudo vem dos indicadores da janela escolhida (uma leitura a cada mudança de período e, depois, a regra dos 5 minutos).
/// Fluxo da unidade (pacientes, fila, vagas) e ofertas em andamento são números de agora e não dependem do período.
/// </summary>
public sealed partial class WorkspaceViewModel
{
    public static IReadOnlyList<PeriodOption> PanelPeriodOptions { get; } =
        [new(1, "Dia"), new(7, "Semana"), new(30, "Mês"), new(365, "Ano"), new(1826, "5 anos"), new(7305, "Máximo")];

    private const string PanelRoomsKeywords = "Consultórios ativos em atendimento agora com grade hoje";
    private const string PanelAttentionKeywords = "Atenção da equipe alertas central de alertas";

    private int panelPeriodDays = 1, panelMetricsLoading, panelMetricsVersion;
    private string panelSearch = "";
    private ViewMode panelViewMode = ViewMode.Cards;
    private CancellationTokenSource? panelMetricsCts;
    private PanelTableRow[] panelTableAll = [];
    private readonly Dictionary<string, ChartKind> panelChartKinds = [];

    /// <summary>Os cards do painel que a pesquisa deixa à vista (resumo do período e fluxo da unidade, no mesmo grupo).</summary>
    public ObservableCollection<MetricCard> PanelCards { get; } = [];
    /// <summary>A grade do modo gráficos.</summary>
    public ObservableCollection<UnitChartCard> PanelChartCards { get; } = [];
    /// <summary>As linhas da tabela de consultas do período que a pesquisa deixa à vista.</summary>
    public ObservableCollection<PanelTableRow> PanelTableRows { get; } = [];

    public int PanelPeriodDays
    {
        get => panelPeriodDays;
        set
        {
            if (panelPeriodDays == value || PanelPeriodOptions.All(option => option.Days != value)) return;
            panelPeriodDays = value;
            Notify();
            foreach (var name in new[] { nameof(PanelSummaryTitle), nameof(PanelTableTitle), nameof(PanelWaitValue), nameof(PanelWaitHint) }) Notify(name);
            RebuildPanelSeries();
            RebuildPanelOverview();
            ReloadPanelMetrics();
        }
    }

    /// <summary>O texto da pesquisa do cabeçalho: filtra cards, painéis de gráfico e linhas da tabela, sem diferenciar maiúsculas nem acentos.</summary>
    public string PanelSearch
    {
        get => panelSearch;
        set { value ??= ""; if (panelSearch == value) return; panelSearch = value; Notify(); RebuildPanelOverview(); }
    }

    /// <summary>Cards ou gráficos (o <c>ViewModeToggle</c> do cabeçalho liga aqui nos dois sentidos; a escolha fica lembrada nas preferências).</summary>
    public ViewMode PanelViewMode
    {
        get => panelViewMode;
        set { if (panelViewMode == value) return; panelViewMode = value; Notify(); Notify(nameof(ShowPanelCards)); Notify(nameof(ShowPanelCharts)); }
    }
    public bool ShowPanelCards => panelViewMode == ViewMode.Cards;
    public bool ShowPanelCharts => panelViewMode == ViewMode.Charts;

    /// <summary>Como o período aparece nos títulos: "do dia", "da semana", "do mês", "do ano", "dos últimos 5 anos", "de todo o período".</summary>
    private string PanelPeriodNoun => panelPeriodDays switch { 1 => "do dia", 7 => "da semana", 30 => "do mês", 365 => "do ano", 1826 => "dos últimos 5 anos", _ => "de todo o período" };
    /// <summary>Como o período aparece nas frases: "hoje", "na semana", "no mês", "no ano", "nos últimos 5 anos", "em todo o período".</summary>
    private string PanelPeriodIn => panelPeriodDays switch { 1 => "hoje", 7 => "na semana", 30 => "no mês", 365 => "no ano", 1826 => "nos últimos 5 anos", _ => "em todo o período" };
    public string PanelSummaryTitle => $"Resumo {PanelPeriodNoun}";
    public string PanelTableTitle => panelPeriodDays == 1 ? $"Consultas dos últimos {PanelChartDays} dias" : $"Consultas {PanelPeriodNoun}";
    public bool PanelMetricsLoading => panelMetricsLoading > 0;
    public string PanelLoadingLabel => panelMetricsLoading > 0 ? "Atualizando o resumo do período…" : "";

    /// <summary>Janela, em dias, que o Painel lê: o próprio período, ou os últimos 30 dias quando o período é o dia.</summary>
    private static int PanelWindow(int period) => period == 1 ? PanelDays : period;

    /// <summary>Os indicadores lidos, só enquanto são os da janela do período escolhido (durante a troca de período não valem).</summary>
    private UnitMetrics? PanelData => panelMetrics is not null && panelRequest is { } request && request.PeriodDays == PanelWindow(panelPeriodDays) ? panelMetrics : null;

    private string PanelDataHint(string ok) =>
        PanelData is not null ? ok
        : !HasActiveObsUnit ? "Aguardando a escolha da UBS"
        : panelFailed ? "Não foi possível ler os indicadores agora."
        : panelMetrics is not null ? "Atualizando o período…"
        : "Aguardando a leitura dos indicadores";

    // ---- Pesquisa: o que fica visível ----

    /// <summary>O título do resumo só aparece se a pesquisa deixou algum card à vista.</summary>
    public bool ShowPanelSummary => PanelCards.Count > 0;
    public bool ShowPanelRooms => SearchMatches(panelSearch.Trim(), PanelRoomsKeywords);
    public bool ShowPanelAttention => SearchMatches(panelSearch.Trim(), PanelAttentionKeywords);
    public bool ShowPanelTable => panelSearch.Trim().Length == 0 || SearchMatches(panelSearch.Trim(), PanelTableTitle) || PanelTableRows.Count > 0;
    public string PanelTableState => PanelTableRows.Count > 0 ? "" : panelTableAll.Length == 0 ? PanelChart.State : "Nenhuma linha corresponde à pesquisa.";
    /// <summary>Aviso do modo cards quando a pesquisa não deixou nenhum bloco à vista.</summary>
    public string PanelCardsEmptyText => PanelCards.Count == 0 && !ShowPanelRooms && !ShowPanelAttention && !ShowPanelTable ? "Nenhum item do painel corresponde à pesquisa." : "";
    public string PanelChartsEmptyText => PanelChartCards.Count > 0 ? "" : panelSearch.Trim().Length > 0 ? "Nenhum gráfico corresponde à pesquisa." : "Nenhum gráfico para mostrar agora.";

    // ---- Cards ----

    private long TodayStatusCount(params string[] statuses) =>
        todayOverview is not null && todayOverviewDate == DateTime.Today ? todayOverview.Count(item => statuses.Contains(item.Status, StringComparer.Ordinal)) : 0;

    private MetricCard[] BuildPanelCards()
    {
        var data = PanelData;
        var today = panelPeriodDays == 1;
        string Count(string todayValue, Func<AppointmentMetrics, long> pick) => today ? todayValue : data is null ? "—" : DisplayFormat.Count(pick(data.Appointments));
        var scheduledHint = today ? "Agendadas para hoje, incluindo as chamadas." : $"Marcadas e ainda não encerradas {PanelPeriodIn}.";
        // O fluxo da unidade é de agora: fila de pessoas aguardando (indicadores) e vagas ativas, sem depender do período.
        var queue = panelMetrics is null ? "—" : DisplayFormat.Count(panelMetrics.Waiting.WaitingPatients);
        return
        [
            new("Consultas concluídas", Count(TodayCompletedCount, a => a.Completed), today ? "Consultas de hoje concluídas." : PanelDataHint($"Consultas concluídas {PanelPeriodIn}."), Tone.Green),
            new("Faltas", Count(TodayMissedCount, a => a.Missed), today ? "Consultas de hoje registradas como falta." : PanelDataHint($"Consultas registradas como falta {PanelPeriodIn}."), Tone.Pink),
            new("Cancelamentos", Count(TodayCancelledCount, a => a.Cancelled), today ? "Consultas de hoje canceladas." : PanelDataHint($"Consultas canceladas {PanelPeriodIn}."), Tone.Yellow),
            new("Consultas agendadas", Count(TodayScheduledCount, a => a.Scheduled + a.Called), today ? scheduledHint : PanelDataHint(scheduledHint), Tone.Blue),
            new("Tempo médio de espera", PanelWaitValue, PanelWaitHint, Tone.Violet),
            new("Ofertas em andamento", OffersRunningCount, "Ofertas de Vaga Relâmpago com prazo correndo agora.", Tone.Teal),
            new("Pacientes cadastrados", PatientTotalCount, "Cadastros de referência desta UBS.", Tone.Blue),
            new("Na fila", queue, panelMetrics is null ? PanelDataHint("") : "Pessoas aguardando vaga agora.", Tone.Violet),
            new("Vagas ativas", SlotOpenCount, "Vagas relâmpago abertas ou em oferta.", Tone.Teal)
        ];
    }

    // ---- Gráficos ----

    private List<UnitChartCard> BuildPanelChartCards()
    {
        var cards = new List<UnitChartCard>();
        var data = PanelData;
        var today = panelPeriodDays == 1;
        var days = PanelChart.Days;
        string emptyNote = !HasActiveObsUnit ? "Escolha uma UBS para ver os gráficos." : PanelChart.State.Length > 0 ? PanelChart.State : "Nenhuma consulta com data marcada no período.";
        long completed, missed, cancelled;
        if (today) { completed = TodayStatusCount("completed"); missed = TodayStatusCount("missed"); cancelled = TodayStatusCount("cancelled"); }
        else { completed = data?.Appointments.Completed ?? 0; missed = data?.Appointments.Missed ?? 0; cancelled = data?.Appointments.Cancelled ?? 0; }
        cards.Add(AppointmentsChartCard(days, completed, missed, cancelled,
            today ? $"Últimos {PanelChartDays} dias, para dar contexto ao dia." : "Consultas pela data marcada, por situação.",
            today ? "Consultas de hoje encerradas." : "Composição das consultas encerradas no período.", emptyNote));
        var span = today ? $"nos últimos {PanelDays} dias" : PanelPeriodIn;
        if (LightningChartCard(panelMetrics?.Lightning, span) is { } lightning) cards.Add(lightning);
        if (panelMetrics is { } waiting && WaitChartCard(waiting.BySpecialty) is { } wait) cards.Add(wait);
        if (panelCapacity is { } capacity && OccupancyChartCard(capacity, "Horários da grade de hoje até daqui a 7 dias.") is { } occupancy) cards.Add(occupancy);
        if (data is not null && SpecialtyTotalsChartCard(data.BySpecialty, today ? $"nos últimos {PanelDays} dias" : PanelPeriodIn) is { } bySpecialty) cards.Add(bySpecialty);
        if (ObsRegistry.Count > 0)
        {
            var slices = PieSlices(("Ativos", ObsRegistry.Count(patient => patient.IsActive), "StatusCompletedBrush"), ("Desativados", ObsRegistry.Count(patient => !patient.IsActive), "ChartBrush6"));
            if (slices.Count > 0) cards.Add(new("Pacientes cadastrados", [], slices, "", "Cadastros de referência desta UBS."));
        }
        return cards;
    }

    /// <summary>Refaz tudo o que o painel mostra a partir do que já foi lido: cards, grade de gráficos e tabela, já filtrados pela pesquisa. Só o que mudou é trocado.</summary>
    private void RebuildPanelOverview()
    {
        var query = panelSearch.Trim();
        Replace(PanelCards, BuildPanelCards().Where(card => SearchMatches(query, $"{card.Title} {card.Value} {card.Hint}")).ToArray());
        ReplaceChartCards(PanelChartCards, BuildPanelChartCards().Where(card => SearchMatches(query, card.SearchText)).ToArray(), panelChartKinds);
        var titleMatches = SearchMatches(query, PanelTableTitle);
        Replace(PanelTableRows, panelTableAll.Where(row => titleMatches || SearchMatches(query, row.SearchText)).ToArray());
        foreach (var name in new[] { nameof(ShowPanelSummary), nameof(ShowPanelRooms), nameof(ShowPanelAttention), nameof(ShowPanelTable), nameof(PanelTableState), nameof(PanelCardsEmptyText), nameof(PanelChartsEmptyText) }) Notify(name);
    }

    /// <summary>Recalcula o eixo e a tabela do Painel com os indicadores lidos e o período escolhido (sem nova leitura).</summary>
    private void RebuildPanelSeries()
    {
        if (PanelData is not { } data || panelRequest is not { } request)
        {
            PanelChart.Clear(panelFailed ? "Não foi possível ler os indicadores agora." : panelMetrics is not null ? "Atualizando o período…" : "Sem indicadores carregados.");
            panelTableAll = [];
            return;
        }
        var last = DateOnly.FromDateTime(request.To.LocalDateTime.AddDays(-1));
        var first = panelPeriodDays == 1 ? last.AddDays(1 - PanelChartDays) : DateOnly.FromDateTime(request.From.LocalDateTime);
        var sparse = SparseDays(data.Daily).Where(point => point.Date >= first && point.Date <= last).ToArray();
        var axis = BuildAxis(sparse, first, last);
        PanelChart.Apply(axis, sparse, sparse.Length == 0 ? (panelPeriodDays == 1 ? $"Nenhuma consulta com data marcada nos últimos {PanelChartDays} dias." : "Nenhuma consulta com data marcada no período.") : "");
        var grouping = ChartData.For(axis.Length);
        panelTableAll = [.. ChartData.Group(sparse, grouping).Select(bucket => new PanelTableRow(BucketPeriod(bucket), bucket.Completed, bucket.Missed, bucket.Cancelled))];
    }

    // ---- Leitura por período ----

    private static UnitMetricsRequest BuildPanelRequest(int period)
    {
        var end = DateTime.Today.AddDays(1);
        var window = PanelWindow(period);
        var to = new DateTimeOffset(end);
        return new(ClampWindowStart(new DateTimeOffset(end.AddDays(-window)), to), to, window);
    }

    /// <summary>
    /// Uma leitura por mudança de período (a anterior é cancelada e resposta atrasada é descartada). Se os indicadores já lidos cobrem a janela, nada é pedido:
    /// Dia e Mês usam os mesmos 30 dias.
    /// </summary>
    private void ReloadPanelMetrics()
    {
        if (disposed || !CanFilterUnitMetrics || mustChangePassword || ActiveObsUnit is not { } unit) return;
        if (PanelData is not null && !panelFailed) { NotifyUnitMetrics(); return; }
        var request = BuildPanelRequest(panelPeriodDays);
        var cancellation = new CancellationTokenSource();
        Interlocked.Exchange(ref panelMetricsCts, cancellation)?.Cancel();
        var version = ++panelMetricsVersion;
        panelAttemptedAt = DateTimeOffset.Now;
        _ = RunPanelMetricsLoadAsync(unit, request, version, cancellation.Token);
    }

    private async Task RunPanelMetricsLoadAsync(HealthUnit unit, UnitMetricsRequest request, int version, CancellationToken cancellation)
    {
        backgroundOperations++; panelMetricsLoading++; NotifyPanelLoading();
        try
        {
            await gate.WaitAsync(cancellation);
            try
            {
                if (cancellation.IsCancellationRequested || disposed || !IsAuthenticated) return;
                var read = await ReadPanelMetricsAsync(unit, request, cancellation);
                if (disposed || !IsAuthenticated || version != panelMetricsVersion || ActiveObsUnit?.Id != unit.Id || cancellation.IsCancellationRequested) return;
                ApplyPanelMetrics(read);
                if (read.Data is not null) { RecoverFromRefreshFailure(); MarkFresh(); }
            }
            catch (NoraApiException error) when (version == panelMetricsVersion)
            {
                if (error.Unauthorized) SignOutRejected(error.Message);
                else if (error.Code == "password-change-required") EnterPasswordChange();
            }
            finally { gate.Release(); }
        }
        catch (OperationCanceledException) { }
        finally
        {
            backgroundOperations--; panelMetricsLoading--;
            if (!disposed) NotifyPanelLoading();
        }
    }

    private void NotifyPanelLoading() { Notify(nameof(IsBusy)); Notify(nameof(ShowProgress)); Notify(nameof(PanelMetricsLoading)); Notify(nameof(PanelLoadingLabel)); }

    private void ClearPanelOverview()
    {
        Interlocked.Exchange(ref panelMetricsCts, null)?.Cancel();
        panelMetricsVersion++;
        panelTableAll = [];
        RebuildPanelOverview();
    }
}
