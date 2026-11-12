using System.Text.RegularExpressions;
using Npgsql;
using Nora.Api.DAOs;
using Nora.Api.Database;

namespace Nora.Api.Services;

/// <summary>
/// Cria o banco de uma UBS (quando a conexão é derivada da central), aplica as migrações de UBS e marca
/// <c>health_units.database_ready_at</c>. O usuário PostgreSQL da API precisa da permissão CREATEDB.
/// </summary>
public sealed partial class UnitDatabaseProvisioner(DatabaseHelper central, UnitDatabaseRouter router, UnitMigrationDao migrations, HealthUnitDao units, ILogger<UnitDatabaseProvisioner> log)
{
    [GeneratedRegex("^[a-z][a-z0-9_]{0,62}$")] private static partial Regex IdentifierPattern();

    /// <summary>Tenta preparar o banco; devolve falso em vez de propagar quando a criação falha (a UBS fica "pending").</summary>
    public async Task<bool> TryProvisionAsync(Guid unitId, Guid? actorId, CancellationToken ct)
    {
        try { await ProvisionAsync(unitId, actorId, ct); return true; }
        catch (ApiCodedException) { return false; }
    }

    /// <summary>Idempotente: reaplica as migrações (aditivas) e só marca/registra a primeira vez. Falha → 503 database-provisioning-failed.</summary>
    public async Task ProvisionAsync(Guid unitId, Guid? actorId, CancellationToken ct)
    {
        if (await units.FindAsync(unitId, ct) is null) throw new ApiNotFoundException();
        try
        {
            if (!router.HasExplicitConnection(unitId))
            {
                if (!router.AutoProvision) throw new InvalidOperationException("Criação automática desativada.");
                await EnsureDatabaseAsync(router.DerivedDatabaseName(unitId), unitId, ct);
            }
            await migrations.MigrateAsync(unitId, ct);
            await units.MarkDatabaseReadyAsync(unitId, actorId, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (ApiNotFoundException) { throw; }
        catch (Exception error)
        {
            // Sem connection string nem detalhes do servidor no registro; só o SQLSTATE, para diagnosticar permissão (42501).
            log.LogWarning("Não foi possível criar ou preparar o banco de uma UBS ({UnitId}); SQLSTATE {SqlState}.", unitId, (error as PostgresException)?.SqlState ?? "n/d");
            throw new ApiCodedException(StatusCodes.Status503ServiceUnavailable, "database-provisioning-failed");
        }
    }

    private async Task EnsureDatabaseAsync(string name, Guid unitId, CancellationToken ct)
    {
        // O identificador vem só do prefixo validado + GUID em hexadecimal; a verificação abaixo é uma segunda barreira.
        if (!IdentifierPattern().IsMatch(name)) throw new InvalidOperationException("Nome de banco inválido.");
        await using var connection = await central.OpenAsync(ct);
        // Trava de sessão (CREATE DATABASE não roda em transação) para serializar tentativas simultâneas da mesma UBS.
        await central.ExecuteAsync(connection, "SELECT pg_advisory_lock(hashtextextended($1,62419806))", ct, unitId.ToString("N"));
        try
        {
            await using (var exists = central.Command(connection, "SELECT EXISTS(SELECT 1 FROM pg_database WHERE datname=$1)", name))
                if (await exists.ExecuteScalarAsync(ct) is true) return;
            try { await central.ExecuteAsync(connection, $"CREATE DATABASE \"{name}\"", ct); }
            catch (PostgresException error) when (error.SqlState == "42P04") { /* criado por outra tentativa */ }
        }
        finally
        {
            await central.ExecuteAsync(connection, "SELECT pg_advisory_unlock(hashtextextended($1,62419806))", CancellationToken.None, unitId.ToString("N"));
        }
    }
}
