using Npgsql;
using Nora.Api.DAOs;
using Nora.Api.Database;
using Nora.Api.Models;

namespace Nora.Api.Services;

/// <summary>
/// "Chamar próximo" da Fila do OBS: a atendente escolhe um horário livre da grade e o botão oferece esse horário, na hora, ao primeiro elegível da fila daquela especialidade (furando a espera automática).
/// A vaga nasce <c>offered</c> com <c>origin='call-next'</c> e a oferta de 10 minutos; se a pessoa recusar ou deixar vencer, a vaga fica <c>open</c> e segue na distribuição automática para os próximos da fila.
/// Concorrência: uma transação central (concessão, conta e sessão, e a lista de elegíveis em <c>FOR SHARE</c>) e uma da UBS (lock da fila, lock do profissional, recálculo do atendimento, entrada <c>FOR UPDATE</c>);
/// o commit da UBS vem antes do central. Dois cliques no mesmo horário: o segundo vê o horário segurado (409 <c>slot-unavailable</c>). A conferência de consulta em outra UBS fica no aceite.
/// </summary>
public sealed class CallNextService(DatabaseHelper central, UnitDatabaseRouter databases, UnitAccessService access, SpecialtyDao specialties, SchedulingService scheduling, TimeProvider time)
{
    /// <summary>Prazo de resposta da oferta, e também a antecedência mínima do horário (a oferta precisa de 10 minutos completos antes dele).</summary>
    public static readonly TimeSpan OfferLifetime = TimeSpan.FromMinutes(10);

    private async Task<Specialty> ActiveSpecialtyAsync(Guid? specialtyId, CancellationToken ct)
    {
        if (specialtyId is not { } id || id == Guid.Empty) throw new ApiInputException();
        return (await specialties.FindAsync([id], ct)).GetValueOrDefault(id) is { IsActive: true } specialty ? specialty : throw new ApiInputException();
    }

    /// <summary>
    /// Horário livre da grade que o profissional tem exatamente em <paramref name="start"/> para a especialidade (a mesma conferência do <see cref="CallAsync"/>: ocupado, bloqueado ou segurado por vaga não vale); nulo se não existe.
    /// </summary>
    private async Task<GeneratedSlot?> FreeSlotAsync(NpgsqlConnection unit, Specialty specialty, Guid professionalId, DateTimeOffset start, CancellationToken ct)
    {
        var data = await SchedulingDao.LoadAsync(unit, start, start.AddSeconds(1), professionalId, ct);
        foreach (var candidate in scheduling.Engine.Generate(data.Templates.Where(template => template.SpecialtyId == specialty.Id).ToList(), start, start.AddSeconds(1)))
            if (candidate.Start == start.UtcTicks && SlotEngine.StateOf(data, candidate) == SlotState.Free) return candidate;
        return null;
    }

    /// <summary>
    /// Prévia do primeiro da fila que pode receber. Sem horário, ignora o horário. Com <paramref name="startsAt"/> e <paramref name="professionalId"/> (o horário que a atendente escolheu) usa o mesmo critério da chamada: pula quem já tem
    /// consulta na UBS que cruze esse horário. Se o horário já não está livre na grade, devolve a prévia sem horário (a chamada responderia 409 <c>slot-unavailable</c>). Especialidade inexistente ou inativa → 400.
    /// </summary>
    public async Task<CallNextPreview> PreviewAsync(SessionInfo session, Guid unitId, Guid? specialtyId, DateTimeOffset? startsAt, Guid? professionalId, CancellationToken ct)
    {
        var specialty = await ActiveSpecialtyAsync(specialtyId, ct);
        if (startsAt is not null != (professionalId is not null) || professionalId == Guid.Empty) throw new ApiInputException();
        return await access.ExecuteAsync(session, unitId, async connection =>
        {
            var eligible = await LightningEligibility.ReadAsync(central, connection, unitId, ct);
            await using var unit = await databases.OpenAsync(unitId, ct);
            DateTimeOffset? start = null, end = null;
            if (startsAt is { } requested && professionalId is { } professional && await FreeSlotAsync(unit, specialty, professional, requested.ToUniversalTime(), ct) is { } slot)
            { start = requested.ToUniversalTime(); end = new DateTimeOffset(slot.End, TimeSpan.Zero); }
            return new CallNextPreview(await LightningEligibility.PreviewAsync(unit, specialty.Name, eligible, ct, start, end) is { } next ? new(next.EntryId, next.Name, next.EnteredAt) : null);
        }, ct, read: true);
    }

    public async Task<CallNextResult> CallAsync(SessionInfo session, Guid unitId, CallNextRequest request, CancellationToken ct)
    {
        if (request.ProfessionalId is not { } professionalId || professionalId == Guid.Empty || request.StartsAt is not { } requested || !request.Confirmed) throw new ApiInputException();
        var specialty = await ActiveSpecialtyAsync(request.SpecialtyId, ct);
        var start = requested.ToUniversalTime();
        return await access.ExecuteAsync(session, unitId, async connection =>
        {
            var now = time.GetUtcNow();
            if (start < now + OfferLifetime) throw new ApiCodedException(StatusCodes.Status409Conflict, "call-next-too-late");
            var eligible = await LightningEligibility.ReadAsync(central, connection, unitId, ct);
            await using var unit = await databases.OpenAsync(unitId, ct);
            await using var transaction = await unit.BeginTransactionAsync(ct);
            await UnitWaitingListDao.LockAsync(unit, ct);
            string name;
            await using (var professional = new NpgsqlCommand("SELECT p.full_name,p.is_active,EXISTS(SELECT 1 FROM nora_unit.professional_specialties s WHERE s.professional_id=p.id AND s.specialty_id=$2) FROM nora_unit.professionals p WHERE p.id=$1", unit))
            {
                professional.Parameters.AddWithValue(professionalId); professional.Parameters.AddWithValue(specialty.Id);
                await using var reader = await professional.ExecuteReaderAsync(ct);
                if (!await reader.ReadAsync(ct)) throw new ApiInputException();
                if (!reader.GetBoolean(1) || !reader.GetBoolean(2)) throw new ApiCodedException(StatusCodes.Status409Conflict, "slot-unavailable");
                name = reader.GetString(0);
            }
            await using (var advisory = new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtextextended(lower($1), 62419809))", unit))
            { advisory.Parameters.AddWithValue(name); await advisory.ExecuteNonQueryAsync(ct); }
            // O atendimento escolhido tem de ser exatamente um horário livre da grade agora (ocupado, bloqueado ou segurado por vaga → slot-unavailable).
            if (await FreeSlotAsync(unit, specialty, professionalId, start, ct) is not { } slot) throw new ApiCodedException(StatusCodes.Status409Conflict, "slot-unavailable");
            var end = new DateTimeOffset(slot.End, TimeSpan.Zero);
            if (await LightningEligibility.PickAsync(unit, specialty.Name, eligible, start, end, ct) is not { } entryId) throw new ApiCodedException(StatusCodes.Status409Conflict, "queue-empty");
            string patientName;
            await using (var read = new NpgsqlCommand("SELECT p.full_name FROM nora_unit.waiting_list q JOIN nora_unit.patient_registry p ON p.cpf=q.cpf WHERE q.id=$1", unit))
            { read.Parameters.AddWithValue(entryId); patientName = (string)(await read.ExecuteScalarAsync(ct))!; }
            var slotId = Guid.NewGuid(); var offerId = Guid.NewGuid(); var expires = now + OfferLifetime;
            await using (var insert = new NpgsqlCommand("INSERT INTO nora_unit.lightning_slots(id,appointment_id,status,created_at,created_by,origin,specialty,specialty_id,professional,professional_id,room_id,starts_at,ends_at) VALUES($1,NULL,'offered',$2,$3,'call-next',$4,$5,$6,$7,$8,$9,$10)", unit))
            {
                insert.Parameters.AddWithValue(slotId); insert.Parameters.AddWithValue(now); insert.Parameters.AddWithValue(session.User.Id); insert.Parameters.AddWithValue(specialty.Name); insert.Parameters.AddWithValue(specialty.Id);
                insert.Parameters.AddWithValue(name); insert.Parameters.AddWithValue(professionalId); insert.Parameters.AddWithValue(slot.Template.RoomId); insert.Parameters.AddWithValue(start); insert.Parameters.AddWithValue(end);
                await insert.ExecuteNonQueryAsync(ct);
            }
            await using (var offer = new NpgsqlCommand("INSERT INTO nora_unit.lightning_offers(id,slot_id,entry_id,expires_at,status) VALUES($1,$2,$3,$4,'pending')", unit))
            { offer.Parameters.AddWithValue(offerId); offer.Parameters.AddWithValue(slotId); offer.Parameters.AddWithValue(entryId); offer.Parameters.AddWithValue(expires); await offer.ExecuteNonQueryAsync(ct); }
            await using (var queue = new NpgsqlCommand("UPDATE nora_unit.waiting_list SET status='offered' WHERE id=$1", unit))
            { queue.Parameters.AddWithValue(entryId); await queue.ExecuteNonQueryAsync(ct); }
            await using (var audit = new NpgsqlCommand("INSERT INTO nora_unit.waiting_list_events(entry_id,actor_id,status,occurred_at,source) VALUES($1,$2,'offered',$3,'staff')", unit))
            { audit.Parameters.AddWithValue(entryId); audit.Parameters.AddWithValue(session.User.Id); audit.Parameters.AddWithValue(now); await audit.ExecuteNonQueryAsync(ct); }
            var created = await UnitLightningDao.ReadSlotAsync(unit, slotId, ct);
            await transaction.CommitAsync(ct);
            return new CallNextResult(created, new(offerId, patientName, expires));
        }, ct);
    }
}
