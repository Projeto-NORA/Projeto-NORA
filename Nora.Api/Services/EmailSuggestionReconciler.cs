using Npgsql;
using Nora.Api.DAOs;
using Nora.Api.Database;

namespace Nora.Api.Services;

/// <summary>
/// Leva a sugestão de troca de e-mail da conta da caixa de saída da UBS para o banco central. A correção do e-mail de referência grava, na transação da UBS, o e-mail novo e a linha da caixa de saída; depois,
/// numa transação central que segura o lock de CPF <c>62419805</c>, a sugestão é registrada com a origem (<c>source_key</c>) igual ao id da linha; só então a linha é apagada. Se o commit central falha depois do
/// da UBS a linha sobra, e a próxima correção do mesmo CPF ou a passada periódica (<see cref="RunOnceAsync"/>, no <see cref="MaintenanceWorker"/>) refaz o registro. Reprocessar é inofensivo: a origem única
/// impede duplicar, e uma sugestão dispensada não renasce. A sugestão guarda o instante da correção na UBS (não o do reprocessamento), e uma linha que sobrou de uma correção mais antiga não substitui uma sugestão mais nova da conta, de outra UBS ou da mesma.
/// </summary>
public sealed class EmailSuggestionReconciler(DatabaseHelper central, UnitPatientRegistryDao registry, HealthUnitDao units, EmailChangeDao emailChanges, TimeProvider time, ILogger<EmailSuggestionReconciler> log)
{
    /// <summary>Linhas mais novas que isto ainda podem estar em andamento (a rota acabou de gravá-las); só as mais antigas entram na passada periódica.</summary>
    public static readonly TimeSpan MinimumAge = TimeSpan.FromMinutes(2);
    public const int BatchPerUnit = 200;

    /// <summary>
    /// Passo 3, na transação central do chamador (que já segura o lock do CPF): se há linha na caixa de saída do CPF — nova ou sobra de uma tentativa que falhou —, registra a sugestão com a origem dela.
    /// Devolve o id da linha, que o chamador apaga depois do commit central (<see cref="ClearAsync"/>).
    /// </summary>
    internal async Task<Guid?> RegisterAsync(NpgsqlConnection connection, Guid unitId, string cpf, CancellationToken ct)
    {
        if (await registry.ReadOutboxAsync(unitId, cpf, ct) is not { } row) return null;
        await emailChanges.RecordAsync(connection, unitId, cpf, row.Email, time.GetUtcNow(), ct, row.Id, row.CreatedAt);
        return row.Id;
    }

    /// <summary>Passo 4, depois do commit central: apaga a linha da caixa de saída. Falha só registra aviso (sem dado pessoal): reprocessar a linha é inofensivo.</summary>
    internal async Task ClearAsync(Guid unitId, string cpf, Guid id, CancellationToken ct)
    {
        try { await registry.DeleteOutboxAsync(unitId, cpf, id, ct); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception) { log.LogWarning("Não foi possível limpar uma linha da caixa de saída de e-mail de uma UBS; a passada periódica tentará de novo."); }
    }

    /// <summary>
    /// Passada periódica: para cada UBS pronta, as linhas mais antigas que <see cref="MinimumAge"/> (até 200 por UBS), cada uma numa transação central com o lock do CPF. UBS com o banco fora do ar é
    /// pulada com aviso. Devolve quantas linhas foram processadas.
    /// </summary>
    public async Task<int> RunOnceAsync(CancellationToken ct)
    {
        var processed = 0;
        foreach (var unit in (await units.ListAsync(ct)).Where(unit => unit.DatabaseState == "ready"))
        {
            try
            {
                foreach (var cpf in await registry.ListStaleOutboxAsync(unit.Id, time.GetUtcNow() - MinimumAge, BatchPerUnit, ct))
                {
                    Guid? done;
                    await using (var connection = await central.OpenAsync(ct))
                    await using (var transaction = await connection.BeginTransactionAsync(ct))
                    {
                        await central.ExecuteAsync(connection, "SELECT pg_advisory_xact_lock(hashtextextended($1,62419805))", ct, cpf);
                        done = await RegisterAsync(connection, unit.Id, cpf, ct);
                        await transaction.CommitAsync(ct);
                    }
                    if (done is { } id) { await ClearAsync(unit.Id, cpf, id, ct); processed++; }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception) { log.LogWarning("Não foi possível reconciliar as sugestões de e-mail de uma UBS; a próxima passada tentará de novo."); }
        }
        return processed;
    }
}
