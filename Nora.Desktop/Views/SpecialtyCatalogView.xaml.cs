using System.Windows;
using System.Windows.Controls;
using Nora.Desktop.Models;
using Nora.Desktop.Services;
using Nora.Desktop.ViewModels;

namespace Nora.Desktop.Views;

/// <summary>Lista de especialidades da rede (Secretaria): criar e renomear em pop-up, desativar (lixeira, com confirmação) e reativar pelo balão.</summary>
public partial class SpecialtyCatalogView : UserControl
{
    public SpecialtyCatalogView()
    {
        InitializeComponent();
        MunicipalDetailsHost.Attach(SpecialtyList, this);
        ListingBehavior.AddBalloonRefreshedHandler(SpecialtyList, OnBalloonRefreshed);
    }

    private WorkspaceViewModel? Workspace => DataContext as WorkspaceViewModel;
    private IReadOnlyList<Specialty> Selected => ListingBehavior.GetSelectedItems<Specialty>(SpecialtyList);

    /// <summary>Desativar serve às ativas da seleção e Reativar às desativadas; a ação que não serve a nenhuma some.</summary>
    private void OnBalloonRefreshed(object sender, RoutedEventArgs args)
    {
        var selected = Selected;
        DeactivateButton.Visibility = selected.Any(item => item.IsActive) ? Visibility.Visible : Visibility.Collapsed;
        ReactivateButton.Visibility = selected.Any(item => !item.IsActive) ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnNew(object sender, RoutedEventArgs e)
    {
        if (Workspace is not { } vm) return;
        vm.NewSpecialtyDraft();
        OpenForm(vm);
    }

    private void OnRename(object sender, RoutedEventArgs e)
    {
        if (Workspace is not { } vm || Selected.FirstOrDefault() is not { } specialty) return;
        vm.SelectedSpecialty = specialty;
        vm.EditSelectedSpecialty();
        OpenForm(vm);
    }

    /// <summary>O erro de nome repetido (409) aparece dentro do pop-up, junto do campo e na faixa de erro dele.</summary>
    private void OpenForm(WorkspaceViewModel vm) =>
        MunicipalDetailsHost.ShowForm(this, vm, vm.SpecialtyDraftTitle, new SpecialtyFormView { DataContext = vm }, vm.SaveSpecialtyAsync, vm.SpecialtySaveLabel, () => vm.Message);

    private async void OnDeactivate(object sender, RoutedEventArgs e)
    {
        if (Workspace is not { } vm) return;
        var items = Selected.Where(item => item.IsActive).ToArray();
        if (items.Length == 0) return;
        var question = items.Length == 1
            ? $"Desativar {items[0].Name}? Ela sai da lista do paciente e de novos vínculos e horários. O que já existe (profissionais, grade e consultas) é preservado."
            : $"Desativar as {items.Length} especialidades selecionadas? Elas saem da lista do paciente e de novos vínculos e horários. O que já existe (profissionais, grade e consultas) é preservado.";
        if (!Confirmation.Ask(Window.GetWindow(this), "Desativar especialidade", question)) return;
        await vm.ChangeSpecialtiesActiveAsync(items, activate: false);
    }

    private async void OnReactivate(object sender, RoutedEventArgs e)
    {
        if (Workspace is not { } vm) return;
        var items = Selected.Where(item => !item.IsActive).ToArray();
        if (items.Length > 0) await vm.ChangeSpecialtiesActiveAsync(items, activate: true);
    }
}
