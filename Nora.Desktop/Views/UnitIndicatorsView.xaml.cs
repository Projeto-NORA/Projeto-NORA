using System.Windows;
using System.Windows.Controls;
using Nora.Desktop.ViewModels;

namespace Nora.Desktop.Views;

/// <summary>Indicadores da UBS ativa (OBS): período, cartões, Vaga Relâmpago, tabela por especialidade e série no tempo.</summary>
public partial class UnitIndicatorsView : UserControl
{
    public UnitIndicatorsView() => InitializeComponent();
    private void OnApplyPeriod(object sender, RoutedEventArgs e) { if (DataContext is WorkspaceViewModel vm) vm.ApplyUnitCustomPeriod(); }
}
