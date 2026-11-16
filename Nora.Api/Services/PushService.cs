using Npgsql;
using Nora.Api.DAOs;
using Nora.Api.Database;

namespace Nora.Api.Services;

/// <summary>Nomes dos clientes HTTP externos (IHttpClientFactory); os testes trocam o handler por um falso.</summary>
public static class NoraHttpClients
{
    public const string Google = "nora-google";
    public const string Push = "nora-push";
}

/// <summary>Resultado de um passe do push: avisos enfileirados, enviados, que voltam a tentar, abandonados e aparelhos removidos.</summary>
public sealed record PushRunResult(int Enqueued, int Sent, int Retried, int Abandoned, int RemovedDevices);

/// <summary>
/// Push pelo servidor (P-042). Cada passe (1) procura os eventos e grava na fila de saída um aviso por aparelho, com a marca de "já avisado" (chave única evento × aparelho),
/// e (2) envia os avisos vencidos pelo FCM. Eventos (decisão do usuário de 2026-11-16): oferta de Vaga Relâmpago pendente para o paciente ou para um dependente (aos
/// responsáveis com vínculo aprovado), comunicado novo com alcance de pacientes, janela de confirmação de presença aberta (consulta <c>scheduled</c> sem confirmação, de 48 h
/// até 2 h antes) e pedido de família aprovado ou recusado (ao responsável). Falha transitória tenta de novo com espera crescente (30 s, 1 min, 2 min… até 30 min) até
/// <c>Nora:Push:MaxAttempts</c>; <c>UNREGISTERED</c>/404 remove o aparelho. Sem configuração, o passe não faz nada.
/// </summary>
public sealed class PushService(PushOptions options, PushDao dao, PushSender sender, DatabaseHelper central, UnitDatabaseRouter databases, HealthUnitService units, TimeProvider time, ILogger<PushService> log)
{
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan Retention = TimeSpan.FromDays(30);
    private static readonly TimeSpan ConfirmationOpens = TimeSpan.FromHours(48);
    private static readonly TimeSpan ConfirmationCloses = TimeSpan.FromHours(2);
    private const int BatchSize = 100;
    private const int MaxBatchesPerPass = 20;
    private const int Parallelism = 8;
    private static readonly PushRunResult Nothing = new(0, 0, 0, 0, 0);
    private readonly SemaphoreSlim pass = new(1, 1);
    private DateTimeOffset? lastPresenceScan;

    public async Task<PushRunResult> RunOnceAsync(CancellationToken ct)
    {
        if (!options.Configured || !central.IsConfigured) return Nothing;
        await pass.WaitAsync(ct);
        try
        {
            var now = time.GetUtcNow();
            var enqueued = await DiscoverAsync(now, ct);
            var (sent, retried, abandoned, removed) = await DeliverAsync(now, ct);
            await dao.CleanupAsync(now - Retention, 500, ct);
            return new(enqueued, sent, retried, abandoned, removed);
        }
        finally { pass.Release(); }
    }

    private async Task<int> DiscoverAsync(DateTimeOffset now, CancellationToken ct)
    {
        // O cursor dos comunicados anda mesmo sem aparelho registrado (um comunicado antigo não vira aviso para o primeiro aparelho que aparecer).
        var enqueued = await dao.EnqueueAnnouncementsAsync(now, now - options.Lookback, ct);
        // Sem nenhum aparelho registrado não há a quem avisar: nem visita as UBS.
        if (!await dao.AnyDeviceAsync(ct)) return enqueued;
        enqueued += await dao.EnqueueFamilyDecisionsAsync(now - options.Lookback, now, ct);
        // A janela de presença muda devagar: a varredura dela tem intervalo próprio (PresenceScanMinutes); as ofertas duram 10 minutos e são vistas a cada passe.
        var scanPresence = lastPresenceScan is not { } last || now < last || now - last >= options.PresenceScanEvery;
        foreach (var unit in await units.ListReadyAsync(ct))
        {
            try
            {
                var (offers, appointments) = await ReadUnitEventsAsync(unit.Id, now, scanPresence, ct);
                enqueued += await dao.EnqueueUnitEventsAsync(unit.Id, "lightning-offer", offers, false, now, ct);
                enqueued += await dao.EnqueueUnitEventsAsync(unit.Id, "presence-window", appointments, true, now, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            // Schema desatualizado já foi avisado na subida (UnitSchemaCheckService).
            catch (ApiCodedException outdated) when (outdated.Code == "unit-database-outdated") { }
            catch (Exception) { log.LogWarning("Não foi possível procurar avisos de push numa unidade; ela é visitada de novo no próximo passe."); }
        }
        if (scanPresence) lastPresenceScan = now;
        return enqueued;
    }

    /// <summary>Uma conexão por UBS: ofertas pendentes ainda no prazo e, quando é a vez, consultas cuja confirmação de presença está aberta (cadastro ativo na UBS).</summary>
    private async Task<(List<(Guid, string)> Offers, List<(Guid, string)> Appointments)> ReadUnitEventsAsync(Guid unitId, DateTimeOffset now, bool scanPresence, CancellationToken ct)
    {
        var offers = new List<(Guid, string)>(); var appointments = new List<(Guid, string)>();
        await using var connection = await databases.OpenAsync(unitId, ct);
        await using (var query = new NpgsqlCommand("SELECT o.id,q.cpf FROM nora_unit.lightning_offers o JOIN nora_unit.waiting_list q ON q.id=o.entry_id WHERE o.status='pending' AND o.expires_at>$1", connection))
        {
            query.Parameters.AddWithValue(now);
            await using var reader = await query.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct)) offers.Add((reader.GetGuid(0), reader.GetString(1)));
        }
        if (!scanPresence) return (offers, appointments);
        await using (var query = new NpgsqlCommand("SELECT a.id,a.cpf FROM nora_unit.appointments a WHERE a.status='scheduled' AND a.confirmed_at IS NULL AND a.starts_at>=$1 AND a.starts_at<=$2 " +
            "AND EXISTS(SELECT 1 FROM nora_unit.patient_registry r WHERE r.cpf=a.cpf AND r.is_active)", connection))
        {
            query.Parameters.AddWithValue(now + ConfirmationCloses); query.Parameters.AddWithValue(now + ConfirmationOpens);
            await using var reader = await query.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct)) appointments.Add((reader.GetGuid(0), reader.GetString(1)));
        }
        return (offers, appointments);
    }

    private async Task<(int Sent, int Retried, int Abandoned, int Removed)> DeliverAsync(DateTimeOffset now, CancellationToken ct)
    {
        int sent = 0, retried = 0, abandoned = 0, removed = 0;
        for (var batch = 0; batch < MaxBatchesPerPass; batch++)
        {
            var claimed = await dao.ClaimDueAsync(now, Lease, BatchSize, ct);
            if (claimed.Count == 0) break;
            var outcomes = new PushOutcome[claimed.Count];
            await Parallel.ForEachAsync(Enumerable.Range(0, claimed.Count), new ParallelOptions { MaxDegreeOfParallelism = Parallelism, CancellationToken = ct }, async (index, token) =>
            {
                var item = claimed[index];
                outcomes[index] = await sender.SendAsync(item.Token, new(item.Type, item.UnitId, item.DependentId, item.AppointmentId), token);
            });
            var done = new List<long>(); var again = new List<(long, DateTimeOffset)>(); var dropped = new List<long>(); var devices = new List<Guid>();
            for (var i = 0; i < claimed.Count; i++)
            {
                var item = claimed[i];
                switch (outcomes[i])
                {
                    case PushOutcome.Sent: done.Add(item.Id); break;
                    case PushOutcome.Unregistered: devices.Add(item.DeviceId); break;
                    case PushOutcome.Transient when item.Attempts < options.MaxAttempts: again.Add((item.Id, now + Backoff(item.Attempts))); break;
                    default: dropped.Add(item.Id); break;
                }
            }
            await dao.CompleteAsync(done, again, dropped, devices.Distinct().ToList(), now, ct);
            sent += done.Count; retried += again.Count; abandoned += dropped.Count; removed += devices.Distinct().Count();
            if (claimed.Count < BatchSize) break;
        }
        return (sent, retried, abandoned, removed);
    }

    /// <summary>Espera antes da próxima tentativa: 30 s depois da primeira falha, dobrando a cada falha, no máximo 30 minutos.</summary>
    internal static TimeSpan Backoff(int attempts) => TimeSpan.FromSeconds(Math.Min(30 * Math.Pow(2, Math.Max(attempts - 1, 0)), 1800));
}

/// <summary>
/// Chama <see cref="PushService"/> de tempos em tempos: primeiro passe 30 s depois da subida e depois a cada <c>Nora:Push:IntervalSeconds</c> (padrão 30). Não faz nada sem
/// configuração do push ou com <c>Nora:Push:Enabled=false</c>. Uma falha é registrada sem dados e o passe seguinte tenta de novo.
/// </summary>
public sealed class PushWorker(PushService push, PushOptions options, ILogger<PushWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.WorkerEnabled || !options.Configured) return;
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            using var timer = new PeriodicTimer(options.Interval);
            do
            {
                try
                {
                    var result = await push.RunOnceAsync(stoppingToken);
                    if (result.Sent + result.Abandoned + result.RemovedDevices > 0)
                        log.LogInformation("Push: {Sent} enviados, {Retried} para nova tentativa, {Abandoned} abandonados, {Removed} aparelhos removidos.", result.Sent, result.Retried, result.Abandoned, result.RemovedDevices);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { throw; }
                catch (Exception) { log.LogWarning("Não foi possível executar o passe do push; a próxima tentativa acontece no próximo intervalo."); }
            } while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
