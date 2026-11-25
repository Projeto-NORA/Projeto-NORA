using System.Globalization;
using System.Windows.Input;
using Nora.Desktop.Models;

namespace Nora.Desktop.ViewModels;

public sealed record ShortcutRow(string Id, string Label, string Combination, bool Editable);

public sealed partial class ShellViewModel
{
    public IReadOnlyList<ShortcutRow> ShortcutRows => DesktopShortcuts.Definitions.Select(definition =>
        new ShortcutRow(definition.Id, definition.Label, ResolveShortcut(definition).GetDisplayStringForCulture(CultureInfo.CurrentUICulture), definition.Editable)).ToArray();

    public KeyGesture ResolveShortcut(ShortcutDefinition definition) => DesktopShortcuts.Resolve(definition, preferences.Shortcuts);

    public string? ChangeShortcut(string id, KeyGesture gesture)
    {
        if (DesktopShortcuts.Definitions.FirstOrDefault(item => item.Id == id) is not { Editable: true } || !DesktopShortcuts.Allowed(gesture))
            return "Use Ctrl com outra tecla ou uma tecla de F1 a F12.";
        var conflict = DesktopShortcuts.Definitions.FirstOrDefault(item => item.Id != id && DesktopShortcuts.Same(ResolveShortcut(item), gesture));
        if (conflict is not null) return $"Essa combinação já é usada em {conflict.Label}.";
        preferences.Shortcuts ??= new(StringComparer.Ordinal);
        preferences.Shortcuts[id] = DesktopShortcuts.Serialize(gesture);
        Persist();
        Notify(nameof(ShortcutRows));
        return null;
    }
}
