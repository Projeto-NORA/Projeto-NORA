using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Nora.Api.Models;

namespace Nora.Api.Services;

/// <summary>Segredo, código de ativação e cabeçalho <c>X-Nora-Device</c> dos computadores do OBS. Só hashes vão para o banco; nada disto entra em log.</summary>
public static class ObsDeviceCredentials
{
    public const string Header = "X-Nora-Device";
    /// <summary>Sem I, O, 0 e 1 (40 bits em 8 símbolos).</summary>
    public const string CodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    public const int CodeLength = 8;
    public static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(15);

    /// <summary>Cabeçalho estrito: 32 hexadecimais (formato N do id), ponto e 64 hexadecimais. Malformado = ausente (nunca erro).</summary>
    public static DeviceCredential? Parse(string? header)
    {
        if (header is not { Length: 97 } || header[32] != '.') return null;
        for (var i = 0; i < 97; i++) if (i != 32 && !Uri.IsHexDigit(header[i])) return null;
        return Guid.TryParseExact(header.AsSpan(0, 32), "N", out var id) && id != Guid.Empty ? new(id, header[33..].ToUpperInvariant()) : null;
    }

    public static DeviceCredential? FromRequest(HttpRequest request) => Parse(request.Headers[Header].ToString());

    public static string NewSecret() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

    public static string HashSecret(string secret) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));

    /// <summary>Código novo, já normalizado (8 símbolos do alfabeto, sem hífen).</summary>
    public static string NewCode()
    {
        Span<char> code = stackalloc char[CodeLength];
        for (var i = 0; i < CodeLength; i++) code[i] = CodeAlphabet[RandomNumberGenerator.GetInt32(CodeAlphabet.Length)];
        return new string(code);
    }

    public static string Display(string code) => $"{code[..4]}-{code[4..]}";

    /// <summary>Maiúsculas, sem espaços e sem hífen; fora do alfabeto ou diferente de 8 símbolos → nulo.</summary>
    public static string? NormalizeCode(string? value)
    {
        if (value is null || value.Length > 40) return null;
        var code = new string(value.Where(c => c is not (' ' or '-')).ToArray()).ToUpperInvariant();
        return code.Length == CodeLength && code.All(c => CodeAlphabet.Contains(c)) ? code : null;
    }

    public static string HashCode(string normalizedCode) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("obs-device-code:" + normalizedCode)));

    /// <summary>Nome do computador: espaços normalizados, 1 a 60 caracteres, sem controle; senão 400.</summary>
    public static string ValidName(string? value)
    {
        var text = string.Join(' ', (value ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (text.Length is < 1 or > 60 || text.Any(char.IsControl)) throw new ApiInputException();
        return text;
    }

    public static string HeaderValue(DeviceCredential credential) => credential.Id.ToString("N", CultureInfo.InvariantCulture) + "." + credential.Secret;
}
