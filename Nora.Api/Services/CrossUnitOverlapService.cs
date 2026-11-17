using Npgsql;
using Nora.Api.Database;

namespace Nora.Api.Services;

/// <summary>
/// Conflito de horário entre UBS: a mesma pessoa (CPF) não pode ter duas consultas que se sobrepõem em UBS diferentes. Consulta que conta: <c>scheduled</c>, <c>called</c>, <c>completed</c> ou <c>missed</c>
/// com <c>starts_at &lt; fim</c> e <c>ends_at &gt; início</c> (encostar não sobrepõe). A garantia contra corrida vem do <see cref="CpfLock"/>: o executor segura o lock de CPF <c>62419805</c> até depois do commit da
/// UBS, então quem chega depois enxerga a consulta já confirmada. Banco de outra UBS fora do ar → 503 (falha fechada, nunca "passou"); desatualizado → 503 <c>unit-database-outdated</c>.
/// O erro nunca diz qual é a outra UBS. Fica de fora: consultas criadas por versões anteriores que já se sobrepõem, e a rota legada global.
/// </summary>
public sealed class CrossUnitOverlapService(DatabaseHelper central, UnitDatabaseRouter databases)
{
    /// <summary>
    /// Confere as outras UBS onde o CPF tem cadastro (índice central, na conexão do lock, sem pedir outra conexão central). Chamar depois da conferência da própria UBS e antes do INSERT.
    /// Lança 409 <c>other-unit-overlap</c> se achar consulta que se sobrepõe.
    /// </summary>
    public async Task EnsureFreeAsync(CpfLock cpfLock, Guid unitId, DateTimeOffset start, DateTimeOffset end, CancellationToken ct)
    {
        var others = new List<Guid>();
        await using (var query = central.Command(cpfLock.Central, "SELECT i.unit_id,u.database_ready_at IS NOT NULL FROM nora.unit_registry_index i JOIN nora.health_units u ON u.id=i.unit_id WHERE i.cpf=$1 AND i.unit_id<>$2 ORDER BY i.unit_id", cpfLock.Cpf, unitId))
        await using (var reader = await query.ExecuteReaderAsync(ct))
            while (await reader.ReadAsync(ct))
                // UBS sem banco criado ("pending") não tem consulta e fica de fora; a chave explícita de conexão também conta como banco pronto.
                if (reader.GetBoolean(1) || databases.HasExplicitConnection(reader.GetGuid(0))) others.Add(reader.GetGuid(0));
        foreach (var other in others)
        {
            await using var connection = await databases.OpenAsync(other, ct);
            await using var overlap = new NpgsqlCommand("SELECT EXISTS(SELECT 1 FROM nora_unit.appointments WHERE cpf=$1 AND status IN ('scheduled','called','completed','missed') AND starts_at<$3 AND ends_at>$2)", connection);
            overlap.Parameters.AddWithValue(cpfLock.Cpf); overlap.Parameters.AddWithValue(start.ToUniversalTime()); overlap.Parameters.AddWithValue(end.ToUniversalTime());
            if (await overlap.ExecuteScalarAsync(ct) is true) throw new ApiCodedException(StatusCodes.Status409Conflict, "other-unit-overlap");
        }
    }
}
