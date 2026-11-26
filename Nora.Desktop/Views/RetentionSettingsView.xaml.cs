using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Nora.Desktop.Models;
using Nora.Desktop.Services;
using Nora.Desktop.ViewModels;

namespace Nora.Desktop.Views;

/// <summary>
/// Exclusão programada (dentro de Configurações): a política geral (um prazo automático ou "nunca excluir sozinho") e a lista aberta dos cadastros desativados, com o
/// balão de ações (agendar e cancelar a exclusão, em vermelho) e a ficha em linhas ao duplo clique. Lê ao entrar na tela, a cada 20 s (sem piscar a lista nem derrubar a seleção)
/// e logo depois de cada ação; não há botão Atualizar.
/// </summary>
public partial class RetentionSettingsView : UserControl
{
    private const string Title = "Exclusão programada";
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(20);
    private readonly DispatcherTimer refresh;
    private WorkspaceViewModel? workspace;
    private CancellationTokenSource? reading;
    private (Guid Account, Guid? Unit)? displayed;
    private int? savedPolicyDays;
    private bool busy;

    public RetentionSettingsView()
    {
        InitializeComponent();
        refresh = new DispatcherTimer(DispatcherPriority.Background) { Interval = RefreshInterval };
        refresh.Tick += async (_, _) => await RefreshAutomaticallyAsync();
        ListingBehavior.AddBalloonRefreshedHandler(Items, OnBalloonRefreshed);
        ListingBehavior.SetOpenHandler(Items, OpenFile);
    }

    /// <summary>A releitura de 20 s: só com a seção à vista e livre (sem leitura nem gravação em andamento).</summary>
    internal Task RefreshAutomaticallyAsync() => IsVisible && displayed is not null && Controls.IsEnabled ? LoadAsync(quiet: true) : Task.CompletedTask;

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        workspace = DataContext as WorkspaceViewModel;
        if (workspace is not null) workspace.PropertyChanged += OnWorkspaceChanged;
        refresh.Start();
        RefreshScope();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        refresh.Stop();
        if (workspace is not null) workspace.PropertyChanged -= OnWorkspaceChanged;
        workspace = null;
        reading?.Cancel();
        displayed = null;
    }

    private void OnVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e) => RefreshScope();

    private void OnWorkspaceChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(WorkspaceViewModel.IsAuthenticated) or nameof(WorkspaceViewModel.ActiveObsUnit)
            or nameof(WorkspaceViewModel.MustChangePassword))
            RefreshScope();
    }

    private async void RefreshScope()
    {
        if (!IsLoaded) return;
        var scope = workspace?.RetentionScope;
        Visibility = scope is null ? Visibility.Collapsed : Visibility.Visible;
        if (!IsVisible || scope is null)
        {
            reading?.Cancel();
            displayed = null;
            Items.ItemsSource = null;
            ItemsEmpty.Visibility = Visibility.Collapsed;
            return;
        }
        if (displayed == scope || busy) return;
        await LoadAsync();
    }

    /// <summary>Lê a política e os cadastros desativados. <paramref name="quiet"/> é a releitura de 20 s: não desliga os controles, não mexe no prazo que a pessoa está digitando e falha em silêncio (a lista na tela continua valendo).</summary>
    private async Task LoadAsync(bool quiet = false)
    {
        if (workspace is not { } vm || vm.RetentionScope is not { } scope) return;
        if (quiet && busy) return;
        displayed = scope;
        Explanation.Text = scope.Unit is null
            ? "Contas OBS desativadas ficam elegíveis quando não têm vínculos nem histórico de uso."
            : "Consultórios desativados ficam elegíveis quando não têm vínculos com a agenda ou o atendimento.";
        using var cancellation = new CancellationTokenSource();
        reading = cancellation;
        try
        {
            if (quiet)
            {
                busy = true;
                try
                {
                    var data = await vm.ReadRetentionAsync(scope.Unit, cancellation.Token);
                    if (!cancellation.IsCancellationRequested && vm.RetentionScope == scope) ShowItems(data.Items);
                }
                catch (Exception error) when (error is NoraApiException or OperationCanceledException) { }
                finally { busy = false; }
                return;
            }
            await RunAsync("Buscando a configuração e os cadastros desativados…", async () =>
            {
                var data = await vm.ReadRetentionAsync(scope.Unit, cancellation.Token);
                if (cancellation.IsCancellationRequested || vm.RetentionScope != scope) return;
                savedPolicyDays = data.Policy.Days;
                NeverDelete.IsChecked = savedPolicyDays is null;
                PolicyDays.Text = (savedPolicyDays ?? 30).ToString();
                UpdatePolicyControls();
                ShowItems(data.Items);
                Feedback.Text = "";
                Controls.IsEnabled = true;
            });
        }
        finally
        {
            reading = null;
            if (displayed != workspace?.RetentionScope) RefreshScope();
        }
    }

    /// <summary>A lista só é trocada quando mudou; trocar devolve a seleção pela chave (o <see cref="ListingBehavior"/>), então o balão não pisca.</summary>
    private void ShowItems(IReadOnlyList<RetentionItem> items)
    {
        if (Items.ItemsSource is not IReadOnlyList<RetentionItem> current || !current.SequenceEqual(items)) Items.ItemsSource = items;
        ItemsEmpty.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void OnRetry(object sender, RoutedEventArgs e) => await LoadAsync();

    private void OnPolicyChanged(object sender, RoutedEventArgs e) => UpdatePolicyControls();

    private void UpdatePolicyControls()
    {
        if (SavePolicy is null) return;
        var automatic = NeverDelete.IsChecked == false;
        PolicyDays.Visibility = PolicyDaysLabel.Visibility = automatic ? Visibility.Visible : Visibility.Collapsed;
        PolicyActions.Visibility = automatic || savedPolicyDays.HasValue ? Visibility.Visible : Visibility.Collapsed;
        SavePolicy.Content = automatic ? "Salvar prazo" : "Salvar preferência";
    }

    // ----- balão: o que cada ação mostra depende do cadastro selecionado -----

    private RetentionItem? Selected => ListingBehavior.GetSelectedItems<RetentionItem>(Items) is [var only] ? only : null;

    private void OnBalloonRefreshed(object sender, RoutedEventArgs e)
    {
        var item = Selected;
        Schedule.Visibility = item?.Eligible == true ? Visibility.Visible : Visibility.Collapsed;
        CancelSchedule.Visibility = item?.ScheduledAt is not null ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void OnSavePolicy(object sender, RoutedEventArgs e)
    {
        if (workspace is not { } vm || vm.RetentionScope is not { } scope) return;
        int? days = null;
        if (NeverDelete.IsChecked == false && !TryDays(PolicyDays.Text, out days)) return;
        var question = days is { } time ? $"Excluir cadastros elegíveis automaticamente após {time} dias?"
            : "Desativar o prazo automático de exclusão? Os agendamentos individuais continuam válidos.";
        if (!Confirm(question)) return;
        if (await RunAsync("Salvando o prazo de exclusão…", () => vm.SaveRetentionAsync(scope.Unit, days))) await LoadAsync();
    }

    /// <summary>Agendar: o prazo é pedido num pop-up com a grade de cadastro; depois vem a confirmação de sempre. Sem confirmar, nada é agendado e o pop-up continua aberto.</summary>
    private async void OnSchedule(object sender, RoutedEventArgs e)
    {
        if (workspace is not { } vm || vm.RetentionScope is not { } scope || Selected is not { Eligible: true } item) return;
        var days = new TextBox { Text = "30", MaxLength = 5 };
        System.Windows.Automation.AutomationProperties.SetName(days, "Prazo deste cadastro em dias");
        EnterNavigation.SetGroup(days, "RetentionSettingsView.schedule");
        var field = new StackPanel();
        field.Children.Add(new TextBlock { Text = "Excluir daqui a quantos dias (1 a 36.500)", Style = (Style)FindResource("FieldLabelText") });
        field.Children.Add(days);
        var form = new FormGrid();
        form.Children.Add(field);
        var error = "";
        var saved = DialogService.ShowForm(Window.GetWindow(this), $"Agendar exclusão de {item.Name}", form, async () =>
        {
            error = "";
            if (!int.TryParse(days.Text, out var amount) || amount is < 1 or > 36500) { error = "Informe um prazo de 1 a 36.500 dias."; return false; }
            if (!Confirm($"Agendar a exclusão de {item.Name} para daqui a {amount} dias?", overDialog: true)) { error = "Nada foi agendado."; return false; }
            try { await vm.ScheduleDeletionAsync(scope.Unit, item.Id, amount); return true; }
            catch (NoraApiException failure) { error = failure.Message; return false; }
        }, "Agendar exclusão", error: () => error);
        if (saved) await LoadAsync();
    }

    private async void OnCancelSchedule(object sender, RoutedEventArgs e)
    {
        if (workspace is not { } vm || vm.RetentionScope is not { } scope || Selected is not { ScheduledAt: not null } item) return;
        if (!Confirm($"Cancelar a exclusão de {item.Name}?")) return;
        if (await RunAsync("Salvando o agendamento…", () => vm.ScheduleDeletionAsync(scope.Unit, item.Id, null))) await LoadAsync();
    }

    /// <summary>A ficha em linhas do cadastro desativado (duplo clique, segundo clique ou Enter), uma de cada vez.</summary>
    private Task OpenFile(object entry)
    {
        if (entry is not RetentionItem item) return Task.CompletedTask;
        var kind = displayed?.Unit is null ? "Conta OBS desativada" : "Consultório desativado";
        var fields = new List<DetailField> { new("Nome", item.Name, true, "Cadastro"), new("Tipo", kind, false, "Cadastro"), new("Situação", item.Tag, false, "Exclusão"), new("Detalhe", item.Status, false, "Exclusão") };
        if (item.ScheduledAt is { } date) fields.Add(new("Exclusão programada para", date.ToLocalTime().ToString("dd/MM/yyyy HH:mm", DisplayFormat.Culture), false, "Exclusão"));
        fields.Add(new("Pode ser excluído", item.Eligible ? "Sim" : "Não: há vínculos ou histórico de uso", false, "Exclusão"));
        var owner = Window.GetWindow(this);
        DialogService.ShowModal(new ItemDetailsWindow(new ItemDetails(item.Name, kind, fields)) { Owner = owner, Title = item.Name }, owner);
        return Task.CompletedTask;
    }

    private bool TryDays(string text, out int? days)
    {
        days = null;
        if (int.TryParse(text, out var amount) && amount is >= 1 and <= 36500) { days = amount; return true; }
        Feedback.Text = "Informe um prazo de 1 a 36.500 dias.";
        return false;
    }

    private async Task<bool> RunAsync(string status, Func<Task> work)
    {
        if (busy) return false;
        var wasEnabled = Controls.IsEnabled;
        busy = true;
        Controls.IsEnabled = false;
        Retry.Visibility = Visibility.Collapsed;
        Progress.Visibility = Visibility.Visible;
        Feedback.Text = status;
        try
        {
            await work();
            return true;
        }
        catch (OperationCanceledException) { return false; }
        catch (NoraApiException error)
        {
            Feedback.Text = error.Message;
            Retry.Visibility = Visibility.Visible;
            return false;
        }
        finally
        {
            busy = false;
            Controls.IsEnabled |= wasEnabled;
            Progress.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>A confirmação de sempre; vinda de dentro do pop-up de cadastro ela abre sobre ele (a janela ativa), não sobre a janela principal.</summary>
    private bool Confirm(string question, bool overDialog = false) => Confirmation.Ask(overDialog ? null : Window.GetWindow(this), Title, question);
}
