using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using Nora.Desktop.Models;
using Nora.Desktop.Services;

namespace Nora.Desktop.ViewModels;

public sealed record PeriodOption(int Days, string Label);

/// <summary>Filtros que geraram os números na tela: período (sempre dia inteiro, fim exclusivo), UBS, região e especialidade.</summary>
public sealed record MetricsRequest(DateTimeOffset From, DateTimeOffset To, Guid? UnitId, string Region, string Specialty, int PeriodDays);

/// <summary>
/// Indicadores da rede (GET /api/municipal/metrics). Um filtro compartilhado por Visão geral, Unidades, Especialidades e Indicadores:
/// cada mudança faz uma única requisição, a anterior é cancelada e uma resposta atrasada nunca sobrescreve a mais nova.
/// </summary>
public sealed partial class WorkspaceViewModel
{
    private const int MaxRangeDays = 366;
    private int metricsPeriodDays = 30;
    private string metricsFromText = "", metricsToText = "", metricsRangeError = "", metricsRegion = "", metricsSpecialty = "";
    private (DateTime From, DateTime To)? appliedCustomRange;
    private NetworkMetrics? metrics;
    private MetricsRequest? appliedMetrics;
    private bool metricsFailed;
    private int networkLoading, networkVersion;
    private CancellationTokenSource? networkCts;
    private readonly SortedSet<string> knownSpecialties = new(StringComparer.CurrentCultureIgnoreCase);

    public static IReadOnlyList<PeriodOption> PeriodOptions { get; } =
        [new(7, "Últimos 7 dias"), new(30, "Últimos 30 dias"), new(90, "Últimos 90 dias"), new(0, "Personalizado")];
    public ObservableCollection<FilterOption> RegionOptions { get; } = [new("", "Todas as regiões")];
    public ObservableCollection<FilterOption> SpecialtyOptions { get; } = [new("", "Todas as especialidades")];
    public ObservableCollection<MetricCard> OverviewCards { get; } = [];
    public ObservableCollection<PerformanceRow> UnitPerformance { get; } = [];
    public ObservableCollection<PerformanceRow> SpecialtyPerformance { get; } = [];
    public ObservableCollection<PerformanceRow> RegionPerformance { get; } = [];
    public ChartSeries NetworkChart { get; } = new();
    public ObservableCollection<DailyPoint> DailyRows => NetworkChart.Rows;
    public IReadOnlyList<DailyPoint> ChartDays => NetworkChart.Days;

    public int MetricsPeriodDays
    {
        get => metricsPeriodDays;
        set
        {
            if (metricsPeriodDays == value) return;
            metricsPeriodDays = value;
            if (value == 0 && metricsFromText.Length == 0 && appliedMetrics is { } last)
            {
                metricsFromText = DisplayFormat.Date(DateOnly.FromDateTime(last.From.LocalDateTime));
                metricsToText = DisplayFormat.Date(DateOnly.FromDateTime(last.To.LocalDateTime.AddDays(-1)));
                Notify(nameof(MetricsFromText)); Notify(nameof(MetricsToText));
            }
            Notify(); Notify(nameof(IsCustomPeriod));
            // Personalizado só consulta ao aplicar as datas; os períodos prontos consultam na hora.
            if (value != 0) ReloadNetwork(includeSummary: false);
        }
    }
    public bool IsCustomPeriod => metricsPeriodDays == 0;
    public string MetricsFromText { get => metricsFromText; set { metricsFromText = value ?? ""; Notify(); ClearRangeError(); } }
    public string MetricsToText { get => metricsToText; set { metricsToText = value ?? ""; Notify(); ClearRangeError(); } }
    public string MetricsRangeError { get => metricsRangeError; private set { if (metricsRangeError == value) return; metricsRangeError = value; Notify(); Notify(nameof(HasMetricsRangeError)); } }
    public bool HasMetricsRangeError => metricsRangeError.Length > 0;
    private void ClearRangeError() { if (metricsRangeError.Length > 0) MetricsRangeError = ""; }
    public string MetricsRegion { get => metricsRegion; set { value ??= ""; if (metricsRegion == value) return; metricsRegion = value; Notify(); ReloadNetwork(includeSummary: false); } }
    public string MetricsSpecialty { get => metricsSpecialty; set { value ??= ""; if (metricsSpecialty == value) return; metricsSpecialty = value; Notify(); ReloadNetwork(includeSummary: false); } }

    public bool CanFilterNetwork => IsAuthenticated && IsMunicipal;
    public bool MetricsLoading => networkLoading > 0;
    public string MetricsLoadingLabel => networkLoading > 0 ? "Atualizando os indicadores…" : "";
    public bool HasMetrics => metrics is not null;
    public string MetricsStateText => !IsAuthenticated ? "Entre na Secretaria para carregar os indicadores."
        : metrics is not null ? ""
        : networkLoading > 0 ? "Carregando os indicadores…"
        : metricsFailed ? "Os indicadores não puderam ser carregados. Os valores aparecem como — até uma nova tentativa."
        : metricsPeriodDays == 0 && appliedCustomRange is null ? "Informe as datas e aplique o período personalizado."
        : "Os indicadores ainda não foram carregados.";
    /// <summary>Filtros a que os números mostrados se referem (a faixa de dados desatualizados avisa quando a última consulta falhou).</summary>
    public string MetricsScopeLabel
    {
        get
        {
            if (appliedMetrics is not { } request) return "";
            var unit = request.UnitId is { } id ? Units.FirstOrDefault(item => item.Id == id)?.Name ?? "UBS selecionada" : "Toda a rede";
            var first = DateOnly.FromDateTime(request.From.LocalDateTime); var last = DateOnly.FromDateTime(request.To.LocalDateTime.AddDays(-1));
            return $"Números de {DisplayFormat.Date(first)} a {DisplayFormat.Date(last)} · {unit} · {(request.Region.Length == 0 ? "todas as regiões" : request.Region)} · {(request.Specialty.Length == 0 ? "todas as especialidades" : request.Specialty)}";
        }
    }
    public long UnitsWithoutDatabaseCount => Math.Max(metrics?.UnitsWithoutDatabase ?? 0, summary?.UnitsWithoutDatabase ?? 0);
    public bool HasUnitsWithoutDatabase => UnitsWithoutDatabaseCount > 0;
    public string UnitsWithoutDatabaseNotice => UnitsWithoutDatabaseCount switch
    {
        0 => "",
        1 => "1 UBS está fora destes números porque a base dela ainda não foi criada. Crie a base na aba Unidades.",
        var count => $"{count} UBS estão fora destes números porque a base delas ainda não foi criada. Crie as bases na aba Unidades."
    };
    public string ChartState => NetworkChart.State;
    public bool HasChartData => NetworkChart.HasData;

    private static readonly string[] MetricsNames =
    [
        nameof(MetricsScopeLabel), nameof(HasMetrics), nameof(MetricsStateText), nameof(UnitsWithoutDatabaseCount), nameof(HasUnitsWithoutDatabase), nameof(UnitsWithoutDatabaseNotice),
        nameof(ChartDays), nameof(ChartState), nameof(HasChartData)
    ];
    private void NotifyMetrics() { foreach (var name in MetricsNames) Notify(name); }

    /// <summary>Valida as datas digitadas (dd/mm/aaaa) e consulta o período personalizado.</summary>
    public void ApplyCustomPeriod()
    {
        if (!IsAuthenticated || !IsMunicipal) return;
        var error = ValidateDateRange(metricsFromText, metricsToText, out var from, out var to);
        MetricsRangeError = error;
        if (error.Length > 0) return;
        appliedCustomRange = (from, to);
        ReloadNetwork(includeSummary: false);
    }

    /// <summary>Datas inicial e final (inclusivas) em dd/mm/aaaa: início ≤ fim e no máximo 366 dias.</summary>
    internal static string ValidateDateRange(string fromText, string toText, out DateTime from, out DateTime to)
    {
        to = default;
        if (!DisplayFormat.TryParseDate(fromText, out from) || !DisplayFormat.TryParseDate(toText, out to)) return "Informe as duas datas no formato dd/mm/aaaa.";
        if (from > to) return "A data inicial não pode ser posterior à data final.";
        if ((to - from).TotalDays + 1 > MaxRangeDays) return "O período pode ter no máximo 366 dias.";
        return "";
    }

    private bool TryBuildMetricsRequest(out MetricsRequest request)
    {
        request = default!;
        DateTime start, endExclusive;
        if (metricsPeriodDays == 0)
        {
            if (appliedCustomRange is not { } range) return false;
            start = range.From; endExclusive = range.To.AddDays(1);
        }
        else { endExclusive = DateTime.Today.AddDays(1); start = endExclusive.AddDays(-metricsPeriodDays); }
        request = new(new DateTimeOffset(start), new DateTimeOffset(endExclusive), summaryUnitFilter == Guid.Empty ? null : summaryUnitFilter, metricsRegion, metricsSpecialty, metricsPeriodDays);
        return true;
    }

    /// <summary>Uma requisição por mudança de filtro: a anterior é cancelada e só a mais nova pode alterar a tela.</summary>
    private void ReloadNetwork(bool includeSummary)
    {
        if (disposed || !IsAuthenticated || !IsMunicipal || mustChangePassword) return;
        if (!TryBuildMetricsRequest(out var request) && !includeSummary) { NotifyMetrics(); return; }
        var cancellation = new CancellationTokenSource();
        Interlocked.Exchange(ref networkCts, cancellation)?.Cancel();
        var version = ++networkVersion;
        _ = RunNetworkLoadAsync(version, includeSummary, cancellation.Token);
    }

    private async Task RunNetworkLoadAsync(int version, bool includeSummary, CancellationToken cancellation)
    {
        backgroundOperations++; networkLoading++; NotifyLoading();
        try
        {
            await gate.WaitAsync(cancellation);
            try
            {
                if (cancellation.IsCancellationRequested || disposed || !IsAuthenticated) return;
                await Task.WhenAll(LoadMetricsAsync(version, cancellation), includeSummary ? LoadSummaryAsync(version, cancellation) : Task.CompletedTask);
                if (version != networkVersion || disposed) return;
                RecoverFromRefreshFailure(); metricsFailed = false; MarkFresh();
            }
            catch (NoraApiException error) { if (version == networkVersion) ReportNetworkFailure(error); }
            finally { gate.Release(); }
        }
        catch (OperationCanceledException) { }
        finally
        {
            backgroundOperations--; networkLoading--;
            if (!disposed) { NotifyLoading(); Notify(nameof(MetricsStateText)); }
        }
    }

    private void NotifyLoading() { Notify(nameof(IsBusy)); Notify(nameof(ShowProgress)); Notify(nameof(MetricsLoading)); Notify(nameof(MetricsLoadingLabel)); Notify(nameof(MetricsStateText)); }

    /// <summary>Falha de uma consulta pedida por filtro: sempre aparece na faixa comum e marca os números anteriores como desatualizados.</summary>
    private void ReportNetworkFailure(NoraApiException error)
    {
        if (error.Unauthorized) { SignOut(); Say(MessageKind.Error, error.Message); return; }
        if (error.Code == "password-change-required") { EnterPasswordChange(); return; }
        metricsFailed = true;
        refreshFailed = true; refreshErrorText = error.Message;
        MarkStale(); Say(MessageKind.Error, error.Message);
    }

    /// <summary>Lê os indicadores com os filtros atuais. Sem alteração de conteúdo nada é recriado.</summary>
    private async Task LoadMetricsAsync(int version, CancellationToken cancellation = default)
    {
        if (!TryBuildMetricsRequest(out var request)) return;
        var result = await api.MetricsAsync(request.From, request.To, request.UnitId, request.Region, request.Specialty, cancellation);
        if (disposed || !IsAuthenticated || version != networkVersion || cancellation.IsCancellationRequested) return;
        ApplyMetrics(result, request);
    }

    private void ApplyMetrics(NetworkMetrics result, MetricsRequest request)
    {
        var scopeChanged = appliedMetrics != request;
        var contentChanged = metrics is null || JsonSerializer.Serialize(metrics) != JsonSerializer.Serialize(result);
        appliedMetrics = request; metricsFailed = false;
        if (!scopeChanged && !contentChanged) return;
        metrics = result;
        Replace(OverviewCards, BuildOverviewCards());
        Replace(UnitPerformance, result.ByUnit.Select(unit => new PerformanceRow(unit.Name, unit.Region, unit.Appointments.AttendanceScore, unit.Appointments.Completed, unit.Appointments.Missed, unit.Appointments.Cancelled, unit.Waiting.ActiveEntries, unit.Waiting.AverageWaitMinutes)).OrderBy(row => row.Name, StringComparer.CurrentCultureIgnoreCase).ToArray());
        Replace(RegionPerformance, result.ByRegion.Select(region => new PerformanceRow(region.Region, "", region.Appointments.AttendanceScore, region.Appointments.Completed, region.Appointments.Missed, region.Appointments.Cancelled, region.Waiting.ActiveEntries, region.Waiting.AverageWaitMinutes)).OrderBy(row => row.Name, StringComparer.CurrentCultureIgnoreCase).ToArray());
        Replace(SpecialtyPerformance, result.BySpecialty.Select(item => new PerformanceRow(item.Specialty, "", item.AttendanceScore, item.Completed, item.Missed, item.Cancelled, item.ActiveEntries, item.AverageWaitMinutes)).OrderBy(row => row.Name, StringComparer.CurrentCultureIgnoreCase).ToArray());
        var sparse = SparseDays(result.Daily);
        NetworkChart.Apply(BuildChartDays(sparse, request.From, request.To), sparse, result.Daily.Length == 0 ? "Nenhuma consulta com data marcada no período." : "");
        if (request.Specialty.Length == 0) CollectSpecialties(result);
        NotifyMetrics();
    }

    private void CollectSpecialties(NetworkMetrics result)
    {
        var grew = false;
        foreach (var item in result.BySpecialty) if (!string.IsNullOrWhiteSpace(item.Specialty)) grew |= knownSpecialties.Add(item.Specialty.Trim());
        if (!grew) return;
        Replace(SpecialtyOptions, new[] { new FilterOption("", "Todas as especialidades") }.Concat(knownSpecialties.Select(name => new FilterOption(name, name))).ToArray());
    }

    /// <summary>Série densa do período: dias sem registro valem zero. A janela é [from, to), então o último dia é o anterior a "to".</summary>
    private static DailyPoint[] BuildChartDays(DailyPoint[] sparse, DateTimeOffset from, DateTimeOffset to) =>
        ChartSeries.Dense(sparse, DateOnly.FromDateTime(from.LocalDateTime), DateOnly.FromDateTime(to.LocalDateTime.AddDays(-1)));

    /// <summary>Pontos esparsos da API (só dias com dado), em ordem de data.</summary>
    private static DailyPoint[] SparseDays(DailyMetrics[] daily) =>
        daily.Select(day => DateOnly.TryParseExact(day.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? new DailyPoint(date, day.Completed, day.Missed, day.Cancelled) : null)
            .OfType<DailyPoint>().OrderBy(point => point.Date).ToArray();

    private MetricCard[] BuildOverviewCards() => BuildCards(metrics?.Totals.Appointments, metrics?.Totals.Waiting);

    /// <summary>Seis cartões do período, iguais na Secretaria (rede) e no OBS (UBS): consultas, score e esperas.</summary>
    private static MetricCard[] BuildCards(AppointmentMetrics? appointments, WaitingMetrics? waiting)
    {
        static string Number(long? value) => value is { } number ? DisplayFormat.Count(number) : "—";
        return
        [
            new("Consultas concluídas", Number(appointments?.Completed), "Consultas com data marcada no período"),
            new("Faltas", Number(appointments?.Missed), "Consultas registradas como falta"),
            new("Cancelamentos", Number(appointments?.Cancelled), appointments is null ? "Aguardando os indicadores" : appointments.CancelledByPatient == 1 ? "1 feito pelo paciente" : $"{DisplayFormat.Count(appointments.CancelledByPatient)} feitos pelo paciente"),
            new("Comparecimento", appointments is null ? "—" : DisplayFormat.Percent(appointments.AttendanceScore),
                appointments is null ? "Aguardando os indicadores" : appointments.AttendanceScore is null ? "Sem consulta encerrada no período para calcular." : "Concluídas ÷ (concluídas + faltas + cancelamentos do paciente ponderados)"),
            new("Espera média da fila", waiting is null ? "—" : DisplayFormat.Wait(waiting.AverageWaitMinutes),
                waiting is null ? "Aguardando os indicadores" : waiting.AverageWaitMinutes is null ? "Nenhuma entrada saiu da fila por oferta aceita no período." : "Da entrada na fila até a oferta aceita"),
            new("Maior espera atual", waiting is null ? "—" : DisplayFormat.Wait(waiting.LongestActiveWaitMinutes),
                waiting is null ? "Aguardando os indicadores" : waiting.LongestActiveWaitMinutes is null ? "Nenhuma entrada ativa na fila agora." : "Maior espera entre as entradas ativas agora")
        ];
    }

    private void ClearMetrics()
    {
        Interlocked.Exchange(ref networkCts, null)?.Cancel();
        networkVersion++;
        metrics = null; appliedMetrics = null; appliedCustomRange = null; metricsFailed = false; NetworkChart.Clear("Sem indicadores carregados.");
        metricsPeriodDays = 30; metricsRegion = ""; metricsSpecialty = ""; metricsFromText = ""; metricsToText = ""; metricsRangeError = "";
        knownSpecialties.Clear();
        Replace(OverviewCards, BuildOverviewCards()); Replace(UnitPerformance, Array.Empty<PerformanceRow>()); Replace(RegionPerformance, Array.Empty<PerformanceRow>());
        Replace(SpecialtyPerformance, Array.Empty<PerformanceRow>());
        Replace(SpecialtyOptions, new[] { new FilterOption("", "Todas as especialidades") });
        foreach (var name in new[] { nameof(MetricsPeriodDays), nameof(IsCustomPeriod), nameof(MetricsRegion), nameof(MetricsSpecialty), nameof(MetricsFromText), nameof(MetricsToText), nameof(MetricsRangeError), nameof(HasMetricsRangeError), nameof(CanFilterNetwork) }) Notify(name);
        NotifyMetrics();
    }
}
