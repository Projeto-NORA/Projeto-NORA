using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Nora.Desktop.ViewModels;

namespace Nora.Desktop.Views;
public partial class ObsUnitRegistryView : UserControl
{
    private readonly DispatcherTimer cepTimer = new() { Interval = TimeSpan.FromMilliseconds(600) };
    public ObsUnitRegistryView()
    {
        InitializeComponent();
        cepTimer.Tick += OnCepTimer;
        Unloaded += (_, _) => cepTimer.Stop();
    }
    private void OnCepChanged(object sender, TextChangedEventArgs e)
    {
        cepTimer.Stop();
        if (sender is TextBox { IsKeyboardFocusWithin: true } box && Services.BrazilianDataValidator.IsCompleteCep(box.Text)) cepTimer.Start();
    }
    private async void OnCepTimer(object? sender, EventArgs e)
    {
        cepTimer.Stop();
        if (DataContext is not WorkspaceViewModel { ObsRegistryDraftOpen: true } vm || !Services.BrazilianDataValidator.IsCompleteCep(vm.ObsRegistryCep)) return;
        if (!vm.CanOperate) { cepTimer.Start(); return; }
        await vm.LookupObsRegistryCepAsync();
    }
    private async void OnRefresh(object sender, RoutedEventArgs e) { if (DataContext is WorkspaceViewModel vm) await vm.LoadObsRegistryAsync(); }
    private async void OnLookupCep(object sender, RoutedEventArgs e) { if (DataContext is WorkspaceViewModel vm) await vm.LookupObsRegistryCepAsync(); }
    private async void OnCreate(object sender, RoutedEventArgs e) { if (DataContext is WorkspaceViewModel vm) await vm.CreateObsRegistryAsync(); }
    private void OnEdit(object sender, RoutedEventArgs e) { if (DataContext is WorkspaceViewModel vm) vm.EditSelectedObsRegistry(); }
    private void OnNew(object sender, RoutedEventArgs e) { if (DataContext is WorkspaceViewModel vm) vm.NewObsRegistryDraft(); }
    private async void OnUpdateEmail(object sender, RoutedEventArgs e)
    {
        if (DataContext is not WorkspaceViewModel vm || vm.SelectedObsRegistry is null) return;
        if (MessageBox.Show($"Corrigir o e-mail de {vm.SelectedObsRegistry.FullName} nesta UBS?", "Confirmar correção", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            await vm.UpdateObsRegistryEmailAsync();
    }
    private void OnDetails(object sender, RoutedEventArgs e)
    {
        if (DataContext is WorkspaceViewModel { SelectedObsRegistry: { } patient } && Window.GetWindow(this) is ShellWindow shell) shell.OpenItemDetails(patient);
    }
    private void OnPatientDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListBox list && ItemsControl.ContainerFromElement(list, e.OriginalSource as DependencyObject) is ListBoxItem && list.SelectedItem is { } item && Window.GetWindow(this) is ShellWindow shell)
        { shell.OpenItemDetails(item); e.Handled = true; }
    }
    private void OnPatientKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is ListBox { SelectedItem: { } item } && Window.GetWindow(this) is ShellWindow shell)
        { shell.OpenItemDetails(item); e.Handled = true; }
    }
    private async void OnSetActive(object sender, RoutedEventArgs e)
    {
        if (DataContext is not WorkspaceViewModel vm || vm.SelectedObsRegistry is not { } patient) return;
        string description;
        // O que a pessoa vai confirmar é fixado antes da pergunta: a lista pode ser relida enquanto ela está aberta, e o botão não pode virar o contrário.
        var activate = !patient.IsActive;
        if (patient.IsActive)
        {
            // Desativar cancela as consultas futuras da UBS: a API diz quantas antes da pergunta. Sem esse número a confirmação não é feita.
            var preview = await vm.PreviewObsDeactivationAsync();
            if (preview is null || vm.SelectedObsRegistry?.Cpf != patient.Cpf) return;
            description = WorkspaceViewModel.DeactivationConfirmationText(patient.FullName, preview);
        }
        else description = $"Reativar {patient.FullName} nesta UBS? Entradas e consultas canceladas não serão restauradas.";
        if (Services.Confirmation.Ask(Window.GetWindow(this), "Confirmar situação na UBS", description) && vm.SelectedObsRegistry?.Cpf == patient.Cpf)
            await vm.SetObsRegistryActiveAsync(patient.Cpf, activate);
    }
}
