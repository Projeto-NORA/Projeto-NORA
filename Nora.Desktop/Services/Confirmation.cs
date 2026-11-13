using System.Windows;

namespace Nora.Desktop.Services;

/// <summary>Pergunta de confirmação antes de ações com efeito sobre outras pessoas. O teste substitui <see cref="Handler"/> para responder sem abrir janela.</summary>
public static class Confirmation
{
    public static Func<Window?, string, string, bool>? Handler { get; set; }

    public static bool Ask(Window? owner, string title, string text) =>
        Handler?.Invoke(owner, title, text) ?? MessageBox.Show(owner, text, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
}
