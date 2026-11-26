namespace Nora.Desktop.ViewModels;

public sealed partial class WorkspaceViewModel
{
    public async Task ClearPageFiltersAsync(IReadOnlySet<string> filters)
    {
        if (filters.Contains("patients"))
        {
            ObsRegistrySearch = "";
            ObsRegistryStatusFilter = ObsRegistryPresenceFilter = "Todos";
            ObsRegistrySort = "Mais recentes";
        }
        if (filters.Contains("units")) UnitSearch = "";
        if (filters.Contains("unitMetrics"))
        {
            UnitFromText = UnitToText = UnitSpecialtySearch = "";
            UnitSpecialtyFilter = UnitSpecialtyFilterOptions[0];
            UnitPeriodDays = 30;
        }
        // A pesquisa em linha das telas de números da rede também é apagada por "Limpar filtros" (como a das listas).
        if (filters.Any(key => key.StartsWith("network", StringComparison.Ordinal))) NetworkSearch = "";
        var clearNetworkMetrics = filters.Contains("networkMetrics");
        var clearNetworkScope = clearNetworkMetrics || filters.Contains("networkLightning") || filters.Contains("networkOverview");
        var refreshNetwork = clearNetworkScope && summaryUnitFilter != Guid.Empty;
        if (refreshNetwork)
        {
            summaryUnitFilter = Guid.Empty;
            Notify(nameof(SummaryUnitFilter));
        }
        if (clearNetworkMetrics)
        {
            var hadFilters = metricsPeriodDays != 30 || metricsRegion.Length > 0 || metricsSpecialty.Length > 0;
            metricsPeriodDays = 30;
            metricsRegion = metricsSpecialty = "";
            MetricsFromText = MetricsToText = "";
            Notify(nameof(MetricsPeriodDays)); Notify(nameof(IsCustomPeriod)); Notify(nameof(PeriodSummaryTitle)); Notify(nameof(MetricsRegion)); Notify(nameof(MetricsSpecialty));
            if (hadFilters || refreshNetwork) ReloadNetwork(includeSummary: refreshNetwork);
        }
        else if (refreshNetwork) ReloadNetwork(includeSummary: true);

        if (filters.Contains("networkLightning")) LightningPeriodDays = 0;
        if (filters.Contains("networkAlerts") && (alertsUnitFilter != Guid.Empty || alertsRegion.Length > 0))
        {
            alertsUnitFilter = Guid.Empty;
            alertsRegion = "";
            Notify(nameof(AlertsUnitFilter)); Notify(nameof(AlertsRegion));
            ReloadNetworkAlerts();
        }
        if (filters.Contains("municipalAgenda"))
        {
            ResetAgendaRange();
            RefreshVisibleAppointments();
            if (MunicipalAppointmentsLoaded && CanReadUnitRegistry) await LoadMunicipalAppointmentsAsync();
        }
        if (filters.Contains("history"))
        {
            HistoryFromText = HistoryToText = "";
            HistoryUnitFilter = Guid.Empty;
            if (historyLoaded && CanQueryHistory) await LoadHistoryAsync();
        }
    }
}
