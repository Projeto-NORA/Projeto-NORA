namespace Nora.Api;

/// <summary>Códigos de erro da agenda (Fase 3) com título e descrição próprios no problem+json; o status vem da exceção.</summary>
internal static class SchedulingProblems
{
    private static readonly Dictionary<string, (string Code, string Title, string Detail)> Known = new(StringComparer.Ordinal)
    {
        ["catalog-required"] = ("catalog-required", "Profissional e especialidade obrigatórios", "Esta UBS marca consultas pelo catálogo: informe professionalId e specialtyId de um horário livre da grade."),
        ["slot-unavailable"] = ("slot-unavailable", "Horário indisponível", "O horário escolhido não está livre. Escolha outro horário da lista de disponibilidade."),
        ["patient-overlap"] = ("patient-overlap", "Consulta no mesmo horário", "O paciente já tem uma consulta que cruza este horário."),
        ["specialty-limit"] = ("specialty-limit", "Limite da especialidade", "Já existe uma consulta futura marcada pelo aplicativo nesta especialidade. Cancele-a ou aguarde a data para marcar outra."),
        ["booking-window"] = ("booking-window", "Horário que já passou", "Esse horário já começou ou passou. Escolha um horário que ainda não começou."),
        ["confirmation-window"] = ("confirmation-window", "Fora do prazo de confirmação", "A presença é confirmada de 48 horas até 2 horas antes da consulta.")
    };

    public static bool TryGet(string? code, out (string Code, string Title, string Detail) problem)
    {
        if (code is not null && Known.TryGetValue(code, out problem)) return true;
        problem = default; return false;
    }
}
