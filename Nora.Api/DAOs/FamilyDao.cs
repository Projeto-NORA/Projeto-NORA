using Npgsql;
using Nora.Api.Database;

namespace Nora.Api.DAOs;

/// <summary>Linha crua de um pedido ou vínculo de Família (banco central), com os nomes que as visões precisam.</summary>
internal sealed record FamilyLinkRow(Guid Id, Guid GuardianId, string GuardianName, string GuardianCpf, string DependentCpf, Guid UnitId, string UnitName, string Status, DateTimeOffset RequestedAt,
    string? DecidedByName, DateTimeOffset? DecidedAt, string? DecisionReason, DateTimeOffset? ClosedAt);

/// <summary>SQL da Família (pedidos e vínculos de responsável e dependente, banco central). Quem chama já autorizou a conta; as transições de estado são condicionais ao estado atual.</summary>
public sealed class FamilyDao(DatabaseHelper database)
{
    /// <summary>Máximo de pedidos pendentes e vínculos aprovados de um responsável juntos.</summary>
    public const int MaxActivePerGuardian = 20;

    private const string Columns = "l.id,l.guardian_id,g.full_name,g.cpf,l.dependent_cpf,l.unit_id,u.name,l.status,l.requested_at,d.full_name,l.decided_at,l.decision_reason,l.closed_at";
    private const string From = "FROM nora.family_links l JOIN nora.accounts g ON g.id=l.guardian_id JOIN nora.health_units u ON u.id=l.unit_id LEFT JOIN nora.accounts d ON d.id=l.decided_by";

    private static FamilyLinkRow Read(NpgsqlDataReader reader) => new(reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetGuid(5), reader.GetString(6), reader.GetString(7),
        reader.GetFieldValue<DateTimeOffset>(8), reader.IsDBNull(9) ? null : reader.GetString(9), reader.IsDBNull(10) ? null : reader.GetFieldValue<DateTimeOffset>(10), reader.IsDBNull(11) ? null : reader.GetString(11),
        reader.IsDBNull(12) ? null : reader.GetFieldValue<DateTimeOffset>(12));

    /// <summary>Lock de transação por responsável: serializa pedidos do mesmo responsável (limite e duplicidade) sem bloquear os demais.</summary>
    internal Task LockGuardianAsync(NpgsqlConnection connection, Guid guardianId, CancellationToken ct) =>
        database.ExecuteAsync(connection, "SELECT pg_advisory_xact_lock(hashtextextended($1,62419830))", ct, guardianId.ToString("N"));

    internal async Task<(long Active, bool Duplicate)> CountActiveAsync(NpgsqlConnection connection, Guid guardianId, string dependentCpf, Guid unitId, CancellationToken ct)
    {
        await using var query = database.Command(connection, "SELECT count(*),COALESCE(bool_or(dependent_cpf=$2 AND unit_id=$3),false) FROM nora.family_links WHERE guardian_id=$1 AND status IN ('pending','approved')", guardianId, dependentCpf, unitId);
        await using var reader = await query.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        return (reader.GetInt64(0), reader.GetBoolean(1));
    }

    internal Task<int> InsertAsync(NpgsqlConnection connection, Guid id, Guid guardianId, string dependentCpf, Guid unitId, DateTimeOffset now, CancellationToken ct) =>
        database.ExecuteAsync(connection, "INSERT INTO nora.family_links(id,guardian_id,dependent_cpf,unit_id,status,requested_at) VALUES($1,$2,$3,$4,'pending',$5)", ct, id, guardianId, dependentCpf, unitId, now);

    /// <summary>Pedidos e vínculos do responsável, os mais recentes primeiro.</summary>
    internal async Task<List<FamilyLinkRow>> ListForGuardianAsync(Guid guardianId, CancellationToken ct)
    {
        await using var connection = await database.OpenAsync(ct);
        await using var query = database.Command(connection, $"SELECT {Columns} {From} WHERE l.guardian_id=$1 ORDER BY l.requested_at DESC,l.id", guardianId);
        return await ReadAllAsync(query, ct);
    }

    /// <summary>Pedidos de uma UBS por situação, do mais antigo ao mais novo (fila de conferência).</summary>
    internal async Task<List<FamilyLinkRow>> ListForUnitAsync(Guid unitId, string status, int limit, CancellationToken ct)
    {
        await using var connection = await database.OpenAsync(ct);
        await using var query = database.Command(connection, $"SELECT {Columns} {From} WHERE l.unit_id=$1 AND l.status=$2 ORDER BY l.requested_at,l.id LIMIT $3", unitId, status, limit);
        return await ReadAllAsync(query, ct);
    }

    /// <summary>Uma linha bloqueada para escrita, ou nula se o vínculo não existe (com filtros opcionais de dono e de UBS, aplicados no SQL).</summary>
    internal async Task<FamilyLinkRow?> LockAsync(NpgsqlConnection connection, Guid id, Guid? guardianId, Guid? unitId, CancellationToken ct)
    {
        await using var query = database.Command(connection, $"SELECT {Columns} {From} WHERE l.id=$1 AND ($2::uuid IS NULL OR l.guardian_id=$2) AND ($3::uuid IS NULL OR l.unit_id=$3) FOR UPDATE OF l", id, (object?)guardianId ?? DBNull.Value, (object?)unitId ?? DBNull.Value);
        var rows = await ReadAllAsync(query, ct);
        return rows.Count == 0 ? null : rows[0];
    }

    internal async Task<FamilyLinkRow> ReadAsync(NpgsqlConnection connection, Guid id, CancellationToken ct)
    {
        await using var query = database.Command(connection, $"SELECT {Columns} {From} WHERE l.id=$1", id);
        return (await ReadAllAsync(query, ct)).Single();
    }

    /// <summary>Aprovação ou recusa pela equipe (a transição é condicional a <c>pending</c>, travada pelo chamador).</summary>
    internal Task<int> DecideAsync(NpgsqlConnection connection, Guid id, string status, Guid actorId, DateTimeOffset now, string? reason, CancellationToken ct) =>
        database.ExecuteAsync(connection, "UPDATE nora.family_links SET status=$2,decided_by=$3,decided_at=$4,decision_reason=$5 WHERE id=$1 AND status='pending'", ct, id, status, actorId, now, (object?)reason ?? DBNull.Value);

    /// <summary>Cancelamento do pedido pelo responsável ou encerramento do vínculo (responsável ou equipe): fecha o vínculo e guarda quem fechou e quando.</summary>
    internal Task<int> CloseAsync(NpgsqlConnection connection, Guid id, string from, string to, Guid actorId, DateTimeOffset now, CancellationToken ct) =>
        database.ExecuteAsync(connection, "UPDATE nora.family_links SET status=$3,closed_by=$4,closed_at=$5 WHERE id=$1 AND status=$2", ct, id, from, to, actorId, now);

    /// <summary>
    /// UBS onde o CPF pode ter consultas marcadas pelo app, para o limite de uma consulta futura por especialidade na rede: UBS de vínculos de família aprovados ou já encerrados (de qualquer responsável; encerrar
    /// o vínculo não cancela a consulta, que continua contando) e UBS vinculadas à conta de paciente do próprio CPF, se existir. Só entram as que existem; quem filtra as de banco pronto é o chamador.
    /// </summary>
    internal async Task<List<Guid>> NetworkUnitIdsAsync(string dependentCpf, CancellationToken ct)
    {
        await using var connection = await database.OpenAsync(ct);
        await using var query = database.Command(connection, "SELECT unit_id FROM nora.family_links WHERE dependent_cpf=$1 AND status IN ('approved','ended') UNION SELECT pu.unit_id FROM nora.patient_units pu JOIN nora.accounts a ON a.id=pu.patient_id WHERE a.scope='patient' AND a.cpf=$1", dependentCpf);
        await using var reader = await query.ExecuteReaderAsync(ct);
        var ids = new List<Guid>();
        while (await reader.ReadAsync(ct)) ids.Add(reader.GetGuid(0));
        return ids;
    }

    /// <summary>
    /// Se o CPF ainda tem alguém que responda por ele nesta UBS (mesma regra de elegibilidade das ofertas): outro vínculo aprovado com responsável ativo, ou conta de paciente ativa vinculada à UBS.
    /// Chamado na transação do encerramento, depois de fechar o vínculo.
    /// </summary>
    internal async Task<bool> HasAccessAsync(NpgsqlConnection connection, Guid unitId, string dependentCpf, CancellationToken ct)
    {
        await using var query = database.Command(connection, "SELECT EXISTS(SELECT 1 FROM nora.family_links l JOIN nora.accounts g ON g.id=l.guardian_id WHERE l.unit_id=$1 AND l.dependent_cpf=$2 AND l.status='approved' AND g.scope='patient' AND g.is_active) OR EXISTS(SELECT 1 FROM nora.patient_units p JOIN nora.accounts a ON a.id=p.patient_id WHERE p.unit_id=$1 AND a.scope='patient' AND a.is_active AND a.cpf=$2)", unitId, dependentCpf);
        return await query.ExecuteScalarAsync(ct) is true;
    }

    /// <summary>Vínculos aprovados do responsável (dependente, UBS), bloqueados para leitura na transação do chamador, para a consulta agregada de ofertas.</summary>
    internal async Task<List<(Guid Id, string DependentCpf, Guid UnitId, string UnitName)>> LockApprovedAsync(NpgsqlConnection connection, Guid guardianId, CancellationToken ct)
    {
        await using var query = database.Command(connection, "SELECT l.id,l.dependent_cpf,l.unit_id,u.name FROM nora.family_links l JOIN nora.health_units u ON u.id=l.unit_id WHERE l.guardian_id=$1 AND l.status='approved' ORDER BY l.requested_at,l.id FOR SHARE OF l", guardianId);
        await using var reader = await query.ExecuteReaderAsync(ct);
        var rows = new List<(Guid, string, Guid, string)>();
        while (await reader.ReadAsync(ct)) rows.Add((reader.GetGuid(0), reader.GetString(1), reader.GetGuid(2), reader.GetString(3)));
        return rows;
    }

    private static async Task<List<FamilyLinkRow>> ReadAllAsync(NpgsqlCommand query, CancellationToken ct)
    {
        await using var reader = await query.ExecuteReaderAsync(ct);
        var rows = new List<FamilyLinkRow>();
        while (await reader.ReadAsync(ct)) rows.Add(Read(reader));
        return rows;
    }
}
