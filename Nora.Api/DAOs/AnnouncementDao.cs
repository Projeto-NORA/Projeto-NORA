using Npgsql;
using Nora.Api.Database;
using Nora.Api.Models;
using Nora.Api.Services;

namespace Nora.Api.DAOs;

/// <summary>
/// Comunicados no banco central. O alcance (rede, regiões, UBS) é avaliado na leitura contra as concessões (equipe) ou os vínculos (paciente)
/// de hoje, então não há dado pessoal no comunicado. As leituras do app percorrem só o índice parcial dos comunicados em vigor, por seq.
/// </summary>
public sealed class AnnouncementDao(DatabaseHelper central)
{
    /// <summary>Todos os publicadores serializam por este lock de transação antes do INSERT: a ordem de seq passa a ser a ordem de commit.</summary>
    private const long PublishLock = 62419830;
    private const string StaffUnits = "SELECT unit_id FROM nora.staff_units WHERE account_id=$1";
    private const string PatientUnits = "SELECT unit_id FROM nora.patient_units WHERE patient_id=$1";

    /// <summary>
    /// Predicado de visibilidade sobre <c>nora.announcements a</c>; $1 conta, $2 agora, $3 público do leitor ('staff' ou 'patients'). Toda a rede só
    /// alcança quem tem ao menos uma UBS (concessão ou vínculo); região e UBS específicas, só quem tem UBS nelas.
    /// </summary>
    private static string Visible(bool staff)
    {
        var units = staff ? StaffUnits : PatientUnits;
        return $"a.closed_at IS NULL AND (a.expires_at IS NULL OR a.expires_at>$2) AND a.audience IN ('both',$3) AND ((a.scope='all' AND EXISTS({units})) " +
            $"OR (a.scope='units' AND EXISTS(SELECT 1 FROM nora.announcement_units au WHERE au.announcement_id=a.id AND au.unit_id IN ({units}))) " +
            $"OR (a.scope='region' AND EXISTS(SELECT 1 FROM nora.announcement_regions ar JOIN nora.health_units h ON lower(btrim(h.region))=ar.region_key WHERE ar.announcement_id=a.id AND h.id IN ({units}))))";
    }

    // ---- Secretaria ----

    public async Task<AnnouncementSummary> CreateAsync(Guid actorId, AnnouncementDraft draft, DateTimeOffset now, CancellationToken ct)
    {
        await using var connection = await central.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await central.ExecuteAsync(connection, "SELECT pg_advisory_xact_lock($1)", ct, PublishLock);
        string[] regionKeys = [], regionLabels = [];
        if (draft.Scope == "units")
        {
            await using var count = central.Command(connection, "SELECT count(*) FROM nora.health_units WHERE id=ANY($1)", draft.UnitIds);
            if ((long)(await count.ExecuteScalarAsync(ct))! != draft.UnitIds.Length) throw new ApiInputException();
        }
        else if (draft.Scope == "region")
        {
            // A chave sai do próprio banco (lower/btrim), igual à comparação feita na leitura; o rótulo é a grafia da UBS cadastrada.
            await using var resolve = central.Command(connection, "SELECT w.k,(SELECT btrim(h.region) FROM nora.health_units h WHERE lower(btrim(h.region))=w.k ORDER BY h.region LIMIT 1) FROM (SELECT DISTINCT lower(btrim(x)) AS k FROM unnest($1::text[]) AS x) w ORDER BY w.k", (object)draft.Regions);
            var keys = new List<string>(); var labels = new List<string>();
            await using (var reader = await resolve.ExecuteReaderAsync(ct))
                while (await reader.ReadAsync(ct))
                {
                    if (reader.IsDBNull(1)) throw new ApiInputException();
                    keys.Add(reader.GetString(0)); labels.Add(reader.GetString(1));
                }
            regionKeys = [.. keys]; regionLabels = [.. labels];
        }
        var id = Guid.NewGuid();
        await central.ExecuteAsync(connection, "INSERT INTO nora.announcements(id,title,body,audience,scope,created_by,created_at,expires_at) VALUES($1,$2,$3,$4,$5,$6,$7,$8)", ct,
            id, draft.Title, draft.Body, draft.Audience, draft.Scope, actorId, now.ToUniversalTime(), draft.ExpiresAt is { } expires ? (object)expires.ToUniversalTime() : DBNull.Value);
        if (regionKeys.Length > 0)
            await central.ExecuteAsync(connection, "INSERT INTO nora.announcement_regions(announcement_id,region_key,region_label) SELECT $1,t.k,t.l FROM unnest($2::text[],$3::text[]) AS t(k,l)", ct, id, regionKeys, regionLabels);
        if (draft.Scope == "units")
            await central.ExecuteAsync(connection, "INSERT INTO nora.announcement_units(announcement_id,unit_id) SELECT $1,u FROM unnest($2::uuid[]) AS u", ct, id, draft.UnitIds);
        var result = (await ReadSummariesAsync(connection, "a.id=$1", now, ct, id)).Single();
        await transaction.CommitAsync(ct);
        return result;
    }

    /// <summary>Os comunicados da própria conta, do mais recente ao mais antigo; <paramref name="before"/> é o cursor (seq) da página anterior.</summary>
    public async Task<AnnouncementSummaryPage> ListOwnAsync(Guid actorId, long? before, int limit, DateTimeOffset now, CancellationToken ct)
    {
        await using var connection = await central.OpenAsync(ct);
        var rows = await ReadSummariesAsync(connection, "a.created_by=$1 AND a.seq<$2 ORDER BY a.seq DESC LIMIT $3", now, ct, actorId, before ?? long.MaxValue, limit + 1);
        var more = rows.Count > limit;
        if (more) rows.RemoveAt(limit);
        return new(rows, more ? rows[^1].Sequence : null);
    }

    /// <summary>Encerra (não apaga) o comunicado da própria conta. Inexistente ou de outra conta → 404; já encerrado → 409.</summary>
    public async Task CloseAsync(Guid actorId, Guid id, DateTimeOffset now, CancellationToken ct)
    {
        await using var connection = await central.OpenAsync(ct);
        if (await central.ExecuteAsync(connection, "UPDATE nora.announcements SET closed_at=$3,closed_by=$2 WHERE id=$1 AND created_by=$2 AND closed_at IS NULL", ct, id, actorId, now.ToUniversalTime()) == 1) return;
        await using var exists = central.Command(connection, "SELECT 1 FROM nora.announcements WHERE id=$1 AND created_by=$2", id, actorId);
        throw await exists.ExecuteScalarAsync(ct) is null ? new ApiNotFoundException() : new ApiConflictException();
    }

    /// <summary>Contagens por público e os nomes (sem e-mail) das contas de equipe que leram; limite de 500 nomes.</summary>
    public async Task<AnnouncementReads> ReadsAsync(Guid actorId, Guid id, DateTimeOffset now, CancellationToken ct)
    {
        await using var connection = await central.OpenAsync(ct);
        var summary = (await ReadSummariesAsync(connection, "a.id=$1 AND a.created_by=$2", now, ct, id, actorId)).SingleOrDefault() ?? throw new ApiNotFoundException();
        var staff = new List<AnnouncementReader>();
        await using var query = central.Command(connection, "SELECT x.full_name,r.read_at FROM nora.announcement_reads r JOIN nora.accounts x ON x.id=r.account_id WHERE r.announcement_id=$1 AND x.scope='obs' ORDER BY r.read_at,x.full_name,x.id LIMIT 500", id);
        await using var reader = await query.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) staff.Add(new(reader.GetString(0), reader.GetFieldValue<DateTimeOffset>(1)));
        return new(summary.StaffReads, summary.PatientReads, staff);
    }

    private async Task<List<AnnouncementSummary>> ReadSummariesAsync(NpgsqlConnection connection, string where, DateTimeOffset now, CancellationToken ct, params object[] values)
    {
        var rows = new List<(Guid Id, long Seq, string Title, string Body, string Audience, string Scope, DateTimeOffset Created, DateTimeOffset? Expires, DateTimeOffset? Closed, long StaffReads, long PatientReads)>();
        await using (var query = central.Command(connection,
            "SELECT a.id,a.seq,a.title,a.body,a.audience,a.scope,a.created_at,a.expires_at,a.closed_at," +
            "(SELECT count(*) FROM nora.announcement_reads r JOIN nora.accounts x ON x.id=r.account_id WHERE r.announcement_id=a.id AND x.scope='obs')," +
            "(SELECT count(*) FROM nora.announcement_reads r JOIN nora.accounts x ON x.id=r.account_id WHERE r.announcement_id=a.id AND x.scope='patient') " +
            "FROM nora.announcements a WHERE " + where, values))
        await using (var reader = await query.ExecuteReaderAsync(ct))
            while (await reader.ReadAsync(ct))
                rows.Add((reader.GetGuid(0), reader.GetInt64(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetString(5), reader.GetFieldValue<DateTimeOffset>(6),
                    reader.IsDBNull(7) ? null : reader.GetFieldValue<DateTimeOffset>(7), reader.IsDBNull(8) ? null : reader.GetFieldValue<DateTimeOffset>(8), reader.GetInt64(9), reader.GetInt64(10)));
        if (rows.Count == 0) return [];
        var ids = rows.Select(row => row.Id).ToArray();
        var regions = new Dictionary<Guid, List<string>>(); var units = new Dictionary<Guid, List<Guid>>();
        await using (var query = central.Command(connection, "SELECT announcement_id,region_label FROM nora.announcement_regions WHERE announcement_id=ANY($1) ORDER BY region_label", ids))
        await using (var reader = await query.ExecuteReaderAsync(ct))
            while (await reader.ReadAsync(ct)) Add(regions, reader.GetGuid(0), reader.GetString(1));
        await using (var query = central.Command(connection, "SELECT announcement_id,unit_id FROM nora.announcement_units WHERE announcement_id=ANY($1) ORDER BY unit_id", ids))
        await using (var reader = await query.ExecuteReaderAsync(ct))
            while (await reader.ReadAsync(ct)) Add(units, reader.GetGuid(0), reader.GetGuid(1));
        return rows.Select(row => new AnnouncementSummary(row.Id, row.Seq, row.Title, row.Body, row.Audience, row.Scope,
            regions.TryGetValue(row.Id, out var r) ? [.. r] : [], units.TryGetValue(row.Id, out var u) ? [.. u] : [],
            row.Created, row.Expires, row.Closed, row.Closed is not null ? "closed" : row.Expires is { } expires && expires <= now ? "expired" : "active", row.StaffReads, row.PatientReads)).ToList();

        static void Add<T>(Dictionary<Guid, List<T>> map, Guid id, T value)
        {
            if (!map.TryGetValue(id, out var list)) map[id] = list = [];
            list.Add(value);
        }
    }

    // ---- Equipe (OBS) e paciente ----

    /// <summary>
    /// Comunicados em vigor visíveis à conta, em ordem crescente de seq e só com seq &gt; <paramref name="after"/>. O teto (maior seq) é lido
    /// antes da consulta: como a ordem de seq é a de commit, nada abaixo dele ainda vai aparecer depois, então devolver esse teto como cursor
    /// não pula comunicado e permite que a próxima chamada, sem novidade, custe só uma leitura do índice.
    /// </summary>
    public async Task<AnnouncementPage> ListForAccountAsync(Guid accountId, bool staff, long after, int limit, DateTimeOffset now, CancellationToken ct)
    {
        await using var connection = await central.OpenAsync(ct);
        long ceiling;
        await using (var max = central.Command(connection, "SELECT COALESCE(max(seq),0) FROM nora.announcements"))
            ceiling = (long)(await max.ExecuteScalarAsync(ct))!;
        if (after >= ceiling) return new([], ceiling, false);
        var items = new List<AnnouncementItem>();
        await using (var query = central.Command(connection,
            "SELECT a.id,a.seq,a.title,a.body,a.created_at,a.expires_at,r.read_at FROM nora.announcements a " +
            "LEFT JOIN nora.announcement_reads r ON r.announcement_id=a.id AND r.account_id=$1 " +
            $"WHERE a.seq>$4 AND a.seq<=$5 AND {Visible(staff)} ORDER BY a.seq LIMIT $6",
            accountId, now.ToUniversalTime(), staff ? "staff" : "patients", after, ceiling, limit + 1))
        await using (var reader = await query.ExecuteReaderAsync(ct))
            while (await reader.ReadAsync(ct))
                items.Add(new(reader.GetGuid(0), reader.GetInt64(1), reader.GetString(2), reader.GetString(3), reader.GetFieldValue<DateTimeOffset>(4),
                    reader.IsDBNull(5) ? null : reader.GetFieldValue<DateTimeOffset>(5), !reader.IsDBNull(6), reader.IsDBNull(6) ? null : reader.GetFieldValue<DateTimeOffset>(6)));
        var more = items.Count > limit;
        if (more) items.RemoveAt(limit);
        return new(items, more ? items[^1].Sequence : ceiling, more);
    }

    /// <summary>Marca como lido (idempotente: a primeira leitura vale e o instante não muda). Comunicado que a conta não enxerga → 404.</summary>
    public async Task MarkReadAsync(Guid accountId, bool staff, Guid id, DateTimeOffset now, CancellationToken ct)
    {
        await using var connection = await central.OpenAsync(ct);
        await using (var visible = central.Command(connection, $"SELECT 1 FROM nora.announcements a WHERE a.id=$4 AND {Visible(staff)}", accountId, now.ToUniversalTime(), staff ? "staff" : "patients", id))
            if (await visible.ExecuteScalarAsync(ct) is null) throw new ApiNotFoundException();
        await central.ExecuteAsync(connection, "INSERT INTO nora.announcement_reads(announcement_id,account_id,read_at) VALUES($1,$2,$3) ON CONFLICT DO NOTHING", ct, id, accountId, now.ToUniversalTime());
    }
}
