using System.Windows;
using System.Windows.Controls;
using Nora.Desktop.Services;
using Nora.Desktop.ViewModels;

namespace Nora.Desktop.Views;

/// <summary>Configurações da UBS (OBS): profissionais, consultórios, grade semanal e bloqueios. As ações que tiram algo do ar pedem confirmação.</summary>
public partial class UnitSettingsView : UserControl
{
    public UnitSettingsView() => InitializeComponent();
    private WorkspaceViewModel? Workspace => DataContext as WorkspaceViewModel;
    private async void OnRefresh(object sender, RoutedEventArgs e) { if (Workspace is { } vm) await vm.LoadCatalogAsync(); }

    private async void OnSaveProfile(object sender, RoutedEventArgs e) { if (Workspace is { CanSaveProfile: true } vm) await vm.SaveProfileAsync(); }
    private async void OnRefreshDevices(object sender, RoutedEventArgs e) { if (Workspace is { } vm) await vm.LoadDevicesAsync(); }
    private async void OnActivateThisComputer(object sender, RoutedEventArgs e) { if (Workspace is { CanActivateThisComputer: true } vm) await vm.ActivateThisComputerAsync(); }
    private async void OnCreateDeviceCode(object sender, RoutedEventArgs e) { if (Workspace is { CanCreateDeviceCode: true } vm) await vm.CreateDeviceCodeAsync(); }
    private async void OnIssueDeviceCode(object sender, RoutedEventArgs e) { if (Workspace is { CanIssueNewDeviceCode: true } vm) await vm.IssueNewDeviceCodeAsync(); }
    private async void OnRevokeDevice(object sender, RoutedEventArgs e)
    {
        if (Workspace is not { CanRevokeDevice: true } vm) return;
        if (!Confirmation.Ask(Window.GetWindow(this), "Revogar computador", vm.RevokeConfirmationText)) return;
        await vm.RevokeDeviceAsync();
    }
    private void OnCopyIssuedCode(object sender, RoutedEventArgs e) => Workspace?.CopyIssuedCode();
    private void OnCloseIssuedCode(object sender, RoutedEventArgs e) => Workspace?.CloseIssuedCode();

    private void OnNewProfessional(object sender, RoutedEventArgs e) => Workspace?.NewProfessionalDraft();
    private void OnEditProfessional(object sender, RoutedEventArgs e) => Workspace?.EditSelectedProfessional();
    private void OnSpecialtyChoice(object sender, RoutedEventArgs e) => Workspace?.SpecialtyChoiceChanged();
    private async void OnSaveProfessional(object sender, RoutedEventArgs e) { if (Workspace is { } vm) await vm.SaveProfessionalAsync(); }
    private async void OnToggleProfessional(object sender, RoutedEventArgs e)
    {
        if (Workspace is not { SelectedProfessional: { } professional, CanChangeProfessional: true } vm) return;
        if (professional.IsActive && !Confirmation.Ask(Window.GetWindow(this), "Desativar profissional",
            $"Desativar {professional.FullName}? Ele sai dos horários livres e do agendamento. As consultas já marcadas continuam como estavam."))
            return;
        await vm.ChangeProfessionalActiveAsync(professional.Id, !professional.IsActive);
    }

    private void OnNewRoom(object sender, RoutedEventArgs e) => Workspace?.NewRoomDraft();
    private void OnEditRoom(object sender, RoutedEventArgs e) => Workspace?.EditSelectedRoom();
    private async void OnSaveRoom(object sender, RoutedEventArgs e) { if (Workspace is { } vm) await vm.SaveRoomAsync(); }
    private async void OnToggleRoom(object sender, RoutedEventArgs e)
    {
        if (Workspace is not { SelectedRoom: { } room, CanChangeRoom: true } vm) return;
        if (room.IsActive && !Confirmation.Ask(Window.GetWindow(this), "Desativar consultório", $"Desativar {room.Name}? Os horários dele saem da disponibilidade; a grade que aponta para ele continua cadastrada."))
            return;
        await vm.ChangeRoomActiveAsync(room.Id, !room.IsActive);
    }

    private void OnNewSchedule(object sender, RoutedEventArgs e) => Workspace?.NewScheduleDraft();
    private void OnEditSchedule(object sender, RoutedEventArgs e) => Workspace?.EditSelectedSchedule();
    private async void OnSaveSchedule(object sender, RoutedEventArgs e) { if (Workspace is { } vm) await vm.SaveScheduleAsync(); }
    private async void OnDeleteSchedule(object sender, RoutedEventArgs e)
    {
        if (Workspace is not { SelectedSchedule: { } row, CanChangeSchedule: true } vm) return;
        if (!Confirmation.Ask(Window.GetWindow(this), "Remover horário da grade", $"Remover {row.WeekdayDisplay} {row.RangeDisplay} de {row.ProfessionalName}? Os horários livres dessa linha deixam de ser oferecidos; consultas já marcadas não são afetadas."))
            return;
        await vm.DeleteScheduleAsync();
    }

    private async void OnSaveBlock(object sender, RoutedEventArgs e) { if (Workspace is { } vm) await vm.SaveBlockAsync(); }
    private async void OnDeleteBlock(object sender, RoutedEventArgs e)
    {
        if (Workspace is not { SelectedBlock: { } block, CanChangeBlock: true } vm) return;
        if (!Confirmation.Ask(Window.GetWindow(this), "Remover bloqueio", $"Remover o bloqueio \"{block.Reason}\" ({block.PeriodDisplay})? Os horários que ele cobria voltam a ser oferecidos."))
            return;
        await vm.DeleteBlockAsync();
    }
}
