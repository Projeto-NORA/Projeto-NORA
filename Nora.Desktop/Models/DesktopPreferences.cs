namespace Nora.Desktop.Models;

public enum Appearance { Light, Dark, System }

public sealed class DesktopPreferences
{
    public Appearance Appearance { get; set; } = Appearance.System;
    public bool ShowSidebarTips { get; set; } = true;
    public bool ConfirmClose { get; set; } = true;
    public bool SaveNavigation { get; set; } = true;
    public bool Animations { get; set; } = true;
    public bool RememberSidebar { get; set; } = true;
    public bool SidebarExpanded { get; set; } = true;
    public double SidebarWidth { get; set; } = 264;
    public bool FullScreen { get; set; }
    public string LastPage { get; set; } = "home";
    public Dictionary<string, string> Shortcuts { get; set; } = new(StringComparer.Ordinal);
}

public sealed record DesktopProfile(string Id, string Name, bool IsMunicipal)
{
    public static DesktopProfile Operational { get; } = new("obs", "NORA OBS", false);
    public static DesktopProfile Municipal { get; } = new("municipal", "NORA Municipal", true);
}

/// <summary>
/// Item do menu lateral. Uma área principal pode ter subabas (<see cref="Children"/>): ao abri-la, a barra lateral passa a mostrar só as subabas.
/// <see cref="Id"/> é a seção (página) aberta; <see cref="Sub"/> é a parte da página mostrada pela subaba. <see cref="Badge"/> é o contador de não lidos.
/// </summary>
public sealed class NavigationItem(string id, string icon, string title, bool available = false, string? tip = null, string? sub = null, IReadOnlyList<NavigationItem>? children = null) : Nora.Desktop.Generics.ObservableObject
{
    private int badge;
    private bool selected;
    public string Id { get; } = id;
    public string? Sub { get; } = sub;
    /// <summary>Destino do comando de navegação: "seção" ou "seção:subaba".</summary>
    /// <summary>Área com subabas abre a primeira subaba; item simples abre a própria seção.</summary>
    public string Route => Children.Count > 0 ? Children[0].Route : Sub is null ? Id : $"{Id}:{Sub}";
    public string Icon { get; } = icon;
    public string Title { get; } = title;
    public string Tip { get; } = tip ?? title;
    public bool Available { get; } = available;
    public IReadOnlyList<NavigationItem> Children { get; } = children ?? [];
    public bool HasChildren => Children.Count > 0;
    public int Badge { get => badge; set { if (badge == value) return; badge = value; Notify(); Notify(nameof(HasBadge)); Notify(nameof(BadgeText)); Notify(nameof(AutomationName)); } }
    public bool HasBadge => badge > 0;
    public string BadgeText => badge > 99 ? "99+" : badge.ToString();
    /// <summary>O contador também vai no nome acessível: o número é texto, não só cor.</summary>
    public string AutomationName => badge > 0 ? Id == "alerts" || Children.Any(child => child.Id == "alerts") ? $"{Title}, {badge} {(badge == 1 ? "novo alerta" : "novos alertas")}" : $"{Title}, {badge} {(badge == 1 ? "novo comunicado" : "novos comunicados")}" : Title;
    public bool IsSelected { get => selected; set { if (selected == value) return; selected = value; Notify(); } }
}