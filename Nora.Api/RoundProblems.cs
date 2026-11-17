namespace Nora.Api;

/// <summary>
/// Códigos de erro da rodada final (computadores do OBS, "chamar próximo", aprovação das vagas e conflito entre UBS) com título e descrição próprios no problem+json;
/// o status vem da exceção. O código <c>other-unit-overlap</c> nunca diz qual UBS tem a outra consulta.
/// </summary>
internal static class RoundProblems
{
    private static readonly Dictionary<string, (string Code, string Title, string Detail)> Known = new(StringComparer.Ordinal)
    {
        ["device-required"] = ("device-required", "Computador não ativado", "Este computador não está ativado para a sua UBS. Peça ao administrador da UBS para ativá-lo."),
        ["invalid-activation-code"] = ("invalid-activation-code", "Código de ativação inválido", "O código está errado, venceu ou já foi usado. Peça um novo código ao administrador da UBS."),
        ["device-already-active"] = ("device-already-active", "Computador já ativado", "Este computador já está ativado. Para usá-lo em outra UBS, revogue-o antes."),
        ["device-revoked"] = ("device-revoked", "Computador desativado", "Este computador foi desativado pelo administrador da UBS ou a ativação não vale mais."),
        ["queue-empty"] = ("queue-empty", "Ninguém para chamar", "Nenhuma pessoa desta fila pode receber a oferta agora."),
        ["call-next-too-late"] = ("call-next-too-late", "Horário muito próximo", "A oferta precisa de 10 minutos completos antes do horário. Escolha um horário mais tarde."),
        ["approval-expired"] = ("approval-expired", "Prazo de aprovação encerrado", "Falta menos de 1 hora para a consulta e a vaga foi fechada."),
        ["other-unit-overlap"] = ("other-unit-overlap", "Consulta em outra UBS", "Já tem consulta neste horário em outra UBS.")
    };

    public static bool TryGet(string? code, out (string Code, string Title, string Detail) problem)
    {
        if (code is not null && Known.TryGetValue(code, out problem)) return true;
        problem = default; return false;
    }
}
