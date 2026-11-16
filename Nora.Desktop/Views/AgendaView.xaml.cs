using System.Windows;
using System.Windows.Controls;
using Nora.Desktop.ViewModels;

namespace Nora.Desktop.Views;

public partial class AgendaView : UserControl
{
    public AgendaView() => InitializeComponent();
    private void OnPrevious(object sender, RoutedEventArgs e) { if (DataContext is WorkspaceViewModel vm) vm.MoveAgenda(-1); }
    private void OnNext(object sender, RoutedEventArgs e) { if (DataContext is WorkspaceViewModel vm) vm.MoveAgenda(1); }
    private async void OnLoad(object sender, RoutedEventArgs e) { if (DataContext is WorkspaceViewModel vm) await vm.LoadAppointmentsAsync(); }
    private void OnAppointmentDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is ListBox list && ItemsControl.ContainerFromElement(list, e.OriginalSource as DependencyObject) is ListBoxItem && list.SelectedItem is { } item && Window.GetWindow(this) is ShellWindow shell)
        { shell.OpenItemDetails(item); e.Handled = true; }
    }
    private void OnAppointmentKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter && sender is ListBox { SelectedItem: { } item } && Window.GetWindow(this) is ShellWindow shell)
        { shell.OpenItemDetails(item); e.Handled = true; }
    }
    private async void OnCreate(object sender, RoutedEventArgs e) { if (DataContext is WorkspaceViewModel vm) await vm.CreateAppointmentAsync(); }
    private async void OnStatus(object sender, RoutedEventArgs e)
    {
        if (DataContext is not WorkspaceViewModel vm || sender is not Button { Tag: string status } || vm.SelectedAppointment is null) return;
        if (status == "cancelled" && MessageBox.Show("Confirma o cancelamento desta consulta?", "Cancelar consulta", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        await vm.ChangeAppointmentStatusAsync(status, status == "cancelled");
    }
    private async void OnCreateLightning(object sender, RoutedEventArgs e)
    {
        if (DataContext is not WorkspaceViewModel { CanCreateLightning: true } vm) return;
        if (MessageBox.Show("Oferecer o horário desta consulta cancelada para a fila da especialidade?", "Gerar Vaga Relâmpago", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            await vm.CreateUnitLightningSlotAsync();
    }
}
