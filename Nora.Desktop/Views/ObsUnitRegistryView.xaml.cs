using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Nora.Desktop.ViewModels;

namespace Nora.Desktop.Views;
public partial class ObsUnitRegistryView : UserControl
{
    public ObsUnitRegistryView() => InitializeComponent();
    private async void OnLoadUnits(object sender, RoutedEventArgs e) { if (DataContext is WorkspaceViewModel vm) await vm.LoadObsUnitsAsync(); }
    private async void OnConfirmUnit(object sender, RoutedEventArgs e) { if (DataContext is WorkspaceViewModel vm) await vm.ConfirmObsUnitAsync(); }
    private async void OnRefresh(object sender, RoutedEventArgs e) { if (DataContext is WorkspaceViewModel vm) await vm.LoadObsRegistryAsync(); }
    private async void OnLookupCep(object sender, RoutedEventArgs e) { if (DataContext is WorkspaceViewModel vm) await vm.LookupObsRegistryCepAsync(); }
    private async void OnCreate(object sender, RoutedEventArgs e) { if (DataContext is WorkspaceViewModel vm) await vm.CreateObsRegistryAsync(); }
    private void OnEdit(object sender, RoutedEventArgs e) { if (DataContext is WorkspaceViewModel vm) vm.EditSelectedObsRegistry(); }
    private void OnNew(object sender, RoutedEventArgs e) { if (DataContext is WorkspaceViewModel vm) vm.NewObsRegistryDraft(); }
    private void OnLocate(object sender, RoutedEventArgs e)
    {
        if (DataContext is not WorkspaceViewModel vm || !vm.HasAvailableObsUnits) return;
        var dialog = new ObsLocationDialog { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() == true) vm.SuggestClosestObsUnit(dialog.Latitude, dialog.Longitude);
    }
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
        var description = patient.IsActive
            ? $"Desativar {patient.FullName} somente nesta UBS? As entradas e ofertas pendentes da unidade serão canceladas; consultas já agendadas serão mantidas."
            : $"Reativar {patient.FullName} nesta UBS? Entradas canceladas não serão restauradas.";
        if (MessageBox.Show(description, "Confirmar situação na UBS", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            await vm.SetObsRegistryActiveAsync();
    }
}
