using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Npgsql;
using Nora.Api;
using Nora.Api.DAOs;
using Nora.Api.Database;
using Nora.Api.Models;
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

        await Check("X-Forwarded-For: desligado por padrão e ignorado de quem não é proxy conhecido; com KnownProxies o IP do cliente vem do cabeçalho; entrada inválida impede a subida", async () =>
        {
            // /api/address/cep/{cep} (30 por minuto por IP) responde 400 sem consulta externa para CEP inválido: serve de sonda do IP usado pelo limitador.
            async Task<int> Probe(string[] args, int requests)
            {
                await using var app = BuildApp(null, "unused", args, null);
                await app.StartAsync();
                try
                {
                    using var client = Client(app); var limited = 0;
                    for (var i = 0; i < requests; i++)
                    {
                        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/address/cep/12ABC");
                        request.Headers.Add("X-Forwarded-For", $"203.0.113.{i + 1}");
                        using var response = await client.SendAsync(request);
                        if ((int)response.StatusCode == 429) limited++; else Expect(response, 400);
                    }
                    return limited;
                }
                finally { await app.StopAsync(); }
            }
            Require(await Probe([], 31) == 1, "Sem KnownProxies o cabeçalho X-Forwarded-For deve ser ignorado (todas as chamadas são do mesmo IP).");
            Require(await Probe(["--Nora:ForwardedHeaders:KnownProxies:0", "10.9.9.9"], 31) == 1, "Cabeçalho de quem não é proxy conhecido deve ser ignorado.");
            Require(await Probe(["--Nora:ForwardedHeaders:KnownProxies:0", "127.0.0.1"], 31) == 0, "Com o proxy conhecido, cada IP do X-Forwarded-For deve ter o próprio limite.");
            Require(await Probe(["--Nora:ForwardedHeaders:KnownNetworks:0", "127.0.0.0/8"], 31) == 0, "Com KnownNetworks cobrindo o proxy, cada IP do X-Forwarded-For deve ter o próprio limite.");
            Require(await Probe(["--Nora:ForwardedHeaders:KnownProxies", "10.9.9.9,127.0.0.1"], 31) == 0, "Lista separada por vírgula (variável de ambiente) deve valer.");
            foreach (var invalid in new[] { new[] { "--Nora:ForwardedHeaders:KnownProxies:0", "não-é-ip" }, ["--Nora:ForwardedHeaders:KnownNetworks:0", "10.0.0.0/99"] })
            {
                try { await using var rejected = BuildApp(null, "unused", invalid, null); throw new InvalidOperationException("Configuração inválida de proxy foi aceita."); }
                catch (InvalidOperationException error) when (error.Message.Contains("ForwardedHeaders")) { }
            }
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

        await Check("Revalidação na transação: desativação aguarda a requisição OBS em andamento; sessão revogada, conta inativa e troca obrigatória são recusadas nos executores de UBS", async () =>
        {
            await using var h = await Harness.StartAsync(connectionString);
            var unit = await h.CreateUnitAsync("UBS Revalidação", grantObs: false);
            var access = h.App.Services.GetRequiredService<UnitAccessService>(); var auth = h.App.Services.GetRequiredService<AuthService>();
            using (var created = await h.Municipal.PostAsJsonAsync("/api/municipal/staff", new { fullName = "Atendente Corrida", cpf = Cpf(60), email = "race@example.test", password = "Provisoria-123", role = "attendant" })) Expect(created, 201);
            var staffId = await h.ScalarAsync<Guid>("SELECT id FROM nora.accounts WHERE email='race@example.test'");
            using (var grant = await h.Municipal.PutAsync($"/api/municipal/staff/{staffId}/units/{unit}", null)) Expect(grant, 204);
            using var staff = h.NewClient(); await Login(staff, "obs", "race@example.test", "Provisoria-123");
            using (var change = await staff.PostAsJsonAsync("/api/auth/password", new { currentPassword = "Provisoria-123", newPassword = "Senha-corrida-123" })) Expect(change, 204);
            async Task<SessionInfo> Session()
            {
                using var login = await h.Anonymous.PostAsJsonAsync("/api/auth/login", new { scope = "obs", identifier = "race@example.test", password = "Senha-corrida-123" });
                Expect(login, 200);
                return (await auth.AuthenticateAsync((await Read(login)).GetProperty("token").GetString()!, default))!;
            }
            var session = await Session();
            // Requisição em andamento (autenticada antes da desativação): a desativação precisa esperar o fim dela.
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var held = access.ExecuteAsync(session, unit, async () => { started.TrySetResult(); await release.Task; return true; }, CancellationToken.None);
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var deactivation = h.Municipal.PatchAsJsonAsync($"/api/municipal/staff/{staffId}/active", new { isActive = false, confirmed = true });
            try { await Task.Delay(300); Require(!deactivation.IsCompleted, "A desativação da conta não aguardou a requisição OBS em andamento."); }
            finally { release.TrySetResult(); }
            await held;
            using (var done = await deactivation) Expect(done, 204);
            // Depois da desativação a mesma sessão (já autenticada) não executa mais nada.
            try { await access.ExecuteAsync(session, unit, () => Task.FromResult(true), default); throw new InvalidOperationException("Conta desativada ainda executou operação da UBS."); }
            catch (ApiUnauthorizedException) { }
            using (var reactivate = await h.Municipal.PatchAsJsonAsync($"/api/municipal/staff/{staffId}/active", new { isActive = true, confirmed = true })) Expect(reactivate, 204);
            try { await access.ExecuteAsync(session, unit, () => Task.FromResult(true), default); throw new InvalidOperationException("Sessão revogada pela desativação ainda executou operação da UBS."); }
            catch (ApiUnauthorizedException) { }
            // Troca obrigatória imposta depois da autenticação (redefinição de senha pela Secretaria) bloqueia a operação com o código dedicado.
            var fresh = await Session();
            await h.ExecuteAsync("UPDATE nora.accounts SET must_change_password=true WHERE id=$1", staffId);
            try { await access.ExecuteAsync(fresh, unit, () => Task.FromResult(true), default); throw new InvalidOperationException("Troca obrigatória pendente não bloqueou a operação da UBS."); }
            catch (ApiCodedException error) when (error.Status == 403 && error.Code == "password-change-required") { }
            await h.ExecuteAsync("UPDATE nora.accounts SET must_change_password=false WHERE id=$1", staffId);
            Require(await access.ExecuteAsync(fresh, unit, () => Task.FromResult(true), default), "Sessão válida deveria executar a operação.");
            // Leitura nominal da gestão: sessão revogada depois da autenticação é recusada dentro da transação.
            using var managerLogin = await h.Anonymous.PostAsJsonAsync("/api/auth/login", new { scope = "municipal", identifier = "manager@example.test", password = Password });
            Expect(managerLogin, 200);
            var managerSession = (await auth.AuthenticateAsync((await Read(managerLogin)).GetProperty("token").GetString()!, default))!;
            await h.ExecuteAsync("UPDATE nora.sessions SET revoked_at=CURRENT_TIMESTAMP WHERE token_hash=$1", managerSession.TokenHash);
            try { await access.ExecuteManagementReadAsync(managerSession, unit, () => Task.FromResult(true), default); throw new InvalidOperationException("Sessão da gestão revogada ainda leu a UBS."); }
            catch (ApiUnauthorizedException) { }
            Require(await h.ScalarAsync<long>("SELECT count(*) FROM nora.unit_access_events WHERE unit_id=$1", unit) == 0, "A leitura recusada deixou evento de acesso.");
        });

        await Check("UBS com schema desatualizado: aviso no log (sem dado pessoal), 503 unit-database-outdated nas rotas dela, a API sobe e a migração recupera", async () =>
        {
            await using var h = await Harness.StartAsync(connectionString);
            var unit = await h.CreateUnitAsync("UBS Antiga");
            // Estado de um banco migrado por versão anterior: sem o carimbo de versão e sem a coluna source.
            await using (var old = await h.Router.OpenAsync(unit))
            await using (var downgrade = new NpgsqlCommand("DROP TABLE IF EXISTS nora_unit.schema_version; ALTER TABLE nora_unit.appointment_events DROP COLUMN source", old))
                await downgrade.ExecuteNonQueryAsync();
            var capture = new LogCapture();
            var (restarted, obs, municipal) = await h.RestartAsync(services: services => services.AddSingleton<ILoggerProvider>(capture));
            var warned = false;
            for (var attempt = 0; attempt < 100 && !warned; attempt++)
            {
                warned = capture.Lines.Any(line => line.Contains(unit.ToString()) && line.Contains("--migrate-all-units"));
                if (!warned) await Task.Delay(100);
            }
            Require(warned, "A subida não registrou o aviso de schema desatualizado com a instrução --migrate-all-units.");
            Require(!capture.Lines.Any(line => line.Contains("Host=") || line.Contains("Username=") || line.Contains(Cpf(91))), "O aviso expôs connection string ou dado pessoal.");
            var window = $"from={Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(-1).ToString("O"))}&to={Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(1).ToString("O"))}";
            using (var metrics = await obs.GetAsync($"/api/obs/units/{unit}/metrics?{window}")) await ExpectProblem(metrics, 503, "unit-database-outdated");
            using (var registry = await obs.GetAsync($"/api/obs/units/{unit}/registry")) await ExpectProblem(registry, 503, "unit-database-outdated");
            using (var summary = await municipal.GetAsync("/api/municipal/summary")) await ExpectProblem(summary, 503, "unit-database-outdated");
            await restarted.Services.GetRequiredService<UnitMigrationDao>().MigrateAsync(unit);
            using (var metrics = await obs.GetAsync($"/api/obs/units/{unit}/metrics?{window}")) Expect(metrics, 200);
            using (var summary = await municipal.GetAsync("/api/municipal/summary")) Expect(summary, 200);
            Require(await h.UnitScalarAsync<int>(unit, "SELECT version FROM nora_unit.schema_version WHERE singleton") == ExpectedUnitSchemaVersion(), "A migração não carimbou a versão atual do schema da UBS.");
        });

        await Check("Nova tentativa de banco numa UBS pronta e atual retorna sem reaplicar migrações (não espera o lock de migração)", async () =>
        {
            await using var h = await Harness.StartAsync(connectionString);
            var unit = await h.CreateUnitAsync("UBS Pronta");
            await using var holder = await h.Router.OpenAsync(unit);
            await using (var acquire = new NpgsqlCommand("SELECT pg_advisory_lock(62419804)", holder)) await acquire.ExecuteNonQueryAsync();
            try
            {
                using var response = await h.Municipal.PostAsync($"/api/municipal/units/{unit}/database", null).WaitAsync(TimeSpan.FromSeconds(8));
                Expect(response, 200);
                Require((await Read(response)).GetProperty("databaseState").GetString() == "ready", "A nova tentativa não devolveu a unidade pronta.");
            }
            finally { await using var release = new NpgsqlCommand("SELECT pg_advisory_unlock(62419804)", holder); await release.ExecuteNonQueryAsync(); }
            // Banco sem o carimbo de versão (desatualizado) é migrado de novo pela mesma rota.
            await using (var old = await h.Router.OpenAsync(unit)) await using (var drop = new NpgsqlCommand("DROP TABLE IF EXISTS nora_unit.schema_version", old)) await drop.ExecuteNonQueryAsync();
            var (_, _, freshMunicipal) = await h.RestartAsync();
            using var healed = await freshMunicipal.PostAsync($"/api/municipal/units/{unit}/database", null); Expect(healed, 200);
            Require(await h.UnitScalarAsync<int>(unit, "SELECT version FROM nora_unit.schema_version WHERE singleton") == ExpectedUnitSchemaVersion(), "A nova tentativa não migrou a UBS desatualizada.");
        });

        await Check("Migrações de UBS 004, 008 e 010: restrições CHECK só são recriadas quando faltam ou diferem; reaplicar duas vezes mantém a mesma restrição validada", async () =>
        {
            await using var h = await Harness.StartAsync(connectionString);
            var unit = await h.CreateUnitAsync("UBS Restrições");
            var migrations = h.App.Services.GetRequiredService<UnitMigrationDao>();
            (string Table, string Name)[] constraints = [("patient_registry_events", "patient_registry_events_action_check"), ("waiting_list_events", "waiting_list_events_source_check"), ("appointment_events", "appointment_events_source_check")];
            async Task<Dictionary<string, (long Oid, bool Validated, string Definition)>> Snapshot()
            {
                var result = new Dictionary<string, (long, bool, string)>();
                await using var connection = await h.Router.OpenAsync(unit);
                foreach (var (table, name) in constraints)
                {
                    await using var query = new NpgsqlCommand("SELECT c.oid::bigint, c.convalidated, pg_get_constraintdef(c.oid) FROM pg_constraint c WHERE c.conrelid=$1::regclass AND c.conname=$2", connection);
                    query.Parameters.AddWithValue("nora_unit." + table); query.Parameters.AddWithValue(name);
                    await using var reader = await query.ExecuteReaderAsync();
                    Require(await reader.ReadAsync(), $"Restrição {name} ausente.");
                    result[name] = (reader.GetInt64(0), reader.GetBoolean(1), reader.GetString(2));
                }
                return result;
            }
            var before = await Snapshot();
            await migrations.MigrateAsync(unit); await migrations.MigrateAsync(unit);
            var after = await Snapshot();
            foreach (var (_, name) in constraints)
                Require(before[name].Oid == after[name].Oid && after[name].Validated, $"A restrição {name} foi recriada (ou ficou sem validar) ao reaplicar a migração.");
            // Dados já gravados: a restrição diferente é substituída, validada contra as linhas existentes.
            await using (var connection = await h.Router.OpenAsync(unit))
            await using (var seed = new NpgsqlCommand("INSERT INTO nora_unit.patient_registry(cpf,full_name,email,created_by) VALUES('52998224725','Paciente','p@example.test','00000000-0000-0000-0000-000000000001'); INSERT INTO nora_unit.patient_registry_events(cpf,actor_id,action) VALUES('52998224725','00000000-0000-0000-0000-000000000001','updated'); ALTER TABLE nora_unit.appointment_events DROP CONSTRAINT appointment_events_source_check; ALTER TABLE nora_unit.appointment_events ADD CONSTRAINT appointment_events_source_check CHECK (source IN ('staff'))", connection))
            await seed.ExecuteNonQueryAsync();
            await migrations.MigrateAsync(unit);
            var repaired = await Snapshot();
            Require(repaired["appointment_events_source_check"].Oid != after["appointment_events_source_check"].Oid && repaired["appointment_events_source_check"].Validated
                && repaired["appointment_events_source_check"].Definition.Contains("'patient'") && repaired["appointment_events_source_check"].Definition.Contains("'system'") && repaired["appointment_events_source_check"].Definition.Contains("'staff'"), "A restrição diferente não foi substituída pela esperada.");
            Require(repaired["patient_registry_events_action_check"].Oid == after["patient_registry_events_action_check"].Oid && repaired["waiting_list_events_source_check"].Oid == after["waiting_list_events_source_check"].Oid, "Restrições corretas foram recriadas sem necessidade.");
        });

        await Check("Limites de troca de senha e de remoção de vínculo são por conta autenticada, não por IP: uma conta esgotada não limita outra no mesmo IP", async () =>
        {
            await using var h = await Harness.StartAsync(connectionString);
            var accounts = h.App.Services.GetRequiredService<AccountService>();
            foreach (var n in new[] { 1, 2 }) await accounts.ProvisionAsync("patient", "patient", new($"Paciente {n}", Cpf(n), $"patient{n}@example.test", Password));
            using var first = h.NewClient(); using var second = h.NewClient();
            await Login(first, "patient", Cpf(1)); await Login(second, "patient", Cpf(2));
            var unlinkPath = $"/api/patient/units/{Guid.NewGuid()}/unlink";
            for (var i = 0; i < 10; i++)
            {
                using var wrong = await first.PostAsJsonAsync("/api/auth/password", new { currentPassword = "Errada-123456", newPassword = "Senha-nova-123" }); await ExpectProblem(wrong, 400, "invalid-current-password");
                using var unlink = await first.PostAsJsonAsync(unlinkPath, new { confirmed = true }); Expect(unlink, 403);
            }
            using (var limitedPassword = await first.PostAsJsonAsync("/api/auth/password", new { currentPassword = "Errada-123456", newPassword = "Senha-nova-123" })) await ExpectProblem(limitedPassword, 429, "too-many-requests");
            using (var limitedUnlink = await first.PostAsJsonAsync(unlinkPath, new { confirmed = true })) await ExpectProblem(limitedUnlink, 429, "too-many-requests");
            // Outra conta, mesmo IP: o contador dela está intacto.
            using (var otherPassword = await second.PostAsJsonAsync("/api/auth/password", new { currentPassword = "Errada-123456", newPassword = "Senha-nova-123" })) await ExpectProblem(otherPassword, 400, "invalid-current-password");
            using (var otherUnlink = await second.PostAsJsonAsync(unlinkPath, new { confirmed = true })) Expect(otherUnlink, 403);
            // A mesma conta com outra sessão continua limitada (o limite é da conta, não do token).
            using var again = h.NewClient(); await Login(again, "patient", Cpf(1));
            using (var sameAccount = await again.PostAsJsonAsync("/api/auth/password", new { currentPassword = "Errada-123456", newPassword = "Senha-nova-123" })) await ExpectProblem(sameAccount, 429, "too-many-requests");
        });

        await Check("Remoção de vínculo com falha do lado central: antes do commit da UBS nada é cancelado; falha no commit central deixa a UBS cancelada e repetir conclui", async () =>
        {
            await using var h = await Harness.StartAsync(connectionString);
            var unit = await h.CreateUnitAsync("UBS Saída");
            var accounts = h.App.Services.GetRequiredService<AccountService>();
            var patientId = (await accounts.ProvisionAsync("patient", "patient", new("Paciente Saída", Cpf(1), "patient1@example.test", Password))).Id;
            using (var register = await h.Obs.PostAsJsonAsync($"/api/obs/units/{unit}/registry", new { cpf = Cpf(1), fullName = "Paciente Saída", email = "patient1@example.test" })) Expect(register, 201);
            using var patient = h.NewClient(); await Login(patient, "patient", Cpf(1));
            var unlinkPath = $"/api/patient/units/{unit}/unlink";
            async Task LinkAndSeed(int seed)
            {
                using var link = await patient.PostAsJsonAsync("/api/patient/units/link", new { unitIds = new[] { unit } }); Expect(link, 200);
                var start = DateTimeOffset.UtcNow.AddDays(3 + seed * 3);
                foreach (var offset in new[] { 0, 2 })
                {
                    using var booked = await h.Obs.PostAsJsonAsync($"/api/obs/units/{unit}/appointments", new { cpf = Cpf(1), specialty = "Clínica", professional = $"Prof {seed}-{offset}", startsAt = start.AddHours(offset), endsAt = start.AddHours(offset).AddMinutes(30) }); Expect(booked, 201);
                }
                using var queued = await h.Obs.PostAsJsonAsync($"/api/obs/units/{unit}/waiting-list", new { cpf = Cpf(1), specialty = $"Especialidade {seed}" }); Expect(queued, 201);
            }
            async Task<(long Scheduled, long Waiting, long Links, long Unlinked)> State() =>
                (await h.UnitScalarAsync<long>(unit, "SELECT count(*) FROM nora_unit.appointments WHERE status='scheduled'"), await h.UnitScalarAsync<long>(unit, "SELECT count(*) FROM nora_unit.waiting_list WHERE status IN ('waiting','offered')"),
                 await h.ScalarAsync<long>("SELECT count(*) FROM nora.patient_units WHERE patient_id=$1 AND unit_id=$2", patientId, unit), await h.ScalarAsync<long>("SELECT count(*) FROM nora.patient_unit_link_events WHERE patient_id=$1 AND unit_id=$2 AND action='unlinked'", patientId, unit));
            async Task Inject(bool deferred)
            {
                await h.ExecuteAsync("CREATE OR REPLACE FUNCTION nora.fail_unlink_event() RETURNS trigger LANGUAGE plpgsql AS $f$ BEGIN IF NEW.action = 'unlinked' THEN RAISE EXCEPTION 'falha injetada'; END IF; RETURN NEW; END $f$");
                await h.ExecuteAsync(deferred
                    ? "CREATE CONSTRAINT TRIGGER fail_unlink AFTER INSERT ON nora.patient_unit_link_events DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION nora.fail_unlink_event()"
                    : "CREATE TRIGGER fail_unlink BEFORE INSERT ON nora.patient_unit_link_events FOR EACH ROW EXECUTE FUNCTION nora.fail_unlink_event()");
            }
            Task Heal() => h.ExecuteAsync("DROP TRIGGER fail_unlink ON nora.patient_unit_link_events");

            // (a) Falha ao gravar o evento central: nada da UBS pode ter sido cancelado, e repetir devolve as contagens completas.
            await LinkAndSeed(0);
            await Inject(deferred: false);
            using (var failed = await patient.PostAsJsonAsync(unlinkPath, new { confirmed = true })) Require((int)failed.StatusCode >= 500, "A falha injetada no lado central deveria falhar a requisição.");
            var afterFailure = await State();
            Require(afterFailure.Scheduled == 2 && afterFailure.Waiting == 1 && afterFailure.Links == 1 && afterFailure.Unlinked == 0, $"Falha central antes do commit deixou a UBS alterada ou o vínculo removido (consultas {afterFailure.Scheduled}, fila {afterFailure.Waiting}, vínculos {afterFailure.Links}).");
            await Heal();
            using (var repeated = await patient.PostAsJsonAsync(unlinkPath, new { confirmed = true }))
            { Expect(repeated, 200); var body = await Read(repeated); Require(body.GetProperty("cancelledAppointments").GetInt32() == 2 && body.GetProperty("cancelledEntries").GetInt32() == 1, "A repetição deveria devolver as contagens completas."); }
            var concluded = await State();
            Require(concluded.Scheduled == 0 && concluded.Waiting == 0 && concluded.Links == 0 && concluded.Unlinked == 1, "A repetição não concluiu a remoção do vínculo.");

            // (b) Falha só no commit central (a UBS já confirmou): as consultas ficam canceladas e o vínculo continua; repetir conclui sem duplicar o evento.
            await LinkAndSeed(1);
            await Inject(deferred: true);
            using (var failedCommit = await patient.PostAsJsonAsync(unlinkPath, new { confirmed = true })) Require((int)failedCommit.StatusCode >= 500, "A falha no commit central deveria falhar a requisição.");
            var partial = await State();
            Require(partial.Links == 1 && partial.Unlinked == 1, "Falha no commit central deveria manter o vínculo e não gravar novo evento.");
            await Heal();
            using (var concludedAgain = await patient.PostAsJsonAsync(unlinkPath, new { confirmed = true })) Expect(concludedAgain, 200);
            var final = await State();
            Require(final.Links == 0 && final.Unlinked == 2 && final.Scheduled == 0 && final.Waiting == 0, "Repetir depois da falha no commit deveria concluir a remoção, com um único evento por remoção.");
        });

        await Check("Histórico: cursor composto (instante + desempate) não perde nem repete eventos empatados; aceita o instante ISO antigo; rejeita cursor inválido", async () =>
        {
            await using var h = await Harness.StartAsync(connectionString);
            var unit = await h.CreateUnitAsync("UBS Histórico");
            var tiedAt = DateTimeOffset.UtcNow.AddMinutes(-30); tiedAt = new(tiedAt.Ticks - tiedAt.Ticks % 10, TimeSpan.Zero); // o PostgreSQL guarda microssegundos
            var tied = tiedAt.ToString("O");
            // Oito eventos de duas tabelas no MESMO instante, mais os eventos normais da criação da UBS em instantes distintos.
            foreach (var action in new[] { "created", "deactivated", "reactivated", "password_reset", "created" })
                await h.ExecuteAsync($"INSERT INTO nora.staff_account_events(account_id,actor_id,action,occurred_at) VALUES($1,$2,'{action}',$3::timestamptz)", h.ObsId, h.ManagerId, tied);
            foreach (var action in new[] { "registry_read", "appointment_read", "registry_read" })
                await h.ExecuteAsync($"INSERT INTO nora.unit_access_events(actor_id,unit_id,action,occurred_at) VALUES($1,$2,'{action}',$3::timestamptz)", h.ManagerId, unit, tied);
            using var everything = await h.Municipal.GetAsync("/api/municipal/history?limit=200"); Expect(everything, 200);
            var all = (await Read(everything)).EnumerateArray().ToArray();
            Require(all.Length >= 9 && all.Count(row => row.GetProperty("occurredAt").GetDateTimeOffset() == tiedAt) == 8, "A carga de eventos empatados não apareceu no histórico.");
            // Percorre o histórico inteiro de 3 em 3 seguindo o cursor de cada página (o campo opaco 'cursor'; sem ele, o instante do último item).
            var collected = new List<string>(); string? before = null;
            for (var page = 0; page < 50; page++)
            {
                using var response = await h.Municipal.GetAsync("/api/municipal/history?limit=3" + (before is null ? "" : $"&before={Uri.EscapeDataString(before)}")); Expect(response, 200);
                var rows = (await Read(response)).EnumerateArray().ToArray();
                collected.AddRange(rows.Select(row => $"{row.GetProperty("occurredAt").GetDateTimeOffset():O}|{row.GetProperty("kind").GetString()}|{(row.TryGetProperty("cursor", out var c) ? c.GetString() : string.Empty)}"));
                if (rows.Length < 3) break;
                var last = rows[^1];
                before = last.TryGetProperty("cursor", out var cursor) && cursor.ValueKind == JsonValueKind.String ? cursor.GetString() : last.GetProperty("occurredAt").GetDateTimeOffset().ToString("O");
            }
            Require(collected.Count == all.Length, $"A paginação por cursor devolveu {collected.Count} eventos de {all.Length}: eventos empatados foram perdidos ou repetidos.");
            Require(collected.Distinct().Count() == collected.Count, "A paginação por cursor repetiu eventos.");
            // Compatibilidade: o instante ISO continua valendo (só eventos estritamente anteriores).
            using (var legacy = await h.Municipal.GetAsync($"/api/municipal/history?limit=200&before={Uri.EscapeDataString(tied)}"))
            { Expect(legacy, 200); Require((await Read(legacy)).EnumerateArray().All(row => row.GetProperty("occurredAt").GetDateTimeOffset() < tiedAt), "O cursor por instante ISO deixou de excluir o próprio instante."); }
            foreach (var invalid in new[] { "c1_", "c1_%%%", "c1_AAAA", "ontem", "12345" })
            { using var rejected = await h.Municipal.GetAsync($"/api/municipal/history?before={Uri.EscapeDataString(invalid)}"); Expect(rejected, 400); }
        });

        var cliDll = Path.Combine(root, ".build", "Nora.Api", "bin", "Release", "net10.0", "Nora.Api.dll");
        if (File.Exists(cliDll))
            await Check("--migrate-all-units e --migrate-unit marcam o banco como pronto sem gravar 'unit-database-created' (só migração: o banco não foi criado pela API)", async () =>
            {
                await using var h = await Harness.StartAsync(connectionString);
                var unit = await h.CreateUnitAsync("UBS Antiga CLI", grantObs: false);
                var explicitConnection = new NpgsqlConnectionStringBuilder(connectionString) { Database = h.Router.DerivedDatabaseName(unit) }.ConnectionString;
                async Task OldDatabase() { await h.ExecuteAsync("UPDATE nora.health_units SET database_ready_at=NULL WHERE id=$1", unit); await h.ExecuteAsync("DELETE FROM nora.unit_database_events WHERE unit_id=$1", unit); }
                foreach (var command in new[] { "--migrate-all-units", $"--migrate-unit={unit}" })
                {
                    await OldDatabase(); // como um banco de antes desta versão: pronto por chave explícita, sem database_ready_at e sem evento
                    var (code, output) = await RunCli(cliDll, connectionString, h.Schema, command, $"--Nora:UnitDatabases:{unit:N}={explicitConnection}");
                    Require(code == 0, $"{command} falhou: {output}");
                    Require(await h.ScalarAsync<bool>("SELECT database_ready_at IS NOT NULL FROM nora.health_units WHERE id=$1", unit), $"{command} não marcou o banco como pronto.");
                    Require(await h.ScalarAsync<long>("SELECT count(*) FROM nora.unit_database_events WHERE unit_id=$1", unit) == 0, $"{command} gravou 'unit-database-created' para um banco que a API não criou.");
                }
                // A criação pela API continua registrando o evento uma única vez, com o autor.
                var created = await h.CreateUnitAsync("UBS Criada pela API", grantObs: false);
                Require(await h.ScalarAsync<long>("SELECT count(*) FROM nora.unit_database_events WHERE unit_id=$1 AND actor_id=$2", created, h.ManagerId) == 1, "A criação pela API deveria registrar o evento com o autor.");
            });
        else Console.WriteLine("SKIP CLI de migração (revisão): compile Nora.Api em Release antes.");

        await Check("Fuso horário configurado desconhecido pelo PostgreSQL impede a subida com mensagem clara; fusos válidos (inclusive em minúsculas) sobem", async () =>
        {
            foreach (var valid in new[] { "America/Sao_Paulo", "america/sao_paulo", "UTC" })
            {
                await using var app = BuildApp(connectionString, "nora_review_zone_" + Guid.NewGuid().ToString("N"), ["--Nora:TimeZone", valid], null);
                await app.StartAsync(); await app.StopAsync();
            }
            await using var invalid = BuildApp(connectionString, "nora_review_zone_" + Guid.NewGuid().ToString("N"), ["--Nora:TimeZone", "Mars/Olympus"], null);
            try { await invalid.StartAsync(); throw new InvalidOperationException("A API subiu com um fuso horário que o PostgreSQL não conhece."); }
            catch (InvalidOperationException error) when (error.Message.Contains("Nora:TimeZone") && error.Message.Contains("Mars/Olympus") && !error.Message.Contains("Host=")) { }
        });

        await Check("Banco de UBS criado pela API não aceita CONNECT de PUBLIC: outro papel é recusado (42501) e o usuário da API continua abrindo", async () =>
        {
            await using var h = await Harness.StartAsync(connectionString);
            var unit = await h.CreateUnitAsync("UBS Reservada", grantObs: false);
            var outsider = "nora_outsider_" + Guid.NewGuid().ToString("N")[..12];
            await h.ExecuteAsync($"CREATE ROLE \"{outsider}\" LOGIN");
            try
            {
                var builder = new NpgsqlConnectionStringBuilder(connectionString) { Username = outsider, Pooling = false };
                await using (var control = new NpgsqlConnection(builder.ConnectionString)) await control.OpenAsync(); // o papel existe e conecta onde PUBLIC ainda tem CONNECT
                builder.Database = h.Router.DerivedDatabaseName(unit);
                try { await using var denied = new NpgsqlConnection(builder.ConnectionString); await denied.OpenAsync(); throw new InvalidOperationException("Um papel qualquer conseguiu conectar ao banco da UBS (PUBLIC ainda tem CONNECT)."); }
                catch (PostgresException error) { Require(error.SqlState == "42501", $"Recusa esperada 42501 (permissão negada), recebida {error.SqlState}."); }
                await using var owner = await h.Router.OpenAsync(unit);
            }
            finally { await h.ExecuteAsync($"DROP ROLE IF EXISTS \"{outsider}\""); }
        });

        await Check("Paciente vinculado a UBS pendente: GET /api/patient/units lista as prontas e omite a pendente (sem 503); sem banco disponível a rota da UBS continua 503", async () =>
        {
            await using var h = await Harness.StartAsync(connectionString);
            var ready = await h.CreateUnitAsync("UBS Pronta do Paciente");
            Guid pending;
            h.App.Configuration["Nora:UnitDatabases:AutoProvision"] = "false";
            try
            {
                pending = await h.CreateUnitAsync("UBS Pendente do Paciente");
                using (var registry = await h.Obs.GetAsync($"/api/obs/units/{pending}/registry")) await ExpectProblem(registry, 503, "service-unavailable"); // sem banco e sem criação automática
            }
            finally { h.App.Configuration["Nora:UnitDatabases:AutoProvision"] = "true"; }
            Require(await h.ScalarAsync<bool>("SELECT database_ready_at IS NULL FROM nora.health_units WHERE id=$1", pending), "A UBS pendente deveria estar sem banco pronto.");
            var patientId = (await h.App.Services.GetRequiredService<AccountService>().ProvisionAsync("patient", "patient", new("Paciente Duas UBS", Cpf(1), "patient1@example.test", Password))).Id;
            using (var register = await h.Obs.PostAsJsonAsync($"/api/obs/units/{ready}/registry", new { cpf = Cpf(1), fullName = "Paciente Duas UBS", email = "patient1@example.test" })) Expect(register, 201);
            using var patient = h.NewClient(); await Login(patient, "patient", Cpf(1));
            using (var link = await patient.PostAsJsonAsync("/api/patient/units/link", new { unitIds = new[] { ready } })) Expect(link, 200);
            await h.ExecuteAsync("INSERT INTO nora.patient_units(patient_id,unit_id) VALUES($1,$2)", patientId, pending); // vínculo anterior à UBS ficar sem banco
            using var response = await patient.GetAsync("/api/patient/units"); Expect(response, 200);
            var ids = (await Read(response)).GetProperty("units").EnumerateArray().Select(unit => unit.GetProperty("id").GetGuid()).ToList();
            Require(ids.Contains(ready) && !ids.Contains(pending), "A lista do paciente deveria trazer a UBS pronta e omitir a pendente.");
        });

        await Check("Espera média parte dos eventos de conclusão da janela: índice (status, occurred_at) existe e o plano o usa", async () =>
        {
            await using var h = await Harness.StartAsync(connectionString);
            var unit = await h.CreateUnitAsync("UBS Espera", grantObs: false);
            Require(await h.UnitScalarAsync<bool>(unit, "SELECT to_regclass('nora_unit.waiting_list_events_status_time_idx') IS NOT NULL"), "O índice (status, occurred_at) de waiting_list_events não existe.");
            await using var connection = await h.Router.OpenAsync(unit);
            // Com poucas linhas o planejador prefere varredura sequencial; desligá-la mostra se o índice é utilizável pela consulta.
            await using (var settings = new NpgsqlCommand("SET enable_seqscan=off", connection)) await settings.ExecuteNonQueryAsync();
            await using var explain = new NpgsqlCommand("EXPLAIN " + UnitMetricsDao.WaitSql, connection);
            explain.Parameters.AddWithValue(NpgsqlTypes.NpgsqlDbType.TimestampTz, DateTimeOffset.UtcNow.AddDays(-30)); explain.Parameters.AddWithValue(NpgsqlTypes.NpgsqlDbType.TimestampTz, DateTimeOffset.UtcNow); explain.Parameters.AddWithValue("");
            var plan = new List<string>();
            await using (var reader = await explain.ExecuteReaderAsync()) while (await reader.ReadAsync()) plan.Add(reader.GetString(0));
            Require(plan.Any(line => line.Contains("waiting_list_events_status_time_idx")), "O plano da espera média não usa o índice (status, occurred_at):\n" + string.Join("\n", plan));
        });

        await Check("Rehash de senha no login não sobrescreve um hash trocado no meio do caminho (WHERE password_hash = hash lido); sem concorrência o rehash acontece", async () =>
        {
            var hasher = new RehashingHasher();
            await using var h = await Harness.StartAsync(connectionString, services: services => services.Replace(ServiceDescriptor.Singleton<IPasswordHasher<Account>>(hasher)));
            var accounts = h.App.Services.GetRequiredService<AccountService>();
            await accounts.ProvisionAsync("patient", "patient", new("Paciente Rehash", Cpf(1), "patient1@example.test", Password));
            await accounts.ProvisionAsync("patient", "patient", new("Paciente Corrida", Cpf(2), "patient2@example.test", Password));
            async Task<string> Hash(int n) => await h.ScalarAsync<string>("SELECT password_hash FROM nora.accounts WHERE cpf=$1", Cpf(n));
            // Sem concorrência: o login com verificação "SuccessRehashNeeded" troca o hash por um novo, que continua validando a mesma senha.
            var before = await Hash(1);
            hasher.ForceRehash = true;
            using (var login = await h.Anonymous.PostAsJsonAsync("/api/auth/login", new { scope = "patient", identifier = Cpf(1), password = Password })) Expect(login, 200);
            var after = await Hash(1);
            Require(after != before && hasher.Verify(after, Password), "O rehash do login não gravou um hash novo e válido.");
            // Com concorrência: a senha da conta muda entre a leitura da conta e o rehash; o rehash não pode desfazer a troca.
            var concurrent = hasher.Create("Senha-trocada-concorrente-123");
            hasher.Hook = () =>
            {
                using var connection = new NpgsqlConnection(connectionString); connection.Open();
                using var update = new NpgsqlCommand($"UPDATE {h.Schema}.accounts SET password_hash=$1 WHERE cpf=$2", connection);
                update.Parameters.AddWithValue(concurrent); update.Parameters.AddWithValue(Cpf(2)); update.ExecuteNonQuery();
            };
            using (var raced = await h.Anonymous.PostAsJsonAsync("/api/auth/login", new { scope = "patient", identifier = Cpf(2), password = Password })) Expect(raced, 200);
            Require(await Hash(2) == concurrent, "O rehash do login sobrescreveu o hash trocado no meio do caminho.");
            hasher.ForceRehash = false;
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

    /// <summary>Maior número de migração de UBS embutida (o carimbo esperado em nora_unit.schema_version).</summary>
    private static int ExpectedUnitSchemaVersion() => typeof(UnitMigrationDao).Assembly.GetManifestResourceNames()
        .Where(name => name.Contains(".Database.UnitMigrations.", StringComparison.Ordinal) && name.EndsWith(".sql", StringComparison.Ordinal))
        .Max(name => int.Parse(name[(name.IndexOf(".UnitMigrations.", StringComparison.Ordinal) + ".UnitMigrations.".Length)..][..3]));

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
            var app = BuildApp(connection, schema, extraArgs, services);
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

        /// <summary>Outra instância da API sobre o mesmo schema e os mesmos bancos (como um reinício do processo, com roteador e cache novos).</summary>
        public async Task<(WebApplication App, HttpClient Obs, HttpClient Municipal)> RestartAsync(string[]? extraArgs = null, Action<IServiceCollection>? services = null)
        {
            var app = BuildApp(Connection, Schema, extraArgs, services);
            await app.StartAsync();
            var obs = Client(app); var municipal = Client(app);
            await Login(obs, "obs", "admin@example.test"); await Login(municipal, "municipal", "manager@example.test");
            Owned.Add(obs); Owned.Add(municipal); Restarted.Add(app);
            return (app, obs, municipal);
        }
        public List<WebApplication> Restarted { get; } = [];

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
            foreach (var restarted in Restarted) { await restarted.StopAsync(); await restarted.DisposeAsync(); }
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

    /// <param name="connection">Nulo sobe a API sem banco (só rotas que não o usam).</param>
    private static WebApplication BuildApp(string? connection, string schema, string[]? extraArgs, Action<IServiceCollection>? services)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Nora.Api", "Nora.Api.csproj"))) directory = directory.Parent;
        var args = new List<string> { "--urls", "http://127.0.0.1:0", "--Nora:Schema", schema, "--Nora:UnitLightningWorker:Enabled", "false", "--Nora:ObsDevices:RequireForAttendants", "false",
            "--Logging:LogLevel:Default", "Warning", "--Logging:LogLevel:Nora.Api.Services.UnitDatabaseProvisioner", "None", "--Nora:Registration:HashKey", "synthetic-review-registration-key-32-bytes" };
        if (connection is not null) args.AddRange(["--ConnectionStrings:Nora", connection]);
        if (extraArgs is not null) args.AddRange(extraArgs);
        return NoraApiHost.Create(new()
        {
            Args = [.. args], EnvironmentName = "Development", ApplicationName = typeof(NoraApiHost).Assembly.GetName().Name,
            ContentRootPath = Path.Combine(directory!.FullName, "Nora.Api")
        }, collection => { collection.Replace(ServiceDescriptor.Singleton<IEmailSender>(new NoMail())); services?.Invoke(collection); });
    }

    /// <summary>Provedor de log que guarda as mensagens (categoria|nível|texto) para conferir avisos sem dado pessoal.</summary>
    private sealed class LogCapture : ILoggerProvider
    {
        public System.Collections.Concurrent.ConcurrentQueue<string> Lines { get; } = new();
        public ILogger CreateLogger(string categoryName) => new Sink(categoryName, Lines);
        public void Dispose() { }
        private sealed class Sink(string category, System.Collections.Concurrent.ConcurrentQueue<string> lines) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                lines.Enqueue($"{category}|{logLevel}|{formatter(state, exception)}");
        }
    }

    private static async Task<(int ExitCode, string Output)> RunCli(string dll, string connection, string schema, params string[] arguments)
    {
        var start = new System.Diagnostics.ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        start.ArgumentList.Add(dll);
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        foreach (var argument in new[] { "--ConnectionStrings:Nora", connection, "--Nora:Schema", schema, "--Logging:LogLevel:Default", "Warning" }) start.ArgumentList.Add(argument);
        using var process = System.Diagnostics.Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(120_000)) { process.Kill(true); throw new TimeoutException("CLI excedeu o prazo."); }
        return (process.ExitCode, await output + await error);
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

    /// <summary>Hasher real que, quando armado, declara SuccessRehashNeeded e roda um gancho depois de conferir a senha (simula troca concorrente).</summary>
    private sealed class RehashingHasher : IPasswordHasher<Account>
    {
        private readonly PasswordHasher<Account> inner = new();
        private static readonly Account Placeholder = new(new(Guid.Empty, "", "", ""), "", "", "");
        public bool ForceRehash { get; set; }
        public Action? Hook { get; set; }
        public string HashPassword(Account user, string password) => inner.HashPassword(user, password);
        public string Create(string password) => inner.HashPassword(Placeholder, password);
        public bool Verify(string hash, string password) => inner.VerifyHashedPassword(Placeholder, hash, password) != PasswordVerificationResult.Failed;
        public PasswordVerificationResult VerifyHashedPassword(Account user, string hashedPassword, string providedPassword)
        {
            var result = inner.VerifyHashedPassword(user, hashedPassword, providedPassword);
            if (result == PasswordVerificationResult.Failed || !ForceRehash) return result;
            var hook = Hook; Hook = null; hook?.Invoke();
            return PasswordVerificationResult.SuccessRehashNeeded;
        }
    }

    private sealed class NoMail : IEmailSender
    {
        public bool IsConfigured => true;
        public Task SendAsync(string address, string subject, string body, CancellationToken ct) => Task.CompletedTask;
    }
}
