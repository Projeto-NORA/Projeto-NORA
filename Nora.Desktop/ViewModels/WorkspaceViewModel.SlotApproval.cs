using System.Collections.ObjectModel;
using Nora.Desktop.Models;
using Nora.Desktop.Services;

namespace Nora.Desktop.ViewModels;

/// <summary>
/// Aprovação clínica opcional das vagas reaproveitadas (OBS). O administrador da UBS liga ou desliga. Ligada, cada cancelamento gera uma vaga que fica aguardando a equipe:
/// aprovada, ela é oferecida ao 1º da fila; descartada ou sem aprovação até faltar 1 h para a consulta, fecha. A vaga mostra de qual consulta cancelada veio.
/// </summary>
public sealed partial class WorkspaceViewModel
{
    private UnitLightningSlot? selectedAwaitingSlot;

    /// <summary>Vagas aguardando aprovação, da que vence primeiro para a última.</summary>
    public ObservableCollection<UnitLightningSlot> AwaitingSlots { get; } = [];
    public ObservableCollection<ProgressStep> SlotSteps { get; } = [];
    /// <summary>Passos do salvamento da chave de aprovação, mostrados no cartão dela (os de <see cref="ProfileSteps"/> são do cartão do atendimento ao público).</summary>
    public ObservableCollection<ProgressStep> SlotApprovalSteps { get; } = [];
    public UnitLightningSlot? SelectedAwaitingSlot
    {
        get => selectedAwaitingSlot;
        set { selectedAwaitingSlot = value; Notify(); Notify(nameof(CanDecideSlotApproval)); Notify(nameof(ApproveSlotConfirmationText)); Notify(nameof(DiscardSlotConfirmationText)); }
    }

    public bool SlotApprovalEnabled => unitProfile?.SlotApprovalRequired ?? false;
    public string SlotApprovalState => !HasActiveObsUnit ? "Escolha uma UBS no Painel da UBS para ver a aprovação das vagas."
        : profileFailed && !profileLoaded ? "Não foi possível ler a aprovação das vagas agora. Use Atualizar configurações para tentar de novo."
        : !profileLoaded ? "Lendo a aprovação das vagas…"
        : SlotApprovalEnabled
            ? $"Ligada{(unitProfile?.SlotApprovalSince is { } since ? $" desde {since.ToLocalTime():dd/MM/yyyy HH:mm}" : "")}."
            : "Desligada. A vaga só nasce quando a equipe a gera pela Agenda e é oferecida na hora.";
    public string SlotApprovalToggleLabel => SlotApprovalEnabled ? "Desligar aprovação das vagas" : "Ligar aprovação das vagas";
    /// <summary>A situação (Ligada/Desligada) é conhecida assim que o perfil da UBS foi lido, para qualquer papel e mesmo durante outra operação.</summary>
    public bool SlotApprovalKnown => HasActiveObsUnit && profileLoaded && !profileFailed;
    public bool CanToggleSlotApproval => CanEditProfile && profileLoaded && !profileFailed;
    public string SlotApprovalConfirmationText => SlotApprovalEnabled
        ? "Desligar a aprovação das vagas? As vagas que já aguardam continuam aprováveis até o prazo; não nascem vagas novas aguardando."
        : "Ligar a aprovação? Cada vaga só será oferecida depois que a equipe aprovar; sem aprovação até 1 h antes da consulta, ela fecha.";

    public bool HasAwaitingSlots => AwaitingSlots.Count > 0;
    /// <summary>A seção aparece com a chave ligada ou quando ainda há vagas aguardando (a chave pode ter sido desligada depois).</summary>
    public bool ShowAwaitingSlots => SlotApprovalEnabled || AwaitingSlots.Count > 0;
    public string AwaitingSlotsTitle => $"Aguardando aprovação ({AwaitingSlots.Count})";
    public string AwaitingSlotsState => !HasActiveObsUnit ? "" : !unitSlotsLoaded ? "Lendo as vagas…" : AwaitingSlots.Count == 0 ? "Nenhuma vaga aguardando aprovação." : "";
    public string PanelAwaitingText => AwaitingSlots.Count == 1 ? "1 vaga aguardando aprovação da equipe" : $"{AwaitingSlots.Count} vagas aguardando aprovação da equipe";
    public bool CanDecideSlotApproval => CanOperate && HasActiveObsUnit && SelectedAwaitingSlot is not null;
    public string ApproveSlotConfirmationText => SelectedAwaitingSlot is not { } slot ? "" :
        $"Aprovar e ofertar a vaga de {slot.SummaryDisplay}? Ela passa a ser oferecida ao 1º da fila dessa especialidade.\n\n{slot.SourceDisplay}.";
    public string DiscardSlotConfirmationText => SelectedAwaitingSlot is not { } slot ? "" :
        $"Descartar a vaga de {slot.SummaryDisplay}? Ela fecha sem ser oferecida e não pode ser reaberta.";

    /// <summary>Refaz a lista de vagas aguardando (prazo de aprovação mais curto primeiro) depois de uma leitura das vagas, mantendo a seleção quando ela continua aguardando.</summary>
    private void RefreshAwaitingSlots()
    {
        var selectedId = SelectedAwaitingSlot?.Id;
        Replace(AwaitingSlots, UnitLightningSlots.Where(slot => slot.IsAwaitingApproval).OrderBy(slot => slot.ApprovalDeadline ?? DateTimeOffset.MaxValue).ThenBy(slot => slot.StartsAt).ThenBy(slot => slot.Id).ToArray());
        SelectedAwaitingSlot = selectedId is { } id ? AwaitingSlots.FirstOrDefault(slot => slot.Id == id) : null;
        NotifyProfile();
    }

    /// <summary>Liga ou desliga a aprovação (só o administrador da UBS). A confirmação é pedida pela tela; a API também exige <c>confirmed</c>.</summary>
    public Task ToggleSlotApprovalAsync() => RunAsync(async () =>
    {
        var unit = ActiveObsUnit ?? throw new NoraApiException("Escolha uma UBS no Painel da UBS antes de mudar a aprovação das vagas.");
        if (!IsAdministrator) throw new NoraApiException("Somente o administrador da UBS liga ou desliga a aprovação das vagas.");
        if (!profileLoaded || profileFailed) throw new NoraApiException("Leia as configurações da UBS (Atualizar configurações) antes de mudar a aprovação das vagas.");
        var enable = !SlotApprovalEnabled;
        SlotApprovalState? result = null;
        await WithStepAsync(SlotApprovalSteps, "Salvando a aprovação das vagas…", async () => result = await api.SetSlotApprovalAsync(unit.Id, enable));
        if (disposed || ActiveObsUnit?.Id != unit.Id || unitProfile is null) return;
        unitProfile = unitProfile with { SlotApprovalRequired = result!.Enabled, SlotApprovalSince = result.Since };
        NotifyProfile();
        Say(MessageKind.Success, result.Enabled
            ? "Aprovação das vagas ligada."
            : "Aprovação das vagas desligada. As vagas que já aguardam continuam aprováveis até o prazo.");
    });

    /// <summary>Aprova a vaga selecionada: ela passa a aberta e é oferecida ao 1º da fila. Se o prazo acabou ou o horário foi ocupado, a API a fecha e a lista é relida.</summary>
    public Task ApproveSelectedSlotAsync() => RunAsync(async () =>
    {
        var (unit, slot) = RequireAwaitingSlot();
        try
        {
            await WithStepAsync(SlotSteps, "Aprovando a vaga…", () => api.ApproveLightningSlotAsync(unit.Id, slot.Id));
        }
        catch (NoraApiException error) when (error.Code is "approval-expired" or "slot-unavailable" or "conflict" or "not-found")
        {
            await RefreshSlotsAfterRefusalAsync(unit);
            throw;
        }
        await ReloadAfterChangeAsync(unit, UnitScope.Slots | UnitScope.Queue, "Vaga aprovada. Ela já está sendo oferecida ao 1º da fila.");
    });

    public Task DiscardSelectedSlotAsync() => RunAsync(async () =>
    {
        var (unit, slot) = RequireAwaitingSlot();
        try
        {
            await WithStepAsync(SlotSteps, "Descartando…", () => api.DiscardLightningSlotAsync(unit.Id, slot.Id));
        }
        catch (NoraApiException error) when (error.Code is "conflict" or "not-found")
        {
            await RefreshSlotsAfterRefusalAsync(unit);
            throw;
        }
        await ReloadAfterChangeAsync(unit, UnitScope.Slots, "Vaga descartada.");
    });

    private (HealthUnit Unit, UnitLightningSlot Slot) RequireAwaitingSlot()
    {
        var unit = ActiveObsUnit ?? throw new NoraApiException("Escolha uma UBS antes de decidir sobre a vaga.");
        var slot = SelectedAwaitingSlot ?? throw new NoraApiException("Selecione uma vaga aguardando aprovação.");
        if (!slot.IsAwaitingApproval) throw new NoraApiException("Esta vaga não está mais aguardando aprovação. Atualize as vagas.");
        return (unit, slot);
    }

    /// <summary>A API já decidiu a vaga por outro caminho (fechou, foi aprovada por outra pessoa): a lista é relida para refletir o que ficou gravado.</summary>
    private async Task RefreshSlotsAfterRefusalAsync(HealthUnit unit)
    {
        try { await LoadUnitScopeAsync(unit, UnitScope.Slots | UnitScope.Queue); }
        catch (NoraApiException error) when (!error.Unauthorized)
        {
            MarkStale();
            System.Diagnostics.Trace.TraceWarning("NORA: não foi possível reler as vagas depois da recusa: {0}", error.Message);
        }
    }
}
