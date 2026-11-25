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
using Nora.Api.Services;

internal static partial class FamilyChecks
{
    // ================= Infraestrutura =================

    private static async Task Check(string name, Func<Task> action)
    {
        checks++;
        try { await action(); Console.WriteLine($"PASS [Família] {name}"); }
        catch (Exception error) { failures++; Console.WriteLine($"FAIL [Família] {name}: {error.GetType().Name}: {error.Message}"); }
    }

    private static void Require(bool valid, string message) { if (!valid) throw new InvalidOperationException(message); }

    private static async Task<JsonElement> Read(HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.Clone();
    }

    private static void Expect(HttpResponseMessage response, int status) => Require((int)response.StatusCode == status, $"Esperado HTTP {status}; recebido {(int)response.StatusCode} em {response.RequestMessage?.Method} {response.RequestMessage?.RequestUri?.PathAndQuery}.");

    private static async Task ExpectProblem(HttpResponseMessage response, int status, string code)
    {
        Expect(response, status);
        Require(response.Content.Headers.ContentType?.MediaType == "application/problem+json", "Erro sem application/problem+json.");
        var body = await Read(response);
        Require(body.GetProperty("code").GetString() == code && body.GetProperty("type").GetString() == $"urn:nora:problem:{code}" && body.GetProperty("status").GetInt32() == status && !string.IsNullOrWhiteSpace(body.GetProperty("title").GetString()),
            $"Código de erro esperado '{code}', recebido '{body.GetProperty("code").GetString()}'.");
    }

    internal sealed class TestClock : TimeProvider
    {
        private long ticks = T0.UtcTicks;
        public override DateTimeOffset GetUtcNow() => new(Interlocked.Read(ref ticks), TimeSpan.Zero);
        public void Set(DateTimeOffset moment) => Interlocked.Exchange(ref ticks, moment.UtcTicks);
    }

    /// <summary>Caixa de saída de e-mail do teste: nada é enviado de verdade; guarda destino e texto, e pode simular SMTP desligado ou falha de entrega.</summary>
    internal sealed class MailBox : IEmailSender
    {
        public bool Configured { get; set; } = true;
        public bool Fail { get; set; }
        public List<(string To, string Subject, string Body)> Sent { get; } = [];
        public bool IsConfigured => Configured;
        public Task SendAsync(string address, string subject, string body, CancellationToken ct)
        {
            if (Fail) throw new InvalidOperationException("Falha simulada de entrega.");
            lock (Sent) Sent.Add((address, subject, body));
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// API de teste num schema próprio com relógio fixo, duas UBS (bancos criados pela API), Secretaria, administrador e atendentes OBS (um só da UBS 1, outro só da UBS 2) e pacientes já logados:
    /// quatro responsáveis (G1 a G4), um dependente com conta própria (D2), pacientes da troca de e-mail (P21, P24, P51) e dependentes sem conta, que só têm cadastro de referência.
    /// </summary>
    internal sealed class Fixture : IAsyncDisposable
    {
        public required WebApplication App { get; init; }
        public required string Schema { get; init; }
        public required string ConnectionString { get; init; }
        public required TestClock Clock { get; init; }
        public required MailBox Mail { get; init; }
        public required HttpClient Anonymous { get; init; }
        public required HttpClient Municipal { get; init; }
        public required HttpClient Admin { get; init; }
        public required HttpClient Attendant { get; init; }
        public required HttpClient OtherAttendant { get; init; }
        public HttpClient G1 { get; set; } = null!;
        public HttpClient G2 { get; set; } = null!;
        public HttpClient G3 { get; set; } = null!;
        public HttpClient G4 { get; set; } = null!;
        public HttpClient D2 { get; set; } = null!;
        public HttpClient P21 { get; set; } = null!;
        public HttpClient P24 { get; set; } = null!;
        public HttpClient P51 { get; set; } = null!;
        public Dictionary<int, Guid> PatientIds { get; } = [];
        public Guid AdminId { get; set; }
        public Guid AttendantId { get; set; }
        public Guid Unit1 { get; set; }
        public Guid Unit2 { get; set; }
        public Guid D1Appointment { get; set; }
        public Guid D2Appointment { get; set; }
        public Guid G1OwnAppointment { get; set; }
        public DateTimeOffset ConfirmedAt { get; set; }
        private readonly List<HttpClient> clients = [];
        private DatabaseHelper Database => App.Services.GetRequiredService<DatabaseHelper>();
        private UnitDatabaseRouter Router => App.Services.GetRequiredService<UnitDatabaseRouter>();

        /// <param name="extraArgs">Configuração adicional da API de teste (ex.: Google e push com credenciais sintéticas).</param>
        /// <param name="configure">Serviços adicionais (ex.: o HttpMessageHandler falso dos clientes HTTP externos).</param>
        public static async Task<Fixture> StartAsync(string connection, string[]? extraArgs = null, Action<IServiceCollection>? configure = null)
        {
            var schema = "nora_familia_" + Guid.NewGuid().ToString("N");
            var clock = new TestClock(); var mail = new MailBox();
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Nora.Api", "Nora.Api.csproj"))) directory = directory.Parent;
            // Pools enxutos: o cluster descartável tem max_connections=16 e esta API mantém conexões a duas UBS.
            var small = new NpgsqlConnectionStringBuilder(connection) { MaxPoolSize = 6 }.ConnectionString;
            var app = NoraApiHost.Create(new()
            {
                Args = ["--urls", "http://127.0.0.1:0", "--ConnectionStrings:Nora", small, "--Nora:Schema", schema, "--Nora:UnitLightningWorker:Enabled", "false", "--Nora:Maintenance:Enabled", "false", "--Nora:UnitDatabases:MaxPoolSize", "2",
                    "--Logging:LogLevel:Default", "Warning", "--Logging:LogLevel:Nora.Api.Services.UnitDatabaseProvisioner", "None", "--Nora:Registration:HashKey", "synthetic-family-registration-key-32-bytes",
                    // Estas suítes entram como atendente em computador ativado; o recurso vem desligado no padrão do servidor (24/11/2026) e aqui é ligado de propósito.
                    "--Nora:ObsDevices:Enabled", "true", .. (extraArgs ?? Array.Empty<string>())],
                EnvironmentName = "Development", ApplicationName = typeof(NoraApiHost).Assembly.GetName().Name, ContentRootPath = Path.Combine(directory!.FullName, "Nora.Api")
            }, services =>
            {
                services.Replace(ServiceDescriptor.Singleton<TimeProvider>(clock));
                services.Replace(ServiceDescriptor.Singleton<IEmailSender>(mail));
                configure?.Invoke(services);
            });
            await app.Services.GetRequiredService<MigrationDao>().MigrateAsync();
            var accounts = app.Services.GetRequiredService<AccountService>();
            await accounts.ProvisionAsync("municipal", "manager", new("Gestora da Família", Cpf(901), "gestora@example.test", Password));
            var admin = await accounts.ProvisionAsync("obs", "administrator", new("Administrador Família", Cpf(902), "admin@example.test", Password));
            var attendant = await accounts.ProvisionAsync("obs", "attendant", new("Atendente Família", Cpf(903), "atendente@example.test", Password));
            var other = await accounts.ProvisionAsync("obs", "attendant", new("Outro Atendente", Cpf(904), "outro@example.test", Password));
            await app.StartAsync();
            var f = new Fixture { App = app, Schema = schema, ConnectionString = connection, Clock = clock, Mail = mail, Anonymous = NewClient(app), Municipal = NewClient(app), Admin = NewClient(app), Attendant = NewClient(app), OtherAttendant = NewClient(app), AdminId = admin.Id, AttendantId = attendant.Id };
            f.clients.AddRange([f.Anonymous, f.Municipal, f.Admin, f.Attendant, f.OtherAttendant]);
            await Login(f.Municipal, "municipal", "gestora@example.test"); await Login(f.Admin, "obs", "admin@example.test");
            f.Unit1 = await f.CreateUnitAsync("UBS Família Um", "Centro"); f.Unit2 = await f.CreateUnitAsync("UBS Família Dois", "Norte");
            foreach (var unit in new[] { f.Unit1, f.Unit2 }) { using var grant = await f.Municipal.PutAsync($"/api/municipal/staff/{admin.Id}/units/{unit}", null); Expect(grant, 204); }
            using (var grant = await f.Municipal.PutAsync($"/api/municipal/staff/{attendant.Id}/units/{f.Unit1}", null)) Expect(grant, 204);
            using (var grant = await f.Municipal.PutAsync($"/api/municipal/staff/{other.Id}/units/{f.Unit2}", null)) Expect(grant, 204);
            // As atendentes só entram em computador ativado da UBS delas (o administrador ativa um computador por UBS).
            await ObsDeviceHelper.ActivateAndLoginAsync(f.Admin, f.Attendant, f.Unit1, "atendente@example.test", Password);
            await ObsDeviceHelper.ActivateAndLoginAsync(f.Admin, f.OtherAttendant, f.Unit2, "outro@example.test", Password);

            async Task Person(int number, string name, string email, string registryEmail, Guid[] registeredIn, bool account, bool link)
            {
                if (account) f.PatientIds[number] = (await accounts.ProvisionAsync("patient", "patient", new(name, Cpf(number), email, Password))).Id;
                foreach (var unit in registeredIn) { using var register = await f.Admin.PostAsJsonAsync($"/api/obs/units/{unit}/registry", new { cpf = Cpf(number), fullName = name, email = registryEmail }); Expect(register, 201); }
                if (!account) return;
                var client = NewClient(app); f.clients.Add(client);
                await Login(client, "patient", Cpf(number));
                if (link) { using var linked = await client.PostAsJsonAsync("/api/patient/units/link", new { unitIds = registeredIn }); Expect(linked, 200); }
                f.Assign(number, client);
            }
            Guid[] one = [f.Unit1], both = [f.Unit1, f.Unit2];
            // Responsáveis: o G1 também é paciente da UBS 1 (agenda por conta própria, à parte do limite dos dependentes).
            await Person(1, "Responsável Um", "g1@example.test", "g1@example.test", one, account: true, link: true);
            await Person(2, "Responsável Dois", "g2@example.test", "g2@example.test", [], account: true, link: false);
            await Person(3, "Responsável Três", "g3@example.test", "g3@example.test", [], account: true, link: false);
            await Person(4, "Responsável Quatro", "g4@example.test", "g4@example.test", [], account: true, link: false);
            // Dependente com conta própria.
            await Person(12, "Dependente Doze", "d12@example.test", "d12@example.test", one, account: true, link: true);
            // Dependentes e pacientes sem conta: só o cadastro de referência.
            await Person(11, "Dependente Onze", "", "dependente11@example.test", both, account: false, link: false);
            foreach (var (number, name) in new[] { (13, "Dependente Treze"), (14, "Dependente Catorze"), (31, "Paciente Trinta e Um"), (41, "Paciente Quarenta e Um") })
                await Person(number, name, "", $"sem-conta{number}@example.test", one, account: false, link: false);
            // Troca de e-mail: contas vinculadas (P21, P23, P24, P26, P51), uma sem vínculo com a UBS (P22) e uma só com conta (P25, dona do e-mail que gera conflito).
            await Person(21, "Paciente Vinte e Um", "p21@example.test", "p21@example.test", one, account: true, link: true);
            await Person(22, "Paciente Vinte e Dois", "p22@example.test", "p22@example.test", one, account: true, link: false);
            await Person(23, "Paciente Vinte e Três", "p23@example.test", "antigo23@example.test", one, account: true, link: true);
            await Person(24, "Paciente Vinte e Quatro", "p24@example.test", "p24@example.test", one, account: true, link: true);
            f.PatientIds[25] = (await accounts.ProvisionAsync("patient", "patient", new("Paciente Vinte e Cinco", Cpf(25), "p25@example.test", Password))).Id;
            await Person(26, "Paciente Vinte e Seis", "p26@example.test", "p26@example.test", one, account: true, link: true);
            await Person(51, "Paciente Cinquenta e Um", "p51@example.test", "p51@example.test", one, account: true, link: true);
            return f;
        }

        private void Assign(int number, HttpClient client)
        {
            switch (number) { case 1: G1 = client; break; case 2: G2 = client; break; case 3: G3 = client; break; case 4: G4 = client; break; case 12: D2 = client; break; case 21: P21 = client; break; case 24: P24 = client; break; case 51: P51 = client; break; }
        }

        public async Task<Guid> CreateUnitAsync(string name, string region)
        {
            using var response = await Municipal.PostAsJsonAsync("/api/municipal/units", new { name, region, address = "Rua de teste, 10", latitude = -23.55, longitude = -46.63 });
            Expect(response, 201);
            return (await Read(response)).GetProperty("id").GetGuid();
        }

        public Task<NpgsqlConnection> OpenCentral() => Database.OpenAsync();
        public NpgsqlCommand Command(NpgsqlConnection connection, string sql, params object[] values) => Database.Command(connection, sql, values);
        public async Task<T> Scalar<T>(string sql, params object[] values) { await using var connection = await Database.OpenAsync(); await using var command = Database.Command(connection, sql.Replace("$schema", Schema), values); var result = await command.ExecuteScalarAsync(); return result is null or DBNull ? default! : (T)result; }
        public async Task Exec(string sql, params object[] values) { await using var connection = await Database.OpenAsync(); await using var command = Database.Command(connection, sql, values.Select(Normalize).ToArray()); await command.ExecuteNonQueryAsync(); }
        public async Task<T> UnitScalar<T>(Guid unit, string sql, params object[] values) { await using var connection = await Router.OpenAsync(unit); await using var command = new NpgsqlCommand(sql, connection); foreach (var value in values) command.Parameters.AddWithValue(Normalize(value)); var result = await command.ExecuteScalarAsync(); return result is null or DBNull ? default! : (T)result; }
        /// <summary>
        /// Consulta marcada posterior (gravada direto no banco da UBS) que torna o CPF candidato da Vaga Relâmpago automática da especialidade (2026-11-24: a fila manual foi removida).
        /// Fica em 2027, uma hora por número, então quanto maior o número mais distante (e antes na ordem de oferta, com o mesmo score).
        /// </summary>
        public async Task<Guid> LaterAsync(Guid unit, int number, string specialty, int extraDays = 0)
        {
            var id = Guid.NewGuid(); var start = new DateTimeOffset(2027, 1, 1, 12, 0, 0, TimeSpan.Zero).AddHours(number).AddDays(extraDays);
            await UnitExec(unit, "INSERT INTO nora_unit.appointments(id,cpf,specialty,professional,starts_at,ends_at,status,created_by,created_at) VALUES($1,$2,$3,$4,$5,$6,'scheduled',$7,$8)", id, Cpf(number), specialty, $"Agenda futura {number}", start, start.AddMinutes(30), AdminId, Clock.GetUtcNow());
            return id;
        }
        public Task AdvanceAsync(Guid unit) => App.Services.GetRequiredService<UnitLightningDispatcher>().AdvanceAsync(unit, default);
        public async Task UnitExec(Guid unit, string sql, params object[] values) { await using var connection = await Router.OpenAsync(unit); await using var command = new NpgsqlCommand(sql, connection); foreach (var value in values) command.Parameters.AddWithValue(Normalize(value)); await command.ExecuteNonQueryAsync(); }
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

        private static HttpClient NewClient(WebApplication app) => new()
        {
            BaseAddress = new(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single()),
            Timeout = TimeSpan.FromSeconds(60)
        };

        private static async Task Login(HttpClient client, string scope, string identifier)
        {
            using var response = await client.PostAsJsonAsync("/api/auth/login", new { scope, identifier, password = Password });
            Expect(response, 200);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await Read(response)).GetProperty("token").GetString()!);
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
}
