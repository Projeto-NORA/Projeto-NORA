using Npgsql;
using Nora.Api.Database;
using Nora.Api.Models;

namespace Nora.Api.Services;

/// <summary>
/// Chave da aprovação das vagas reaproveitadas, por UBS (só o administrador). Ligada, todo cancelamento de consulta futura feito depois do instante em que foi ligada gera uma vaga que aguarda a aprovação
/// da equipe (ver <see cref="DAOs.UnitLightningDispatcher"/>); desligar não apaga nada: as que já aguardam continuam aprováveis até o prazo.
/// </summary>
public sealed class SlotApprovalService(DatabaseHelper central, TimeProvider time)
{
    /// <summary>Liga (mantendo o instante se já estava ligada: idempotente) ou desliga (zera). Sem <c>confirmed</c> → 400. Roda na transação do executor do administrador.</summary>
    public async Task<SlotApprovalState> SetAsync(NpgsqlConnection connection, SessionInfo session, Guid unitId, SlotApprovalRequest request, CancellationToken ct)
    {
        if (!request.Confirmed) throw new ApiInputException();
        DateTimeOffset? since;
        await using (var update = central.Command(connection, "UPDATE nora.health_units SET slot_approval_since=CASE WHEN $2 THEN COALESCE(slot_approval_since,$3) ELSE NULL END,slot_approval_updated_by=$4 WHERE id=$1 RETURNING slot_approval_since", unitId, request.Enabled, time.GetUtcNow(), session.User.Id))
        await using (var reader = await update.ExecuteReaderAsync(ct))
        {
            if (!await reader.ReadAsync(ct)) throw new ApiNotFoundException();
            since = reader.IsDBNull(0) ? null : reader.GetFieldValue<DateTimeOffset>(0);
        }
        return new(since is not null, since, session.User.FullName);
    }
}
