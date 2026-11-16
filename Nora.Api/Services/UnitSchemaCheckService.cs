using Nora.Api.DAOs;
using Nora.Api.Database;

namespace Nora.Api.Services;

/// <summary>
/// Ao subir a API (e só nela: os comandos <c>--migrate*</c> não iniciam serviços), confere o schema do banco de cada UBS "ready" e avisa no log
/// as desatualizadas. Roda em segundo plano e nunca impede a subida: a garantia está em <see cref="UnitDatabaseRouter.OpenAsync"/>, que recusa
/// a UBS desatualizada com 503 <c>unit-database-outdated</c>. O aviso traz só o identificador da UBS, sem connection string nem dado pessoal.
/// </summary>
public sealed class UnitSchemaCheckService(DatabaseHelper central, HealthUnitDao units, UnitDatabaseRouter router, ILogger<UnitSchemaCheckService> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!central.IsConfigured) return;
        try
        {
            foreach (var unit in (await units.ListAsync(stoppingToken)).Where(unit => unit.DatabaseState == "ready"))
            {
                try { await using var connection = await router.OpenAsync(unit.Id, stoppingToken); }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { throw; }
                catch (ApiCodedException outdated) when (outdated.Code == "unit-database-outdated")
                {
                    log.LogWarning("O banco da UBS {UnitId} está com o schema desatualizado; execute Nora.Api --migrate-all-units. Até lá as rotas dessa UBS respondem 503 unit-database-outdated.", unit.Id);
                }
                catch (Exception) { log.LogWarning("Não foi possível conferir o schema do banco da UBS {UnitId}.", unit.Id); }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        catch (Exception) { log.LogWarning("Não foi possível listar as UBS para conferir o schema dos bancos."); }
    }
}
