using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Nora.Desktop.Models;
using Nora.Desktop.ViewModels;

namespace Nora.Desktop.Views;

/// <summary>
/// Evolução das consultas (concluídas, faltas, canceladas) de uma <see cref="ChartSeries"/>, desenhada pelo <see cref="LineChartView"/>: um ponto por dia e, em
/// períodos longos, um por semana ou por mês (<see cref="ChartData.For"/>). A tabela dos mesmos dados é do <see cref="ChartDataTable"/>; as linhas ficam em
/// <c>Data.Rows</c>.
/// </summary>
public partial class EvolutionChartView : UserControl
{
    private ChartSeries? series;

    /// <summary>Série que o gráfico desenha: cada tela (rede, UBS, Painel) entrega a sua; o componente é o mesmo.</summary>
    public static readonly DependencyProperty DataProperty = DependencyProperty.Register(nameof(Data), typeof(ChartSeries), typeof(EvolutionChartView), new PropertyMetadata(null, (owner, _) => ((EvolutionChartView)owner).Attach()));
    public ChartSeries? Data { get => (ChartSeries?)GetValue(DataProperty); set => SetValue(DataProperty, value); }

    public EvolutionChartView()
    {
        InitializeComponent();
        Loaded += (_, _) => Attach();
        Unloaded += (_, _) => Detach();
    }

    private void Attach() { Detach(); series = Data; if (series != null) series.PropertyChanged += OnChanged; Refresh(); }
    private void Detach() { if (series != null) series.PropertyChanged -= OnChanged; series = null; }
    private void OnChanged(object? sender, PropertyChangedEventArgs args) { if (args.PropertyName == nameof(ChartSeries.Days)) Refresh(); }

    private void Refresh()
    {
        var days = series?.Days ?? [];
        var hasData = series?.HasData == true && days.Count > 0;
        var grouping = ChartData.For(days.Count);
        Chart.Show(hasData ? ChartData.Evolution(days, grouping) : [], series?.State ?? "",
            hasData ? $"Gráfico de evolução de {days.Count} dias: {days.Sum(day => day.Completed)} concluídas, {days.Sum(day => day.Missed)} faltas e {days.Sum(day => day.Cancelled)} canceladas." : "Gráfico de evolução sem dados");
        StepText.Text = hasData ? ChartData.StepNote(grouping) : "";
        StepText.Visibility = StepText.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }
}
