using System.Collections.ObjectModel;
using System.Globalization;
using Nora.Desktop.Models;
using Nora.Desktop.Services;

namespace Nora.Desktop.ViewModels;

public sealed partial class WorkspaceViewModel
{
    private string unitSearch = "", unitName = "", unitRegion = "", unitAddress = "", unitLatitude = "", unitLongitude = "";
    private HealthUnit? selectedUnit;
    private Guid? editingUnitId;
    private Guid summaryUnitFilter = Guid.Empty;
    private Guid? appliedSummaryUnitId;
    private bool unitsLoaded;
    public ObservableCollection<HealthUnit> Units { get; } = [];
    public IEnumerable<HealthUnit> SummaryUnitOptions => new[] { new HealthUnit(Guid.Empty, "Todas as UBS", "", "", 0, 0) }.Concat(Units);
    public Guid SummaryUnitFilter { get => summaryUnitFilter; set { summaryUnitFilter = value; Notify(); } }
    public string AppliedSummaryUnitLabel => appliedSummaryUnitId is { } id ? Units.FirstOrDefault(unit => unit.Id == id)?.Name ?? "UBS selecionada" : "Todas as UBS";
    public Task ApplySummaryUnitAsync() => RunAsync(async () =>
    {
        if (!IsMunicipal || !IsAuthenticated) throw new NoraApiException("Entre na Secretaria para filtrar a rede.");
        Guid? selected = SummaryUnitFilter == Guid.Empty ? null : SummaryUnitFilter;
        var result = await api.SummaryAsync(selected);
        if (disposed || !IsAuthenticated) return;
        appliedSummaryUnitId = selected; summary = result; summaryReceivedAt = DateTimeOffset.Now;
        if (LightningPeriodDays != 0 && CurrentLightningCohort == null) LightningPeriodDays = 0;
        Notify(nameof(AppliedSummaryUnitLabel)); NotifySummary(); Say(MessageKind.Success, "Resumo atualizado para " + AppliedSummaryUnitLabel + ".");
    });
    public bool UnitsLoaded { get => unitsLoaded; private set { unitsLoaded = value; Notify(); Notify(nameof(UnitsState)); } }
    public string UnitsState => !IsAuthenticated ? "Entre na Visão geral para acessar as unidades." : !UnitsLoaded ? "Atualize para carregar as unidades da API." : Units.Count == 0 ? "Nenhuma UBS cadastrada." : !FilteredUnits.Any() ? "Nenhuma unidade encontrada para esta pesquisa." : "";
    public string UnitSearch { get => unitSearch; set { unitSearch = value; Notify(); NotifyUnits(); } }
    public IEnumerable<HealthUnit> FilteredUnits => Units.Where(u => u.Name.Contains(UnitSearch, StringComparison.CurrentCultureIgnoreCase) || u.Region.Contains(UnitSearch, StringComparison.CurrentCultureIgnoreCase) || u.Address.Contains(UnitSearch, StringComparison.CurrentCultureIgnoreCase));
    public HealthUnit? SelectedUnit { get => selectedUnit; set { selectedUnit = value; UnitRegistry.Clear(); UnitRegistryLoaded = false; MunicipalAppointments.Clear(); MunicipalAppointmentsLoaded = false; Notify(); Notify(nameof(HasSelectedUnit)); Notify(nameof(CanEditUnit)); Notify(nameof(CanReadUnitRegistry)); } }
    private bool unitRegistryLoaded;
    public ObservableCollection<UnitPatientRecord> UnitRegistry { get; } = [];
    public bool UnitRegistryLoaded { get => unitRegistryLoaded; private set { unitRegistryLoaded = value; Notify(); Notify(nameof(UnitRegistryState)); } }
    public string UnitRegistryState => !UnitRegistryLoaded ? "Selecione uma UBS e consulte os cadastros quando necessário." : UnitRegistry.Count == 0 ? "Nenhum paciente cadastrado nesta UBS." : "";
    public bool CanReadUnitRegistry => IsMunicipal && CanOperate && HasSelectedUnit;
    private bool municipalAppointmentsLoaded;
    public ObservableCollection<AppointmentInfo> MunicipalAppointments { get; } = [];
    public bool MunicipalAppointmentsLoaded { get => municipalAppointmentsLoaded; private set { municipalAppointmentsLoaded = value; Notify(); Notify(nameof(MunicipalAppointmentsState)); } }
    public string MunicipalAppointmentsState => !MunicipalAppointmentsLoaded ? "Selecione uma UBS e consulte a agenda." : MunicipalAppointments.Count == 0 ? "Nenhuma consulta no período." : "";
    public Task LoadMunicipalAppointmentsAsync() => RunAsync(async () =>
    {
        if (!IsMunicipal || !IsAuthenticated || SelectedUnit is not { } unit) throw new NoraApiException("Selecione uma UBS para consultar a agenda.");
        var today = DateTimeOffset.Now; var records = await api.UnitAppointmentsAsync(unit.Id, today.AddDays(-30), today.AddDays(30));
        if (disposed || !IsAuthenticated || SelectedUnit?.Id != unit.Id) return;
        Replace(MunicipalAppointments, records); MunicipalAppointmentsLoaded = true; Say(MessageKind.Success, "Agenda da UBS atualizada.");
    });
    public Task LoadUnitRegistryAsync() => RunAsync(async () =>
    {
        if (!IsMunicipal || !IsAuthenticated || SelectedUnit is not { } unit) throw new NoraApiException("Selecione uma UBS para consultar os cadastros.");
        var records = await api.UnitRegistryAsync(unit.Id);
        if (disposed || !IsAuthenticated || SelectedUnit?.Id != unit.Id) return;
        Replace(UnitRegistry, records); UnitRegistryLoaded = true; Say(MessageKind.Success, "Cadastros da UBS atualizados.");
    });
    public bool HasSelectedUnit => SelectedUnit != null;
    public bool CanEditUnit => IsMunicipal && CanOperate && HasSelectedUnit;
    public string UnitName { get => unitName; set { unitName = value; Notify(); } }
    public string UnitRegion { get => unitRegion; set { unitRegion = value; Notify(); } }
    public string UnitAddress { get => unitAddress; set { unitAddress = value; Notify(); } }
    public string UnitLatitude { get => unitLatitude; set { unitLatitude = value; Notify(); } }
    public string UnitLongitude { get => unitLongitude; set { unitLongitude = value; Notify(); } }
    public string UnitDraftTitle => editingUnitId == null ? "Cadastrar UBS" : "Editar UBS";
    private void NotifyUnits() { Notify(nameof(FilteredUnits)); Notify(nameof(UnitsState)); Notify(nameof(SummaryUnitOptions)); Notify(nameof(AppliedSummaryUnitLabel)); }
    public void NewUnitDraft()
    {
        editingUnitId = null; UnitName = UnitRegion = UnitAddress = UnitLatitude = UnitLongitude = ""; Notify(nameof(UnitDraftTitle));
    }
    public void EditSelectedUnit()
    {
        if (SelectedUnit is not { } unit) return;
        editingUnitId = unit.Id; UnitName = unit.Name; UnitRegion = unit.Region; UnitAddress = unit.Address;
        UnitLatitude = unit.Latitude.ToString("R", CultureInfo.InvariantCulture); UnitLongitude = unit.Longitude.ToString("R", CultureInfo.InvariantCulture);
        Notify(nameof(UnitDraftTitle));
    }
    public Task LoadUnitsAsync() => RunAsync(async () =>
    {
        if (!IsMunicipal || !IsAuthenticated) throw new NoraApiException("Entre na Secretaria para carregar as unidades.");
        var result = await api.HealthUnitsAsync();
        if (disposed) return;
        var selectedId = SelectedUnit?.Id;
        Replace(Units, result);
        UnitsLoaded = true;
        SelectedUnit = Units.FirstOrDefault(u => u.Id == selectedId);
        NotifyUnits();
        Say(MessageKind.Success, "Unidades atualizadas.");
    });
    public Task SaveUnitAsync() => RunAsync(async () =>
    {
        if (!IsMunicipal || !IsAuthenticated) throw new NoraApiException("Entre na Secretaria para cadastrar uma UBS.");
        var name = UnitName.Trim(); var region = UnitRegion.Trim(); var address = UnitAddress.Trim();
        if (name.Length is < 2 or > 160 || region.Length is < 2 or > 120 || address.Length is < 2 or > 240 ||
            !TryCoordinate(UnitLatitude, -85, 85, out var lat) || !TryCoordinate(UnitLongitude, -180, 180, out var lon))
            throw new NoraApiException("Informe nome, região, endereço e coordenadas válidas em graus decimais.");
        var saved = editingUnitId is { } id ? await api.UpdateHealthUnitAsync(id, name, region, address, lat, lon) : await api.CreateHealthUnitAsync(name, region, address, lat, lon);
        if (disposed) return;
        var previous = Units.FirstOrDefault(u => u.Id == saved.Id);
        if (previous != null) Units.Remove(previous);
        Units.Add(saved); NotifyUnits(); SelectedUnit = saved; NewUnitDraft();
        Say(MessageKind.Success, "UBS salva. Selecione-a no mapa para conferir a localização.");
    });
    private static bool TryCoordinate(string value, double min, double max, out double number)
        => double.TryParse(value.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out number) && double.IsFinite(number) && number >= min && number <= max;
    private void ClearUnits()
    {
        Units.Clear(); SelectedUnit = null; UnitsLoaded = false; UnitSearch = ""; SummaryUnitFilter = Guid.Empty; appliedSummaryUnitId = null; NewUnitDraft(); NotifyUnits();
    }
}
