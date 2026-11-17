using Npgsql;
using Nora.Api.DAOs;
using Nora.Api.Database;
using Nora.Api.Models;

namespace Nora.Api.Services;

/// <summary>
/// Regras dos computadores do OBS (P-014). As operações do administrador recebem a conexão central do executor de acesso da UBS (<see cref="SchedulingAccessService.ExecuteAdministratorAsync{T}(SessionInfo, Guid, Func{NpgsqlConnection, Task{T}}, CancellationToken)"/>):
/// rodam na mesma transação que segura a conta, a sessão e a concessão, então revogar o próprio computador não se bloqueia e a revogação espera as operações em andamento.
/// </summary>
public sealed class ObsDeviceService(DatabaseHelper central, ObsDeviceDao dao, TimeProvider time)
{
    private const int CodeAttempts = 3;

    /// <summary>
    /// Repete a operação uma vez quando o PostgreSQL a derruba por impasse (<c>40P01</c>, SQLSTATE de deadlock). Dois administradores (ou um administrador e a ativação por código) no mesmo computador podem se cruzar nos locks
    /// da sessão e do computador; o PostgreSQL desfaz uma das transações e a repetição, já sem o cruzamento, conclui. Cada tentativa é uma transação nova (a operação abre a sua); só um impasse é repetido, e uma segunda queda sobe como erro.
    /// </summary>
    internal static async Task<T> RetryOnDeadlockAsync<T>(Func<Task<T>> operation)
    {
        try { return await operation(); }
        catch (PostgresException error) when (error.SqlState == PostgresErrorCodes.DeadlockDetected) { return await operation(); }
    }

    /// <summary>Computador da requisição. Sem cabeçalho, desconhecido, segredo errado ou revogado dão a mesma resposta: 401 device-revoked.</summary>
    public async Task<ObsDeviceRef> CurrentAsync(DeviceCredential? credential, CancellationToken ct) =>
        credential is not null && await dao.FindActiveAsync(credential, ct) is { } device ? device : throw new ApiCodedException(StatusCodes.Status401Unauthorized, "device-revoked");

    /// <summary>Ativa o computador que apresenta o código: código inexistente, vencido ou já usado (inclusive o segundo de dois simultâneos) → 400 invalid-activation-code.</summary>
    public async Task<ObsDeviceActivation> ActivateAsync(ActivateObsDeviceRequest request, CancellationToken ct)
    {
        var code = ObsDeviceCredentials.NormalizeCode(request.Code) ?? throw new ApiCodedException(StatusCodes.Status400BadRequest, "invalid-activation-code");
        return await RetryOnDeadlockAsync(async () =>
        {
            await using var connection = await central.OpenAsync(ct);
            await using var transaction = await connection.BeginTransactionAsync(ct);
            var activation = await dao.ActivateAsync(connection, ObsDeviceCredentials.HashCode(code), ObsDeviceCredentials.NewSecret(), time.GetUtcNow(), ct)
                ?? throw new ApiCodedException(StatusCodes.Status400BadRequest, "invalid-activation-code");
            await transaction.CommitAsync(ct);
            return activation;
        });
    }

    public async Task<ObsDeviceList> ListAsync(NpgsqlConnection connection, Guid unitId, DeviceCredential? current, CancellationToken ct) =>
        new(await dao.ListAsync(connection, unitId, time.GetUtcNow(), current, ct));

    /// <summary>
    /// Cria um computador: ativado agora (devolve o segredo uma única vez) ou aguardando o código de outro computador. Com <c>ActivateThisComputer</c> e a requisição já trazendo um
    /// computador ativo → 409 device-already-active.
    /// </summary>
    public async Task<(ObsDeviceInfo Device, ObsDeviceCredentialResult? Credential, ObsDeviceCodeResult? Code)> CreateAsync(NpgsqlConnection connection, SessionInfo session, Guid unitId, CreateObsDeviceRequest request, DeviceCredential? current, CancellationToken ct)
    {
        var name = ObsDeviceCredentials.ValidName(request.Name);
        if (request.ActivateThisComputer && current is not null && await dao.FindActiveAsync(connection, current, ct) is not null)
            throw new ApiCodedException(StatusCodes.Status409Conflict, "device-already-active");
        var now = time.GetUtcNow(); var id = Guid.NewGuid();
        if (request.ActivateThisComputer)
        {
            var secret = ObsDeviceCredentials.NewSecret();
            if (!await dao.InsertAsync(connection, id, unitId, name, "active", ObsDeviceCredentials.HashSecret(secret), null, null, session.User.Id, now, ct)) throw new ApiConflictException();
            await dao.AddEventAsync(connection, id, unitId, session.User.Id, "created", now, ct);
            await dao.AddEventAsync(connection, id, unitId, session.User.Id, "activated", now, ct);
            return (await dao.ReadOneAsync(connection, unitId, id, now, current, ct), new(id, secret), null);
        }
        var expires = now + ObsDeviceCredentials.CodeLifetime;
        for (var attempt = 0; attempt < CodeAttempts; attempt++)
        {
            var code = ObsDeviceCredentials.NewCode();
            if (!await dao.InsertAsync(connection, id, unitId, name, "pending", null, ObsDeviceCredentials.HashCode(code), expires, session.User.Id, now, ct)) continue;
            await dao.AddEventAsync(connection, id, unitId, session.User.Id, "created", now, ct);
            await dao.AddEventAsync(connection, id, unitId, session.User.Id, "code-issued", now, ct);
            return (await dao.ReadOneAsync(connection, unitId, id, now, current, ct), null, new(ObsDeviceCredentials.Display(code), expires));
        }
        throw new ApiConflictException();
    }

    /// <summary>Código novo para um computador pendente ou ativo (reativação: o segredo antigo vale até o código ser usado). Revogado → 409; de outra UBS ou inexistente → 404.</summary>
    public async Task<ObsDeviceCodeResult> IssueCodeAsync(NpgsqlConnection connection, SessionInfo session, Guid unitId, Guid id, CancellationToken ct)
    {
        var status = await dao.LockStatusAsync(connection, unitId, id, ct) ?? throw new ApiNotFoundException();
        if (status == "revoked") throw new ApiConflictException();
        var now = time.GetUtcNow(); var expires = now + ObsDeviceCredentials.CodeLifetime;
        for (var attempt = 0; attempt < CodeAttempts; attempt++)
        {
            var code = ObsDeviceCredentials.NewCode();
            if (!await dao.SetCodeAsync(connection, id, ObsDeviceCredentials.HashCode(code), expires, ct)) continue;
            await dao.AddEventAsync(connection, id, unitId, session.User.Id, "code-issued", now, ct);
            return new(ObsDeviceCredentials.Display(code), expires);
        }
        throw new ApiConflictException();
    }

    /// <summary>Revoga o computador e as sessões dele. Sem <c>confirmed</c> → 400; já revogado → 409; de outra UBS ou inexistente → 404.</summary>
    public async Task<ObsDeviceInfo> RevokeAsync(NpgsqlConnection connection, SessionInfo session, Guid unitId, Guid id, RevokeObsDeviceRequest request, DeviceCredential? current, CancellationToken ct)
    {
        if (!request.Confirmed) throw new ApiInputException();
        var status = await dao.LockStatusAsync(connection, unitId, id, ct) ?? throw new ApiNotFoundException();
        if (status == "revoked") throw new ApiConflictException();
        var now = time.GetUtcNow();
        await dao.RevokeAsync(connection, id, session.User.Id, now, ct);
        await dao.AddEventAsync(connection, id, unitId, session.User.Id, "revoked", now, ct);
        return await dao.ReadOneAsync(connection, unitId, id, now, current, ct);
    }
}
