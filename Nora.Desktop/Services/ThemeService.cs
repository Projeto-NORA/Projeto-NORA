using Microsoft.Win32;
using System.Windows;
using Nora.Desktop.Models;

namespace Nora.Desktop.Services;

public sealed class ThemeService : IDisposable
{
    private readonly Application application;
    private ResourceDictionary? palette;
    private bool disposed;
    public Appearance Mode { get; private set; } = Appearance.System;
    public ThemeService(Application application)
    {
        this.application = application;
        SystemEvents.UserPreferenceChanged += OnSystemChanged;
        application.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/Nora.Desktop;component/Themes/Controls.xaml", UriKind.Relative) });
    }
    public static bool SystemIsDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
        }
        catch (System.Security.SecurityException) { return false; }
    }
    public void Apply(Appearance mode)
    {
        Mode = Enum.IsDefined(mode) ? mode : Appearance.System;
        var dark = Mode == Appearance.Dark || Mode == Appearance.System && SystemIsDark();
        var replacement = new ResourceDictionary { Source = new Uri($"/Nora.Desktop;component/Themes/{(dark ? "Dark" : "Light")}Theme.xaml", UriKind.Relative) };
        if (palette != null) application.Resources.MergedDictionaries.Remove(palette);
        application.Resources.MergedDictionaries.Add(replacement);
        palette = replacement;
    }
    private void OnSystemChanged(object sender, UserPreferenceChangedEventArgs args)
    {
        if (Mode == Appearance.System && !application.Dispatcher.HasShutdownStarted)
            application.Dispatcher.BeginInvoke(() => { if (!disposed && Mode == Appearance.System) Apply(Appearance.System); });
    }
    public void Dispose() { disposed = true; SystemEvents.UserPreferenceChanged -= OnSystemChanged; }
}
