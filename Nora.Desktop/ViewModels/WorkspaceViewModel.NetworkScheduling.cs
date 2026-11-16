using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.RegularExpressions;
using Nora.Desktop.Generics;
using Nora.Desktop.Models;
using Nora.Desktop.Services;

namespace Nora.Desktop.ViewModels;

/// <summary>Uma meta editável (comparecimento, faltas ou espera média): valor digitado, meta gravada no escopo escolhido e o que a UBS herda da rede.</summary>
public sealed class GoalEditor : ObservableObject
{
    private string input = "", waitUnit = "h", current = "", inherited = "", error = "";
    public GoalEditor(string metric) { Metric = metric; Title = GoalMetric.Title(metric); }
    public string Metric { get; }
    public string Title { get; }
    public bool IsWait => Metric == GoalMetric.AverageWait;
    public string Bound => GoalMetric.Bound(Metric) == "mín." ? "Mínimo" : "Máximo";
    public string Hint => Metric switch
    {
        GoalMetric.Score => "Mínimo desejado para o comparecimento, de 0 a 100%.",
        GoalMetric.MissedRate => "Máximo aceito para faltas ÷ (concluídas + faltas), de 0 a 100%.",
        _ => "Máximo aceito para a espera média da fila, em horas ou dias."
    };
    public string Suffix => IsWait ? "" : "%";
    public string Input { get => input; set { input = value ?? ""; error = ""; Notify(); Notify(nameof(Error)); } }
    public string WaitUnit { get => waitUnit; set { waitUnit = value ?? "h"; error = ""; Notify(); Notify(nameof(Error)); } }
    /// <summary>Meta gravada neste escopo (ou "Sem meta definida aqui").</summary>
    public string Current { get => current; set { current = value; Notify(); Notify(nameof(HasCurrent)); } }
    public bool HasCurrent { get; set; }
    /// <summary>Numa UBS sem sobrescrita: o que vale é a meta da rede.</summary>
    public string Inherited { get => inherited; set { inherited = value; Notify(); } }
    public string Error { get => error; set { error = value; Notify(); } }
    public string AutomationLabel => $"{Title}. {Current}. {Inherited}".Trim();
    public void Refresh() { Notify(nameof(HasCurrent)); Notify(nameof(AutomationLabel)); }

    // O que foi preenchido a partir da meta gravada: serve para saber se a pessoa digitou algo (não perder o rascunho numa releitura)
    // e para regravar o valor exato quando ela salva sem mexer no campo (a tela mostra o valor arredondado).
    public string BaselineInput { get; private set; } = "";
    public string BaselineUnit { get; private set; } = "h";
    public decimal? BaselineTarget { get; private set; }
    public bool IsDirty => input != BaselineInput || (IsWait && waitUnit != BaselineUnit);
    public void SetBaseline(decimal? target) { BaselineInput = input; BaselineUnit = waitUnit; BaselineTarget = target; }
}

/// <summary>
/// Secretaria: lista de especialidades da rede (criar, renomear, ativar e desativar), metas da rede e por UBS com a situação atual, capacidade por UBS e região,
/// alertas da rede com filtros e leitura do catálogo de uma UBS (profissionais, consultórios e grade).
/// </summary>
public sealed partial class WorkspaceViewModel
{
    private static readonly Regex Spaces = new(@"\s+", RegexOptions.Compiled);

    // ---- Especialidades ----

    private bool specialtyCatalogLoaded, specialtyDraftSubmitted;
    private Specialty? selectedSpecialty;
    private Guid? editingSpecialtyId;
    private string specialtyDraftName = "", specialtyServerError = "";
    public ObservableCollection<Specialty> SpecialtyCatalog { get; } = [];
    public Specialty? SelectedSpecialty { get => selectedSpecialty; set { selectedSpecialty = value; Notify(); Notify(nameof(HasSelectedSpecialty)); Notify(nameof(SpecialtyActiveLabel)); Notify(nameof(CanChangeSpecialty)); } }
    public bool HasSelectedSpecialty => selectedSpecialty is not null;
    public bool CanChangeSpecialty => IsMunicipal && CanOperate && HasSelectedSpecialty;
    public bool CanSaveSpecialty => IsMunicipal && CanOperate;
    public string SpecialtyActiveLabel => selectedSpecialty is { IsActive: false } ? "Reativar especialidade" : "Desativar especialidade";
    public string SpecialtyDraftTitle => editingSpecialtyId is null ? "Cadastrar especialidade" : "Renomear especialidade";
    public string SpecialtySaveLabel => editingSpecialtyId is null ? "Cadastrar especialidade" : "Salvar novo nome";
    public string SpecialtyDraftName { get => specialtyDraftName; set { specialtyDraftName = value; specialtyServerError = ""; Notify(); Notify(nameof(SpecialtyNameError)); } }
    public string SpecialtyNameError => specialtyServerError.Length > 0 ? specialtyServerError
        : (specialtyDraftSubmitted || specialtyDraftName.Length > 0) && (Spaces.Replace(specialtyDraftName.Trim(), " ").Length is < 2 or > 120 || specialtyDraftName.Any(char.IsControl)) ? "Informe o nome da especialidade, de 2 a 120 caracteres." : "";
    public string SpecialtyCatalogState => !IsAuthenticated ? "Entre na Secretaria para gerenciar as especialidades."
        : !specialtyCatalogLoaded ? "Carregando as especialidades…"
        : SpecialtyCatalog.Count == 0 ? "Nenhuma especialidade cadastrada. Cadastre a primeira acima: as UBS escolhem dessa lista ao cadastrar profissionais." : "";

    private async Task LoadSpecialtyCatalogCoreAsync()
    {
        var list = await api.MunicipalSpecialtiesAsync();
        if (disposed || !IsAuthenticated) return;
        ApplySpecialtyCatalog(list);
    }
    private void ApplySpecialtyCatalog(Specialty[] list)
    {
        var selectedId = SelectedSpecialty?.Id;
        Replace(SpecialtyCatalog, list.OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase).ToArray());
        specialtyCatalogLoaded = true;
        if (selectedId is { } id) SelectedSpecialty = SpecialtyCatalog.FirstOrDefault(item => item.Id == id);
        Notify(nameof(SpecialtyCatalogState));
        DecorateCapacityCatalog();
    }
    public Task LoadSpecialtyCatalogAsync() => RunAsync(async () =>
    {
        if (!IsMunicipal || !IsAuthenticated) throw new NoraApiException("Entre na Secretaria para consultar as especialidades.");
        await LoadSpecialtyCatalogCoreAsync();
        Say(MessageKind.Success, "Especialidades atualizadas.");
    });
    public void NewSpecialtyDraft()
    {
        editingSpecialtyId = null; specialtyDraftSubmitted = false; specialtyDraftName = specialtyServerError = "";
        foreach (var name in new[] { nameof(SpecialtyDraftName), nameof(SpecialtyNameError), nameof(SpecialtyDraftTitle), nameof(SpecialtySaveLabel) }) Notify(name);
    }
    public void EditSelectedSpecialty()
    {
        if (SelectedSpecialty is not { } item) return;
        NewSpecialtyDraft(); editingSpecialtyId = item.Id; specialtyDraftName = item.Name;
        foreach (var name in new[] { nameof(SpecialtyDraftName), nameof(SpecialtyDraftTitle), nameof(SpecialtySaveLabel) }) Notify(name);
    }
    public Task SaveSpecialtyAsync() => RunAsync(async () =>
    {
        if (!IsMunicipal || !IsAuthenticated) throw new NoraApiException("Entre na Secretaria para cadastrar especialidades.");
        specialtyDraftSubmitted = true; specialtyServerError = ""; Notify(nameof(SpecialtyNameError));
        if (SpecialtyNameError.Length > 0) throw new NoraApiException("Corrija o nome da especialidade.");
        var name = Spaces.Replace(specialtyDraftName.Trim(), " ");
        var editing = editingSpecialtyId;
        try { if (editing is { } id) await api.RenameSpecialtyAsync(id, name); else await api.CreateSpecialtyAsync(name); }
        catch (NoraApiException error) when (error.Status == 409) { specialtyServerError = error.Message; Notify(nameof(SpecialtyNameError)); throw; }
        NewSpecialtyDraft();
        Say(MessageKind.Success, editing is null ? $"Especialidade {name} cadastrada." : $"Especialidade renomeada para {name}.");
        await LoadSpecialtyCatalogCoreAsync();
    });
    public Task ChangeSpecialtyActiveAsync() => RunAsync(async () =>
    {
        var item = SelectedSpecialty ?? throw new NoraApiException("Selecione uma especialidade.");
        var activate = !item.IsActive;
        await api.SetSpecialtyActiveAsync(item.Id, activate);
        Say(MessageKind.Success, activate ? $"Especialidade {item.Name} reativada." : $"Especialidade {item.Name} desativada: ela sai da lista do paciente e de novos vínculos e horários; o que já existe foi preservado.");
        await LoadSpecialtyCatalogCoreAsync();
    });

    // ---- Metas ----

    private bool goalsLoaded;
    private Goal[] goals = [];
    private Guid goalScope = Guid.Empty;
    public ObservableCollection<GoalEditor> GoalEditors { get; } = [.. GoalMetric.All.Select(metric => new GoalEditor(metric))];
    public ObservableCollection<GoalUnitRow> GoalUnitRows { get; } = [];
    /// <summary>"Toda a rede" ou a UBS que recebe a sobrescrita (mesma lista de UBS dos filtros).</summary>
    public Guid GoalScope { get => goalScope; set { if (goalScope == value) return; goalScope = value; Notify(); RefreshGoalEditors(true); Notify(nameof(GoalScopeLabel)); } }
    public string GoalScopeLabel => goalScope == Guid.Empty ? "Metas da rede: valem para toda UBS que não tem meta própria." : $"Metas de {Units.FirstOrDefault(unit => unit.Id == goalScope)?.Name ?? "UBS selecionada"}: sobrescrevem a meta da rede só nesta UBS.";
    public bool CanEditGoals => IsMunicipal && CanOperate;
    public string GoalsState => !IsAuthenticated ? "Entre na Secretaria para definir metas."
        : !goalsLoaded ? "Carregando as metas…"
        : goals.Length == 0 ? "Nenhuma meta definida ainda: sem meta, os indicadores aparecem como \"Sem meta\"." : "";
    public string GoalStatusState => !IsAuthenticated ? "" : metrics is null ? MetricsStateText : GoalUnitRows.Count == 0 ? "Nenhuma UBS neste filtro." : "";

    private async Task LoadGoalsCoreAsync(string? changedMetric = null)
    {
        var list = await api.GoalsAsync();
        if (disposed || !IsAuthenticated) return;
        goals = list; goalsLoaded = true;
        RefreshGoalEditors(false, changedMetric);
        Notify(nameof(GoalsState));
    }
    public Task LoadGoalsAsync() => RunAsync(async () =>
    {
        if (!IsMunicipal || !IsAuthenticated) throw new NoraApiException("Entre na Secretaria para consultar as metas.");
        await LoadGoalsCoreAsync();
        Say(MessageKind.Success, "Metas atualizadas.");
    });

    private static string FormatGoalInput(string metric, decimal target)
    {
        if (metric != GoalMetric.AverageWait) return (target * 100m).ToString("0.##", DisplayFormat.Culture);
        var minutes = (double)target;
        // Em horas com até 4 casas (0,006 min): digitar o que a tela mostra e salvar devolve o mesmo valor gravado (2 casas em minutos).
        return minutes % 1440 == 0 ? (minutes / 1440).ToString("0.##", DisplayFormat.Culture) : (minutes / 60).ToString("0.####", DisplayFormat.Culture);
    }

    /// <summary>
    /// Preenche cada meta com o que está gravado no escopo escolhido. O que a pessoa digitou e ainda não salvou só é trocado quando o escopo mudou
    /// ou quando é a meta que acabou de ser salva ou removida (<paramref name="changedMetric"/>).
    /// </summary>
    private void RefreshGoalEditors(bool scopeChanged, string? changedMetric = null)
    {
        var scope = goalScope == Guid.Empty ? (Guid?)null : goalScope;
        foreach (var editor in GoalEditors)
        {
            var own = goals.FirstOrDefault(goal => goal.Metric == editor.Metric && goal.UnitId == scope);
            var network = scope is null ? null : goals.FirstOrDefault(goal => goal.Metric == editor.Metric && goal.UnitId is null);
            editor.HasCurrent = own is not null;
            editor.Current = own is null ? "Sem meta definida aqui." : $"Meta atual: {GoalMetric.Bound(editor.Metric)} {GoalMetric.Format(editor.Metric, (double)own.Target)}";
            editor.Inherited = scope is null ? "" : own is not null ? (network is null ? "A rede não tem meta para esta métrica." : $"A meta da rede é {GoalMetric.Bound(editor.Metric)} {GoalMetric.Format(editor.Metric, (double)network.Target)}.")
                : network is null ? "Esta UBS ainda não tem meta, nem herdada da rede." : $"Esta UBS herda a meta da rede: {GoalMetric.Bound(editor.Metric)} {GoalMetric.Format(editor.Metric, (double)network.Target)}.";
            if (scopeChanged || editor.Metric == changedMetric || !editor.IsDirty)
            {
                editor.Input = own is null ? "" : FormatGoalInput(editor.Metric, own.Target);
                if (own is not null && editor.IsWait) editor.WaitUnit = (double)own.Target % 1440 == 0 ? "d" : "h";
                editor.SetBaseline(own?.Target);
                editor.Error = "";
            }
            editor.Refresh();
        }
    }

    /// <summary>Converte o valor digitado para o que a API grava: fração (0 a 1) para comparecimento e faltas, minutos para a espera. Devolve a mensagem de erro ou nulo.</summary>
    private static string? ParseGoalTarget(GoalEditor editor, out decimal target)
    {
        target = 0;
        if (!double.TryParse(editor.Input.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || !double.IsFinite(value)) return "Informe um número, por exemplo 80 ou 1,5.";
        if (editor.IsWait)
        {
            var minutes = value * (editor.WaitUnit == "d" ? 1440 : 60);
            if (minutes <= 0) return "A espera máxima precisa ser maior que zero.";
            if (minutes > 366 * 1440) return "A espera máxima pode ter no máximo 366 dias.";
            target = Math.Round((decimal)minutes, 2);
            return null;
        }
        if (value is < 0 or > 100) return "Informe um percentual de 0 a 100.";
        target = Math.Round((decimal)value / 100m, 4);
        return null;
    }

    public Task SaveGoalAsync(string metric) => RunAsync(async () =>
    {
        if (!IsMunicipal || !IsAuthenticated) throw new NoraApiException("Entre na Secretaria para definir metas.");
        var editor = GoalEditors.First(item => item.Metric == metric);
        // Sem mexer no campo, a meta gravada segue exatamente como está (o texto mostrado pode estar arredondado).
        var target = editor.BaselineTarget ?? 0m;
        var problem = !editor.IsDirty && editor.BaselineTarget is not null ? null : ParseGoalTarget(editor, out target);
        if (problem is not null) { editor.Error = problem; throw new NoraApiException("Corrija o valor da meta destacado."); }
        var scope = goalScope == Guid.Empty ? (Guid?)null : goalScope;
        await api.SetGoalAsync(metric, target, scope);
        Say(MessageKind.Success, $"Meta de {editor.Title.ToLowerInvariant()} gravada para {(scope is null ? "a rede" : Units.FirstOrDefault(unit => unit.Id == scope)?.Name ?? "a UBS")}.");
        await LoadGoalsCoreAsync(metric);
        ReloadNetwork(includeSummary: false);
    });

    public Task RemoveGoalAsync(string metric) => RunAsync(async () =>
    {
        if (!IsMunicipal || !IsAuthenticated) throw new NoraApiException("Entre na Secretaria para remover metas.");
        var editor = GoalEditors.First(item => item.Metric == metric);
        var scope = goalScope == Guid.Empty ? (Guid?)null : goalScope;
        await api.RemoveGoalAsync(metric, scope);
        editor.Input = "";
        Say(MessageKind.Success, $"Meta de {editor.Title.ToLowerInvariant()} removida{(scope is null ? " da rede" : " desta UBS")}.");
        await LoadGoalsCoreAsync(metric);
        ReloadNetwork(includeSummary: false);
    });

    // ---- Situação das metas e capacidade, lidas dos indicadores da rede ----

    public ObservableCollection<MetricCard> NetworkCapacityCards { get; } = [];
    public ObservableCollection<CapacityRow> CapacityByUnit { get; } = [];
    public ObservableCollection<CapacityRow> CapacityByRegion { get; } = [];
    public string CapacityState => !IsAuthenticated ? "Entre na Secretaria para ver a capacidade." : metrics is null ? MetricsStateText
        : CapacityByUnit.Count == 0 ? "Nenhuma UBS com base pronta neste filtro." : "";

    private void ApplyNetworkScheduling(NetworkMetrics result)
    {
        Replace(NetworkCapacityCards, BuildCapacityCards(result.Capacity, "Horários da grade com início no período"));
        Replace(CapacityByUnit, result.ByUnit.Select(unit => new CapacityRow(unit.Name, unit.Region, unit.Capacity?.OfferedSlots ?? 0, unit.Capacity?.BookedSlots ?? 0, unit.Capacity?.Occupancy)).OrderBy(row => row.Name, StringComparer.CurrentCultureIgnoreCase).ToArray());
        Replace(CapacityByRegion, result.ByRegion.Select(region => new CapacityRow(region.Region, "", region.Capacity?.OfferedSlots ?? 0, region.Capacity?.BookedSlots ?? 0, region.Capacity?.Occupancy)).OrderBy(row => row.Name, StringComparer.CurrentCultureIgnoreCase).ToArray());
        static GoalUnitRow Row(string name, IEnumerable<GoalStatus>? statuses) { var cells = GoalCell.ForAll(statuses); return new(name, cells[0], cells[1], cells[2]); }
        Replace(GoalUnitRows, new[] { Row("Toda a rede (no filtro)", result.Goals) }.Concat(result.ByUnit.Select(unit => Row(unit.Name, unit.Goals)).OrderBy(row => row.Name, StringComparer.CurrentCultureIgnoreCase)).ToArray());
        Notify(nameof(CapacityState)); Notify(nameof(GoalStatusState));
    }

    private void ClearNetworkScheduling()
    {
        Replace(NetworkCapacityCards, BuildCapacityCards(null, "")); Replace(CapacityByUnit, Array.Empty<CapacityRow>()); Replace(CapacityByRegion, Array.Empty<CapacityRow>()); Replace(GoalUnitRows, Array.Empty<GoalUnitRow>());
        Notify(nameof(CapacityState)); Notify(nameof(GoalStatusState));
    }

    // ---- Alertas da rede ----

    private Guid alertsUnitFilter = Guid.Empty;
    private string alertsRegion = "";
    private bool networkAlertsLoaded, networkAlertsFailed;
    private int networkAlertsLoading, networkAlertsVersion;
    private CancellationTokenSource? networkAlertsCts;
    private DateTimeOffset? networkAlertsAttemptedAt, networkAlertsReadAt;
    // UBS do filtro no momento da leitura: a API só avalia as de base pronta; as outras ficam de fora e não podem virar "nenhum critério passou".
    private int networkAlertsEvaluatedUnits, networkAlertsSkippedUnits;
    public ObservableCollection<AlertRow> NetworkAlerts { get; } = [];
    public Guid AlertsUnitFilter { get => alertsUnitFilter; set { if (alertsUnitFilter == value) return; alertsUnitFilter = value; Notify(); ReloadNetworkAlerts(); } }
    public string AlertsRegion { get => alertsRegion; set { value ??= ""; if (alertsRegion == value) return; alertsRegion = value; Notify(); ReloadNetworkAlerts(); } }
    public bool CanFilterAlerts => IsAuthenticated && IsMunicipal;
    public string NetworkAlertsLoadingLabel => networkAlertsLoading > 0 ? "Avaliando os alertas…" : "";
    public string NetworkAlertsState => !IsAuthenticated ? "Entre na Secretaria para consultar os alertas."
        : networkAlertsFailed && networkAlertsReadAt is null ? "Não foi possível avaliar os alertas da rede agora. Use Atualizar alertas para tentar de novo."
        : networkAlertsLoaded is false ? (networkAlertsLoading > 0 ? "Avaliando os alertas…" : "Os alertas ainda não foram avaliados.")
        : NetworkAlerts.Count == 0 && networkAlertsFailed ? $"A última avaliação não concluiu. Na avaliação anterior, das {networkAlertsReadAt:HH:mm}, não havia alerta neste filtro. Use Atualizar alertas para tentar de novo."
        : NetworkAlerts.Count == 0 && networkAlertsEvaluatedUnits == 0 && networkAlertsSkippedUnits > 0 ? $"Os alertas deste filtro não foram avaliados: {(networkAlertsSkippedUnits == 1 ? "a UBS ainda não tem" : "nenhuma das UBS tem")} base pronta (leitura das {networkAlertsReadAt:HH:mm})."
        : NetworkAlerts.Count == 0 ? $"Nenhum alerta neste filtro. Nenhum critério passou do limite na leitura das {networkAlertsReadAt:HH:mm}.{SkippedUnitsNote}"
        : networkAlertsFailed ? $"A última avaliação não concluiu; estes são os alertas da avaliação anterior, das {networkAlertsReadAt:HH:mm}." : "";
    private string SkippedUnitsNote => networkAlertsSkippedUnits switch { 0 => "", 1 => " 1 UBS sem base pronta ficou de fora.", var count => $" {count} UBS sem base pronta ficaram de fora." };
    public string NetworkAlertsFootnote => NetworkAlerts.Count == 0 ? "" : $"{NetworkAlerts.Count} {(NetworkAlerts.Count == 1 ? "alerta" : "alertas")} · avaliado às {networkAlertsReadAt:HH:mm}. O \"desde\" só existe para a espera longa; os demais mostram a situação atual. UBS sem base pronta ficam de fora.";
    private bool NetworkAlertsDue => networkAlertsAttemptedAt is null || DateTimeOffset.Now - networkAlertsAttemptedAt >= AlertsRefreshAge;
    private static readonly string[] NetworkAlertNames = [nameof(NetworkAlertsLoadingLabel), nameof(NetworkAlertsState), nameof(NetworkAlertsFootnote), nameof(CanFilterAlerts)];
    private void NotifyNetworkAlerts() { foreach (var name in NetworkAlertNames) Notify(name); }

    /// <summary>Leitura direta (carga da seção e atualização manual); sem filtro novo.</summary>
    private async Task LoadNetworkAlertsCoreAsync(CancellationToken cancellation = default)
    {
        networkAlertsAttemptedAt = DateTimeOffset.Now;
        var request = (Unit: alertsUnitFilter == Guid.Empty ? (Guid?)null : alertsUnitFilter, Region: alertsRegion);
        AlertItem[] alerts;
        // Falha de leitura nunca vira "nenhum alerta": o estado de falha fica registrado mesmo quando a leitura vem do botão Atualizar dados.
        try { alerts = await api.NetworkAlertsAsync(request.Unit, request.Region, cancellation); }
        catch (NoraApiException) { networkAlertsFailed = true; NotifyNetworkAlerts(); throw; }
        if (disposed || !IsAuthenticated || cancellation.IsCancellationRequested || request.Unit != (alertsUnitFilter == Guid.Empty ? (Guid?)null : alertsUnitFilter) || request.Region != alertsRegion) return;
        networkAlertsFailed = false; networkAlertsLoaded = true; networkAlertsReadAt = DateTimeOffset.Now;
        // Mesmo filtro da API (AlertService.ReadNetworkAsync): UBS escolhida e região sem diferenciar maiúsculas; só "ready" é avaliada.
        var region = request.Region.Trim(); networkAlertsEvaluatedUnits = networkAlertsSkippedUnits = 0;
        foreach (var unit in Units)
        {
            if ((request.Unit is { } only && unit.Id != only) || (region.Length > 0 && !string.Equals(unit.Region, region, StringComparison.OrdinalIgnoreCase))) continue;
            if (unit.DatabaseState is null or "ready") networkAlertsEvaluatedUnits++; else networkAlertsSkippedUnits++;
        }
        Replace(NetworkAlerts, alerts.Select(AlertRow.From).ToArray());
        NotifyNetworkAlerts();
    }

    /// <summary>Avalia os alertas da rede de novo com o filtro atual (botão Atualizar alertas).</summary>
    public Task RefreshNetworkAlertsAsync() => RunAsync(async () =>
    {
        if (!IsMunicipal || !IsAuthenticated) throw new NoraApiException("Entre na Secretaria para consultar os alertas.");
        await LoadNetworkAlertsCoreAsync();
        if (!networkAlertsFailed) Say(MessageKind.Success, NetworkAlerts.Count == 0 ? "Alertas avaliados: nenhum alerta neste filtro." : $"Alertas avaliados: {NetworkAlerts.Count}.");
    });

    /// <summary>Uma requisição por mudança de filtro: a anterior é cancelada e só a mais nova altera a tela.</summary>
    private void ReloadNetworkAlerts()
    {
        if (disposed || !IsAuthenticated || !IsMunicipal || mustChangePassword) return;
        // As linhas de outro filtro não podem ficar sob o filtro novo se a leitura falhar: a lista zera e o estado passa a ser "avaliando" ou "falhou".
        networkAlertsLoaded = networkAlertsFailed = false; networkAlertsReadAt = null; NetworkAlerts.Clear(); NotifyNetworkAlerts();
        var cancellation = new CancellationTokenSource();
        Interlocked.Exchange(ref networkAlertsCts, cancellation)?.Cancel();
        var version = ++networkAlertsVersion;
        _ = RunFilterLoadAsync(cancellation.Token, delta => { networkAlertsLoading += delta; NotifyNetworkAlerts(); },
            async () => { if (version == networkAlertsVersion) await LoadNetworkAlertsCoreAsync(cancellation.Token); },
            error => { if (version == networkAlertsVersion) ReportFilterFailure(error, () => { networkAlertsFailed = true; NotifyNetworkAlerts(); }); });
    }

    /// <summary>Carga disparada por filtro: uma por vez, cancelável, com a barra de progresso e sem sobrescrever uma leitura mais nova.</summary>
    private async Task RunFilterLoadAsync(CancellationToken cancellation, Action<int> adjustLoading, Func<Task> work, Action<NoraApiException> onError)
    {
        backgroundOperations++; adjustLoading(+1); Notify(nameof(IsBusy)); Notify(nameof(ShowProgress));
        try
        {
            await gate.WaitAsync(cancellation);
            try
            {
                if (cancellation.IsCancellationRequested || disposed || !IsAuthenticated) return;
                await work();
                if (disposed || cancellation.IsCancellationRequested) return;
                RecoverFromRefreshFailure(); MarkFresh();
            }
            catch (NoraApiException error) { onError(error); }
            finally { gate.Release(); }
        }
        catch (OperationCanceledException) { }
        finally
        {
            backgroundOperations--; adjustLoading(-1);
            if (!disposed) { Notify(nameof(IsBusy)); Notify(nameof(ShowProgress)); }
        }
    }

    private void ClearNetworkAlerts()
    {
        Interlocked.Exchange(ref networkAlertsCts, null)?.Cancel();
        networkAlertsVersion++; networkAlertsLoaded = networkAlertsFailed = false; networkAlertsAttemptedAt = networkAlertsReadAt = null; alertsUnitFilter = Guid.Empty; alertsRegion = "";
        NetworkAlerts.Clear(); Notify(nameof(AlertsUnitFilter)); Notify(nameof(AlertsRegion)); NotifyNetworkAlerts();
    }

    // ---- Catálogo de uma UBS em leitura (seção Capacidade) ----

    private HealthUnit? capacityUnit;
    private bool capacityCatalogLoaded, capacityCatalogFailed;
    private int capacityCatalogLoading, capacityCatalogVersion;
    private CancellationTokenSource? capacityCatalogCts;
    public ObservableCollection<Professional> CapacityProfessionals { get; } = [];
    public ObservableCollection<Room> CapacityRooms { get; } = [];
    public ObservableCollection<ScheduleTemplate> CapacitySchedule { get; } = [];
    public HealthUnit? CapacityUnit
    {
        get => capacityUnit;
        set
        {
            if (capacityUnit?.Id == value?.Id) return;
            capacityUnit = value; Notify();
            ClearCapacityCatalogData(); ReloadCapacityCatalog();
        }
    }
    public string CapacityCatalogLoadingLabel => capacityCatalogLoading > 0 ? "Lendo o cadastro da UBS…" : "";
    public string CapacityCatalogState => !IsAuthenticated ? ""
        : capacityUnit is null ? "Escolha uma UBS para ver os profissionais, os consultórios e a grade dela."
        : capacityUnit.DatabasePending ? "A base desta UBS ainda não foi criada: não há cadastro para ler."
        : capacityCatalogFailed ? "Não foi possível ler o cadastro desta UBS agora. Use Ler o cadastro de novo para tentar outra vez."
        : !capacityCatalogLoaded ? "Lendo o cadastro da UBS…"
        : CapacityProfessionals.Count + CapacityRooms.Count + CapacitySchedule.Count == 0 ? "Esta UBS ainda não cadastrou profissionais, consultórios nem grade." : "";
    private static readonly string[] CapacityCatalogNames = [nameof(CapacityCatalogLoadingLabel), nameof(CapacityCatalogState), nameof(CanReloadCapacityCatalog)];
    private void NotifyCapacityCatalog() { foreach (var name in CapacityCatalogNames) Notify(name); }

    private async Task LoadCapacityCatalogCoreAsync(HealthUnit unit, CancellationToken cancellation = default)
    {
        if (unit.DatabasePending) return;
        var professionals = api.UnitProfessionalsAsync(unit.Id, cancellation); var rooms = api.UnitRoomsAsync(unit.Id, cancellation); var schedule = api.UnitScheduleAsync(unit.Id, cancellation);
        var specialties = specialtyCatalogLoaded ? null : api.MunicipalSpecialtiesAsync(cancellation);
        await Task.WhenAll(new Task?[] { professionals, rooms, schedule, specialties }.OfType<Task>());
        if (disposed || !IsAuthenticated || cancellation.IsCancellationRequested || capacityUnit?.Id != unit.Id) return;
        if (specialties is not null) ApplySpecialtyCatalog(await specialties);
        capacityProfessionals = await professionals; capacityRooms = await rooms; capacityScheduleRaw = await schedule;
        capacityCatalogLoaded = true; capacityCatalogFailed = false;
        DecorateCapacityCatalog();
    }
    private Professional[] capacityProfessionals = [];
    private Room[] capacityRooms = [];
    private ScheduleTemplate[] capacityScheduleRaw = [];

    /// <summary>Nomes das especialidades, dos profissionais e dos consultórios nas linhas da grade; refeito quando a lista de especialidades muda.</summary>
    private void DecorateCapacityCatalog()
    {
        string SpecialtyName(Guid id) => SpecialtyCatalog.FirstOrDefault(item => item.Id == id)?.Name ?? "Especialidade removida";
        Replace(CapacityProfessionals, capacityProfessionals.OrderBy(item => item.FullName, StringComparer.CurrentCultureIgnoreCase)
            .Select(item => item with { SpecialtiesDisplay = string.Join(", ", item.SpecialtyIds.Select(SpecialtyName).OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)) }).ToArray());
        Replace(CapacityRooms, capacityRooms.OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase).ToArray());
        Replace(CapacitySchedule, capacityScheduleRaw.OrderBy(item => item.Weekday).ThenBy(item => item.StartTime, StringComparer.Ordinal)
            .Select(item => item with { ProfessionalName = capacityProfessionals.FirstOrDefault(p => p.Id == item.ProfessionalId)?.FullName ?? "—", RoomName = capacityRooms.FirstOrDefault(r => r.Id == item.RoomId)?.Name ?? "—", SpecialtyName = SpecialtyName(item.SpecialtyId) }).ToArray());
        NotifyCapacityCatalog();
    }

    public bool CanReloadCapacityCatalog => IsAuthenticated && capacityUnit is { DatabasePending: false };
    /// <summary>Botão "Ler o cadastro de novo": refaz a leitura da UBS escolhida, mantendo na tela o que já foi lido até a resposta.</summary>
    public void RetryCapacityCatalog() { capacityCatalogFailed = false; ReloadCapacityCatalog(); }

    /// <summary>Uma leitura (três requisições em paralelo) por UBS escolhida; trocar de UBS cancela a anterior.</summary>
    private void ReloadCapacityCatalog()
    {
        if (disposed || !IsAuthenticated || !IsMunicipal || mustChangePassword || capacityUnit is not { } unit) { NotifyCapacityCatalog(); return; }
        var cancellation = new CancellationTokenSource();
        Interlocked.Exchange(ref capacityCatalogCts, cancellation)?.Cancel();
        var version = ++capacityCatalogVersion;
        _ = RunFilterLoadAsync(cancellation.Token, delta => { capacityCatalogLoading += delta; NotifyCapacityCatalog(); },
            async () => { if (version == capacityCatalogVersion) await LoadCapacityCatalogCoreAsync(unit, cancellation.Token); },
            error => { if (version == capacityCatalogVersion) ReportFilterFailure(error, () => { capacityCatalogFailed = true; NotifyCapacityCatalog(); }); });
    }

    private void ClearCapacityCatalogData()
    {
        capacityCatalogLoaded = capacityCatalogFailed = false; capacityProfessionals = []; capacityRooms = []; capacityScheduleRaw = [];
        Replace(CapacityProfessionals, Array.Empty<Professional>()); Replace(CapacityRooms, Array.Empty<Room>()); Replace(CapacitySchedule, Array.Empty<ScheduleTemplate>());
        NotifyCapacityCatalog();
    }

    private void ClearMunicipalScheduling()
    {
        specialtyCatalogLoaded = false; SpecialtyCatalog.Clear(); selectedSpecialty = null; NewSpecialtyDraft(); Notify(nameof(SelectedSpecialty)); Notify(nameof(SpecialtyCatalogState));
        goals = []; goalsLoaded = false; goalScope = Guid.Empty; RefreshGoalEditors(true); Notify(nameof(GoalScope)); Notify(nameof(GoalsState)); Notify(nameof(GoalScopeLabel));
        ClearNetworkScheduling(); ClearNetworkAlerts();
        Interlocked.Exchange(ref capacityCatalogCts, null)?.Cancel(); capacityCatalogVersion++; capacityUnit = null; Notify(nameof(CapacityUnit)); ClearCapacityCatalogData();
    }

    /// <summary>Leituras extras que cada seção da Secretaria precisa; a leitura automática só repete as que passaram de 5 minutos.</summary>
    private Task LoadMunicipalSectionExtrasAsync(bool manual) => Section switch
    {
        "specialties" when manual || !specialtyCatalogLoaded => LoadSpecialtyCatalogCoreAsync(),
        "reports" when manual || !goalsLoaded => LoadGoalsCoreAsync(),
        "alerts" when manual || NetworkAlertsDue => LoadNetworkAlertsCoreAsync(),
        "capacity" when capacityUnit is { } unit && (manual || !capacityCatalogLoaded) => LoadCapacityCatalogCoreAsync(unit),
        "communication" when manual || MyAnnouncementsDue => LoadMyAnnouncementsCoreAsync(keepLoaded: !manual),
        _ => Task.CompletedTask
    };
}
