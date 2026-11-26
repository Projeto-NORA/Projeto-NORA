using System.Windows;
using System.Windows.Controls;
using Nora.Desktop.ViewModels;

namespace Nora.Desktop.Views;

/// <summary>Expansor com a tabela dos dados de uma <see cref="ChartSeries"/> (<c>Data.Rows</c>), para as telas que mostram o número exato ao lado do gráfico.</summary>
public partial class ChartDataTable : UserControl
{
    public static readonly DependencyProperty DataProperty = DependencyProperty.Register(nameof(Data), typeof(ChartSeries), typeof(ChartDataTable), new PropertyMetadata(null));
    public ChartSeries? Data { get => (ChartSeries?)GetValue(DataProperty); set => SetValue(DataProperty, value); }

    public ChartDataTable() => InitializeComponent();
}
