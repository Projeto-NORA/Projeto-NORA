using Npgsql;
using Nora.Api.Database;
using Nora.Api.Services;

namespace Nora.Api.DAOs;

/// <summary>Uma linha da fila de saída já reservada para envio (a tentativa já foi contada).</summary>
public sealed record PushDelivery(long Id, Guid DeviceId, string Token, string Type, Guid? UnitId, Guid? DependentId, Guid? AppointmentId, int Attempts);

/// <summary>
/// SQL do push (banco central, migração 018): aparelhos, fila de saída e cursor dos comunicados. A chave única <c>(event_key, device_id)</c> da fila é a marca de "já avisado":
/// os <c>INSERT ... ON CONFLICT DO NOTHING</c> podem reencontrar o mesmo evento em vários passes (ou em várias instâncias da API) sem repetir o aviso.
/// </summary>
public sealed class PushDao(DatabaseHelper database)
{
    public const int MaxDevicesPerAccount = 10;
    private const long DeviceLock = 62419840;
    private const long AnnouncementCursorLock = 62419841;

    // Destinatários de um evento de UBS por CPF: a conta de paciente ativa do CPF vinculada à UBS e os responsáveis ativos com vínculo de Família aprovado nela.
    private const string Recipients =
        "SELECT d.id AS device_id, NULL::uuid AS dependent_id FROM nora.accounts a JOIN nora.patient_units pu ON pu.patient_id=a.id AND pu.unit_id=$1 JOIN nora.push_devices d ON d.account_id=a.id " +
        "WHERE a.cpf=t.cpf AND a.scope='patient' AND a.is_active " +
        "UNION ALL SELECT d.id, f.id FROM nora.family_links f JOIN nora.accounts g ON g.id=f.guardian_id AND g.is_active JOIN nora.push_devices d ON d.account_id=g.id " +
        "WHERE f.dependent_cpf=t.cpf AND f.unit_id=$1 AND f.status='approved'";

    // ---- Aparelhos ----

    /// <summary>Registra o token para a conta (o token passa a ser só dela) e mantém os 10 aparelhos mais recentes da conta.</summary>
    public async Task RegisterDeviceAsync(Guid accountId, string token, string platform, DateTimeOffset now, CancellationToken ct)
    {
        await using var connection = await database.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await database.ExecuteAsync(connection, "SELECT pg_advisory_xact_lock(hashtextextended($1::text,$2))", ct, accountId, DeviceLock);
        await database.ExecuteAsync(connection, "DELETE FROM nora.push_devices WHERE token=$1 AND account_id<>$2", ct, token, accountId);
        await database.ExecuteAsync(connection, "INSERT INTO nora.push_devices(id,token,account_id,platform,registered_at,updated_at) VALUES($1,$2,$3,$4,$5,$5) " +
            "ON CONFLICT (token) DO UPDATE SET updated_at=EXCLUDED.updated_at,platform=EXCLUDED.platform WHERE push_devices.account_id=EXCLUDED.account_id", ct, Guid.NewGuid(), token, accountId, platform, now);
        await database.ExecuteAsync(connection, "DELETE FROM nora.push_devices WHERE account_id=$1 AND id NOT IN (SELECT id FROM nora.push_devices WHERE account_id=$1 ORDER BY updated_at DESC,registered_at DESC LIMIT $2)", ct, accountId, MaxDevicesPerAccount);
        await transaction.CommitAsync(ct);
    }

    /// <summary>Remove o token se ele for da conta; de outra conta ou inexistente não faz nada (idempotente).</summary>
    public Task<int> RemoveDeviceAsync(Guid accountId, string token, CancellationToken ct) => database.UseAsync(connection =>
        database.ExecuteAsync(connection, "DELETE FROM nora.push_devices WHERE token=$1 AND account_id=$2", ct, token, accountId), ct);

    public Task<bool> AnyDeviceAsync(CancellationToken ct) => database.UseAsync(async connection =>
    {
        await using var command = database.Command(connection, "SELECT EXISTS(SELECT 1 FROM nora.push_devices)");
        return await command.ExecuteScalarAsync(ct) is true;
    }, ct);

    // ---- Enfileiramento (marca de "já avisado") ----

    /// <summary>
    /// Avisos de eventos de uma UBS identificados por CPF (oferta de Vaga Relâmpago ou janela de confirmação de presença). <paramref name="items"/> são os ids do evento;
    /// <paramref name="appointment"/> diz se o id também vai como <c>appointmentId</c>.
    /// </summary>
    public Task<int> EnqueueUnitEventsAsync(Guid unitId, string type, IReadOnlyList<(Guid Id, string Cpf)> items, bool appointment, DateTimeOffset now, CancellationToken ct)
    {
        if (items.Count == 0) return Task.FromResult(0);
        return database.UseAsync(connection => database.ExecuteAsync(connection,
            "INSERT INTO nora.push_outbox(event_key,event_type,device_id,unit_id,dependent_id,appointment_id,created_at,next_attempt_at) " +
            "SELECT $5||':'||t.item,$5,r.device_id,$1,r.dependent_id,CASE WHEN $6 THEN t.item END,$4,$4 FROM unnest($2::uuid[],$3::text[]) AS t(item,cpf) " +
            $"CROSS JOIN LATERAL ({Recipients}) r ON CONFLICT (event_key,device_id) DO NOTHING",
            ct, unitId, items.Select(item => item.Id).ToArray(), items.Select(item => item.Cpf).ToArray(), now, type, appointment), ct);
    }

    /// <summary>Pedidos de família aprovados ou recusados desde <paramref name="since"/>: um aviso por aparelho do responsável, com o vínculo como <c>dependentId</c>.</summary>
    public Task<int> EnqueueFamilyDecisionsAsync(DateTimeOffset since, DateTimeOffset now, CancellationToken ct) => database.UseAsync(connection => database.ExecuteAsync(connection,
        "INSERT INTO nora.push_outbox(event_key,event_type,device_id,unit_id,dependent_id,created_at,next_attempt_at) " +
        "SELECT 'family-decision:'||f.id,'family-decision',d.id,f.unit_id,f.id,$1,$1 FROM nora.family_links f JOIN nora.accounts g ON g.id=f.guardian_id AND g.is_active " +
        "JOIN nora.push_devices d ON d.account_id=g.id WHERE f.decided_at>=$2 AND f.decided_at<=$1 ON CONFLICT (event_key,device_id) DO NOTHING", ct, now, since), ct);

    /// <summary>
    /// Comunicados novos para pacientes (público <c>patients</c> ou <c>both</c>, em vigor), a partir do cursor de <c>seq</c>: na primeira vez, só os publicados desde
    /// <paramref name="firstSince"/>. O alcance é o mesmo da leitura do app (vínculos de hoje); <c>unitId</c> é uma UBS alcançada do paciente. O cursor avança na mesma transação.
    /// </summary>
    public async Task<int> EnqueueAnnouncementsAsync(DateTimeOffset now, DateTimeOffset firstSince, CancellationToken ct)
    {
        await using var connection = await database.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await database.ExecuteAsync(connection, "SELECT pg_advisory_xact_lock($1)", ct, AnnouncementCursorLock);
        long? cursor;
        await using (var read = database.Command(connection, "SELECT position FROM nora.push_cursors WHERE name='announcements'"))
            cursor = await read.ExecuteScalarAsync(ct) as long?;
        long max;
        await using (var top = database.Command(connection, "SELECT coalesce(max(seq),0) FROM nora.announcements"))
            max = (long)(await top.ExecuteScalarAsync(ct))!;
        var inserted = 0;
        if (max > (cursor ?? -1))
            inserted = await database.ExecuteAsync(connection,
                "INSERT INTO nora.push_outbox(event_key,event_type,device_id,unit_id,created_at,next_attempt_at) " +
                "SELECT DISTINCT ON (an.id,d.id) 'announcement:'||an.id,'announcement',d.id,pu.unit_id,$1,$1 FROM nora.announcements an " +
                "JOIN nora.patient_units pu ON an.scope='all' " +
                "OR (an.scope='units' AND EXISTS(SELECT 1 FROM nora.announcement_units au WHERE au.announcement_id=an.id AND au.unit_id=pu.unit_id)) " +
                "OR (an.scope='region' AND EXISTS(SELECT 1 FROM nora.announcement_regions ar JOIN nora.health_units h ON lower(btrim(h.region))=ar.region_key WHERE ar.announcement_id=an.id AND h.id=pu.unit_id)) " +
                "JOIN nora.accounts a ON a.id=pu.patient_id AND a.scope='patient' AND a.is_active JOIN nora.push_devices d ON d.account_id=a.id " +
                "WHERE an.seq>$2 AND an.seq<=$3 AND ($4::timestamptz IS NULL OR an.created_at>=$4) AND an.closed_at IS NULL AND (an.expires_at IS NULL OR an.expires_at>$1) " +
                "AND an.audience IN ('patients','both') ORDER BY an.id,d.id,pu.unit_id ON CONFLICT (event_key,device_id) DO NOTHING",
                ct, now, cursor ?? -1, max, cursor is null ? firstSince : (object)DBNull.Value);
        if (max != cursor)
            await database.ExecuteAsync(connection, "INSERT INTO nora.push_cursors(name,position) VALUES('announcements',$1) ON CONFLICT (name) DO UPDATE SET position=EXCLUDED.position", ct, max);
        await transaction.CommitAsync(ct);
        return inserted;
    }

    // ---- Envio ----

    /// <summary>Reserva até <paramref name="limit"/> avisos vencidos: conta a tentativa e empurra a próxima para depois de <paramref name="lease"/> (se o processo cair no meio, a linha volta sozinha).</summary>
    public Task<List<PushDelivery>> ClaimDueAsync(DateTimeOffset now, TimeSpan lease, int limit, CancellationToken ct) => database.UseAsync(async connection =>
    {
        await using var command = database.Command(connection,
            "UPDATE nora.push_outbox o SET attempts=o.attempts+1,next_attempt_at=$2 FROM nora.push_devices d WHERE d.id=o.device_id AND o.id IN " +
            "(SELECT id FROM nora.push_outbox WHERE sent_at IS NULL AND abandoned_at IS NULL AND next_attempt_at<=$1 ORDER BY next_attempt_at,id LIMIT $3 FOR UPDATE SKIP LOCKED) " +
            "RETURNING o.id,o.device_id,d.token,o.event_type,o.unit_id,o.dependent_id,o.appointment_id,o.attempts", now, now + lease, limit);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var claimed = new List<PushDelivery>();
        while (await reader.ReadAsync(ct))
            claimed.Add(new(reader.GetInt64(0), reader.GetGuid(1), reader.GetString(2), reader.GetString(3), Nullable(reader, 4), Nullable(reader, 5), Nullable(reader, 6), reader.GetInt16(7)));
        return claimed;
    }, ct);

    /// <summary>Grava o resultado de um lote numa transação: enviados, nova tentativa (com a espera), abandonados e aparelhos removidos (UNREGISTERED, que leva junto a fila deles).</summary>
    public async Task CompleteAsync(IReadOnlyList<long> sent, IReadOnlyList<(long Id, DateTimeOffset Next)> retry, IReadOnlyList<long> abandoned, IReadOnlyList<Guid> removedDevices, DateTimeOffset now, CancellationToken ct)
    {
        await using var connection = await database.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        if (sent.Count > 0) await database.ExecuteAsync(connection, "UPDATE nora.push_outbox SET sent_at=$2 WHERE id=ANY($1)", ct, sent.ToArray(), now);
        if (abandoned.Count > 0) await database.ExecuteAsync(connection, "UPDATE nora.push_outbox SET abandoned_at=$2 WHERE id=ANY($1)", ct, abandoned.ToArray(), now);
        if (retry.Count > 0) await database.ExecuteAsync(connection, "UPDATE nora.push_outbox o SET next_attempt_at=t.next FROM unnest($1::bigint[],$2::timestamptz[]) AS t(id,next) WHERE o.id=t.id", ct,
            retry.Select(item => item.Id).ToArray(), retry.Select(item => item.Next).ToArray());
        if (removedDevices.Count > 0) await database.ExecuteAsync(connection, "DELETE FROM nora.push_devices WHERE id=ANY($1)", ct, removedDevices.ToArray());
        await transaction.CommitAsync(ct);
    }

    /// <summary>Apaga, num lote, avisos enviados ou abandonados criados antes de <paramref name="cutoff"/> (a marca de "já avisado" só precisa durar enquanto o evento pode reaparecer).</summary>
    public Task<int> CleanupAsync(DateTimeOffset cutoff, int batch, CancellationToken ct) => database.UseAsync(connection => database.ExecuteAsync(connection,
        "DELETE FROM nora.push_outbox WHERE id IN (SELECT id FROM nora.push_outbox WHERE (sent_at IS NOT NULL OR abandoned_at IS NOT NULL) AND created_at<$1 LIMIT $2 FOR UPDATE SKIP LOCKED)", ct, cutoff, batch), ct);

    private static Guid? Nullable(NpgsqlDataReader reader, int index) => reader.IsDBNull(index) ? null : reader.GetGuid(index);
}
