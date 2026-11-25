using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using Nora.Api;
using Nora.Api.DAOs;
using Nora.Api.Database;
using Nora.Api.Models;
using Nora.Api.Services;

/// <summary>Conta as verificações de uma suíte da rodada final e imprime PASS/FAIL com o nome dela.</summary>
internal sealed class Suite(string label)
{
    public int Checks { get; private set; }
    public int Failures { get; private set; }

    public async Task Check(string name, Func<Task> action)
    {
        Checks++;
        try { await action(); Console.WriteLine($"PASS [{label}] {name}"); }
        catch (Exception error) { Failures++; Console.WriteLine($"FAIL [{label}] {name}: {error.GetType().Name}: {error.Message}"); }
    }

    public int Report()
    {
        Console.WriteLine($"{Checks - Failures}/{Checks} verificações de {label} passaram.");
        return Failures;
    }
}

/// <summary>Relógio de teste: começa na segunda-feira 2026-11-16 12:00 UTC (09:00 em America/Sao_Paulo) e só anda quando o teste manda.</summary>
internal sealed class RoundClock : TimeProvider
{
    public static readonly DateTimeOffset T0 = new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);
    private long ticks = T0.UtcTicks;
    public override DateTimeOffset GetUtcNow() => new(Interlocked.Read(ref ticks), TimeSpan.Zero);
    public void Set(DateTimeOffset moment) => Interlocked.Exchange(ref ticks, moment.UtcTicks);
    public void Advance(TimeSpan span) => Interlocked.Add(ref ticks, span.Ticks);
}

internal sealed class RoundMail : IEmailSender
{
    public bool IsConfigured => true;
    public List<(string To, string Subject, string Body)> Sent { get; } = [];
    public Task SendAsync(string address, string subject, string body, CancellationToken ct) { lock (Sent) Sent.Add((address, subject, body)); return Task.CompletedTask; }
}

/// <summary>
/// API de teste da rodada final: schema próprio, relógio de teste, Secretaria, administrador (com concessão em todas as UBS) e uma atendente (concessão na UBS 1), UBS com banco próprio criado pela API e
/// pacientes sob demanda. Nenhum dado real: tudo vive no cluster descartável e some no fim.
/// </summary>
internal sealed class RoundFixture : IAsyncDisposable
{
    public const string Password = "Disposable-test-Account!29";
    public required WebApplication App { get; init; }
    public required string Schema { get; init; }
    public required string ConnectionString { get; init; }
    public required RoundClock Clock { get; init; }
    public required RoundMail Mail { get; init; }
    public required HttpClient Anonymous { get; init; }
    public required HttpClient Municipal { get; init; }
    public required HttpClient Admin { get; init; }
    public required Guid ManagerId { get; init; }
    public required Guid AdminId { get; init; }
    public required Guid AttendantId { get; init; }
    public List<Guid> Units { get; } = [];
    public Guid Unit1 => Units[0];
    public Guid Unit2 => Units[1];
    public Dictionary<int, HttpClient> Patients { get; } = [];
    public Dictionary<int, Guid> PatientIds { get; } = [];
    private readonly List<HttpClient> clients = [];

    public DatabaseHelper Database => App.Services.GetRequiredService<DatabaseHelper>();
    public UnitDatabaseRouter Router => App.Services.GetRequiredService<UnitDatabaseRouter>();
    public T Service<T>() where T : notnull => App.Services.GetRequiredService<T>();

    public static string Cpf(int number) => SchedulingChecks.Cpf(number);

    /// <param name="unitCount">UBS criadas (cada uma ganha um banco próprio, criado pela API).</param>
    /// <param name="extraArgs">Configuração adicional (ex.: <c>--Nora:ObsDevices:RequireForAttendants</c>).</param>
    public static async Task<RoundFixture> StartAsync(string connection, string name, int unitCount = 2, string[]? extraArgs = null, Action<IServiceCollection>? configure = null)
    {
        var schema = $"nora_{name}_{Guid.NewGuid():N}";
        var clock = new RoundClock(); var mail = new RoundMail();
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Nora.Api", "Nora.Api.csproj"))) directory = directory.Parent;
        // Pools enxutos: o cluster descartável tem max_connections=16 e esta API mantém conexões a várias UBS.
        var small = new NpgsqlConnectionStringBuilder(connection) { MaxPoolSize = 6 }.ConnectionString;
        var app = NoraApiHost.Create(new()
        {
            Args = ["--urls", "http://127.0.0.1:0", "--ConnectionStrings:Nora", small, "--Nora:Schema", schema, "--Nora:UnitLightningWorker:Enabled", "false", "--Nora:Maintenance:Enabled", "false", "--Nora:UnitDatabases:MaxPoolSize", "2",
                "--Logging:LogLevel:Default", "Warning", "--Logging:LogLevel:Nora.Api.Services.UnitDatabaseProvisioner", "None", "--Nora:Registration:HashKey", "synthetic-round-registration-key-32-bytes", "--Nora:ForwardedHeaders:KnownProxies:0", "127.0.0.1",
                // As verificações da rodada final cobrem os computadores do OBS, guardados e desligados no servidor por padrão. Quem testa o padrão passa "false" em extraArgs.
                "--Nora:ObsDevices:Enabled", "true", .. (extraArgs ?? [])],
            EnvironmentName = "Development", ApplicationName = typeof(NoraApiHost).Assembly.GetName().Name, ContentRootPath = Path.Combine(directory!.FullName, "Nora.Api")
        }, services =>
        {
            services.Replace(ServiceDescriptor.Singleton<TimeProvider>(clock));
            services.Replace(ServiceDescriptor.Singleton<IEmailSender>(mail));
            configure?.Invoke(services);
        });
        await app.Services.GetRequiredService<MigrationDao>().MigrateAsync();
        var accounts = app.Services.GetRequiredService<AccountService>();
        var manager = await accounts.ProvisionAsync("municipal", "manager", new("Gestora da rodada", Cpf(901), "gestora@example.test", Password));
        var admin = await accounts.ProvisionAsync("obs", "administrator", new("Administrador da rodada", Cpf(902), "admin@example.test", Password));
        var attendant = await accounts.ProvisionAsync("obs", "attendant", new("Atendente da rodada", Cpf(903), "atendente@example.test", Password));
        await app.StartAsync();
        var f = new RoundFixture { App = app, Schema = schema, ConnectionString = connection, Clock = clock, Mail = mail, Anonymous = NewClient(app), Municipal = NewClient(app), Admin = NewClient(app), ManagerId = manager.Id, AdminId = admin.Id, AttendantId = attendant.Id };
        f.clients.AddRange([f.Anonymous, f.Municipal, f.Admin]);
        await f.LoginAsync(f.Municipal, "municipal", "gestora@example.test"); await f.LoginAsync(f.Admin, "obs", "admin@example.test");
        for (var i = 0; i < unitCount; i++)
        {
            using var response = await f.Municipal.PostAsJsonAsync("/api/municipal/units", new { name = $"UBS da rodada {i + 1}", region = "Centro", address = "Rua de teste, 10", latitude = -23.55, longitude = -46.63 });
            Expect(response, 201);
            f.Units.Add((await Read(response)).GetProperty("id").GetGuid());
            await f.GrantAsync(admin.Id, f.Units[i]);
        }
        if (unitCount > 0) await f.GrantAsync(attendant.Id, f.Units[0]);
        return f;
    }

    public async Task GrantAsync(Guid accountId, Guid unitId)
    {
        using var grant = await Municipal.PutAsync($"/api/municipal/staff/{accountId}/units/{unitId}", null);
        Expect(grant, 204);
    }

    private static int nextAddress;

    /// <summary>
    /// Cliente com endereço de origem próprio (X-Forwarded-For de um proxy confiável, 127.0.0.1): os limites por IP da API valem por cliente, então um cliente não esgota o de outro.
    /// </summary>
    public static HttpClient NewClient(WebApplication app)
    {
        var client = new HttpClient
        {
            BaseAddress = new(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single()),
            Timeout = TimeSpan.FromSeconds(60)
        };
        var number = Interlocked.Increment(ref nextAddress);
        client.DefaultRequestHeaders.Add("X-Forwarded-For", $"10.77.{(number >> 8) & 255}.{number & 255}");
        return client;
    }

    public HttpClient NewClient()
    {
        var client = NewClient(App); clients.Add(client); return client;
    }

    /// <summary>Entra e deixa o token no cliente; devolve o corpo do login. Com <paramref name="device"/> manda o cabeçalho do computador (e o mantém nas próximas requisições).</summary>
    public async Task<JsonElement> LoginAsync(HttpClient client, string scope, string identifier, string? device = null, bool? keepSignedIn = null, string password = Password)
    {
        ObsDeviceHelper.Send(client, device);
        client.DefaultRequestHeaders.Authorization = null;
        using var response = await client.PostAsJsonAsync("/api/auth/login", keepSignedIn is { } keep ? (object)new { scope, identifier, password, keepSignedIn = keep } : new { scope, identifier, password });
        Expect(response, 200);
        var body = await Read(response);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body.GetProperty("token").GetString()!);
        return body;
    }

    /// <summary>Conta OBS nova (administrador ou atendente) com concessões, já logada num cliente novo (com o computador, se informado).</summary>
    public async Task<(HttpClient Client, Guid Id)> StaffAsync(string role, int number, Guid[] units, string? device = null)
    {
        var account = await Service<AccountService>().ProvisionAsync("obs", role, new($"Equipe {number}", Cpf(number), $"equipe{number}@example.test", Password));
        foreach (var unit in units) await GrantAsync(account.Id, unit);
        var client = NewClient();
        await LoginAsync(client, "obs", $"equipe{number}@example.test", device);
        return (client, account.Id);
    }

    /// <summary>Paciente com conta, cadastro de referência nas UBS informadas e vínculo; já logado. Sem UBS: só a conta.</summary>
    public async Task<HttpClient> PatientAsync(int number, params Guid[] units)
    {
        PatientIds[number] = (await Service<AccountService>().ProvisionAsync("patient", "patient", new($"Paciente {number}", Cpf(number), $"paciente{number}@example.test", Password))).Id;
        foreach (var unit in units) await RegisterAsync(number, unit);
        var client = NewClient(); await LoginAsync(client, "patient", Cpf(number));
        if (units.Length > 0) { using var link = await client.PostAsJsonAsync("/api/patient/units/link", new { unitIds = units }); Expect(link, 200); }
        return Patients[number] = client;
    }

    /// <summary>Cadastro de referência feito pela equipe (sem conta).</summary>
    public async Task RegisterAsync(int number, Guid unit, string? email = null)
    {
        using var response = await Admin.PostAsJsonAsync($"/api/obs/units/{unit}/registry", new { cpf = Cpf(number), fullName = $"Paciente {number}", email = email ?? $"paciente{number}@example.test" });
        Expect(response, 201);
    }

    // ---- Catálogo e grade ----

    public async Task<Guid> SpecialtyAsync(string name)
    {
        using var response = await Municipal.PostAsJsonAsync("/api/municipal/specialties", new { name });
        Expect(response, 201); return (await Read(response)).GetProperty("id").GetGuid();
    }

    public async Task<Guid> ProfessionalAsync(Guid unit, string name, params Guid[] specialties)
    {
        using var response = await Admin.PostAsJsonAsync($"/api/obs/units/{unit}/professionals", new { fullName = name, specialtyIds = specialties });
        Expect(response, 201); return (await Read(response)).GetProperty("id").GetGuid();
    }

    public async Task<Guid> RoomAsync(Guid unit, string name)
    {
        using var response = await Admin.PostAsJsonAsync($"/api/obs/units/{unit}/rooms", new { name });
        Expect(response, 201); return (await Read(response)).GetProperty("id").GetGuid();
    }

    /// <summary>Linha da grade semanal (weekday 0 = domingo; horários locais de America/Sao_Paulo).</summary>
    public async Task<Guid> TemplateAsync(Guid unit, Guid professional, int weekday, string start, string end, int minutes, Guid room, Guid specialty)
    {
        using var response = await Admin.PostAsJsonAsync($"/api/obs/units/{unit}/schedule", new { professionalId = professional, weekday, startTime = start, endTime = end, slotMinutes = minutes, roomId = room, specialtyId = specialty });
        Expect(response, 201); return (await Read(response)).GetProperty("id").GetGuid();
    }

    /// <summary>Quarta-feira 2026-11-18 às <paramref name="localTime"/> locais (o fuso é -03:00).</summary>
    public static DateTimeOffset Wednesday(string localTime) => DateTimeOffset.Parse($"2026-11-18T{localTime}:00-03:00").ToUniversalTime();

    // ---- Banco ----

    /// <summary>
    /// UBS cujo banco foi criado "à mão" e migrado só até a versão <paramref name="version"/> do schema (como um banco de versão anterior): cadastrada no central, mas o banco só ganha as migrações
    /// seguintes quando o teste rodar <see cref="UnitMigrationDao"/>. Sem a marca de banco pronto no central (<paramref name="ready"/> a coloca).
    /// </summary>
    public async Task<Guid> CreateLegacyUnitAsync(int version, bool ready = false)
    {
        var unit = (await Service<HealthUnitDao>().CreateAsync(new(Guid.NewGuid(), "UBS legada", "Centro", "Rua legada, 1", -23.5, -46.6), default)).Id;
        await using (var admin = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(ConnectionString) { Database = "postgres", Pooling = false }.ConnectionString))
        {
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{Router.DerivedDatabaseName(unit)}\"", admin); await create.ExecuteNonQueryAsync();
        }
        await using var raw = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(ConnectionString) { Database = Router.DerivedDatabaseName(unit), Pooling = false }.ConnectionString);
        await raw.OpenAsync();
        const string marker = ".UnitMigrations.";
        foreach (var name in UnitSchema.ResourceNames.Where(name => int.Parse(name[(name.IndexOf(marker, StringComparison.Ordinal) + marker.Length)..][..3]) <= version))
        {
            await using var stream = typeof(UnitSchema).Assembly.GetManifestResourceStream(name)!; using var reader = new StreamReader(stream);
            await using var command = new NpgsqlCommand(await reader.ReadToEndAsync(), raw); await command.ExecuteNonQueryAsync();
        }
        await using (var identity = new NpgsqlCommand("INSERT INTO nora_unit.identity(singleton,unit_id) VALUES(true,$1)", raw)) { identity.Parameters.AddWithValue(unit); await identity.ExecuteNonQueryAsync(); }
        await using (var stamp = new NpgsqlCommand($"INSERT INTO nora_unit.schema_version(singleton,version) VALUES(true,{version})", raw)) await stamp.ExecuteNonQueryAsync();
        if (ready) await Exec("UPDATE nora.health_units SET database_ready_at=$2 WHERE id=$1", unit, RoundClock.T0);
        return unit;
    }

    public async Task<T> Scalar<T>(string sql, params object[] values)
    {
        await using var connection = await Database.OpenAsync();
        await using var command = Database.Command(connection, sql.Replace("$schema", Schema), values.Select(Normalize).ToArray());
        var result = await command.ExecuteScalarAsync();
        return result is null or DBNull ? default! : (T)result;
    }

    public async Task Exec(string sql, params object[] values)
    {
        await using var connection = await Database.OpenAsync();
        await using var command = Database.Command(connection, sql, values.Select(Normalize).ToArray());
        await command.ExecuteNonQueryAsync();
    }

    public async Task<T> UnitScalar<T>(Guid unit, string sql, params object[] values)
    {
        await using var connection = await Router.OpenAsync(unit);
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var value in values) command.Parameters.AddWithValue(Normalize(value));
        var result = await command.ExecuteScalarAsync();
        return result is null or DBNull ? default! : (T)result;
    }

    public async Task UnitExec(Guid unit, string sql, params object[] values)
    {
        await using var connection = await Router.OpenAsync(unit);
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var value in values) command.Parameters.AddWithValue(Normalize(value));
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>Redistribui as ofertas da UBS agora, como o worker de 15 s faria (o worker fica desligado nos testes).</summary>
    public Task AdvanceAsync(Guid unit) => Service<UnitLightningDispatcher>().AdvanceAsync(unit, default);

    /// <summary>Tira o banco de uma UBS do ar para quem abre a conexão pela API (a identidade do banco deixa de ser a da UBS) enquanto <paramref name="action"/> roda, e o restaura no fim.</summary>
    public async Task WithUnitDownAsync(Guid unit, Func<Task> action)
    {
        var direct = new NpgsqlConnectionStringBuilder(ConnectionString) { Database = Router.DerivedDatabaseName(unit), Pooling = false }.ConnectionString;
        async Task Set(Guid value) { await using var raw = new NpgsqlConnection(direct); await raw.OpenAsync(); await using var update = new NpgsqlCommand("UPDATE nora_unit.identity SET unit_id=$1", raw); update.Parameters.AddWithValue(value); await update.ExecuteNonQueryAsync(); }
        await Set(Guid.NewGuid());
        try { await action(); }
        finally { await Set(unit); }
    }

    private static object Normalize(object? value) => value is DateTimeOffset moment ? moment.ToUniversalTime() : value ?? DBNull.Value;

    // ---- Asserções ----

    public static void Ensure(bool valid, string message) { if (!valid) throw new InvalidOperationException(message); }

    public static async Task<JsonElement> Read(HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.Clone();
    }

    public static void Expect(HttpResponseMessage response, int status) =>
        Ensure((int)response.StatusCode == status, $"Esperado HTTP {status}; recebido {(int)response.StatusCode} em {response.RequestMessage?.Method} {response.RequestMessage?.RequestUri?.PathAndQuery}.");

    public static async Task<JsonElement> ExpectProblem(HttpResponseMessage response, int status, string code)
    {
        Expect(response, status);
        Ensure(response.Content.Headers.ContentType?.MediaType == "application/problem+json", "Erro sem application/problem+json.");
        var body = await Read(response);
        Ensure(body.GetProperty("code").GetString() == code && body.GetProperty("type").GetString() == $"urn:nora:problem:{code}" && body.GetProperty("status").GetInt32() == status
            && !string.IsNullOrWhiteSpace(body.GetProperty("title").GetString()) && !string.IsNullOrWhiteSpace(body.GetProperty("detail").GetString()) && !string.IsNullOrWhiteSpace(body.GetProperty("traceId").GetString()),
            $"Código de erro esperado '{code}', recebido '{body.GetProperty("code").GetString()}'.");
        return body;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var client in clients) client.Dispose();
        await App.StopAsync();
        try { await using var connection = await Database.OpenAsync(); await Database.ExecuteAsync(connection, $"DROP SCHEMA IF EXISTS {Schema} CASCADE", default); }
        catch (Exception) { /* o cluster descartável é removido pelo script */ }
        await App.DisposeAsync();
    }
}
