using Nora.Api.DAOs;
using Nora.Api.Database;

namespace Nora.Api.Services;

public sealed class UnitLightningWorker(HealthUnitService units, UnitDatabaseRouter databases, UnitLightningDispatcher dispatcher, IConfiguration config, ILogger<UnitLightningWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (config.GetValue("Nora:UnitLightningWorker:Enabled", true) == false) return;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    foreach (var unit in await units.ListAsync(stoppingToken))
                    {
                        if (!databases.IsConfigured(unit.Id)) continue;
                        try { await dispatcher.AdvanceAsync(unit.Id, stoppingToken); }
                        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { throw; }
                        catch (Exception) { log.LogWarning("Não foi possível atualizar as ofertas de uma unidade."); }
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { throw; }
                catch (Exception) { log.LogWarning("Não foi possível obter unidades para atualizar as ofertas."); }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
