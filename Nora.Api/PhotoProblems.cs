namespace Nora.Api;

/// <summary>Códigos de erro da foto de perfil (<c>/api/auth/photo</c>) com título e descrição próprios no problem+json; o status vem da exceção.</summary>
internal static class PhotoProblems
{
    private static readonly Dictionary<string, (string Code, string Title, string Detail)> Known = new(StringComparer.Ordinal)
    {
        ["photo-not-found"] = ("photo-not-found", "Sem foto de perfil", "Esta conta ainda não tem foto de perfil."),
        ["photo-invalid"] = ("photo-invalid", "Foto inválida", "Envie uma imagem JPEG, PNG ou WebP. O tipo informado precisa ser o mesmo do arquivo e o arquivo não pode estar vazio."),
        ["photo-too-large"] = ("photo-too-large", "Foto grande demais", "A foto pode ter no máximo 2 MB. Reduza a imagem e tente novamente.")
    };

    public static bool TryGet(string? code, out (string Code, string Title, string Detail) problem)
    {
        if (code is not null && Known.TryGetValue(code, out problem)) return true;
        problem = default; return false;
    }
}
