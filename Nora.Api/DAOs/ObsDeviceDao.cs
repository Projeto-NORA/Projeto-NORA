using Npgsql;
using Nora.Api.Database;
using Nora.Api.Models;
using Nora.Api.Services;

namespace Nora.Api.DAOs;

internal sealed record ObsDeviceEnrollment(ObsDeviceRef Device, bool CanSignIn);

/// <summary>
/// SQL dos computadores do OBS (banco central). Os métodos que recebem a conexão rodam na transação do chamador (o executor de acesso da UBS ou a ativação por código), que
/// já segura a conta e a sessão; nada aqui confere permissão. Tempo sempre pelo relógio informado, nunca pelo do banco.
/// </summary>
public sealed class ObsDeviceDao(DatabaseHelper database)
{
    private const string DeviceSelect = "SELECT d.id,d.name,d.status,d.created_at,c.full_name,d.activated_at,d.last_login_at,d.revoked_at,r.full_name,CASE WHEN d.code_expires_at>$2 THEN d.code_expires_at END,(d.id=$3::uuid AND d.secret_hash=$4::text) " +
        "FROM nora.obs_devices d JOIN nora.accounts c ON c.id=d.created_by LEFT JOIN nora.accounts r ON r.id=d.revoked_by ";

    /// <summary>Computador ativo com este id e este segredo (ou nulo). <paramref name="lockRow"/> segura a linha com <c>FOR NO KEY UPDATE</c> até o fim da transação (login): a revogação (<c>FOR UPDATE</c>) espera o login, e dois logins no mesmo computador se enfileiram em vez de segurar um lock compartilhado que ambos promoveriam ao gravar <c>last_login_at</c> (impasse).</summary>
    internal static async Task<ObsDeviceRef?> FindActiveAsync(DatabaseHelper database, NpgsqlConnection connection, DeviceCredential credential, bool lockRow, CancellationToken ct)
    {
        await using var query = database.Command(connection,
            "SELECT d.id,d.name,d.unit_id,u.name FROM nora.obs_devices d JOIN nora.health_units u ON u.id=d.unit_id WHERE d.id=$1 AND d.status='active' AND d.secret_hash=$2" + (lockRow ? " FOR NO KEY UPDATE OF d" : ""),
            credential.Id, ObsDeviceCredentials.HashSecret(credential.Secret));
        await using var reader = await query.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? new(reader.GetGuid(0), reader.GetString(1), reader.GetGuid(2), reader.GetString(3)) : null;
    }

    public Task<ObsDeviceRef?> FindActiveAsync(DeviceCredential credential, CancellationToken ct) => database.UseAsync(connection => FindActiveAsync(database, connection, credential, false, ct), ct);

    internal Task<ObsDeviceRef?> FindActiveAsync(NpgsqlConnection connection, DeviceCredential credential, CancellationToken ct) => FindActiveAsync(database, connection, credential, false, ct);

    internal async Task<ObsDeviceEnrollment?> FindEnrollmentAsync(NpgsqlConnection connection, DeviceCredential credential, CancellationToken ct)
    {
        await using var query = database.Command(connection,
            "SELECT d.id,d.name,d.unit_id,u.name,COALESCE(d.status='active' AND d.secret_hash=$2,false) " +
            "FROM nora.obs_devices d JOIN nora.health_units u ON u.id=d.unit_id WHERE d.id=$1 FOR NO KEY UPDATE OF d",
            credential.Id, ObsDeviceCredentials.HashSecret(credential.Secret));
        await using var reader = await query.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct)
            ? new(new(reader.GetGuid(0), reader.GetString(1), reader.GetGuid(2), reader.GetString(3)), reader.GetBoolean(4))
            : null;
    }

    /// <summary>Lista apenas unidades concedidas. Ao preparar um login, segura a concessão enquanto o computador e a sessão são gravados.</summary>
    internal static async Task<List<LoginUnitOption>> ListAuthorizedUnitsAsync(DatabaseHelper database, NpgsqlConnection connection,
        Guid accountId, Guid? unitId, bool lockRows, CancellationToken ct)
    {
        await using var command = database.Command(connection,
            "SELECT u.id,u.name FROM nora.staff_units s JOIN nora.health_units u ON u.id=s.unit_id " +
            "WHERE s.account_id=$1 AND ($2::uuid IS NULL OR s.unit_id=$2) ORDER BY u.name,u.id" + (lockRows ? " FOR SHARE OF s" : ""),
            accountId, (object?)unitId ?? DBNull.Value);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var units = new List<LoginUnitOption>();
        while (await reader.ReadAsync(ct)) units.Add(new(reader.GetGuid(0), reader.GetString(1)));
        return units;
    }

    private static object[] Values(Guid? first, DateTimeOffset now, DeviceCredential? current, params object[] extra)
    {
        var values = new List<object> { first ?? (object)DBNull.Value, now, (object?)current?.Id ?? DBNull.Value, (object?)(current is null ? null : ObsDeviceCredentials.HashSecret(current.Secret)) ?? DBNull.Value };
        values.AddRange(extra);
        return [.. values];
    }

    private static ObsDeviceInfo Read(NpgsqlDataReader reader) => new(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetFieldValue<DateTimeOffset>(3), reader.GetString(4),
        reader.IsDBNull(5) ? null : reader.GetFieldValue<DateTimeOffset>(5), reader.IsDBNull(6) ? null : reader.GetFieldValue<DateTimeOffset>(6), reader.IsDBNull(7) ? null : reader.GetFieldValue<DateTimeOffset>(7),
        reader.IsDBNull(8) ? null : reader.GetString(8), reader.IsDBNull(9) ? null : reader.GetFieldValue<DateTimeOffset>(9), !reader.IsDBNull(10) && reader.GetBoolean(10));

    /// <summary>Computadores da UBS: ativos, depois os aguardando código, depois os revogados; por nome dentro de cada situação.</summary>
    internal async Task<List<ObsDeviceInfo>> ListAsync(NpgsqlConnection connection, Guid unitId, DateTimeOffset now, DeviceCredential? current, CancellationToken ct)
    {
        // $1 = UBS; $2 agora; $3 e $4 = computador da requisição (a consulta compartilha o SELECT com a leitura de um só).
        await using var query = database.Command(connection, DeviceSelect + "WHERE d.unit_id=$1 ORDER BY CASE d.status WHEN 'active' THEN 0 WHEN 'pending' THEN 1 ELSE 2 END,lower(d.name),d.id", Values(unitId, now, current));
        await using var reader = await query.ExecuteReaderAsync(ct);
        var devices = new List<ObsDeviceInfo>();
        while (await reader.ReadAsync(ct)) devices.Add(Read(reader));
        return devices;
    }

    internal async Task<ObsDeviceInfo> ReadOneAsync(NpgsqlConnection connection, Guid unitId, Guid id, DateTimeOffset now, DeviceCredential? current, CancellationToken ct)
    {
        await using var query = database.Command(connection, DeviceSelect + "WHERE d.unit_id=$1 AND d.id=$5", Values(unitId, now, current, id));
        await using var reader = await query.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Read(reader) : throw new ApiNotFoundException();
    }

    /// <summary>Cria o computador. Retorna falso quando o código colidiu com outro (o chamador gera um novo).</summary>
    internal async Task<bool> InsertAsync(NpgsqlConnection connection, Guid id, Guid unitId, string name, string status, string? secretHash, string? codeHash, DateTimeOffset? codeExpiresAt, Guid createdBy, DateTimeOffset now, CancellationToken ct)
    {
        var count = await database.ExecuteAsync(connection,
            "INSERT INTO nora.obs_devices(id,unit_id,name,status,secret_hash,code_hash,code_expires_at,created_by,created_at,activated_at) VALUES($1,$2,$3,$4,$5::text,$6::text,$7::timestamptz,$8,$9,CASE WHEN $4='active' THEN $9::timestamptz END) ON CONFLICT DO NOTHING",
            ct, id, unitId, name, status, (object?)secretHash ?? DBNull.Value, (object?)codeHash ?? DBNull.Value, (object?)codeExpiresAt ?? DBNull.Value, createdBy, now);
        return count == 1;
    }

    internal Task<int> AddEventAsync(NpgsqlConnection connection, Guid deviceId, Guid unitId, Guid? actorId, string action, DateTimeOffset now, CancellationToken ct) =>
        database.ExecuteAsync(connection, "INSERT INTO nora.obs_device_events(device_id,unit_id,actor_id,action,occurred_at) VALUES($1,$2,$3::uuid,$4,$5)", ct, deviceId, unitId, (object?)actorId ?? DBNull.Value, action, now);

    /// <summary>Situação do computador da UBS, bloqueado para escrita na transação do chamador; nulo se não existe naquela UBS.</summary>
    internal async Task<string?> LockStatusAsync(NpgsqlConnection connection, Guid unitId, Guid id, CancellationToken ct)
    {
        await using var query = database.Command(connection, "SELECT status FROM nora.obs_devices WHERE id=$1 AND unit_id=$2 FOR UPDATE", id, unitId);
        return await query.ExecuteScalarAsync(ct) as string;
    }

    /// <summary>Troca o código de ativação, a menos que o novo colida com outro vigente (falso).</summary>
    internal async Task<bool> SetCodeAsync(NpgsqlConnection connection, Guid id, string codeHash, DateTimeOffset expiresAt, CancellationToken ct) =>
        await database.ExecuteAsync(connection, "UPDATE nora.obs_devices SET code_hash=$2,code_expires_at=$3 WHERE id=$1 AND NOT EXISTS(SELECT 1 FROM nora.obs_devices o WHERE o.code_hash=$2)", ct, id, codeHash, expiresAt) == 1;

    /// <summary>Revoga o computador (limpa segredo e código) e as sessões abertas ligadas a ele; as seguintes requisições dessas sessões recebem 401.</summary>
    internal async Task RevokeAsync(NpgsqlConnection connection, Guid id, Guid actorId, DateTimeOffset now, CancellationToken ct)
    {
        await database.ExecuteAsync(connection, "UPDATE nora.obs_devices SET status='revoked',secret_hash=NULL,code_hash=NULL,code_expires_at=NULL,revoked_by=$2,revoked_at=$3 WHERE id=$1", ct, id, actorId, now);
        await RevokeSessionsAsync(connection, id, now, actorId, ct);
    }

    /// <summary>
    /// Encerra as sessões abertas do computador e espera as operações em andamento das sessões dos outros usuários (que a revogação deve esperar). Com <paramref name="actorId"/> (o administrador que revoga),
    /// as sessões dele ligadas ao computador pulam o que outra requisição dele segura com <c>FOR SHARE</c> (a revalidação do executor): essa requisição espera a concessão da UBS que a revogação já segura,
    /// e esperar por ela em troca do lock exclusivo daria impasse (o administrador revoga o computador em que está enquanto o OBS dispara outra leitura). Não há perda: sessão ligada a computador revogado já não
    /// vale (<see cref="SessionDao.FindAsync"/> exige o computador ativo), então a linha pulada só fica sem <c>revoked_at</c> até a manutenção apagá-la.
    /// </summary>
    internal async Task RevokeSessionsAsync(NpgsqlConnection connection, Guid deviceId, DateTimeOffset now, Guid? actorId, CancellationToken ct)
    {
        await database.ExecuteAsync(connection, "UPDATE nora.sessions SET revoked_at=$2,presence_seen_at=NULL WHERE device_id=$1 AND revoked_at IS NULL AND ($3::uuid IS NULL OR account_id<>$3)", ct, deviceId, now, (object?)actorId ?? DBNull.Value);
        if (actorId is { } actor)
            await database.ExecuteAsync(connection, "UPDATE nora.sessions SET revoked_at=$2,presence_seen_at=NULL WHERE token_hash IN (SELECT token_hash FROM nora.sessions WHERE device_id=$1 AND revoked_at IS NULL AND account_id=$3 FOR UPDATE SKIP LOCKED)", ct, deviceId, now, actor);
    }

    /// <summary>Ativação com o código: segura o computador com o código vigente (pendente ou ativo) e grava o segredo novo. Código inexistente, vencido ou já usado → nulo.</summary>
    internal async Task<ObsDeviceActivation?> ActivateAsync(NpgsqlConnection connection, string codeHash, string secret, DateTimeOffset now, CancellationToken ct)
    {
        Guid id, unitId; string name, unitName;
        await using (var find = database.Command(connection,
            "SELECT d.id,d.unit_id,d.name,u.name FROM nora.obs_devices d JOIN nora.health_units u ON u.id=d.unit_id WHERE d.code_hash=$1 AND d.code_expires_at>$2 AND d.status IN ('pending','active') FOR UPDATE OF d", codeHash, now))
        await using (var reader = await find.ExecuteReaderAsync(ct))
        {
            if (!await reader.ReadAsync(ct)) return null;
            id = reader.GetGuid(0); unitId = reader.GetGuid(1); name = reader.GetString(2); unitName = reader.GetString(3);
        }
        await database.ExecuteAsync(connection, "UPDATE nora.obs_devices SET status='active',secret_hash=$2,activated_at=$3,code_hash=NULL,code_expires_at=NULL WHERE id=$1", ct, id, ObsDeviceCredentials.HashSecret(secret), now);
        // Reativação = troca de máquina: o computador antigo e as sessões dele deixam de valer.
        await RevokeSessionsAsync(connection, id, now, null, ct);
        await AddEventAsync(connection, id, unitId, null, "activated", now, ct);
        return new(id, secret, name, unitId, unitName);
    }

    /// <summary>Limpa, em lote, códigos de ativação vencidos há mais de <paramref name="cutoff"/> (o computador continua e pode receber um código novo).</summary>
    public Task<int> CleanExpiredCodesAsync(DateTimeOffset cutoff, int batch, CancellationToken ct) => database.UseAsync(connection => database.ExecuteAsync(connection,
        "UPDATE nora.obs_devices SET code_hash=NULL,code_expires_at=NULL WHERE id IN (SELECT id FROM nora.obs_devices WHERE code_expires_at<$1 LIMIT $2 FOR UPDATE SKIP LOCKED)", ct, cutoff, batch), ct);
}
