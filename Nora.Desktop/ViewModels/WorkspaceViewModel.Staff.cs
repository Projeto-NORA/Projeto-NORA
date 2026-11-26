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
    // UBS com acesso de cada conta (a API só informa por conta): lidas na primeira carga, para contas novas e a cada 5 minutos, no máximo 4 por vez.
    private static readonly TimeSpan StaffUnitsRefreshAge = TimeSpan.FromMinutes(5);
    private readonly Dictionary<Guid, string[]> staffUnitNames = [];
    private DateTimeOffset? staffUnitsReadAt;
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
            var accounts = await staffTask;
            await ReadStaffUnitNamesAsync(accounts);
            if (disposed || !IsAuthenticated) return;
            ApplyStaff(accounts); ApplyUnits(await unitsTask);
        }
        finally { staffLoading = false; if (!disposed) Notify(nameof(StaffState)); }
    }
    private void ApplyStaff(StaffAccount[] accounts)
    {
        var selectedId = selectedStaff?.Id;
        applyingStaff = true;
        try { Replace(Staff, DecorateStaff(accounts).OrderBy(account => account.FullName, StringComparer.CurrentCultureIgnoreCase).ThenBy(account => account.Id).ToArray()); }
        finally { applyingStaff = false; }
        staffLoaded = true; Notify(nameof(StaffState));
        if (selectedId is { } id) SelectedStaff = Staff.FirstOrDefault(account => account.Id == id);
    }
    /// <summary>Lê as contas de novo (depois de gravar algo) e aplica na lista.</summary>
    private async Task ReloadStaffAsync()
    {
        var accounts = await api.StaffAsync();
        if (disposed || !IsAuthenticated) return;
        await ReadStaffUnitNamesAsync(accounts);
        if (disposed || !IsAuthenticated) return;
        ApplyStaff(accounts);
    }

    private StaffAccount[] DecorateStaff(IEnumerable<StaffAccount> accounts) =>
        [.. accounts.Select(account => staffUnitNames.TryGetValue(account.Id, out var names) ? account with { UnitsDisplay = names.Length == 0 ? "Sem UBS" : string.Join(", ", names) } : account)];

    /// <summary>Lê as UBS com acesso das contas que ainda não foram lidas (todas, se a última leitura passou de 5 minutos). Falha de uma conta só deixa a UBS dela fora da linha; sessão recusada sobe.</summary>
    private async Task ReadStaffUnitNamesAsync(StaffAccount[] accounts)
    {
        var stale = staffUnitsReadAt is null || DateTimeOffset.Now - staffUnitsReadAt >= StaffUnitsRefreshAge;
        var pending = accounts.Where(account => stale || !staffUnitNames.ContainsKey(account.Id)).ToArray();
        if (pending.Length > 0)
        {
            using var limit = new SemaphoreSlim(4);
            await Task.WhenAll(pending.Select(async account =>
            {
                await limit.WaitAsync();
                try { staffUnitNames[account.Id] = NamesOf(await api.StaffUnitsAsync(account.Id)); }
                catch (NoraApiException error) when (!error.Unauthorized) { }
                finally { limit.Release(); }
            }));
            if (stale) staffUnitsReadAt = DateTimeOffset.Now;
        }
        foreach (var gone in staffUnitNames.Keys.Except(accounts.Select(account => account.Id)).ToArray()) staffUnitNames.Remove(gone);
    }
    private static string[] NamesOf(IEnumerable<HealthUnit> units) => [.. units.Select(unit => unit.Name).OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)];

    /// <summary>Lê as UBS com acesso da conta selecionada (ao abrir conceder ou retirar acesso).</summary>
    public Task LoadStaffUnitsAsync() => RunAsync(async () =>
    {
        var staff = SelectedStaff ?? throw new NoraApiException("Selecione uma conta OBS.");
        var units = await api.StaffUnitsAsync(staff.Id);
        if (disposed || SelectedStaff?.Id != staff.Id) return;
        Replace(StaffUnits, units.OrderBy(unit => unit.Name, StringComparer.CurrentCultureIgnoreCase).ToArray()); SelectedStaffGrant = null;
    });
    /// <summary>Concede ou retira o acesso da conta selecionada à UBS escolhida (a confirmação é pedida pela tela) e atualiza a lista de contas. Devolve se concluiu.</summary>
    public async Task<bool> ChangeStaffUnitAsync(bool grant)
    {
        var done = false;
        await RunAsync(async () =>
        {
            var staff = SelectedStaff ?? throw new NoraApiException("Selecione uma conta OBS.");
            var unit = (grant ? SelectedStaffUnit : SelectedStaffGrant) ?? throw new NoraApiException("Selecione uma UBS.");
            await api.SetStaffUnitAsync(staff.Id, unit.Id, grant);
            var units = await api.StaffUnitsAsync(staff.Id);
            if (disposed || SelectedStaff?.Id != staff.Id) return;
            Replace(StaffUnits, units.OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase).ToArray()); SelectedStaffGrant = null;
            staffUnitNames[staff.Id] = NamesOf(units);
            ApplyStaff(Staff.ToArray());
            done = true;
            Say(MessageKind.Success, grant ? "Vínculo concedido e registrado." : "Vínculo revogado e registrado.");
        });
        return done;
    }

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
            await ReloadStaffAsync();
        });
        return created;
    }
    /// <summary>Começa um cadastro de conta do zero (ao abrir o pop-up "Nova conta").</summary>
    public void NewStaffDraft() => ClearStaffForm();
    private void ClearStaffForm()
    {
        staffFormSubmitted = false; staffName = staffEmail = ""; staffRole = "attendant"; StaffPasswordError = ""; StaffConfirmError = "";
        foreach (var name in new[] { nameof(StaffName), nameof(StaffEmail), nameof(StaffRole) }) Notify(name);
        foreach (var name in StaffFormErrors) Notify(name);
    }

    /// <summary>
    /// Desativa ou reativa as contas escolhidas (a confirmação, uma só para o lote, é pedida pela tela). Desativar encerra as sessões e preserva as concessões de UBS.
    /// A lista de contas é lida de novo uma vez, no fim.
    /// </summary>
    public Task ChangeStaffActiveAsync(IReadOnlyList<StaffAccount> accounts, bool activate) => RunAsync(async () =>
    {
        if (accounts.Count == 0) throw new NoraApiException("Selecione uma conta OBS.");
        foreach (var staff in accounts) await api.SetStaffActiveAsync(staff.Id, activate);
        if (disposed || !IsAuthenticated) return;
        var who = accounts.Count == 1 ? $"Conta de {accounts[0].FullName}" : $"{accounts.Count} contas";
        Say(MessageKind.Success, activate
            ? $"{who} {(accounts.Count == 1 ? "reativada" : "reativadas")}. As concessões de UBS preservadas voltam a valer."
            : $"{who} {(accounts.Count == 1 ? "desativada" : "desativadas")}: as sessões foram encerradas e as concessões de UBS foram preservadas.");
        await ReloadStaffAsync();
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
            await ReloadStaffAsync();
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
        staffUnitNames.Clear(); staffUnitsReadAt = null;
        ClearStaffForm(); ClearStaffResetErrors(); ClearHistory();
        foreach (var name in new[] { nameof(SelectedStaff), nameof(SelectedStaffUnit), nameof(SelectedStaffGrant), nameof(StaffState) }) Notify(name);
        NotifyStaffActions();
    }
}
