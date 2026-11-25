namespace Nora.Desktop.ViewModels;

public sealed partial class ShellViewModel
{
    private bool compactAccess;
    public bool ShowCompactAccess => compactAccess && Workspace.IsSignedOut;

    public void EnableCompactAccess()
    {
        compactAccess = true;
        Workspace.EnableDesktopAccess();
        Workspace.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(WorkspaceViewModel.IsAuthenticated))
                NotifyAccessVisibility();
        };
        NotifyAccessVisibility();
    }

    private void NotifyAccessVisibility()
    {
        Notify(nameof(ShowCompactAccess));
        Notify(nameof(IsHome));
        Notify(nameof(IsSettings));
    }
}
