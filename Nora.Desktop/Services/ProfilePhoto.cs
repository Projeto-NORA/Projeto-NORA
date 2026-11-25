using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Nora.Desktop.Services;

/// <summary>Preparo da foto de perfil: abre JPG, PNG ou WebP, reduz para no máximo 512 px no maior lado e grava em JPEG.</summary>
public static class ProfilePhoto
{
    public const int MaxSide = 512;
    public const string FileFilter = "Imagens (*.jpg;*.jpeg;*.png;*.webp)|*.jpg;*.jpeg;*.png;*.webp";

    /// <summary>Converte a imagem para JPEG de até 512 px. Lança <see cref="NoraApiException"/> com texto para a pessoa quando a imagem não abre.</summary>
    public static byte[] PrepareJpeg(byte[] source)
    {
        BitmapSource frame;
        try
        {
            using var input = new MemoryStream(source);
            var decoder = BitmapDecoder.Create(input, BitmapCreateOptions.PreservePixelFormat | BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad);
            frame = decoder.Frames[0];
        }
        catch (Exception error) when (error is NotSupportedException or FileFormatException or ArgumentException or InvalidOperationException or IOException or System.Runtime.InteropServices.COMException)
        {
            throw new NoraApiException("Não foi possível abrir a imagem. Use uma foto JPG, PNG ou WebP.");
        }
        var scale = Math.Min(1d, (double)MaxSide / Math.Max(frame.PixelWidth, frame.PixelHeight));
        BitmapSource resized = scale < 1 ? new TransformedBitmap(frame, new ScaleTransform(scale, scale)) : frame;
        // JPEG não tem transparência: o fundo transparente de um PNG vira branco.
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.DrawRectangle(Brushes.White, null, new System.Windows.Rect(0, 0, resized.PixelWidth, resized.PixelHeight));
            context.DrawImage(resized, new System.Windows.Rect(0, 0, resized.PixelWidth, resized.PixelHeight));
        }
        var target = new RenderTargetBitmap(resized.PixelWidth, resized.PixelHeight, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);
        foreach (var quality in new[] { 88, 75, 60 })
        {
            var encoder = new JpegBitmapEncoder { QualityLevel = quality };
            encoder.Frames.Add(BitmapFrame.Create(target));
            using var output = new MemoryStream();
            encoder.Save(output);
            if (output.Length <= NoraApiClient.MaxPhotoBytes) return output.ToArray();
        }
        throw new NoraApiException("A foto passou de 2 MB mesmo reduzida. Escolha outra imagem.");
    }

    /// <summary>Imagem pronta para o avatar (congelada); nula quando os bytes não abrem.</summary>
    public static ImageSource? ToImage(byte[]? bytes)
    {
        if (bytes is not { Length: > 0 }) return null;
        try
        {
            var image = new BitmapImage();
            using var stream = new MemoryStream(bytes);
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = 160;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception error) when (error is NotSupportedException or FileFormatException or ArgumentException or InvalidOperationException or IOException or System.Runtime.InteropServices.COMException)
        {
            return null;
        }
    }
}
