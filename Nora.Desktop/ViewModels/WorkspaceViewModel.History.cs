using System.Collections.ObjectModel;
using Nora.Desktop.Models;
using Nora.Desktop.Services;

namespace Nora.Desktop.ViewModels;

/// <summary>Histórico de ações da gestão (GET /api/municipal/history): páginas de 100, mais recentes primeiro, "carregar mais" pelo cursor que a API devolve em cada evento.</summary>
public sealed partial class WorkspaceViewModel
{
    private const int HistoryPageSize = 100;
    private string historyFromText = "", historyToText = "", historyRangeError = "";
    private Guid historyUnitFilter = Guid.Empty;
    private (DateTimeOffset? From, DateTimeOffset? To, Guid? Unit) appliedHistory;
    private bool historyLoaded, historyLoading, historyHasMore;
    private int historyVersion;
    public ObservableCollection<HistoryEvent> HistoryEvents { get; } = [];

    public string HistoryFromText { get => historyFromText; set { historyFromText = value ?? ""; Notify(); ClearHistoryRangeError(); } }
    public string HistoryToText { get => historyToText; set { historyToText = value ?? ""; Notify(); ClearHistoryRangeError(); } }
    public Guid HistoryUnitFilter { get => historyUnitFilter; set { historyUnitFilter = value; Notify(); } }
    public string HistoryRangeError { get => historyRangeError; private set { if (historyRangeError == value) return; historyRangeError = value; Notify(); Notify(nameof(HasHistoryRangeError)); } }
    public bool HasHistoryRangeError => historyRangeError.Length > 0;
    private void ClearHistoryRangeError() { if (historyRangeError.Length > 0) HistoryRangeError = ""; }
    public bool CanLoadMoreHistory => IsMunicipal && CanOperate && historyHasMore;
    public bool CanQueryHistory => IsMunicipal && CanOperate;
    public string HistoryState => !IsAuthenticated ? "Entre na Secretaria para consultar o histórico."
        : !historyLoaded ? (historyLoading ? "Carregando o histórico…" : "O histórico ainda não foi carregado.")
        : HistoryEvents.Count == 0 ? "Nenhuma ação registrada para este período e esta UBS." : "";
    /// <summary>Rodapé da listagem: quantas ações aparecem e se há mais antigas (vazio sem ações).</summary>
    public string HistoryFooter => !historyLoaded || HistoryEvents.Count == 0 ? ""
        : historyHasMore ? $"{HistoryEvents.Count} ações mostradas. Há ações mais antigas: use Carregar mais." : $"{HistoryEvents.Count} ações no total para este filtro.";

    /// <summary>Datas opcionais e inclusivas (dd/mm/aaaa): "Até" vai até a meia-noite seguinte.</summary>
    private bool TryReadHistoryFilter(out (DateTimeOffset? From, DateTimeOffset? To, Guid? Unit) filter)
    {
        filter = default;
        DateTime from = default, to = default;
        var hasFrom = historyFromText.Trim().Length > 0; var hasTo = historyToText.Trim().Length > 0;
        if ((hasFrom && !DisplayFormat.TryParseDate(historyFromText, out from)) || (hasTo && !DisplayFormat.TryParseDate(historyToText, out to))) { HistoryRangeError = "Informe as datas no formato dd/mm/aaaa."; return false; }
        if (hasFrom && hasTo && from > to) { HistoryRangeError = "A data inicial não pode ser posterior à data final."; return false; }
        HistoryRangeError = "";
        filter = (hasFrom ? new DateTimeOffset(from) : null, hasTo ? new DateTimeOffset(to.AddDays(1)) : null, historyUnitFilter == Guid.Empty ? null : historyUnitFilter);
        return true;
    }

    /// <summary>Primeira página com o filtro digitado (reinicia a lista) ou a página seguinte do filtro já aplicado.</summary>
    private async Task LoadHistoryPageAsync(bool reset)
    {
        if (!IsMunicipal || !IsAuthenticated) throw new NoraApiException("Entre na Secretaria para consultar o histórico.");
        if (reset) { if (!TryReadHistoryFilter(out var filter)) return; appliedHistory = filter; }
        // A página seguinte parte do cursor opaco do último item: eventos que compartilham o mesmo instante não se perdem. Sem cursor (API antiga), cai no instante.
        var before = reset || HistoryEvents.Count == 0 ? null : HistoryEvents[^1].Cursor ?? HistoryEvents[^1].OccurredAt.ToString("O");
        var version = ++historyVersion;
        historyLoading = true; Notify(nameof(HistoryState));
        try
        {
            var page = await api.HistoryAsync(appliedHistory.From, appliedHistory.To, appliedHistory.Unit, HistoryPageSize, before);
            if (disposed || !IsAuthenticated || version != historyVersion) return;
            if (reset) Replace(HistoryEvents, page);
            else foreach (var item in page) HistoryEvents.Add(item);
            historyLoaded = true; historyHasMore = page.Length >= HistoryPageSize;
        }
        finally { historyLoading = false; if (!disposed) { Notify(nameof(HistoryState)); Notify(nameof(HistoryFooter)); Notify(nameof(CanLoadMoreHistory)); } }
    }

    public Task LoadHistoryAsync() => RunAsync(async () =>
    {
        await LoadHistoryPageAsync(reset: true);
        if (historyLoaded && !HasHistoryRangeError) Say(MessageKind.Success, "Histórico atualizado.");
    });
    public Task LoadMoreHistoryAsync() => RunAsync(() => LoadHistoryPageAsync(reset: false));

    /// <summary>
    /// Leitura do ciclo de 20 s: relê a primeira página com o filtro já aplicado, para as ações novas aparecerem sozinhas. Quando a pessoa já carregou páginas mais antigas
    /// ("Carregar mais"), o ciclo não mexe na lista (as ações mais recentes entram quando ela refaz a consulta).
    /// </summary>
    private async Task RefreshHistoryFirstPageAsync()
    {
        if (!historyLoaded || HistoryEvents.Count > HistoryPageSize) return;
        var version = ++historyVersion;
        var page = await api.HistoryAsync(appliedHistory.From, appliedHistory.To, appliedHistory.Unit, HistoryPageSize, null);
        if (disposed || !IsAuthenticated || version != historyVersion) return;
        Replace(HistoryEvents, page);
        historyHasMore = page.Length >= HistoryPageSize;
        Notify(nameof(HistoryState)); Notify(nameof(HistoryFooter)); Notify(nameof(CanLoadMoreHistory));
    }

    /// <summary>Leitura da seção Usuários no ciclo de 20 s: contas (com as UBS de cada uma, relidas a cada 5 minutos) e a primeira página do histórico. As UBS da rede não entram: vêm do login e da seção Unidades.</summary>
    private async Task LoadUsersSectionAsync()
    {
        await ReloadStaffAsync();
        await RefreshHistoryFirstPageAsync();
    }

    private void ClearHistory()
    {
        historyVersion++; HistoryEvents.Clear(); historyLoaded = false; historyLoading = false; historyHasMore = false; appliedHistory = default;
        historyFromText = historyToText = ""; historyRangeError = ""; historyUnitFilter = Guid.Empty;
        foreach (var name in new[] { nameof(HistoryFromText), nameof(HistoryToText), nameof(HistoryUnitFilter), nameof(HasHistoryRangeError), nameof(HistoryState), nameof(HistoryFooter), nameof(CanLoadMoreHistory) }) Notify(name);
    }
}
