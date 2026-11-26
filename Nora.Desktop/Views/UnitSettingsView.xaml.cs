using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Nora.Desktop.Models;
using Nora.Desktop.Services;
using Nora.Desktop.ViewModels;

namespace Nora.Desktop.Views;

/// <summary>
/// Informações da UBS (OBS): atendimento ao público, profissionais, consultórios, grade semanal e bloqueios. As ações das listagens ficam no balão (editar e o que tira algo do ar,
/// sempre com confirmação e uma só para o lote); os cadastros abrem em pop-up e só fecham quando a API aceita.
/// </summary>
public partial class UnitSettingsView : UserControl
{
    public UnitSettingsView()
    {
        InitializeComponent();
        // A ficha em linhas (segundo clique e Enter) dos itens destas listagens; o balão só mostra Reativar ou Desativar quando vale para todos os selecionados.
        foreach (var list in new[] { ProfessionalsList, RoomsList, ScheduleList, BlocksList }) ListingBehavior.SetOpenHandler(list, OpenDetailsAsync);
        ListingBehavior.AddBalloonRefreshedHandler(ProfessionalsList, (_, _) => ShowActivationAction(ListingBehavior.GetSelectedItems<Professional>(ProfessionalsList).Select(item => item.IsActive), ProfessionalsBalloon, ProfessionalDeactivateButton, ProfessionalRestoreButton));
        ListingBehavior.AddBalloonRefreshedHandler(RoomsList, (_, _) => ShowActivationAction(ListingBehavior.GetSelectedItems<Room>(RoomsList).Select(item => item.IsActive), RoomsBalloon, RoomDeactivateButton, RoomRestoreButton));
    }

    private WorkspaceViewModel? Workspace => DataContext as WorkspaceViewModel;

    /// <summary>
    /// Desativar vale quando todos os selecionados estão ativos; Reativar, quando todos estão desativados. Com os dois tipos misturados nenhuma das duas aparece.
    /// O botão que passa a valer aparece antes de o outro sumir e o balão recalcula as ações no fim: com todos os botões escondidos a área de ações do balão fica oculta e um botão
    /// que volta a aparecer dentro dela não avisaria ninguém.
    /// </summary>
    private static void ShowActivationAction(IEnumerable<bool> activeStates, SelectionBalloon balloon, Button deactivate, Button restore)
    {
        var states = activeStates.ToArray();
        var canDeactivate = states.Length > 0 && states.All(active => active);
        var canRestore = states.Length > 0 && states.All(active => !active);
        if (canDeactivate) deactivate.Visibility = Visibility.Visible;
        if (canRestore) restore.Visibility = Visibility.Visible;
        if (!canDeactivate) deactivate.Visibility = Visibility.Collapsed;
        if (!canRestore) restore.Visibility = Visibility.Collapsed;
        balloon.RefreshActions();
    }

    private async void OnSaveProfile(object sender, RoutedEventArgs e) { if (Workspace is { CanSaveProfile: true } vm) await vm.SaveProfileAsync(); }

    // ---- Ficha em linhas ----

    /// <summary>Abre a ficha do item e só termina quando ela fecha (a fila de fichas espera cada uma). Ela fecha se a sessão termina ou a UBS ativa muda.</summary>
    private Task OpenDetailsAsync(object item)
    {
        if (Workspace is not { } vm || Window.GetWindow(this) is not { } owner || vm.BuildCatalogDetails(item) is not { } details) return Task.CompletedTask;
        var dialog = new ItemDetailsWindow(details) { Owner = owner, Title = details.Title, MaxHeight = Math.Max(320, Math.Min(SystemParameters.WorkArea.Height - 40, owner.ActualHeight - 40)) };
        var initialUnit = vm.ActiveObsUnit?.Id;
        PropertyChangedEventHandler closeOnChange = (_, changed) =>
        {
            if ((changed.PropertyName == nameof(WorkspaceViewModel.IsAuthenticated) && !vm.IsAuthenticated) ||
                (changed.PropertyName == nameof(WorkspaceViewModel.ActiveObsUnit) && vm.ActiveObsUnit?.Id != initialUnit)) dialog.Close();
        };
        vm.PropertyChanged += closeOnChange;
        dialog.Closed += (_, _) => vm.PropertyChanged -= closeOnChange;
        DialogService.ShowModal(dialog, owner);
        return Task.CompletedTask;
    }

    // ---- Pop-ups de cadastro ----

    /// <summary>Mensagem do pop-up quando o motivo da recusa já aparece escrito junto dos campos (o aviso do 409 ou o erro do campo): a barra do pop-up não repete o texto.</summary>
    private const string FieldsHighlighted = "Corrija o que está destacado e salve de novo.";

    /// <summary>
    /// Abre o pop-up de cadastro. Como a ficha, ele fecha sozinho se a sessão termina (o ciclo de 20 s ou o próprio salvar recebeu 401) ou se a UBS ativa muda: o rascunho era daquela
    /// sessão e daquela UBS. Durante um salvamento o pop-up não fecha; nesse caso o fechamento espera o salvamento terminar.
    /// </summary>
    private void ShowForm(WorkspaceViewModel vm, string title, string template, Func<Task<bool>> save, string primary, Func<string?> error)
    {
        var owner = Window.GetWindow(this);
        var form = new ContentControl { Content = vm, ContentTemplate = (DataTemplate)Resources[template], Focusable = false };
        var initialUnit = vm.ActiveObsUnit?.Id;
        bool Stale() => !vm.IsAuthenticated || vm.ActiveObsUnit?.Id != initialUnit;
        void CloseForms() { foreach (var dialog in Application.Current.Windows.OfType<FormDialog>().Where(item => ReferenceEquals(item.Request.Content, form)).ToArray()) dialog.Close(); }
        PropertyChangedEventHandler closeOnChange = (_, changed) =>
        {
            if (changed.PropertyName is nameof(WorkspaceViewModel.IsAuthenticated) or nameof(WorkspaceViewModel.ActiveObsUnit) && Stale()) CloseForms();
        };
        async Task<bool> SaveAndCheck()
        {
            var saved = await save();
            // Sessão recusada no meio do salvamento: o pop-up estava ocupado e não fechou; fecha logo depois que ele volta a ficar livre.
            if (!saved && Stale()) _ = Dispatcher.InvokeAsync(CloseForms, System.Windows.Threading.DispatcherPriority.Background);
            return saved;
        }
        vm.PropertyChanged += closeOnChange;
        try { DialogService.ShowForm(owner, title, form, SaveAndCheck, primary, error: error); }
        finally { vm.PropertyChanged -= closeOnChange; }
    }

    // ---- Profissionais ----

    private void OnNewProfessional(object sender, RoutedEventArgs e)
    {
        if (Workspace is not { CanEditCatalog: true } vm) return;
        vm.NewProfessionalDraft();
        ShowForm(vm, vm.ProfessionalDraftTitle, "ProfessionalFormTemplate", vm.SaveProfessionalFormAsync, "Salvar profissional", () => vm.ProfessionalNameError.Length + vm.ProfessionalSpecialtyError.Length + vm.CouncilError.Length > 0 ? FieldsHighlighted : vm.FormError);
    }

    private void OnEditProfessional(object sender, RoutedEventArgs e)
    {
        if (Workspace is not { CanEditCatalog: true } vm || ListingBehavior.GetSelectedItems<Professional>(ProfessionalsList) is not [var professional]) return;
        vm.SelectedProfessional = professional;
        vm.EditSelectedProfessional();
        ShowForm(vm, vm.ProfessionalDraftTitle, "ProfessionalFormTemplate", vm.SaveProfessionalFormAsync, "Salvar profissional", () => vm.ProfessionalNameError.Length + vm.ProfessionalSpecialtyError.Length + vm.CouncilError.Length > 0 ? FieldsHighlighted : vm.FormError);
    }

    private void OnSpecialtyChoice(object sender, RoutedEventArgs e) => Workspace?.SpecialtyChoiceChanged();

    private async void OnDeactivateProfessionals(object sender, RoutedEventArgs e)
    {
        if (Workspace is not { CanEditCatalog: true } vm) return;
        var selected = ListingBehavior.GetSelectedItems<Professional>(ProfessionalsList);
        if (selected.Count == 0) return;
        var asked = selected.Count == 1
            ? Confirmation.Ask(Window.GetWindow(this), "Desativar profissional", $"Desativar {selected[0].FullName}? Ele sai dos horários livres e do agendamento. As consultas já marcadas continuam como estavam.")
            : Confirmation.Ask(Window.GetWindow(this), "Desativar profissionais", $"Desativar {selected.Count} profissionais? Eles saem dos horários livres e do agendamento. As consultas já marcadas continuam como estavam.");
        if (asked) await vm.ChangeProfessionalsActiveAsync([.. selected.Select(item => item.Id)], false);
    }

    private async void OnReactivateProfessionals(object sender, RoutedEventArgs e)
    {
        if (Workspace is not { CanEditCatalog: true } vm) return;
        var selected = ListingBehavior.GetSelectedItems<Professional>(ProfessionalsList);
        if (selected.Count > 0) await vm.ChangeProfessionalsActiveAsync([.. selected.Select(item => item.Id)], true);
    }

    // ---- Consultórios ----

    private void OnNewRoom(object sender, RoutedEventArgs e)
    {
        if (Workspace is not { CanEditCatalog: true } vm) return;
        vm.NewRoomDraft();
        ShowForm(vm, vm.RoomDraftTitle, "RoomFormTemplate", vm.SaveRoomFormAsync, "Salvar consultório", () => vm.RoomNameError.Length > 0 ? FieldsHighlighted : vm.FormError);
    }

    private void OnEditRoom(object sender, RoutedEventArgs e)
    {
        if (Workspace is not { CanEditCatalog: true } vm || ListingBehavior.GetSelectedItems<Room>(RoomsList) is not [var room]) return;
        vm.SelectedRoom = room;
        vm.EditSelectedRoom();
        ShowForm(vm, vm.RoomDraftTitle, "RoomFormTemplate", vm.SaveRoomFormAsync, "Salvar consultório", () => vm.RoomNameError.Length > 0 ? FieldsHighlighted : vm.FormError);
    }

    private async void OnDeactivateRooms(object sender, RoutedEventArgs e)
    {
        if (Workspace is not { CanEditCatalog: true } vm) return;
        var selected = ListingBehavior.GetSelectedItems<Room>(RoomsList);
        if (selected.Count == 0) return;
        var asked = selected.Count == 1
            ? Confirmation.Ask(Window.GetWindow(this), "Desativar consultório", $"Desativar {selected[0].Name}? Os horários dele saem da disponibilidade; a grade que aponta para ele continua cadastrada.")
            : Confirmation.Ask(Window.GetWindow(this), "Desativar consultórios", $"Desativar {selected.Count} consultórios? Os horários deles saem da disponibilidade; a grade que aponta para eles continua cadastrada.");
        if (asked) await vm.ChangeRoomsActiveAsync([.. selected.Select(item => item.Id)], false);
    }

    private async void OnReactivateRooms(object sender, RoutedEventArgs e)
    {
        if (Workspace is not { CanEditCatalog: true } vm) return;
        var selected = ListingBehavior.GetSelectedItems<Room>(RoomsList);
        if (selected.Count > 0) await vm.ChangeRoomsActiveAsync([.. selected.Select(item => item.Id)], true);
    }

    // ---- Grade semanal ----

    private void OnNewSchedule(object sender, RoutedEventArgs e)
    {
        if (Workspace is not { CanEditCatalog: true } vm) return;
        vm.NewScheduleDraft();
        ShowForm(vm, vm.ScheduleDraftTitle, "ScheduleFormTemplate", vm.SaveScheduleFormAsync, "Salvar horário na grade", () => vm.ScheduleError.Length + vm.ScheduleOverlapError.Length > 0 ? FieldsHighlighted : vm.FormError);
    }

    private void OnEditSchedule(object sender, RoutedEventArgs e)
    {
        if (Workspace is not { CanEditCatalog: true } vm || ListingBehavior.GetSelectedItems<ScheduleTemplate>(ScheduleList) is not [var row]) return;
        vm.SelectedSchedule = row;
        vm.EditSelectedSchedule();
        ShowForm(vm, vm.ScheduleDraftTitle, "ScheduleFormTemplate", vm.SaveScheduleFormAsync, "Salvar horário na grade", () => vm.ScheduleError.Length + vm.ScheduleOverlapError.Length > 0 ? FieldsHighlighted : vm.FormError);
    }

    private async void OnDeleteSchedules(object sender, RoutedEventArgs e)
    {
        if (Workspace is not { CanEditCatalog: true } vm) return;
        var selected = ListingBehavior.GetSelectedItems<ScheduleTemplate>(ScheduleList);
        if (selected.Count == 0) return;
        var asked = selected.Count == 1
            ? Confirmation.Ask(Window.GetWindow(this), "Remover horário da grade", $"Remover {selected[0].WeekdayDisplay} {selected[0].RangeDisplay} de {selected[0].ProfessionalName}? Os horários livres dessa linha deixam de ser oferecidos; consultas já marcadas não são afetadas.")
            : Confirmation.Ask(Window.GetWindow(this), "Remover horários da grade", $"Remover {selected.Count} horários da grade? Os horários livres dessas linhas deixam de ser oferecidos; consultas já marcadas não são afetadas.");
        if (asked) await vm.DeleteSchedulesAsync([.. selected.Select(item => item.Id)]);
    }

    // ---- Bloqueios ----

    private void OnNewBlock(object sender, RoutedEventArgs e)
    {
        if (Workspace is not { CanEditCatalog: true } vm) return;
        vm.NewBlockDraft();
        ShowForm(vm, "Novo bloqueio", "BlockFormTemplate", vm.SaveBlockFormAsync, "Cadastrar bloqueio", () => vm.BlockError.Length > 0 ? FieldsHighlighted : vm.FormError);
    }

    private async void OnDeleteBlocks(object sender, RoutedEventArgs e)
    {
        if (Workspace is not { CanEditCatalog: true } vm) return;
        var selected = ListingBehavior.GetSelectedItems<ScheduleBlock>(BlocksList);
        if (selected.Count == 0) return;
        var asked = selected.Count == 1
            ? Confirmation.Ask(Window.GetWindow(this), "Remover bloqueio", $"Remover o bloqueio \"{selected[0].Reason}\" ({selected[0].PeriodDisplay})? Os horários que ele cobria voltam a ser oferecidos.")
            : Confirmation.Ask(Window.GetWindow(this), "Remover bloqueios", $"Remover {selected.Count} bloqueios? Os horários que eles cobriam voltam a ser oferecidos.");
        if (asked) await vm.DeleteBlocksAsync([.. selected.Select(item => item.Id)]);
    }
}
