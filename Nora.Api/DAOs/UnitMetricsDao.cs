using System.Text.RegularExpressions;
using Npgsql;
using Nora.Api.Database;
using Nora.Api.Services;

namespace Nora.Api.DAOs;

/// <summary>Somas aditivas (nunca médias prontas), para que unidades, regiões e a rede se combinem sem erro.</summary>
internal sealed class MetricsCounters
{
    public long Scheduled, Called, Completed, Missed, Cancelled, CancelledByPatient, ActiveEntries, AcceptedEntries;
    public double Weight, WaitMinutes;
    public void Add(MetricsCounters other)
    {
        Scheduled += other.Scheduled; Called += other.Called; Completed += other.Completed; Missed += other.Missed; Cancelled += other.Cancelled;
        CancelledByPatient += other.CancelledByPatient; ActiveEntries += other.ActiveEntries; AcceptedEntries += other.AcceptedEntries;
        Weight += other.Weight; WaitMinutes += other.WaitMinutes;
    }
    public void ApplyStatus(string status, long count, bool byPatient, double weight)
    {
        switch (status)
        {
            case "scheduled": Scheduled += count; break;
            case "called": Called += count; break;
            case "completed": Completed += count; break;
            case "missed": Missed += count; break;
            case "cancelled": Cancelled += count; if (byPatient) { CancelledByPatient += count; Weight += weight; } break;
        }
    }
}

internal sealed class SpecialtyData
{
    public readonly Dictionary<string, long> Spellings = new(StringComparer.Ordinal);
    public readonly MetricsCounters Counters = new();
    public void Spell(string spelling, long count) => Spellings[spelling] = Spellings.GetValueOrDefault(spelling) + count;
    public void Add(SpecialtyData other) { foreach (var (spelling, count) in other.Spellings) Spell(spelling, count); Counters.Add(other.Counters); }
    /// <summary>Grafia mais frequente; empate resolvido pela ordem ordinal, para o resultado não variar.</summary>
    public string Representative() => Spellings.OrderByDescending(pair => pair.Value).ThenBy(pair => pair.Key, StringComparer.Ordinal).First().Key;
}

internal sealed class UnitMetricsData
{
    public readonly MetricsCounters Total = new();
    public readonly Dictionary<string, SpecialtyData> Specialties = new(StringComparer.Ordinal);
    public readonly SortedDictionary<DateOnly, MetricsCounters> Daily = [];
    public readonly HashSet<string> WaitingCpfs = new(StringComparer.Ordinal);
    public double? LongestActiveWaitMinutes;
    public long RegisteredSlots, AcceptedOffers, DeclinedOffers, ExpiredOffers;
    public SpecialtyData Specialty(string key) => Specialties.TryGetValue(key, out var data) ? data : Specialties[key] = new();
}

internal readonly record struct PatientTally(long Completed, long Missed, long CancelledByPatient, double Weight);

/// <summary>Leituras agregadas do banco de uma UBS. O chamador já autorizou o acesso à unidade.</summary>
public sealed partial class UnitMetricsDao
{
    [GeneratedRegex("^[A-Za-z][A-Za-z0-9_+/-]{0,63}$")] private static partial Regex ZonePattern();

    private const string CancelledByPatientJoin = "LEFT JOIN nora_unit.appointment_events e ON e.appointment_id=a.id AND a.status='cancelled' AND e.status='cancelled' AND e.source='patient'";
    private const string LeadMicros = "(EXTRACT(EPOCH FROM (a.starts_at - e.occurred_at)) * 1000000)::bigint";

    /// <summary>
    /// Espera média: entradas cuja saída por oferta aceita ocorreu na janela ($1 ≤ saída &lt; $2); $3 = especialidade ('' = todas).
    /// Parte dos eventos 'completed' da janela (índice waiting_list_events_status_time_idx, migração 011) e só então junta a fila; antes
    /// varria a fila inteira e buscava o último evento de cada entrada. Uma entrada só conclui uma vez, então o instante de saída
    /// dentro da janela é o próprio instante de saída da entrada.
    /// </summary>
    internal const string WaitSql = """
        SELECT lower(btrim(q.specialty)), btrim(q.specialty), count(*), COALESCE(sum(EXTRACT(EPOCH FROM (x.at - q.entered_at)) / 60.0), 0)::float8
        FROM (SELECT ev.entry_id, max(ev.occurred_at) AS at FROM nora_unit.waiting_list_events ev WHERE ev.status='completed' AND ev.occurred_at >= $1 AND ev.occurred_at < $2 GROUP BY ev.entry_id) x
        JOIN nora_unit.waiting_list q ON q.id=x.entry_id
        WHERE EXISTS (SELECT 1 FROM nora_unit.lightning_offers o WHERE o.entry_id=q.id AND o.status='accepted')
          AND ($3 = '' OR lower(btrim(q.specialty))=lower(btrim($3)))
        GROUP BY 1,2
        """;

    private readonly UnitDatabaseRouter databases;
    private readonly TimeProvider time;
    private readonly string zone;

    /// <summary>Fuso do dia da série diária: <c>Nora:TimeZone</c> (padrão America/Sao_Paulo). A existência no PostgreSQL é conferida na subida (TimeZoneStartupCheck).</summary>
    public static string ConfiguredZone(IConfiguration configuration) => configuration["Nora:TimeZone"] is { Length: > 0 } configured ? configured : "America/Sao_Paulo";

    public UnitMetricsDao(UnitDatabaseRouter databases, TimeProvider time, IConfiguration configuration)
    {
        this.databases = databases; this.time = time;
        zone = ConfiguredZone(configuration);
        if (!ZonePattern().IsMatch(zone)) throw new InvalidOperationException("Fuso horário configurado inválido.");
    }

    /// <summary>
    /// Janela por data marcada (from ≤ starts_at &lt; to); espera por oferta aceita com saída na janela; vagas criadas na janela.
    /// Uma viagem ao banco (NpgsqlBatch) com quatro consultas agrupadas. <paramref name="specialty"/> vazio = sem filtro.
    /// </summary>
    internal async Task<UnitMetricsData> ReadAsync(Guid unitId, DateTimeOffset from, DateTimeOffset to, string specialty, CancellationToken ct)
    {
        var start = from.ToUniversalTime(); var end = to.ToUniversalTime(); var now = time.GetUtcNow();
        await using var connection = await databases.OpenAsync(unitId, ct);
        await using var batch = new NpgsqlBatch(connection);
        batch.BatchCommands.Add(Statement($"""
            SELECT lower(btrim(a.specialty)), btrim(a.specialty), (a.starts_at AT TIME ZONE $3)::date, a.status, e.id IS NOT NULL, {LeadMicros}, count(*)
            FROM nora_unit.appointments a {CancelledByPatientJoin}
            WHERE a.starts_at >= $1 AND a.starts_at < $2 AND ($4 = '' OR lower(btrim(a.specialty))=lower(btrim($4)))
            GROUP BY 1,2,3,4,5,6
            """, start, end, zone, specialty));
        batch.BatchCommands.Add(Statement("""
            SELECT lower(btrim(q.specialty)), btrim(q.specialty), q.cpf, q.entered_at
            FROM nora_unit.waiting_list q
            WHERE q.status IN ('waiting','offered') AND ($1 = '' OR lower(btrim(q.specialty))=lower(btrim($1)))
            """, specialty));
        batch.BatchCommands.Add(Statement(WaitSql, start, end, specialty));
        batch.BatchCommands.Add(Statement("""
            SELECT count(DISTINCT s.id), count(o.id) FILTER (WHERE o.status='accepted'), count(o.id) FILTER (WHERE o.status='declined'), count(o.id) FILTER (WHERE o.status='expired')
            FROM nora_unit.lightning_slots s
            JOIN nora_unit.appointments a ON a.id=s.appointment_id
            LEFT JOIN nora_unit.lightning_offers o ON o.slot_id=s.id
            WHERE s.created_at >= $1 AND s.created_at < $2 AND ($3 = '' OR lower(btrim(a.specialty))=lower(btrim($3)))
            """, start, end, specialty));
        var data = new UnitMetricsData();
        await using var reader = await batch.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var count = reader.GetInt64(6); var byPatient = reader.GetBoolean(4);
            var weight = byPatient ? count * AttendanceScore.CancellationWeight(TimeSpan.FromTicks(reader.GetInt64(5) * 10)) : 0;
            var group = data.Specialty(reader.GetString(0));
            group.Spell(reader.GetString(1), count);
            var status = reader.GetString(3); var day = reader.GetFieldValue<DateOnly>(2);
            data.Total.ApplyStatus(status, count, byPatient, weight);
            group.Counters.ApplyStatus(status, count, byPatient, weight);
            if (!data.Daily.TryGetValue(day, out var daily)) data.Daily[day] = daily = new();
            daily.ApplyStatus(status, count, byPatient, weight);
        }
        await reader.NextResultAsync(ct);
        DateTimeOffset? oldest = null;
        while (await reader.ReadAsync(ct))
        {
            var group = data.Specialty(reader.GetString(0));
            group.Spell(reader.GetString(1), 1);
            data.Total.ActiveEntries++; group.Counters.ActiveEntries++;
            data.WaitingCpfs.Add(reader.GetString(2));
            var entered = reader.GetFieldValue<DateTimeOffset>(3);
            if (oldest is null || entered < oldest) oldest = entered;
        }
        if (oldest is { } first) data.LongestActiveWaitMinutes = Math.Max(0, (now - first).TotalMinutes);
        await reader.NextResultAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var group = data.Specialty(reader.GetString(0));
            group.Spell(reader.GetString(1), reader.GetInt64(2));
            var accepted = reader.GetInt64(2); var minutes = reader.GetDouble(3);
            data.Total.AcceptedEntries += accepted; data.Total.WaitMinutes += minutes;
            group.Counters.AcceptedEntries += accepted; group.Counters.WaitMinutes += minutes;
        }
        await reader.NextResultAsync(ct);
        if (await reader.ReadAsync(ct))
        {
            data.RegisteredSlots = reader.GetInt64(0); data.AcceptedOffers = reader.GetInt64(1); data.DeclinedOffers = reader.GetInt64(2); data.ExpiredOffers = reader.GetInt64(3);
        }
        return data;
    }

    /// <summary>
    /// Comparecimento por CPF numa só consulta agrupada (para a listagem inteira do OBS ou para um paciente). Só cancelamentos
    /// de origem 'patient' pesam; consultas scheduled/called não entram. <paramref name="cpf"/> vazio = todos os pacientes.
    /// </summary>
    internal async Task<Dictionary<string, PatientTally>> ReadAttendanceAsync(Guid unitId, DateTimeOffset from, DateTimeOffset to, string cpf, CancellationToken ct)
    {
        await using var connection = await databases.OpenAsync(unitId, ct);
        await using var query = new NpgsqlCommand($"""
            SELECT a.cpf, a.status, e.id IS NOT NULL, {LeadMicros}, count(*)
            FROM nora_unit.appointments a {CancelledByPatientJoin}
            WHERE a.starts_at >= $1 AND a.starts_at < $2 AND a.status IN ('completed','missed','cancelled') AND ($3 = '' OR a.cpf=$3)
            GROUP BY 1,2,3,4
            """, connection);
        query.Parameters.AddWithValue(from.ToUniversalTime()); query.Parameters.AddWithValue(to.ToUniversalTime()); query.Parameters.AddWithValue(cpf);
        await using var reader = await query.ExecuteReaderAsync(ct);
        var result = new Dictionary<string, PatientTally>(StringComparer.Ordinal);
        while (await reader.ReadAsync(ct))
        {
            var count = reader.GetInt64(4); var cpfValue = reader.GetString(0);
            var tally = result.GetValueOrDefault(cpfValue);
            result[cpfValue] = reader.GetString(1) switch
            {
                "completed" => tally with { Completed = tally.Completed + count },
                "missed" => tally with { Missed = tally.Missed + count },
                _ when reader.GetBoolean(2) => tally with { CancelledByPatient = tally.CancelledByPatient + count, Weight = tally.Weight + count * AttendanceScore.CancellationWeight(TimeSpan.FromTicks(reader.GetInt64(3) * 10)) },
                _ => tally
            };
        }
        return result;
    }

    private static NpgsqlBatchCommand Statement(string sql, params object[] values)
    {
        var command = new NpgsqlBatchCommand(sql);
        foreach (var value in values) command.Parameters.Add(new NpgsqlParameter { Value = value });
        return command;
    }
}
