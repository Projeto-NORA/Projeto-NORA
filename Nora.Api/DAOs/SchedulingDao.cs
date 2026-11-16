using System.Globalization;
using Npgsql;
using Nora.Api.Database;
using Nora.Api.Models;
using Nora.Api.Services;

namespace Nora.Api.DAOs;

/// <summary>Consulta e dado validado de uma linha da grade (já convertido pelo serviço).</summary>
public sealed record ScheduleTemplateDraft(Guid ProfessionalId, int Weekday, TimeOnly Start, TimeOnly End, int SlotMinutes, Guid RoomId, Guid SpecialtyId, DateOnly? ValidFrom, DateOnly? ValidUntil);

public sealed record WaitingGroup(string Key, string Name, long Count, DateTimeOffset Oldest);

/// <summary>
/// Agenda do banco de uma UBS: profissionais, consultórios, grade, bloqueios e a marcação a partir do catálogo. O chamador já autorizou o acesso
/// à unidade. Mudanças de grade serializam sob um lock de transação próprio; a marcação usa os mesmos locks de profissional (62419809) e de paciente
/// (62419810) da agenda antiga, então os dois caminhos nunca ocupam o mesmo horário ao mesmo tempo.
/// </summary>
public sealed class SchedulingDao(UnitDatabaseRouter databases, TimeProvider time)
{
    private const int GridLockKey = 62419812;
    /// <summary>Maior duração de atendimento da grade; a carga de consultas e bloqueios vai até este tanto depois do fim da janela.</summary>
    private const int MaxSlotMinutes = 240;
    private const string ProfessionalSelect = "SELECT p.id,p.full_name,p.council_acronym,p.council_number,p.council_state,p.is_active,COALESCE(array_agg(s.specialty_id ORDER BY s.specialty_id) FILTER (WHERE s.specialty_id IS NOT NULL), ARRAY[]::uuid[]) FROM nora_unit.professionals p LEFT JOIN nora_unit.professional_specialties s ON s.professional_id=p.id";
    private const string TemplateSelect = "SELECT t.id,t.professional_id,t.weekday,t.start_time,t.end_time,t.slot_minutes,t.room_id,t.specialty_id,t.valid_from,t.valid_until FROM nora_unit.schedule_templates t";

    private static NpgsqlCommand Cmd(NpgsqlConnection connection, string sql, params object?[] values)
    {
        var command = new NpgsqlCommand(sql, connection);
        foreach (var value in values) command.Parameters.Add(new NpgsqlParameter { Value = value ?? DBNull.Value });
        return command;
    }
    private static NpgsqlBatchCommand Statement(string sql, params object?[] values)
    {
        var command = new NpgsqlBatchCommand(sql);
        foreach (var value in values) command.Parameters.Add(new NpgsqlParameter { Value = value ?? DBNull.Value });
        return command;
    }
    private static async Task LockAsync(NpgsqlConnection connection, int key, CancellationToken ct)
    {
        await using var command = Cmd(connection, "SELECT pg_advisory_xact_lock($1)", (long)key);
        await command.ExecuteNonQueryAsync(ct);
    }

    // ---- Profissionais ----

    public async Task<bool> HasActiveProfessionalAsync(Guid unitId, CancellationToken ct)
    {
        await using var connection = await databases.OpenAsync(unitId, ct);
        await using var query = Cmd(connection, "SELECT EXISTS(SELECT 1 FROM nora_unit.professionals WHERE is_active)");
        return await query.ExecuteScalarAsync(ct) is true;
    }

    public async Task<List<Professional>> ListProfessionalsAsync(Guid unitId, CancellationToken ct)
    {
        await using var connection = await databases.OpenAsync(unitId, ct);
        await using var query = Cmd(connection, ProfessionalSelect + " GROUP BY p.id ORDER BY lower(p.full_name),p.id");
        await using var reader = await query.ExecuteReaderAsync(ct);
        var items = new List<Professional>();
        while (await reader.ReadAsync(ct)) items.Add(ReadProfessional(reader));
        return items;
    }

    public async Task<Professional> CreateProfessionalAsync(Guid unitId, ProfessionalRequest request, CancellationToken ct)
    {
        var id = Guid.NewGuid();
        await using var connection = await databases.OpenAsync(unitId, ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await using (var insert = Cmd(connection, "INSERT INTO nora_unit.professionals(id,full_name,council_acronym,council_number,council_state) VALUES($1,$2,$3,$4,$5)", id, request.FullName, request.CouncilAcronym, request.CouncilNumber, request.CouncilState))
            await insert.ExecuteNonQueryAsync(ct);
        await ReplaceSpecialtiesAsync(connection, id, request.SpecialtyIds!, ct);
        var result = await ReadProfessionalAsync(connection, id, ct);
        await transaction.CommitAsync(ct);
        return result;
    }

    public async Task<Professional> UpdateProfessionalAsync(Guid unitId, Guid id, ProfessionalRequest request, CancellationToken ct)
    {
        await using var connection = await databases.OpenAsync(unitId, ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await LockAsync(connection, GridLockKey, ct);
        await using (var update = Cmd(connection, "UPDATE nora_unit.professionals SET full_name=$2,council_acronym=$3,council_number=$4,council_state=$5 WHERE id=$1", id, request.FullName, request.CouncilAcronym, request.CouncilNumber, request.CouncilState))
            if (await update.ExecuteNonQueryAsync(ct) == 0) throw new ApiNotFoundException();
        // Tirar uma especialidade que ainda tem grade deixaria atendimentos fora do que o profissional faz: o OBS remove a grade antes.
        await using (var used = Cmd(connection, "SELECT EXISTS(SELECT 1 FROM nora_unit.schedule_templates WHERE professional_id=$1 AND NOT (specialty_id=ANY($2)))", id, request.SpecialtyIds!.Distinct().ToArray()))
            if (await used.ExecuteScalarAsync(ct) is true) throw new ApiConflictException();
        await ReplaceSpecialtiesAsync(connection, id, request.SpecialtyIds!, ct);
        var result = await ReadProfessionalAsync(connection, id, ct);
        await transaction.CommitAsync(ct);
        return result;
    }

    public async Task<Professional> SetProfessionalActiveAsync(Guid unitId, Guid id, bool isActive, CancellationToken ct)
    {
        await using var connection = await databases.OpenAsync(unitId, ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await using (var update = Cmd(connection, "UPDATE nora_unit.professionals SET is_active=$2 WHERE id=$1", id, isActive))
            if (await update.ExecuteNonQueryAsync(ct) == 0) throw new ApiNotFoundException();
        var result = await ReadProfessionalAsync(connection, id, ct);
        await transaction.CommitAsync(ct);
        return result;
    }

    private static async Task ReplaceSpecialtiesAsync(NpgsqlConnection connection, Guid professionalId, List<Guid> specialtyIds, CancellationToken ct)
    {
        await using (var delete = Cmd(connection, "DELETE FROM nora_unit.professional_specialties WHERE professional_id=$1 AND NOT (specialty_id=ANY($2))", professionalId, specialtyIds.Distinct().ToArray()))
            await delete.ExecuteNonQueryAsync(ct);
        await using var insert = Cmd(connection, "INSERT INTO nora_unit.professional_specialties(professional_id,specialty_id) SELECT $1,s FROM unnest($2::uuid[]) AS s ON CONFLICT DO NOTHING", professionalId, specialtyIds.Distinct().ToArray());
        await insert.ExecuteNonQueryAsync(ct);
    }

    private static async Task<Professional> ReadProfessionalAsync(NpgsqlConnection connection, Guid id, CancellationToken ct)
    {
        await using var query = Cmd(connection, ProfessionalSelect + " WHERE p.id=$1 GROUP BY p.id", id);
        await using var reader = await query.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadProfessional(reader) : throw new ApiNotFoundException();
    }

    private static Professional ReadProfessional(NpgsqlDataReader reader) => new(reader.GetGuid(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetString(3),
        reader.IsDBNull(4) ? null : reader.GetString(4), reader.GetBoolean(5), [.. reader.GetFieldValue<Guid[]>(6)]);

    // ---- Consultórios ----

    public async Task<List<Room>> ListRoomsAsync(Guid unitId, CancellationToken ct)
    {
        await using var connection = await databases.OpenAsync(unitId, ct);
        await using var query = Cmd(connection, "SELECT id,name,is_active FROM nora_unit.rooms ORDER BY lower(name),id");
        await using var reader = await query.ExecuteReaderAsync(ct);
        var items = new List<Room>();
        while (await reader.ReadAsync(ct)) items.Add(new(reader.GetGuid(0), reader.GetString(1), reader.GetBoolean(2)));
        return items;
    }

    public async Task<Room> CreateRoomAsync(Guid unitId, string name, CancellationToken ct)
    {
        await using var connection = await databases.OpenAsync(unitId, ct);
        await using var insert = Cmd(connection, "INSERT INTO nora_unit.rooms(id,name) VALUES($1,$2) RETURNING id,name,is_active", Guid.NewGuid(), name);
        await using var reader = await insert.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        return new(reader.GetGuid(0), reader.GetString(1), reader.GetBoolean(2));
    }

    public async Task<Room> RenameRoomAsync(Guid unitId, Guid id, string name, CancellationToken ct)
    {
        await using var connection = await databases.OpenAsync(unitId, ct);
        await using var update = Cmd(connection, "UPDATE nora_unit.rooms SET name=$2 WHERE id=$1 RETURNING id,name,is_active", id, name);
        await using var reader = await update.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? new(reader.GetGuid(0), reader.GetString(1), reader.GetBoolean(2)) : throw new ApiNotFoundException();
    }

    public async Task<Room> SetRoomActiveAsync(Guid unitId, Guid id, bool isActive, CancellationToken ct)
    {
        await using var connection = await databases.OpenAsync(unitId, ct);
        await using var update = Cmd(connection, "UPDATE nora_unit.rooms SET is_active=$2 WHERE id=$1 RETURNING id,name,is_active", id, isActive);
        await using var reader = await update.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? new(reader.GetGuid(0), reader.GetString(1), reader.GetBoolean(2)) : throw new ApiNotFoundException();
    }

    // ---- Grade semanal ----

    public async Task<List<ScheduleTemplate>> ListTemplatesAsync(Guid unitId, Guid? professionalId, CancellationToken ct)
    {
        await using var connection = await databases.OpenAsync(unitId, ct);
        await using var query = Cmd(connection, TemplateSelect + " WHERE ($1::uuid IS NULL OR t.professional_id=$1) ORDER BY t.professional_id,t.weekday,t.start_time,t.id", professionalId);
        await using var reader = await query.ExecuteReaderAsync(ct);
        var items = new List<ScheduleTemplate>();
        while (await reader.ReadAsync(ct)) items.Add(ReadTemplate(reader));
        return items;
    }

    public async Task<ScheduleTemplate> CreateTemplateAsync(Guid unitId, ScheduleTemplateDraft draft, CancellationToken ct)
    {
        var id = Guid.NewGuid();
        await using var connection = await databases.OpenAsync(unitId, ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await LockAsync(connection, GridLockKey, ct);
        await ValidateTemplateAsync(connection, id, draft, ct);
        await using (var insert = Cmd(connection, "INSERT INTO nora_unit.schedule_templates(id,professional_id,weekday,start_time,end_time,slot_minutes,room_id,specialty_id,valid_from,valid_until) VALUES($1,$2,$3,$4,$5,$6,$7,$8,$9::date,$10::date)",
            id, draft.ProfessionalId, (short)draft.Weekday, draft.Start, draft.End, draft.SlotMinutes, draft.RoomId, draft.SpecialtyId, draft.ValidFrom, draft.ValidUntil))
            await insert.ExecuteNonQueryAsync(ct);
        var result = await ReadTemplateAsync(connection, id, ct);
        await transaction.CommitAsync(ct);
        return result;
    }

    public async Task<ScheduleTemplate> UpdateTemplateAsync(Guid unitId, Guid id, ScheduleTemplateDraft draft, CancellationToken ct)
    {
        await using var connection = await databases.OpenAsync(unitId, ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await LockAsync(connection, GridLockKey, ct);
        await using (var exists = Cmd(connection, "SELECT 1 FROM nora_unit.schedule_templates WHERE id=$1 FOR UPDATE", id))
            if (await exists.ExecuteScalarAsync(ct) is null) throw new ApiNotFoundException();
        await ValidateTemplateAsync(connection, id, draft, ct);
        await using (var update = Cmd(connection, "UPDATE nora_unit.schedule_templates SET professional_id=$2,weekday=$3,start_time=$4,end_time=$5,slot_minutes=$6,room_id=$7,specialty_id=$8,valid_from=$9::date,valid_until=$10::date WHERE id=$1",
            id, draft.ProfessionalId, (short)draft.Weekday, draft.Start, draft.End, draft.SlotMinutes, draft.RoomId, draft.SpecialtyId, draft.ValidFrom, draft.ValidUntil))
            await update.ExecuteNonQueryAsync(ct);
        var result = await ReadTemplateAsync(connection, id, ct);
        await transaction.CommitAsync(ct);
        return result;
    }

    public async Task DeleteTemplateAsync(Guid unitId, Guid id, CancellationToken ct)
    {
        await using var connection = await databases.OpenAsync(unitId, ct);
        await using var delete = Cmd(connection, "DELETE FROM nora_unit.schedule_templates WHERE id=$1", id);
        if (await delete.ExecuteNonQueryAsync(ct) == 0) throw new ApiNotFoundException();
    }

    /// <summary>Profissional ativo com a especialidade, consultório ativo e sem sobreposição do profissional nem do consultório (mesmo dia da semana, horários e vigências que se cruzam). Chamar sob o lock da grade.</summary>
    private static async Task ValidateTemplateAsync(NpgsqlConnection connection, Guid id, ScheduleTemplateDraft draft, CancellationToken ct)
    {
        await using (var professional = Cmd(connection, "SELECT p.is_active, $2=ANY(COALESCE((SELECT array_agg(specialty_id) FROM nora_unit.professional_specialties WHERE professional_id=p.id), ARRAY[]::uuid[])) FROM nora_unit.professionals p WHERE p.id=$1", draft.ProfessionalId, draft.SpecialtyId))
        await using (var reader = await professional.ExecuteReaderAsync(ct))
        {
            if (!await reader.ReadAsync(ct)) throw new ApiInputException();
            if (!reader.GetBoolean(1)) throw new ApiInputException();
            if (!reader.GetBoolean(0)) throw new ApiConflictException();
        }
        await using (var room = Cmd(connection, "SELECT is_active FROM nora_unit.rooms WHERE id=$1", draft.RoomId))
        {
            var active = await room.ExecuteScalarAsync(ct);
            if (active is null) throw new ApiInputException();
            if (active is not true) throw new ApiConflictException();
        }
        await using var overlap = Cmd(connection, """
            SELECT EXISTS(SELECT 1 FROM nora_unit.schedule_templates t
              WHERE t.id<>$1 AND t.weekday=$2 AND (t.professional_id=$3 OR t.room_id=$4) AND t.start_time<$6 AND t.end_time>$5
                AND COALESCE(t.valid_from,'-infinity'::date) <= COALESCE($8::date,'infinity'::date) AND COALESCE(t.valid_until,'infinity'::date) >= COALESCE($7::date,'-infinity'::date))
            """, id, (short)draft.Weekday, draft.ProfessionalId, draft.RoomId, draft.Start, draft.End, draft.ValidFrom, draft.ValidUntil);
        if (await overlap.ExecuteScalarAsync(ct) is true) throw new ApiConflictException();
    }

    private static async Task<ScheduleTemplate> ReadTemplateAsync(NpgsqlConnection connection, Guid id, CancellationToken ct)
    {
        await using var query = Cmd(connection, TemplateSelect + " WHERE t.id=$1", id);
        await using var reader = await query.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadTemplate(reader) : throw new ApiNotFoundException();
    }

    private static ScheduleTemplate ReadTemplate(NpgsqlDataReader reader) => new(reader.GetGuid(0), reader.GetGuid(1), reader.GetInt16(2),
        reader.GetFieldValue<TimeOnly>(3).ToString("HH:mm", CultureInfo.InvariantCulture), reader.GetFieldValue<TimeOnly>(4).ToString("HH:mm", CultureInfo.InvariantCulture), reader.GetInt32(5), reader.GetGuid(6), reader.GetGuid(7),
        reader.IsDBNull(8) ? null : reader.GetFieldValue<DateOnly>(8).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), reader.IsDBNull(9) ? null : reader.GetFieldValue<DateOnly>(9).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

    // ---- Bloqueios ----

    public async Task<List<ScheduleBlock>> ListBlocksAsync(Guid unitId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        await using var connection = await databases.OpenAsync(unitId, ct);
        await using var query = Cmd(connection, "SELECT id,professional_id,starts_at,ends_at,reason FROM nora_unit.schedule_blocks WHERE starts_at<$2 AND ends_at>$1 ORDER BY starts_at,id", from.ToUniversalTime(), to.ToUniversalTime());
        await using var reader = await query.ExecuteReaderAsync(ct);
        var items = new List<ScheduleBlock>();
        while (await reader.ReadAsync(ct)) items.Add(new(reader.GetGuid(0), reader.IsDBNull(1) ? null : reader.GetGuid(1), reader.GetFieldValue<DateTimeOffset>(2), reader.GetFieldValue<DateTimeOffset>(3), reader.GetString(4)));
        return items;
    }

    public async Task<ScheduleBlock> CreateBlockAsync(Guid unitId, Guid actorId, Guid? professionalId, DateTimeOffset startsAt, DateTimeOffset endsAt, string reason, CancellationToken ct)
    {
        await using var connection = await databases.OpenAsync(unitId, ct);
        if (professionalId is { } professional)
        {
            await using var exists = Cmd(connection, "SELECT 1 FROM nora_unit.professionals WHERE id=$1", professional);
            if (await exists.ExecuteScalarAsync(ct) is null) throw new ApiInputException();
        }
        var id = Guid.NewGuid();
        await using var insert = Cmd(connection, "INSERT INTO nora_unit.schedule_blocks(id,professional_id,starts_at,ends_at,reason,created_by) VALUES($1,$2::uuid,$3,$4,$5,$6)", id, professionalId, startsAt.ToUniversalTime(), endsAt.ToUniversalTime(), reason, actorId);
        await insert.ExecuteNonQueryAsync(ct);
        return new(id, professionalId, startsAt.ToUniversalTime(), endsAt.ToUniversalTime(), reason);
    }

    public async Task DeleteBlockAsync(Guid unitId, Guid id, CancellationToken ct)
    {
        await using var connection = await databases.OpenAsync(unitId, ct);
        await using var delete = Cmd(connection, "DELETE FROM nora_unit.schedule_blocks WHERE id=$1", id);
        if (await delete.ExecuteNonQueryAsync(ct) == 0) throw new ApiNotFoundException();
    }

    // ---- Horários: carga, marcação, confirmação ----

    /// <summary>
    /// Carrega, numa só viagem, o que decide a situação dos atendimentos que começam em [from, to): grade de profissionais e consultórios ativos, bloqueios,
    /// consultas que ocupam horário e vagas relâmpago abertas/ofertadas. Consultas e bloqueios vão até a maior duração de atendimento depois de <paramref name="to"/>,
    /// para que um atendimento no fim da janela ainda enxergue o que o cobre.
    /// </summary>
    internal static async Task<ScheduleData> LoadAsync(NpgsqlConnection connection, DateTimeOffset from, DateTimeOffset to, Guid? professionalId, CancellationToken ct)
    {
        var low = from.ToUniversalTime(); var high = to.ToUniversalTime().AddMinutes(MaxSlotMinutes);
        await using var batch = new NpgsqlBatch(connection);
        batch.BatchCommands.Add(Statement("SELECT t.id,t.professional_id,p.full_name,lower(btrim(p.full_name)),t.weekday,t.start_time,t.end_time,t.slot_minutes,t.room_id,r.name,t.specialty_id,t.valid_from,t.valid_until FROM nora_unit.schedule_templates t JOIN nora_unit.professionals p ON p.id=t.professional_id AND p.is_active JOIN nora_unit.rooms r ON r.id=t.room_id AND r.is_active WHERE ($1::uuid IS NULL OR t.professional_id=$1)", professionalId));
        batch.BatchCommands.Add(Statement("SELECT professional_id,starts_at,ends_at FROM nora_unit.schedule_blocks WHERE starts_at<$2 AND ends_at>$1", low, high));
        batch.BatchCommands.Add(Statement("SELECT lower(btrim(professional)),starts_at,ends_at FROM nora_unit.appointments WHERE status IN ('scheduled','called','completed','missed') AND starts_at<$2 AND ends_at>$1", low, high));
        batch.BatchCommands.Add(Statement("SELECT lower(btrim(a.professional)),a.starts_at,a.ends_at FROM nora_unit.lightning_slots s JOIN nora_unit.appointments a ON a.id=s.appointment_id WHERE s.status IN ('open','offered') AND a.starts_at<$2 AND a.ends_at>$1", low, high));
        await using var reader = await batch.ExecuteReaderAsync(ct);
        var templates = new List<ScheduleTemplateRow>();
        while (await reader.ReadAsync(ct))
            templates.Add(new(reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), reader.GetString(3), reader.GetInt16(4), reader.GetFieldValue<TimeOnly>(5), reader.GetFieldValue<TimeOnly>(6), reader.GetInt32(7), reader.GetGuid(8), reader.GetString(9), reader.GetGuid(10),
                reader.IsDBNull(11) ? null : reader.GetFieldValue<DateOnly>(11), reader.IsDBNull(12) ? null : reader.GetFieldValue<DateOnly>(12)));
        await reader.NextResultAsync(ct);
        var unitBlocks = new List<Span>(); var professionalBlocks = new Dictionary<Guid, List<Span>>();
        while (await reader.ReadAsync(ct))
        {
            var span = new Span(reader.GetFieldValue<DateTimeOffset>(1).UtcTicks, reader.GetFieldValue<DateTimeOffset>(2).UtcTicks);
            if (reader.IsDBNull(0)) unitBlocks.Add(span);
            else { var key = reader.GetGuid(0); if (!professionalBlocks.TryGetValue(key, out var list)) professionalBlocks[key] = list = []; list.Add(span); }
        }
        await reader.NextResultAsync(ct);
        var appointments = await ReadByKeyAsync(reader, ct);
        await reader.NextResultAsync(ct);
        var held = await ReadByKeyAsync(reader, ct);
        return new()
        {
            Templates = templates, UnitBlocks = IntervalSet.Build(unitBlocks),
            ProfessionalBlocks = professionalBlocks.ToDictionary(pair => pair.Key, pair => IntervalSet.Build(pair.Value)),
            Appointments = appointments, Held = held
        };
    }

    private static async Task<Dictionary<string, IntervalSet>> ReadByKeyAsync(NpgsqlDataReader reader, CancellationToken ct)
    {
        var groups = new Dictionary<string, List<Span>>(StringComparer.Ordinal);
        while (await reader.ReadAsync(ct))
        {
            var key = reader.GetString(0);
            if (!groups.TryGetValue(key, out var list)) groups[key] = list = [];
            list.Add(new(reader.GetFieldValue<DateTimeOffset>(1).UtcTicks, reader.GetFieldValue<DateTimeOffset>(2).UtcTicks));
        }
        return groups.ToDictionary(pair => pair.Key, pair => IntervalSet.Build(pair.Value), StringComparer.Ordinal);
    }

    /// <summary>Abre a conexão da UBS só para carregar os dados de uma janela (disponibilidade e capacidade).</summary>
    internal async Task<ScheduleData> LoadAsync(Guid unitId, DateTimeOffset from, DateTimeOffset to, Guid? professionalId, CancellationToken ct)
    {
        await using var connection = await databases.OpenAsync(unitId, ct);
        return await LoadAsync(connection, from, to, professionalId, ct);
    }

    /// <summary>
    /// Marca uma consulta num atendimento livre da grade (equipe: <paramref name="bookedBy"/> "staff"; paciente: "patient"), numa transação da UBS: confere profissional e
    /// especialidade, recalcula a grade com os locks de profissional e de paciente seguros e recusa horário que não seja exatamente um atendimento livre.
    /// Atendimento ocupado, bloqueado, segurado por vaga relâmpago ou inexistente → 409 slot-unavailable; paciente já com consulta no mesmo horário → 409 patient-overlap.
    /// </summary>
    internal async Task<AppointmentInfo> BookAsync(Guid unitId, Guid actorId, string cpf, Guid professionalId, Specialty specialty, DateTimeOffset startsAt, DateTimeOffset? endsAt, string bookedBy, SlotEngine engine, CancellationToken ct)
    {
        var start = startsAt.ToUniversalTime();
        await using var connection = await databases.OpenAsync(unitId, ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        string name; bool active; bool hasSpecialty;
        await using (var professional = Cmd(connection, "SELECT p.full_name,p.is_active,EXISTS(SELECT 1 FROM nora_unit.professional_specialties s WHERE s.professional_id=p.id AND s.specialty_id=$2) FROM nora_unit.professionals p WHERE p.id=$1", professionalId, specialty.Id))
        await using (var reader = await professional.ExecuteReaderAsync(ct))
        {
            if (!await reader.ReadAsync(ct)) throw new ApiInputException();
            name = reader.GetString(0); active = reader.GetBoolean(1); hasSpecialty = reader.GetBoolean(2);
        }
        if (!active || !hasSpecialty) throw new ApiCodedException(StatusCodes.Status409Conflict, "slot-unavailable");
        await using (var advisory = Cmd(connection, "SELECT pg_advisory_xact_lock(hashtextextended(lower($1), 62419809))", name)) await advisory.ExecuteNonQueryAsync(ct);
        await using (var patientLock = Cmd(connection, "SELECT pg_advisory_xact_lock(hashtextextended($1, 62419810))", cpf)) await patientLock.ExecuteNonQueryAsync(ct);
        await using (var registered = Cmd(connection, "SELECT is_active FROM nora_unit.patient_registry WHERE cpf=$1 FOR SHARE", cpf))
            if (await registered.ExecuteScalarAsync(ct) is not true) throw new ApiNotFoundException();
        var data = await LoadAsync(connection, start, start.AddSeconds(1), professionalId, ct);
        GeneratedSlot? slot = null;
        foreach (var candidate in engine.Generate(data.Templates.Where(template => template.SpecialtyId == specialty.Id).ToList(), start, start.AddSeconds(1)))
            if (candidate.Start == start.UtcTicks && SlotEngine.StateOf(data, candidate) == SlotState.Free) { slot = candidate; break; }
        if (slot is not { } chosen) throw new ApiCodedException(StatusCodes.Status409Conflict, "slot-unavailable");
        if (endsAt is { } end && end.ToUniversalTime().UtcTicks != chosen.End) throw new ApiInputException();
        await using (var overlap = Cmd(connection, "SELECT EXISTS(SELECT 1 FROM nora_unit.appointments WHERE cpf=$1 AND status IN ('scheduled','called','completed','missed') AND starts_at<$3 AND ends_at>$2)", cpf, start, new DateTimeOffset(chosen.End, TimeSpan.Zero)))
            if (await overlap.ExecuteScalarAsync(ct) is true) throw new ApiCodedException(StatusCodes.Status409Conflict, "patient-overlap");
        var id = Guid.NewGuid(); var now = time.GetUtcNow();
        await using (var insert = Cmd(connection, "INSERT INTO nora_unit.appointments(id,cpf,specialty,professional,starts_at,ends_at,status,created_by,created_at,professional_id,specialty_id,room_id,booked_by) VALUES($1,$2,$3,$4,$5,$6,'scheduled',$7,$8,$9,$10,$11,$12)",
            id, cpf, specialty.Name, name, start, new DateTimeOffset(chosen.End, TimeSpan.Zero), actorId, now, professionalId, specialty.Id, chosen.Template.RoomId, bookedBy))
            await insert.ExecuteNonQueryAsync(ct);
        await using (var audit = Cmd(connection, "INSERT INTO nora_unit.appointment_events(appointment_id,actor_id,status,occurred_at,source) VALUES($1,$2,'scheduled',$3,$4)", id, actorId, now, bookedBy))
            await audit.ExecuteNonQueryAsync(ct);
        var result = await AppointmentDao.ReadOneAsync(connection, id, ct);
        await transaction.CommitAsync(ct);
        return result;
    }

    /// <summary>
    /// Confirmação de presença pelo próprio paciente: de 48 h até 2 h antes do início (limites incluídos); fora disso 409 confirmation-window. Idempotente: repetir devolve a consulta
    /// já confirmada sem mudar o instante. Só consulta <c>scheduled</c> pode ser confirmada (outra situação → 409); consulta de outra pessoa ou inexistente → 404.
    /// </summary>
    public async Task<AppointmentInfo> ConfirmAsync(Guid unitId, string cpf, Guid id, CancellationToken ct)
    {
        await using var connection = await databases.OpenAsync(unitId, ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        string status; DateTimeOffset startsAt; DateTimeOffset? confirmedAt;
        await using (var select = Cmd(connection, "SELECT status,starts_at,confirmed_at FROM nora_unit.appointments WHERE id=$1 AND cpf=$2 FOR UPDATE", id, cpf))
        await using (var reader = await select.ExecuteReaderAsync(ct))
        {
            if (!await reader.ReadAsync(ct)) throw new ApiNotFoundException();
            status = reader.GetString(0); startsAt = reader.GetFieldValue<DateTimeOffset>(1); confirmedAt = reader.IsDBNull(2) ? null : reader.GetFieldValue<DateTimeOffset>(2);
        }
        if (status != "scheduled") throw new ApiConflictException();
        if (confirmedAt is null)
        {
            var now = time.GetUtcNow();
            if (now < startsAt.AddHours(-48) || now > startsAt.AddHours(-2)) throw new ApiCodedException(StatusCodes.Status409Conflict, "confirmation-window");
            await using var update = Cmd(connection, "UPDATE nora_unit.appointments SET confirmed_at=$2 WHERE id=$1", id, now);
            await update.ExecuteNonQueryAsync(ct);
        }
        var result = await AppointmentDao.ReadOneAsync(connection, id, ct);
        await transaction.CommitAsync(ct);
        return result;
    }

    /// <summary>Consultas futuras scheduled/called que o paciente marcou pelo app naquela especialidade (limite de uma por especialidade na rede).</summary>
    public async Task<long> CountPatientBookingsAsync(Guid unitId, string cpf, Guid specialtyId, DateTimeOffset now, CancellationToken ct)
    {
        await using var connection = await databases.OpenAsync(unitId, ct);
        await using var query = Cmd(connection, "SELECT count(*) FROM nora_unit.appointments WHERE cpf=$1 AND specialty_id=$2 AND booked_by='patient' AND status IN ('scheduled','called') AND starts_at>$3", cpf, specialtyId, now.ToUniversalTime());
        return (long)(await query.ExecuteScalarAsync(ct))!;
    }

    // ---- Consultórios ativos, fila por especialidade ----

    /// <summary>Consultórios com consulta chamada hoje (<c>called</c> com início no dia local) e consultórios ativos com grade vigente para o dia da semana de hoje.</summary>
    public async Task<ActiveRooms> ReadActiveRoomsAsync(Guid unitId, DateTimeOffset dayStart, DateTimeOffset dayEnd, DateOnly today, CancellationToken ct)
    {
        await using var connection = await databases.OpenAsync(unitId, ct);
        await using var query = Cmd(connection, """
            SELECT (SELECT count(DISTINCT room_id) FROM nora_unit.appointments WHERE status='called' AND room_id IS NOT NULL AND starts_at>=$1 AND starts_at<$2),
                   (SELECT count(DISTINCT t.room_id) FROM nora_unit.schedule_templates t JOIN nora_unit.rooms r ON r.id=t.room_id AND r.is_active JOIN nora_unit.professionals p ON p.id=t.professional_id AND p.is_active
                     WHERE t.weekday=$3 AND COALESCE(t.valid_from,'-infinity'::date)<=$4 AND COALESCE(t.valid_until,'infinity'::date)>=$4)
            """, dayStart.ToUniversalTime(), dayEnd.ToUniversalTime(), (short)today.DayOfWeek, today);
        await using var reader = await query.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        return new((int)reader.GetInt64(0), (int)reader.GetInt64(1));
    }

    /// <summary>Quem aguarda (waiting/offered) por especialidade: quantidade e a entrada mais antiga. Sem dado pessoal.</summary>
    public async Task<List<WaitingGroup>> ReadWaitingBySpecialtyAsync(Guid unitId, CancellationToken ct)
    {
        await using var connection = await databases.OpenAsync(unitId, ct);
        await using var query = Cmd(connection, "SELECT lower(btrim(specialty)),min(btrim(specialty)),count(*),min(entered_at) FROM nora_unit.waiting_list WHERE status IN ('waiting','offered') GROUP BY 1 ORDER BY 1");
        await using var reader = await query.ExecuteReaderAsync(ct);
        var groups = new List<WaitingGroup>();
        while (await reader.ReadAsync(ct)) groups.Add(new(reader.GetString(0), reader.GetString(1), reader.GetInt64(2), reader.GetFieldValue<DateTimeOffset>(3)));
        return groups;
    }
}
