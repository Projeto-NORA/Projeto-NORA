using System.Windows;
using System.Windows.Controls;
using Nora.Desktop.ViewModels;

namespace Nora.Desktop.Views;

/// <summary>Filtro compartilhado dos indicadores da rede (período, UBS, região e especialidade). Só aparece onde os quatro têm efeito.</summary>
public partial class NetworkFiltersView : UserControl
{
    public NetworkFiltersView() => InitializeComponent();
    private void OnApplyPeriod(object sender, RoutedEventArgs e) { if (DataContext is WorkspaceViewModel vm) vm.ApplyCustomPeriod(); }
}
