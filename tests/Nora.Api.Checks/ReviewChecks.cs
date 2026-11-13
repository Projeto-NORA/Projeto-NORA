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

        await Check("POST /api/municipal/units tem o mesmo limite da nova tentativa de banco (20 por 10 min por IP, compartilhado)", async () =>
        {
            // Sem criação automática cada cadastro é barato (a UBS fica pending); o limite vale igual.
            await using var h = await Harness.StartAsync(connectionString, ["--Nora:UnitDatabases:AutoProvision", "false"]);
            Guid last = Guid.Empty;
            for (var i = 0; i < 20; i++) last = await h.CreateUnitAsync($"UBS limite {i}", grantObs: false);
            using var limited = await h.Municipal.PostAsJsonAsync("/api/municipal/units", new { name = "UBS acima do limite", region = "Centro", address = "Rua de teste, 10", latitude = -23.55, longitude = -46.63 });
            await ExpectProblem(limited, 429, "too-many-requests");
            using var retry = await h.Municipal.PostAsync($"/api/municipal/units/{last}/database", null);
            await ExpectProblem(retry, 429, "too-many-requests");
            Require(await h.ScalarAsync<long>("SELECT count(*) FROM nora.health_units") == 20, "A criação acima do limite foi gravada.");
        });

        // {{NOVOS}}
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

    /// <summary>API de teste num schema próprio, com a Secretaria e um administrador OBS já autenticados. A verificação periódica fica desligada.</summary>
    private sealed class Harness : IAsyncDisposable
    {
        public required WebApplication App { get; init; }
        public required string Schema { get; init; }
        public required string Connection { get; init; }
        public required HttpClient Anonymous { get; init; }
        public required HttpClient Municipal { get; init; }
        public required HttpClient Obs { get; init; }
        public required Guid ManagerId { get; init; }
        public required Guid ObsId { get; init; }
        public DatabaseHelper Database => App.Services.GetRequiredService<DatabaseHelper>();
        public UnitDatabaseRouter Router => App.Services.GetRequiredService<UnitDatabaseRouter>();
        public List<IDisposable> Owned { get; } = [];

        public static async Task<Harness> StartAsync(string connection, string[]? extraArgs = null, Action<IServiceCollection>? services = null)
        {
            var schema = "nora_review_" + Guid.NewGuid().ToString("N");
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Nora.Api", "Nora.Api.csproj"))) directory = directory.Parent;
            var args = new List<string> { "--urls", "http://127.0.0.1:0", "--ConnectionStrings:Nora", connection, "--Nora:Schema", schema, "--Nora:UnitLightningWorker:Enabled", "false",
                "--Logging:LogLevel:Default", "Warning", "--Logging:LogLevel:Nora.Api.Services.UnitDatabaseProvisioner", "None", "--Nora:Registration:HashKey", "synthetic-review-registration-key-32-bytes" };
            if (extraArgs is not null) args.AddRange(extraArgs);
            var app = NoraApiHost.Create(new()
            {
                Args = [.. args], EnvironmentName = "Development", ApplicationName = typeof(NoraApiHost).Assembly.GetName().Name,
                ContentRootPath = Path.Combine(directory!.FullName, "Nora.Api")
            }, collection => { collection.Replace(ServiceDescriptor.Singleton<IEmailSender>(new NoMail())); services?.Invoke(collection); });
            await app.Services.GetRequiredService<MigrationDao>().MigrateAsync();
            var accounts = app.Services.GetRequiredService<AccountService>();
            var manager = await accounts.ProvisionAsync("municipal", "manager", new("Gestora de revisão", Cpf(90), "manager@example.test", Password));
            var obs = await accounts.ProvisionAsync("obs", "administrator", new("Administrador de revisão", Cpf(91), "admin@example.test", Password));
            await app.StartAsync();
            var harness = new Harness { App = app, Schema = schema, Connection = connection, Anonymous = Client(app), Municipal = Client(app), Obs = Client(app), ManagerId = manager.Id, ObsId = obs.Id };
            await Login(harness.Municipal, "municipal", "manager@example.test"); await Login(harness.Obs, "obs", "admin@example.test");
            return harness;
        }

        public HttpClient NewClient() { var client = Client(App); Owned.Add(client); return client; }

        /// <summary>Cadastra uma UBS pela API (o banco é criado automaticamente, salvo AutoProvision falso) e concede o acesso ao administrador OBS.</summary>
        public async Task<Guid> CreateUnitAsync(string name, bool grantObs = true, int expectedStatus = 201)
        {
            using var response = await Municipal.PostAsJsonAsync("/api/municipal/units", new { name, region = "Centro", address = "Rua de teste, 10", latitude = -23.55, longitude = -46.63 });
            Expect(response, expectedStatus);
            var id = (await Read(response)).GetProperty("id").GetGuid();
            if (grantObs) { using var grant = await Municipal.PutAsync($"/api/municipal/staff/{ObsId}/units/{id}", null); Expect(grant, 204); }
            return id;
        }

        public async Task<T> ScalarAsync<T>(string sql, params object[] values)
        {
            await using var connection = await Database.OpenAsync();
            await using var command = Database.Command(connection, sql, values);
            return (T)(await command.ExecuteScalarAsync())!;
        }

        public async Task<T> UnitScalarAsync<T>(Guid unit, string sql, params object[] values)
        {
            await using var connection = await Router.OpenAsync(unit);
            await using var command = new NpgsqlCommand(sql, connection);
            foreach (var value in values) command.Parameters.AddWithValue(value);
            return (T)(await command.ExecuteScalarAsync())!;
        }

        public async Task ExecuteAsync(string sql, params object[] values)
        {
            await using var connection = await Database.OpenAsync();
            await Database.ExecuteAsync(connection, sql, default, values);
        }

        public async ValueTask DisposeAsync()
        {
            foreach (var owned in Owned) owned.Dispose();
            Anonymous.Dispose(); Municipal.Dispose(); Obs.Dispose();
            await App.StopAsync();
            try
            {
                await using var connection = await Database.OpenAsync();
                await Database.ExecuteAsync(connection, $"DROP SCHEMA IF EXISTS {Schema} CASCADE", default);
            }
            catch (Exception) { /* o cluster descartável é removido pelo script */ }
            await App.DisposeAsync();
        }
    }

    private static HttpClient Client(WebApplication app) => new()
    {
        BaseAddress = new(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single()),
        Timeout = TimeSpan.FromSeconds(60)
    };
    private static async Task Login(HttpClient client, string scope, string identifier, string password = Password)
    {
        using var response = await client.PostAsJsonAsync("/api/auth/login", new { scope, identifier, password });
        Expect(response, 200);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await Read(response)).GetProperty("token").GetString()!);
    }
    private static async Task<JsonElement> Read(HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.Clone();
    }
    private static void Expect(HttpResponseMessage response, int status) => Require((int)response.StatusCode == status, $"Esperado HTTP {status}; recebido {(int)response.StatusCode} em {response.RequestMessage?.Method} {response.RequestMessage?.RequestUri?.AbsolutePath}.");
    private static async Task ExpectProblem(HttpResponseMessage response, int status, string code)
    {
        Expect(response, status);
        var body = await Read(response);
        Require(body.GetProperty("code").GetString() == code && body.GetProperty("type").GetString() == $"urn:nora:problem:{code}" && body.GetProperty("status").GetInt32() == status, $"Código de erro esperado '{code}', recebido '{body.GetProperty("code").GetString()}'.");
    }
    private static string Cpf(int number)
    {
        var cpf = (123456780 + number).ToString();
        for (var count = 9; count <= 10; count++)
        {
            var sum = Enumerable.Range(0, count).Sum(i => (cpf[i] - '0') * (count + 1 - i));
            var digit = sum * 10 % 11;
            cpf += (digit == 10 ? 0 : digit).ToString();
        }
        return cpf;
    }

    private sealed class NoMail : IEmailSender
    {
        public bool IsConfigured => true;
        public Task SendAsync(string address, string subject, string body, CancellationToken ct) => Task.CompletedTask;
    }
}
