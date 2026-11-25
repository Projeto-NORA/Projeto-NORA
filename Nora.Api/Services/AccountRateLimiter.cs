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
        GoogleLink => 20,
        PushDevice => 30,
        ObsDevices => 30,
        ProfilePhoto => 30,
        _ => throw new ArgumentOutOfRangeException(nameof(policy))
    };

    public const string PasswordChange = "password-change";
    public const string PatientUnlink = "patient-unlink";
    /// <summary>Prévia da remoção do vínculo (leitura que o app faz ao abrir a confirmação).</summary>
    public const string PatientUnlinkPreview = "patient-unlink-preview";
    /// <summary>Vincular ou desvincular a conta Google (cada vínculo valida um ID token).</summary>
    public const string GoogleLink = "google-link";
    /// <summary>Registrar ou remover o token de push de um aparelho.</summary>
    public const string PushDevice = "push-device";
    /// <summary>Criar computador do OBS, gerar código de ativação e revogar (cada geração de código é uma permissão).</summary>
    public const string ObsDevices = "obs-devices";
    /// <summary>Enviar ou remover a foto de perfil (PUT e DELETE de /api/auth/photo).</summary>
    public const string ProfilePhoto = "profile-photo";

    /// <summary>Consome uma permissão da conta na política; sem permissão disponível → 429.</summary>
    public void Require(string policy, Guid accountId)
    {
        using var lease = limiter.AttemptAcquire((policy, accountId));
        if (!lease.IsAcquired) throw new ApiRateLimitException();
    }

    public void Dispose() => limiter.Dispose();
}
