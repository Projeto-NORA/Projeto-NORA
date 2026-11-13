using Npgsql;
using Nora.Api.DAOs;
using Nora.Api.Database;

namespace Nora.Api.Services;

/// <summary>
/// Confere na subida que o PostgreSQL conhece o fuso de <c>Nora:TimeZone</c> (usado em <c>AT TIME ZONE</c> na série diária dos indicadores).
/// Fuso desconhecido só apareceria como erro de SQL na primeira consulta de indicadores; aqui a subida falha com uma mensagem clara.
/// Sem banco central configurado, ou com o banco inacessível neste instante, a conferência é adiada com um aviso (a prontidão da API já
/// acusa a indisponibilidade) e nunca derruba a subida por causa de rede.
/// </summary>
public sealed class TimeZoneStartupCheck(DatabaseHelper central, IConfiguration configuration, ILogger<TimeZoneStartupCheck> log) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!central.IsConfigured) return;
        var zone = UnitMetricsDao.ConfiguredZone(configuration);
        bool known;
        try
        {
            await using var connection = await central.OpenAsync(cancellationToken);
            await using var query = new NpgsqlCommand("SELECT EXISTS(SELECT 1 FROM pg_timezone_names WHERE lower(name)=lower($1))", connection);
            query.Parameters.AddWithValue(zone);
            known = await query.ExecuteScalarAsync(cancellationToken) is true;
        }
        catch (Exception error) when (error is NpgsqlException or DatabaseUnavailableException)
        {
            log.LogWarning("Não foi possível conferir o fuso horário configurado em Nora:TimeZone porque o banco está inacessível; a conferência não foi feita.");
            return;
        }
        if (!known) throw new InvalidOperationException($"O fuso horário configurado em Nora:TimeZone ('{zone}') não é conhecido pelo PostgreSQL (consulte pg_timezone_names). Use um nome IANA, como America/Sao_Paulo.");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
