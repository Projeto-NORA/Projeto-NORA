using Npgsql;
using Nora.Api.Database;
using Nora.Api.Services;

namespace Nora.Api.DAOs;

/// <summary>
/// Quem pode receber uma oferta de Vaga Relâmpago, num só lugar para o distribuidor automático e o "chamar próximo". Elegível: conta de paciente ativa vinculada à UBS, ou dependente com vínculo de Família aprovado e
/// responsável ativo (a oferta vai ao responsável); mais, na UBS, entrada <c>waiting</c> da especialidade, cadastro ativo, sem oferta pendente e (com horário) sem consulta na UBS que cruze o horário. Ordem de oferta: mais pontos de score primeiro, depois <c>entered_at, id</c>.
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

    private const string Candidates =
        "FROM nora_unit.waiting_list q WHERE q.status='waiting' AND lower(btrim(q.specialty))=lower(btrim($1)) AND q.cpf=ANY($2) " +
        "AND EXISTS(SELECT 1 FROM nora_unit.patient_registry p WHERE p.cpf=q.cpf AND p.is_active) " +
        "AND NOT EXISTS(SELECT 1 FROM nora_unit.lightning_offers o JOIN nora_unit.waiting_list other ON other.id=o.entry_id WHERE o.status='pending' AND other.cpf=q.cpf) ";
    private const string NoOverlap = "AND NOT EXISTS(SELECT 1 FROM nora_unit.appointments a WHERE a.cpf=q.cpf AND a.status IN ('scheduled','called','completed','missed') AND a.starts_at<$4 AND a.ends_at>$3) ";

    /// <summary>R-030: quem recusou ou deixou vencer a oferta desta vaga volta à fila na mesma posição, mas não recebe a mesma vaga de novo (a vaga segue para o próximo).</summary>
    private const string NotAlreadyOffered = "AND NOT EXISTS(SELECT 1 FROM nora_unit.lightning_offers r JOIN nora_unit.waiting_list rq ON rq.id=r.entry_id WHERE r.slot_id=$5 AND rq.cpf=q.cpf AND r.status IN ('declined','expired')) ";


    /// <summary>
    /// Candidatos na ordem de oferta: mais pontos primeiro (score em pontos do CPF na própria UBS, docs/api.md) e, em empate, a ordem de entrada na fila (<c>entered_at, id</c>). Quem não tem consulta encerrada vale 1000.
    /// Sem travar linha: quem decide trava a escolhida depois.
    /// </summary>
    private static async Task<List<(long Id, string Cpf, DateTimeOffset EnteredAt)>> RankAsync(NpgsqlConnection unit, string filter, object[] values, DateTimeOffset now, CancellationToken ct)
    {
        var candidates = new List<(long Id, string Cpf, DateTimeOffset EnteredAt)>();
        await using (var query = new NpgsqlCommand("SELECT q.id,q.cpf,q.entered_at " + filter + "ORDER BY q.entered_at,q.id", unit))
        {
            foreach (var value in values) query.Parameters.AddWithValue(value);
            await using var reader = await query.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct)) candidates.Add((reader.GetInt64(0), reader.GetString(1), reader.GetFieldValue<DateTimeOffset>(2)));
        }
        if (candidates.Count < 2) return candidates;
        var (from, to) = UnitRegistryReadService.AttendanceWindow(now);
        var tallies = await UnitMetricsDao.ReadAttendanceAsync(unit, from, to, candidates.Select(candidate => candidate.Cpf).Distinct().ToArray(), ct);
        return candidates.OrderByDescending(candidate => tallies.GetValueOrDefault(candidate.Cpf).Points).ToList();
    }

    /// <summary>
    /// Primeira entrada elegível para o horário na ordem de oferta (mais pontos, depois a ordem de entrada), travada (<c>FOR UPDATE</c>) na transação da UBS; nulo se ninguém pode receber. Com <paramref name="slotId"/> (vaga que já existe),
    /// quem já recusou ou deixou vencer a oferta dessa mesma vaga fica de fora; no "chamar próximo" a vaga ainda não existe e não há o que excluir. O chamador já segura o lock da fila da UBS.
    /// </summary>
    public static async Task<long?> PickAsync(NpgsqlConnection unit, string specialty, IReadOnlyCollection<string> eligible, DateTimeOffset start, DateTimeOffset end, DateTimeOffset now, CancellationToken ct, Guid? slotId = null)
    {
        var values = new List<object> { specialty, eligible.ToArray(), start, end };
        if (slotId is { } id) values.Add(id);
        foreach (var candidate in await RankAsync(unit, Candidates + NoOverlap + (slotId is null ? "" : NotAlreadyOffered), values.ToArray(), now, ct))
        {
            await using var lockRow = new NpgsqlCommand("SELECT id FROM nora_unit.waiting_list WHERE id=$1 AND status='waiting' FOR UPDATE", unit);
            lockRow.Parameters.AddWithValue(candidate.Id);
            if (await lockRow.ExecuteScalarAsync(ct) is long entry) return entry;
        }
        return null;
    }

    /// <summary>
    /// Prévia do primeiro da ordem de oferta que pode receber, sem travar: a oferta real pode ir a outra pessoa se a fila mudar. Com <paramref name="start"/> e <paramref name="end"/> usa o mesmo critério de horário do
    /// <see cref="PickAsync"/> (pula quem já tem consulta na UBS que cruze o horário); sem eles, ignora o horário.
    /// </summary>
    public static async Task<(long EntryId, string Name, DateTimeOffset EnteredAt)?> PreviewAsync(NpgsqlConnection unit, string specialty, IReadOnlyCollection<string> eligible, DateTimeOffset now, CancellationToken ct, DateTimeOffset? start = null, DateTimeOffset? end = null)
    {
        var timed = start is not null && end is not null;
        var values = new List<object> { specialty, eligible.ToArray() };
        if (start is { } from && end is { } to) { values.Add(from); values.Add(to); }
        if ((await RankAsync(unit, Candidates + (timed ? NoOverlap : ""), values.ToArray(), now, ct)).FirstOrDefault() is not { Id: > 0 } first) return null;
        await using var name = new NpgsqlCommand("SELECT full_name FROM nora_unit.patient_registry WHERE cpf=$1", unit);
        name.Parameters.AddWithValue(first.Cpf);
        return (first.Id, (string)(await name.ExecuteScalarAsync(ct))!, first.EnteredAt);
    }
}
