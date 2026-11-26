using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Nora.Desktop.Views;

namespace Nora.Desktop.Services;

/// <summary>Tipo do diálogo: define o ícone e a cor do círculo do ícone.</summary>
public enum DialogKind { Info, Success, Warning, Error, Question }

/// <summary>Pedido de diálogo, entregue ao <see cref="DialogService.Handler"/> dos testes.</summary>
public sealed record DialogRequest(DialogKind Kind, string Title, string Message, string Primary, string? Secondary);

/// <summary>Janela que escurece o conteúdo enquanto um diálogo da marca está aberto (a janela principal dos dois apps).</summary>
public interface IDialogHost
{
    void SetDimmed(bool dimmed);
}

/// <summary>
/// Diálogo padrão da marca: cantos arredondados, sombra, fundo escurecido, ícone por tipo, botão primário e secundário, temas claro e escuro.
/// Substitui o MessageBox do Windows em todos os avisos, confirmações e erros dos dois apps. Os testes respondem por <see cref="Handler"/>.
/// </summary>
public static class DialogService
{
    public static Func<DialogRequest, bool>? Handler { get; set; }

    /// <summary>Mostra o diálogo e devolve verdadeiro quando a pessoa escolhe o botão primário.</summary>
    public static bool Show(Window? owner, DialogKind kind, string title, string message, string primary = "Entendi", string? secondary = null)
    {
        var request = new DialogRequest(kind, title, message, primary, secondary);
        if (Handler is { } handler) return handler(request);
        var dialog = new NoraDialog(request);
        return ShowModal(dialog, owner) == true;
    }

    public static bool Ask(Window? owner, string title, string message, string confirm = "Confirmar", string cancel = "Cancelar") =>
        Show(owner, DialogKind.Question, title, message, confirm, cancel);

    public static void Info(Window? owner, string title, string message) => Show(owner, DialogKind.Info, title, message);
    public static void Success(Window? owner, string title, string message) => Show(owner, DialogKind.Success, title, message);
    public static void Warning(Window? owner, string title, string message) => Show(owner, DialogKind.Warning, title, message);
    public static void Error(Window? owner, string title, string message) => Show(owner, DialogKind.Error, title, message);

    /// <summary>
    /// Pop-up de cadastro da marca: título, X, o formulário (<paramref name="content"/>, com o próprio DataContext) numa área com rolagem e o botão primário à direita.
    /// <paramref name="save"/> roda quando a pessoa aciona o primário (clique ou Enter no último campo) com o botão desabilitado enquanto espera: verdadeiro fecha e
    /// este método devolve verdadeiro; falso mantém o pop-up aberto com a mensagem de <paramref name="error"/> dentro dele. X e Esc fecham sem salvar (devolve falso).
    /// <paramref name="secondary"/> é um botão opcional à esquerda do primário que roda <paramref name="secondaryAction"/> sem fechar.
    /// </summary>
    public static bool ShowForm(Window? owner, string title, FrameworkElement content, Func<Task<bool>> save, string primary = "Salvar", string? secondary = null, Func<Task>? secondaryAction = null, Func<string?>? error = null)
    {
        var dialog = new FormDialog(new FormDialogRequest(title, content, save, primary, secondary, secondaryAction, error));
        ShowModal(dialog, owner);
        return dialog.Saved;
    }

    /// <summary>Abre uma janela modal da marca: escurece a janela dona enquanto o diálogo está aberto.</summary>
    public static bool? ShowModal(Window dialog, Window? owner)
    {
        owner ??= Application.Current?.Windows.OfType<Window>().FirstOrDefault(window => window.IsActive) ?? Application.Current?.MainWindow;
        if (owner is { IsLoaded: true } && !ReferenceEquals(owner, dialog)) { dialog.Owner = owner; dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner; }
        else dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var host = dialog.Owner as IDialogHost;
        host?.SetDimmed(true);
        try { return dialog.ShowDialog(); }
        finally { host?.SetDimmed(false); }
    }

    /// <summary>Janela sem moldura do Windows que não pode ser transparente (tem conteúdo nativo, como o WebView2): pede cantos arredondados ao Windows 11.</summary>
    public static void RoundCorners(Window window)
    {
        window.SourceInitialized += (_, _) =>
        {
            if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)) return;
            var preference = 2;
            _ = DwmSetWindowAttribute(new WindowInteropHelper(window).Handle, 33, ref preference, sizeof(int));
        };
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
}
