using System.Collections.ObjectModel;
using Nora.Desktop.Models;
using Nora.Desktop.Services;

namespace Nora.Desktop.ViewModels;

/// <summary>Especialidade do Chamar próximo com quantas pessoas aguardam nela; as que têm gente na fila vêm primeiro.</summary>
public sealed record SpecialtyQueueOption(Guid Id, string Name, int Waiting)
{
    public string Label => Waiting == 0 ? Name : $"{Name} · {Waiting} na fila";
}
public sealed record CallNextPeriod(int Days, string Label);

/// <summary>
/// "Chamar próximo" na Fila do OBS: a atendente escolhe um horário livre da grade e o botão oferece esse horário ao 1º da fila da especialidade, na hora (10 minutos para responder).
/// Se a pessoa recusar ou deixar vencer, o horário segue sozinho, pela Vaga Relâmpago, para os próximos da fila. A conferência de consulta em outra UBS acontece no aceite.
/// </summary>
public sealed partial class WorkspaceViewModel
{
    private bool callNextOpen, callNextSearched, callNextSlotsFailed, callNextPreviewFailed;
    private SpecialtyQueueOption? callNextSpecialty;
    private CallNextPeriod callNextPeriod = CallNextPeriods[0];
    private AvailableSlot? callNextSlot;
    private NextInQueue? callNextPreview;

    public static IReadOnlyList<CallNextPeriod> CallNextPeriods { get; } = [new(1, "Hoje"), new(7, "Próximos 7 dias")];
    public ObservableCollection<SpecialtyQueueOption> CallNextSpecialties { get; } = [];
    public ObservableCollection<AvailableSlot> CallNextSlots { get; } = [];
    public ObservableCollection<ProgressStep> CallNextSteps { get; } = [];

    public bool CallNextOpen { get => callNextOpen; private set { callNextOpen = value; Notify(); } }
    public bool CanOpenCallNext => CanOperate && HasActiveObsUnit;
    public SpecialtyQueueOption? CallNextSpecialty
    {
        get => callNextSpecialty;
        set
        {
            if (choiceSync > 0 && value is null) return;
            if (callNextSpecialty?.Id == value?.Id) { callNextSpecialty = value; return; }
            callNextSpecialty = value; Notify();
            ClearCallNextResults();
        }
    }
    public CallNextPeriod CallNextPeriodChoice
    {
        get => callNextPeriod;
        set { if (value is null || callNextPeriod == value) return; callNextPeriod = value; Notify(); ClearCallNextResults(); }
    }
    public AvailableSlot? CallNextSlot { get => callNextSlot; set { callNextSlot = value; Notify(); Notify(nameof(CanCallNextOffer)); Notify(nameof(CallNextConfirmationText)); } }
    public bool CanCallNextSearch => CanOperate && HasActiveObsUnit && CallNextSpecialty is not null;
    public bool CanCallNextOffer => CanOperate && HasActiveObsUnit && CallNextSlot is not null && callNextPreview is not null;
    /// <summary>A leitura da grade da UBS (profissionais e especialidades) falhou: sem ela a lista de especialidades pode estar vazia e o botão de buscar fica desligado, então o painel oferece reler só a grade.</summary>
    public bool CanRetryCallNextCatalog => CallNextOpen && CanOperate && HasActiveObsUnit && catalogFailed;

    /// <summary>A UBS já leu o catálogo e não tem profissional ativo: sem grade não há horário para chamar.</summary>
    public bool CallNextWithoutCatalog => HasActiveObsUnit && catalogLoaded && !catalogFailed && !UsesCatalogBooking;
    public string CallNextState => !HasActiveObsUnit ? ""
        : CallNextWithoutCatalog ? "Esta UBS ainda não tem grade; cadastre profissionais e grade para chamar pelo horário."
        : catalogFailed ? "Não foi possível ler a grade da UBS. Use Reler a grade para tentar de novo."
        : !catalogLoaded ? "Lendo a grade da UBS…"
        : CallNextSpecialty is null ? "Escolha a especialidade para buscar horários livres."
        : callNextSlotsFailed ? "Não foi possível buscar os horários livres. Use Buscar horários livres para tentar de novo."
        : !callNextSearched ? "Escolha o período e use Buscar horários livres."
        : CallNextSlots.Count == 0 ? "Nenhum horário livre nesse período."
        : "";
    public string CallNextPreviewText => !callNextSearched || callNextSlotsFailed ? ""
        : callNextPreviewFailed ? "Não foi possível conferir o 1º da fila agora. Use Buscar horários livres para tentar de novo."
        : callNextPreview is { } next ? $"1º da fila que pode receber: {next.Display}"
        : "Ninguém desta fila pode receber pelo aplicativo agora.";
    public string CallNextConfirmationText => callNextSlot is { } slot && callNextPreview is { } next
        ? $"Oferecer {slot.StartsAt.ToLocalTime():dd/MM HH:mm} com {slot.ProfessionalName} a {NextInQueue.ShortName(next.PatientName)}? Ela tem 10 minutos para responder."
        : "";

    private static readonly string[] CallNextNames =
    [
        nameof(CanOpenCallNext), nameof(CanCallNextSearch), nameof(CanCallNextOffer), nameof(CanRetryCallNextCatalog), nameof(CallNextWithoutCatalog), nameof(CallNextState), nameof(CallNextPreviewText), nameof(CallNextConfirmationText)
    ];
    private void NotifyCallNext() { foreach (var name in CallNextNames) Notify(name); }

    /// <summary>Abre ou fecha o painel. Abrir refaz as opções de especialidade com a fila atual e começa uma busca limpa.</summary>
    public void ToggleCallNext()
    {
        if (!CanOpenCallNext && !CallNextOpen) return;
        CallNextOpen = !CallNextOpen;
        if (CallNextOpen) { RebuildCallNextOptions(); ClearCallNextResults(); }
        NotifyCallNext();
    }

    /// <summary>Especialidades ativas da rede, com a contagem de quem aguarda; só as que têm gente na fila vêm primeiro.</summary>
    private void RebuildCallNextOptions()
    {
        var waiting = new Dictionary<string, int>(StringComparer.CurrentCultureIgnoreCase);
        foreach (var entry in UnitWaitingList.Where(item => item.Status == "waiting")) { var key = entry.Specialty.Trim(); waiting[key] = waiting.GetValueOrDefault(key) + 1; }
        var options = ObsSpecialties.Where(item => item.IsActive)
            .Select(item => new SpecialtyQueueOption(item.Id, item.Name, waiting.GetValueOrDefault(item.Name.Trim())))
            .OrderByDescending(item => item.Waiting > 0).ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
        var chosen = callNextSpecialty?.Id;
        choiceSync++;
        try { Replace(CallNextSpecialties, options); }
        finally { choiceSync--; }
        var kept = chosen is { } id ? CallNextSpecialties.FirstOrDefault(item => item.Id == id) : null;
        if (kept?.Id != callNextSpecialty?.Id) { callNextSpecialty = kept; Notify(nameof(CallNextSpecialty)); ClearCallNextResults(); }
        else if (kept is not null && !ReferenceEquals(kept, callNextSpecialty)) { callNextSpecialty = kept; Notify(nameof(CallNextSpecialty)); }
        NotifyCallNext();
    }

    /// <summary>Outra especialidade ou período invalida a lista e a prévia: elas voltam vazias até a próxima busca.</summary>
    private void ClearCallNextResults()
    {
        CallNextSlots.Clear(); CallNextSlot = null; callNextPreview = null;
        callNextSearched = callNextSlotsFailed = callNextPreviewFailed = false;
        CallNextSteps.Clear();
        NotifyCallNext();
    }

    /// <summary>Botão "Reler a grade": refaz só a leitura dos profissionais e das especialidades da UBS e remonta as opções do painel. Falha continua repetível com o mesmo botão.</summary>
    public Task RetryCallNextCatalogAsync() => RunAsync(async () =>
    {
        var unit = ActiveObsUnit ?? throw new NoraApiException("Escolha uma UBS no Painel da UBS antes de chamar o próximo da fila.");
        ClearCallNextResults();
        await WithStepAsync(CallNextSteps, "Lendo a grade da UBS…", async () =>
        {
            await LoadUnitScopeAsync(unit, UnitScope.Catalog);
            if (catalogFailed) throw new NoraApiException("Não foi possível ler a grade da UBS agora. Tente de novo.");
        }, clear: false);
        if (ActiveObsUnit?.Id == unit.Id) RebuildCallNextOptions();
    });

    /// <summary>Busca os horários livres da especialidade no período e confere quem é o 1º da fila. Cada passo concluído fica marcado; uma falha pode ser repetida com o mesmo botão.</summary>
    public Task SearchCallNextSlotsAsync() => RunAsync(async () =>
    {
        var unit = ActiveObsUnit ?? throw new NoraApiException("Escolha uma UBS no Painel da UBS antes de chamar o próximo da fila.");
        var specialty = CallNextSpecialty ?? throw new NoraApiException("Escolha a especialidade.");
        ClearCallNextResults();
        if (!catalogLoaded || catalogFailed)
        {
            await WithStepAsync(CallNextSteps, "Lendo a grade da UBS…", async () =>
            {
                await LoadUnitScopeAsync(unit, UnitScope.Catalog);
                if (catalogFailed) throw new NoraApiException("Não foi possível ler a grade da UBS agora. Tente de novo.");
            }, clear: false);
            RebuildCallNextOptions();
            if (CallNextSpecialty?.Id != specialty.Id) throw new NoraApiException("A especialidade escolhida deixou de estar disponível. Escolha de novo.");
        }
        if (!UsesCatalogBooking) { NotifyCallNext(); return; }
        var from = DateTimeOffset.Now;
        var to = callNextPeriod.Days == 1 ? new DateTimeOffset(DateTime.Today.AddDays(1)) : from.AddDays(callNextPeriod.Days);
        AvailableSlot[]? slots = null;
        try { await WithStepAsync(CallNextSteps, "Buscando horários livres…", async () => slots = await api.AvailabilityAsync(unit.Id, from, to, specialty.Id, null), clear: false); }
        catch (NoraApiException)
        {
            callNextSlotsFailed = true; callNextSearched = true; NotifyCallNext();
            throw;
        }
        if (disposed || ActiveObsUnit?.Id != unit.Id || CallNextSpecialty?.Id != specialty.Id) return;
        Replace(CallNextSlots, slots!.OrderBy(item => item.StartsAt).ThenBy(item => item.ProfessionalName, StringComparer.CurrentCultureIgnoreCase).ToArray());
        callNextSearched = true;
        await ReadCallNextPreviewAsync(unit, specialty);
        NotifyCallNext();
    });

    /// <summary>Confere o 1º da fila que pode receber. Uma falha dela não derruba a lista de horários: a prévia diz que não conseguiu e o botão de oferecer fica desabilitado.</summary>
    private async Task ReadCallNextPreviewAsync(HealthUnit unit, SpecialtyQueueOption specialty)
    {
        NextInQueue? next = null;
        callNextPreviewFailed = false;
        try { await WithStepAsync(CallNextSteps, "Conferindo o 1º da fila…", async () => next = await api.NextInQueueAsync(unit.Id, specialty.Id), clear: false); }
        catch (NoraApiException error) when (!error.Unauthorized && error.Code != "password-change-required")
        {
            callNextPreviewFailed = true;
            System.Diagnostics.Trace.TraceWarning("NORA: não foi possível conferir o 1º da fila: {0}", error.Message);
        }
        if (disposed || ActiveObsUnit?.Id != unit.Id || CallNextSpecialty?.Id != specialty.Id) return;
        callNextPreview = next;
        NotifyCallNext();
    }

    /// <summary>Oferece o horário escolhido ao 1º da fila. A tela pede a confirmação antes; a API também exige <c>confirmed</c>.</summary>
    public Task OfferCallNextAsync() => RunAsync(async () =>
    {
        var unit = ActiveObsUnit ?? throw new NoraApiException("Escolha uma UBS antes de oferecer o horário.");
        var specialty = CallNextSpecialty ?? throw new NoraApiException("Escolha a especialidade.");
        var slot = CallNextSlot ?? throw new NoraApiException("Escolha um horário livre da lista.");
        if (callNextPreview is null) throw new NoraApiException("Ninguém desta fila pode receber a oferta agora. Busque de novo para conferir a fila.");
        CallNextResult? result = null;
        try { await WithStepAsync(CallNextSteps, "Oferecendo o horário…", async () => result = await api.CallNextAsync(unit.Id, specialty.Id, slot.ProfessionalId, slot.StartsAt), clear: false); }
        catch (NoraApiException error) when (error.Code is "slot-unavailable" or "call-next-too-late")
        {
            // O horário não vale mais: sai da lista e nada mais é oferecido nele.
            CallNextSlots.Remove(slot); CallNextSlot = null; NotifyCallNext();
            throw;
        }
        catch (NoraApiException error) when (error.Code == "queue-empty")
        {
            callNextPreview = null; NotifyCallNext();
            throw;
        }
        CallNextSlots.Remove(slot); CallNextSlot = null;
        await ReadCallNextPreviewAsync(unit, specialty);
        var name = NextInQueue.ShortName(result!.Offer.PatientName);
        await ReloadAfterChangeAsync(unit, UnitScope.Queue | UnitScope.Slots,
            $"Horário oferecido a {name} até {result.Offer.ExpiresAt.ToLocalTime():HH:mm}. Se ela recusar ou não responder, o horário segue sozinho para os próximos da fila.");
        RebuildCallNextOptions();
    });

    private void ClearCallNext()
    {
        CallNextOpen = false; callNextSpecialty = null; Notify(nameof(CallNextSpecialty));
        Replace(CallNextSpecialties, Array.Empty<SpecialtyQueueOption>());
        ClearCallNextResults();
    }
}
