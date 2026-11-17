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
    internal async Task<string> RevalidateAsync(NpgsqlConnection connection, SessionInfo session, CancellationToken ct)
    {
        await using var query = central.Command(connection, "SELECT a.cpf,a.must_change_password FROM nora.accounts a JOIN nora.sessions s ON s.account_id=a.id WHERE a.id=$1 AND a.is_active AND s.token_hash=$2 AND s.revoked_at IS NULL AND s.expires_at>$3 FOR SHARE OF a,s", session.User.Id, session.TokenHash, time.GetUtcNow());
        await using var reader = await query.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) throw new ApiUnauthorizedException();
        if (reader.GetBoolean(1)) throw new ApiCodedException(StatusCodes.Status403Forbidden, "password-change-required");
        return reader.GetString(0);
    }

    /// <param name="requireActiveRegistry">Exige cadastro ativo do CPF na UBS (padrão). A prévia da remoção do vínculo passa falso, como a própria remoção, que funciona com o cadastro desativado.</param>
    /// <param name="read">Rota de leitura: segura o vínculo só em <c>FOR SHARE</c> (as leituras do mesmo paciente não se enfileiram), e sair da UBS (<c>FOR UPDATE</c> + <c>DELETE</c>) continua esperando a leitura terminar. Escritas usam <c>FOR UPDATE</c>.</param>
    public async Task<T> ExecutePatientAsync<T>(SessionInfo session, Guid unitId, Func<string, Task<T>> action, CancellationToken ct, bool requireActiveRegistry = true, bool read = false)
    {
        if (unitId == Guid.Empty || session.User.Scope != "patient" || session.User.Role != "patient") throw new ApiForbiddenException();
        await using var connection = await central.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        var cpf = await RevalidateAsync(connection, session, ct);
        await using (var link = central.Command(connection, read ? "SELECT 1 FROM nora.patient_units WHERE patient_id=$1 AND unit_id=$2 FOR SHARE" : "SELECT 1 FROM nora.patient_units WHERE patient_id=$1 AND unit_id=$2 FOR UPDATE", session.User.Id, unitId))
            if (await link.ExecuteScalarAsync(ct) is null) throw new ApiForbiddenException();
        if (requireActiveRegistry && !await registry.IsActiveAsync(unitId, cpf, ct)) throw new ApiForbiddenException();
        var result = await action(cpf);
        await transaction.CommitAsync(ct);
        return result;
    }
    /// <summary>
    /// Remoção voluntária do vínculo: segura o vínculo, remove a linha de patient_units (evento 'unlinked') na transação central ainda
    /// aberta, roda a ação na UBS (que confirma a própria transação) e só então confirma a central. Não exige cadastro ativo na UBS, para
    /// que o paciente consiga sair mesmo com o cadastro desativado.
    /// Duas transações de bancos diferentes não têm commit atômico. A ordem acima reduz a janela de inconsistência a uma única falha: o commit
    /// central depois do commit da UBS. Falha da UBS ou de qualquer instrução central antes disso desfaz tudo, e nada fica cancelado. Se o
    /// commit central falhar depois de a UBS confirmar, as consultas e entradas ficam canceladas e o vínculo continua; repetir a chamada é
    /// seguro e conclui, pois a ação só cancela o que ainda estiver ativo (a contagem da repetição refere-se apenas ao que restava ativo).
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
        await central.ExecuteAsync(connection, "DELETE FROM nora.patient_units WHERE patient_id=$1 AND unit_id=$2", ct, session.User.Id, unitId);
        await central.ExecuteAsync(connection, "INSERT INTO nora.patient_unit_link_events(patient_id,unit_id,action) VALUES($1,$2,'unlinked')", ct, session.User.Id, unitId);
        var result = await action(cpf);
        await transaction.CommitAsync(ct);
        return result;
    }

    /// <summary>
    /// Executor do responsável (Família): a conta do responsável age em nome de um dependente numa UBS. Na transação central repete a revalidação de conta e sessão do responsável e
    /// segura o vínculo (<c>FOR SHARE</c>, de modo que encerrar o vínculo espera as operações em andamento) que precisa estar aprovado, ser do responsável e da UBS da URL; depois confere
    /// o cadastro ativo do dependente na UBS e executa <paramref name="action"/> com o CPF do dependente, a mesma lógica das rotas do paciente. Sem vínculo aprovado, de outro responsável,
    /// encerrado, de outra UBS ou com o cadastro do dependente inativo → 403 (nunca revela qual). O CPF do dependente nunca vem do cliente.
    /// </summary>
    /// <param name="serializeCpf">Segura o lock de CPF <c>62419805</c> (o mesmo da conta do próprio dependente e da remoção de vínculo) para serializar o que depende do conjunto de consultas do CPF na rede (agendamento).</param>
    public Task<T> ExecuteGuardianAsync<T>(SessionInfo session, Guid dependentId, Guid unitId, Func<string, Task<T>> action, CancellationToken ct, bool serializeCpf = false) =>
        ExecuteGuardianCoreAsync(session, dependentId, unitId, (_, cpf) => action(cpf), ct, serializeCpf);

    /// <summary>Corpo do executor do responsável: a ação também recebe a conexão central da transação (usada para montar o <see cref="CpfLock"/> quando <paramref name="serializeCpf"/>).</summary>
    internal async Task<T> ExecuteGuardianCoreAsync<T>(SessionInfo session, Guid dependentId, Guid unitId, Func<NpgsqlConnection, string, Task<T>> action, CancellationToken ct, bool serializeCpf)
    {
        if (dependentId == Guid.Empty || unitId == Guid.Empty || session.User.Scope != "patient" || session.User.Role != "patient") throw new ApiForbiddenException();
        await using var connection = await central.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await RevalidateAsync(connection, session, ct);
        string dependentCpf;
        await using (var link = central.Command(connection, "SELECT dependent_cpf FROM nora.family_links WHERE id=$1 AND guardian_id=$2 AND unit_id=$3 AND status='approved' FOR SHARE", dependentId, session.User.Id, unitId))
            dependentCpf = await link.ExecuteScalarAsync(ct) as string ?? throw new ApiForbiddenException();
        if (serializeCpf) await central.ExecuteAsync(connection, "SELECT pg_advisory_xact_lock(hashtextextended($1,62419805))", ct, dependentCpf);
        if (!await registry.IsActiveAsync(unitId, dependentCpf, ct)) throw new ApiForbiddenException();
        var result = await action(connection, dependentCpf);
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

    /// <summary>Executa a ação com a concessão da UBS segura; ver <see cref="ExecuteAsync{T}(SessionInfo, Guid, Func{NpgsqlConnection, Task{T}}, CancellationToken)"/>.</summary>
    public Task<T> ExecuteAsync<T>(SessionInfo session, Guid unitId, Func<Task<T>> action, CancellationToken ct, bool read = false) => ExecuteAsync(session, unitId, _ => action(), ct, read);

    /// <summary>
    /// Executor da equipe do OBS: revalida conta e sessão e segura a concessão da UBS (<c>FOR UPDATE</c>) até o commit; a ação recebe a conexão central da transação (para quem precisa gravar no
    /// banco central sem pedir uma segunda conexão nem bloquear a si mesmo). A atendente ligada a um computador só opera a UBS dele (403); o administrador não é restringido.
    /// </summary>
    /// <param name="read">Rota de leitura: segura a concessão só em <c>FOR SHARE</c>, e as leituras da mesma conta não se enfileiram; a revogação (<c>DELETE</c>) e as escritas (<c>FOR UPDATE</c>) continuam esperando a leitura terminar.</param>
    public async Task<T> ExecuteAsync<T>(SessionInfo session, Guid unitId, Func<NpgsqlConnection, Task<T>> action, CancellationToken ct, bool read = false)
    {
        if (unitId == Guid.Empty || session.User.Scope != "obs" || session.User.Role is not ("administrator" or "attendant")) throw new ApiForbiddenException();
        if (session.User.Role == "attendant" && session.DeviceUnitId is { } deviceUnit && deviceUnit != unitId) throw new ApiForbiddenException();
        await using var connection = await central.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await RevalidateAsync(connection, session, ct);
        await using (var grant = central.Command(connection, read ? "SELECT 1 FROM nora.staff_units WHERE account_id=$1 AND unit_id=$2 FOR SHARE" : "SELECT 1 FROM nora.staff_units WHERE account_id=$1 AND unit_id=$2 FOR UPDATE", session.User.Id, unitId))
            if (await grant.ExecuteScalarAsync(ct) is null) throw new ApiForbiddenException();
        var result = await action(connection);
        await transaction.CommitAsync(ct);
        return result;
    }

    /// <summary>
    /// Executor da equipe para o que cria consulta de um CPF: como <see cref="ExecuteAsync{T}(SessionInfo, Guid, Func{NpgsqlConnection, Task{T}}, CancellationToken)"/> e, depois da concessão, segura o lock de CPF <c>62419805</c> até o commit central
    /// (que vem depois do commit da UBS), para a conferência de consulta em outra UBS enxergar tudo que as outras UBS já confirmaram. CPF inválido → 400 antes de qualquer gravação.
    /// </summary>
    public Task<T> ExecuteForCpfAsync<T>(SessionInfo session, Guid unitId, string? cpfValue, Func<CpfLock, Task<T>> action, CancellationToken ct) =>
        ExecuteAsync(session, unitId, async connection =>
        {
            var cpf = AccountService.NormalizeCpf(cpfValue);
            if (!AccountService.ValidCpf(cpf)) throw new ApiInputException();
            await central.ExecuteAsync(connection, "SELECT pg_advisory_xact_lock(hashtextextended($1,62419805))", ct, cpf);
            return await action(new CpfLock(cpf, connection));
        }, ct);
}

/// <summary>
/// Prova de que o lock de CPF <c>62419805</c> está seguro na transação central <see cref="Central"/>. Só os executores de acesso criam (o construtor é interno): a conferência de consulta em
/// outra UBS (<see cref="CrossUnitOverlapService"/>) só aceita este tipo, o que impede chamá-la sem o lock.
/// </summary>
public readonly struct CpfLock
{
    internal CpfLock(string cpf, NpgsqlConnection central) { Cpf = cpf; Central = central; }
    public string Cpf { get; }
    internal NpgsqlConnection Central { get; }
}
