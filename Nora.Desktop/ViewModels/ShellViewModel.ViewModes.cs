using Nora.Desktop.Models;

namespace Nora.Desktop.ViewModels;

public sealed partial class ShellViewModel
{
    /// <summary>Como a tela aparece (cards ou gráficos), pelo que a pessoa escolheu da última vez neste programa; sem escolha, cards.</summary>
    public ViewMode GetViewMode(string screen) =>
        preferences.ViewModes is { } modes && modes.TryGetValue(screen, out var saved) && saved == "charts" ? ViewMode.Charts : ViewMode.Cards;

    /// <summary>Lembra o modo da tela nas preferências. Cards é o padrão e não ocupa espaço no arquivo.</summary>
    public void SetViewMode(string screen, ViewMode mode)
    {
        if (string.IsNullOrWhiteSpace(screen) || GetViewMode(screen) == mode) return;
        preferences.ViewModes ??= new(StringComparer.Ordinal);
        if (mode == ViewMode.Charts) preferences.ViewModes[screen] = "charts";
        else preferences.ViewModes.Remove(screen);
        Persist();
    }
}
