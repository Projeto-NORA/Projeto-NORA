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
    public bool FullScreen { get; set; }
    public string LastPage { get; set; } = "home";
}

public sealed record DesktopProfile(string Id, string Name, bool IsMunicipal)
{
    public static DesktopProfile Operational { get; } = new("obs", "NORA OBS", false);
    public static DesktopProfile Municipal { get; } = new("municipal", "NORA Municipal", true);
}

public sealed record NavigationItem(string Id, string Icon, string Title, bool Available = false);
public sealed record MetricCard(string Title, string Icon, string Description);
public sealed record OperationalRow(string Reference, string Description, string Status);
