using System.Windows;
using System.Windows.Controls;
using Nora.Desktop.ViewModels;

namespace Nora.Desktop.Views;

/// <summary>Alertas da rede (Secretaria), com filtros de UBS e região.</summary>
public partial class NetworkAlertsView : UserControl
{
    public NetworkAlertsView() => InitializeComponent();
    private async void OnRefresh(object sender, RoutedEventArgs e) { if (DataContext is WorkspaceViewModel vm) await vm.RefreshNetworkAlertsAsync(); }
}
