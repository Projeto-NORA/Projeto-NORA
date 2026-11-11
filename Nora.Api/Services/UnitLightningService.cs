using Nora.Api.DAOs;

namespace Nora.Api.Services;

public sealed class UnitLightningService(UnitLightningDispatcher dispatcher, ILogger<UnitLightningService> log)
{
    public async Task TryAdvanceAsync(Guid unitId, CancellationToken ct)
    {
        try { await dispatcher.AdvanceAsync(unitId, ct); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception) { log.LogWarning("Não foi possível atualizar imediatamente as ofertas de uma unidade; a verificação periódica tentará novamente."); }
    }
}
