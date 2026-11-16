using System.Windows;
using System.Windows.Controls;
using Nora.Desktop.ViewModels;

namespace Nora.Desktop.Views;

/// <summary>Alertas da UBS ativa (OBS), avaliados pela API.</summary>
public partial class UnitAlertsView : UserControl
{
    public UnitAlertsView() => InitializeComponent();
    private async void OnRefresh(object sender, RoutedEventArgs e) { if (DataContext is WorkspaceViewModel vm) await vm.RefreshAlertsAsync(); }
}
