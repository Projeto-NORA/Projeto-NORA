using System.Windows.Media;
using Nora.Desktop.Models;
using Nora.Desktop.Services;

namespace Nora.Desktop.ViewModels;

/// <summary>Foto de perfil da conta logada: lida depois do login, trocada ou removida pelo menu do avatar (barra lateral ou Configurações). Sem foto, o avatar mostra as iniciais.</summary>
public sealed partial class WorkspaceViewModel
{
    private ImageSource? accountPhoto;
    private int photoVersion;
    public ImageSource? AccountPhoto { get => accountPhoto; private set { accountPhoto = value; Notify(); Notify(nameof(HasAccountPhoto)); Notify(nameof(PhotoStateLabel)); } }
    public bool HasAccountPhoto => accountPhoto is not null;
    public string PhotoStateLabel => !IsAuthenticated ? "Entre na conta para escolher uma foto." : HasAccountPhoto ? "Foto de perfil definida." : "Sem foto: o avatar mostra as suas iniciais.";

    /// <summary>Lê a foto da conta em segundo plano. Falha de leitura não vira erro na tela: o avatar continua com as iniciais.</summary>
    private async Task LoadAccountPhotoAsync()
    {
        var version = ++photoVersion;
        try
        {
            var bytes = await api.AccountPhotoAsync();
            if (disposed || !IsAuthenticated || version != photoVersion) return;
            AccountPhoto = ProfilePhoto.ToImage(bytes);
        }
        catch (NoraApiException) { }
    }

    /// <summary>Reduz a imagem escolhida para no máximo 512 px, envia em JPEG e mostra a nova foto.</summary>
    public Task ChangeAccountPhotoAsync(byte[] original) => RunAsync(async () =>
    {
        var jpeg = ProfilePhoto.PrepareJpeg(original);
        await api.UploadAccountPhotoAsync(jpeg, "image/jpeg");
        photoVersion++;
        AccountPhoto = ProfilePhoto.ToImage(jpeg);
        Say(MessageKind.Success, "Foto de perfil atualizada.");
    });

    public Task RemoveAccountPhotoAsync() => RunAsync(async () =>
    {
        await api.DeleteAccountPhotoAsync();
        photoVersion++;
        AccountPhoto = null;
        Say(MessageKind.Success, "Foto de perfil removida.");
    });

    private void ClearAccountPhoto() { photoVersion++; AccountPhoto = null; }
}
