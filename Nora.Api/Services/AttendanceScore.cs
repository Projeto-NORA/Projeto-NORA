namespace Nora.Api.Services;

/// <summary>
/// Score de comparecimento. Por paciente (desde 2026-11-24) são pontos de 0 a 1000 por eventos (<see cref="Compute(IEnumerable{Event}, DateTimeOffset)"/>); a razão antiga
/// concluídas ÷ (concluídas + faltas + pesos dos cancelamentos do paciente) continua só nos indicadores agregados da UBS e da rede (<c>attendanceScore</c>).
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

    /// <summary>Teto dos pontos.</summary>
    public const int MaxPoints = 1000;
    /// <summary>Pontos de quem ainda não tem histórico na janela, e o ponto de partida do cálculo por eventos.</summary>
    public const int InitialPoints = 700;

    /// <summary>Tipo de um evento do score por pontos.</summary>
    public enum EventKind { Attended, Missed, PatientCancellation }

    /// <summary>
    /// Evento do score: comparecimento ou falta (no horário marcado) ou cancelamento feito pelo paciente (no instante do cancelamento, com a antecedência <paramref name="Lead"/>).
    /// <paramref name="Lightning"/>: a consulta veio do aceite de uma Vaga Relâmpago (<c>booked_by='lightning'</c>).
    /// </summary>
    public readonly record struct Event(DateTimeOffset At, EventKind Kind, TimeSpan Lead = default, bool Lightning = false);

    /// <summary>
    /// Perda de um cancelamento feito pelo paciente pela antecedência (início da consulta menos o instante do cancelamento), cada faixa com o limite inferior incluído: 48 h ou mais = 0; de 24 h a menos de 48 h = 10;
    /// de 6 h a menos de 24 h = 20; de 1 h a menos de 6 h = 30; menos de 1 h ou depois do horário = 40.
    /// </summary>
    public static int CancellationPenalty(TimeSpan lead) => lead.TotalHours switch
    {
        >= 48 => 0,
        >= 24 => 10,
        >= 6 => 20,
        >= 1 => 30,
        _ => 40
    };

    /// <summary>
    /// Score por eventos, estilo XP (decisão de 2026-11-24): começa em <see cref="InitialPoints"/> e processa os eventos em ordem cronológica, limitando a 0–1000 a cada passo.
    /// Comparecer: +60, mais bônus de sequência de +10 por comparecimento consecutivo anterior (até +40: o 1º vale 60, o 2º 70 … do 5º em diante 100), mais +20 se a consulta veio de uma Vaga Relâmpago aceita.
    /// Falta: −60 e a sequência volta a zero. Cancelamento pelo paciente: <see cref="CancellationPenalty"/> (não mexe na sequência). Penalidade de evento com mais de 90 dias antes de <paramref name="now"/> conta pela
    /// metade (arredondada para baixo). Cancelamento pela equipe, pelo sistema ou pela antecipação relâmpago não é evento (0). Exemplos: sem histórico = 700; dois cancelamentos em cima da hora = 700 − 40 − 40 = 620;
    /// três comparecimentos = 700 + 60 + 70 + 80 = 910; falta e depois comparecimento = 700 − 60 + 60 = 700.
    /// </summary>
    public static int Compute(IEnumerable<Event> events, DateTimeOffset now)
    {
        var points = InitialPoints; var streak = 0;
        foreach (var item in events.OrderBy(item => item.At))
        {
            var old = now - item.At > TimeSpan.FromDays(90);
            int Penalty(int value) => old ? value / 2 : value;
            switch (item.Kind)
            {
                case EventKind.Attended:
                    points += 60 + Math.Min(10 * streak, 40) + (item.Lightning ? 20 : 0); streak++; break;
                case EventKind.Missed:
                    points -= Penalty(60); streak = 0; break;
                default:
                    points -= Penalty(CancellationPenalty(item.Lead)); break;
            }
            points = Math.Clamp(points, 0, MaxPoints);
        }
        return points;
    }

    /// <summary>Faixa dos pontos: <c>low</c> (0–299), <c>regular</c> (300–599), <c>good</c> (600–799) e <c>excellent</c> (800–1000).</summary>
    public static string Band(int points) => points switch
    {
        < 300 => "low",
        < 600 => "regular",
        < 800 => "good",
        _ => "excellent"
    };
}
