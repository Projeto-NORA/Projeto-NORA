using System.Windows;
using System.Windows.Controls;
using Nora.Desktop.Models;
using Nora.Desktop.Services;
using Nora.Desktop.ViewModels;

namespace Nora.Desktop.Views;

/// <summary>Cadastro de UBS: listagem aberta com balão (Editar e "Tentar criar a base de novo"), ficha em linhas e o cadastro em pop-up.</summary>
public partial class HealthUnitsView : UserControl
{
    public HealthUnitsView()
    {
        InitializeComponent();
        MunicipalDetailsHost.Attach(UnitList, this);
        ListingBehavior.AddBalloonRefreshedHandler(UnitList, OnBalloonRefreshed);
    }

    private WorkspaceViewModel? Workspace => DataContext as WorkspaceViewModel;
    private HealthUnit? SelectedUnit => ListingBehavior.GetSelectedItems<HealthUnit>(UnitList).FirstOrDefault();

    /// <summary>"Tentar criar a base de novo" só serve à UBS cuja base está pendente.</summary>
    private void OnBalloonRefreshed(object sender, RoutedEventArgs args) =>
        ProvisionButton.Visibility = ListingBehavior.GetSelectedItems<HealthUnit>(UnitList) is [{ DatabasePending: true }] ? Visibility.Visible : Visibility.Collapsed;

    private void OnNew(object sender, RoutedEventArgs e)
    {
        if (Workspace is not { } vm) return;
        vm.NewUnitDraft();
        OpenForm(vm);
    }

    private void OnEdit(object sender, RoutedEventArgs e)
    {
        if (Workspace is not { } vm || SelectedUnit is not { } unit) return;
        vm.SelectedUnit = unit;
        vm.EditSelectedUnit();
        OpenForm(vm);
    }

    /// <summary>Cadastro e edição no pop-up: os passos (localizar o endereço, salvar) aparecem dentro dele; erros da API e dos campos ficam nele também.</summary>
    private void OpenForm(WorkspaceViewModel vm) =>
        MunicipalDetailsHost.ShowForm(this, vm, vm.UnitDraftTitle, new UnitFormView { DataContext = vm }, vm.SaveUnitAsync, "Salvar UBS", () => vm.Message);

    private void OnProvision(object sender, RoutedEventArgs e)
    {
        if (Workspace is not { } vm || SelectedUnit is not { DatabasePending: true } unit) return;
        vm.SelectedUnit = unit;
        vm.UnitProvisionSteps.Clear();
        MunicipalDetailsHost.ShowForm(this, vm, "Criar a base da UBS", new UnitProvisionFormView { DataContext = vm }, vm.ProvisionSelectedUnitDatabaseAsync, "Criar a base", () => vm.Message);
    }
}
