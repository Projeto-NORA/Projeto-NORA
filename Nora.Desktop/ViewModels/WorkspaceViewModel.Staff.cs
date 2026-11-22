using System.Collections.ObjectModel;
using Nora.Desktop.Models;
using Nora.Desktop.Services;

namespace Nora.Desktop.ViewModels;

/// <summary>Contas OBS na Secretaria: lista (ativas e inativas), criação com senha provisória, desativar/reativar, redefinir senha e concessões de UBS.</summary>
public sealed partial class WorkspaceViewModel
{
    private StaffAccount? selectedStaff;
    private HealthUnit? selectedStaffUnit, selectedStaffGrant;
    private bool staffLoaded, staffLoading, staffFormSubmitted, applyingStaff;
    private string staffName = "", staffEmail = "", staffRole = "attendant", staffPasswordError = "", staffConfirmError = "", staffResetPasswordError = "", staffResetConfirmError = "";
    public ObservableCollection<StaffAccount> Staff { get; } = [];
    public ObservableCollection<HealthUnit> StaffUnits { get; } = [];
    public static IReadOnlyList<FilterOption> StaffRoleOptions { get; } = [new("attendant", "Atendente"), new("administrator", "Administrador")];

    public StaffAccount? SelectedStaff
    {
        get => selectedStaff;
        set
        {
            if (value is null && applyingStaff) return;
            // A lista recarregada devolve a mesma conta: a seleção e seus vínculos já lidos continuam.
            var same = selectedStaff?.Id == value?.Id;
            selectedStaff = value;
            if (!same) { StaffUnits.Clear(); SelectedStaffGrant = null; }
            Notify(); NotifyStaffActions();
        }
    }
    public HealthUnit? SelectedStaffUnit { get => selectedStaffUnit; set { selectedStaffUnit = value; Notify(); NotifyStaffActions(); } }
    public HealthUnit? SelectedStaffGrant { get => selectedStaffGrant; set { selectedStaffGrant = value; Notify(); NotifyStaffActions(); } }

    public bool HasSelectedStaff => selectedStaff is not null;
    public bool CanLoadStaffUnits => IsMunicipal && CanOperate && HasSelectedStaff;
    public bool CanGrantStaffUnit => IsMunicipal && CanOperate && HasSelectedStaff && selectedStaffUnit is not null;
    public bool CanRevokeStaffUnit => IsMunicipal && CanOperate && HasSelectedStaff && selectedStaffGrant is not null;
    public bool CanChangeStaffActive => IsMunicipal && CanOperate && HasSelectedStaff;
    public bool CanResetStaffPassword => IsMunicipal && CanOperate && HasSelectedStaff;
    public string StaffActiveActionLabel => selectedStaff is { IsActive: false } ? "Reativar conta" : "Desativar conta";
    public string SelectedStaffLabel => selectedStaff is { } account ? $"Conta selecionada: {account.FullName} · {account.RoleDisplay} · {account.StatusDisplay}" : "Nenhuma conta selecionada.";
    private void NotifyStaffActions()
    {
        foreach (var name in new[] { nameof(HasSelectedStaff), nameof(CanLoadStaffUnits), nameof(CanGrantStaffUnit), nameof(CanRevokeStaffUnit), nameof(CanChangeStaffActive), nameof(CanResetStaffPassword), nameof(StaffActiveActionLabel), nameof(SelectedStaffLabel) }) Notify(name);
    }
    public string StaffState => !IsAuthenticated ? "Entre na Secretaria para gerenciar as contas."
        : !staffLoaded ? (staffLoading ? "Carregando as contas…" : "As contas ainda não foram carregadas.")
        : Staff.Count == 0 ? "Nenhuma conta OBS cadastrada." : "";

    // Formulário de nova conta. As senhas nunca ficam no estado: chegam como argumento do envio e a tela limpa os campos.
    public string StaffName { get => staffName; set { staffName = value; Notify(); Notify(nameof(StaffNameError)); } }
    public string StaffEmail { get => staffEmail; set { staffEmail = value; Notify(); Notify(nameof(StaffEmailError)); } }
    public string StaffRole { get => staffRole; set { staffRole = value ?? "attendant"; Notify(); } }
    private bool ShowStaffError(string value) => staffFormSubmitted || value.Length > 0;
    public string StaffNameError => ShowStaffError(staffName) && (staffName.Trim().Length is < 2 or > 160 || staffName.Any(char.IsControl)) ? "Informe o nome completo, de 2 a 160 caracteres." : "";
    public string StaffEmailError => ShowStaffError(staffEmail) && !BrazilianDataValidator.IsValidEmail(staffEmail) ? "E-mail inválido. Use o formato nome@dominio.com." : "";
    public string StaffPasswordError { get => staffPasswordError; private set { if (staffPasswordError == value) return; staffPasswordError = value; Notify(); } }
    public string StaffConfirmError { get => staffConfirmError; private set { if (staffConfirmError == value) return; staffConfirmError = value; Notify(); } }
    public string StaffResetPasswordError { get => staffResetPasswordError; private set { if (staffResetPasswordError == value) return; staffResetPasswordError = value; Notify(); } }
    public string StaffResetConfirmError { get => staffResetConfirmError; private set { if (staffResetConfirmError == value) return; staffResetConfirmError = value; Notify(); } }
    private static readonly string[] StaffFormErrors = [nameof(StaffNameError), nameof(StaffEmailError)];

    private static string PasswordRuleError(string password) => password.Length is < 8 or > 256 ? "A senha provisória precisa ter de 8 a 256 caracteres." : "";
    private static string ConfirmError(string password, string confirm) => confirm == password ? "" : "A confirmação não é igual à senha provisória.";

    private async Task LoadStaffCoreAsync()
    {
        staffLoading = true; Notify(nameof(StaffState));
        try
        {
            var staffTask = api.StaffAsync(); var unitsTask = api.HealthUnitsAsync();
            await Task.WhenAll(staffTask, unitsTask);
            if (disposed || !IsAuthenticated) return;
            ApplyStaff(await staffTask); ApplyUnits(await unitsTask);
        }
        finally { staffLoading = false; if (!disposed) Notify(nameof(StaffState)); }
    }
    private void ApplyStaff(StaffAccount[] accounts)
    {
        var selectedId = selectedStaff?.Id;
        applyingStaff = true;
        try { Replace(Staff, accounts.OrderBy(account => account.FullName, StringComparer.CurrentCultureIgnoreCase).ThenBy(account => account.Id).ToArray()); }
        finally { applyingStaff = false; }
        staffLoaded = true; Notify(nameof(StaffState));
        if (selectedId is { } id) SelectedStaff = Staff.FirstOrDefault(account => account.Id == id);
    }

    public Task LoadStaffAsync() => RunAsync(async () =>
    {
        if (!IsMunicipal || !IsAuthenticated) throw new NoraApiException("Entre na Secretaria para gerenciar acessos.");
        await LoadStaffCoreAsync();
        Say(MessageKind.Success, "Contas e unidades atualizadas. Selecione uma conta para ver seus vínculos.");
    });
    public Task LoadStaffUnitsAsync() => RunAsync(async () =>
    {
        var staff = SelectedStaff ?? throw new NoraApiException("Selecione uma conta OBS.");
        var units = await api.StaffUnitsAsync(staff.Id);
        if (disposed || SelectedStaff?.Id != staff.Id) return;
        Replace(StaffUnits, units.OrderBy(unit => unit.Name, StringComparer.CurrentCultureIgnoreCase).ToArray()); SelectedStaffGrant = null; Say(MessageKind.Success, "Vínculos atualizados.");
    });
    public Task ChangeStaffUnitAsync(bool grant) => RunAsync(async () =>
    {
        var staff = SelectedStaff ?? throw new NoraApiException("Selecione uma conta OBS.");
        var unit = (grant ? SelectedStaffUnit : SelectedStaffGrant) ?? throw new NoraApiException("Selecione uma UBS.");
        await api.SetStaffUnitAsync(staff.Id, unit.Id, grant);
        var units = await api.StaffUnitsAsync(staff.Id);
        if (disposed || SelectedStaff?.Id != staff.Id) return;
        Replace(StaffUnits, units.OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase).ToArray()); SelectedStaffGrant = null;
        Say(MessageKind.Success, grant ? "Vínculo concedido e registrado." : "Vínculo revogado e registrado.");
    });

    /// <summary>Cria a conta OBS com senha provisória (troca obrigatória no primeiro acesso). Devolve se criou, para a tela limpar as senhas.</summary>
    public async Task<bool> CreateStaffAsync(string password, string confirm)
    {
        var created = false;
        await RunAsync(async () =>
        {
            if (!IsMunicipal || !IsAuthenticated) throw new NoraApiException("Entre na Secretaria para criar contas.");
            staffFormSubmitted = true;
            foreach (var name in StaffFormErrors) Notify(name);
            StaffPasswordError = PasswordRuleError(password); StaffConfirmError = StaffPasswordError.Length > 0 ? "" : ConfirmError(password, confirm);
            if (StaffNameError.Length + StaffEmailError.Length + StaffPasswordError.Length + StaffConfirmError.Length > 0)
                throw new NoraApiException("Corrija os campos destacados no formulário da conta.");
            var account = await api.CreateStaffAsync(staffName.Trim(), staffEmail.Trim(), staffRole, password);
            if (disposed || !IsAuthenticated) return;
            ClearStaffForm(); created = true;
            Say(MessageKind.Success, $"Conta de {account.FullName} criada. A pessoa será obrigada a trocar a senha provisória no primeiro acesso.");
            ApplyStaff(await api.StaffAsync());
        });
        return created;
    }
    private void ClearStaffForm()
    {
        staffFormSubmitted = false; staffName = staffEmail = ""; staffRole = "attendant"; StaffPasswordError = ""; StaffConfirmError = "";
        foreach (var name in new[] { nameof(StaffName), nameof(StaffEmail), nameof(StaffRole) }) Notify(name);
        foreach (var name in StaffFormErrors) Notify(name);
    }

    /// <summary>Desativa ou reativa a conta selecionada (a confirmação é pedida pela tela). Desativar encerra as sessões e preserva as concessões de UBS.</summary>
    public Task ChangeStaffActiveAsync() => RunAsync(async () =>
    {
        var staff = SelectedStaff ?? throw new NoraApiException("Selecione uma conta OBS.");
        var activate = !staff.IsActive;
        await api.SetStaffActiveAsync(staff.Id, activate);
        if (disposed || !IsAuthenticated) return;
        Say(MessageKind.Success, activate ? $"Conta de {staff.FullName} reativada. As concessões de UBS preservadas voltam a valer." : $"Conta de {staff.FullName} desativada: as sessões foram encerradas e as concessões de UBS foram preservadas.");
        ApplyStaff(await api.StaffAsync());
    });

    /// <summary>Redefine a senha da conta selecionada para uma senha provisória, com troca obrigatória e sessões encerradas.</summary>
    public async Task<bool> ResetStaffPasswordAsync(string password, string confirm)
    {
        var done = false;
        await RunAsync(async () =>
        {
            var staff = SelectedStaff ?? throw new NoraApiException("Selecione uma conta OBS.");
            if (!ValidateStaffResetPassword(password, confirm)) return;
            await api.ResetStaffPasswordAsync(staff.Id, password);
            if (disposed || !IsAuthenticated) return;
            done = true;
            Say(MessageKind.Success, $"Senha provisória de {staff.FullName} definida: as sessões foram encerradas e a troca será exigida no próximo acesso.");
            ApplyStaff(await api.StaffAsync());
        });
        return done;
    }
    /// <summary>Confere a senha provisória digitada antes de pedir a confirmação; os erros aparecem junto aos campos.</summary>
    public bool ValidateStaffResetPassword(string password, string confirm)
    {
        StaffResetPasswordError = PasswordRuleError(password); StaffResetConfirmError = StaffResetPasswordError.Length > 0 ? "" : ConfirmError(password, confirm);
        return StaffResetPasswordError.Length + StaffResetConfirmError.Length == 0;
    }
    public void ClearStaffResetErrors() { StaffResetPasswordError = ""; StaffResetConfirmError = ""; }

    private void ClearStaff()
    {
        Staff.Clear(); StaffUnits.Clear(); selectedStaff = null; selectedStaffUnit = null; selectedStaffGrant = null; staffLoaded = false; staffLoading = false;
        ClearStaffForm(); ClearStaffResetErrors(); ClearHistory();
        foreach (var name in new[] { nameof(SelectedStaff), nameof(SelectedStaffUnit), nameof(SelectedStaffGrant), nameof(StaffState) }) Notify(name);
        NotifyStaffActions();
    }
}
