using Npgsql;
using Nora.Api.Database;
using Nora.Api.Models;
using Nora.Api.Services;

namespace Nora.Api.DAOs;

/// <summary>
/// Vagas relâmpago da UBS. A vaga tem dados próprios (horário, especialidade, profissional): toda leitura usa as colunas da vaga, e a consulta de origem só serve para mostrar de onde ela veio.
/// Cria a vaga a partir de uma consulta cancelada, aprova e descarta as que aguardam aprovação e registra a resposta do paciente; a distribuição é do <see cref="UnitLightningDispatcher"/>.
/// </summary>
/// <param name="central">Banco central, só para o nome de quem decidiu (aprovou ou descartou) nas listas da equipe.</param>
public sealed class UnitLightningDao(UnitDatabaseRouter databases, TimeProvider time, DatabaseHelper? central = null)
{
    private const string SlotColumns = "s.id,s.appointment_id,s.specialty,s.professional,s.starts_at,s.ends_at,s.status,s.created_at,s.origin,s.decided_by,s.decided_at,s.closed_reason,a.cpf,p.full_name,a.starts_at,a.ends_at,c.occurred_at,c.source";
    private const string SlotFrom = "FROM nora_unit.lightning_slots s LEFT JOIN nora_unit.appointments a ON a.id=s.appointment_id LEFT JOIN nora_unit.patient_registry p ON p.cpf=a.cpf " +
        "LEFT JOIN LATERAL (SELECT e.occurred_at,e.source FROM nora_unit.appointment_events e WHERE e.appointment_id=a.id AND e.status='cancelled' ORDER BY e.id DESC LIMIT 1) c ON true ";

    public async Task<UnitLightningSlot> CreateAsync(Guid unitId, Guid actorId, CreateUnitLightningSlotRequest request, CancellationToken ct)
    {
        if (unitId == Guid.Empty || actorId == Guid.Empty || request.AppointmentId == Guid.Empty || !request.Confirmed) throw new ApiInputException();
        await using var connection = await databases.OpenAsync(unitId, ct); await using var transaction = await connection.BeginTransactionAsync(ct);
        await UnitWaitingListDao.LockAsync(connection, ct);
        string professional, specialty; Guid? professionalId, specialtyId, roomId; DateTimeOffset startsAt, endsAt;
        await using (var select = new NpgsqlCommand("SELECT status,starts_at,ends_at,professional,specialty,professional_id,specialty_id,room_id FROM nora_unit.appointments WHERE id=$1 FOR UPDATE", connection))
        {
            select.Parameters.AddWithValue(request.AppointmentId); await using var reader = await select.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct)) throw new ApiNotFoundException();
            if (reader.GetString(0) != "cancelled" || reader.GetFieldValue<DateTimeOffset>(1) <= time.GetUtcNow()) throw new ApiConflictException();
            startsAt = reader.GetFieldValue<DateTimeOffset>(1); endsAt = reader.GetFieldValue<DateTimeOffset>(2); professional = reader.GetString(3); specialty = reader.GetString(4);
            professionalId = reader.IsDBNull(5) ? null : reader.GetGuid(5); specialtyId = reader.IsDBNull(6) ? null : reader.GetGuid(6); roomId = reader.IsDBNull(7) ? null : reader.GetGuid(7);
        }
        await using (var advisory = new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtextextended(lower($1), 62419809))", connection))
        { advisory.Parameters.AddWithValue(professional); await advisory.ExecuteNonQueryAsync(ct); }
        await using (var conflict = new NpgsqlCommand("SELECT EXISTS(SELECT 1 FROM nora_unit.appointments WHERE lower(professional)=lower($1) AND status IN ('scheduled','called','completed','missed') AND starts_at<$3 AND ends_at>$2) OR EXISTS(SELECT 1 FROM nora_unit.lightning_slots WHERE lower(btrim(professional))=lower(btrim($1)) AND status IN ('open','offered') AND starts_at<$3 AND ends_at>$2)", connection))
        { conflict.Parameters.AddWithValue(professional); conflict.Parameters.AddWithValue(startsAt); conflict.Parameters.AddWithValue(endsAt); if (await conflict.ExecuteScalarAsync(ct) is true) throw new ApiConflictException(); }
        var id = Guid.NewGuid();
        await using (var insert = new NpgsqlCommand("INSERT INTO nora_unit.lightning_slots(id,appointment_id,status,created_at,created_by,origin,specialty,specialty_id,professional,professional_id,room_id,starts_at,ends_at) VALUES($1,$2,'open',$3,$4,'staff',$5,$6,$7,$8,$9,$10,$11) ON CONFLICT(appointment_id) DO NOTHING", connection))
        {
            insert.Parameters.AddWithValue(id); insert.Parameters.AddWithValue(request.AppointmentId); insert.Parameters.AddWithValue(time.GetUtcNow()); insert.Parameters.AddWithValue(actorId);
            insert.Parameters.AddWithValue(specialty); insert.Parameters.Add(new NpgsqlParameter { Value = (object?)specialtyId ?? DBNull.Value, NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Uuid });
            insert.Parameters.AddWithValue(professional); insert.Parameters.Add(new NpgsqlParameter { Value = (object?)professionalId ?? DBNull.Value, NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Uuid });
            insert.Parameters.Add(new NpgsqlParameter { Value = (object?)roomId ?? DBNull.Value, NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Uuid });
            insert.Parameters.AddWithValue(startsAt); insert.Parameters.AddWithValue(endsAt);
            if (await insert.ExecuteNonQueryAsync(ct) != 1) throw new ApiConflictException();
        }
        var slot = await ReadOneAsync(connection, id, ct); await transaction.CommitAsync(ct); return (await WithNamesAsync([slot], ct))[0];
    }

    /// <summary>Vaga lida do banco da UBS, com o id (central) de quem decidiu, que vira nome em <see cref="WithNamesAsync"/>.</summary>
    private sealed record SlotRow(UnitLightningSlot Slot, Guid? DecidedBy);

    /// <summary>
    /// Lista da UBS para o OBS (P4, docs/desempenho.md): <b>todas</b> as vagas ativas (<c>open</c>, <c>offered</c>, <c>awaiting_approval</c>) e até <paramref name="historyLimit"/> encerradas (<c>accepted</c>, <c>closed</c>), das mais recentes
    /// para as mais antigas (data de criação, depois id). Com <paramref name="historyBefore"/> (cursor de uma resposta anterior) devolve só as encerradas mais antigas que ele, sem as ativas. <see cref="UnitLightningSlotPage.HistoryCursor"/>
    /// é nulo quando não há mais encerradas. Os itens saem na ordem de criação (a mais antiga primeiro).
    /// </summary>
    public async Task<UnitLightningSlotPage> ListAsync(Guid unitId, int historyLimit, string? historyBefore, CancellationToken ct)
    {
        var before = historyBefore is null ? ((DateTimeOffset, Guid)?)null : DecodeCursor(historyBefore);
        await using var connection = await databases.OpenAsync(unitId, ct);
        // Um só retrato do banco para as ativas e o histórico: sem ele, uma vaga que passa de 'offered' para 'accepted' entre as duas consultas voltaria nas duas listas.
        await using var snapshot = await connection.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, ct);
        var rows = new List<SlotRow>();
        if (before is null)
        {
            await using var active = new NpgsqlCommand("SELECT " + SlotColumns + " " + SlotFrom + "WHERE s.status IN ('open','offered','awaiting_approval') ORDER BY s.created_at,s.id", connection);
            await using var reader = await active.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct)) rows.Add(Read(reader));
        }
        string? cursor = null;
        await using (var history = new NpgsqlCommand("SELECT " + SlotColumns + " " + SlotFrom + "WHERE s.status IN ('accepted','closed') " + (before is null ? "" : "AND (s.created_at,s.id)<($2,$3) ") + "ORDER BY s.created_at DESC,s.id DESC LIMIT $1", connection))
        {
            history.Parameters.AddWithValue(historyLimit + 1);
            if (before is { } point) { history.Parameters.AddWithValue(point.Item1); history.Parameters.AddWithValue(point.Item2); }
            await using var reader = await history.ExecuteReaderAsync(ct);
            var closed = new List<SlotRow>();
            while (await reader.ReadAsync(ct)) closed.Add(Read(reader));
            if (closed.Count > historyLimit) { closed.RemoveAt(historyLimit); cursor = PageCursor.Encode(closed[^1].Slot.CreatedAt, closed[^1].Slot.Id.ToString()); }
            rows.AddRange(closed);
        }
        return new((await WithNamesAsync(rows.OrderBy(row => row.Slot.CreatedAt).ThenBy(row => row.Slot.Id).ToList(), ct)), cursor);
    }

    private static (DateTimeOffset, Guid) DecodeCursor(string cursor)
    {
        var (at, key) = PageCursor.Decode(cursor);
        return Guid.TryParse(key, out var id) ? (at, id) : throw new ApiInputException();
    }

    /// <summary>
    /// Aprova a vaga que aguarda aprovação: ela passa a <c>open</c> e entra na distribuição. Falta 1 h ou menos → fecha (<c>approval-timeout</c>, gravado) e 409 <c>approval-expired</c>; o horário foi ocupado de novo (consulta ativa ou
    /// vaga aberta/ofertada do profissional) → fecha (<c>conflict</c>, gravado) e 409 <c>slot-unavailable</c>. Não aguardando → 409; outra UBS ou inexistente → 404. O chamador redistribui depois.
    /// </summary>
    public async Task<UnitLightningSlot> ApproveAsync(Guid unitId, Guid actorId, Guid slotId, bool confirmed, CancellationToken ct)
    {
        if (slotId == Guid.Empty || actorId == Guid.Empty || !confirmed) throw new ApiInputException();
        await using var connection = await databases.OpenAsync(unitId, ct); await using var transaction = await connection.BeginTransactionAsync(ct);
        await UnitWaitingListDao.LockAsync(connection, ct);
        var (status, professional, startsAt, endsAt) = await LockSlotAsync(connection, slotId, ct);
        if (status != "awaiting_approval") throw new ApiConflictException();
        var now = time.GetUtcNow();
        if (startsAt <= now + UnitLightningDispatcher.ApprovalLead)
        {
            await CloseAsync(connection, slotId, "approval-timeout", ct); await transaction.CommitAsync(ct);
            throw new ApiCodedException(StatusCodes.Status409Conflict, "approval-expired");
        }
        await using (var advisory = new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtextextended(lower($1), 62419809))", connection))
        { advisory.Parameters.AddWithValue(professional); await advisory.ExecuteNonQueryAsync(ct); }
        await using (var conflict = new NpgsqlCommand("SELECT EXISTS(SELECT 1 FROM nora_unit.appointments WHERE lower(btrim(professional))=lower(btrim($1)) AND status IN ('scheduled','called','completed','missed') AND starts_at<$3 AND ends_at>$2) OR EXISTS(SELECT 1 FROM nora_unit.lightning_slots WHERE id<>$4 AND lower(btrim(professional))=lower(btrim($1)) AND status IN ('open','offered') AND starts_at<$3 AND ends_at>$2)", connection))
        {
            conflict.Parameters.AddWithValue(professional); conflict.Parameters.AddWithValue(startsAt); conflict.Parameters.AddWithValue(endsAt); conflict.Parameters.AddWithValue(slotId);
            if (await conflict.ExecuteScalarAsync(ct) is true)
            {
                await CloseAsync(connection, slotId, "conflict", ct); await transaction.CommitAsync(ct);
                throw new ApiCodedException(StatusCodes.Status409Conflict, "slot-unavailable");
            }
        }
        await using (var approve = new NpgsqlCommand("UPDATE nora_unit.lightning_slots SET status='open',decided_by=$2,decided_at=$3 WHERE id=$1", connection))
        { approve.Parameters.AddWithValue(slotId); approve.Parameters.AddWithValue(actorId); approve.Parameters.AddWithValue(now); await approve.ExecuteNonQueryAsync(ct); }
        var slot = await ReadOneAsync(connection, slotId, ct); await transaction.CommitAsync(ct);
        return (await WithNamesAsync([slot], ct))[0];
    }

    /// <summary>Descarta a vaga que aguarda aprovação (<c>closed</c>, <c>discarded</c>). Não aguardando → 409; outra UBS ou inexistente → 404.</summary>
    public async Task<UnitLightningSlot> DiscardAsync(Guid unitId, Guid actorId, Guid slotId, bool confirmed, CancellationToken ct)
    {
        if (slotId == Guid.Empty || actorId == Guid.Empty || !confirmed) throw new ApiInputException();
        await using var connection = await databases.OpenAsync(unitId, ct); await using var transaction = await connection.BeginTransactionAsync(ct);
        await UnitWaitingListDao.LockAsync(connection, ct);
        var (status, _, _, _) = await LockSlotAsync(connection, slotId, ct);
        if (status != "awaiting_approval") throw new ApiConflictException();
        await using (var discard = new NpgsqlCommand("UPDATE nora_unit.lightning_slots SET status='closed',closed_reason='discarded',decided_by=$2,decided_at=$3 WHERE id=$1", connection))
        { discard.Parameters.AddWithValue(slotId); discard.Parameters.AddWithValue(actorId); discard.Parameters.AddWithValue(time.GetUtcNow()); await discard.ExecuteNonQueryAsync(ct); }
        var slot = await ReadOneAsync(connection, slotId, ct); await transaction.CommitAsync(ct);
        return (await WithNamesAsync([slot], ct))[0];
    }

    private static async Task<(string Status, string Professional, DateTimeOffset StartsAt, DateTimeOffset EndsAt)> LockSlotAsync(NpgsqlConnection connection, Guid slotId, CancellationToken ct)
    {
        await using var select = new NpgsqlCommand("SELECT status,professional,starts_at,ends_at FROM nora_unit.lightning_slots WHERE id=$1 FOR UPDATE", connection);
        select.Parameters.AddWithValue(slotId); await using var reader = await select.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) throw new ApiNotFoundException();
        return (reader.GetString(0), reader.GetString(1), reader.GetFieldValue<DateTimeOffset>(2), reader.GetFieldValue<DateTimeOffset>(3));
    }

    private static async Task CloseAsync(NpgsqlConnection connection, Guid slotId, string reason, CancellationToken ct)
    {
        await using var close = new NpgsqlCommand("UPDATE nora_unit.lightning_slots SET status='closed',closed_reason=$2 WHERE id=$1", connection);
        close.Parameters.AddWithValue(slotId); close.Parameters.AddWithValue(reason); await close.ExecuteNonQueryAsync(ct);
    }

    /// <summary>Preenche o nome de quem decidiu (conta central) numa consulta só por lista.</summary>
    private async Task<List<UnitLightningSlot>> WithNamesAsync(List<SlotRow> slots, CancellationToken ct)
    {
        var ids = slots.Where(row => row.DecidedBy is not null).Select(row => row.DecidedBy!.Value).Distinct().ToArray();
        if (ids.Length == 0 || central is null) return slots.Select(row => row.Slot).ToList();
        await using var connection = await central.OpenAsync(ct);
        await using var query = central.Command(connection, "SELECT id,full_name FROM nora.accounts WHERE id=ANY($1)", ids);
        await using var reader = await query.ExecuteReaderAsync(ct);
        var names = new Dictionary<Guid, string>();
        while (await reader.ReadAsync(ct)) names[reader.GetGuid(0)] = reader.GetString(1);
        return slots.Select(row => row.DecidedBy is { } id ? row.Slot with { DecidedByName = names.GetValueOrDefault(id) } : row.Slot).ToList();
    }

    /// <summary>Ofertas de uma vaga em ordem cronológica (o prazo de dez minutos cresce a cada oferta), só com o nome do paciente.</summary>
    public async Task<List<UnitSlotOffer>> ListOffersAsync(Guid unitId, Guid slotId, CancellationToken ct)
    {
        await using var connection = await databases.OpenAsync(unitId, ct);
        await using var query = new NpgsqlCommand("SELECT o.id,p.full_name,o.status,o.expires_at,s.id FROM nora_unit.lightning_slots s LEFT JOIN nora_unit.lightning_offers o ON o.slot_id=s.id LEFT JOIN nora_unit.waiting_list q ON q.id=o.entry_id LEFT JOIN nora_unit.patient_registry p ON p.cpf=q.cpf WHERE s.id=$1 ORDER BY o.expires_at,o.id", connection);
        query.Parameters.AddWithValue(slotId);
        await using var reader = await query.ExecuteReaderAsync(ct); var offers = new List<UnitSlotOffer>(); var slotFound = false;
        while (await reader.ReadAsync(ct))
        {
            slotFound = true;
            if (!reader.IsDBNull(0)) offers.Add(new(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetFieldValue<DateTimeOffset>(3)));
        }
        return slotFound ? offers : throw new ApiNotFoundException();
    }
    /// <param name="pendingOnly">Só as ofertas ainda abertas (pendentes e dentro do prazo): é o que a consulta periódica dos dependentes precisa para notificar, sem trazer o histórico.</param>
    public async Task<List<UnitLightningOffer>> ListForPatientAsync(Guid unitId, string cpf, CancellationToken ct, bool pendingOnly = false)
    {
        await using var connection = await databases.OpenAsync(unitId, ct);
        await using var query = new NpgsqlCommand("SELECT o.id,o.slot_id,s.specialty,s.professional,s.starts_at,s.ends_at,o.expires_at,o.status FROM nora_unit.lightning_offers o JOIN nora_unit.waiting_list q ON q.id=o.entry_id JOIN nora_unit.lightning_slots s ON s.id=o.slot_id WHERE q.cpf=$1 AND (NOT $2 OR (o.status='pending' AND o.expires_at>$3)) ORDER BY o.expires_at,o.id", connection);
        query.Parameters.AddWithValue(cpf); query.Parameters.AddWithValue(pendingOnly); query.Parameters.AddWithValue(time.GetUtcNow()); await using var reader = await query.ExecuteReaderAsync(ct); var offers = new List<UnitLightningOffer>();
        while (await reader.ReadAsync(ct)) offers.Add(new(reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), reader.GetString(3), reader.GetFieldValue<DateTimeOffset>(4), reader.GetFieldValue<DateTimeOffset>(5), reader.GetFieldValue<DateTimeOffset>(6), reader.GetString(7)));
        return offers;
    }

    /// <summary>Uma linha da consulta agregada de ofertas pendentes: o CPF, o nome do cadastro ativo e a oferta.</summary>
    internal sealed record PendingOfferRow(string Cpf, string Name, UnitLightningOffer Offer);

    /// <summary>
    /// Ofertas ainda abertas (pendentes e dentro do prazo) dos CPFs informados, numa única consulta: só entram CPFs com cadastro de referência ativo na UBS, e o nome já vem junto. Ordem por prazo da oferta.
    /// </summary>
    internal async Task<List<PendingOfferRow>> ListPendingForCpfsAsync(Guid unitId, string[] cpfs, CancellationToken ct)
    {
        await using var connection = await databases.OpenAsync(unitId, ct);
        await using var query = new NpgsqlCommand("SELECT q.cpf,p.full_name,o.id,o.slot_id,s.specialty,s.professional,s.starts_at,s.ends_at,o.expires_at,o.status FROM nora_unit.lightning_offers o JOIN nora_unit.waiting_list q ON q.id=o.entry_id JOIN nora_unit.patient_registry p ON p.cpf=q.cpf AND p.is_active JOIN nora_unit.lightning_slots s ON s.id=o.slot_id WHERE q.cpf=ANY($1) AND o.status='pending' AND o.expires_at>$2 ORDER BY o.expires_at,o.id", connection);
        query.Parameters.AddWithValue(cpfs); query.Parameters.AddWithValue(time.GetUtcNow());
        await using var reader = await query.ExecuteReaderAsync(ct); var rows = new List<PendingOfferRow>();
        while (await reader.ReadAsync(ct)) rows.Add(new(reader.GetString(0), reader.GetString(1), new(reader.GetGuid(2), reader.GetGuid(3), reader.GetString(4), reader.GetString(5), reader.GetFieldValue<DateTimeOffset>(6), reader.GetFieldValue<DateTimeOffset>(7), reader.GetFieldValue<DateTimeOffset>(8), reader.GetString(9))));
        return rows;
    }

    /// <summary>
    /// Cancela as ofertas pendentes do CPF (o dependente perdeu quem responda por ele): reabre as vagas, marca as ofertas como canceladas e cancela as entradas de fila que estavam 'offered' (com evento de auditoria e,
    /// quando é o responsável quem encerra, o <c>guardian_id</c>). Entradas 'waiting' ficam como estão; sem acesso, o distribuidor as ignora. Devolve quantas ofertas foram canceladas; o chamador redistribui as vagas.
    /// </summary>
    internal async Task<int> CancelPendingOffersAsync(Guid unitId, string cpf, Guid actorId, string source, Guid? guardianId, CancellationToken ct)
    {
        await using var connection = await databases.OpenAsync(unitId, ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await UnitWaitingListDao.LockAsync(connection, ct);
        await using (var reopen = new NpgsqlCommand("UPDATE nora_unit.lightning_slots SET status='open' WHERE id IN (SELECT o.slot_id FROM nora_unit.lightning_offers o JOIN nora_unit.waiting_list q ON q.id=o.entry_id WHERE q.cpf=$1 AND o.status='pending')", connection))
        { reopen.Parameters.AddWithValue(cpf); await reopen.ExecuteNonQueryAsync(ct); }
        int cancelled;
        await using (var offers = new NpgsqlCommand("UPDATE nora_unit.lightning_offers SET status='cancelled' WHERE status='pending' AND entry_id IN (SELECT id FROM nora_unit.waiting_list WHERE cpf=$1)", connection))
        { offers.Parameters.AddWithValue(cpf); cancelled = await offers.ExecuteNonQueryAsync(ct); }
        if (cancelled > 0)
            await using (var queue = new NpgsqlCommand("WITH changed AS (UPDATE nora_unit.waiting_list SET status='cancelled' WHERE cpf=$1 AND status='offered' RETURNING id) INSERT INTO nora_unit.waiting_list_events(entry_id,actor_id,status,occurred_at,source,guardian_id) SELECT id,$2,'cancelled',$3,$4,$5 FROM changed", connection))
            {
                queue.Parameters.AddWithValue(cpf); queue.Parameters.AddWithValue(actorId); queue.Parameters.AddWithValue(time.GetUtcNow()); queue.Parameters.AddWithValue(source); queue.Parameters.Add(new NpgsqlParameter { Value = (object?)guardianId ?? DBNull.Value });
                await queue.ExecuteNonQueryAsync(ct);
            }
        await transaction.CommitAsync(ct);
        return cancelled;
    }

    /// <param name="beforeInsert">Chamada no aceite, depois das conferências da própria UBS e antes de gravar a consulta, com o intervalo dela: confere consulta da mesma pessoa em outra UBS (409 <c>other-unit-overlap</c> desfaz tudo e a oferta continua pendente).</param>
    public async Task<UnitLightningResponseResult> RespondAsync(Guid unitId, Guid actorId, string cpf, Guid offerId, bool accept, CancellationToken ct, Guid? guardianId = null, Func<DateTimeOffset, DateTimeOffset, Task>? beforeInsert = null)
    {
        if (unitId == Guid.Empty || actorId == Guid.Empty || offerId == Guid.Empty || !AccountService.ValidCpf(cpf)) throw new ApiInputException();
        await using var connection = await databases.OpenAsync(unitId, ct); await using var transaction = await connection.BeginTransactionAsync(ct);
        await UnitWaitingListDao.LockAsync(connection, ct);
        Guid slotId; long entryId; string specialty; string professional; Guid? professionalId, specialtyId, roomId; DateTimeOffset start; DateTimeOffset end; DateTimeOffset expires;
        await using (var query = new NpgsqlCommand("SELECT o.slot_id,o.entry_id,s.specialty,s.professional,s.starts_at,s.ends_at,o.expires_at,o.status,s.status,q.status,s.professional_id,s.specialty_id,s.room_id FROM nora_unit.lightning_offers o JOIN nora_unit.waiting_list q ON q.id=o.entry_id JOIN nora_unit.lightning_slots s ON s.id=o.slot_id WHERE o.id=$1 AND q.cpf=$2 FOR UPDATE OF o,s,q", connection))
        {
            query.Parameters.AddWithValue(offerId); query.Parameters.AddWithValue(cpf);
            await using var reader = await query.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct)) throw new ApiNotFoundException();
            if (reader.GetString(7) != "pending" || reader.GetString(8) != "offered" || reader.GetString(9) != "offered") throw new ApiConflictException();
            slotId = reader.GetGuid(0); entryId = reader.GetInt64(1); specialty = reader.GetString(2); professional = reader.GetString(3);
            start = reader.GetFieldValue<DateTimeOffset>(4); end = reader.GetFieldValue<DateTimeOffset>(5); expires = reader.GetFieldValue<DateTimeOffset>(6);
            professionalId = reader.IsDBNull(10) ? null : reader.GetGuid(10); specialtyId = reader.IsDBNull(11) ? null : reader.GetGuid(11); roomId = reader.IsDBNull(12) ? null : reader.GetGuid(12);
        }
        var now = time.GetUtcNow();
        if (expires <= now || start <= now)
        {
            await SetOfferResultAsync(connection, offerId, slotId, entryId, actorId, "expired", start <= now ? "closed" : "open", now, "system", ct, closedReason: start <= now ? "too-late" : null);
            await transaction.CommitAsync(ct); throw new ApiConflictException();
        }
        Guid? appointmentId = null;
        if (accept)
        {
            await using (var professionalLock = new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtextextended(lower($1), 62419809))", connection))
            { professionalLock.Parameters.AddWithValue(professional); await professionalLock.ExecuteNonQueryAsync(ct); }
            await using (var patientLock = new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtextextended($1, 62419810))", connection))
            { patientLock.Parameters.AddWithValue(cpf); await patientLock.ExecuteNonQueryAsync(ct); }
            await using (var overlap = new NpgsqlCommand("SELECT EXISTS(SELECT 1 FROM nora_unit.appointments WHERE (lower(professional)=lower($1) OR cpf=$2) AND status IN ('scheduled','called','completed','missed') AND starts_at<$4 AND ends_at>$3)", connection))
            { overlap.Parameters.AddWithValue(professional); overlap.Parameters.AddWithValue(cpf); overlap.Parameters.AddWithValue(start); overlap.Parameters.AddWithValue(end); if (await overlap.ExecuteScalarAsync(ct) is true) throw new ApiConflictException(); }
            if (beforeInsert is not null) await beforeInsert(start, end);
            appointmentId = Guid.NewGuid();
            await using (var insert = new NpgsqlCommand("INSERT INTO nora_unit.appointments(id,cpf,specialty,professional,starts_at,ends_at,status,created_by,created_at,booked_by,professional_id,specialty_id,room_id) VALUES($1,$2,$3,$4,$5,$6,'scheduled',$7,$8,'lightning',$9,$10,$11)", connection))
            {
                foreach (var value in new object[] { appointmentId.Value, cpf, specialty, professional, start, end, actorId, now }) insert.Parameters.AddWithValue(value);
                foreach (var value in new[] { professionalId, specialtyId, roomId }) insert.Parameters.Add(new NpgsqlParameter { Value = (object?)value ?? DBNull.Value, NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Uuid });
                await insert.ExecuteNonQueryAsync(ct);
            }
            await using (var audit = new NpgsqlCommand("INSERT INTO nora_unit.appointment_events(appointment_id,actor_id,status,occurred_at,source,guardian_id) VALUES($1,$2,'scheduled',$3,'patient',$4)", connection))
            { audit.Parameters.AddWithValue(appointmentId.Value); audit.Parameters.AddWithValue(actorId); audit.Parameters.AddWithValue(now); audit.Parameters.Add(new NpgsqlParameter { Value = (object?)guardianId ?? DBNull.Value }); await audit.ExecuteNonQueryAsync(ct); }
        }
        await SetOfferResultAsync(connection, offerId, slotId, entryId, actorId, accept ? "accepted" : "declined", accept ? "accepted" : "open", now, "patient", ct, guardianId);
        await transaction.CommitAsync(ct); return new(accept, appointmentId);
    }

    private static async Task SetOfferResultAsync(NpgsqlConnection connection, Guid offerId, Guid slotId, long entryId, Guid actorId, string offerStatus, string slotStatus, DateTimeOffset now, string source, CancellationToken ct, Guid? guardianId = null, string? closedReason = null)
    {
        await using (var update = new NpgsqlCommand("UPDATE nora_unit.lightning_offers SET status=$2 WHERE id=$1", connection))
        { update.Parameters.AddWithValue(offerId); update.Parameters.AddWithValue(offerStatus); await update.ExecuteNonQueryAsync(ct); }
        await using (var update = new NpgsqlCommand("UPDATE nora_unit.lightning_slots SET status=$2,closed_reason=$3 WHERE id=$1", connection))
        { update.Parameters.AddWithValue(slotId); update.Parameters.AddWithValue(slotStatus); update.Parameters.Add(new NpgsqlParameter { Value = (object?)closedReason ?? DBNull.Value, NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Text }); await update.ExecuteNonQueryAsync(ct); }
        // R-030: só o aceite tira a pessoa da fila. Recusar ou deixar vencer devolve a entrada a 'waiting' com o entered_at original (mesma posição); a mesma vaga não é oferecida de novo a ela (LightningEligibility).
        var entryStatus = offerStatus == "accepted" ? "completed" : "waiting";
        await using (var update = new NpgsqlCommand("UPDATE nora_unit.waiting_list SET status=$2 WHERE id=$1", connection))
        { update.Parameters.AddWithValue(entryId); update.Parameters.AddWithValue(entryStatus); await update.ExecuteNonQueryAsync(ct); }
        await using (var audit = new NpgsqlCommand("INSERT INTO nora_unit.waiting_list_events(entry_id,actor_id,status,occurred_at,source,guardian_id) VALUES($1,$2,$6,$3,$4,$5)", connection))
        { audit.Parameters.AddWithValue(entryId); audit.Parameters.AddWithValue(actorId); audit.Parameters.AddWithValue(now); audit.Parameters.AddWithValue(source); audit.Parameters.Add(new NpgsqlParameter { Value = (object?)guardianId ?? DBNull.Value }); audit.Parameters.AddWithValue(entryStatus); await audit.ExecuteNonQueryAsync(ct); }
    }

    /// <summary>A vaga como a equipe a enxerga (sem o nome de quem decidiu), na transação do chamador.</summary>
    internal static async Task<UnitLightningSlot> ReadSlotAsync(NpgsqlConnection connection, Guid id, CancellationToken ct) => (await ReadOneAsync(connection, id, ct)).Slot;

    private static async Task<SlotRow> ReadOneAsync(NpgsqlConnection connection, Guid id, CancellationToken ct)
    {
        await using var query = new NpgsqlCommand("SELECT " + SlotColumns + " " + SlotFrom + "WHERE s.id=$1", connection);
        query.Parameters.AddWithValue(id); await using var reader = await query.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) throw new ApiNotFoundException(); return Read(reader);
    }

    private static SlotRow Read(NpgsqlDataReader r)
    {
        var status = r.GetString(6); var startsAt = r.GetFieldValue<DateTimeOffset>(4);
        var cancelledBy = r.IsDBNull(17) ? null : r.GetString(17);
        UnitSlotSource? source = r.IsDBNull(1) ? null : new(r.GetGuid(1), r.GetString(13), r.GetFieldValue<DateTimeOffset>(14), r.GetFieldValue<DateTimeOffset>(15),
            r.IsDBNull(16) ? null : r.GetFieldValue<DateTimeOffset>(16), cancelledBy is "patient" or "staff" or "system" ? cancelledBy : null);
        return new(new(r.GetGuid(0), r.IsDBNull(1) ? null : r.GetGuid(1), r.GetString(2), r.GetString(3), startsAt, r.GetFieldValue<DateTimeOffset>(5), status, r.GetFieldValue<DateTimeOffset>(7),
            r.GetString(8), status == "awaiting_approval" ? startsAt - UnitLightningDispatcher.ApprovalLead : null, r.IsDBNull(10) ? null : r.GetFieldValue<DateTimeOffset>(10),
            null, r.IsDBNull(11) ? null : r.GetString(11), source), r.IsDBNull(9) ? null : r.GetGuid(9));
    }
}
