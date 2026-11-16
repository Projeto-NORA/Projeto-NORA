using System.Threading.RateLimiting;
using Nora.Api.Database;
using Nora.Api.DAOs;
using Nora.Api.Models;

namespace Nora.Api.Services;

/// <summary>
/// Executores de acesso da agenda (Fase 3) que complementam <see cref="UnitAccessService"/> e reaproveitam a mesma revalidação de conta e sessão dentro da transação central.
/// </summary>
public sealed class SchedulingAccessService(DatabaseHelper central, UnitPatientRegistryDao registry, UnitAccessService access)
{
    /// <summary>Só o administrador da UBS mantém profissionais, consultórios, grade e bloqueios; o atendente recebe 403. A concessão da UBS segue o executor comum.</summary>
    public Task<T> ExecuteAdministratorAsync<T>(SessionInfo session, Guid unitId, Func<Task<T>> action, CancellationToken ct)
    {
        if (session.User.Role != "administrator") throw new ApiForbiddenException();
        return access.ExecuteAsync(session, unitId, action, ct);
    }

    /// <summary>
    /// Leitura da Secretaria de dado que não é de paciente (profissionais, consultórios, grade e bloqueios da UBS). Como a leitura nominal, revalida conta e sessão e confere a
    /// unidade, mas não grava evento de acesso: o histórico de consultas nominais só registra leitura de dado pessoal.
    /// </summary>
    public async Task<T> ExecuteManagementCatalogReadAsync<T>(SessionInfo session, Guid unitId, Func<Task<T>> action, CancellationToken ct)
    {
        if (unitId == Guid.Empty || session.User.Scope != "municipal" || session.User.Role != "manager") throw new ApiForbiddenException();
        await using var connection = await central.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await access.RevalidateAsync(connection, session, ct);
        await using (var unit = central.Command(connection, "SELECT id FROM nora.health_units WHERE id=$1 FOR KEY SHARE", unitId))
            if (await unit.ExecuteScalarAsync(ct) is null) throw new ApiNotFoundException();
        var result = await action();
        await transaction.CommitAsync(ct);
        return result;
    }

    /// <summary>Executor do responsável pelo dependente com o lock de CPF (do dependente) seguro: ver <see cref="UnitAccessService.ExecuteGuardianAsync{T}"/>. Usado pelo agendamento, que depende das consultas do CPF na rede inteira.</summary>
    public Task<T> ExecuteGuardianNetworkAsync<T>(SessionInfo session, Guid dependentId, Guid unitId, Func<string, Task<T>> action, CancellationToken ct) =>
        access.ExecuteGuardianAsync(session, dependentId, unitId, action, ct, serializeCpf: true);

    /// <summary>
    /// Como <see cref="UnitAccessService.ExecutePatientAsync{T}"/>, e além disso serializa, pelo mesmo lock de CPF da remoção de vínculo (<c>62419805</c>), tudo o que depende do conjunto de
    /// consultas do paciente na rede inteira. É o que impede duas UBS, em paralelo, de furarem o limite de uma consulta futura por especialidade: o segundo pedido só começa depois
    /// de o primeiro confirmar a UBS e a central, e então enxerga a consulta nova. Ordem dos bloqueios igual à da remoção de vínculo (CPF, depois o vínculo), sem risco de impasse.
    /// </summary>
    public async Task<T> ExecutePatientNetworkAsync<T>(SessionInfo session, Guid unitId, Func<string, Task<T>> action, CancellationToken ct)
    {
        if (unitId == Guid.Empty || session.User.Scope != "patient" || session.User.Role != "patient") throw new ApiForbiddenException();
        await using var connection = await central.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        var cpf = await access.RevalidateAsync(connection, session, ct);
        await central.ExecuteAsync(connection, "SELECT pg_advisory_xact_lock(hashtextextended($1,62419805))", ct, cpf);
        await using (var link = central.Command(connection, "SELECT 1 FROM nora.patient_units WHERE patient_id=$1 AND unit_id=$2 FOR UPDATE", session.User.Id, unitId))
            if (await link.ExecuteScalarAsync(ct) is null) throw new ApiForbiddenException();
        if (!await registry.IsActiveAsync(unitId, cpf, ct)) throw new ApiForbiddenException();
        var result = await action(cpf);
        await transaction.CommitAsync(ct);
        return result;
    }
}

/// <summary>Limite por conta autenticada das rotas de escrita do paciente na agenda (mesmo desenho de <see cref="AccountRateLimiter"/>, em memória).</summary>
public sealed class SchedulingRateLimiter : IDisposable
{
    public const string Booking = "patient-booking";
    public const string Confirmation = "patient-confirmation";
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(10);
    private readonly PartitionedRateLimiter<(string Policy, Guid Account)> limiter = PartitionedRateLimiter.Create<(string Policy, Guid Account), (string Policy, Guid Account)>(key =>
        RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions { PermitLimit = key.Policy == Booking ? 20 : 40, Window = Window, QueueLimit = 0 }));

    /// <summary>Consome uma permissão da conta na política; sem permissão disponível → 429.</summary>
    public void Require(string policy, Guid accountId)
    {
        using var lease = limiter.AttemptAcquire((policy, accountId));
        if (!lease.IsAcquired) throw new ApiRateLimitException();
    }

    public void Dispose() => limiter.Dispose();
}
