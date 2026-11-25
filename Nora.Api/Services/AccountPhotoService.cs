using System.Security.Cryptography;
using Microsoft.Net.Http.Headers;
using Nora.Api.DAOs;

namespace Nora.Api.Services;

/// <summary>
/// Foto de perfil de qualquer conta autenticada. O corpo do PUT é o binário cru: o tipo vem do Content-Type (parâmetros como <c>; charset</c> são ignorados) e precisa bater com os
/// bytes mágicos do arquivo; a leitura para ao passar de 2 MB (o Content-Length não é levado em conta), então um corpo maior nunca é carregado inteiro.
/// </summary>
public sealed class AccountPhotoService(AccountPhotoDao photos, TimeProvider time)
{
    public const int MaxBytes = 2 * 1024 * 1024;
    private static readonly string[] Supported = ["image/jpeg", "image/png", "image/webp"];

    /// <summary>Foto da conta; sem foto → 404 <c>photo-not-found</c>.</summary>
    public async Task<AccountPhoto> GetAsync(Guid accountId, CancellationToken ct) =>
        await photos.GetAsync(accountId, ct) ?? throw new ApiCodedException(StatusCodes.Status404NotFound, "photo-not-found");

    /// <summary>Valida e grava a foto lida de <paramref name="body"/>: tipo não suportado, vazio ou bytes que não batem → 400 <c>photo-invalid</c>; acima de 2 MB → 413 <c>photo-too-large</c>.</summary>
    public async Task SaveAsync(Guid accountId, string? contentType, Stream body, CancellationToken ct)
    {
        var type = MediaTypeHeaderValue.TryParse(contentType, out var parsed) ? parsed.MediaType.Value?.ToLowerInvariant() : null;
        if (type is null || !Supported.Contains(type)) throw Invalid();
        var content = await ReadLimitedAsync(body, ct);
        if (content.Length == 0 || DetectType(content) != type) throw Invalid();
        await photos.SaveAsync(accountId, new AccountPhoto(type, content, Convert.ToHexStringLower(SHA256.HashData(content))), time.GetUtcNow(), ct);
    }

    /// <summary>Remove a foto da conta (idempotente).</summary>
    public Task DeleteAsync(Guid accountId, CancellationToken ct) => photos.DeleteAsync(accountId, ct);

    /// <summary>Tipo pelos bytes mágicos: JPEG FF D8 FF; PNG 89 50 4E 47 0D 0A 1A 0A; WebP "RIFF" + 4 bytes de tamanho + "WEBP". Outro conteúdo → nulo.</summary>
    public static string? DetectType(ReadOnlySpan<byte> content)
    {
        ReadOnlySpan<byte> jpeg = [0xFF, 0xD8, 0xFF];
        ReadOnlySpan<byte> png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        if (content.StartsWith(jpeg)) return "image/jpeg";
        if (content.StartsWith(png)) return "image/png";
        if (content.Length >= 12 && content[..4].SequenceEqual("RIFF"u8) && content[8..12].SequenceEqual("WEBP"u8)) return "image/webp";
        return null;
    }

    /// <summary>Lê o corpo até 2 MB; o primeiro byte além disso encerra a leitura com 413.</summary>
    private static async Task<byte[]> ReadLimitedAsync(Stream body, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await body.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + read > MaxBytes) throw new ApiCodedException(StatusCodes.Status413PayloadTooLarge, "photo-too-large");
            buffer.Write(chunk, 0, read);
        }
        return buffer.ToArray();
    }

    private static ApiCodedException Invalid() => new(StatusCodes.Status400BadRequest, "photo-invalid");
}
