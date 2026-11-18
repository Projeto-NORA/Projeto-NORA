using Npgsql;
using Nora.Api.Database;
using Nora.Api.Models;
using Nora.Api.Services;

namespace Nora.Api.DAOs;

/// <summary>Internal unit boundary. Callers must authorize the unit before invoking this DAO.</summary>
public sealed class UnitWaitingListDao(UnitDatabaseRouter databases, TimeProvider time)
{
    private const string EntryFrom = "FROM nora_unit.waiting_list q JOIN nora_unit.patient_registry p ON p.cpf=q.cpf ";
    private const string EntryColumns = "SELECT q.id,q.cpf,p.full_name,q.specialty,q.entered_at,q.status ";

    /// <summary>Entradas de um CPF (rotas do paciente e do responsável), em ordem de entrada. Usa o índice por CPF (P5).</summary>
    public async Task<List<UnitWaitingEntry>> ListAsync(Guid unitId, CancellationToken ct, string cpf)
    {
        await using var connection = await databases.OpenAsync(unitId, ct);
        await using var query = new NpgsqlCommand(EntryColumns + EntryFrom + "WHERE q.cpf=$1 ORDER BY q.entered_at,q.id", connection);
        query.Parameters.AddWithValue(cpf);
        await using var reader = await query.ExecuteReaderAsync(ct); var entries = new List<UnitWaitingEntry>();
        while (await reader.ReadAsync(ct)) entries.Add(Read(reader));
        return entries;
    }

    /// <summary>
    /// Fila da UBS para o OBS (P4, docs/desempenho.md): <b>todas</b> as entradas ativas (<c>waiting</c>, <c>offered</c>) e até <paramref name="historyLimit"/> encerradas (<c>completed</c>, <c>cancelled</c>), das mais recentes para as mais
    /// antigas (data de entrada, depois id). Com <paramref name="historyBefore"/> devolve só as encerradas mais antigas que o cursor, sem as ativas. Cada item traz os pontos de score do CPF na UBS e a posição de oferta
    /// (<see cref="ObsWaitingEntry.OfferOrder"/>) das ativas. Os itens saem na ordem de entrada (a mais antiga primeiro).
    /// </summary>
    public async Task<ObsWaitingPage> ListForObsAsync(Guid unitId, int historyLimit, string? historyBefore, CancellationToken ct)
    {
        var before = historyBefore is null ? ((DateTimeOffset, long)?)null : DecodeCursor(historyBefore);
        await using var connection = await databases.OpenAsync(unitId, ct);
        var entries = new List<(UnitWaitingEntry Entry, string Group)>();
        if (before is null)
        {
            await using var active = new NpgsqlCommand("SELECT q.id,q.cpf,p.full_name,q.specialty,q.entered_at,q.status,lower(btrim(q.specialty)) " + EntryFrom + "WHERE q.status IN ('waiting','offered') ORDER BY q.entered_at,q.id", connection);
            await using var reader = await active.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct)) entries.Add((Read(reader), reader.GetString(6)));
        }
        string? cursor = null;
        await using (var history = new NpgsqlCommand(EntryColumns + EntryFrom + "WHERE q.status IN ('completed','cancelled') " + (before is null ? "" : "AND (q.entered_at,q.id)<($2,$3) ") + "ORDER BY q.entered_at DESC,q.id DESC LIMIT $1", connection))
        {
            history.Parameters.AddWithValue(historyLimit + 1);
            if (before is { } point) { history.Parameters.AddWithValue(point.Item1); history.Parameters.AddWithValue(point.Item2); }
            await using var reader = await history.ExecuteReaderAsync(ct);
            var closed = new List<UnitWaitingEntry>();
            while (await reader.ReadAsync(ct)) closed.Add(Read(reader));
            if (closed.Count > historyLimit) { closed.RemoveAt(historyLimit); cursor = PageCursor.Encode(closed[^1].EnteredAt, closed[^1].Id.ToString(System.Globalization.CultureInfo.InvariantCulture)); }
            entries.AddRange(closed.Select(entry => (entry, "")));
        }
        if (entries.Count == 0) return new([], cursor);
        var (from, to) = UnitRegistryReadService.AttendanceWindow(time.GetUtcNow());
        var tallies = await UnitMetricsDao.ReadAttendanceAsync(connection, from, to, entries.Select(item => item.Entry.Cpf).Distinct().ToArray(), ct);
        // Ordem de oferta de cada especialidade: mais pontos primeiro; em empate vale a ordem de entrada (as ativas já vêm por entered_at,id e o OrderBy é estável).
        var order = new Dictionary<long, int>();
        foreach (var group in entries.Where(item => item.Group != "").GroupBy(item => item.Group))
        {
            var position = 0;
            foreach (var item in group.OrderByDescending(item => tallies.GetValueOrDefault(item.Entry.Cpf).Points)) order[item.Entry.Id] = ++position;
        }
        return new(entries.Select(item => item.Entry).OrderBy(entry => entry.EnteredAt).ThenBy(entry => entry.Id).Select(entry =>
        {
            var points = tallies.GetValueOrDefault(entry.Cpf).Points;
            return new ObsWaitingEntry(entry.Id, entry.Cpf, entry.PatientName, entry.Specialty, entry.EnteredAt, entry.Status, points, AttendanceScore.Band(points), order.TryGetValue(entry.Id, out var offerOrder) ? offerOrder : null);
        }).ToList(), cursor);
    }

    private static (DateTimeOffset, long) DecodeCursor(string cursor)
    {
        var (at, key) = PageCursor.Decode(cursor);
        return long.TryParse(key, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var id) ? (at, id) : throw new ApiInputException();
    }

    public async Task<UnitWaitingEntry> AddAsync(Guid unitId, Guid actorId, AddUnitWaitingEntryRequest request, CancellationToken ct)
    {
        var cpf = AccountService.NormalizeCpf(request.Cpf); var specialty = request.Specialty?.Trim() ?? "";
        if (unitId == Guid.Empty || actorId == Guid.Empty || !AccountService.ValidCpf(cpf) || specialty.Length is < 1 or > 120 || specialty.Any(char.IsControl)) throw new ApiInputException();
        await using var connection = await databases.OpenAsync(unitId, ct); await using var transaction = await connection.BeginTransactionAsync(ct);
        await LockAsync(connection, ct);
        await using (var patient = new NpgsqlCommand("SELECT is_active FROM nora_unit.patient_registry WHERE cpf=$1 FOR SHARE", connection))
        { patient.Parameters.AddWithValue(cpf); if (await patient.ExecuteScalarAsync(ct) is not true) throw new ApiNotFoundException(); }
        var now = time.GetUtcNow(); long id;
        await using (var insert = new NpgsqlCommand("INSERT INTO nora_unit.waiting_list(cpf,specialty,entered_at,status,created_by) VALUES($1,$2,$3,'waiting',$4) ON CONFLICT DO NOTHING RETURNING id", connection))
        {
            insert.Parameters.AddWithValue(cpf); insert.Parameters.AddWithValue(specialty); insert.Parameters.AddWithValue(now); insert.Parameters.AddWithValue(actorId);
            id = await insert.ExecuteScalarAsync(ct) is long created ? created : throw new ApiConflictException();
        }
        await RecordAsync(connection, id, actorId, "waiting", now, ct);
        var item = await GetAsync(connection, id, ct); await transaction.CommitAsync(ct); return item;
    }

    public async Task CancelAsync(Guid unitId, Guid actorId, long id, bool confirmed, CancellationToken ct)
    {
        if (id <= 0 || !confirmed) throw new ApiInputException();
        await using var connection = await databases.OpenAsync(unitId, ct); await using var transaction = await connection.BeginTransactionAsync(ct);
        await LockAsync(connection, ct);
        await using (var update = new NpgsqlCommand("UPDATE nora_unit.waiting_list SET status='cancelled' WHERE id=$1 AND status IN ('waiting','offered') RETURNING id", connection))
        { update.Parameters.AddWithValue(id); if (await update.ExecuteScalarAsync(ct) is not long) throw new ApiConflictException(); }
        await using (var reopen = new NpgsqlCommand("UPDATE nora_unit.lightning_slots SET status='open' WHERE id IN (SELECT slot_id FROM nora_unit.lightning_offers WHERE entry_id=$1 AND status='pending')", connection))
        { reopen.Parameters.AddWithValue(id); await reopen.ExecuteNonQueryAsync(ct); }
        await using (var cancel = new NpgsqlCommand("UPDATE nora_unit.lightning_offers SET status='cancelled' WHERE entry_id=$1 AND status='pending'", connection))
        { cancel.Parameters.AddWithValue(id); await cancel.ExecuteNonQueryAsync(ct); }
        await RecordAsync(connection, id, actorId, "cancelled", time.GetUtcNow(), ct); await transaction.CommitAsync(ct);
    }

    /// <summary>Cancela a própria entrada do paciente (CPF da sessão), com evento de origem 'patient'. Outra pessoa ou inexistente → 404; já encerrada → 409.</summary>
    public async Task CancelByPatientAsync(Guid unitId, Guid actorId, string cpf, long id, bool confirmed, CancellationToken ct, Guid? guardianId = null)
    {
        if (id <= 0 || actorId == Guid.Empty || !confirmed) throw new ApiInputException();
        await using var connection = await databases.OpenAsync(unitId, ct); await using var transaction = await connection.BeginTransactionAsync(ct);
        await LockAsync(connection, ct);
        string status;
        await using (var select = new NpgsqlCommand("SELECT status FROM nora_unit.waiting_list WHERE id=$1 AND cpf=$2 FOR UPDATE", connection))
        { select.Parameters.AddWithValue(id); select.Parameters.AddWithValue(cpf); status = await select.ExecuteScalarAsync(ct) as string ?? throw new ApiNotFoundException(); }
        if (status is not ("waiting" or "offered")) throw new ApiConflictException();
        await using (var update = new NpgsqlCommand("UPDATE nora_unit.waiting_list SET status='cancelled' WHERE id=$1", connection))
        { update.Parameters.AddWithValue(id); await update.ExecuteNonQueryAsync(ct); }
        await using (var reopen = new NpgsqlCommand("UPDATE nora_unit.lightning_slots SET status='open' WHERE id IN (SELECT slot_id FROM nora_unit.lightning_offers WHERE entry_id=$1 AND status='pending')", connection))
        { reopen.Parameters.AddWithValue(id); await reopen.ExecuteNonQueryAsync(ct); }
        await using (var cancel = new NpgsqlCommand("UPDATE nora_unit.lightning_offers SET status='cancelled' WHERE entry_id=$1 AND status='pending'", connection))
        { cancel.Parameters.AddWithValue(id); await cancel.ExecuteNonQueryAsync(ct); }
        await RecordAsync(connection, id, actorId, "cancelled", time.GetUtcNow(), ct, "patient", guardianId); await transaction.CommitAsync(ct);
    }

    private static async Task RecordAsync(NpgsqlConnection connection, long id, Guid actor, string status, DateTimeOffset now, CancellationToken ct, string origin = "staff", Guid? guardianId = null)
    {
        await using var insert = new NpgsqlCommand("INSERT INTO nora_unit.waiting_list_events(entry_id,actor_id,status,occurred_at,source,guardian_id) VALUES($1,$2,$3,$4,$5,$6)", connection);
        insert.Parameters.AddWithValue(id); insert.Parameters.AddWithValue(actor); insert.Parameters.AddWithValue(status); insert.Parameters.AddWithValue(now); insert.Parameters.AddWithValue(origin); insert.Parameters.Add(new NpgsqlParameter { Value = (object?)guardianId ?? DBNull.Value }); await insert.ExecuteNonQueryAsync(ct);
    }
    internal static async Task LockAsync(NpgsqlConnection connection, CancellationToken ct)
    { await using var command = new NpgsqlCommand("SELECT pg_advisory_xact_lock(62419811)", connection); await command.ExecuteNonQueryAsync(ct); }
    private static async Task<UnitWaitingEntry> GetAsync(NpgsqlConnection connection, long id, CancellationToken ct)
    {
        await using var query = new NpgsqlCommand("SELECT q.id,q.cpf,p.full_name,q.specialty,q.entered_at,q.status FROM nora_unit.waiting_list q JOIN nora_unit.patient_registry p ON p.cpf=q.cpf WHERE q.id=$1", connection);
        query.Parameters.AddWithValue(id); await using var reader = await query.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) throw new ApiNotFoundException(); return Read(reader);
    }
    private static UnitWaitingEntry Read(NpgsqlDataReader reader) => new(reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetFieldValue<DateTimeOffset>(4), reader.GetString(5));
}
