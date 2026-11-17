using Nora.Api.DAOs;
using Nora.Api.Database;

namespace Nora.Api.Services;

/// <summary>Quantas linhas cada limpeza apagou.</summary>
public sealed record MaintenanceResult(int Sessions, int RegistrationChallenges, int PasswordResetCodes, int DeviceCodes = 0);

/// <summary>
/// Limpeza periódica do banco central: sessões revogadas ou vencidas há mais de 30 dias, desafios de cadastro e de recuperação de senha vencidos há mais de 1 dia e códigos de ativação de computadores do OBS vencidos
/// há mais de 1 dia (o código é limpo; o computador continua). Apaga em lotes pequenos,
/// cada lote em uma instrução própria (sem transação longa nem lock longo), pulando linhas travadas (<c>FOR UPDATE SKIP LOCKED</c>): várias instâncias da API podem rodar a limpeza ao mesmo tempo.
/// Os índices por data (migração 017) evitam varrer as tabelas. A sessão apagada já estava inválida havia 30 dias; o histórico de ações não depende delas.
/// </summary>
public sealed class MaintenanceService(DatabaseHelper central, TimeProvider time, ObsDeviceDao devices)
{
    public static readonly TimeSpan SessionRetention = TimeSpan.FromDays(30);
    public static readonly TimeSpan ChallengeRetention = TimeSpan.FromDays(1);
    public const int BatchSize = 500;

    public bool IsConfigured => central.IsConfigured;

    public async Task<MaintenanceResult> RunOnceAsync(CancellationToken ct, int batchSize = BatchSize)
    {
        var now = time.GetUtcNow();
        var sessionCutoff = now - SessionRetention; var challengeCutoff = now - ChallengeRetention;
        var sessions = await DeleteInBatchesAsync("DELETE FROM nora.sessions WHERE token_hash IN (SELECT token_hash FROM nora.sessions WHERE expires_at<$1 OR (revoked_at IS NOT NULL AND revoked_at<$1) LIMIT $2 FOR UPDATE SKIP LOCKED)", sessionCutoff, batchSize, ct);
        var registrations = await DeleteInBatchesAsync("DELETE FROM nora.patient_registration_challenges WHERE id IN (SELECT id FROM nora.patient_registration_challenges WHERE expires_at<$1 LIMIT $2 FOR UPDATE SKIP LOCKED)", challengeCutoff, batchSize, ct);
        var resets = await DeleteInBatchesAsync("DELETE FROM nora.patient_password_reset_codes WHERE challenge_id IN (SELECT challenge_id FROM nora.patient_password_reset_codes WHERE expires_at<$1 LIMIT $2 FOR UPDATE SKIP LOCKED)", challengeCutoff, batchSize, ct);
        var codes = 0;
        while (true)
        {
            var cleaned = await devices.CleanExpiredCodesAsync(challengeCutoff, batchSize, ct);
            codes += cleaned;
            if (cleaned < batchSize) break;
            ct.ThrowIfCancellationRequested();
        }
        return new(sessions, registrations, resets, codes);
    }

    private async Task<int> DeleteInBatchesAsync(string sql, DateTimeOffset cutoff, int batchSize, CancellationToken ct)
    {
        var total = 0;
        while (true)
        {
            int deleted;
            await using (var connection = await central.OpenAsync(ct)) deleted = await central.ExecuteAsync(connection, sql, ct, cutoff, batchSize);
            total += deleted;
            // Um lote incompleto esvaziou o que havia (ou o que não estava travado): não há por que insistir.
            if (deleted < batchSize) return total;
            ct.ThrowIfCancellationRequested();
        }
    }
}

/// <summary>
/// Chama <see cref="MaintenanceService"/> de tempos em tempos: primeiro passe um minuto depois da subida e depois a cada <c>Nora:Maintenance:IntervalHours</c> (padrão 6; 1 a 168).
/// <c>Nora:Maintenance:Enabled</c> (padrão verdadeiro) liga e desliga; intervalo inválido impede a inicialização.
/// </summary>
public sealed class MaintenanceWorker(MaintenanceService maintenance, EmailSuggestionReconciler emailSuggestions, IConfiguration configuration, ILogger<MaintenanceWorker> log) : BackgroundService
{
    private readonly TimeSpan interval = Interval(configuration);

    private static TimeSpan Interval(IConfiguration configuration)
    {
        var value = configuration["Nora:Maintenance:IntervalHours"];
        if (string.IsNullOrWhiteSpace(value)) return TimeSpan.FromHours(6);
        return int.TryParse(value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var hours) && hours is >= 1 and <= 168
            ? TimeSpan.FromHours(hours) : throw new InvalidOperationException("Nora:Maintenance:IntervalHours inválido: informe um inteiro de 1 a 168.");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("Nora:Maintenance:Enabled", true)) return;
        try
        {
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            using var timer = new PeriodicTimer(interval);
            do
            {
                try
                {
                    if (!maintenance.IsConfigured) continue;
                    var result = await maintenance.RunOnceAsync(stoppingToken);
                    if (result.Sessions + result.RegistrationChallenges + result.PasswordResetCodes + result.DeviceCodes > 0)
                        log.LogInformation("Limpeza periódica: {Sessions} sessões, {Registrations} desafios de cadastro, {Resets} de recuperação e {Codes} códigos de ativação apagados.", result.Sessions, result.RegistrationChallenges, result.PasswordResetCodes, result.DeviceCodes);
                    // Sugestões de e-mail que sobraram na caixa de saída de uma UBS (o commit central falhou depois do da UBS) voltam ao banco central.
                    var reconciled = await emailSuggestions.RunOnceAsync(stoppingToken);
                    if (reconciled > 0) log.LogInformation("Reconciliação: {Count} sugestões de e-mail recuperadas da caixa de saída das UBS.", reconciled);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { throw; }
                catch (Exception) { log.LogWarning("Não foi possível executar a limpeza periódica; a próxima tentativa acontece no próximo intervalo."); }
            } while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
