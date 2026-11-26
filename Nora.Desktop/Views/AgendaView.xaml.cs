using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Nora.Desktop.Models;
using Nora.Desktop.Services;
using Nora.Desktop.ViewModels;

namespace Nora.Desktop.Views;

public partial class AgendaView : UserControl
{
    private WorkspaceViewModel? workspace;
    private ScrollViewer? weekScroll;

    public AgendaView()
    {
        InitializeComponent();
        // O balão mostra só as ações que valem para todas as consultas selecionadas (uma consulta cancelada não pode ser chamada, por exemplo).
        ListingBehavior.AddBalloonRefreshedHandler(DayList, OnBalloonRefreshed);
        ListingBehavior.AddBalloonRefreshedHandler(WeekList, OnBalloonRefreshed);
        DayList.SelectionChanged += OnSelectionChanged;
        WeekList.SelectionChanged += OnSelectionChanged;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        DataContextChanged += (_, _) => { Detach(); if (IsLoaded) Attach(); };
    }

    private ShellViewModel? Shell => (Window.GetWindow(this) as ShellWindow)?.ViewModel;

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Attach();
        WeekList.ApplyTemplate();
        if (WeekList.Template.FindName("PART_Scroll", WeekList) is ScrollViewer scroll)
        {
            weekScroll = scroll;
            scroll.ScrollChanged += OnWeekScrollChanged;
            scroll.SizeChanged += OnWeekScrollChanged;
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        Detach();
        if (weekScroll is { } scroll) { scroll.ScrollChanged -= OnWeekScrollChanged; scroll.SizeChanged -= OnWeekScrollChanged; weekScroll = null; }
    }

    /// <summary>Liga a tela ao ViewModel: a visão lembrada nas preferências volta, e a escolha da pessoa passa a ser lembrada; os textos de estado seguem o ViewModel e a pesquisa.</summary>
    private void Attach()
    {
        if (workspace is not null || DataContext is not WorkspaceViewModel vm) return;
        workspace = vm;
        Shell?.RestoreAgendaPeriodOnce();
        vm.PropertyChanged += OnWorkspaceChanged;
        ((INotifyCollectionChanged)DayList.Items).CollectionChanged += OnListItemsChanged;
        ((INotifyCollectionChanged)WeekList.Items).CollectionChanged += OnListItemsChanged;
        UpdateStates();
    }

    private void Detach()
    {
        if (workspace is null) return;
        workspace.PropertyChanged -= OnWorkspaceChanged;
        ((INotifyCollectionChanged)DayList.Items).CollectionChanged -= OnListItemsChanged;
        ((INotifyCollectionChanged)WeekList.Items).CollectionChanged -= OnListItemsChanged;
        workspace = null;
    }

    private void OnWorkspaceChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(WorkspaceViewModel.AgendaViewMode):
                if (workspace is { } vm) Shell?.SetAgendaPeriod(vm.AgendaViewMode);
                // A lista que sai de cena não guarda seleção: ela voltaria a mandar consultas antigas para o ViewModel.
                if (workspace?.IsAgendaWeek != true) ListingBehavior.ClearSelection(WeekList);
                if (workspace?.IsAgendaWeek == true || workspace?.IsAgendaYear == true) ListingBehavior.ClearSelection(DayList);
                UpdateStates();
                break;
            case nameof(WorkspaceViewModel.AgendaStateText):
            case nameof(WorkspaceViewModel.AgendaIsLoading):
            case nameof(WorkspaceViewModel.HasActiveObsUnit):
                UpdateStates();
                break;
        }
    }

    private void OnListItemsChanged(object? sender, NotifyCollectionChangedEventArgs e) => UpdateStates();

    /// <summary>
    /// Os avisos no lugar da lista: sem UBS, lendo a agenda, ou sem consulta. "Nenhuma consulta neste dia." (nada no período) é diferente de "Nenhuma consulta encontrada
    /// para esta busca." (a pesquisa escondeu tudo).
    /// </summary>
    private void UpdateStates()
    {
        if (workspace is not { } vm) return;
        var searched = DayList.Items.Count == 0 && vm.Appointments.Count > 0;
        var text = vm.AgendaStateText.Length > 0 ? vm.AgendaStateText : searched ? "Nenhuma consulta encontrada para esta busca." : "";
        ShowState(DayState, DayStateText, DayProgress, text, vm.AgendaIsLoading);
        ShowState(WeekState, WeekStateText, WeekProgress, text, vm.AgendaIsLoading);
        YearStateText.Text = text;
        YearState.Visibility = vm.IsAgendaYear && text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static void ShowState(FrameworkElement panel, TextBlock label, ProgressBar progress, string text, bool loading)
    {
        label.Text = text;
        progress.Visibility = loading ? Visibility.Visible : Visibility.Collapsed;
        panel.Visibility = text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>O cabeçalho dos dias reserva o espaço da barra de rolagem da grade, para as colunas dos dias ficarem alinhadas com as da grade.</summary>
    private void OnWeekScrollChanged(object sender, EventArgs e)
    {
        if (weekScroll is { } scroll) WeekHeaderSpacer.Width = new GridLength(Math.Max(0, scroll.ActualWidth - scroll.ViewportWidth));
    }

    /// <summary>Só a lista visível manda a seleção para o ViewModel (a que está escondida pode estar desfazendo a sua).</summary>
    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (workspace is { } vm && sender is ListBox { IsVisible: true } list) vm.SetAppointmentSelection(ListingBehavior.GetSelectedItems<AppointmentInfo>(list));
    }

    private void OnBalloonRefreshed(object sender, RoutedEventArgs e)
    {
        if (sender is not ListBox list || ListingBehavior.GetBalloon(list) is not { Content: Panel actions } balloon) return;
        var selected = ListingBehavior.GetSelectedItems<AppointmentInfo>(list);
        foreach (var button in actions.Children.OfType<Button>())
            button.Visibility = button.Tag is string status && WorkspaceViewModel.AllowsAppointmentStatus(status, selected) ? Visibility.Visible : Visibility.Collapsed;
        // Com a área de ações escondida os botões não avisam a mudança: o balão é avisado daqui (sem isso, depois de uma seleção sem ação ele nunca mais mostraria as ações).
        balloon.RefreshActions();
    }

    private void OnPrevious(object sender, RoutedEventArgs e) { if (DataContext is WorkspaceViewModel vm) vm.MoveAgenda(-1); }
    private void OnNext(object sender, RoutedEventArgs e) { if (DataContext is WorkspaceViewModel vm) vm.MoveAgenda(1); }
    private void OnToday(object sender, RoutedEventArgs e) { if (DataContext is WorkspaceViewModel vm) vm.GoToAgendaToday(); }
    private void OnOpenMonth(object sender, RoutedEventArgs e)
    {
        if (DataContext is WorkspaceViewModel vm && sender is FrameworkElement { DataContext: AgendaYearMonth month }) vm.OpenAgendaMonth(month.FirstDay);
    }

    /// <summary>Abre o agendamento num pop-up. O erro (horário ocupado, sobreposição, cadastro desatualizado…) aparece dentro dele, e o pop-up só fecha quando a consulta é marcada.</summary>
    private void OnNewAppointment(object sender, RoutedEventArgs e)
    {
        if (DataContext is not WorkspaceViewModel vm) return;
        var saved = DialogService.ShowForm(Window.GetWindow(this), "Agendar nova consulta", new AgendaBookingForm { DataContext = vm }, vm.CreateAppointmentAsync, "Agendar",
            error: () => vm.MessageKind == MessageKind.Error ? vm.Message : null);
        // Fechar sem agendar não deixa o erro do pop-up como aviso da tela.
        if (!saved) vm.DismissMessageCommand.Execute(null);
    }

    private async void OnStatus(object sender, RoutedEventArgs e)
    {
        if (DataContext is not WorkspaceViewModel vm || sender is not Button { Tag: string status } || vm.SelectedAppointmentCount == 0) return;
        if (status == "cancelled")
        {
            var many = vm.SelectedAppointmentCount > 1;
            if (!Confirmation.Ask(Window.GetWindow(this), many ? "Cancelar consultas" : "Cancelar consulta",
                    many ? $"As {vm.SelectedAppointmentCount} consultas serão canceladas e os horários podem ser oferecidos como vaga relâmpago." : "A consulta será cancelada e o horário pode ser oferecido como vaga relâmpago.",
                    many ? "Cancelar consultas" : "Cancelar consulta", "Voltar")) return;
        }
        await vm.ChangeAppointmentStatusAsync(status, status == "cancelled");
    }
}
