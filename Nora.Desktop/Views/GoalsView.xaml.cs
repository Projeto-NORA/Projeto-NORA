using System.Windows;
using System.Windows.Controls;
using Nora.Desktop.Models;
using Nora.Desktop.Services;
using Nora.Desktop.ViewModels;

namespace Nora.Desktop.Views;

/// <summary>Metas da rede e por UBS (Secretaria): definir, alterar e remover; a situação vem dos indicadores.</summary>
public partial class GoalsView : UserControl
{
    public GoalsView() => InitializeComponent();
    private async void OnRefresh(object sender, RoutedEventArgs e) { if (DataContext is WorkspaceViewModel vm) await vm.LoadGoalsAsync(); }
    private async void OnSave(object sender, RoutedEventArgs e) { if (DataContext is WorkspaceViewModel vm && sender is Button { Tag: string metric }) await vm.SaveGoalAsync(metric); }
    private async void OnRemove(object sender, RoutedEventArgs e)
    {
        if (DataContext is not WorkspaceViewModel vm || sender is not Button { Tag: string metric }) return;
        if (!Confirmation.Ask(Window.GetWindow(this), "Remover meta", $"Remover a meta de {GoalMetric.Title(metric).ToLowerInvariant()} deste escopo? Os indicadores passam a usar a meta da rede (ou Sem meta)."))
            return;
        await vm.RemoveGoalAsync(metric);
    }
}
