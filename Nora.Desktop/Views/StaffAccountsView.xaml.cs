using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Nora.Desktop.Models;
using Nora.Desktop.Services;
using Nora.Desktop.ViewModels;

namespace Nora.Desktop.Views;

/// <summary>
/// Contas do OBS: listagem aberta, conta nova e redefinição de senha provisória em pop-up, desativar (lixeira) e reativar pelo balão, sempre com confirmação.
/// A rota "Nova conta" mostra esta mesma tela e abre o pop-up de conta nova.
/// </summary>
public partial class StaffAccountsView : UserControl
{
    private ShellWindow? shell;
    private bool routeFormShown;

    public StaffAccountsView()
    {
        InitializeComponent();
        MunicipalDetailsHost.Attach(StaffList, this);
        ListingBehavior.AddBalloonRefreshedHandler(StaffList, OnBalloonRefreshed);
        Loaded += (_, _) => { Watch(); OpenForRoute(); };
        Unloaded += (_, _) => Unwatch();
        IsVisibleChanged += (_, _) => OpenForRoute();
    }

    private WorkspaceViewModel? Workspace => DataContext as WorkspaceViewModel;
    private IReadOnlyList<StaffAccount> Selected => ListingBehavior.GetSelectedItems<StaffAccount>(StaffList);

    private void Watch()
    {
        Unwatch();
        shell = Window.GetWindow(this) as ShellWindow;
        if (shell is not null) shell.ViewModel.PropertyChanged += OnShellChanged;
    }

    private void Unwatch()
    {
        if (shell is not null) shell.ViewModel.PropertyChanged -= OnShellChanged;
        shell = null;
    }

    private void OnShellChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(ShellViewModel.SubPage) or nameof(ShellViewModel.Route)) OpenForRoute();
    }

    /// <summary>O item de menu "Nova conta" (rota <c>users:create</c>) leva a esta tela já com o pop-up aberto; a rota continua existindo para quem a tem salva nas preferências.</summary>
    private void OpenForRoute()
    {
        if (shell?.ViewModel.SubPage != "create") { routeFormShown = false; return; }
        if (routeFormShown || !IsVisible || Workspace is not { IsAuthenticated: true }) return;
        routeFormShown = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Input, OpenCreateForm);
    }

    /// <summary>Desativar serve às contas ativas da seleção e Reativar às desativadas; a ação que não serve a nenhuma some. Redefinir senha só vale para uma conta.</summary>
    private void OnBalloonRefreshed(object sender, RoutedEventArgs args)
    {
        var selected = Selected;
        DeactivateButton.Visibility = selected.Any(item => item.IsActive) ? Visibility.Visible : Visibility.Collapsed;
        ReactivateButton.Visibility = selected.Any(item => !item.IsActive) ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnNew(object sender, RoutedEventArgs e) => OpenCreateForm();

    /// <summary>Conta nova no pop-up. A senha provisória só existe nos campos: segue como argumento do envio e os campos são limpos ao criar ou fechar.</summary>
    private void OpenCreateForm()
    {
        if (Workspace is not { } vm) return;
        vm.NewStaffDraft();
        var form = new StaffCreateFormView { DataContext = vm };
        async Task<bool> Create() { var created = await vm.CreateStaffAsync(form.Password, form.Confirm); if (created) form.ClearPasswords(); return created; }
        try { DialogService.ShowForm(Window.GetWindow(this), "Nova conta OBS", form, Create, "Criar conta", error: () => vm.Message); }
        finally { form.ClearPasswords(); }
    }

    private void OnReset(object sender, RoutedEventArgs e)
    {
        if (Workspace is not { } vm || Selected.FirstOrDefault() is not { } account) return;
        vm.SelectedStaff = account;
        vm.ClearStaffResetErrors();
        var form = new StaffResetFormView { DataContext = vm };
        var problem = "";
        async Task<bool> Reset()
        {
            problem = "";
            if (!vm.ValidateStaffResetPassword(form.Password, form.Confirm)) { problem = "Corrija os campos destacados."; return false; }
            if (!Confirmation.Ask(null, "Confirmar redefinição de senha", $"Definir uma nova senha provisória para {account.FullName}? As sessões da conta serão encerradas e a pessoa será obrigada a trocar a senha no próximo acesso.")) { problem = "A redefinição não foi confirmada."; return false; }
            var done = await vm.ResetStaffPasswordAsync(form.Password, form.Confirm);
            if (done) form.ClearPasswords();
            return done;
        }
        try { DialogService.ShowForm(Window.GetWindow(this), "Redefinir senha provisória", form, Reset, "Redefinir senha", error: () => problem.Length > 0 ? problem : vm.Message); }
        finally { form.ClearPasswords(); vm.ClearStaffResetErrors(); }
    }

    private async void OnDeactivate(object sender, RoutedEventArgs e)
    {
        if (Workspace is not { } vm) return;
        var accounts = Selected.Where(item => item.IsActive).ToArray();
        if (accounts.Length == 0) return;
        var question = accounts.Length == 1
            ? $"Desativar a conta de {accounts[0].FullName}? As sessões abertas serão encerradas e a pessoa não poderá entrar. As concessões de UBS são preservadas."
            : $"Desativar as {accounts.Length} contas selecionadas? As sessões abertas serão encerradas e as pessoas não poderão entrar. As concessões de UBS são preservadas.";
        if (!Confirmation.Ask(Window.GetWindow(this), "Confirmar desativação", question)) return;
        await vm.ChangeStaffActiveAsync(accounts, activate: false);
    }

    private async void OnReactivate(object sender, RoutedEventArgs e)
    {
        if (Workspace is not { } vm) return;
        var accounts = Selected.Where(item => !item.IsActive).ToArray();
        if (accounts.Length == 0) return;
        var question = accounts.Length == 1
            ? $"Reativar a conta de {accounts[0].FullName}? A pessoa volta a poder entrar com a senha atual, e as concessões de UBS preservadas voltam a valer."
            : $"Reativar as {accounts.Length} contas selecionadas? As pessoas voltam a poder entrar com a senha atual, e as concessões de UBS preservadas voltam a valer.";
        if (!Confirmation.Ask(Window.GetWindow(this), "Confirmar reativação", question)) return;
        await vm.ChangeStaffActiveAsync(accounts, activate: true);
    }
}
