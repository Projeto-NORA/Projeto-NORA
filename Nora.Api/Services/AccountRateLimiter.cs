using System.Threading.RateLimiting;

namespace Nora.Api.Services;

/// <summary>
/// Limite de taxa por conta autenticada para as rotas que exigem sessão. O limitador do ASP.NET Core roda antes do filtro de sessão e só
/// enxerga o IP, que atrás de NAT ou proxy é compartilhado por muitas pessoas; aqui o contador é da conta (id vindo da sessão já
/// validada, sem consulta extra ao banco), então uma conta esgotada não limita as demais e trocar de sessão não zera o contador.
/// O estado fica em memória do processo, como o dos limitadores por IP.
/// </summary>
public sealed class AccountRateLimiter : IDisposable
{
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(10);
    private readonly PartitionedRateLimiter<(string Policy, Guid Account)> limiter = PartitionedRateLimiter.Create<(string Policy, Guid Account), (string Policy, Guid Account)>(key =>
        RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions { PermitLimit = PermitLimit(key.Policy), Window = Window, QueueLimit = 0 }));

    private static int PermitLimit(string policy) => policy switch
    {
        PasswordChange or PatientUnlink => 10,
        PatientUnlinkPreview => 60,
        _ => throw new ArgumentOutOfRangeException(nameof(policy))
    };

    public const string PasswordChange = "password-change";
    public const string PatientUnlink = "patient-unlink";
    /// <summary>Prévia da remoção do vínculo (leitura que o app faz ao abrir a confirmação).</summary>
    public const string PatientUnlinkPreview = "patient-unlink-preview";

    /// <summary>Consome uma permissão da conta na política; sem permissão disponível → 429.</summary>
    public void Require(string policy, Guid accountId)
    {
        using var lease = limiter.AttemptAcquire((policy, accountId));
        if (!lease.IsAcquired) throw new ApiRateLimitException();
    }

    public void Dispose() => limiter.Dispose();
}
