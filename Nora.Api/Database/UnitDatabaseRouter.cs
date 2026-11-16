using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Npgsql;
using Nora.Api.Services;

namespace Nora.Api.Database;

/// <summary>
/// Database connections are chosen only from server configuration, never from a client-supplied URL.
/// A chave explícita <c>Nora:UnitDatabases:&lt;id sem hífens&gt;</c> tem precedência; sem ela, com
/// <c>Nora:UnitDatabases:AutoProvision</c> verdadeiro (padrão), a conexão deriva da central trocando só o nome do banco
/// para <c>&lt;prefixo&gt;&lt;id sem hífens&gt;</c> (prefixo em <c>Nora:UnitDatabases:NamePrefix</c>, padrão <c>nora_ubs_</c>).
/// </summary>
public sealed partial class UnitDatabaseRouter : IAsyncDisposable
{
    public const string DefaultNamePrefix = "nora_ubs_";
    /// <summary>Conexões simultâneas por UBS no pool derivado. Baixo de propósito: com N unidades o teto total é N × este valor.</summary>
    public const int DefaultMaxPoolSize = 3;
    private const int MaxConfigurablePoolSize = 100;
    private const int ExplicitMaxPoolSize = 10;
    /// <summary>Segundos até uma conexão ociosa de UBS ser fechada e intervalo da poda; mantêm as conexões das unidades quietas fechadas entre as visitas do worker.</summary>
    private const int IdleLifetimeSeconds = 60;
    private const int PruningIntervalSeconds = 10;
    [GeneratedRegex("^[a-z][a-z0-9_]{0,30}$")] private static partial Regex PrefixPattern();

    private readonly IConfiguration configuration;
    private readonly Dictionary<(Guid, string), NpgsqlDataSource> sources = [];
    /// <summary>Bancos (pool) cujo schema já foi conferido como atual neste processo; só cresce, pois o schema não volta atrás.</summary>
    private readonly ConcurrentDictionary<NpgsqlDataSource, bool> currentSchemas = new();
    private readonly object sync = new();

    public UnitDatabaseRouter(IConfiguration configuration)
    {
        this.configuration = configuration;
        _ = NamePrefix; // Prefixo inválido interrompe a inicialização em vez de falhar na primeira UBS.
        _ = MaxPoolSize;
    }

    /// <summary>Máximo de conexões por UBS do pool derivado (<c>Nora:UnitDatabases:MaxPoolSize</c>, 1 a 100, padrão 3).</summary>
    public int MaxPoolSize
    {
        get
        {
            var value = configuration["Nora:UnitDatabases:MaxPoolSize"];
            if (string.IsNullOrWhiteSpace(value)) return DefaultMaxPoolSize;
            return int.TryParse(value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var size) && size is >= 1 and <= MaxConfigurablePoolSize
                ? size : throw new InvalidOperationException("MaxPoolSize de UBS inválido: informe um inteiro de 1 a 100.");
        }
    }

    public string NamePrefix
    {
        get
        {
            var value = configuration["Nora:UnitDatabases:NamePrefix"];
            if (string.IsNullOrEmpty(value)) return DefaultNamePrefix;
            return PrefixPattern().IsMatch(value) ? value : throw new InvalidOperationException("Prefixo de banco de UBS inválido.");
        }
    }
    public bool AutoProvision => configuration.GetValue("Nora:UnitDatabases:AutoProvision", true);
    public bool HasExplicitConnection(Guid unitId) => unitId != Guid.Empty && !string.IsNullOrWhiteSpace(configuration[$"Nora:UnitDatabases:{unitId:N}"]);
    /// <summary>Nome do banco derivado; montado só do prefixo validado e do GUID em hexadecimal.</summary>
    public string DerivedDatabaseName(Guid unitId) => NamePrefix + unitId.ToString("N");
    /// <summary>Há alguma conexão resolvível (explícita ou derivada); não garante que o banco exista.</summary>
    public bool IsConfigured(Guid unitId) => unitId != Guid.Empty && (HasExplicitConnection(unitId) || (AutoProvision && !string.IsNullOrWhiteSpace(configuration.GetConnectionString("Nora"))));

    /// <summary>Connection string efetiva da UBS: chave explícita ou a central com o banco derivado.</summary>
    internal string BuildConnectionString(Guid unitId)
    {
        var (raw, isExplicit) = Resolve(unitId);
        return Build(unitId, raw, isExplicit);
    }

    private (string Raw, bool IsExplicit) Resolve(Guid unitId)
    {
        if (unitId == Guid.Empty) throw new DatabaseUnavailableException();
        var explicitValue = configuration[$"Nora:UnitDatabases:{unitId:N}"];
        var isExplicit = !string.IsNullOrWhiteSpace(explicitValue);
        var raw = isExplicit ? explicitValue : AutoProvision ? configuration.GetConnectionString("Nora") : null;
        if (string.IsNullOrWhiteSpace(raw)) throw new DatabaseUnavailableException();
        return (raw, isExplicit);
    }

    private string Build(Guid unitId, string raw, bool isExplicit)
    {
        var builder = new NpgsqlConnectionStringBuilder(raw);
        builder.IncludeErrorDetail = false;
        builder.LogParameters = false;
        if (isExplicit) builder.MaxPoolSize = Math.Min(builder.MaxPoolSize, ExplicitMaxPoolSize);
        else
        {
            // O banco derivado herdaria o mínimo e o máximo do pool central; cada UBS tem pool próprio e enxuto.
            builder.Database = DerivedDatabaseName(unitId);
            builder.MinPoolSize = 0;
            builder.MaxPoolSize = MaxPoolSize;
            builder.ConnectionIdleLifetime = IdleLifetimeSeconds;
            builder.ConnectionPruningInterval = PruningIntervalSeconds;
        }
        return builder.ConnectionString;
    }

    private NpgsqlDataSource Source(Guid unitId)
    {
        var (raw, isExplicit) = Resolve(unitId);
        lock (sync)
        {
            if (sources.TryGetValue((unitId, raw), out var existing)) return existing;
            var source = NpgsqlDataSource.Create(Build(unitId, raw, isExplicit));
            sources.Add((unitId, raw), source);
            return source;
        }
    }

    internal Task<NpgsqlConnection> OpenForMigrationAsync(Guid unitId, CancellationToken ct) => Source(unitId).OpenConnectionAsync(ct).AsTask();

    /// <summary>Chamado ao fim de uma migração bem-sucedida: o schema da UBS passa a contar como atual sem nova conferência.</summary>
    internal void MarkSchemaCurrent(Guid unitId) => currentSchemas[Source(unitId)] = true;

    /// <summary>
    /// Abre a conexão da UBS e confere a identidade do banco. Na primeira abertura de cada banco (por processo) confere também a versão do
    /// schema: um banco sem a versão atual falha com 503 <c>unit-database-outdated</c>, em vez de um erro de SQL numa rota qualquer.
    /// Um banco desatualizado é conferido de novo a cada abertura, para voltar a funcionar assim que for migrado (inclusive por outro processo).
    /// </summary>
    public async Task<NpgsqlConnection> OpenAsync(Guid unitId, CancellationToken ct = default)
    {
        var source = Source(unitId);
        var connection = await source.OpenConnectionAsync(ct);
        try
        {
            await using (var command = new NpgsqlCommand("SELECT unit_id FROM nora_unit.identity WHERE singleton", connection))
                if (await command.ExecuteScalarAsync(ct) is not Guid actual || actual != unitId) throw new DatabaseUnavailableException();
            if (!currentSchemas.ContainsKey(source))
            {
                if (await ReadSchemaVersionAsync(connection, ct) < UnitSchema.ExpectedVersion) throw new ApiCodedException(StatusCodes.Status503ServiceUnavailable, "unit-database-outdated");
                currentSchemas[source] = true;
            }
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    /// <summary>Versão carimbada em <c>nora_unit.schema_version</c>; 0 quando a tabela ou a linha não existem (banco migrado por uma versão anterior).</summary>
    private static async Task<int> ReadSchemaVersionAsync(NpgsqlConnection connection, CancellationToken ct)
    {
        await using (var exists = new NpgsqlCommand("SELECT to_regclass('nora_unit.schema_version') IS NOT NULL", connection))
            if (await exists.ExecuteScalarAsync(ct) is not true) return 0;
        await using var read = new NpgsqlCommand("SELECT COALESCE((SELECT version FROM nora_unit.schema_version WHERE singleton), 0)", connection);
        return (int)(await read.ExecuteScalarAsync(ct))!;
    }

    public async ValueTask DisposeAsync()
    {
        List<NpgsqlDataSource> captured;
        lock (sync) { captured = sources.Values.ToList(); sources.Clear(); }
        foreach (var source in captured) await source.DisposeAsync();
    }
}
