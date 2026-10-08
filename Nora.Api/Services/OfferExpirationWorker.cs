using Nora.Api.DAOs;
using Nora.Api.Database;
namespace Nora.Api.Services;
public sealed class OfferExpirationWorker(IServiceProvider services,ILogger<OfferExpirationWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer=new PeriodicTimer(TimeSpan.FromSeconds(15));
        try
        {
            while(await timer.WaitForNextTickAsync(stoppingToken))
            {
                if(!services.GetRequiredService<DatabaseHelper>().IsConfigured) continue;
                try { await services.GetRequiredService<WorkflowDao>().AdvanceAsync(stoppingToken); }
                catch(OperationCanceledException) when(stoppingToken.IsCancellationRequested) { break; }
                catch(Exception) { log.LogWarning("A verificação de expiração não pôde acessar o banco NORA."); }
            }
        }
        catch(OperationCanceledException) when(stoppingToken.IsCancellationRequested) { }
    }
}
