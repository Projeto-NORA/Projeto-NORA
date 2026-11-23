using System.Windows;
using System.Windows.Controls;
using Nora.Desktop.Services;
using Nora.Desktop.ViewModels;

namespace Nora.Desktop.Views;

/// <summary>Lista de especialidades da rede (Secretaria): criar, renomear, ativar e desativar.</summary>
public partial class SpecialtyCatalogView : UserControl
{
    public SpecialtyCatalogView() => InitializeComponent();
    private WorkspaceViewModel? Workspace => DataContext as WorkspaceViewModel;
    private async void OnRefresh(object sender, RoutedEventArgs e) { if (Workspace is { } vm) await vm.LoadSpecialtyCatalogAsync(); }
    private void OnNew(object sender, RoutedEventArgs e) { Workspace?.NewSpecialtyDraft(); SpecialtyForm.BringIntoView(); }
    private void OnEdit(object sender, RoutedEventArgs e) { Workspace?.EditSelectedSpecialty(); SpecialtyForm.BringIntoView(); }
    private async void OnSave(object sender, RoutedEventArgs e) { if (Workspace is { } vm) await vm.SaveSpecialtyAsync(); }
    private async void OnToggleActive(object sender, RoutedEventArgs e)
    {
        if (Workspace is not { SelectedSpecialty: { } specialty, CanChangeSpecialty: true } vm) return;
        if (specialty.IsActive && !Confirmation.Ask(Window.GetWindow(this), "Desativar especialidade",
            $"Desativar {specialty.Name}? Ela sai da lista do paciente e de novos vínculos e horários. O que já existe (profissionais, grade e consultas) é preservado."))
            return;
        await vm.ChangeSpecialtyActiveAsync();
    }
}
