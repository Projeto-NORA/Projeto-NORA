using System.Runtime.InteropServices;
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
            var monitor = MonitorFromWindow(new WindowInteropHelper(window).Handle, 2);
            var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            if (GetMonitorInfo(monitor, ref info))
            {
                var matrix = PresentationSource.FromVisual(window)?.CompositionTarget?.TransformFromDevice ?? System.Windows.Media.Matrix.Identity;
                var origin = matrix.Transform(new Point(info.Monitor.Left, info.Monitor.Top));
                var end = matrix.Transform(new Point(info.Monitor.Right, info.Monitor.Bottom));
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
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size; public NativeRect Monitor, Work; public uint Flags; }
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
}
