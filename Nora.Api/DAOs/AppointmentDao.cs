using Npgsql;
using Nora.Api.Database;
using Nora.Api.Models;
using Nora.Api.Services;

namespace Nora.Api.DAOs;

/// <param name="central">Banco central, só para o nome do responsável nas listas do paciente (<see cref="ListForPatientAsync"/>).</param>
public sealed class AppointmentDao(UnitDatabaseRouter databases, TimeProvider time, SchedulingService? scheduling = null, DatabaseHelper? central = null)
{
    public async Task<AppointmentInfo> CreateAsync(Guid unitId, Guid actorId, CreateAppointmentRequest request, CancellationToken ct)
    {
        // Com ao menos um profissional ativo cadastrado a UBS marca só pelo catálogo e pela grade (Fase 3); sem profissional continua o texto livre abaixo.
        if (scheduling is not null && await scheduling.UsesCatalogAsync(unitId, ct)) return await scheduling.BookForStaffAsync(unitId, actorId, request, ct);
        var cpf = AccountService.NormalizeCpf(request.Cpf);
        var specialty = request.Specialty?.Trim() ?? "";
        var professional = request.Professional?.Trim() ?? "";
        var start = request.StartsAt.ToUniversalTime(); var end = request.EndsAt.ToUniversalTime();
        if (!AccountService.ValidCpf(cpf) || specialty.Length is < 1 or > 120 || professional.Length is < 1 or > 160 ||
            specialty.Any(char.IsControl) || professional.Any(char.IsControl) || start <= time.GetUtcNow() || end <= start || end - start > TimeSpan.FromHours(12))
            throw new ApiInputException();
        await using var connection = await databases.OpenAsync(unitId, ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        // Serialize bookings for the same professional in this UBS, including overlapping time intervals.
        await using (var advisory = new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtextextended(lower($1), 62419809))", connection))
        { advisory.Parameters.AddWithValue(professional); await advisory.ExecuteNonQueryAsync(ct); }
        await using (var patientLock = new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtextextended($1, 62419810))", connection))
        { patientLock.Parameters.AddWithValue(cpf); await patientLock.ExecuteNonQueryAsync(ct); }
        await using (var exists = new NpgsqlCommand("SELECT is_active FROM nora_unit.patient_registry WHERE cpf=$1 FOR SHARE", connection))
        { exists.Parameters.AddWithValue(cpf); if (await exists.ExecuteScalarAsync(ct) is not true) throw new ApiNotFoundException(); }
        await using (var conflict = new NpgsqlCommand("SELECT EXISTS(SELECT 1 FROM nora_unit.appointments WHERE (lower(professional)=lower($1) OR cpf=$4) AND status IN ('scheduled','called','completed','missed') AND starts_at<$3 AND ends_at>$2) OR EXISTS(SELECT 1 FROM nora_unit.lightning_slots s JOIN nora_unit.appointments a ON a.id=s.appointment_id WHERE lower(a.professional)=lower($1) AND s.status IN ('open','offered') AND a.starts_at<$3 AND a.ends_at>$2)", connection))
        { conflict.Parameters.AddWithValue(professional); conflict.Parameters.AddWithValue(start); conflict.Parameters.AddWithValue(end); conflict.Parameters.AddWithValue(cpf); if (await conflict.ExecuteScalarAsync(ct) is true) throw new ApiConflictException(); }
        var id = Guid.NewGuid(); var now = time.GetUtcNow();
        await using (var insert = new NpgsqlCommand("INSERT INTO nora_unit.appointments(id,cpf,specialty,professional,starts_at,ends_at,status,created_by,created_at) VALUES($1,$2,$3,$4,$5,$6,'scheduled',$7,$8)", connection))
        { foreach (var value in new object[] { id, cpf, specialty, professional, start, end, actorId, now }) insert.Parameters.AddWithValue(value); await insert.ExecuteNonQueryAsync(ct); }
        await InsertEventAsync(connection, id, actorId, "scheduled", now, "staff", ct);
        var result = await ReadOneAsync(connection, id, ct);
        await transaction.CommitAsync(ct);
        return result;
    }

    public async Task<List<AppointmentInfo>> ListAsync(Guid unitId, DateTimeOffset from, DateTimeOffset to, string? cpf, CancellationToken ct)
    {
        from = from.ToUniversalTime(); to = to.ToUniversalTime();
        if (to <= from || to - from > TimeSpan.FromDays(366)) throw new ApiInputException();
        await using var connection = await databases.OpenAsync(unitId, ct);
        await using var query = new NpgsqlCommand("SELECT " + Columns + " FROM nora_unit.appointments a JOIN nora_unit.patient_registry p ON p.cpf=a.cpf WHERE a.starts_at >= $1 AND a.starts_at < $2 AND ($3='' OR a.cpf=$3) ORDER BY a.starts_at,a.id", connection);
        query.Parameters.AddWithValue(from); query.Parameters.AddWithValue(to); query.Parameters.AddWithValue(cpf ?? "");
        await using var reader = await query.ExecuteReaderAsync(ct); var items = new List<AppointmentInfo>();
        while (await reader.ReadAsync(ct)) items.Add(Read(reader));
        return items;
    }

    /// <summary>
    /// Consultas do CPF na janela, como <see cref="ListAsync"/>, com quem marcou cada uma pelo app (evento <c>scheduled</c> de origem <c>patient</c>): <c>BookedByYou</c> quando foi a conta
    /// <paramref name="viewerId"/> (para si ou como responsável) e <c>BookedByGuardianName</c> quando foi um responsável pela Família (<c>guardian_id</c>), lido do banco central numa consulta só.
    /// Consulta marcada pela equipe: false e nulo.
    /// </summary>
    public async Task<List<AppointmentInfo>> ListForPatientAsync(Guid unitId, DateTimeOffset from, DateTimeOffset to, string cpf, Guid viewerId, CancellationToken ct)
    {
        from = from.ToUniversalTime(); to = to.ToUniversalTime();
        if (to <= from || to - from > TimeSpan.FromDays(366)) throw new ApiInputException();
        var rows = new List<(AppointmentInfo Info, Guid? Guardian)>();
        await using (var connection = await databases.OpenAsync(unitId, ct))
        await using (var query = new NpgsqlCommand("SELECT " + Columns + ",b.actor_id,b.guardian_id FROM nora_unit.appointments a JOIN nora_unit.patient_registry p ON p.cpf=a.cpf LEFT JOIN LATERAL (SELECT e.actor_id,e.guardian_id FROM nora_unit.appointment_events e WHERE e.appointment_id=a.id AND e.status='scheduled' AND e.source='patient' ORDER BY e.id LIMIT 1) b ON true WHERE a.cpf=$3 AND a.starts_at >= $1 AND a.starts_at < $2 ORDER BY a.starts_at,a.id", connection))
        {
            query.Parameters.AddWithValue(from); query.Parameters.AddWithValue(to); query.Parameters.AddWithValue(cpf);
            await using var reader = await query.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                rows.Add((Read(reader) with { BookedByYou = !reader.IsDBNull(12) && reader.GetGuid(12) == viewerId }, reader.IsDBNull(13) ? null : reader.GetGuid(13)));
        }
        var guardians = rows.Where(row => row.Guardian is not null).Select(row => row.Guardian!.Value).Distinct().ToArray();
        var names = guardians.Length == 0 ? null : await GuardianNamesAsync(guardians, ct);
        return rows.Select(row => row.Guardian is { } guardian ? row.Info with { BookedByGuardianName = names!.GetValueOrDefault(guardian) } : row.Info).ToList();
    }

    private async Task<Dictionary<Guid, string>> GuardianNamesAsync(Guid[] ids, CancellationToken ct)
    {
        var database = central ?? throw new InvalidOperationException("Banco central não configurado para o AppointmentDao.");
        await using var connection = await database.OpenAsync(ct);
        await using var query = database.Command(connection, "SELECT id,full_name FROM nora.accounts WHERE id=ANY($1)", ids);
        await using var reader = await query.ExecuteReaderAsync(ct);
        var names = new Dictionary<Guid, string>(ids.Length);
        while (await reader.ReadAsync(ct)) names[reader.GetGuid(0)] = reader.GetString(1);
        return names;
    }

    /// <param name="guardianId">Conta do responsável quando o paciente é um dependente (Família): vai para o evento, que continua com origem "patient".</param>
    public async Task<AppointmentInfo> ChangeStatusAsync(Guid unitId, Guid actorId, Guid id, AppointmentStatusRequest request, CancellationToken ct, string? patientCpf = null, Guid? guardianId = null)
    {
        if (id == Guid.Empty || request.Status is not ("called" or "completed" or "missed" or "cancelled") || (request.Status == "cancelled" && !request.Confirmed)) throw new ApiInputException();
        await using var connection = await databases.OpenAsync(unitId, ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await using (var select = new NpgsqlCommand("SELECT status FROM nora_unit.appointments WHERE id=$1 AND ($2='' OR cpf=$2) FOR UPDATE", connection))
        {
            select.Parameters.AddWithValue(id); select.Parameters.AddWithValue(patientCpf ?? "");
            var previous = await select.ExecuteScalarAsync(ct) as string ?? throw new ApiNotFoundException();
            if (!((previous == "scheduled" && request.Status is "called" or "missed" or "cancelled") ||
                  (previous == "called" && request.Status is "completed" or "missed" or "cancelled"))) throw new ApiConflictException();
        }
        await using (var update = new NpgsqlCommand("UPDATE nora_unit.appointments SET status=$2 WHERE id=$1", connection))
        { update.Parameters.AddWithValue(id); update.Parameters.AddWithValue(request.Status); await update.ExecuteNonQueryAsync(ct); }
        await InsertEventAsync(connection, id, actorId, request.Status, time.GetUtcNow(), patientCpf is null ? "staff" : "patient", ct, guardianId);
        var result = await ReadOneAsync(connection, id, ct); await transaction.CommitAsync(ct); return result;
    }

    /// <summary>Origem: "staff" (OBS), "patient" (aplicativo) ou "system"; só cancelamentos "patient" pesam no score de comparecimento.</summary>
    private static async Task InsertEventAsync(NpgsqlConnection connection, Guid id, Guid actor, string status, DateTimeOffset now, string origin, CancellationToken ct, Guid? guardianId = null)
    {
        await using var insert = new NpgsqlCommand("INSERT INTO nora_unit.appointment_events(appointment_id,actor_id,status,occurred_at,source,guardian_id) VALUES($1,$2,$3,$4,$5,$6)", connection);
        insert.Parameters.AddWithValue(id); insert.Parameters.AddWithValue(actor); insert.Parameters.AddWithValue(status); insert.Parameters.AddWithValue(now); insert.Parameters.AddWithValue(origin); insert.Parameters.Add(new NpgsqlParameter { Value = (object?)guardianId ?? DBNull.Value }); await insert.ExecuteNonQueryAsync(ct);
    }
    internal static async Task<AppointmentInfo> ReadOneAsync(NpgsqlConnection connection, Guid id, CancellationToken ct)
    {
        await using var query = new NpgsqlCommand("SELECT " + Columns + " FROM nora_unit.appointments a JOIN nora_unit.patient_registry p ON p.cpf=a.cpf WHERE a.id=$1", connection);
        query.Parameters.AddWithValue(id); await using var reader = await query.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) throw new ApiNotFoundException(); return Read(reader);
    }
    internal const string Columns = "a.id,a.cpf,p.full_name,a.specialty,a.professional,a.starts_at,a.ends_at,a.status,a.confirmed_at,a.booked_by,a.professional_id,a.specialty_id";
    internal static AppointmentInfo Read(NpgsqlDataReader reader) => new(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetFieldValue<DateTimeOffset>(5), reader.GetFieldValue<DateTimeOffset>(6), reader.GetString(7),
        reader.IsDBNull(8) ? null : reader.GetFieldValue<DateTimeOffset>(8), reader.GetString(9), reader.IsDBNull(10) ? null : reader.GetGuid(10), reader.IsDBNull(11) ? null : reader.GetGuid(11));
}
