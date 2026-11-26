using System.ComponentModel;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using Microsoft.Win32;
using Nora.Desktop.Models;
using Nora.Desktop.Services;
using Nora.Desktop.ViewModels;

namespace Nora.Desktop.Views;

/// <summary>
/// Relatórios e metas (Secretaria): as metas da rede e por UBS numa listagem aberta (editar abre o pop-up de cadastro, remover é a lixeira com confirmação),
/// a situação das metas por UBS e a exportação do resumo em CSV (botão do cabeçalho).
/// </summary>
public partial class GoalsView : UserControl
{
    public GoalsView()
    {
        InitializeComponent();
        ListingBehavior.SetOpenHandler(GoalList, item => { OpenDetails(item); return Task.CompletedTask; });
        ListingBehavior.AddBalloonRefreshedHandler(GoalList, (_, _) => RefreshBalloon());
        // A tela ocupa a altura da área de conteúdo: a tabela da situação rola sozinha e o balão fica no pé da tela, sem faixa vazia sob as metas.
        ContentAreaFill.Attach(this);
    }

    /// <summary>"Remover" só serve quando todas as metas selecionadas têm meta gravada neste escopo; senão a ação some do balão.</summary>
    private void RefreshBalloon()
    {
        var selected = ListingBehavior.GetSelectedItems<GoalEditor>(GoalList);
        RemoveButton.Visibility = selected.Count > 0 && selected.All(goal => goal.HasCurrent) ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnEdit(object sender, RoutedEventArgs e)
    {
        if (ListingBehavior.GetSelectedItems<GoalEditor>(GoalList) is [var goal]) EditGoal(goal);
    }

    private void EditGoal(GoalEditor goal)
    {
        if (DataContext is not WorkspaceViewModel workspace) return;
        var form = BuildForm(goal);
        string? busy = null;
        var saved = DialogService.ShowForm(Window.GetWindow(this), $"Meta de {goal.Title.ToLowerInvariant()}", form,
            async () =>
            {
                // Outra ação ainda em andamento: a gravação não chegaria a rodar, e a mensagem de sucesso que estivesse na faixa não pode fechar o pop-up.
                busy = workspace.CanOperate ? null : "Aguarde a ação em andamento terminar e tente de novo.";
                if (busy is not null) return false;
                await workspace.SaveGoalAsync(goal.Metric);
                // A sessão caiu durante a gravação: o pop-up fecha (a janela volta para o acesso) em vez de ficar aberto sobre ele.
                return !workspace.IsAuthenticated || workspace.MessageKind == MessageKind.Success;
            },
            "Salvar meta", error: () => busy ?? (goal.Error.Length > 0 ? goal.Error : workspace.MessageKind == MessageKind.Error ? workspace.Message : null));
        if (!saved || !workspace.IsAuthenticated) goal.Revert();
        ListingBehavior.ClearSelection(GoalList);
    }

    /// <summary>O formulário da meta no pop-up (sem card): a dica e o valor (com a unidade na espera); o erro aparece no próprio pop-up.</summary>
    private static FormGrid BuildForm(GoalEditor goal)
    {
        static StackPanel Field(string label, FrameworkElement control)
        {
            var panel = new StackPanel();
            panel.Children.Add(new TextBlock { Text = label, Style = (Style)Application.Current.FindResource("FieldLabelText") });
            panel.Children.Add(control);
            return panel;
        }
        var form = new FormGrid { DataContext = goal };
        var hint = new TextBlock { Text = goal.Hint, TextWrapping = TextWrapping.Wrap, Style = (Style)Application.Current.FindResource("Muted") };
        FormGrid.SetFullRow(hint, true);
        form.Children.Add(hint);
        var value = new TextBox { MinHeight = 38, VerticalContentAlignment = VerticalAlignment.Center };
        value.SetBinding(TextBox.TextProperty, new Binding(nameof(GoalEditor.Input)) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
        AutomationProperties.SetName(value, $"Valor da meta de {goal.Title}");
        EnterNavigation.SetGroup(value, "meta-pop-up");
        form.Children.Add(Field(goal.IsWait ? goal.Bound : $"{goal.Bound} (%)", value));
        if (goal.IsWait)
        {
            var unit = new ComboBox { MinHeight = 38, SelectedValuePath = "Tag", Items = { new ComboBoxItem { Content = "horas", Tag = "h" }, new ComboBoxItem { Content = "dias", Tag = "d" } } };
            unit.SetBinding(System.Windows.Controls.Primitives.Selector.SelectedValueProperty, new Binding(nameof(GoalEditor.WaitUnit)));
            AutomationProperties.SetName(unit, "Unidade da espera máxima");
            EnterNavigation.SetGroup(unit, "meta-pop-up");
            form.Children.Add(Field("Unidade", unit));
        }
        return form;
    }

    private async void OnRemove(object sender, RoutedEventArgs e)
    {
        // Outra ação em andamento: a remoção não chegaria a rodar (e a confirmação seria pedida à toa).
        if (DataContext is not WorkspaceViewModel { CanEditGoals: true } workspace) return;
        var selected = ListingBehavior.GetSelectedItems<GoalEditor>(GoalList).Where(goal => goal.HasCurrent).ToList();
        if (selected.Count == 0) return;
        var consequence = "Os indicadores passam a usar a meta da rede (ou Sem meta).";
        var asked = selected.Count == 1
            ? Confirmation.Ask(Window.GetWindow(this), "Remover meta", $"Remover a meta de {GoalMetric.Title(selected[0].Metric).ToLowerInvariant()} deste escopo? {consequence}")
            : Confirmation.Ask(Window.GetWindow(this), "Remover metas", $"Remover as {selected.Count} metas selecionadas deste escopo? {consequence}");
        if (!asked) return;
        try
        {
            foreach (var goal in selected)
            {
                await workspace.RemoveGoalAsync(goal.Metric);
                // Na primeira falha o lote para: a mensagem de erro fica na faixa (a remoção seguinte a trocaria por um sucesso) e as metas restantes continuam selecionadas.
                if (workspace.MessageKind == MessageKind.Error || !workspace.IsAuthenticated) return;
            }
            ListingBehavior.ClearSelection(GoalList);
        }
        catch (Exception error) when (error is not OutOfMemoryException) { workspace.ReportUnexpected(error); }
    }

    /// <summary>A ficha da meta (duplo clique ou Enter): a métrica, o limite, a meta gravada neste escopo e o que a UBS herda da rede.</summary>
    private void OpenDetails(object item)
    {
        if (item is not GoalEditor goal || DataContext is not WorkspaceViewModel workspace) return;
        var fields = new List<DetailField>
        {
            new("Métrica", goal.Title), new("Limite", goal.Bound), new("Escopo", ScopeName(workspace)), new("Meta neste escopo", goal.Current)
        };
        if (goal.Inherited.Length > 0) fields.Add(new("Meta da rede", goal.Inherited));
        fields.Add(new("Como funciona", goal.Hint));
        var owner = Window.GetWindow(this);
        var dialog = new ItemDetailsWindow(new ItemDetails(goal.Title, "Meta da rede", fields)) { Owner = owner, Title = goal.Title };
        PropertyChangedEventHandler closeOnSignOut = (_, changed) => { if (changed.PropertyName == nameof(WorkspaceViewModel.IsAuthenticated) && !workspace.IsAuthenticated) dialog.Close(); };
        workspace.PropertyChanged += closeOnSignOut;
        dialog.Closed += (_, _) => workspace.PropertyChanged -= closeOnSignOut;
        DialogService.ShowModal(dialog, owner);
    }

    /// <summary>O escopo das metas na ficha: "Toda a rede" ou o nome da UBS (a frase longa fica no cabeçalho da tela).</summary>
    private static string ScopeName(WorkspaceViewModel workspace) =>
        workspace.GoalScope == Guid.Empty ? "Toda a rede" : workspace.Units.FirstOrDefault(unit => unit.Id == workspace.GoalScope)?.Name ?? "UBS selecionada";

    private void OnExport(object sender, RoutedEventArgs e)
    {
        if (DataContext is not WorkspaceViewModel { CanExportSummary: true } workspace) return;
        var dialog = new SaveFileDialog { Title = "Exportar resumo agregado", Filter = "Arquivo CSV (*.csv)|*.csv", DefaultExt = ".csv", FileName = "nora-resumo-rede.csv", AddExtension = true };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true || !workspace.CanExportSummary) return;
        try
        {
            File.WriteAllText(dialog.FileName, workspace.CreateSummaryCsv(), new UTF8Encoding(true));
            DialogService.Success(Window.GetWindow(this), "Resumo exportado", "O arquivo CSV foi salvo no local escolhido.");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            DialogService.Error(Window.GetWindow(this), "Exportação não concluída", "Não foi possível salvar o arquivo. Confira o local escolhido e tente de novo.");
        }
    }
}
