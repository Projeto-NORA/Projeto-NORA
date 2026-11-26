using System.Runtime.InteropServices;

namespace Nora.Desktop.Services;

/// <summary>Retângulo em pixels físicos, como o Windows o informa.</summary>
internal readonly record struct PixelBounds(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;
}

/// <summary>Monitor em que a janela está: a tela inteira (tela cheia) e a área de trabalho, sem a barra de tarefas (janela maximizada).</summary>
internal static class MonitorArea
{
    private const uint DefaultToNearest = 2;

    public static bool TryGet(IntPtr window, out PixelBounds monitor, out PixelBounds work)
    {
        monitor = work = default;
        var handle = MonitorFromWindow(window, DefaultToNearest);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (handle == IntPtr.Zero || !GetMonitorInfo(handle, ref info)) return false;
        monitor = new(info.Monitor.Left, info.Monitor.Top, info.Monitor.Right, info.Monitor.Bottom);
        work = new(info.Work.Left, info.Work.Top, info.Work.Right, info.Work.Bottom);
        return true;
    }

    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size; public NativeRect Monitor, Work; public uint Flags; }
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
}
