using System.Threading.RateLimiting;

namespace Nora.Api.Services;

/// <summary>
/// Limite por conta autenticada das rotas da Família e da troca de e-mail da conta (mesmo desenho de <see cref="AccountRateLimiter"/>: contador da conta, em memória do processo).
/// O agendamento e a confirmação de presença do responsável usam as políticas de <see cref="SchedulingRateLimiter"/>, que dividem o contador com as do próprio paciente.
/// Sem permissão disponível → 429.
/// </summary>
public sealed class FamilyRateLimiter : IDisposable
{
    /// <summary>Pedir vínculo com dependente (cada pedido confere um CPF na UBS: o limite baixo impede varredura de CPFs).</summary>
    public const string Request = "family-request";
    /// <summary>Escritas do responsável: cancelar pedido, encerrar vínculo, cancelar consulta, sair da fila, responder oferta.</summary>
    public const string Write = "family-write";
    /// <summary>Leituras do responsável (consultas, fila, ofertas, Jornada, score, disponibilidade, lista de dependentes). O app consulta as ofertas de 30 em 30 s (20 por 10 minutos).</summary>
    public const string Read = "family-read";
    /// <summary>Pedir o código da troca de e-mail (o servidor também limita a 3 envios por sugestão).</summary>
    public const string EmailCode = "email-suggestion-code";
    /// <summary>Confirmar o código da troca de e-mail (o banco também limita a 5 tentativas por código).</summary>
    public const string EmailConfirm = "email-suggestion-confirm";
    /// <summary>Ler ou dispensar a sugestão de e-mail.</summary>
    public const string EmailRead = "email-suggestion-read";

    private static readonly TimeSpan Window = TimeSpan.FromMinutes(10);
    private readonly PartitionedRateLimiter<(string Policy, Guid Account)> limiter = PartitionedRateLimiter.Create<(string Policy, Guid Account), (string Policy, Guid Account)>(key =>
        RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions { PermitLimit = PermitLimit(key.Policy), Window = Window, QueueLimit = 0 }));

    private static int PermitLimit(string policy) => policy switch
    {
        Request => 10,
        Write => 60,
        Read => 240,
        EmailCode => 10,
        EmailConfirm => 20,
        EmailRead => 60,
        _ => throw new ArgumentOutOfRangeException(nameof(policy))
    };

    /// <summary>Consome uma permissão da conta na política; sem permissão disponível → 429.</summary>
    public void Require(string policy, Guid accountId)
    {
        using var lease = limiter.AttemptAcquire((policy, accountId));
        if (!lease.IsAcquired) throw new ApiRateLimitException();
    }

    public void Dispose() => limiter.Dispose();
}
