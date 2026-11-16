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
}

public sealed record DesktopProfile(string Id, string Name, bool IsMunicipal)
{
    public static DesktopProfile Operational { get; } = new("obs", "NORA OBS", false);
    public static DesktopProfile Municipal { get; } = new("municipal", "NORA Municipal", true);
}

/// <summary>Item do menu lateral. <see cref="Badge"/> é o contador de não lidos (comunicados) e <see cref="IsSelected"/> marca a página aberta.</summary>
public sealed class NavigationItem(string id, string icon, string title, bool available = false) : Nora.Desktop.Generics.ObservableObject
{
    private int badge;
    private bool selected;
    public string Id { get; } = id;
    public string Icon { get; } = icon;
    public string Title { get; } = title;
    public bool Available { get; } = available;
    public int Badge { get => badge; set { if (badge == value) return; badge = value; Notify(); Notify(nameof(HasBadge)); Notify(nameof(BadgeText)); Notify(nameof(AutomationName)); } }
    public bool HasBadge => badge > 0;
    public string BadgeText => badge > 99 ? "99+" : badge.ToString();
    /// <summary>O contador também vai no nome acessível: o número é texto, não só cor.</summary>
    public string AutomationName => badge > 0 ? $"{Title}, {badge} {(badge == 1 ? "não lido" : "não lidos")}" : Title;
    public bool IsSelected { get => selected; set { if (selected == value) return; selected = value; Notify(); } }
}
