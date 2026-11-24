using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using Nora.Desktop.Models;
using Nora.Desktop.Services;

namespace Nora.Desktop.ViewModels;

/// <summary>Secretaria: UBS (lista, formulário, estado da base), cadastro de referência e agenda por UBS e filtro de UBS da rede.</summary>
public sealed partial class WorkspaceViewModel
{
    private static readonly HealthUnit WholeNetwork = new(Guid.Empty, "Toda a rede", "", "", 0, 0);
    private string unitSearch = "", unitName = "", unitRegion = "", unitAddress = "", unitLatitude = "", unitLongitude = "";
    private HealthUnit? selectedUnit;
    private Guid? editingUnitId;
    private Guid summaryUnitFilter = Guid.Empty;
    private Guid? appliedSummaryUnitId;
    private bool unitsLoaded, unitFormSubmitted, filteringUnits;
    private string unitAgendaFromText = "", unitAgendaToText = "", unitAgendaRangeError = "", unitAgendaStatus = "";
    public ObservableCollection<HealthUnit> Units { get; } = [];
    /// <summary>Seletor único de UBS: "Toda a rede" mais as unidades; escolher aplica na hora aos números da rede.</summary>
    public ObservableCollection<HealthUnit> UnitFilterOptions { get; } = [WholeNetwork];
    public Guid SummaryUnitFilter
    {
        get => summaryUnitFilter;
        set
        {
            if (summaryUnitFilter == value) return;
            summaryUnitFilter = value; Notify();
            ReloadNetwork(includeSummary: true);
        }
    }
    /// <summary>Texto do painel de exportação: diz o escopo do resumo que foi aplicado (o que vai no CSV), não o da escolha ainda em leitura.</summary>
    public string SummaryExportNote => "Baixe em CSV os totais carregados da API. O arquivo contém dados agregados e não inclui pacientes identificáveis. "
        + (appliedSummaryUnitId is null ? "O resumo é de toda a rede; o período filtra somente as vagas pela data de criação; pacientes e fila são totais atuais de toda a rede."
            : $"O resumo é só da {AppliedSummaryUnitLabel}, que aparece pelo nome no arquivo; o período filtra somente as vagas pela data de criação; pacientes e fila são totais atuais só dessa UBS.");
    public string AppliedSummaryUnitLabel => appliedSummaryUnitId is { } id ? Units.FirstOrDefault(unit => unit.Id == id)?.Name ?? "UBS selecionada" : "Toda a rede";
    public bool UnitsLoaded { get => unitsLoaded; private set { unitsLoaded = value; Notify(); Notify(nameof(UnitsState)); } }
    public string UnitsState => !IsAuthenticated ? "Entre na Visão geral para acessar as unidades." : !UnitsLoaded ? "Carregando as unidades da API…" : Units.Count == 0 ? "Nenhuma UBS cadastrada." : !FilteredUnits.Any() ? "Nenhuma unidade encontrada para esta pesquisa." : "";
    public string UnitSearch
    {
        get => unitSearch;
        // A pesquisa troca a lista mostrada; a UBS selecionada continua selecionada mesmo escondida (o ListBox limparia a seleção).
        set { unitSearch = value ?? ""; Notify(); filteringUnits = true; try { NotifyUnits(); Notify(nameof(SelectedUnit)); } finally { filteringUnits = false; } }
    }
    public IEnumerable<HealthUnit> FilteredUnits => Units.Where(u => u.Name.Contains(UnitSearch, StringComparison.CurrentCultureIgnoreCase) || u.Region.Contains(UnitSearch, StringComparison.CurrentCultureIgnoreCase) || u.Address.Contains(UnitSearch, StringComparison.CurrentCultureIgnoreCase));
    public HealthUnit? SelectedUnit
    {
        get => selectedUnit;
        set
        {
            if (value is null && filteringUnits) return;
            // A mesma UBS reatribuída (atualização da lista, estado da base) mantém cadastro e agenda já carregados.
            var sameUnit = selectedUnit?.Id == value?.Id;
            selectedUnit = value;
            if (!sameUnit) { UnitRegistry.Clear(); UnitRegistryLoaded = false; MunicipalAppointments.Clear(); VisibleMunicipalAppointments.Clear(); MunicipalAppointmentsLoaded = false; }
            Notify(); Notify(nameof(HasSelectedUnit)); Notify(nameof(CanEditUnit)); Notify(nameof(CanReadUnitRegistry)); Notify(nameof(CanProvisionUnitDatabase));
            Notify(nameof(SelectedUnitLabel)); Notify(nameof(SelectedUnitDatabasePending)); Notify(nameof(UnitRegistryHint));
        }
    }
    public string SelectedUnitLabel => selectedUnit is { } unit ? $"UBS selecionada: {unit.SelectorLabel}" : "Nenhuma UBS selecionada.";
    public bool SelectedUnitDatabasePending => selectedUnit?.DatabasePending == true;
    public bool CanProvisionUnitDatabase => IsMunicipal && CanOperate && SelectedUnitDatabasePending;
    public string UnitRegistryHint => SelectedUnitDatabasePending
        ? "A base desta UBS ainda não foi criada: crie a base antes de consultar cadastros e agenda."
        : "A consulta mostra os dados de referência desta unidade e é registrada no histórico de acesso.";

    private bool unitRegistryLoaded;
    public ObservableCollection<UnitPatientRecord> UnitRegistry { get; } = [];
    public bool UnitRegistryLoaded { get => unitRegistryLoaded; private set { unitRegistryLoaded = value; Notify(); Notify(nameof(UnitRegistryState)); } }
    public string UnitRegistryState => !UnitRegistryLoaded ? "Selecione uma UBS e consulte os cadastros quando necessário." : UnitRegistry.Count == 0 ? "Nenhum paciente cadastrado nesta UBS." : "";
    public bool CanReadUnitRegistry => IsMunicipal && CanOperate && HasSelectedUnit && !SelectedUnitDatabasePending;

    // Agenda nominal da UBS: período inicial/final (padrão −30/+30 dias), situação filtrada no cliente.
    private bool municipalAppointmentsLoaded;
    public ObservableCollection<AppointmentInfo> MunicipalAppointments { get; } = [];
    public ObservableCollection<AppointmentInfo> VisibleMunicipalAppointments { get; } = [];
    public static IReadOnlyList<FilterOption> AppointmentStatusOptions { get; } =
        [new("", "Todas as situações"), new("scheduled", "Agendada"), new("called", "Chamada"), new("completed", "Concluída"), new("missed", "Falta"), new("cancelled", "Cancelada")];
    public string UnitAgendaFromText { get => unitAgendaFromText; set { unitAgendaFromText = value ?? ""; Notify(); ClearAgendaRangeError(); } }
    public string UnitAgendaToText { get => unitAgendaToText; set { unitAgendaToText = value ?? ""; Notify(); ClearAgendaRangeError(); } }
    public string UnitAgendaRangeError { get => unitAgendaRangeError; private set { if (unitAgendaRangeError == value) return; unitAgendaRangeError = value; Notify(); Notify(nameof(HasUnitAgendaRangeError)); } }
    public bool HasUnitAgendaRangeError => unitAgendaRangeError.Length > 0;
    private void ClearAgendaRangeError() { if (unitAgendaRangeError.Length > 0) UnitAgendaRangeError = ""; }
    public string UnitAgendaStatus { get => unitAgendaStatus; set { value ??= ""; if (unitAgendaStatus == value) return; unitAgendaStatus = value; Notify(); RefreshVisibleAppointments(); } }
    public bool MunicipalAppointmentsLoaded { get => municipalAppointmentsLoaded; private set { municipalAppointmentsLoaded = value; Notify(); Notify(nameof(MunicipalAppointmentsState)); } }
    public string MunicipalAppointmentsState => !MunicipalAppointmentsLoaded ? "Selecione uma UBS e abra Filtros para consultar a agenda do período."
        : MunicipalAppointments.Count == 0 ? "Nenhuma consulta no período."
        : VisibleMunicipalAppointments.Count == 0 ? "Nenhuma consulta com esta situação no período." : "";
    private void RefreshVisibleAppointments()
    {
        Replace(VisibleMunicipalAppointments, MunicipalAppointments.Where(item => unitAgendaStatus.Length == 0 || item.Status == unitAgendaStatus).ToArray());
        Notify(nameof(MunicipalAppointmentsState));
    }
    private void ResetAgendaRange()
    {
        var today = DateTime.Today;
        unitAgendaFromText = DisplayFormat.Date(DateOnly.FromDateTime(today.AddDays(-30))); unitAgendaToText = DisplayFormat.Date(DateOnly.FromDateTime(today.AddDays(30)));
        unitAgendaRangeError = ""; unitAgendaStatus = "";
        foreach (var name in new[] { nameof(UnitAgendaFromText), nameof(UnitAgendaToText), nameof(UnitAgendaRangeError), nameof(HasUnitAgendaRangeError), nameof(UnitAgendaStatus) }) Notify(name);
    }

    public Task LoadMunicipalAppointmentsAsync() => RunAsync(async () =>
    {
        if (!IsMunicipal || !IsAuthenticated || SelectedUnit is not { } unit) throw new NoraApiException("Selecione uma UBS para consultar a agenda.");
        var error = ValidateDateRange(UnitAgendaFromText, UnitAgendaToText, out var from, out var to);
        UnitAgendaRangeError = error;
        if (error.Length > 0) return;
        // Datas inclusivas: o fim vai até a meia-noite seguinte; a API aceita no máximo 366 dias.
        var records = await api.UnitAppointmentsAsync(unit.Id, new DateTimeOffset(from), new DateTimeOffset(to.AddDays(1)));
        if (disposed || !IsAuthenticated || SelectedUnit?.Id != unit.Id) return;
        Replace(MunicipalAppointments, records.OrderBy(item => item.StartsAt).ThenBy(item => item.Id).ToArray());
        MunicipalAppointmentsLoaded = true; RefreshVisibleAppointments();
        Say(MessageKind.Success, "Agenda da UBS atualizada.");
    });
    public Task LoadUnitRegistryAsync() => RunAsync(async () =>
    {
        if (!IsMunicipal || !IsAuthenticated || SelectedUnit is not { } unit) throw new NoraApiException("Selecione uma UBS para consultar os cadastros.");
        var records = await api.UnitRegistryAsync(unit.Id);
        if (disposed || !IsAuthenticated || SelectedUnit?.Id != unit.Id) return;
        Replace(UnitRegistry, records.OrderBy(item => item.FullName, StringComparer.CurrentCultureIgnoreCase).ThenBy(item => item.Cpf, StringComparer.Ordinal).ToArray()); UnitRegistryLoaded = true;
        Say(MessageKind.Success, "Cadastros da UBS atualizados.");
    });
    public bool HasSelectedUnit => SelectedUnit != null;
    public bool CanEditUnit => IsMunicipal && CanOperate && HasSelectedUnit;

    // Formulário de UBS: mensagem por campo (aparece ao digitar e ao salvar) e avisos de coordenadas que não bloqueiam.
    public string UnitName { get => unitName; set { unitName = value; Notify(); Notify(nameof(UnitNameError)); } }
    public string UnitRegion { get => unitRegion; set { unitRegion = value; Notify(); Notify(nameof(UnitRegionError)); } }
    public string UnitAddress { get => unitAddress; set { unitAddress = value; Notify(); Notify(nameof(UnitAddressError)); } }
    public string UnitLatitude { get => unitLatitude; set { unitLatitude = value; Notify(); Notify(nameof(UnitLatitudeError)); Notify(nameof(UnitCoordinateWarning)); } }
    public string UnitLongitude { get => unitLongitude; set { unitLongitude = value; Notify(); Notify(nameof(UnitLongitudeError)); Notify(nameof(UnitCoordinateWarning)); } }
    public string UnitDraftTitle => editingUnitId == null ? "Cadastrar UBS" : "Editar UBS";
    private string TextError(string value, int min, int max, string message) => (unitFormSubmitted || value.Length > 0) && value.Trim().Length < min || value.Trim().Length > max ? message : "";
    public string UnitNameError => TextError(unitName, 2, 160, "Informe o nome da UBS, de 2 a 160 caracteres.");
    public string UnitRegionError => TextError(unitRegion, 2, 120, "Informe a região, de 2 a 120 caracteres.");
    public string UnitAddressError => TextError(unitAddress, 2, 240, "Informe o endereço, de 2 a 240 caracteres.");
    public string UnitLatitudeError => (unitFormSubmitted || unitLatitude.Length > 0) && !TryCoordinate(unitLatitude, -85, 85, out _) ? "Latitude inválida: use graus decimais entre -85 e 85 (ex.: -23,55)." : "";
    public string UnitLongitudeError => (unitFormSubmitted || unitLongitude.Length > 0) && !TryCoordinate(unitLongitude, -180, 180, out _) ? "Longitude inválida: use graus decimais entre -180 e 180 (ex.: -46,63)." : "";
    private bool HasUnitFormErrors => UnitNameError.Length + UnitRegionError.Length + UnitAddressError.Length + UnitLatitudeError.Length + UnitLongitudeError.Length > 0;
    /// <summary>Aviso, não bloqueio: (0, 0), coordenadas aparentemente trocadas ou fora do Brasil.</summary>
    public string UnitCoordinateWarning
    {
        get
        {
            if (!TryCoordinate(unitLatitude, -85, 85, out var lat) || !TryCoordinate(unitLongitude, -180, 180, out var lon)) return "";
            if (lat == 0 && lon == 0) return "Atenção: as coordenadas (0, 0) ficam no oceano, não no Brasil. Confira antes de salvar.";
            if (InBrazil(lat, lon)) return "";
            return InBrazil(lon, lat)
                ? "Atenção: latitude e longitude parecem estar trocadas. Confira antes de salvar."
                : "Atenção: estas coordenadas ficam fora do Brasil. Confira antes de salvar.";
        }
    }
    private static bool InBrazil(double latitude, double longitude) => latitude is >= -34 and <= 5.6 && longitude is >= -74.5 and <= -28;
    private static readonly string[] UnitFormNames = [nameof(UnitNameError), nameof(UnitRegionError), nameof(UnitAddressError), nameof(UnitLatitudeError), nameof(UnitLongitudeError), nameof(UnitCoordinateWarning)];

    private void NotifyUnits() { Notify(nameof(FilteredUnits)); Notify(nameof(UnitsState)); Notify(nameof(AppliedSummaryUnitLabel)); Notify(nameof(SummaryExportNote)); }

    /// <summary>A lista de UBS mudou de conteúdo: seletores de UBS e de região acompanham, sem recriar o que não mudou.</summary>
    private void UnitsChanged()
    {
        filteringUnits = true;
        Replace(UnitFilterOptions, new[] { WholeNetwork }.Concat(Units).ToArray());
        var regions = Units.Select(unit => unit.Region.Trim()).Where(region => region.Length > 0).Distinct(StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(region => region, StringComparer.CurrentCultureIgnoreCase).Select(region => new FilterOption(region, region));
        Replace(RegionOptions, new[] { new FilterOption("", "Todas as regiões") }.Concat(regions).ToArray());
        try { NotifyUnits(); Notify(nameof(SelectedUnit)); } finally { filteringUnits = false; }
        Notify(nameof(MetricsScopeLabel));
        RebuildAnnouncementChoices();
    }

    public void NewUnitDraft()
    {
        editingUnitId = null; unitFormSubmitted = false; UnitName = UnitRegion = UnitAddress = UnitLatitude = UnitLongitude = ""; Notify(nameof(UnitDraftTitle));
        foreach (var name in UnitFormNames) Notify(name);
    }
    public void EditSelectedUnit()
    {
        if (SelectedUnit is not { } unit) return;
        editingUnitId = unit.Id; unitFormSubmitted = false; UnitName = unit.Name; UnitRegion = unit.Region; UnitAddress = unit.Address;
        UnitLatitude = unit.Latitude.ToString("R", CultureInfo.InvariantCulture); UnitLongitude = unit.Longitude.ToString("R", CultureInfo.InvariantCulture);
        Notify(nameof(UnitDraftTitle));
    }

    private async Task LoadUnitsCoreAsync()
    {
        var result = await api.HealthUnitsAsync();
        if (disposed || !IsAuthenticated) return;
        ApplyUnits(result);
    }
    private void ApplyUnits(HealthUnit[] result)
    {
        var sorted = result.OrderBy(unit => unit.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(unit => unit.Id).ToArray();
        var selectedId = SelectedUnit?.Id;
        var changed = Replace(Units, sorted);
        var firstLoad = !UnitsLoaded;
        UnitsLoaded = true;
        if (selectedId is { } id) SelectedUnit = Units.FirstOrDefault(unit => unit.Id == id);
        if (changed || firstLoad) UnitsChanged();
    }
    public Task LoadUnitsAsync() => RunAsync(async () =>
    {
        if (!IsMunicipal || !IsAuthenticated) throw new NoraApiException("Entre na Secretaria para carregar as unidades.");
        await LoadUnitsCoreAsync();
        Say(MessageKind.Success, "Unidades atualizadas.");
    });

    /// <summary>Insere (ou move) a UBS na posição ordenada por nome, como a lista que vem da API.</summary>
    private void UpsertUnit(HealthUnit saved)
    {
        for (var index = 0; index < Units.Count; index++) if (Units[index].Id == saved.Id) { Units.RemoveAt(index); break; }
        var position = Units.Count;
        for (var index = 0; index < Units.Count; index++)
        {
            var order = string.Compare(saved.Name, Units[index].Name, StringComparison.CurrentCultureIgnoreCase);
            if (order < 0 || (order == 0 && saved.Id.CompareTo(Units[index].Id) < 0)) { position = index; break; }
        }
        Units.Insert(position, saved);
        UnitsChanged();
    }

    public Task SaveUnitAsync() => RunAsync(async () =>
    {
        if (!IsMunicipal || !IsAuthenticated) throw new NoraApiException("Entre na Secretaria para cadastrar uma UBS.");
        unitFormSubmitted = true;
        foreach (var field in UnitFormNames) Notify(field);
        if (HasUnitFormErrors) throw new NoraApiException("Corrija os campos destacados no formulário da UBS.");
        var name = UnitName.Trim(); var region = UnitRegion.Trim(); var address = UnitAddress.Trim();
        TryCoordinate(UnitLatitude, -85, 85, out var lat); TryCoordinate(UnitLongitude, -180, 180, out var lon);
        var creating = editingUnitId is null;
        var saved = editingUnitId is { } id ? await api.UpdateHealthUnitAsync(id, name, region, address, lat, lon) : await api.CreateHealthUnitAsync(name, region, address, lat, lon);
        if (disposed) return;
        UpsertUnit(saved); SelectedUnit = saved; NewUnitDraft();
        if (saved.DatabasePending)
            Say(MessageKind.Info, "UBS salva, mas a base de dados dela não foi criada: o usuário PostgreSQL da API precisa de permissão para criar bancos. Use \"Tentar criar a base\" depois de ajustar a permissão.");
        else Say(MessageKind.Success, "UBS salva. Selecione-a no mapa para conferir a localização.");
        if (creating) ReloadNetwork(includeSummary: true);
    });

    /// <summary>Repete a criação da base da UBS (idempotente no servidor). Falha → 503 database-provisioning-failed, explicada na faixa comum.</summary>
    public Task ProvisionSelectedUnitDatabaseAsync() => RunAsync(async () =>
    {
        if (!IsMunicipal || !IsAuthenticated || SelectedUnit is not { DatabasePending: true } unit) throw new NoraApiException("Selecione uma UBS com a base ainda não criada.");
        var saved = await api.ProvisionUnitDatabaseAsync(unit.Id);
        if (disposed || !IsAuthenticated) return;
        UpsertUnit(saved);
        if (SelectedUnit?.Id == saved.Id) SelectedUnit = saved;
        Say(saved.DatabasePending ? MessageKind.Error : MessageKind.Success,
            saved.DatabasePending ? "A API respondeu, mas a base da UBS continua sem estar pronta. Tente novamente." : $"Base da UBS {saved.Name} criada. Ela passa a entrar nos números da rede.");
        ReloadNetwork(includeSummary: true);
    });

    private static bool TryCoordinate(string value, double min, double max, out double number)
        => double.TryParse(value.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out number) && double.IsFinite(number) && number >= min && number <= max;

    private void ClearUnits()
    {
        Units.Clear(); selectedUnit = null; UnitRegistry.Clear(); UnitRegistryLoaded = false; MunicipalAppointments.Clear(); VisibleMunicipalAppointments.Clear(); MunicipalAppointmentsLoaded = false;
        UnitsLoaded = false; unitSearch = ""; Notify(nameof(UnitSearch)); summaryUnitFilter = Guid.Empty; appliedSummaryUnitId = null;
        Replace(UnitFilterOptions, new[] { WholeNetwork }); Replace(RegionOptions, new[] { new FilterOption("", "Todas as regiões") });
        ResetAgendaRange(); NewUnitDraft(); ClearMetrics(); NotifyUnits();
        foreach (var name in new[] { nameof(SelectedUnit), nameof(SummaryUnitFilter), nameof(HasSelectedUnit), nameof(CanEditUnit), nameof(CanReadUnitRegistry), nameof(SelectedUnitLabel), nameof(SelectedUnitDatabasePending), nameof(CanProvisionUnitDatabase), nameof(UnitRegistryHint) }) Notify(name);
    }
}
