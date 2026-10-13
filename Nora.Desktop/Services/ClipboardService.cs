using System.Windows;

namespace Nora.Desktop.Services;

public interface IClipboardService
{
    void SetText(string text);
}

public sealed class WindowsClipboardService : IClipboardService
{
    public void SetText(string text) => Clipboard.SetText(text);
}
