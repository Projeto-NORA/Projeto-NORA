using System.Collections.ObjectModel;
using Nora.Desktop.Models;
using Nora.Desktop.Services;

namespace Nora.Desktop.ViewModels;

public sealed partial class WorkspaceViewModel
{
    private StaffAccount? selectedStaff;
    private HealthUnit? selectedStaffUnit, selectedStaffGrant;
    public ObservableCollection<StaffAccount> Staff { get; } = [];
    public ObservableCollection<HealthUnit> StaffUnits { get; } = [];
    public StaffAccount? SelectedStaff { get => selectedStaff; set { selectedStaff = value; StaffUnits.Clear(); SelectedStaffGrant = null; Notify(); } }
    public HealthUnit? SelectedStaffUnit { get => selectedStaffUnit; set { selectedStaffUnit = value; Notify(); } }
    public HealthUnit? SelectedStaffGrant { get => selectedStaffGrant; set { selectedStaffGrant = value; Notify(); } }
    public Task LoadStaffAsync() => RunAsync(async () =>
    {
        if (!IsMunicipal || !IsAuthenticated) throw new NoraApiException("Entre na Secretaria para gerenciar acessos.");
        var staff = await api.StaffAsync();
        var units = await api.HealthUnitsAsync();
        if (disposed) return;
        Replace(Staff, staff); Replace(Units, units); UnitsLoaded = true; SelectedStaff = null;
        Say(MessageKind.Success, "Contas e unidades atualizadas. Selecione uma conta e consulte seus vínculos.");
    });
    public Task LoadStaffUnitsAsync() => RunAsync(async () =>
    {
        var staff = SelectedStaff ?? throw new NoraApiException("Selecione uma conta OBS.");
        var units = await api.StaffUnitsAsync(staff.Id);
        if (disposed || SelectedStaff?.Id != staff.Id) return;
        Replace(StaffUnits, units); SelectedStaffGrant = null; Say(MessageKind.Success, "Vínculos atualizados.");
    });
    public Task ChangeStaffUnitAsync(bool grant) => RunAsync(async () =>
    {
        var staff = SelectedStaff ?? throw new NoraApiException("Selecione uma conta OBS.");
        var unit = (grant ? SelectedStaffUnit : SelectedStaffGrant) ?? throw new NoraApiException("Selecione uma UBS.");
        await api.SetStaffUnitAsync(staff.Id, unit.Id, grant);
        var units = await api.StaffUnitsAsync(staff.Id);
        if (disposed || SelectedStaff?.Id != staff.Id) return;
        Replace(StaffUnits, units); SelectedStaffGrant = null;
        Say(MessageKind.Success, grant ? "Vínculo concedido e registrado." : "Vínculo revogado e registrado.");
    });
    private void ClearStaff()
    {
        Staff.Clear(); StaffUnits.Clear(); SelectedStaff = null; SelectedStaffUnit = null; SelectedStaffGrant = null;
    }
}
