using System.Text;

namespace Nora.Api.Services;

/// <summary>Regras de texto compartilhadas por comunicados e Jornada.</summary>
internal static class CommunityText
{
    private static readonly UTF8Encoding Strict = new(false, true);

    /// <summary>Recusa texto que o UTF-8 não representa (par substituto solto), antes de o banco falhar com 500.</summary>
    public static bool Encodable(string text)
    {
        try { Strict.GetByteCount(text); return true; }
        catch (EncoderFallbackException) { return false; }
    }

    /// <summary>Texto de uma linha, aparado, de <paramref name="min"/> a <paramref name="max"/> caracteres, sem controle; fora disso → 400.</summary>
    public static string Line(string? value, int min, int max)
    {
        var text = value?.Trim() ?? "";
        if (text.Length < min || text.Length > max || text.Any(char.IsControl) || !Encodable(text)) throw new ApiInputException();
        return text;
    }

    /// <summary>Texto opcional: vazio vira nulo; acima de <paramref name="max"/> → 400.</summary>
    public static string? OptionalLine(string? value, int max) => string.IsNullOrWhiteSpace(value) ? null : Line(value, 1, max);
}
