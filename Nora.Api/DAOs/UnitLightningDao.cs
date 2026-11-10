using Npgsql;
using Nora.Api.Database;
using Nora.Api.Models;
using Nora.Api.Services;

namespace Nora.Api.DAOs;

/// <summary>Creates a unit-local slot from a cancelled appointment; distribution is implemented separately.</summary>
public sealed class UnitLightningDao(UnitDatabaseRouter databases, TimeProvider time)
{
    public async Task<UnitLightningSlot> CreateAsync(Guid unitId, Guid actorId, CreateUnitLightningSlotRequest request, CancellationToken ct)
    {
        if (unitId == Guid.Empty || actorId == Guid.Empty || request.AppointmentId == Guid.Empty || !request.Confirmed) throw new ApiInputException();
        await using var connection = await databases.OpenAsync(unitId, ct); await using var transaction = await connection.BeginTransactionAsync(ct);
        await UnitWaitingListDao.LockAsync(connection, ct);
        string professional; DateTimeOffset startsAt; DateTimeOffset endsAt;
        await using (var select = new NpgsqlCommand("SELECT status,starts_at,ends_at,professional FROM nora_unit.appointments WHERE id=$1 FOR UPDATE", connection))
        {
            select.Parameters.AddWithValue(request.AppointmentId); await using var reader = await select.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct)) throw new ApiNotFoundException();
            if (reader.GetString(0) != "cancelled" || reader.GetFieldValue<DateTimeOffset>(1) <= time.GetUtcNow()) throw new ApiConflictException();
            startsAt = reader.GetFieldValue<DateTimeOffset>(1); endsAt = reader.GetFieldValue<DateTimeOffset>(2); professional = reader.GetString(3);
        }
        await using (var advisory = new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtextextended(lower($1), 62419809))", connection))
        { advisory.Parameters.AddWithValue(professional); await advisory.ExecuteNonQueryAsync(ct); }
        await using (var conflict = new NpgsqlCommand("SELECT EXISTS(SELECT 1 FROM nora_unit.appointments WHERE lower(professional)=lower($1) AND status IN ('scheduled','called','completed','missed') AND starts_at<$3 AND ends_at>$2) OR EXISTS(SELECT 1 FROM nora_unit.lightning_slots s JOIN nora_unit.appointments a ON a.id=s.appointment_id WHERE lower(a.professional)=lower($1) AND s.status IN ('open','offered') AND a.starts_at<$3 AND a.ends_at>$2)", connection))
        { conflict.Parameters.AddWithValue(professional); conflict.Parameters.AddWithValue(startsAt); conflict.Parameters.AddWithValue(endsAt); if (await conflict.ExecuteScalarAsync(ct) is true) throw new ApiConflictException(); }
        var id = Guid.NewGuid();
        await using (var insert = new NpgsqlCommand("INSERT INTO nora_unit.lightning_slots(id,appointment_id,status,created_at,created_by) VALUES($1,$2,'open',$3,$4) ON CONFLICT(appointment_id) DO NOTHING", connection))
        {
            insert.Parameters.AddWithValue(id); insert.Parameters.AddWithValue(request.AppointmentId); insert.Parameters.AddWithValue(time.GetUtcNow()); insert.Parameters.AddWithValue(actorId);
            if (await insert.ExecuteNonQueryAsync(ct) != 1) throw new ApiConflictException();
        }
        var slot = await ReadOneAsync(connection, id, ct); await transaction.CommitAsync(ct); return slot;
    }

    public async Task<List<UnitLightningSlot>> ListAsync(Guid unitId, CancellationToken ct)
    {
        await using var connection = await databases.OpenAsync(unitId, ct);
        await using var query = new NpgsqlCommand("SELECT s.id,s.appointment_id,a.specialty,a.professional,a.starts_at,a.ends_at,s.status,s.created_at FROM nora_unit.lightning_slots s JOIN nora_unit.appointments a ON a.id=s.appointment_id ORDER BY s.created_at,s.id", connection);
        await using var reader = await query.ExecuteReaderAsync(ct); var result = new List<UnitLightningSlot>();
        while (await reader.ReadAsync(ct)) result.Add(Read(reader)); return result;
    }
    public async Task<List<UnitLightningOffer>> ListForPatientAsync(Guid unitId, string cpf, CancellationToken ct)
    {
        await using var connection = await databases.OpenAsync(unitId, ct);
        await using var query = new NpgsqlCommand("SELECT o.id,o.slot_id,a.specialty,a.professional,a.starts_at,a.ends_at,o.expires_at,o.status FROM nora_unit.lightning_offers o JOIN nora_unit.waiting_list q ON q.id=o.entry_id JOIN nora_unit.lightning_slots s ON s.id=o.slot_id JOIN nora_unit.appointments a ON a.id=s.appointment_id WHERE q.cpf=$1 ORDER BY o.expires_at,o.id", connection);
        query.Parameters.AddWithValue(cpf); await using var reader = await query.ExecuteReaderAsync(ct); var offers = new List<UnitLightningOffer>();
        while (await reader.ReadAsync(ct)) offers.Add(new(reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), reader.GetString(3), reader.GetFieldValue<DateTimeOffset>(4), reader.GetFieldValue<DateTimeOffset>(5), reader.GetFieldValue<DateTimeOffset>(6), reader.GetString(7)));
        return offers;
    }

    public async Task<UnitLightningResponseResult> RespondAsync(Guid unitId, Guid actorId, string cpf, Guid offerId, bool accept, CancellationToken ct)
    {
        if (unitId == Guid.Empty || actorId == Guid.Empty || offerId == Guid.Empty || !AccountService.ValidCpf(cpf)) throw new ApiInputException();
        await using var connection = await databases.OpenAsync(unitId, ct); await using var transaction = await connection.BeginTransactionAsync(ct);
        await UnitWaitingListDao.LockAsync(connection, ct);
        Guid slotId; long entryId; string specialty; string professional; DateTimeOffset start; DateTimeOffset end; DateTimeOffset expires;
        await using (var query = new NpgsqlCommand("SELECT o.slot_id,o.entry_id,a.specialty,a.professional,a.starts_at,a.ends_at,o.expires_at,o.status,s.status,q.status FROM nora_unit.lightning_offers o JOIN nora_unit.waiting_list q ON q.id=o.entry_id JOIN nora_unit.lightning_slots s ON s.id=o.slot_id JOIN nora_unit.appointments a ON a.id=s.appointment_id WHERE o.id=$1 AND q.cpf=$2 FOR UPDATE OF o,s,q", connection))
        {
            query.Parameters.AddWithValue(offerId); query.Parameters.AddWithValue(cpf);
            await using var reader = await query.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct)) throw new ApiNotFoundException();
            if (reader.GetString(7) != "pending" || reader.GetString(8) != "offered" || reader.GetString(9) != "offered") throw new ApiConflictException();
            slotId = reader.GetGuid(0); entryId = reader.GetInt64(1); specialty = reader.GetString(2); professional = reader.GetString(3);
            start = reader.GetFieldValue<DateTimeOffset>(4); end = reader.GetFieldValue<DateTimeOffset>(5); expires = reader.GetFieldValue<DateTimeOffset>(6);
        }
        var now = time.GetUtcNow();
        if (expires <= now || start <= now)
        {
            await SetOfferResultAsync(connection, offerId, slotId, entryId, actorId, "expired", start <= now ? "closed" : "open", now, "system", ct);
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
            appointmentId = Guid.NewGuid();
            await using (var insert = new NpgsqlCommand("INSERT INTO nora_unit.appointments(id,cpf,specialty,professional,starts_at,ends_at,status,created_by,created_at) VALUES($1,$2,$3,$4,$5,$6,'scheduled',$7,$8)", connection))
            { foreach (var value in new object[] { appointmentId.Value, cpf, specialty, professional, start, end, actorId, now }) insert.Parameters.AddWithValue(value); await insert.ExecuteNonQueryAsync(ct); }
            await using (var audit = new NpgsqlCommand("INSERT INTO nora_unit.appointment_events(appointment_id,actor_id,status,occurred_at) VALUES($1,$2,'scheduled',$3)", connection))
            { audit.Parameters.AddWithValue(appointmentId.Value); audit.Parameters.AddWithValue(actorId); audit.Parameters.AddWithValue(now); await audit.ExecuteNonQueryAsync(ct); }
        }
        await SetOfferResultAsync(connection, offerId, slotId, entryId, actorId, accept ? "accepted" : "declined", accept ? "accepted" : "open", now, "patient", ct);
        await transaction.CommitAsync(ct); return new(accept, appointmentId);
    }

    private static async Task SetOfferResultAsync(NpgsqlConnection connection, Guid offerId, Guid slotId, long entryId, Guid actorId, string offerStatus, string slotStatus, DateTimeOffset now, string source, CancellationToken ct)
    {
        await using (var update = new NpgsqlCommand("UPDATE nora_unit.lightning_offers SET status=$2 WHERE id=$1", connection))
        { update.Parameters.AddWithValue(offerId); update.Parameters.AddWithValue(offerStatus); await update.ExecuteNonQueryAsync(ct); }
        await using (var update = new NpgsqlCommand("UPDATE nora_unit.lightning_slots SET status=$2 WHERE id=$1", connection))
        { update.Parameters.AddWithValue(slotId); update.Parameters.AddWithValue(slotStatus); await update.ExecuteNonQueryAsync(ct); }
        await using (var update = new NpgsqlCommand("UPDATE nora_unit.waiting_list SET status='completed' WHERE id=$1", connection))
        { update.Parameters.AddWithValue(entryId); await update.ExecuteNonQueryAsync(ct); }
        await using (var audit = new NpgsqlCommand("INSERT INTO nora_unit.waiting_list_events(entry_id,actor_id,status,occurred_at,source) VALUES($1,$2,'completed',$3,$4)", connection))
        { audit.Parameters.AddWithValue(entryId); audit.Parameters.AddWithValue(actorId); audit.Parameters.AddWithValue(now); audit.Parameters.AddWithValue(source); await audit.ExecuteNonQueryAsync(ct); }
    }
    private static async Task<UnitLightningSlot> ReadOneAsync(NpgsqlConnection connection, Guid id, CancellationToken ct)
    {
        await using var query = new NpgsqlCommand("SELECT s.id,s.appointment_id,a.specialty,a.professional,a.starts_at,a.ends_at,s.status,s.created_at FROM nora_unit.lightning_slots s JOIN nora_unit.appointments a ON a.id=s.appointment_id WHERE s.id=$1", connection);
        query.Parameters.AddWithValue(id); await using var reader = await query.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) throw new ApiNotFoundException(); return Read(reader);
    }
    private static UnitLightningSlot Read(NpgsqlDataReader r) => new(r.GetGuid(0), r.GetGuid(1), r.GetString(2), r.GetString(3), r.GetFieldValue<DateTimeOffset>(4), r.GetFieldValue<DateTimeOffset>(5), r.GetString(6), r.GetFieldValue<DateTimeOffset>(7));
}
