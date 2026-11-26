using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Nora.Desktop.Models;
using Nora.Desktop.Services;
using Nora.Desktop.ViewModels;

namespace Nora.Desktop.Views;

/// <summary>Uma UBS da lista do pop-up "Trocar perfil": nome, região e a marca "Ativa" na que a conta está usando agora.</summary>
public sealed record ProfileOption(HealthUnit Unit, bool IsActive)
{
    public string Name => Unit.Name;
    public string Region => string.IsNullOrWhiteSpace(Unit.Region) ? "" : "Região: " + Unit.Region;
    public string Tag => IsActive ? "Ativa" : "";
    /// <summary>Nome da linha para o leitor de tela (nome, região e se é a ativa).</summary>
    public string AutomationLabel => Unit.SelectorLabel + (IsActive ? ", ativa" : "");
}

/// <summary>
/// Pop-up "Trocar perfil" (abre pelo menu do perfil no OBS, pelo botão "Escolher UBS" dos avisos das telas e sozinho depois do login quando a conta tem várias UBS e nenhuma ativa).
/// Lê a lista de UBS ao abrir (com indicador e, se falhar, a mensagem aqui dentro), marca e seleciona a ativa, e "Trocar" confirma pela lógica de sempre
/// (<see cref="WorkspaceViewModel.ConfirmObsUnitAsync"/>). X e Esc fecham sem trocar. "Sugerir pela localização" só seleciona a sugestão na lista.
/// </summary>
public partial class SwitchProfileDialog : Window
{
    private readonly WorkspaceViewModel workspace;
    private readonly ObservableCollection<ProfileOption> options = [];
    private bool busy, loading, syncing, closed;

    public SwitchProfileDialog(WorkspaceViewModel workspace)
    {
        this.workspace = workspace;
        InitializeComponent();
        UnitList.ItemsSource = options;
        AutomationProperties.SetName(this, "Trocar perfil");
        SourceInitialized += (_, _) => MaxHeight = Math.Max(320, Math.Min(SystemParameters.WorkArea.Height, Owner is { IsLoaded: true } owner ? owner.ActualHeight : double.PositiveInfinity) - 40);
        Loaded += OnLoaded;
        Closing += (_, args) => { if (busy) args.Cancel = true; };
        workspace.PropertyChanged += OnWorkspaceChanged;
        Closed += (_, _) => { closed = true; workspace.PropertyChanged -= OnWorkspaceChanged; };
    }

    /// <summary>As UBS mostradas, na ordem da lista.</summary>
    public IReadOnlyList<ProfileOption> Options => options;
    /// <summary>Os controles que as checagens acionam: a lista, o primário "Trocar", "Sugerir pela localização" e o X.</summary>
    public ListBox Units => UnitList;
    public Button Primary => SwitchButton;
    public Button Suggest => SuggestButton;
    public Button CloseX => CloseButton;
    /// <summary>A lista está sendo lida da API (indicador ligado).</summary>
    public bool IsLoading => loading;
    /// <summary>A troca está em andamento: os botões e o X ficam desabilitados e o pop-up não fecha.</summary>
    public bool IsBusy => busy;
    /// <summary>"Trocar" concluiu e o pop-up fechou por isso (X e Esc deixam falso).</summary>
    public bool Switched { get; private set; }
    /// <summary>A mensagem de erro mostrada dentro do pop-up (vazia quando não há).</summary>
    public string ErrorMessage => ErrorText.Text;
    /// <summary>A dica mostrada abaixo da lista, por exemplo a da sugestão pela localização (vazia quando não há).</summary>
    public string HintMessage => HintText.Text;

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Frame.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(140)));
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        Zoom.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.96, 1, TimeSpan.FromMilliseconds(180)) { EasingFunction = ease });
        Zoom.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.96, 1, TimeSpan.FromMilliseconds(180)) { EasingFunction = ease });
        UpdateButtons();
        await ReloadAsync();
    }

    /// <summary>Relê as UBS da conta e remonta a lista, com a ativa marcada e selecionada. Falha vira mensagem dentro do pop-up.</summary>
    public async Task ReloadAsync()
    {
        if (busy || loading) return;
        ShowError(null);
        SetState(loading: true, busy: false);
        var finished = false;
        try { finished = await workspace.LoadObsUnitsAsync(quiet: true); }
        catch (Exception failure) when (failure is not OutOfMemoryException) { System.Diagnostics.Trace.TraceError("NORA: a leitura das UBS falhou: {0}", failure); }
        if (closed) return;
        // A API recusou a sessão durante a leitura: a conta saiu (a faixa da janela principal explica) e não há mais o que escolher aqui.
        if (!workspace.IsAuthenticated) { Close(); return; }
        Rebuild();
        if (workspace.HasMessage && workspace.MessageKind == MessageKind.Error) ShowError(workspace.Message);
        else if (!finished) ShowError("Não foi possível ler a lista de UBS agora. Tente novamente em instantes.");
        SetState(loading: false, busy: false);
        FocusList();
    }

    private void Rebuild()
    {
        var activeId = workspace.ActiveObsUnit?.Id;
        syncing = true;
        try
        {
            options.Clear();
            foreach (var unit in workspace.ObsUnits) options.Add(new ProfileOption(unit, unit.Id == activeId));
        }
        finally { syncing = false; }
        // A UBS ativa abre marcada e já selecionada; sem ativa, vale a escolha que já existia (se ainda está na lista).
        if (options.FirstOrDefault(option => option.IsActive) is { } active) workspace.CandidateObsUnit = active.Unit;
        // Uma escolha que ficou de antes e saiu da lista (concessão revogada) não pode manter o Trocar habilitado sem nada selecionado.
        else if (workspace.CandidateObsUnit is { } stale && options.All(option => option.Unit.Id != stale.Id)) workspace.CandidateObsUnit = null;
        SyncSelection();
        var empty = options.Count == 0;
        UnitList.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
        EmptyText.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        EmptyText.Text = workspace.ObsUnitPrompt is { Length: > 0 } prompt ? prompt : "Nenhuma UBS autorizada para esta conta.";
    }

    /// <summary>"Trocar": confirma a UBS escolhida pela lógica existente. Deu certo, o pop-up fecha; falhou, fica aberto com a mensagem.</summary>
    public async Task SwitchAsync()
    {
        if (busy || loading || workspace.CandidateObsUnit is not { } unit || !workspace.CanConfirmObsUnit) return;
        ShowError(null); HideHint();
        SetState(loading: false, busy: true);
        try { await workspace.ConfirmObsUnitAsync(); }
        catch (Exception failure) when (failure is not OutOfMemoryException) { System.Diagnostics.Trace.TraceError("NORA: a troca de UBS falhou: {0}", failure); }
        if (closed) return;
        SetState(loading: false, busy: false);
        if (!workspace.IsAuthenticated) { Close(); return; }
        if (workspace.ActiveObsUnit?.Id == unit.Id) { Switched = true; Close(); return; }
        ShowError(workspace.HasMessage && workspace.MessageKind == MessageKind.Error ? workspace.Message : "Não foi possível trocar de UBS agora. Tente novamente em instantes.");
    }

    private void OnSuggest(object sender, RoutedEventArgs e)
    {
        if (busy || loading || !workspace.HasAvailableObsUnits) return;
        if (!Confirmation.Ask(this, "Acessar localização", "Permitir que o NORA acesse a localização deste computador para sugerir a UBS autorizada mais próxima?")) return;
        var location = new ObsLocationDialog { Owner = this };
        if (DialogService.ShowModal(location, this) != true) return;
        if (!workspace.SuggestClosestObsUnit(location.Latitude, location.Longitude) || workspace.CandidateObsUnit is not { } suggested) return;
        ShowHint(suggested.Id == workspace.ActiveObsUnit?.Id ? "A UBS mais próxima de você já é a que está ativa."
            : $"Sugestão pela localização: {suggested.SelectorLabel}. Confira e clique em Trocar.");
        FocusList();
    }

    private async void OnSwitch(object sender, RoutedEventArgs e) => await SwitchAsync();
    private void OnClose(object sender, RoutedEventArgs e) => Close();
    private void OnDrag(object sender, MouseButtonEventArgs e) { if (e.LeftButton == MouseButtonState.Pressed && e.OriginalSource is not Button) DragMove(); }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (syncing) return;
        HideHint();
        workspace.CandidateObsUnit = (UnitList.SelectedItem as ProfileOption)?.Unit;
        UpdateButtons();
    }

    private void OnWorkspaceChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (closed) return;
        // Sessão encerrada com o pop-up aberto (por exemplo recusada pela API no ciclo de atualização): fecha, a menos que a leitura ou a troca em andamento ainda precise terminar.
        if (args.PropertyName == nameof(WorkspaceViewModel.IsAuthenticated) && !workspace.IsAuthenticated && !busy && !loading) { Dispatcher.BeginInvoke(() => { if (!closed && !busy) Close(); }); return; }
        if (args.PropertyName == nameof(WorkspaceViewModel.CandidateObsUnit)) SyncSelection();
        if (args.PropertyName is nameof(WorkspaceViewModel.CandidateObsUnit) or nameof(WorkspaceViewModel.CanConfirmObsUnit) or nameof(WorkspaceViewModel.CanOperate) or nameof(WorkspaceViewModel.HasAvailableObsUnits)) UpdateButtons();
    }

    /// <summary>A seleção da lista acompanha a UBS escolhida no ViewModel (a sugestão pela localização muda a escolha por fora do clique).</summary>
    private void SyncSelection()
    {
        var id = workspace.CandidateObsUnit?.Id;
        var option = id is null ? null : options.FirstOrDefault(item => item.Unit.Id == id);
        if (ReferenceEquals(UnitList.SelectedItem, option)) return;
        syncing = true;
        try { UnitList.SelectedItem = option; }
        finally { syncing = false; }
    }

    private void SetState(bool loading, bool busy)
    {
        this.loading = loading; this.busy = busy;
        Progress.Visibility = loading || busy ? Visibility.Visible : Visibility.Collapsed;
        BusyText.Visibility = loading || busy ? Visibility.Visible : Visibility.Collapsed;
        BusyText.Text = busy ? "Trocando de UBS e carregando os dados dela…" : "Lendo as UBS que esta conta pode acessar…";
        UnitList.IsEnabled = !busy;
        UpdateButtons();
    }

    private void UpdateButtons()
    {
        SwitchButton.IsEnabled = !busy && !loading && workspace.CanConfirmObsUnit;
        SuggestButton.IsEnabled = !busy && !loading && workspace.HasAvailableObsUnits;
        CloseButton.IsEnabled = !busy;
    }

    /// <summary>O foco abre na UBS selecionada (setas e Enter funcionam de imediato); sem seleção, na lista.</summary>
    private void FocusList()
    {
        if (options.Count == 0) return;
        UnitList.UpdateLayout();
        if (UnitList.SelectedItem is { } selected && UnitList.ItemContainerGenerator.ContainerFromItem(selected) is ListBoxItem item) item.Focus();
        else UnitList.Focus();
    }

    private void ShowError(string? message)
    {
        ErrorText.Text = message ?? "";
        ErrorBox.Visibility = string.IsNullOrWhiteSpace(message) ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ShowHint(string text) { HintText.Text = text; HintText.Visibility = Visibility.Visible; }
    private void HideHint() { HintText.Text = ""; HintText.Visibility = Visibility.Collapsed; }
}
