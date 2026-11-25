using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Nora.Desktop.Services;
using Nora.Desktop.ViewModels;

namespace Nora.Desktop.Views;

public partial class AccountAvatar : UserControl
{
    public static readonly DependencyProperty SizeProperty = DependencyProperty.Register(nameof(Size), typeof(double), typeof(AccountAvatar), new PropertyMetadata(36d));
    public double Size { get => (double)GetValue(SizeProperty); set => SetValue(SizeProperty, value); }
    public double InitialsSize => Math.Max(11, Size * 0.38);
    public AccountAvatar() => InitializeComponent();

    /// <summary>Menu do avatar ("Alterar foto" / "Remover foto"), usado na barra lateral e em Configurações.</summary>
    public static void OpenPhotoMenu(FrameworkElement target, WorkspaceViewModel workspace)
    {
        if (!workspace.IsAuthenticated) return;
        var menu = new ContextMenu { Style = (Style)Application.Current.FindResource("NoraContextMenu"), PlacementTarget = target, Placement = System.Windows.Controls.Primitives.PlacementMode.Top };
        var change = new MenuItem { Header = "Alterar foto", Icon = "", ToolTip = "Escolha uma imagem JPG, PNG ou WebP. Ela é reduzida para 512 px." };
        change.Click += async (_, _) => await ChooseAsync(target, workspace);
        var remove = new MenuItem { Header = "Remover foto", Icon = "", IsEnabled = workspace.HasAccountPhoto, ToolTip = "Volta a mostrar as suas iniciais." };
        remove.Click += async (_, _) =>
        {
            if (DialogService.Ask(Window.GetWindow(target), "Remover foto?", "O avatar volta a mostrar as suas iniciais.", "Remover", "Cancelar")) await workspace.RemoveAccountPhotoAsync();
        };
        menu.Items.Add(change); menu.Items.Add(remove);
        menu.IsOpen = true;
    }

    private static async Task ChooseAsync(FrameworkElement target, WorkspaceViewModel workspace)
    {
        var dialog = new OpenFileDialog { Title = "Escolher foto de perfil", Filter = ProfilePhoto.FileFilter, CheckFileExists = true };
        if (dialog.ShowDialog(Window.GetWindow(target)) != true) return;
        byte[] bytes;
        try
        {
            if (new FileInfo(dialog.FileName).Length > 30 * 1024 * 1024) { DialogService.Error(Window.GetWindow(target), "Imagem muito grande", "Escolha uma imagem de até 30 MB."); return; }
            bytes = await File.ReadAllBytesAsync(dialog.FileName);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            DialogService.Error(Window.GetWindow(target), "Foto não carregada", "Não foi possível ler o arquivo escolhido.");
            return;
        }
        await workspace.ChangeAccountPhotoAsync(bytes);
    }
}
