using Npgsql;

namespace Nora.Api.DAOs;

/// <summary>
/// Lock da fila da UBS. A fila manual (<c>nora_unit.waiting_list</c>) foi removida em 2026-11-24 (a Vaga Relâmpago é automática); a tabela fica só como histórico e o lock
/// <c>62419811</c> continua serializando vagas, ofertas, respostas e expiração.
/// </summary>
internal static class UnitWaitingListDao
{
    internal static async Task LockAsync(NpgsqlConnection connection, CancellationToken ct)
    { await using var command = new NpgsqlCommand("SELECT pg_advisory_xact_lock(62419811)", connection); await command.ExecuteNonQueryAsync(ct); }
}
