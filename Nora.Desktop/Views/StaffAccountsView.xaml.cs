using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Nora.Desktop.Services;
using Nora.Desktop.ViewModels;

namespace Nora.Desktop.Views;

/// <summary>Contas do OBS: lista, desativar/reativar e redefinir senha provisória, sempre com confirmação.</summary>
public partial class StaffAccountsView : UserControl
{
    private WorkspaceViewModel? observed;
    public StaffAccountsView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Observe();
        Loaded += (_, _) => Observe();
        Unloaded += (_, _) => { ClearPasswords(); Unobserve(); };
    }
    private void Observe() { Unobserve(); observed = DataContext as WorkspaceViewModel; if (observed != null) observed.PropertyChanged += OnChanged; }
    private void Unobserve() { if (observed != null) observed.PropertyChanged -= OnChanged; observed = null; }
    private void OnChanged(object? sender, PropertyChangedEventArgs args)
    {
        // A senha digitada pertence à conta que estava selecionada: trocar a seleção ou sair apaga os campos.
        if (args.PropertyName is nameof(WorkspaceViewModel.IsAuthenticated) or nameof(WorkspaceViewModel.SelectedStaff)) ClearPasswords();
    }
    private void ClearPasswords() { ResetPassword.Clear(); ResetConfirm.Clear(); }
    private async void OnLoad(object sender, RoutedEventArgs e) { if (DataContext is WorkspaceViewModel vm) await vm.LoadStaffAsync(); }
    private async void OnChangeActive(object sender, RoutedEventArgs e)
    {
        if (DataContext is not WorkspaceViewModel { SelectedStaff: { } staff, CanChangeStaffActive: true } vm) return;
        var question = staff.IsActive
            ? $"Desativar a conta de {staff.FullName}? As sessões abertas serão encerradas e a pessoa não poderá entrar. As concessões de UBS são preservadas."
            : $"Reativar a conta de {staff.FullName}? A pessoa volta a poder entrar com a senha atual, e as concessões de UBS preservadas voltam a valer.";
        if (!Confirmation.Ask(Window.GetWindow(this), staff.IsActive ? "Confirmar desativação" : "Confirmar reativação", question)) return;
        await vm.ChangeStaffActiveAsync();
    }
    private async void OnReset(object sender, RoutedEventArgs e)
    {
        if (DataContext is not WorkspaceViewModel { SelectedStaff: { } staff, CanResetStaffPassword: true } vm) return;
        if (!vm.ValidateStaffResetPassword(ResetPassword.Password, ResetConfirm.Password)) return;
        if (!Confirmation.Ask(Window.GetWindow(this), "Confirmar redefinição de senha", $"Definir uma nova senha provisória para {staff.FullName}? As sessões da conta serão encerradas e a pessoa será obrigada a trocar a senha no próximo acesso.")) return;
        if (await vm.ResetStaffPasswordAsync(ResetPassword.Password, ResetConfirm.Password)) ClearPasswords();
    }
}
