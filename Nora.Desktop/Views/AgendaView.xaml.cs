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
    private void OnToggleBooking(object sender, RoutedEventArgs e) => BookingPanel.Visibility = BookingPanel.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
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
        if (status == "cancelled" && !Nora.Desktop.Services.Confirmation.Ask(Window.GetWindow(this), "Cancelar consulta", "A consulta será cancelada e o horário pode ser oferecido como vaga relâmpago.", "Cancelar consulta", "Voltar")) return;
        await vm.ChangeAppointmentStatusAsync(status, status == "cancelled");
    }
}
