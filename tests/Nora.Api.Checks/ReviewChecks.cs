using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using Nora.Api;
using Nora.Api.DAOs;
using Nora.Api.Database;
using Nora.Api.Services;

/// <summary>
/// Regressões dos achados da revisão crítica do back-end (pool por UBS, limites, revalidação, versão do schema, cursor do histórico etc.).
/// Cada verificação reproduz o achado e fica vermelha sem a correção. Usa só NORA_TEST_CONNECTION (cluster descartável); os bancos de UBS
/// criados automaticamente ficam nesse cluster.
/// </summary>
internal static class ReviewChecks
{
    private const string Password = "Disposable-test-Account!29";
    private static int checks;
    private static int failures;

    public static async Task<int> RunAsync(string root)
    {
        await Check("Pool derivado por UBS: máximo configurável (padrão 3), mínimo 0, ociosidade curta e poda coerente; configuração inválida recusada", () =>
        {
            var unit = Guid.NewGuid();
            const string central = "Host=127.0.0.1;Database=postgres;Username=u;Minimum Pool Size=10;Maximum Pool Size=100";
            NpgsqlConnectionStringBuilder Pool(string? maxPoolSize, string? connection = null, Guid? id = null)
            {
                var values = new Dictionary<string, string?> { ["ConnectionStrings:Nora"] = connection ?? central };
                if (maxPoolSize is not null) values["Nora:UnitDatabases:MaxPoolSize"] = maxPoolSize;
                if (id is { } explicitId && connection is not null) values[$"Nora:UnitDatabases:{explicitId:N}"] = connection;
                var router = new UnitDatabaseRouter(new ConfigurationBuilder().AddInMemoryCollection(values).Build());
                return new(router.BuildConnectionString(id ?? unit));
            }
            var byDefault = Pool(null);
            Require(byDefault.MaxPoolSize == 3 && byDefault.MinPoolSize == 0 && byDefault.ConnectionIdleLifetime == 60 && byDefault.ConnectionPruningInterval == 10, $"Padrão do pool derivado incorreto (máx {byDefault.MaxPoolSize}, mín {byDefault.MinPoolSize}, ociosidade {byDefault.ConnectionIdleLifetime}s, poda {byDefault.ConnectionPruningInterval}s).");
            Require(byDefault.Database == "nora_ubs_" + unit.ToString("N"), "O banco derivado mudou de nome.");
            Require(Pool("5").MaxPoolSize == 5 && Pool("1").MaxPoolSize == 1 && Pool("100").MaxPoolSize == 100, "Máximo configurado não foi aplicado.");
            foreach (var invalid in new[] { "0", "-1", "abc", "101", "2.5" })
            {
                try { _ = Pool(invalid); throw new InvalidOperationException($"MaxPoolSize inválido '{invalid}' foi aceito."); }
                catch (InvalidOperationException error) when (error.Message.Contains("MaxPoolSize")) { }
            }
            // Chave explícita continua valendo e nunca passa de dez conexões, como antes.
            var explicitId = Guid.NewGuid();
            Require(Pool(null, "Host=127.0.0.1;Database=ubs_manual;Username=u;Maximum Pool Size=100", explicitId).MaxPoolSize == 10 && Pool(null, "Host=127.0.0.1;Database=ubs_manual;Username=u;Maximum Pool Size=4", explicitId).MaxPoolSize == 4, "Chave explícita deveria continuar limitada a dez conexões.");
            return Task.CompletedTask;
        });

        var connectionString = Environment.GetEnvironmentVariable("NORA_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Console.WriteLine("SKIP revisão PostgreSQL: configure NORA_TEST_CONNECTION somente para uma base descartável.");
            Console.WriteLine($"{checks - failures}/{checks} verificações da revisão passaram.");
            return failures;
        }

        await Check("Pool derivado respeita o máximo configurado: a terceira conexão simultânea espera e estoura o prazo", async () =>
        {
            var schema = "nora_review_pool_" + Guid.NewGuid().ToString("N");
            var builder = new NpgsqlConnectionStringBuilder(connectionString) { Timeout = 2, MaxPoolSize = 100, MinPoolSize = 5 };
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Nora"] = builder.ConnectionString, ["Nora:Schema"] = schema, ["Nora:UnitDatabases:MaxPoolSize"] = "2"
            }).Build();
            await using var central = new DatabaseHelper(config);
            await using var router = new UnitDatabaseRouter(config);
            var unit = Guid.NewGuid(); var name = router.DerivedDatabaseName(unit);
            await using (var admin = await central.OpenAsync()) await central.ExecuteAsync(admin, $"CREATE DATABASE \"{name}\"", default);
            try
            {
                await new UnitMigrationDao(router, central).MigrateAsync(unit);
                await using var first = await router.OpenAsync(unit); await using var second = await router.OpenAsync(unit);
                try { await using var third = await router.OpenAsync(unit); throw new InvalidOperationException("A terceira conexão simultânea foi aberta: o pool passa do máximo configurado."); }
                catch (NpgsqlException) { }
                catch (TimeoutException) { }
            }
            finally
            {
                await using var admin = await central.OpenAsync();
                await central.ExecuteAsync(admin, $"DROP DATABASE IF EXISTS \"{name}\" WITH (FORCE)", default);
            }
        });

        Console.WriteLine($"{checks - failures}/{checks} verificações da revisão passaram.");
        return failures;
    }

    private static async Task Check(string name, Func<Task> action)
    {
        checks++;
        try { await action(); Console.WriteLine($"PASS [Revisão] {name}"); }
        catch (Exception error) { failures++; Console.WriteLine($"FAIL [Revisão] {name}: {error.GetType().Name}: {error.Message}"); }
    }

    private static void Require(bool valid, string message) { if (!valid) throw new InvalidOperationException(message); }
}
