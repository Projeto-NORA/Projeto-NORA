using System.Collections.ObjectModel;
using Nora.Desktop.Models;
using Nora.Desktop.Services;

namespace Nora.Desktop.ViewModels;

/// <summary>
/// OBS: Família. O responsável pede pelo aplicativo o vínculo com um dependente (CPF); a equipe da UBS do dependente confere os documentos e aprova ou recusa aqui.
/// Aprovado, o responsável age pelo dependente no aplicativo; a equipe pode encerrar o vínculo. Os dois estados (pedidos pendentes e vínculos aprovados) são lidos juntos,
/// só nesta aba, a cada ciclo de atualização e depois de cada decisão. Os métodos supõem que a confirmação já foi dada na tela.
/// </summary>
public sealed partial class WorkspaceViewModel
{
    public const int FamilyReasonMax = 280;
    private bool familyPendingLoaded, familyApprovedLoaded, familyFailed;
    private bool familyLoaded => familyPendingLoaded && familyApprovedLoaded;
    private FamilyLink? selectedPendingFamily, selectedApprovedFamily;
    private string familyRejectReason = "";

    public ObservableCollection<FamilyLink> FamilyPending { get; } = [];
    public ObservableCollection<FamilyLink> FamilyApproved { get; } = [];
    public bool ShowFamily => IsAuthenticated && IsOperational && Section == "family";
    public FamilyLink? SelectedPendingFamily { get => selectedPendingFamily; set { selectedPendingFamily = value; Notify(); Notify(nameof(CanDecideFamily)); } }
    public FamilyLink? SelectedApprovedFamily { get => selectedApprovedFamily; set { selectedApprovedFamily = value; Notify(); Notify(nameof(CanEndFamily)); } }
    public bool CanDecideFamily => CanOperate && HasActiveObsUnit && SelectedPendingFamily is not null;
    public bool CanEndFamily => CanOperate && HasActiveObsUnit && SelectedApprovedFamily is not null;
    /// <summary>Motivo opcional da recusa (de 1 a 280 caracteres); o responsável é informado da recusa e do motivo.</summary>
    public string FamilyRejectReason { get => familyRejectReason; set { familyRejectReason = value ?? ""; Notify(); Notify(nameof(FamilyRejectReasonError)); } }
    public string FamilyRejectReasonError => familyRejectReason.Trim().Length > FamilyReasonMax ? $"O motivo pode ter até {FamilyReasonMax} caracteres."
        : familyRejectReason.Any(char.IsControl) ? "O motivo não aceita quebra de linha nem caracteres de controle." : "";
    public string FamilyPendingState => !HasActiveObsUnit ? "Escolha uma UBS no Painel da UBS para ver os pedidos de família."
        : !familyPendingLoaded ? (familyFailed ? "Não foi possível ler os pedidos agora. Use Atualizar para tentar de novo." : "Carregando os pedidos…")
        : familyFailed ? "A última leitura não concluiu; estes são os pedidos da leitura anterior."
        : FamilyPending.Count == 0 ? "Nenhum pedido aguardando decisão nesta UBS." : "";
    public string FamilyApprovedState => !HasActiveObsUnit ? ""
        : !familyApprovedLoaded ? (familyFailed ? "Não foi possível ler os vínculos agora. Use Atualizar para tentar de novo." : "Carregando os vínculos…")
        : familyFailed ? "A última leitura não concluiu; estes são os vínculos da leitura anterior."
        : FamilyApproved.Count == 0 ? "Nenhum vínculo aprovado nesta UBS." : "";
    public string FamilySummary => !familyLoaded ? "" : $"{FamilyPending.Count} {(FamilyPending.Count == 1 ? "pedido aguardando" : "pedidos aguardando")} · {FamilyApproved.Count} {(FamilyApproved.Count == 1 ? "vínculo aprovado" : "vínculos aprovados")}";
    private static readonly string[] FamilyNames = [nameof(FamilyPendingState), nameof(FamilyApprovedState), nameof(FamilySummary), nameof(CanDecideFamily), nameof(CanEndFamily)];
    private void NotifyFamily() { foreach (var name in FamilyNames) Notify(name); }

    /// <summary>Aplica a leitura; mantém a seleção pelo id e não recria linhas que não mudaram.</summary>
    private void ApplyFamily(Tolerant<FamilyLink[]>? pendingRead, Tolerant<FamilyLink[]>? approvedRead)
    {
        if (pendingRead?.Value is { } pending)
        {
            var selected = SelectedPendingFamily?.Id;
            Replace(FamilyPending, pending.OrderBy(link => link.RequestedAt).ToArray());
            SelectedPendingFamily = selected is null ? null : FamilyPending.FirstOrDefault(link => link.Id == selected);
            familyPendingLoaded = true;
        }
        if (approvedRead?.Value is { } approved)
        {
            var selected = SelectedApprovedFamily?.Id;
            Replace(FamilyApproved, approved.OrderBy(link => link.GuardianName, StringComparer.CurrentCultureIgnoreCase).ThenBy(link => link.RequestedAt).ToArray());
            SelectedApprovedFamily = selected is null ? null : FamilyApproved.FirstOrDefault(link => link.Id == selected);
            familyApprovedLoaded = true;
        }
        familyFailed = (pendingRead?.Failed ?? false) || (approvedRead?.Failed ?? false);
        NotifyFamily();
    }

    public Task LoadFamilyAsync() => RunAsync(async () =>
    {
        var unit = ActiveObsUnit ?? throw new NoraApiException("Escolha uma UBS no Painel da UBS antes de ver os pedidos de família.");
        await LoadUnitScopeAsync(unit, UnitScope.Family);
        if (ActiveObsUnit?.Id == unit.Id && familyFailed) { Say(MessageKind.Error, "Não foi possível ler os pedidos de família agora. O que já estava na tela foi mantido."); return; }
        if (ActiveObsUnit?.Id == unit.Id) Say(MessageKind.Success, FamilyPending.Count == 0 ? "Lista atualizada: nenhum pedido aguardando." : $"Lista atualizada: {FamilySummary}.");
    });

    public Task ApproveFamilyAsync() => RunAsync(async () =>
    {
        var unit = ActiveObsUnit ?? throw new NoraApiException("Escolha uma UBS antes de aprovar o vínculo.");
        var link = SelectedPendingFamily ?? throw new NoraApiException("Selecione um pedido para aprovar.");
        await DecideFamilyAsync(unit, () => api.ApproveFamilyLinkAsync(unit.Id, link.Id));
        await ReloadAfterChangeAsync(unit, UnitScope.Family, $"Vínculo aprovado: {link.GuardianName} passa a agir por {link.DependentDisplay} no aplicativo.");
    });

    public Task RejectFamilyAsync() => RunAsync(async () =>
    {
        var unit = ActiveObsUnit ?? throw new NoraApiException("Escolha uma UBS antes de recusar o pedido.");
        var link = SelectedPendingFamily ?? throw new NoraApiException("Selecione um pedido para recusar.");
        if (FamilyRejectReasonError.Length > 0) throw new NoraApiException(FamilyRejectReasonError);
        var reason = string.IsNullOrWhiteSpace(familyRejectReason) ? null : familyRejectReason.Trim();
        await DecideFamilyAsync(unit, () => api.RejectFamilyLinkAsync(unit.Id, link.Id, reason));
        FamilyRejectReason = "";
        await ReloadAfterChangeAsync(unit, UnitScope.Family, $"Pedido de {link.GuardianName} recusado{(reason is null ? "" : " com o motivo informado")}.");
    });

    public Task EndFamilyAsync() => RunAsync(async () =>
    {
        var unit = ActiveObsUnit ?? throw new NoraApiException("Escolha uma UBS antes de encerrar o vínculo.");
        var link = SelectedApprovedFamily ?? throw new NoraApiException("Selecione um vínculo aprovado para encerrar.");
        await DecideFamilyAsync(unit, () => api.EndFamilyLinkAsync(unit.Id, link.Id));
        await ReloadAfterChangeAsync(unit, UnitScope.Family, $"Vínculo encerrado: {link.GuardianName} não age mais por {link.DependentDisplay}.");
    });

    /// <summary>Decisão ou encerramento: se outra pessoa já decidiu (409) ou o pedido sumiu (404), a lista é lida de novo para mostrar o que ficou valendo, e a mensagem do erro continua.</summary>
    private async Task DecideFamilyAsync(HealthUnit unit, Func<Task> decide)
    {
        try { await decide(); }
        catch (NoraApiException error) when (error.Status is 409 or 404 && !error.Unauthorized)
        {
            try { await LoadUnitScopeAsync(unit, UnitScope.Family); } catch (NoraApiException reading) when (!reading.Unauthorized) { MarkStale(); }
            throw;
        }
    }

    private void ClearFamily()
    {
        familyPendingLoaded = familyApprovedLoaded = familyFailed = false; FamilyPending.Clear(); FamilyApproved.Clear(); selectedPendingFamily = selectedApprovedFamily = null; familyRejectReason = "";
        Notify(nameof(SelectedPendingFamily)); Notify(nameof(SelectedApprovedFamily)); Notify(nameof(FamilyRejectReason)); Notify(nameof(FamilyRejectReasonError)); NotifyFamily();
    }
}
