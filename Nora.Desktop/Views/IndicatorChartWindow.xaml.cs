using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Nora.Desktop.Models;

namespace Nora.Desktop.Views;

/// <summary>Pop-up do mini botão de gráfico dos cartões: o título e o alternador Linha | Pizza (um <see cref="ChartPanel"/>) com o gráfico do indicador, nas cores do tema.</summary>
public partial class IndicatorChartWindow : Window
{
    public IndicatorChartWindow(IndicatorChart chart)
    {
        InitializeComponent();
        Title = chart.Title;
        ChartHost.Title = chart.Title;
        ChartHost.LineNote = chart.LineNote;
        ChartHost.PieNote = chart.PieNote;
        ChartHost.Lines = chart.Line.Count == 0 ? [] : [new ChartLine(chart.Title, "ChartBrush1", chart.Line)];
        ChartHost.Slices = chart.Pie;
    }

    /// <summary>O gráfico do pop-up (título, alternador e desenho).</summary>
    public ChartPanel Chart => ChartHost;

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    /// <summary>Arrasta pelo topo do pop-up, mas nunca pelo alternador nem pelos botões.</summary>
    private void OnDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || e.GetPosition(Frame).Y > 70) return;
        for (var node = e.OriginalSource as DependencyObject; node is not null && !ReferenceEquals(node, Frame); node = ParentOf(node))
            if (node is ButtonBase or ListBoxItem) return;
        DragMove();
    }

    private static DependencyObject? ParentOf(DependencyObject node) => node is Visual ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node);
}
