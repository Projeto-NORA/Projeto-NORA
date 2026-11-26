using System.Windows;
using System.Windows.Controls;
using Nora.Desktop.Models;
using Nora.Desktop.Services;
using Nora.Desktop.ViewModels;

namespace Nora.Desktop.Views;

/// <summary>Acessos às UBS: conceder e retirar o acesso de uma conta a uma UBS, em pop-up, sempre com confirmação.</summary>
public partial class StaffAccessView : UserControl
{
    public StaffAccessView()
    {
        InitializeComponent();
        MunicipalDetailsHost.Attach(AccessList, this);
        ListingBehavior.AddBalloonRefreshedHandler(AccessList, OnBalloonRefreshed);
    }

    private WorkspaceViewModel? Workspace => DataContext as WorkspaceViewModel;
    private StaffAccount? Selected => ListingBehavior.GetSelectedItems<StaffAccount>(AccessList).FirstOrDefault();

    /// <summary>Retirar acesso só serve a uma conta que tem UBS: some quando já se sabe que ela não tem nenhuma (enquanto as UBS não foram lidas, continua à vista).</summary>
    private void OnBalloonRefreshed(object sender, RoutedEventArgs args)
    {
        if (ListingBehavior.GetSelectedItems<StaffAccount>(AccessList) is [{ UnitsDisplay: StaffAccount.NoUnitsDisplay }]) RevokeButton.Visibility = Visibility.Collapsed;
    }

    /// <summary>Escolhe a conta (o ViewModel guarda a UBS a conceder e a retirar) e lê as UBS que ela já tem.</summary>
    private async Task<bool> PrepareAsync(WorkspaceViewModel vm, StaffAccount account)
    {
        vm.SelectedStaff = account;
        vm.SelectedStaffUnit = null; vm.SelectedStaffGrant = null;
        await vm.LoadStaffUnitsAsync();
        return !vm.HasMessage || vm.MessageKind != MessageKind.Error;
    }

    private async void OnGrant(object sender, RoutedEventArgs e)
    {
        if (Workspace is not { } vm || Selected is not { } account || !await PrepareAsync(vm, account)) return;
        var granted = vm.StaffUnits.Select(unit => unit.Id).ToHashSet();
        var options = vm.Units.Where(unit => !granted.Contains(unit.Id)).ToArray();
        var owner = Window.GetWindow(this);
        if (options.Length == 0) { DialogService.Info(owner, "Conceder acesso", $"{account.FullName} já tem acesso a todas as UBS da rede."); return; }
        var form = new StaffUnitFormView(account.FullName, "A conta passa a ver cadastros, agenda e vagas da UBS escolhida.", "UBS para conceder acesso", options);
        var problem = "";
        async Task<bool> Grant()
        {
            problem = "";
            if (form.SelectedUnit is not { } unit) { problem = "Escolha a UBS."; return false; }
            vm.SelectedStaffUnit = unit;
            if (!Confirmation.Ask(null, "Confirmar concessão", $"Conceder a {account.FullName} acesso à UBS {unit.Name}? A conta passa a ver cadastros, agenda e vagas dessa unidade.")) { problem = "A concessão não foi confirmada."; return false; }
            return await vm.ChangeStaffUnitAsync(true);
        }
        MunicipalDetailsHost.ShowForm(this, vm, "Conceder acesso a uma UBS", form, Grant, "Conceder acesso", () => problem.Length > 0 ? problem : vm.Message);
    }

    private async void OnRevoke(object sender, RoutedEventArgs e)
    {
        if (Workspace is not { } vm || Selected is not { } account || !await PrepareAsync(vm, account)) return;
        var owner = Window.GetWindow(this);
        if (vm.StaffUnits.Count == 0) { DialogService.Info(owner, "Retirar acesso", $"{account.FullName} não tem acesso a nenhuma UBS."); return; }
        var form = new StaffUnitFormView(account.FullName, "A conta deixa de ver cadastros, agenda e vagas da UBS escolhida. A retirada fica registrada no histórico.", "UBS para retirar o acesso", vm.StaffUnits.ToArray());
        var problem = "";
        async Task<bool> Revoke()
        {
            problem = "";
            if (form.SelectedUnit is not { } unit) { problem = "Escolha a UBS."; return false; }
            vm.SelectedStaffGrant = unit;
            if (!Confirmation.Ask(null, "Confirmar revogação", $"Revogar o vínculo de {account.FullName} com {unit.Name}?")) { problem = "A retirada não foi confirmada."; return false; }
            return await vm.ChangeStaffUnitAsync(false);
        }
        MunicipalDetailsHost.ShowForm(this, vm, "Retirar acesso a uma UBS", form, Revoke, "Retirar acesso", () => problem.Length > 0 ? problem : vm.Message);
    }
}
