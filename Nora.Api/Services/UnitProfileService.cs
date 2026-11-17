using System.Globalization;
using Npgsql;
using Nora.Api.DAOs;
using Nora.Api.Database;
using Nora.Api.Models;

namespace Nora.Api.Services;

/// <summary>
/// Atendimento ao público da UBS: horário de funcionamento por dia da semana (até 2 intervalos por dia, sem sobreposição) e texto livre de serviços, no banco central; as especialidades vêm da grade
/// da UBS (nunca digitadas). Só o administrador da UBS grava; a atendente, o paciente vinculado e o responsável leem.
/// </summary>
public sealed class UnitProfileService(DatabaseHelper central, UnitDatabaseRouter databases, SchedulingDao scheduling, SpecialtyDao specialties, SchedulingService schedulingService, TimeProvider time)
{
    public const int ServicesMax = 1000;
    private const int IntervalsPerDay = 2;

    /// <summary>Leitura própria (conexão central nova): app e OBS.</summary>
    public async Task<UnitProfile> ReadAsync(Guid unitId, CancellationToken ct)
    {
        await using var connection = await central.OpenAsync(ct);
        return (await ReadCoreAsync(connection, unitId, ct)).Profile;
    }

    /// <summary>Leitura do OBS: o perfil e a situação da aprovação das vagas.</summary>
    public async Task<ObsUnitProfile> ReadForObsAsync(Guid unitId, CancellationToken ct)
    {
        await using var connection = await central.OpenAsync(ct);
        var (profile, approvalSince) = await ReadCoreAsync(connection, unitId, ct);
        return new(profile.UnitId, profile.Informed, profile.OpeningHours, profile.Services, profile.Specialties, profile.UpdatedAt, approvalSince is not null, approvalSince);
    }

    /// <summary>
    /// Substitui tudo numa transação central (a do executor do administrador): a UBS é bloqueada para escrita, os horários são apagados e regravados e o texto e o carimbo são gravados juntos, então duas gravações
    /// simultâneas nunca misturam as duas versões (a última vence). Qualquer violação da validação → 400, sem gravar nada.
    /// </summary>
    public async Task<UnitProfile> ReplaceAsync(NpgsqlConnection connection, SessionInfo session, Guid unitId, UnitProfileRequest request, CancellationToken ct)
    {
        var hours = ValidateHours(request.OpeningHours); var services = ValidateServices(request.Services);
        await using (var lockRow = central.Command(connection, "SELECT 1 FROM nora.health_units WHERE id=$1 FOR UPDATE", unitId))
            if (await lockRow.ExecuteScalarAsync(ct) is null) throw new ApiNotFoundException();
        await central.ExecuteAsync(connection, "DELETE FROM nora.unit_opening_hours WHERE unit_id=$1", ct, unitId);
        foreach (var hour in hours)
            await central.ExecuteAsync(connection, "INSERT INTO nora.unit_opening_hours(unit_id,weekday,opens_at,closes_at) VALUES($1,$2,$3,$4)", ct, unitId, (short)hour.Weekday, hour.Opens, hour.Closes);
        await central.ExecuteAsync(connection, "UPDATE nora.health_units SET services_text=$2::text,profile_updated_at=$3,profile_updated_by=$4 WHERE id=$1", ct, unitId, (object?)services ?? DBNull.Value, time.GetUtcNow(), session.User.Id);
        return (await ReadCoreAsync(connection, unitId, ct)).Profile;
    }

    private async Task<(UnitProfile Profile, DateTimeOffset? ApprovalSince)> ReadCoreAsync(NpgsqlConnection connection, Guid unitId, CancellationToken ct)
    {
        string? services; DateTimeOffset? updatedAt, approvalSince; bool ready;
        await using (var unit = central.Command(connection, "SELECT services_text,profile_updated_at,slot_approval_since,database_ready_at IS NOT NULL FROM nora.health_units WHERE id=$1", unitId))
        await using (var reader = await unit.ExecuteReaderAsync(ct))
        {
            if (!await reader.ReadAsync(ct)) throw new ApiNotFoundException();
            services = reader.IsDBNull(0) ? null : reader.GetString(0); updatedAt = reader.IsDBNull(1) ? null : reader.GetFieldValue<DateTimeOffset>(1);
            approvalSince = reader.IsDBNull(2) ? null : reader.GetFieldValue<DateTimeOffset>(2); ready = reader.GetBoolean(3);
        }
        var hours = new List<OpeningHour>();
        await using (var query = central.Command(connection, "SELECT weekday,opens_at,closes_at FROM nora.unit_opening_hours WHERE unit_id=$1 ORDER BY weekday,opens_at", unitId))
        await using (var reader = await query.ExecuteReaderAsync(ct))
            while (await reader.ReadAsync(ct)) hours.Add(new(reader.GetInt16(0), Format(reader.GetFieldValue<TimeOnly>(1)), Format(reader.GetFieldValue<TimeOnly>(2))));
        return (new(unitId, updatedAt is not null, hours, services, await SpecialtiesAsync(unitId, ready, ct), updatedAt), approvalSince);
    }

    /// <summary>Especialidades da grade vigente (vem do banco da UBS); UBS sem banco pronto ou sem grade → vazio. Só especialidades ativas, por nome.</summary>
    private async Task<List<SpecialtyRef>> SpecialtiesAsync(Guid unitId, bool ready, CancellationToken ct)
    {
        if (!ready && !databases.HasExplicitConnection(unitId)) return [];
        var ids = await scheduling.ListGridSpecialtyIdsAsync(unitId, schedulingService.Today(), ct);
        if (ids.Count == 0) return [];
        var found = await specialties.FindAsync(ids, ct);
        return found.Values.Where(item => item.IsActive).OrderBy(item => item.Name, StringComparer.Create(new CultureInfo("pt-BR"), ignoreCase: true)).ThenBy(item => item.Id).Select(item => new SpecialtyRef(item.Id, item.Name)).ToList();
    }

    private static string Format(TimeOnly time) => time.ToString("HH:mm", CultureInfo.InvariantCulture);

    /// <summary>Até 2 intervalos por dia, <c>closes &gt; opens</c>, sem sobreposição (encostar pode); "HH:mm" estrito; dia da semana de 0 a 6. Lista ausente → 400; vazia → fechada todos os dias.</summary>
    internal static List<(int Weekday, TimeOnly Opens, TimeOnly Closes)> ValidateHours(List<OpeningHourRequest>? request)
    {
        if (request is null || request.Count > 7 * IntervalsPerDay) throw new ApiInputException();
        var hours = new List<(int Weekday, TimeOnly Opens, TimeOnly Closes)>();
        foreach (var item in request)
        {
            if (item?.Weekday is not (>= 0 and <= 6) || !TimeOnly.TryParseExact(item.Opens, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var opens)
                || !TimeOnly.TryParseExact(item.Closes, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var closes) || closes <= opens) throw new ApiInputException();
            hours.Add((item.Weekday.Value, opens, closes));
        }
        hours.Sort((a, b) => a.Weekday != b.Weekday ? a.Weekday.CompareTo(b.Weekday) : a.Opens.CompareTo(b.Opens));
        for (var i = 1; i < hours.Count; i++)
            if (hours[i].Weekday == hours[i - 1].Weekday && hours[i].Opens < hours[i - 1].Closes) throw new ApiInputException();
        if (hours.GroupBy(hour => hour.Weekday).Any(day => day.Count() > IntervalsPerDay)) throw new ApiInputException();
        return hours;
    }

    /// <summary>Texto puro sem espaços nas pontas, até 1.000 caracteres, quebras de linha permitidas e nenhum outro caractere de controle; vazio vira nulo.</summary>
    internal static string? ValidateServices(string? value)
    {
        var text = value?.Trim() ?? "";
        if (text.Length > ServicesMax || text.Any(c => char.IsControl(c) && c is not ('\n' or '\r'))) throw new ApiInputException();
        return text.Length == 0 ? null : text;
    }
}
