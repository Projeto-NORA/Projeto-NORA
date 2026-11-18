namespace Nora.Desktop.Generics;

/// <summary>
/// Junta a "primeira página" que a API devolve a cada leitura (todos os itens ativos mais os encerrados mais recentes) com o histórico mais antigo que a pessoa já pediu em "Carregar mais histórico".
/// Regra do ciclo automático de 20 s: ele relê só a primeira página e <b>preserva</b> o que já estava carregado. O que já estava na lista e saiu da primeira página (porque novos itens
/// foram encerrados) continua nela, sem duplicar. Só se a primeira página perder todo o contato com o histórico já carregado (mais encerramentos num ciclo do que cabem numa página)
/// o histórico recomeça, e <see cref="ApplyPage"/> avisa por <c>restarted</c> para a tela dizer isso.
/// Um item que estava <b>ativo</b> na lista e não vem mais na primeira página foi encerrado (a API devolve todos os ativos) e é mais antigo que as encerradas da página: ele não pode sumir, então
/// <paramref name="asClosed"/> o converte em encerrado (sem saber se foi concluído ou cancelado) e ele continua na lista junto do histórico carregado.
/// </summary>
public sealed class HistoryPager<T, TKey>(Func<T, TKey> key, Func<T, bool> isActive, Func<T, T>? asClosed = null) where TKey : notnull
{
    private T[] shown = [];
    private string? pageCursor, moreCursor;
    private bool moreLoaded;

    /// <summary>Itens que a tela mostra agora: a primeira página da última leitura, depois o histórico que já estava carregado.</summary>
    public IReadOnlyList<T> Shown => shown;

    /// <summary>Cursor opaco para pedir encerrados mais antigos que todos os que já estão na lista; nulo quando não há mais.</summary>
    public string? Cursor => moreLoaded ? moreCursor : pageCursor;
    public bool HasMore => Cursor is not null;

    /// <summary>Aplica uma leitura da primeira página e devolve a lista a mostrar.</summary>
    public T[] ApplyPage(IReadOnlyList<T> items, string? cursor, out bool restarted)
    {
        restarted = false;
        var inPage = items.Select(key).ToHashSet();
        var previousClosed = shown.Where(item => !isActive(item)).ToArray();
        var vanished = asClosed is null ? [] : shown.Where(item => isActive(item) && !inPage.Contains(key(item))).Select(asClosed).ToArray();
        IEnumerable<T> retained = [];
        if (cursor is null)
        {
            // A página já é o histórico inteiro: nada fica de fora dela.
            moreLoaded = false; moreCursor = null;
        }
        else if (previousClosed.Length > 0 && !previousClosed.Any(item => inPage.Contains(key(item))))
        {
            // Sem nenhum encerrado em comum: não dá para garantir que não há buraco entre a página nova e o histórico carregado; recomeça.
            restarted = true; moreLoaded = false; moreCursor = null;
        }
        else retained = previousClosed.Where(item => !inPage.Contains(key(item)));
        pageCursor = cursor;
        shown = [.. items, .. retained, .. vanished];
        return shown;
    }

    /// <summary>Acrescenta uma página de encerrados mais antigos (sem repetir o que já está na lista) e guarda o cursor seguinte.</summary>
    public T[] ApplyOlder(IReadOnlyList<T> older, string? cursor)
    {
        var known = shown.Select(key).ToHashSet();
        shown = [.. shown, .. older.Where(item => !isActive(item) && known.Add(key(item)))];
        moreLoaded = true; moreCursor = cursor;
        return shown;
    }

    public void Clear() { shown = []; pageCursor = moreCursor = null; moreLoaded = false; }
}
