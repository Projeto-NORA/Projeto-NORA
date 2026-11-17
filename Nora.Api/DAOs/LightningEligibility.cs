using Npgsql;
using Nora.Api.Database;

namespace Nora.Api.DAOs;

/// <summary>
/// Quem pode receber uma oferta de Vaga Relâmpago, num só lugar para o distribuidor automático e o "chamar próximo". Elegível: conta de paciente ativa vinculada à UBS, ou dependente com vínculo de Família aprovado e
/// responsável ativo (a oferta vai ao responsável); mais, na UBS, entrada <c>waiting</c> da especialidade, cadastro ativo, sem oferta pendente e (com horário) sem consulta na UBS que cruze o horário. Ordem <c>entered_at, id</c>.
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

    /// <summary>Primeira entrada elegível para o horário, travada (<c>FOR UPDATE</c>) na transação da UBS; nulo se ninguém pode receber.</summary>
    public static async Task<long?> PickAsync(NpgsqlConnection unit, string specialty, IReadOnlyCollection<string> eligible, DateTimeOffset start, DateTimeOffset end, CancellationToken ct)
    {
        await using var query = new NpgsqlCommand("SELECT q.id " + Candidates + NoOverlap + "ORDER BY q.entered_at,q.id LIMIT 1 FOR UPDATE OF q", unit);
        query.Parameters.AddWithValue(specialty); query.Parameters.AddWithValue(eligible.ToArray()); query.Parameters.AddWithValue(start); query.Parameters.AddWithValue(end);
        return await query.ExecuteScalarAsync(ct) is long id ? id : null;
    }

    /// <summary>Prévia do primeiro da fila que pode receber (sem considerar horário, sem travar): a oferta real pode ir a outra pessoa se a fila mudar.</summary>
    public static async Task<(long EntryId, string Name, DateTimeOffset EnteredAt)?> PreviewAsync(NpgsqlConnection unit, string specialty, IReadOnlyCollection<string> eligible, CancellationToken ct)
    {
        await using var query = new NpgsqlCommand("SELECT q.id,(SELECT p.full_name FROM nora_unit.patient_registry p WHERE p.cpf=q.cpf),q.entered_at " + Candidates + "ORDER BY q.entered_at,q.id LIMIT 1", unit);
        query.Parameters.AddWithValue(specialty); query.Parameters.AddWithValue(eligible.ToArray());
        await using var reader = await query.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? (reader.GetInt64(0), reader.GetString(1), reader.GetFieldValue<DateTimeOffset>(2)) : null;
    }
}
