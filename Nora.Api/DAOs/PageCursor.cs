using System.Buffers.Text;
using System.Globalization;
using System.Text;
using Nora.Api.Services;

namespace Nora.Api.DAOs;

/// <summary>
/// Cursor opaco da paginação do histórico das listas do OBS (P4): a posição (instante e chave) do último item devolvido, em base64 de URL. O cliente só o devolve em <c>historyBefore</c>; o servidor o decodifica e usa o par
/// como limite exclusivo (<c>(instante, chave) &lt; cursor</c>), de modo que nenhum item se repete nem é pulado entre páginas, mesmo com itens novos entrando no meio.
/// </summary>
internal static class PageCursor
{
    public const int DefaultLimit = 100, MaxLimit = 500;

    public static string Encode(DateTimeOffset at, string key) => Base64Url.EncodeToString(Encoding.UTF8.GetBytes(at.UtcTicks.ToString(CultureInfo.InvariantCulture) + "|" + key));

    /// <summary>Cursor inválido (não decodifica ou sem as duas partes) → 400.</summary>
    public static (DateTimeOffset At, string Key) Decode(string cursor)
    {
        try
        {
            var parts = Encoding.UTF8.GetString(Base64Url.DecodeFromChars(cursor)).Split('|', 2);
            if (parts.Length == 2 && parts[1].Length > 0 && long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks) && ticks <= DateTimeOffset.MaxValue.UtcTicks)
                return (new DateTimeOffset(ticks, TimeSpan.Zero), parts[1]);
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException) { }
        throw new ApiInputException();
    }

    /// <summary>Limite de itens encerrados por página: padrão 100, de 1 a 500; fora disso → 400.</summary>
    public static int Limit(int? requested) => requested is null ? DefaultLimit : requested is >= 1 and <= MaxLimit ? requested.Value : throw new ApiInputException();
}
