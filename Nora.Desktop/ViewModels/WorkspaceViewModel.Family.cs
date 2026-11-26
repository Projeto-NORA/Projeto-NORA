using System.Collections.ObjectModel;
using Nora.Desktop.Models;
using Nora.Desktop.Services;

namespace Nora.Desktop.ViewModels;

/// <summary>
/// OBS: Família e Vínculos aprovados. O responsável pede pelo aplicativo o vínculo com um dependente (CPF); a equipe da UBS do dependente confere os documentos e aprova ou recusa aqui.
/// Aprovado, o responsável age pelo dependente no aplicativo; a equipe pode encerrar o vínculo. Os dois estados (pedidos pendentes e vínculos aprovados) são lidos juntos,
/// só nestas duas telas, ao entrar, a cada ciclo de atualização e logo depois de cada decisão. Os métodos supõem que a confirmação já foi dada na tela.
/// </summary>
public sealed partial class WorkspaceViewModel
{
    public const int FamilyReasonMax = 280;
    // Cada leitura tem o próprio indicador: a falha de uma não pode fazer a outra, lida agora, parecer da leitura anterior.
    private bool familyPendingLoaded, familyApprovedLoaded, familyPendingFailed, familyApprovedFailed, familyPendingTruncated, familyApprovedTruncated;
    private FamilyLink? selectedPendingFamily, selectedApprovedFamily;
    private string familyRejectReason = "";

    public ObservableCollection<FamilyLink> FamilyPending { get; } = [];
    public ObservableCollection<FamilyLink> FamilyApproved { get; } = [];
    public bool ShowFamily => IsAuthenticated && IsOperational && Section == "family";
    public FamilyLink? SelectedPendingFamily { get => selectedPendingFamily; set { selectedPendingFamily = value; Notify(); Notify(nameof(CanDecideFamily)); } }
    public FamilyLink? SelectedApprovedFamily { get => selectedApprovedFamily; set { selectedApprovedFamily = value; Notify(); Notify(nameof(CanEndFamily)); } }
    public bool CanDecideFamily => CanOperate && HasActiveObsUnit && SelectedPendingFamily is not null;
    public bool CanEndFamily => CanOperate && HasActiveObsUnit && SelectedApprovedFamily is not null;
    /// <summary>Motivo opcional da recusa (de 1 a 280 caracteres), digitado no pop-up "Recusar pedido"; o responsável é informado da recusa e do motivo.</summary>
    public string FamilyRejectReason { get => familyRejectReason; set { familyRejectReason = value ?? ""; Notify(); Notify(nameof(FamilyRejectReasonError)); Notify(nameof(FamilyRejectReasonCounter)); } }
    public string FamilyRejectReasonError => familyRejectReason.Trim().Length > FamilyReasonMax ? $"O motivo pode ter até {FamilyReasonMax} caracteres."
        : familyRejectReason.Any(char.IsControl) ? "O motivo não aceita quebra de linha nem caracteres de controle." : "";
    /// <summary>Contador do campo de motivo: "12/280".</summary>
    public string FamilyRejectReasonCounter => $"{familyRejectReason.Trim().Length}/{FamilyReasonMax}";
    // Sem UBS ativa quem avisa é o ObsUnitNoticeView ("Escolher UBS"); o estado das listas fica em branco para não repetir o aviso.
    public string FamilyPendingState => !HasActiveObsUnit ? ""
        : !familyPendingLoaded ? (familyPendingFailed ? "Não foi possível ler os pedidos agora. A tela tenta de novo sozinha." : "Carregando os pedidos…")
        : familyPendingFailed ? "A última leitura não concluiu; estes são os pedidos da leitura anterior."
        : familyPendingTruncated ? $"A lista passou do limite de leitura desta tela: mostrando os {FamilyPending.Count} mais antigos. Decida estes e os seguintes aparecem em seguida."
        : FamilyPending.Count == 0 ? "Nenhum pedido aguardando decisão nesta UBS." : "";
    public string FamilyApprovedState => !HasActiveObsUnit ? ""
        : !familyApprovedLoaded ? (familyApprovedFailed ? "Não foi possível ler os vínculos agora. A tela tenta de novo sozinha." : "Carregando os vínculos…")
        : familyApprovedFailed ? "A última leitura não concluiu; estes são os vínculos da leitura anterior."
        : familyApprovedTruncated ? $"A lista passou do limite de leitura desta tela: mostrando os {FamilyApproved.Count} mais antigos; os mais novos não aparecem."
        : FamilyApproved.Count == 0 ? "Nenhum vínculo aprovado nesta UBS." : "";
    /// <summary>A primeira leitura dos pedidos ainda não terminou (nem falhou): a tela mostra o indicador na cor da marca.</summary>
    public bool FamilyPendingLoading => HasActiveObsUnit && !familyPendingLoaded && !familyPendingFailed;
    public bool FamilyApprovedLoading => HasActiveObsUnit && !familyApprovedLoaded && !familyApprovedFailed;
    private static readonly string[] FamilyNames = [nameof(FamilyPendingState), nameof(FamilyApprovedState), nameof(FamilyPendingLoading), nameof(FamilyApprovedLoading), nameof(CanDecideFamily), nameof(CanEndFamily)];
    private void NotifyFamily() { foreach (var name in FamilyNames) Notify(name); }

    /// <summary>Aplica a leitura; mantém a seleção pelo id e não recria linhas que não mudaram.</summary>
    private void ApplyFamily(Tolerant<FamilyLinkPages>? pendingRead, Tolerant<FamilyLinkPages>? approvedRead)
    {
        if (pendingRead is { } pendingResult)
        {
            familyPendingFailed = pendingResult.Failed;
            if (pendingResult.Value is { } pending)
            {
                var selected = SelectedPendingFamily?.Id;
                Replace(FamilyPending, pending.Links.OrderBy(link => link.RequestedAt).ToArray());
                SelectedPendingFamily = selected is null ? null : FamilyPending.FirstOrDefault(link => link.Id == selected);
                familyPendingLoaded = true; familyPendingTruncated = pending.Truncated;
            }
        }
        if (approvedRead is { } approvedResult)
        {
            familyApprovedFailed = approvedResult.Failed;
            if (approvedResult.Value is { } approved)
            {
                var selected = SelectedApprovedFamily?.Id;
                Replace(FamilyApproved, approved.Links.OrderBy(link => link.GuardianName, StringComparer.CurrentCultureIgnoreCase).ThenBy(link => link.RequestedAt).ToArray());
                SelectedApprovedFamily = selected is null ? null : FamilyApproved.FirstOrDefault(link => link.Id == selected);
                familyApprovedLoaded = true; familyApprovedTruncated = approved.Truncated;
            }
        }
        NotifyFamily();
    }

    public Task ApproveFamilyAsync() => RunAsync(async () =>
    {
        var unit = ActiveObsUnit ?? throw new NoraApiException("Escolha uma UBS antes de aprovar o vínculo.");
        var link = SelectedPendingFamily ?? throw new NoraApiException("Selecione um pedido para aprovar.");
        await DecideFamilyAsync(unit, () => api.ApproveFamilyLinkAsync(unit.Id, link.Id));
        await ReloadAfterChangeAsync(unit, UnitScope.Family, "Pedido aprovado.");
    });

    /// <summary>
    /// Recusa o pedido selecionado com o motivo digitado no pop-up (opcional). Devolve se a API aceitou: o pop-up fecha só nesse caso e, se falhar, mostra a mensagem dentro dele.
    /// A faixa de retorno diz "Pedido recusado." ou "Pedido recusado e motivo enviado ao responsável.".
    /// </summary>
    public async Task<bool> RejectFamilyAsync()
    {
        var rejected = false;
        await RunAsync(async () =>
        {
            var unit = ActiveObsUnit ?? throw new NoraApiException("Escolha uma UBS antes de recusar o pedido.");
            var link = SelectedPendingFamily ?? throw new NoraApiException("Selecione um pedido para recusar.");
            if (FamilyRejectReasonError.Length > 0) throw new NoraApiException(FamilyRejectReasonError);
            var reason = string.IsNullOrWhiteSpace(familyRejectReason) ? null : familyRejectReason.Trim();
            await DecideFamilyAsync(unit, () => api.RejectFamilyLinkAsync(unit.Id, link.Id, reason));
            rejected = true;
            FamilyRejectReason = "";
            await ReloadAfterChangeAsync(unit, UnitScope.Family, reason is null ? "Pedido recusado." : "Pedido recusado e motivo enviado ao responsável.");
        });
        return rejected;
    }

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
        familyPendingLoaded = familyApprovedLoaded = familyPendingFailed = familyApprovedFailed = familyPendingTruncated = familyApprovedTruncated = false; FamilyPending.Clear(); FamilyApproved.Clear(); selectedPendingFamily = selectedApprovedFamily = null; familyRejectReason = "";
        Notify(nameof(SelectedPendingFamily)); Notify(nameof(SelectedApprovedFamily)); Notify(nameof(FamilyRejectReason)); Notify(nameof(FamilyRejectReasonError)); Notify(nameof(FamilyRejectReasonCounter)); NotifyFamily();
    }
}
