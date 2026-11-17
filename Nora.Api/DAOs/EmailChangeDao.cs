using Npgsql;
using Nora.Api.Database;
using Nora.Api.Models;

namespace Nora.Api.DAOs;

/// <summary>Estado de uma sugestão pendente de troca do e-mail da conta, com o que a verificação do código precisa.</summary>
internal sealed record PendingEmailSuggestion(Guid Id, string SuggestedEmail, DateTimeOffset CreatedAt, string? CodeHash, DateTimeOffset? CodeExpiresAt, short Sends, short Attempts);

/// <summary>SQL das sugestões de troca de e-mail da conta (banco central). Uma sugestão pendente por conta; as demais ficam como histórico.</summary>
public sealed class EmailChangeDao(DatabaseHelper database)
{
    /// <summary>
    /// Registra, na transação central do chamador (que segura o lock do CPF), a sugestão de trocar o e-mail da conta de paciente do CPF para <paramref name="newEmail"/>.
    /// Só vale se a conta existe, está ativa e tem vínculo com a UBS que corrigiu o e-mail (uma UBS sem vínculo não faz aparecer pedido no app do paciente). A sugestão
    /// pendente anterior é substituída; se o e-mail da conta já é o novo, só a pendente antiga é encerrada.
    /// </summary>
    /// <param name="sourceCreatedAt">Instante da correção na UBS (a linha da caixa de saída). A sugestão nasce com ele, e se a conta já tem sugestão mais nova (qualquer situação, de qualquer UBS) esta é antiga demais: nada é feito, para a passada periódica não trocar a sugestão mais nova por uma que sobrou de uma correção anterior.</param>
    /// <param name="sourceKey">Id da linha da caixa de saída da UBS que originou a sugestão: se já existe sugestão com esta origem (em qualquer situação, inclusive dispensada) nada é feito, então reprocessar a caixa de saída nunca recria nem duplica.</param>
    internal async Task RecordAsync(NpgsqlConnection connection, Guid unitId, string cpf, string newEmail, DateTimeOffset now, CancellationToken ct, Guid? sourceKey = null, DateTimeOffset? sourceCreatedAt = null)
    {
        if (sourceKey is { } source)
            await using (var exists = database.Command(connection, "SELECT 1 FROM nora.email_change_suggestions WHERE source_key=$1", source))
                if (await exists.ExecuteScalarAsync(ct) is not null) return;
        Guid? accountId = null; string? current = null;
        await using (var find = database.Command(connection, "SELECT a.id,a.email FROM nora.accounts a JOIN nora.patient_units p ON p.patient_id=a.id AND p.unit_id=$2 WHERE a.scope='patient' AND a.is_active AND a.cpf=$1", cpf, unitId))
        await using (var reader = await find.ExecuteReaderAsync(ct))
            if (await reader.ReadAsync(ct)) { accountId = reader.GetGuid(0); current = reader.GetString(1); }
        if (accountId is not { } account) return;
        var createdAt = sourceCreatedAt ?? now;
        if (sourceCreatedAt is not null)
            await using (var newer = database.Command(connection, "SELECT 1 FROM nora.email_change_suggestions WHERE account_id=$1 AND created_at>$2", account, createdAt))
                if (await newer.ExecuteScalarAsync(ct) is not null) return;
        await database.ExecuteAsync(connection, "UPDATE nora.email_change_suggestions SET status='superseded',closed_at=$2,code_hash=NULL,code_expires_at=NULL WHERE account_id=$1 AND status='pending'", ct, account, now);
        if (string.Equals(current, newEmail, StringComparison.OrdinalIgnoreCase)) return;
        await database.ExecuteAsync(connection, "INSERT INTO nora.email_change_suggestions(id,account_id,unit_id,suggested_email,status,created_at,source_key) VALUES($1,$2,$3,$4,'pending',$5,$6::uuid)", ct, Guid.NewGuid(), account, unitId, newEmail, createdAt, (object?)sourceKey ?? DBNull.Value);
    }

    /// <summary>A sugestão pendente da conta com o nome da UBS, para a leitura do app; nula quando não há.</summary>
    public async Task<(Guid Id, string Email, Guid UnitId, string UnitName, DateTimeOffset CreatedAt)?> ReadAsync(Guid accountId, CancellationToken ct)
    {
        await using var connection = await database.OpenAsync(ct);
        await using var query = database.Command(connection, "SELECT s.id,s.suggested_email,s.unit_id,u.name,s.created_at FROM nora.email_change_suggestions s JOIN nora.health_units u ON u.id=s.unit_id WHERE s.account_id=$1 AND s.status='pending'", accountId);
        await using var reader = await query.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? (reader.GetGuid(0), reader.GetString(1), reader.GetGuid(2), reader.GetString(3), reader.GetFieldValue<DateTimeOffset>(4)) : null;
    }

    /// <summary>Sugestão pendente da conta, bloqueada para escrita na transação do chamador.</summary>
    internal async Task<PendingEmailSuggestion?> LockPendingAsync(NpgsqlConnection connection, Guid accountId, CancellationToken ct)
    {
        await using var query = database.Command(connection, "SELECT id,suggested_email,created_at,code_hash,code_expires_at,sends,attempts FROM nora.email_change_suggestions WHERE account_id=$1 AND status='pending' FOR UPDATE", accountId);
        await using var reader = await query.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? new(reader.GetGuid(0), reader.GetString(1), reader.GetFieldValue<DateTimeOffset>(2), reader.IsDBNull(3) ? null : reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetFieldValue<DateTimeOffset>(4), reader.GetInt16(5), reader.GetInt16(6)) : null;
    }

    internal Task<int> StoreCodeAsync(NpgsqlConnection connection, Guid id, string codeHash, DateTimeOffset expiresAt, CancellationToken ct) =>
        database.ExecuteAsync(connection, "UPDATE nora.email_change_suggestions SET code_hash=$2,code_expires_at=$3,sends=sends+1,attempts=0 WHERE id=$1 AND status='pending'", ct, id, codeHash, expiresAt);

    internal Task<int> CountAttemptAsync(NpgsqlConnection connection, Guid id, CancellationToken ct) =>
        database.ExecuteAsync(connection, "UPDATE nora.email_change_suggestions SET attempts=LEAST(attempts+1,5) WHERE id=$1", ct, id);

    internal Task<int> CloseAsync(NpgsqlConnection connection, Guid id, string status, DateTimeOffset now, CancellationToken ct) =>
        database.ExecuteAsync(connection, "UPDATE nora.email_change_suggestions SET status=$2,closed_at=$3,code_hash=NULL,code_expires_at=NULL WHERE id=$1 AND status='pending'", ct, id, status, now);

    /// <summary>Invalida o código enviado (envio que falhou), sem apagar a sugestão. Usa conexão própria: o chamador não está mais em transação.</summary>
    internal async Task InvalidateCodeAsync(Guid id, string codeHash)
    {
        await using var connection = await database.OpenAsync(CancellationToken.None);
        await database.ExecuteAsync(connection, "UPDATE nora.email_change_suggestions SET code_hash=NULL,code_expires_at=NULL WHERE id=$1 AND code_hash=$2", CancellationToken.None, id, codeHash);
    }

    /// <summary>Troca o e-mail da conta. A unicidade (escopo, e-mail) é do banco: conflito vira 409 pelo tratador de erros.</summary>
    internal Task<int> ChangeAccountEmailAsync(NpgsqlConnection connection, Guid accountId, string email, CancellationToken ct) =>
        database.ExecuteAsync(connection, "UPDATE nora.accounts SET email=$2 WHERE id=$1 AND scope='patient' AND is_active", ct, accountId, email);

    /// <summary>Consome os códigos de recuperação de senha ainda abertos da conta, na mesma transação da troca de e-mail: um código enviado ao endereço antigo não pode mais redefinir a senha.</summary>
    internal Task<int> ConsumeResetCodesAsync(NpgsqlConnection connection, Guid accountId, DateTimeOffset now, CancellationToken ct) =>
        database.ExecuteAsync(connection, "UPDATE nora.patient_password_reset_codes SET consumed_at=$2 WHERE account_id=$1 AND consumed_at IS NULL", ct, accountId, now);

    /// <summary>
    /// Conta e sessão revalidadas com a conta bloqueada para escrita (a troca de e-mail altera a linha da conta): ativa, sessão válida. Devolve falso se a sessão ou a conta já não valem.
    /// </summary>
    internal async Task<bool> LockAccountAsync(NpgsqlConnection connection, Guid accountId, string tokenHash, DateTimeOffset now, CancellationToken ct)
    {
        await using var query = database.Command(connection, "SELECT 1 FROM nora.accounts a JOIN nora.sessions s ON s.account_id=a.id WHERE a.id=$1 AND a.is_active AND s.token_hash=$2 AND s.revoked_at IS NULL AND s.expires_at>$3 FOR UPDATE OF a FOR SHARE OF s", accountId, tokenHash, now);
        return await query.ExecuteScalarAsync(ct) is not null;
    }
}
