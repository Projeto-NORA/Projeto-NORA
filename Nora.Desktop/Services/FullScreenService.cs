using System.Windows;
using System.Windows.Interop;

namespace Nora.Desktop.Services;

public sealed class FullScreenService(Window window)
{
    private Rect restoreBounds;
    private WindowState restoreState;
    private WindowStyle restoreStyle;
    private ResizeMode restoreResize;
    private bool active;
    public void Apply(bool enabled)
    {
        if (enabled == active) return;
        if (enabled)
        {
            restoreState = window.WindowState;
            restoreBounds = window.WindowState == WindowState.Normal ? new(window.Left, window.Top, window.Width, window.Height) : window.RestoreBounds;
            restoreStyle = window.WindowStyle; restoreResize = window.ResizeMode;
            window.WindowState = WindowState.Normal;
            window.WindowStyle = WindowStyle.None;
            window.ResizeMode = ResizeMode.NoResize;
            if (MonitorArea.TryGet(new WindowInteropHelper(window).Handle, out var monitor, out _))
            {
                var matrix = PresentationSource.FromVisual(window)?.CompositionTarget?.TransformFromDevice ?? System.Windows.Media.Matrix.Identity;
                var origin = matrix.Transform(new Point(monitor.Left, monitor.Top));
                var end = matrix.Transform(new Point(monitor.Right, monitor.Bottom));
                window.Left = origin.X; window.Top = origin.Y; window.Width = end.X - origin.X; window.Height = end.Y - origin.Y;
            }
            else { window.Left = 0; window.Top = 0; window.Width = SystemParameters.PrimaryScreenWidth; window.Height = SystemParameters.PrimaryScreenHeight; }
        }
        else
        {
            window.WindowStyle = restoreStyle; window.ResizeMode = restoreResize;
            window.Left = restoreBounds.Left; window.Top = restoreBounds.Top; window.Width = restoreBounds.Width; window.Height = restoreBounds.Height;
            window.WindowState = restoreState;
        }
        active = enabled;
    }
}
