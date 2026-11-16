using System.Threading.RateLimiting;

namespace Nora.Api.Services;

/// <summary>
/// Limite de taxa por conta autenticada para comunicação e Jornada (mesma ideia de <see cref="AccountRateLimiter"/>, em classe própria para
/// cada módulo ter a sua tabela de políticas). O contador é da conta, vindo da sessão já validada, sem consulta ao banco; o estado fica em
/// memória do processo. Sem permissão disponível → 429.
/// </summary>
public sealed class CommunityRateLimiter : IDisposable
{
    public const string AnnouncementPublish = "announcement-publish";
    public const string AnnouncementRead = "announcement-read";
    public const string AnnouncementPoll = "announcement-poll";
    public const string HealthRecordsView = "health-records-view";

    private readonly PartitionedRateLimiter<(string Policy, Guid Account)> limiter = PartitionedRateLimiter.Create<(string Policy, Guid Account), (string Policy, Guid Account)>(key =>
        RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions { PermitLimit = PermitLimit(key.Policy), Window = Window, QueueLimit = 0 }));

    private static int PermitLimit(string policy) => policy switch
    {
        AnnouncementPublish => 30,
        AnnouncementRead => 120,
        // O app consulta a cada 30 s (20 por 10 minutos); a folga cobre mais de um aparelho e a abertura da aba Avisos.
        AnnouncementPoll => 120,
        HealthRecordsView => 60,
        _ => throw new ArgumentOutOfRangeException(nameof(policy))
    };

    private static readonly TimeSpan Window = TimeSpan.FromMinutes(10);

    public void Require(string policy, Guid accountId)
    {
        using var lease = limiter.AttemptAcquire((policy, accountId));
        if (!lease.IsAcquired) throw new ApiRateLimitException();
    }

    public void Dispose() => limiter.Dispose();
}
