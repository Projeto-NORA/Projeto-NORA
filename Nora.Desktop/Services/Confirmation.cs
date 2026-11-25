using System.Windows;

namespace Nora.Desktop.Services;

/// <summary>Pergunta de confirmação antes de ações com efeito sobre outras pessoas, no diálogo padrão da marca. O teste substitui <see cref="Handler"/> para responder sem abrir janela.</summary>
public static class Confirmation
{
    public static Func<Window?, string, string, bool>? Handler { get; set; }

    public static bool Ask(Window? owner, string title, string text, string confirm = "Confirmar", string cancel = "Cancelar") =>
        Handler?.Invoke(owner, title, text) ?? DialogService.Ask(owner, title, text, confirm, cancel);
}
