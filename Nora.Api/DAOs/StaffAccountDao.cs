using Nora.Api.Database;
using Nora.Api.Models;
using Nora.Api.Services;

namespace Nora.Api.DAOs;

/// <summary>Contas OBS mantidas pela Secretaria. Cada ação grava staff_account_events na mesma transação.</summary>
public sealed class StaffAccountDao(DatabaseHelper database, TimeProvider time)
{
    /// <summary>Duplicidade de e-mail no escopo obs vira PostgresException 23505, mapeada para 409.</summary>
    public async Task<StaffAccount> CreateAsync(Guid actorId, string role, StaffAccountDraft request, string passwordHash, CancellationToken ct)
    {
        await using var connection = await database.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        var id = Guid.NewGuid();
        await database.ExecuteAsync(connection, "INSERT INTO nora.accounts(id,full_name,email,password_hash,scope,role,must_change_password) VALUES($1,$2,$3,$4,'obs',$5,true)", ct, id, request.FullName, request.Email, passwordHash, role);
        await RecordAsync(connection, id, actorId, "created", ct);
        await transaction.CommitAsync(ct);
        return new(id, request.FullName, role, request.Email, true, true);
    }

    /// <summary>Altera a situação; desativar revoga as sessões. As concessões de UBS ficam intactas. Sem mudança de estado não grava evento.</summary>
    public async Task SetActiveAsync(Guid actorId, Guid id, bool active, CancellationToken ct)
    {
        await using var connection = await database.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        bool current;
        await using (var select = database.Command(connection, "SELECT is_active FROM nora.accounts WHERE id=$1 AND scope='obs' FOR UPDATE", id))
            current = await select.ExecuteScalarAsync(ct) is bool value ? value : throw new ApiNotFoundException();
        if (current != active)
        {
            await database.ExecuteAsync(connection, "UPDATE nora.accounts SET is_active=$2 WHERE id=$1", ct, id, active);
            if (!active) await database.ExecuteAsync(connection, "UPDATE nora.sessions SET revoked_at=$2,presence_seen_at=NULL WHERE account_id=$1 AND revoked_at IS NULL", ct, id, time.GetUtcNow());
            await RecordAsync(connection, id, actorId, active ? "reactivated" : "deactivated", ct);
        }
        await transaction.CommitAsync(ct);
    }

    /// <summary>Define uma senha provisória (troca obrigatória no próximo login) e revoga todas as sessões da conta.</summary>
    public async Task ResetPasswordAsync(Guid actorId, Guid id, string passwordHash, CancellationToken ct)
    {
        await using var connection = await database.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        if (await database.ExecuteAsync(connection, "UPDATE nora.accounts SET password_hash=$2,must_change_password=true WHERE id=$1 AND scope='obs'", ct, id, passwordHash) != 1) throw new ApiNotFoundException();
        await database.ExecuteAsync(connection, "UPDATE nora.sessions SET revoked_at=$2,presence_seen_at=NULL WHERE account_id=$1 AND revoked_at IS NULL", ct, id, time.GetUtcNow());
        await RecordAsync(connection, id, actorId, "password_reset", ct);
        await transaction.CommitAsync(ct);
    }

    private Task RecordAsync(Npgsql.NpgsqlConnection connection, Guid accountId, Guid actorId, string action, CancellationToken ct) =>
        database.ExecuteAsync(connection, "INSERT INTO nora.staff_account_events(account_id,actor_id,action) VALUES($1,$2,$3)", ct, accountId, actorId, action);
}
