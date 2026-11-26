using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using Nora.Desktop.Models;

namespace Nora.Desktop.Views;

/// <summary>A grade de <see cref="ChartPanel"/> do Painel da UBS e dos Indicadores (modo gráficos): um painel por <see cref="UnitChartCard"/>.</summary>
public partial class UnitChartGrid : UserControl
{
    public static readonly DependencyProperty CardsProperty = DependencyProperty.Register(nameof(Cards), typeof(ObservableCollection<UnitChartCard>), typeof(UnitChartGrid),
        new PropertyMetadata(null, (owner, args) => ((UnitChartGrid)owner).Grid.ItemsSource = (ObservableCollection<UnitChartCard>?)args.NewValue));

    public UnitChartGrid() => InitializeComponent();

    /// <summary>Os painéis mostrados (a grade troca só o painel que mudou).</summary>
    public ObservableCollection<UnitChartCard>? Cards { get => (ObservableCollection<UnitChartCard>?)GetValue(CardsProperty); set => SetValue(CardsProperty, value); }
}
