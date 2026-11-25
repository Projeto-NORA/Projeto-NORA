using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Nora.Desktop.Models;
using Nora.Desktop.Services;
using Nora.Desktop.ViewModels;

namespace Nora.Desktop.Views;

public partial class RetentionSettingsView : UserControl
{
    private WorkspaceViewModel? workspace;
    private CancellationTokenSource? reading;
    private (Guid Account, Guid? Unit)? displayed;
    private int? savedPolicyDays;
    private bool busy;

    public RetentionSettingsView() => InitializeComponent();

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        workspace = DataContext as WorkspaceViewModel;
        if (workspace is not null) workspace.PropertyChanged += OnWorkspaceChanged;
        RefreshScope();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
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
            return;
        }
        if (displayed == scope || busy) return;
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        if (workspace is not { } vm || vm.RetentionScope is not { } scope) return;
        displayed = scope;
        Explanation.Text = scope.Unit is null
            ? "Contas OBS desativadas ficam elegíveis quando não têm vínculos nem histórico de uso."
            : "Consultórios desativados ficam elegíveis quando não têm vínculos com a agenda ou o atendimento.";
        using var cancellation = new CancellationTokenSource();
        reading = cancellation;
        try
        {
            await RunAsync("Buscando a configuração e os cadastros desativados…", async () =>
            {
                var data = await vm.ReadRetentionAsync(scope.Unit, cancellation.Token);
                if (cancellation.IsCancellationRequested || vm.RetentionScope != scope) return;
                savedPolicyDays = data.Policy.Days;
                NeverDelete.IsChecked = savedPolicyDays is null;
                PolicyDays.Text = (savedPolicyDays ?? 30).ToString();
                UpdatePolicyControls();
                var id = (Items.SelectedItem as RetentionItem)?.Id;
                Items.ItemsSource = data.Items;
                Items.SelectedItem = data.Items.FirstOrDefault(item => item.Id == id);
                Feedback.Text = data.Items.Count == 0 ? "Nenhum cadastro desativado." : "";
                Controls.IsEnabled = true;
            });
        }
        finally
        {
            reading = null;
            if (displayed != workspace?.RetentionScope) RefreshScope();
        }
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

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var item = Items.SelectedItem as RetentionItem;
        ItemActions.Visibility = item is null ? Visibility.Collapsed : Visibility.Visible;
        ItemStatus.Text = item?.Status ?? "";
        Schedule.IsEnabled = ItemDays.IsEnabled = item?.Eligible == true;
        CancelSchedule.IsEnabled = item?.ScheduledAt is not null;
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

    private async void OnSchedule(object sender, RoutedEventArgs e)
    {
        if (!TryDays(ItemDays.Text, out var days)) return;
        await ChangeScheduleAsync(days);
    }

    private async void OnCancelSchedule(object sender, RoutedEventArgs e) => await ChangeScheduleAsync(null);

    private async Task ChangeScheduleAsync(int? days)
    {
        if (workspace is not { } vm || vm.RetentionScope is not { } scope || Items.SelectedItem is not RetentionItem item) return;
        if (!Confirm(days is { } time ? $"Agendar a exclusão de {item.Name} para daqui a {time} dias?"
            : $"Cancelar a exclusão de {item.Name}?")) return;
        if (await RunAsync("Salvando o agendamento…", () => vm.ScheduleDeletionAsync(scope.Unit, item.Id, days)))
            await LoadAsync();
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

    private bool Confirm(string question) =>
        MessageBox.Show(Window.GetWindow(this), question, "Exclusão de itens desativados", MessageBoxButton.YesNo,
            MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes;
}
