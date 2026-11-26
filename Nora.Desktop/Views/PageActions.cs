using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;

namespace Nora.Desktop.Views;

/// <summary>
/// Ações da tela (alternador cards/gráficos, botão "+ Novo …"). A janela principal hospeda o conteúdo no canto direito do cabeçalho enquanto
/// a tela está visível; fora dela (prévias, janelas de teste e pop-ups) o conteúdo aparece na própria tela, no lugar onde foi declarado.
/// </summary>
[ContentProperty(nameof(Actions))]
public sealed class PageActions : Decorator
{
    private ShellWindow? shell;

    public FrameworkElement? Actions { get; set; }

    public PageActions()
    {
        Loaded += (_, _) =>
        {
            shell = Window.GetWindow(this) as ShellWindow;
            Child = shell is null ? Actions : null;
            shell?.RegisterPageActions(this);
        };
        Unloaded += (_, _) =>
        {
            shell?.UnregisterPageActions(this);
            shell = null;
        };
        IsVisibleChanged += (_, _) => shell?.UpdatePageActions();
    }

    protected override void OnInitialized(EventArgs args)
    {
        base.OnInitialized(args);
        Child = Actions;
    }
}
