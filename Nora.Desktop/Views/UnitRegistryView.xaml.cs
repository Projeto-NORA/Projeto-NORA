using System.Windows;
using System.Windows.Controls;
using Nora.Desktop.ViewModels;

namespace Nora.Desktop.Views;

/// <summary>Consulta nominal (auditada) da UBS selecionada: cadastro de referência e agenda por período.</summary>
public partial class UnitRegistryView : UserControl
{
    public UnitRegistryView() => InitializeComponent();
    private async void OnReadRegistry(object sender, RoutedEventArgs e) { if (DataContext is WorkspaceViewModel vm) await vm.LoadUnitRegistryAsync(); }
    private async void OnReadAppointments(object sender, RoutedEventArgs e) { if (DataContext is WorkspaceViewModel vm) await vm.LoadMunicipalAppointmentsAsync(); }
}
