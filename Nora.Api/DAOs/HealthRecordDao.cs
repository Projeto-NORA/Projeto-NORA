using Npgsql;
using Nora.Api.Database;
using Nora.Api.Models;
using Nora.Api.Services;

namespace Nora.Api.DAOs;

/// <summary>
/// Exames e vacinas por paciente no banco da UBS. Fronteira interna: quem chama já autorizou a UBS (equipe) ou o vínculo (paciente).
/// Nada é apagado: a correção é a anulação com motivo. Toda escrita grava autor e instante em <c>health_record_events</c>, na mesma transação.
/// </summary>
public sealed class HealthRecordDao(UnitDatabaseRouter databases, TimeProvider time)
{
    public const int NameMax = 160, NoteMax = 280, ReasonMin = 3;
    private static readonly DateOnly EarliestDate = new(1900, 1, 1);

    public async Task<ExamInfo> CreateExamAsync(Guid unitId, Guid actorId, CreateExamRequest request, CancellationToken ct)
    {
        var cpf = Cpf(request.Cpf); var name = CommunityText.Line(request.Name, 1, NameMax); var note = CommunityText.OptionalLine(request.Note, NoteMax);
        await using var connection = await databases.OpenAsync(unitId, ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await RequireActiveAsync(connection, cpf, ct);
        var id = Guid.NewGuid(); var now = time.GetUtcNow();
        await Exec(connection, "INSERT INTO nora_unit.health_exams(id,cpf,name,status,requested_at,note,created_by) VALUES($1,$2,$3,'requested',$4,$5,$6)", ct, id, cpf, name, now, (object?)note ?? DBNull.Value, actorId);
        await RecordAsync(connection, "exam", id, actorId, "created", now, ct);
        var result = await ReadExamAsync(connection, id, true, ct);
        await transaction.CommitAsync(ct);
        return result;
    }

    /// <summary>Única transição aceita a partir de <c>requested</c>: para <c>done</c> (com a data de realização) ou <c>cancelled</c>. Qualquer outra → 409.</summary>
    public async Task<ExamInfo> ChangeExamStatusAsync(Guid unitId, Guid actorId, Guid id, ExamStatusRequest request, CancellationToken ct)
    {
        if (id == Guid.Empty || request.Status is not ("done" or "cancelled") || (request.Status == "done") != request.PerformedOn.HasValue) throw new ApiInputException();
        if (request.PerformedOn is { } performed) CheckDate(performed);
        await using var connection = await databases.OpenAsync(unitId, ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        var current = await LockStatusAsync(connection, "health_exams", id, ct);
        if (current != "requested") throw new ApiConflictException();
        await Exec(connection, "UPDATE nora_unit.health_exams SET status=$2,performed_on=$3 WHERE id=$1", ct, id, request.Status, (object?)request.PerformedOn ?? DBNull.Value);
        await RecordAsync(connection, "exam", id, actorId, request.Status, time.GetUtcNow(), ct);
        var result = await ReadExamAsync(connection, id, true, ct);
        await transaction.CommitAsync(ct);
        return result;
    }

    public async Task<ExamInfo> VoidExamAsync(Guid unitId, Guid actorId, Guid id, VoidHealthRecordRequest request, CancellationToken ct)
    {
        var reason = VoidReason(id, request);
        await using var connection = await databases.OpenAsync(unitId, ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        if (await LockStatusAsync(connection, "health_exams", id, ct) == "voided") throw new ApiConflictException();
        var now = time.GetUtcNow();
        await Exec(connection, "UPDATE nora_unit.health_exams SET status='voided',void_reason=$2,voided_at=$3 WHERE id=$1", ct, id, reason, now);
        await RecordAsync(connection, "exam", id, actorId, "voided", now, ct);
        var result = await ReadExamAsync(connection, id, true, ct);
        await transaction.CommitAsync(ct);
        return result;
    }

    public async Task<VaccineInfo> CreateVaccineAsync(Guid unitId, Guid actorId, CreateVaccineRequest request, CancellationToken ct)
    {
        var cpf = Cpf(request.Cpf); var name = CommunityText.Line(request.Name, 1, NameMax); var note = CommunityText.OptionalLine(request.Note, NoteMax);
        if (request.Dose is not ("1" or "2" or "3" or "booster" or "single") || request.AppliedOn is not { } applied) throw new ApiInputException();
        CheckDate(applied);
        await using var connection = await databases.OpenAsync(unitId, ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await RequireActiveAsync(connection, cpf, ct);
        var id = Guid.NewGuid(); var now = time.GetUtcNow();
        await Exec(connection, "INSERT INTO nora_unit.health_vaccines(id,cpf,name,dose,applied_on,status,note,created_by,created_at) VALUES($1,$2,$3,$4,$5,'applied',$6,$7,$8)", ct, id, cpf, name, request.Dose, applied, (object?)note ?? DBNull.Value, actorId, now);
        await RecordAsync(connection, "vaccine", id, actorId, "created", now, ct);
        var result = await ReadVaccineAsync(connection, id, true, ct);
        await transaction.CommitAsync(ct);
        return result;
    }

    public async Task<VaccineInfo> VoidVaccineAsync(Guid unitId, Guid actorId, Guid id, VoidHealthRecordRequest request, CancellationToken ct)
    {
        var reason = VoidReason(id, request);
        await using var connection = await databases.OpenAsync(unitId, ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        if (await LockStatusAsync(connection, "health_vaccines", id, ct) == "voided") throw new ApiConflictException();
        var now = time.GetUtcNow();
        await Exec(connection, "UPDATE nora_unit.health_vaccines SET status='voided',void_reason=$2,voided_at=$3 WHERE id=$1", ct, id, reason, now);
        await RecordAsync(connection, "vaccine", id, actorId, "voided", now, ct);
        var result = await ReadVaccineAsync(connection, id, true, ct);
        await transaction.CommitAsync(ct);
        return result;
    }

    /// <summary>Exames e vacinas do paciente, do mais recente ao mais antigo. <paramref name="staffView"/> inclui o motivo da anulação; para o paciente ele nunca sai.</summary>
    /// <param name="requireRegistry">Equipe: o CPF precisa existir no cadastro da UBS (senão 404). O paciente já foi conferido pelo executor.</param>
    public async Task<HealthRecords> ListAsync(Guid unitId, string cpf, bool includeVoided, bool staffView, bool requireRegistry, CancellationToken ct)
    {
        await using var connection = await databases.OpenAsync(unitId, ct);
        if (requireRegistry)
            await using (var exists = new NpgsqlCommand("SELECT 1 FROM nora_unit.patient_registry WHERE cpf=$1", connection))
            {
                exists.Parameters.AddWithValue(cpf);
                if (await exists.ExecuteScalarAsync(ct) is null) throw new ApiNotFoundException();
            }
        var exams = new List<ExamInfo>(); var vaccines = new List<VaccineInfo>();
        await using (var query = new NpgsqlCommand(ExamSelect + " WHERE cpf=$1 AND ($2 OR status<>'voided') ORDER BY COALESCE(performed_on,(requested_at AT TIME ZONE 'UTC')::date) DESC,requested_at DESC,id", connection))
        {
            query.Parameters.AddWithValue(cpf); query.Parameters.AddWithValue(includeVoided);
            await using var reader = await query.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct)) exams.Add(Exam(reader, staffView));
        }
        await using (var query = new NpgsqlCommand(VaccineSelect + " WHERE cpf=$1 AND ($2 OR status<>'voided') ORDER BY applied_on DESC,created_at DESC,id", connection))
        {
            query.Parameters.AddWithValue(cpf); query.Parameters.AddWithValue(includeVoided);
            await using var reader = await query.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct)) vaccines.Add(Vaccine(reader, staffView));
        }
        return new(exams, vaccines);
    }

    // ---- validação ----

    public static string Cpf(string? value)
    {
        var cpf = AccountService.NormalizeCpf(value);
        return AccountService.ValidCpf(cpf) ? cpf : throw new ApiInputException();
    }

    private static string VoidReason(Guid id, VoidHealthRecordRequest request)
    {
        if (id == Guid.Empty || !request.Confirmed) throw new ApiInputException();
        return CommunityText.Line(request.Reason, ReasonMin, NoteMax);
    }

    /// <summary>Data de calendário sem fuso: de 1900 até amanhã em UTC (cobre qualquer fuso, sem aceitar data futura de verdade).</summary>
    private void CheckDate(DateOnly date)
    {
        if (date < EarliestDate || date > DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime).AddDays(1)) throw new ApiInputException();
    }

    // ---- acesso ----

    private static async Task RequireActiveAsync(NpgsqlConnection connection, string cpf, CancellationToken ct)
    {
        await using var exists = new NpgsqlCommand("SELECT is_active FROM nora_unit.patient_registry WHERE cpf=$1 FOR SHARE", connection);
        exists.Parameters.AddWithValue(cpf);
        if (await exists.ExecuteScalarAsync(ct) is not true) throw new ApiNotFoundException();
    }

    /// <summary><paramref name="table"/> é sempre uma constante deste arquivo, nunca entrada do cliente.</summary>
    private static async Task<string> LockStatusAsync(NpgsqlConnection connection, string table, Guid id, CancellationToken ct)
    {
        await using var select = new NpgsqlCommand($"SELECT status FROM nora_unit.{table} WHERE id=$1 FOR UPDATE", connection);
        select.Parameters.AddWithValue(id);
        return await select.ExecuteScalarAsync(ct) as string ?? throw new ApiNotFoundException();
    }

    private static async Task Exec(NpgsqlConnection connection, string sql, CancellationToken ct, params object[] values)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var value in values) command.Parameters.Add(new NpgsqlParameter { Value = value });
        await command.ExecuteNonQueryAsync(ct);
    }

    private static Task RecordAsync(NpgsqlConnection connection, string kind, Guid id, Guid actorId, string action, DateTimeOffset now, CancellationToken ct)
        => Exec(connection, "INSERT INTO nora_unit.health_record_events(kind,record_id,actor_id,action,occurred_at) VALUES($1,$2,$3,$4,$5)", ct, kind, id, actorId, action, now);

    private const string ExamSelect = "SELECT id,name,status,requested_at,performed_on,note,voided_at,void_reason FROM nora_unit.health_exams";
    private const string VaccineSelect = "SELECT id,name,dose,applied_on,status,note,voided_at,void_reason FROM nora_unit.health_vaccines";

    private static async Task<ExamInfo> ReadExamAsync(NpgsqlConnection connection, Guid id, bool staffView, CancellationToken ct)
    {
        await using var query = new NpgsqlCommand(ExamSelect + " WHERE id=$1", connection);
        query.Parameters.AddWithValue(id);
        await using var reader = await query.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Exam(reader, staffView) : throw new ApiNotFoundException();
    }

    private static async Task<VaccineInfo> ReadVaccineAsync(NpgsqlConnection connection, Guid id, bool staffView, CancellationToken ct)
    {
        await using var query = new NpgsqlCommand(VaccineSelect + " WHERE id=$1", connection);
        query.Parameters.AddWithValue(id);
        await using var reader = await query.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Vaccine(reader, staffView) : throw new ApiNotFoundException();
    }

    private static ExamInfo Exam(NpgsqlDataReader reader, bool staffView) => new(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetFieldValue<DateTimeOffset>(3),
        reader.IsDBNull(4) ? null : reader.GetFieldValue<DateOnly>(4), reader.IsDBNull(5) ? null : reader.GetString(5), reader.IsDBNull(6) ? null : reader.GetFieldValue<DateTimeOffset>(6),
        staffView && !reader.IsDBNull(7) ? reader.GetString(7) : null);

    private static VaccineInfo Vaccine(NpgsqlDataReader reader, bool staffView) => new(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetFieldValue<DateOnly>(3), reader.GetString(4),
        reader.IsDBNull(5) ? null : reader.GetString(5), reader.IsDBNull(6) ? null : reader.GetFieldValue<DateTimeOffset>(6), staffView && !reader.IsDBNull(7) ? reader.GetString(7) : null);
}
