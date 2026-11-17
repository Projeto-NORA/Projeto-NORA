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
/// e o Painel, com os últimos 30 dias fixos (espera média) e a série dos últimos 7 dias, lidos na troca de UBS, na atualização manual
/// e, no ciclo de 20 s, só quando a seção está aberta e a última tentativa tem mais de 5 minutos.
/// </summary>
public sealed partial class WorkspaceViewModel
{
    private const int PanelDays = 30, PanelChartDays = 7;
    private static readonly TimeSpan MetricsRefreshAge = TimeSpan.FromMinutes(5);

    private int unitPeriodDays = 30;
    private string unitFromText = "", unitToText = "", unitRangeError = "";
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

    public static IReadOnlyList<PeriodOption> UnitPeriodOptions { get; } =
        [new(1, "Hoje"), new(7, "Últimos 7 dias"), new(30, "Últimos 30 dias"), new(90, "Últimos 90 dias"), new(0, "Personalizado")];
    public ObservableCollection<MetricCard> UnitOverviewCards { get; } = [];
    public ObservableCollection<MetricCard> UnitLightningCards { get; } = [];
    public ObservableCollection<PerformanceRow> UnitSpecialtyPerformance { get; } = [];
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
            Notify(); Notify(nameof(IsUnitCustomPeriod));
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
        : unitMetrics is not null ? ""
        : unitMetricsLoading > 0 ? "Carregando os indicadores…"
        : unitMetricsFailed ? "Os indicadores não puderam ser carregados. Os valores aparecem como — até uma nova tentativa."
        : unitPeriodDays == 0 && appliedUnitRange is null ? "Informe as datas e aplique o período personalizado."
        : "Os indicadores ainda não foram carregados.";
    public string UnitSpecialtyState => unitMetrics is null ? "" : unitMetrics.BySpecialty.Length == 0 ? "Nenhuma especialidade com consulta ou fila neste período." : "";
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

    public string PanelWaitValue => panelMetrics is null ? "—" : DisplayFormat.Wait(panelMetrics.Waiting.AverageWaitMinutes);
    public string PanelWaitHint => !HasActiveObsUnit ? "Aguardando a escolha da UBS"
        : panelMetrics is null ? (panelFailed ? "Não foi possível ler os indicadores agora." : "Aguardando a leitura dos indicadores")
        : panelMetrics.Waiting.AverageWaitMinutes is null ? $"Nenhuma entrada saiu da fila por oferta aceita nos últimos {PanelDays} dias."
        : panelFailed ? "A última leitura não concluiu; este é o valor da leitura anterior."
        : $"Média dos últimos {PanelDays} dias, da entrada na fila até a oferta aceita";

    private static readonly string[] UnitMetricsNames =
    [
        nameof(HasUnitMetrics), nameof(UnitMetricsStateText), nameof(UnitMetricsScopeLabel), nameof(UnitSpecialtyState), nameof(CanFilterUnitMetrics)
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
        var error = ValidateDateRange(unitFromText, unitToText, out var from, out var to);
        UnitRangeError = error;
        if (error.Length > 0) return;
        appliedUnitRange = (from, to);
        ReloadUnitMetrics();
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
        request = new(new DateTimeOffset(start), new DateTimeOffset(endExclusive), unitPeriodDays);
        return true;
    }

    private static UnitMetricsRequest BuildPanelRequest()
    {
        var end = DateTime.Today.AddDays(1);
        return new(new DateTimeOffset(end.AddDays(-PanelDays)), new DateTimeOffset(end), PanelDays);
    }

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
        Replace(UnitOverviewCards, BuildCards(result.Appointments, result.Waiting));
        Replace(UnitLightningCards, BuildLightningCards(result.Lightning));
        Replace(UnitSpecialtyPerformance, result.BySpecialty.Select(item => new PerformanceRow(item.Specialty, "", item.AttendanceScore, item.Completed, item.Missed, item.Cancelled, item.ActiveEntries, item.AverageWaitMinutes))
            .OrderBy(row => row.Name, StringComparer.CurrentCultureIgnoreCase).ToArray());
        var sparse = SparseDays(result.Daily);
        UnitChart.Apply(BuildChartDays(sparse, request.From, request.To), sparse, result.Daily.Length == 0 ? "Nenhuma consulta com data marcada no período." : "");
        ApplyUnitCapacityAndGoals(result);
        NotifyUnitMetrics();
    }

    private static MetricCard[] BuildLightningCards(LightningMetrics? lightning)
    {
        static string Number(long? value) => value is { } number ? DisplayFormat.Count(number) : "—";
        var hint = lightning is null ? "Aguardando os indicadores" : "Vagas criadas no período";
        return
        [
            new("Vagas registradas", Number(lightning?.RegisteredSlots), hint),
            new("Ofertas aceitas", Number(lightning?.AcceptedOffers), lightning is null ? hint : "Aceite não confirma o atendimento"),
            new("Ofertas recusadas", Number(lightning?.DeclinedOffers), hint),
            new("Ofertas expiradas", Number(lightning?.ExpiredOffers), lightning is null ? hint : "Sem resposta dentro do prazo")
        ];
    }

    /// <summary>
    /// Leitura tolerante do Painel: se só os indicadores falharem (por exemplo, base sem a migração mais nova), a UBS ainda abre e o cartão avisa.
    /// Sessão encerrada e troca obrigatória de senha continuam subindo como erro.
    /// </summary>
    private async Task<UnitMetricsRead> ReadPanelMetricsAsync(HealthUnit unit, UnitMetricsRequest request)
    {
        try { return new(await api.UnitMetricsAsync(unit.Id, request.From, request.To), request); }
        catch (NoraApiException error) when (!error.Unauthorized && error.Code != "password-change-required") { return new(null, request, true); }
    }

    private static async Task<UnitMetricsRead> ShareAsync(Task<UnitMetrics> read, UnitMetricsRequest request) => new(await read, request);
    private static async Task<UnitMetricsRead> SharePanelAsync(Task<UnitMetricsRead> shared, UnitMetricsRequest request) => (await shared) with { Request = request };

    /// <summary>Guarda os últimos 30 dias do Painel e a série dos últimos 7 (tirada da mesma resposta, sem outra requisição).</summary>
    private void ApplyPanelMetrics(UnitMetricsRead read)
    {
        if (read.Data is not { } data) { panelFailed = true; NotifyUnitMetrics(); return; }
        var unchanged = panelRequest == read.Request && panelMetrics is not null && JsonSerializer.Serialize(panelMetrics) == JsonSerializer.Serialize(data);
        panelFailed = false;
        if (unchanged) { NotifyUnitMetrics(); return; }
        panelMetrics = data; panelRequest = read.Request;
        var last = DateOnly.FromDateTime(read.Request.To.LocalDateTime.AddDays(-1)); var first = last.AddDays(1 - PanelChartDays);
        var sparse = SparseDays(data.Daily).Where(point => point.Date >= first && point.Date <= last).ToArray();
        PanelChart.Apply(ChartSeries.Dense(sparse, first, last), sparse, sparse.Length == 0 ? $"Nenhuma consulta com data marcada nos últimos {PanelChartDays} dias." : "");
        NotifyUnitMetrics();
    }

    private void ClearUnitMetrics()
    {
        Interlocked.Exchange(ref unitMetricsCts, null)?.Cancel();
        unitMetricsVersion++;
        unitMetrics = null; appliedUnitMetrics = null; appliedUnitRange = null; unitMetricsFailed = false; unitMetricsAttemptedAt = null;
        panelMetrics = null; panelRequest = null; panelFailed = false; panelAttemptedAt = null;
        unitPeriodDays = 30; unitFromText = unitToText = unitRangeError = "";
        Replace(UnitOverviewCards, BuildCards(null, null)); Replace(UnitLightningCards, BuildLightningCards(null)); Replace(UnitSpecialtyPerformance, Array.Empty<PerformanceRow>());
        UnitChart.Clear("Sem indicadores carregados."); PanelChart.Clear("Sem indicadores carregados.");
        foreach (var name in new[] { nameof(UnitPeriodDays), nameof(IsUnitCustomPeriod), nameof(UnitFromText), nameof(UnitToText), nameof(UnitRangeError), nameof(HasUnitRangeError) }) Notify(name);
        NotifyUnitMetrics();
    }
}
