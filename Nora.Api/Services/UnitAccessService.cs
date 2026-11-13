using Npgsql;
using Nora.Api.Database;
using Nora.Api.DAOs;
using Nora.Api.Models;

namespace Nora.Api.Services;

/// <summary>
/// Hold the unit grant while the unit operation runs, so revocation waits for it to finish. Todo executor repete, dentro da transação, a
/// revalidação de conta e sessão (<see cref="RevalidateAsync"/>): a autenticação do filtro acontece antes, e uma desativação, redefinição de
/// senha ou revogação de sessão no meio do caminho não pode deixar a operação terminar depois.
/// </summary>
public sealed class UnitAccessService(DatabaseHelper central, TimeProvider time, UnitPatientRegistryDao registry)
{
    /// <summary>
    /// Confere sob bloqueio compartilhado (<c>FOR SHARE</c> em conta e sessão) que a conta está ativa, a sessão vale e não há troca de senha
    /// pendente. Quem desativa a conta, redefine a senha ou revoga a sessão precisa do bloqueio exclusivo e, por isso, espera a operação
    /// terminar. Devolve o CPF da conta. Conta/sessão inválidas → 401; troca obrigatória pendente → 403 password-change-required.
    /// </summary>
    private async Task<string> RevalidateAsync(NpgsqlConnection connection, SessionInfo session, CancellationToken ct)
    {
        await using var query = central.Command(connection, "SELECT a.cpf,a.must_change_password FROM nora.accounts a JOIN nora.sessions s ON s.account_id=a.id WHERE a.id=$1 AND a.is_active AND s.token_hash=$2 AND s.revoked_at IS NULL AND s.expires_at>$3 FOR SHARE OF a,s", session.User.Id, session.TokenHash, time.GetUtcNow());
        await using var reader = await query.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) throw new ApiUnauthorizedException();
        if (reader.GetBoolean(1)) throw new ApiCodedException(StatusCodes.Status403Forbidden, "password-change-required");
        return reader.GetString(0);
    }

    public async Task<T> ExecutePatientAsync<T>(SessionInfo session, Guid unitId, Func<string, Task<T>> action, CancellationToken ct)
    {
        if (unitId == Guid.Empty || session.User.Scope != "patient" || session.User.Role != "patient") throw new ApiForbiddenException();
        await using var connection = await central.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        var cpf = await RevalidateAsync(connection, session, ct);
        await using (var link = central.Command(connection, "SELECT 1 FROM nora.patient_units WHERE patient_id=$1 AND unit_id=$2 FOR UPDATE", session.User.Id, unitId))
            if (await link.ExecuteScalarAsync(ct) is null) throw new ApiForbiddenException();
        if (!await registry.IsActiveAsync(unitId, cpf, ct)) throw new ApiForbiddenException();
        var result = await action(cpf);
        await transaction.CommitAsync(ct);
        return result;
    }
    /// <summary>
    /// Remoção voluntária do vínculo: segura o vínculo, roda a ação na UBS e remove a linha de patient_units (evento 'unlinked') na mesma
    /// transação central. Não exige cadastro ativo na UBS, para que o paciente consiga sair mesmo com o cadastro desativado.
    /// Falha da UBS desfaz a remoção do vínculo; repetir é seguro, pois a ação só cancela o que ainda estiver ativo.
    /// </summary>
    public async Task<T> ExecutePatientUnlinkAsync<T>(SessionInfo session, Guid unitId, Func<string, Task<T>> action, CancellationToken ct)
    {
        if (unitId == Guid.Empty || session.User.Scope != "patient" || session.User.Role != "patient") throw new ApiForbiddenException();
        await using var connection = await central.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        var cpf = await RevalidateAsync(connection, session, ct);
        await central.ExecuteAsync(connection, "SELECT pg_advisory_xact_lock(hashtextextended($1,62419805))", ct, cpf);
        await using (var link = central.Command(connection, "SELECT 1 FROM nora.patient_units WHERE patient_id=$1 AND unit_id=$2 FOR UPDATE", session.User.Id, unitId))
            if (await link.ExecuteScalarAsync(ct) is null) throw new ApiForbiddenException();
        var result = await action(cpf);
        await central.ExecuteAsync(connection, "DELETE FROM nora.patient_units WHERE patient_id=$1 AND unit_id=$2", ct, session.User.Id, unitId);
        await central.ExecuteAsync(connection, "INSERT INTO nora.patient_unit_link_events(patient_id,unit_id,action) VALUES($1,$2,'unlinked')", ct, session.User.Id, unitId);
        await transaction.CommitAsync(ct);
        return result;
    }

    public async Task<T> ExecuteManagementReadAsync<T>(SessionInfo session, Guid unitId, Func<Task<T>> action, CancellationToken ct, string eventType = "registry_read")
    {
        if (unitId == Guid.Empty || session.User.Scope != "municipal" || session.User.Role != "manager" || eventType is not ("registry_read" or "appointment_read")) throw new ApiForbiddenException();
        await using var connection = await central.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await RevalidateAsync(connection, session, ct);
        await using (var unit = central.Command(connection, "SELECT id FROM nora.health_units WHERE id=$1 FOR KEY SHARE", unitId))
            if (await unit.ExecuteScalarAsync(ct) is null) throw new ApiNotFoundException();
        var result = await action();
        await central.ExecuteAsync(connection, "INSERT INTO nora.unit_access_events(actor_id,unit_id,action) VALUES($1,$2,$3)", ct, session.User.Id, unitId, eventType);
        await transaction.CommitAsync(ct);
        return result;
    }

    public async Task<T> ExecuteAsync<T>(SessionInfo session, Guid unitId, Func<Task<T>> action, CancellationToken ct)
    {
        if (unitId == Guid.Empty || session.User.Scope != "obs" || session.User.Role is not ("administrator" or "attendant")) throw new ApiForbiddenException();
        await using var connection = await central.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await RevalidateAsync(connection, session, ct);
        await using (var grant = central.Command(connection, "SELECT 1 FROM nora.staff_units WHERE account_id=$1 AND unit_id=$2 FOR UPDATE", session.User.Id, unitId))
            if (await grant.ExecuteScalarAsync(ct) is null) throw new ApiForbiddenException();
        var result = await action();
        await transaction.CommitAsync(ct);
        return result;
    }
}
