using Npgsql;
using Nora.Api.Database;
using Nora.Api.Services;

namespace Nora.Api.DAOs;

/// <summary>
/// Quem pode receber uma oferta de Vaga Relâmpago (2026-11-24: automática, sem fila manual). Elegível: conta de paciente ativa vinculada à UBS, ou dependente com vínculo de Família aprovado e responsável ativo (a oferta vai ao
/// responsável); mais, na UBS, consulta <c>scheduled</c> da mesma especialidade com início posterior ao da vaga (é ela que será antecipada), cadastro ativo, sem oferta pendente, sem outra consulta que cruze o horário e sem ter
/// recusado ou deixado vencer a mesma vaga. Ordem: mais pontos de score, depois a consulta mais distante, depois o id.
/// </summary>
internal static class LightningEligibility
{
    /// <summary>
    /// CPFs elegíveis na central, com os vínculos e contas bloqueados para leitura (<c>FOR SHARE</c>) até o fim da transação do chamador: encerrar o vínculo ou desativar a conta espera. Cada CPF aparece uma vez.
    /// </summary>
    public static async Task<HashSet<string>> ReadAsync(DatabaseHelper central, NpgsqlConnection connection, Guid unitId, CancellationToken ct)
    {
        var eligible = new HashSet<string>(StringComparer.Ordinal);
        await using (var query = central.Command(connection, "SELECT a.cpf FROM nora.patient_units p JOIN nora.accounts a ON a.id=p.patient_id WHERE p.unit_id=$1 AND a.scope='patient' AND a.is_active ORDER BY a.id FOR SHARE OF a,p", unitId))
        await using (var reader = await query.ExecuteReaderAsync(ct))
            while (await reader.ReadAsync(ct)) eligible.Add(reader.GetString(0));
        await using (var family = central.Command(connection, "SELECT l.dependent_cpf FROM nora.family_links l JOIN nora.accounts g ON g.id=l.guardian_id WHERE l.unit_id=$1 AND l.status='approved' AND g.scope='patient' AND g.is_active ORDER BY l.id FOR SHARE OF l,g", unitId))
        await using (var reader = await family.ExecuteReaderAsync(ct))
            while (await reader.ReadAsync(ct)) eligible.Add(reader.GetString(0));
        return eligible;
    }

    /// <summary>
    /// Consultas que podem ser antecipadas para a vaga ($1 especialidade, $2 CPFs elegíveis, $3/$4 início e fim da vaga, $5 a vaga): consulta <c>scheduled</c> da mesma especialidade (qualquer profissional) com início posterior ao da
    /// vaga, cadastro ativo, sem oferta pendente, sem outra consulta que cruze o horário da vaga (a própria consulta que será antecipada não conta) e sem ter recusado ou deixado vencer esta mesma vaga.
    /// Uma linha por CPF: a consulta mais distante dele (é a que ganha mais ao antecipar).
    /// </summary>
    private const string Candidates =
        "SELECT DISTINCT ON (a.cpf) a.id,a.cpf,a.starts_at FROM nora_unit.appointments a " +
        "WHERE a.status='scheduled' AND a.starts_at>$3 AND lower(btrim(a.specialty))=lower(btrim($1)) AND a.cpf=ANY($2) " +
        "AND EXISTS(SELECT 1 FROM nora_unit.patient_registry p WHERE p.cpf=a.cpf AND p.is_active) " +
        "AND NOT EXISTS(SELECT 1 FROM nora_unit.lightning_offers o WHERE o.status='pending' AND o.cpf=a.cpf) " +
        "AND NOT EXISTS(SELECT 1 FROM nora_unit.appointments b WHERE b.cpf=a.cpf AND b.id<>a.id AND b.status IN ('scheduled','called','completed','missed') AND b.starts_at<$4 AND b.ends_at>$3) " +
        "AND NOT EXISTS(SELECT 1 FROM nora_unit.lightning_offers r WHERE r.slot_id=$5 AND r.cpf=a.cpf AND r.status IN ('declined','expired')) " +
        "ORDER BY a.cpf,a.starts_at DESC,a.id";

    /// <summary>Candidato escolhido: o CPF e a consulta que será antecipada.</summary>
    public sealed record Candidate(string Cpf, Guid AppointmentId, DateTimeOffset AppointmentStartsAt);

    /// <summary>
    /// Candidatos na ordem de oferta: mais pontos de score primeiro (no banco da própria UBS); empate → a consulta mais distante (ganha mais ao antecipar); depois o id da consulta. Sem travar linha.
    /// </summary>
    internal static async Task<List<Candidate>> RankAsync(NpgsqlConnection unit, string specialty, IReadOnlyCollection<string> eligible, DateTimeOffset start, DateTimeOffset end, Guid slotId, DateTimeOffset now, CancellationToken ct)
    {
        var candidates = new List<Candidate>();
        await using (var query = new NpgsqlCommand(Candidates, unit))
        {
            foreach (var value in new object[] { specialty, eligible.ToArray(), start, end, slotId }) query.Parameters.AddWithValue(value);
            await using var reader = await query.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct)) candidates.Add(new(reader.GetString(1), reader.GetGuid(0), reader.GetFieldValue<DateTimeOffset>(2)));
        }
        if (candidates.Count == 0) return candidates;
        var (from, to) = UnitRegistryReadService.AttendanceWindow(now);
        var tallies = await UnitMetricsDao.ReadAttendanceAsync(unit, from, to, candidates.Select(candidate => candidate.Cpf).ToArray(), ct);
        return candidates.OrderByDescending(candidate => tallies.GetValueOrDefault(candidate.Cpf).Points).ThenByDescending(candidate => candidate.AppointmentStartsAt).ThenBy(candidate => candidate.AppointmentId).ToList();
    }

    /// <summary>
    /// Primeiro candidato da ordem de oferta, com a consulta que será antecipada travada (<c>FOR UPDATE</c>) e ainda <c>scheduled</c>; nulo se ninguém pode receber. O chamador já segura o lock da fila da UBS.
    /// </summary>
    public static async Task<Candidate?> PickAsync(NpgsqlConnection unit, Guid slotId, string specialty, IReadOnlyCollection<string> eligible, DateTimeOffset start, DateTimeOffset end, DateTimeOffset now, CancellationToken ct)
    {
        foreach (var candidate in await RankAsync(unit, specialty, eligible, start, end, slotId, now, ct))
        {
            await using var lockRow = new NpgsqlCommand("SELECT id FROM nora_unit.appointments WHERE id=$1 AND status='scheduled' FOR UPDATE", unit);
            lockRow.Parameters.AddWithValue(candidate.AppointmentId);
            if (await lockRow.ExecuteScalarAsync(ct) is Guid) return candidate;
        }
        return null;
    }
}
