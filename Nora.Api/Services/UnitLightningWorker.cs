using Nora.Api.DAOs;

namespace Nora.Api.Services;

public sealed class UnitLightningWorker(HealthUnitService units, UnitLightningDispatcher dispatcher, IConfiguration config, ILogger<UnitLightningWorker> log) : BackgroundService
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
                    foreach (var unit in await units.ListReadyAsync(stoppingToken))
                    {
                        try { await dispatcher.AdvanceAsync(unit.Id, stoppingToken); }
                        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { throw; }
                        // Schema desatualizado já foi avisado na subida (UnitSchemaCheckService); não repete o aviso a cada 15 s.
                        catch (ApiCodedException outdated) when (outdated.Code == "unit-database-outdated") { }
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
