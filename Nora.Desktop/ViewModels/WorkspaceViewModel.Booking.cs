using System.Collections.ObjectModel;
using Nora.Desktop.Models;
using Nora.Desktop.Services;

namespace Nora.Desktop.ViewModels;

/// <summary>
/// Agendamento pelo catálogo (OBS): quando a UBS tem ao menos um profissional ativo, "Agendar consulta" escolhe especialidade, profissional e um horário livre
/// da grade (GET .../availability, uma requisição por mudança de escolha ou de dia; a anterior é cancelada e resposta atrasada é descartada).
/// Sem profissional ativo a agenda continua aceitando texto livre, como antes.
/// </summary>
public sealed partial class WorkspaceViewModel
{
    private const int BookingLookAheadDays = 14;
    private Specialty? bookingSpecialty;
    private Professional? bookingProfessional;
    private AvailableSlot? bookingSlot;
    private bool bookingSlotsLoaded, bookingSlotsFailed;
    private int bookingLoading, bookingVersion;
    private CancellationTokenSource? bookingCts;
    /// <summary>Especialidades ativas que ao menos um profissional ativo atende.</summary>
    public ObservableCollection<Specialty> BookingSpecialties { get; } = [];
    /// <summary>Profissionais ativos da especialidade escolhida.</summary>
    public ObservableCollection<Professional> BookingProfessionals { get; } = [];
    public ObservableCollection<AvailableSlot> BookingSlots { get; } = [];

    /// <summary>A UBS marca consultas pelo catálogo (há profissional ativo). Com a leitura do catálogo falha nada é afirmado: o agendamento fica indisponível.</summary>
    public bool UsesCatalogBooking => HasActiveObsUnit && catalogLoaded && !catalogFailed && Professionals.Any(item => item.IsActive);
    public bool UsesFreeTextBooking => HasActiveObsUnit && catalogLoaded && !catalogFailed && !UsesCatalogBooking;
    public bool BookingCatalogUnavailable => HasActiveObsUnit && catalogFailed;
    public bool CanBook => CanOperate && HasActiveObsUnit && (UsesCatalogBooking || UsesFreeTextBooking);
    public bool BookingSlotsBusy => bookingLoading > 0;
    public string BookingIntro => UsesCatalogBooking
        ? "Esta UBS marca pelo catálogo: escolha a especialidade, o profissional e um horário livre da grade. Os horários já ocupados, bloqueados ou fora da grade não aparecem."
        : UsesFreeTextBooking ? "Esta UBS ainda não tem profissional ativo cadastrado: informe a especialidade e o profissional em texto livre. Depois que o administrador cadastrar profissionais, a marcação passa a usar só o catálogo."
        : BookingCatalogUnavailable ? "Não foi possível ler o cadastro de profissionais agora, por isso o agendamento fica indisponível. Atualize a agenda para tentar de novo." : "";
    public Specialty? BookingSpecialty
    {
        get => bookingSpecialty;
        set
        {
            if (choiceSync > 0 && value is null) return;
            if (Equals(bookingSpecialty, value)) return;
            bookingSpecialty = value; Notify();
            bookingProfessional = null; Notify(nameof(BookingProfessional));
            RebuildBookingProfessionals();
            ReloadBookingSlots();
        }
    }
    public Professional? BookingProfessional
    {
        get => bookingProfessional;
        set { if (choiceSync > 0 && value is null) return; if (bookingProfessional?.Id == value?.Id) return; bookingProfessional = value; Notify(); ReloadBookingSlots(); }
    }
    public AvailableSlot? BookingSlot { get => bookingSlot; set { bookingSlot = value; Notify(); Notify(nameof(BookingSelectionLabel)); } }
    public string BookingSelectionLabel => bookingSlot is { } slot ? $"Horário escolhido: {slot.Display}" : "Nenhum horário escolhido.";
    public string BookingWindowLabel => $"Horários livres a partir de {DisplayFormat.Date(DateOnly.FromDateTime(BookingWindowStart().LocalDateTime))}, nos próximos {BookingLookAheadDays} dias.";
    public string BookingState => !UsesCatalogBooking ? ""
        : bookingSpecialty is null ? "Escolha a especialidade para ver os profissionais."
        : bookingProfessional is null ? (BookingProfessionals.Count == 0 ? "Nenhum profissional ativo atende esta especialidade." : "Escolha o profissional para ver os horários livres.")
        : bookingLoading > 0 ? "Buscando horários livres…"
        : bookingSlotsFailed ? "Não foi possível buscar os horários livres. Escolha o profissional de novo ou use Atualizar agenda para tentar outra vez."
        : bookingSlotsLoaded && BookingSlots.Count == 0 ? $"Nenhum horário livre nos próximos {BookingLookAheadDays} dias para este profissional nesta especialidade. Escolha outro dia no calendário, outro profissional ou peça ao administrador para ajustar a grade."
        : "";

    private static readonly string[] BookingNames =
    [
        nameof(UsesCatalogBooking), nameof(UsesFreeTextBooking), nameof(BookingCatalogUnavailable), nameof(CanBook), nameof(BookingSlotsBusy), nameof(BookingIntro), nameof(BookingState), nameof(BookingWindowLabel), nameof(BookingSelectionLabel)
    ];
    private void NotifyBooking() { foreach (var name in BookingNames) Notify(name); }

    /// <summary>A janela começa no dia selecionado no calendário (ou agora, se esse dia já passou); a API só devolve atendimentos futuros para a equipe.</summary>
    private DateTimeOffset BookingWindowStart() { var day = new DateTimeOffset(selectedAgendaDate.Date); var now = DateTimeOffset.Now; return day > now ? day : now; }

    /// <summary>Recalcula as listas do agendamento depois de uma leitura do catálogo, mantendo as escolhas que continuam válidas.</summary>
    private void RebuildBookingOptions()
    {
        var chosen = bookingSpecialty;
        // O ComboBox grava null na escolha quando a lista é trocada por baixo dele: esse null é ignorado e a escolha volta pelo id (profissional e horário não se perdem numa releitura).
        choiceSync++;
        try { Replace(BookingSpecialties, ObsSpecialties.Where(item => item.IsActive && Professionals.Any(professional => professional.IsActive && professional.SpecialtyIds.Contains(item.Id))).ToArray()); }
        finally { choiceSync--; }
        var kept = chosen is null ? null : BookingSpecialties.FirstOrDefault(item => item.Id == chosen.Id);
        if (kept?.Id != bookingSpecialty?.Id) { bookingSpecialty = kept; Notify(nameof(BookingSpecialty)); bookingProfessional = null; Notify(nameof(BookingProfessional)); ClearBookingSlots(); }
        else if (kept is not null && !ReferenceEquals(kept, bookingSpecialty)) { bookingSpecialty = kept; Notify(nameof(BookingSpecialty)); }
        if (RebuildBookingProfessionals()) { if (bookingProfessional is null) ClearBookingSlots(); else ReloadBookingSlots(); }
        NotifyBooking();
    }

    /// <summary>Profissionais da especialidade escolhida; se só houver um, ele já vem escolhido (a leitura dos horários sai da mudança de especialidade).</summary>
    private bool RebuildBookingProfessionals()
    {
        choiceSync++;
        try { Replace(BookingProfessionals, bookingSpecialty is { } specialty ? Professionals.Where(item => item.IsActive && item.SpecialtyIds.Contains(specialty.Id)).ToArray() : []); }
        finally { choiceSync--; }
        var kept = bookingProfessional is { } current ? BookingProfessionals.FirstOrDefault(item => item.Id == current.Id) : null;
        if (kept is null && BookingProfessionals.Count == 1) kept = BookingProfessionals[0];
        if (kept?.Id == bookingProfessional?.Id)
        {
            if (kept is not null && !ReferenceEquals(kept, bookingProfessional)) { bookingProfessional = kept; Notify(nameof(BookingProfessional)); }
            return false;
        }
        bookingProfessional = kept; Notify(nameof(BookingProfessional));
        return true;
    }

    private void ClearBookingSlots()
    {
        Interlocked.Exchange(ref bookingCts, null)?.Cancel();
        bookingVersion++;
        BookingSlots.Clear(); BookingSlot = null; bookingSlotsLoaded = bookingSlotsFailed = false;
        NotifyBooking();
    }

    /// <summary>Uma requisição por mudança de especialidade, profissional ou dia: a anterior é cancelada e só a mais nova altera a tela.</summary>
    private void ReloadBookingSlots()
    {
        if (disposed || !IsAuthenticated || mustChangePassword || ActiveObsUnit is not { } unit || !UsesCatalogBooking || bookingSpecialty is not { } specialty || bookingProfessional is not { } professional)
        { ClearBookingSlots(); return; }
        var from = BookingWindowStart();
        var cancellation = new CancellationTokenSource();
        Interlocked.Exchange(ref bookingCts, cancellation)?.Cancel();
        var version = ++bookingVersion;
        bookingSlotsLoaded = bookingSlotsFailed = false;
        _ = RunBookingSlotsAsync(unit, specialty, professional, from, from.AddDays(BookingLookAheadDays), version, cancellation.Token);
    }

    private async Task RunBookingSlotsAsync(HealthUnit unit, Specialty specialty, Professional professional, DateTimeOffset from, DateTimeOffset to, int version, CancellationToken cancellation)
    {
        backgroundOperations++; bookingLoading++; NotifyBookingLoading();
        try
        {
            await gate.WaitAsync(cancellation);
            try
            {
                if (cancellation.IsCancellationRequested || disposed || !IsAuthenticated) return;
                var slots = await api.AvailabilityAsync(unit.Id, from, to, specialty.Id, professional.Id, cancellation);
                if (disposed || !IsAuthenticated || version != bookingVersion || ActiveObsUnit?.Id != unit.Id || cancellation.IsCancellationRequested) return;
                var chosenStart = BookingSlot?.StartsAt;
                Replace(BookingSlots, slots.OrderBy(item => item.StartsAt).ToArray());
                bookingSlotsLoaded = true; bookingSlotsFailed = false;
                BookingSlot = chosenStart is { } start ? BookingSlots.FirstOrDefault(item => item.StartsAt == start) : null;
                RecoverFromRefreshFailure(); MarkFresh();
            }
            catch (NoraApiException error) { if (version == bookingVersion) ReportFilterFailure(error, () => bookingSlotsFailed = true); }
            finally { gate.Release(); }
        }
        catch (OperationCanceledException) { }
        finally
        {
            backgroundOperations--; bookingLoading--;
            if (!disposed) { NotifyBookingLoading(); NotifyBooking(); }
        }
    }

    private void NotifyBookingLoading() { Notify(nameof(IsBusy)); Notify(nameof(ShowProgress)); Notify(nameof(BookingSlotsBusy)); Notify(nameof(BookingState)); }

    /// <summary>Falha de uma consulta pedida por filtro: encerra a sessão, pede a troca de senha ou aparece na faixa comum e marca os dados como desatualizados.</summary>
    private void ReportFilterFailure(NoraApiException error, Action markFailed)
    {
        if (error.Unauthorized) { SignOut(); Say(MessageKind.Error, error.Message); return; }
        if (error.Code == "password-change-required") { EnterPasswordChange(); return; }
        markFailed();
        refreshFailed = true; refreshErrorText = error.Message;
        MarkStale(); Say(MessageKind.Error, error.Message);
    }

    private async Task RecoverBookingAsync(HealthUnit unit, string? code)
    {
        // Horário ocupado no meio do caminho: a lista de horários é lida de novo (depois desta operação). Catálogo desatualizado: o catálogo é lido de novo.
        if (code == "slot-unavailable") ReloadBookingSlots();
        else if (code == "catalog-required") { try { await LoadUnitScopeAsync(unit, UnitScope.Catalog); } catch (NoraApiException) { } }
    }

    private void ClearBooking()
    {
        bookingSpecialty = null; bookingProfessional = null; bookingSlot = null;
        Replace(BookingSpecialties, Array.Empty<Specialty>()); Replace(BookingProfessionals, Array.Empty<Professional>());
        ClearBookingSlots();
        Notify(nameof(BookingSpecialty)); Notify(nameof(BookingProfessional));
    }
}
