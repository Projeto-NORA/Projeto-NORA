namespace Nora.Api.Services;

/// <summary>
/// Score de comparecimento definido pelo usuário em 2026-11-11:
/// concluídas ÷ (concluídas + faltas + soma dos pesos dos cancelamentos feitos pelo paciente no app).
/// Consultas ainda agendadas ou chamadas e cancelamentos registrados pela equipe não entram na conta.
/// </summary>
public static class AttendanceScore
{
    /// <summary>
    /// Fração de uma falta (0 a 1) que um cancelamento feito pelo paciente representa.
    /// <paramref name="lead"/> é a antecedência: início da consulta menos o instante do cancelamento.
    /// </summary>
    /// <remarks>
    /// Cada faixa inclui o seu limite inferior: cancelar com exatamente 2 h de antecedência pesa 0,8, não 0,9.
    /// Antecedência negativa (cancelamento registrado depois do horário) pesa como uma falta.
    /// </remarks>
    public static double CancellationWeight(TimeSpan lead) => lead.TotalHours switch
    {
        < 1 => 1.0,
        < 2 => 0.9,
        < 6 => 0.8,
        < 12 => 0.6,
        < 24 => 0.4,
        < 48 => 0.2,
        < 72 => 0.1,
        < 96 => 0.02,
        _ => 0.0
    };

    /// <summary>Devolve nulo quando ainda não há consulta encerrada para avaliar.</summary>
    public static double? Compute(long completed, long missed, double cancellationWeight)
    {
        var denominator = completed + missed + cancellationWeight;
        return denominator <= 0 ? null : completed / denominator;
    }
}
