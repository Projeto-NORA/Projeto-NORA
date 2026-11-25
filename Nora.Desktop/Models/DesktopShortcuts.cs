using System.Windows.Input;

namespace Nora.Desktop.Models;

public sealed record ShortcutDefinition(string Id, string Label, KeyGesture Gesture, bool Editable = true);

public static class DesktopShortcuts
{
    public static IReadOnlyList<ShortcutDefinition> Definitions { get; } =
    [
        new("home", "Ir à tela inicial", new(Key.D1, ModifierKeys.Control)),
        new("settings", "Ir a Configurações", new(Key.OemComma, ModifierKeys.Control, "Ctrl + ,")),
        new("search", "Pesquisar na tela", new(Key.F, ModifierKeys.Control), false),
        new("sidebar", "Alternar barra lateral", new(Key.B, ModifierKeys.Control)),
        new("theme", "Alternar tema", new(Key.T, ModifierKeys.Control | ModifierKeys.Shift)),
        new("fullscreen", "Entrar ou sair da tela cheia", new(Key.F11)),
        new("esc", "Fechar pesquisa ou sair de tela cheia", new(Key.Escape), false),
        new("close", "Fechar o aplicativo", new(Key.F4, ModifierKeys.Alt), false)
    ];

    private static readonly KeyGestureConverter Converter = new();

    public static KeyGesture Resolve(ShortcutDefinition definition, IReadOnlyDictionary<string, string>? overrides)
    {
        if (definition.Editable && overrides?.TryGetValue(definition.Id, out var saved) == true)
        {
            try
            {
                if (Converter.ConvertFromInvariantString(saved) is KeyGesture gesture && Allowed(gesture)) return gesture;
            }
            catch (Exception error) when (error is NotSupportedException or ArgumentException or FormatException) { }
        }
        return definition.Gesture;
    }

    public static string Serialize(KeyGesture gesture) => Converter.ConvertToInvariantString(new KeyGesture(gesture.Key, gesture.Modifiers)) ?? "";

    public static bool Same(KeyGesture first, KeyGesture second) => first.Key == second.Key && first.Modifiers == second.Modifiers;

    public static bool Allowed(KeyGesture gesture) =>
        !gesture.Modifiers.HasFlag(ModifierKeys.Windows) && !gesture.Modifiers.HasFlag(ModifierKeys.Alt) &&
        (gesture.Modifiers.HasFlag(ModifierKeys.Control) || gesture.Key is >= Key.F1 and <= Key.F12);
}
