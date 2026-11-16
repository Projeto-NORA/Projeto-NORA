namespace Nora.Api;

/// <summary>Códigos de erro da Família com título e descrição próprios no problem+json; o status vem da exceção (sempre 409).</summary>
internal static class FamilyProblems
{
    private static readonly Dictionary<string, (string Code, string Title, string Detail)> Known = new(StringComparer.Ordinal)
    {
        ["family-link-exists"] = ("family-link-exists", "Pedido já existente", "Já existe um pedido pendente ou um vínculo aprovado com este dependente nesta UBS."),
        ["family-link-limit"] = ("family-link-limit", "Limite de dependentes", "Cancele pedidos pendentes ou encerre vínculos antes de pedir outro."),
        ["family-link-closed"] = ("family-link-closed", "Pedido já decidido", "O pedido ou vínculo mudou de situação e esta ação não vale mais. Atualize a lista."),
        ["dependent-inactive"] = ("dependent-inactive", "Cadastro do dependente inativo", "O cadastro do dependente nesta UBS está desativado. Reative-o antes de aprovar o vínculo.")
    };

    public static bool TryGet(string? code, out (string Code, string Title, string Detail) problem)
    {
        if (code is not null && Known.TryGetValue(code, out problem)) return true;
        problem = default; return false;
    }
}
