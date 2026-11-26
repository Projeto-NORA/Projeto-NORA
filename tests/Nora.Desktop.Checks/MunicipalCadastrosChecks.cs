using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using Nora.Desktop.Models;
using Nora.Desktop.Services;
using Nora.Desktop.ViewModels;
using Nora.Desktop.Views;

namespace Nora.Desktop.Checks;

/// <summary>
/// Painel Municipal, grupo cadastros e listagens (Cadastro de UBS, Cadastros de referência, Mapa da rede, Catálogo de especialidades, Comunicação e Usuários):
/// listagem aberta com balão de ações, ficha em linhas, pop-ups de cadastro e a ausência de botão Atualizar. Os ajudantes abaixo servem também às checagens
/// antigas da Secretaria, que foram adaptadas às telas novas.
/// </summary>
internal static partial class Program
{
    private static FrameworkElement PageHeaderOf(ShellWindow window) => (FrameworkElement)window.FindName("PageHeader");

    /// <summary>Um botão do cabeçalho da janela principal (as ações da tela, como "Nova UBS").</summary>
    private static Button HeaderButton(ShellWindow window, string name) =>
        Descendants<Button>(PageHeaderOf(window)).Single(button => button.IsVisible && (AutomationProperties.GetName(button) == name || button.Content?.ToString() == name));

    private static ListBox ListNamed(FrameworkElement content, string name) => Descendants<ListBox>(content).Single(box => AutomationProperties.GetName(box) == name);

    /// <summary>Seleciona linhas da listagem (o balão aparece, como num clique) e deixa a tela assentar.</summary>
    private static void SelectRows(ShellWindow window, ListBox list, params object[] items)
    {
        list.SelectedItems.Clear();
        foreach (var item in items) list.SelectedItems.Add(item);
        window.UpdateLayout();
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        window.UpdateLayout();
    }

    /// <summary>Um botão do balão de ações da listagem (pelo texto ou pelo nome de automação).</summary>
    private static Button BalloonButton(ListBox list, string name) =>
        Descendants<Button>(ListingBehavior.GetBalloon(list) ?? throw new InvalidOperationException("A listagem não tem balão.")).Single(button => button.Content?.ToString() == name || AutomationProperties.GetName(button) == name);

    /// <summary>Clica no botão do cabeçalho que abre um pop-up de cadastro e inspeciona o pop-up enquanto ele está aberto.</summary>
    private static void InspectHeaderForm(ShellWindow window, string buttonName, Action<FormDialog> inspect) =>
        InspectModal<FormDialog>(window, inspect, () => InvokeButton(HeaderButton(window, buttonName)));
}
