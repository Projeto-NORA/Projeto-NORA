using System.Diagnostics;
using System.Net;
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

/// <summary>
/// Verificações das rotas e regras da integração back x front: criação automática do banco da UBS, contas e troca de senha,
/// origem dos eventos, score, indicadores, histórico, resumo e ações do paciente. Usa só NORA_TEST_CONNECTION (cluster descartável);
/// os bancos de UBS são criados automaticamente nesse cluster e ficam nele.
/// </summary>
internal static class PlatformChecks
{
    private const string Password = "Disposable-test-Account!29";

    public static async Task<int> RunAsync(string root)
    {
        var connectionString = Environment.GetEnvironmentVariable("NORA_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Console.WriteLine("SKIP plataforma PostgreSQL: configure NORA_TEST_CONNECTION somente para uma base descartável.");
            return 0;
        }
        var schema = "nora_platform_" + Guid.NewGuid().ToString("N");
        var clock = new CheckClock();
        var t0 = clock.GetUtcNow();
        var checks = 0; var failures = 0;
        await using var app = Create(schema, connectionString, clock);
        var database = app.Services.GetRequiredService<DatabaseHelper>();
        var routers = app.Services.GetRequiredService<UnitDatabaseRouter>();
        try
        {
            await app.Services.GetRequiredService<MigrationDao>().MigrateAsync();
            var accounts = app.Services.GetRequiredService<AccountService>();
            var managerAccount = await accounts.ProvisionAsync("municipal", "manager", new("Gestora de teste", Cpf(90), "manager@example.test", Password));
            var obsAccount = await accounts.ProvisionAsync("obs", "administrator", new("Administrador de teste", Cpf(91), "admin@example.test", Password));
            await accounts.ProvisionAsync("obs", "attendant", new("Atendente sem unidade", Cpf(92), "free@example.test", Password));
            var patientIds = new Dictionary<int, Guid>();
            foreach (var n in new[] { 1, 2, 3, 4, 5, 6, 7 }) patientIds[n] = (await accounts.ProvisionAsync("patient", "patient", new($"Paciente {n}", Cpf(n), $"patient{n}@example.test", Password))).Id;
            await app.StartAsync();
            using var anonymous = Client(app); using var municipal = Client(app); using var obs = Client(app); using var free = Client(app);
            await Login(municipal, "municipal", "manager@example.test"); await Login(obs, "obs", "admin@example.test"); await Login(free, "obs", "free@example.test");
            var patients = new Dictionary<int, HttpClient>();
            foreach (var n in patientIds.Keys) { patients[n] = Client(app); await Login(patients[n], "patient", Cpf(n)); }
            Guid u1 = Guid.Empty, u2 = Guid.Empty, pendingUnit = Guid.Empty;

            async Task<T> Scalar<T>(string sql, params object[] values)
            {
                await using var connection = await database.OpenAsync();
                await using var command = database.Command(connection, sql, values);
                return (T)(await command.ExecuteScalarAsync())!;
            }
            async Task<T> UnitScalar<T>(Guid unit, string sql, params object[] values)
            {
                await using var connection = await routers.OpenAsync(unit);
                await using var command = new NpgsqlCommand(sql, connection);
                foreach (var value in values) command.Parameters.AddWithValue(value);
                return (T)(await command.ExecuteScalarAsync())!;
            }
            async Task<JsonElement> CreateUnit(string name, string region, int status = 201)
            {
                using var response = await municipal.PostAsJsonAsync("/api/municipal/units", new { name, region, address = "Rua de teste, 10", latitude = -23.55, longitude = -46.63 });
                Expect(response, status);
                return await Read(response);
            }
            async Task<Guid> Book(Guid unit, string cpf, string specialty, string professional, DateTimeOffset start, int minutes = 30)
            {
                using var response = await obs.PostAsJsonAsync($"/api/obs/units/{unit}/appointments", new { cpf, specialty, professional, startsAt = start, endsAt = start.AddMinutes(minutes) });
                Expect(response, 201);
                return (await Read(response)).GetProperty("id").GetGuid();
            }
            async Task Status(Guid unit, Guid appointment, string status, HttpClient? actor = null)
            {
                using var response = await (actor ?? obs).PatchAsJsonAsync($"/api/obs/units/{unit}/appointments/{appointment}/status", new { status, confirmed = status == "cancelled" });
                Expect(response, 200);
            }
            Task Advance(Guid unit) => app.Services.GetRequiredService<UnitLightningDispatcher>().AdvanceAsync(unit, default);
            async Task Register(Guid unit, int n, string? name = null)
            {
                using var response = await obs.PostAsJsonAsync($"/api/obs/units/{unit}/registry", new { cpf = Cpf(n), fullName = name ?? $"Paciente {n}", email = $"patient{n}@example.test" });
                Expect(response, 201);
            }
            async Task<JsonElement> Attendance(Guid unit, int n)
            {
                using var response = await obs.GetAsync($"/api/obs/units/{unit}/registry");
                Expect(response, 200);
                return (await Read(response)).EnumerateArray().Single(p => p.GetProperty("cpf").GetString() == Cpf(n)).GetProperty("attendance");
            }

            await Check("Migrações centrais reaplicadas e colunas da integração presentes", async () =>
            {
                await app.Services.GetRequiredService<MigrationDao>().MigrateAsync();
                Require(await Scalar<long>("SELECT count(*) FROM information_schema.columns WHERE table_schema=$1 AND ((table_name='accounts' AND column_name='must_change_password') OR (table_name='health_units' AND column_name='database_ready_at') OR (table_name='patient_unit_link_events' AND column_name='action'))", schema) == 3, "Colunas da migração 014 ausentes.");
                Require(await Scalar<long>("SELECT count(*) FROM information_schema.tables WHERE table_schema=$1 AND table_name IN ('staff_account_events','unit_database_events')", schema) == 2, "Tabelas da migração 014 ausentes.");
            });

            await Check("Cadastrar UBS cria o banco, migra, marca pronto e registra o evento; repetir é idempotente", async () =>
            {
                var draft = new { name = "UBS Um", region = "Centro", address = "Rua de teste, 10", latitude = -23.55, longitude = -46.63 };
                using (var anonymousCreate = await anonymous.PostAsJsonAsync("/api/municipal/units", draft)) Expect(anonymousCreate, 401);
                using (var obsCreate = await obs.PostAsJsonAsync("/api/municipal/units", draft)) Expect(obsCreate, 403);
                var created = await CreateUnit("UBS Um", "Centro");
                u1 = created.GetProperty("id").GetGuid();
                Require(created.GetProperty("databaseState").GetString() == "ready", "UBS recém-cadastrada deveria ter o banco pronto.");
                var databaseName = "nora_ubs_" + u1.ToString("N");
                Require(await Scalar<long>("SELECT count(*) FROM pg_database WHERE datname=$1", databaseName) == 1, "Banco da UBS não foi criado.");
                Require(await UnitScalar<long>(u1, "SELECT count(*) FROM information_schema.tables WHERE table_schema='nora_unit' AND table_name='appointment_events'") == 1, "Migrações de UBS não foram aplicadas.");
                Require(await Scalar<bool>("SELECT database_ready_at IS NOT NULL FROM nora.health_units WHERE id=$1", u1), "database_ready_at não foi registrado.");
                Require(await Scalar<long>("SELECT count(*) FROM nora.unit_database_events WHERE unit_id=$1 AND actor_id=$2", u1, managerAccount.Id) == 1, "Evento unit-database-created ausente ou sem autor.");
                for (var repeat = 0; repeat < 2; repeat++)
                {
                    using var again = await municipal.PostAsync($"/api/municipal/units/{u1}/database", null); Expect(again, 200);
                    Require((await Read(again)).GetProperty("databaseState").GetString() == "ready", "Nova tentativa não devolveu a unidade pronta.");
                }
                Require(await Scalar<long>("SELECT count(*) FROM nora.unit_database_events WHERE unit_id=$1", u1) == 1, "Repetição duplicou o evento.");
                using (var unknown = await municipal.PostAsync($"/api/municipal/units/{Guid.NewGuid()}/database", null)) Expect(unknown, 404);
                using (var denied = await obs.PostAsync($"/api/municipal/units/{u1}/database", null)) Expect(denied, 403);
                using (var anonymousRetry = await anonymous.PostAsync($"/api/municipal/units/{u1}/database", null)) Expect(anonymousRetry, 401);
                using (var list = await municipal.GetAsync("/api/municipal/units")) { Expect(list, 200); Require((await Read(list)).EnumerateArray().Single(u => u.GetProperty("id").GetGuid() == u1).GetProperty("databaseState").GetString() == "ready", "Listagem sem databaseState."); }
                using (var update = await municipal.PutAsJsonAsync($"/api/municipal/units/{u1}", draft)) { Expect(update, 200); Require((await Read(update)).GetProperty("databaseState").GetString() == "ready", "Alteração perdeu databaseState."); }
                var migrations = app.Services.GetRequiredService<UnitMigrationDao>();
                await migrations.MigrateAsync(u1); await migrations.MigrateAsync(u1);
                Require((await UnitScalar<string>(u1, "SELECT column_default FROM information_schema.columns WHERE table_schema='nora_unit' AND table_name='appointment_events' AND column_name='source'")).Contains("staff"), "Coluna source sem padrão 'staff'.");
                u2 = (await CreateUnit("UBS Dois", "Norte")).GetProperty("id").GetGuid();
            });

            await Check("Banco não criado deixa a UBS pending, fora do catálogo, do vínculo, do resumo e dos indicadores; nova tentativa funciona", async () =>
            {
                app.Configuration["Nora:UnitDatabases:AutoProvision"] = "false";
                JsonElement pending;
                try { pending = await CreateUnit("UBS Pendente", "Leste"); }
                finally { app.Configuration["Nora:UnitDatabases:AutoProvision"] = "true"; }
                pendingUnit = pending.GetProperty("id").GetGuid();
                Require(pending.GetProperty("databaseState").GetString() == "pending", "UBS sem banco deveria ficar pending.");
                Require(await Scalar<bool>("SELECT database_ready_at IS NULL FROM nora.health_units WHERE id=$1", pendingUnit), "database_ready_at preenchido sem banco.");
                using (var catalog = await anonymous.GetAsync("/api/registration/units"))
                {
                    Expect(catalog, 200); var ids = (await Read(catalog)).GetProperty("units").EnumerateArray().Select(u => u.GetProperty("id").GetGuid()).ToList();
                    Require(ids.Contains(u1) && !ids.Contains(pendingUnit), "Catálogo público deve listar só UBS prontas.");
                }
                using (var link = await patients[1].PostAsJsonAsync("/api/patient/units/link", new { unitIds = new[] { pendingUnit } })) Expect(link, 400);
                using (var start = await anonymous.PostAsJsonAsync("/api/registration/start", new { fullName = "Paciente Pendente", cpf = Cpf(8), password = Password, unitIds = new[] { pendingUnit } })) Expect(start, 400);
                using (var summary = await municipal.GetAsync("/api/municipal/summary")) { Expect(summary, 200); Require((await Read(summary)).GetProperty("unitsWithoutDatabase").GetInt64() == 1, "Resumo não contou a UBS sem banco."); }
                var window = $"from={Uri.EscapeDataString(t0.ToString("O"))}&to={Uri.EscapeDataString(t0.AddDays(30).ToString("O"))}";
                using (var metrics = await municipal.GetAsync($"/api/municipal/metrics?{window}"))
                {
                    Expect(metrics, 200); var body = await Read(metrics);
                    Require(body.GetProperty("unitsWithoutDatabase").GetInt64() == 1 && body.GetProperty("byUnit").EnumerateArray().All(u => u.GetProperty("unitId").GetGuid() != pendingUnit), "Indicadores deveriam ignorar e contar a UBS pending.");
                }
                app.Configuration["Nora:UnitDatabases:AutoProvision"] = "false";
                try { using var failed = await municipal.PostAsync($"/api/municipal/units/{pendingUnit}/database", null); await ExpectProblem(failed, 503, "database-provisioning-failed"); }
                finally { app.Configuration["Nora:UnitDatabases:AutoProvision"] = "true"; }
                Require(await Scalar<long>("SELECT count(*) FROM nora.unit_database_events WHERE unit_id=$1", pendingUnit) == 0, "Falha registrou evento de banco criado.");
                // Mantida pending até o fim: resumo e indicadores a contam; a tentativa final acontece no último passo.
            });

            await Check("Sem permissão CREATEDB a UBS continua cadastrada como pending e o 201 informa", async () =>
            {
                var role = "nora_limited_" + Guid.NewGuid().ToString("N")[..12];
                var builder = new NpgsqlConnectionStringBuilder(connectionString);
                var limitedSchema = "nora_limited_" + Guid.NewGuid().ToString("N");
                await using (var admin = await database.OpenAsync())
                {
                    await database.ExecuteAsync(admin, $"CREATE ROLE \"{role}\" LOGIN", default);
                    await database.ExecuteAsync(admin, $"GRANT CREATE ON DATABASE \"{builder.Database}\" TO \"{role}\"", default);
                }
                try
                {
                    builder.Username = role;
                    await using (var limited = new NpgsqlConnection(builder.ConnectionString))
                    {
                        await limited.OpenAsync();
                        try { await using var create = new NpgsqlCommand("CREATE DATABASE nora_limited_probe", limited); await create.ExecuteNonQueryAsync(); throw new InvalidOperationException("O papel sem CREATEDB conseguiu criar banco."); }
                        catch (PostgresException error) { Require(error.SqlState == "42501", "A recusa esperada era 42501 (permissão negada)."); }
                    }
                    await using var limitedApp = Create(limitedSchema, builder.ConnectionString, new CheckClock());
                    await limitedApp.Services.GetRequiredService<MigrationDao>().MigrateAsync();
                    await limitedApp.Services.GetRequiredService<AccountService>().ProvisionAsync("municipal", "manager", new("Gestora limitada", Cpf(93), "limited@example.test", Password));
                    await limitedApp.StartAsync();
                    try
                    {
                        using var limitedManager = Client(limitedApp); await Login(limitedManager, "municipal", "limited@example.test");
                        using var created = await limitedManager.PostAsJsonAsync("/api/municipal/units", new { name = "UBS Sem Permissão", region = "Sul", address = "Rua de teste, 20", latitude = -23.5, longitude = -46.6 });
                        Expect(created, 201); var body = await Read(created);
                        Require(body.GetProperty("databaseState").GetString() == "pending", "Falha ao criar o banco deveria deixar a UBS pending.");
                        using var retry = await limitedManager.PostAsync($"/api/municipal/units/{body.GetProperty("id").GetGuid()}/database", null);
                        await ExpectProblem(retry, 503, "database-provisioning-failed");
                    }
                    finally { await limitedApp.StopAsync(); }
                }
                finally
                {
                    await using var admin = await database.OpenAsync();
                    await database.ExecuteAsync(admin, $"DROP SCHEMA IF EXISTS {limitedSchema} CASCADE", default);
                    await database.ExecuteAsync(admin, $"DROP OWNED BY \"{role}\"", default);
                    await database.ExecuteAsync(admin, $"DROP ROLE \"{role}\"", default);
                }
            });

            var cliDll = Path.Combine(root, ".build", "Nora.Api", "bin", "Release", "net10.0", "Nora.Api.dll");
            if (File.Exists(cliDll))
                await Check("CLI --migrate-unit marca o estado e --migrate-all-units migra todas as UBS prontas", async () =>
                {
                    var (single, singleOutput) = await RunCli(cliDll, connectionString, schema, $"--migrate-unit={u1}");
                    Require(single == 0 && singleOutput.Contains("Banco da UBS atualizado"), "--migrate-unit falhou.");
                    var ready = await Scalar<long>("SELECT count(*) FROM nora.health_units WHERE database_ready_at IS NOT NULL");
                    var (all, allOutput) = await RunCli(cliDll, connectionString, schema, "--migrate-all-units");
                    Require(all == 0 && allOutput.Contains($"Bancos de UBS atualizados: {ready}; falhas: 0."), $"--migrate-all-units não migrou as {ready} UBS prontas.");
                    var (conflict, _) = await RunCli(cliDll, connectionString, schema, "--migrate-all-units", "--migrate");
                    Require(conflict != 0, "--migrate-all-units combinado com --migrate deveria ser recusado.");
                });
            else Console.WriteLine("SKIP CLI de migração: compile Nora.Api em Release antes.");

            // ---- Contas, troca de senha e troca obrigatória ----
            Guid staffId = Guid.Empty;
            await Check("Secretaria cria conta OBS com senha provisória; validações, duplicidade e autorização", async () =>
            {
                var good = new { fullName = "Atendente Nova", email = "NOVA@example.test", password = "Provisoria-123", role = "attendant" };
                using (var anonymousCreate = await anonymous.PostAsJsonAsync("/api/municipal/staff", good)) Expect(anonymousCreate, 401);
                using (var obsCreate = await obs.PostAsJsonAsync("/api/municipal/staff", good)) Expect(obsCreate, 403);
                using (var patientCreate = await patients[1].PostAsJsonAsync("/api/municipal/staff", good)) Expect(patientCreate, 403);
                foreach (var invalid in new object[] { new { fullName = "A", email = "x@example.test", password = "Provisoria-123", role = "attendant" }, new { fullName = "Atendente", password = "Provisoria-123", role = "attendant" },
                    new { fullName = "Atendente", email = "inválido", password = "Provisoria-123", role = "attendant" }, new { fullName = "Atendente", email = "x@example.test", password = "", role = "attendant" },
                    new { fullName = "Atendente", email = "x@example.test", password = "Provisoria-123", role = "manager" }, new { fullName = "Atendente", email = "x@example.test", password = "Provisoria-123", role = "patient" } })
                { using var rejected = await municipal.PostAsJsonAsync("/api/municipal/staff", invalid); Expect(rejected, 400); }
                using var created = await municipal.PostAsJsonAsync("/api/municipal/staff", good); Expect(created, 201);
                var body = await Read(created); staffId = body.GetProperty("id").GetGuid();
                Require(body.GetProperty("role").GetString() == "attendant" && body.GetProperty("isActive").GetBoolean() && body.GetProperty("mustChangePassword").GetBoolean() && body.GetProperty("email").GetString() == "nova@example.test", "Conta criada com contrato incorreto.");
                Require(!(await created.Content.ReadAsStringAsync()).Contains("Provisoria"), "Resposta ecoou a senha.");
                using (var duplicateEmail = await municipal.PostAsJsonAsync("/api/municipal/staff", new { fullName = "Outra", email = "nova@example.test", password = "Provisoria-123", role = "administrator" })) Expect(duplicateEmail, 409);
                using var list = await municipal.GetAsync("/api/municipal/staff"); Expect(list, 200);
                var row = (await Read(list)).EnumerateArray().Single(a => a.GetProperty("id").GetGuid() == staffId);
                Require(row.GetProperty("email").GetString() == "nova@example.test" && row.GetProperty("isActive").GetBoolean() && row.GetProperty("mustChangePassword").GetBoolean() && row.GetProperty("fullName").GetString() == "Atendente Nova", "Listagem de contas sem os campos novos.");
                Require(await Scalar<long>("SELECT count(*) FROM nora.staff_account_events WHERE account_id=$1 AND actor_id=$2 AND action='created'", staffId, managerAccount.Id) == 1, "Criação de conta não gravou evento.");
            });

            await Check("Senha provisória bloqueia rotas até a troca; senha atual incorreta é 400 e não 401; troca revoga as outras sessões", async () =>
            {
                using var first = Client(app); using var second = Client(app);
                await Login(first, "obs", "nova@example.test", "Provisoria-123");
                using (var login = await anonymous.PostAsJsonAsync("/api/auth/login", new { scope = "obs", identifier = "nova@example.test", password = "Provisoria-123" }))
                { Expect(login, 200); Require((await Read(login)).GetProperty("user").GetProperty("mustChangePassword").GetBoolean(), "Login não informou a troca obrigatória."); }
                await Login(second, "obs", "nova@example.test", "Provisoria-123");
                using (var me = await first.GetAsync("/api/auth/me")) { Expect(me, 200); Require((await Read(me)).GetProperty("mustChangePassword").GetBoolean(), "/me não informou a troca obrigatória."); }
                foreach (var blocked in new[] { "/api/obs/units", $"/api/obs/units/{u1}/registry", $"/api/obs/units/{u1}/metrics?from=2026-10-07T12:00:00Z&to=2026-10-08T12:00:00Z" })
                { using var response = await first.GetAsync(blocked); await ExpectProblem(response, 403, "password-change-required"); }
                using (var blockedWrite = await first.PostAsJsonAsync($"/api/obs/units/{u1}/waiting-list", new { cpf = Cpf(1), specialty = "X" })) await ExpectProblem(blockedWrite, 403, "password-change-required");
                using (var anonymousPassword = await anonymous.PostAsJsonAsync("/api/auth/password", new { currentPassword = "a", newPassword = "Senha-nova-123" })) Expect(anonymousPassword, 401);
                using (var wrong = await first.PostAsJsonAsync("/api/auth/password", new { currentPassword = "Errada-123456", newPassword = "Senha-nova-123" })) await ExpectProblem(wrong, 400, "invalid-current-password");
                using (var same = await first.PostAsJsonAsync("/api/auth/password", new { currentPassword = "Provisoria-123", newPassword = "Provisoria-123" })) await ExpectProblem(same, 400, "invalid-input");
                // Senha sem restrições (2026-11-24): só a vazia e a acima de 256 caracteres são recusadas.
                using (var empty = await first.PostAsJsonAsync("/api/auth/password", new { currentPassword = "Provisoria-123", newPassword = "" })) await ExpectProblem(empty, 400, "invalid-input");
                using (var tooLong = await first.PostAsJsonAsync("/api/auth/password", new { currentPassword = "Provisoria-123", newPassword = new string('x', 257) })) await ExpectProblem(tooLong, 400, "invalid-input");
                using (var missing = await first.PostAsJsonAsync("/api/auth/password", new { currentPassword = "Provisoria-123" })) Expect(missing, 400);
                using (var stillBlocked = await first.GetAsync("/api/obs/units")) Expect(stillBlocked, 403);
                using (var changed = await first.PostAsJsonAsync("/api/auth/password", new { currentPassword = "Provisoria-123", newPassword = "Senha-nova-123" })) Expect(changed, 204);
                using (var open = await first.GetAsync("/api/obs/units")) Expect(open, 200);
                using (var me = await first.GetAsync("/api/auth/me")) { Expect(me, 200); Require(!(await Read(me)).GetProperty("mustChangePassword").GetBoolean(), "A troca não limpou must_change_password."); }
                using (var otherSession = await second.GetAsync("/api/auth/me")) Expect(otherSession, 401);
                using (var oldLogin = await anonymous.PostAsJsonAsync("/api/auth/login", new { scope = "obs", identifier = "nova@example.test", password = "Provisoria-123" })) Expect(oldLogin, 401);
                using (var newLogin = await anonymous.PostAsJsonAsync("/api/auth/login", new { scope = "obs", identifier = "nova@example.test", password = "Senha-nova-123" })) { Expect(newLogin, 200); Require(!(await Read(newLogin)).GetProperty("user").GetProperty("mustChangePassword").GetBoolean(), "Novo login ainda exige troca."); }
                // Qualquer perfil troca a própria senha: o paciente 7 troca para uma senha curta só de números (aceita) e volta ao valor original.
                using (var patientChange = await patients[7].PostAsJsonAsync("/api/auth/password", new { currentPassword = Password, newPassword = "1234" })) Expect(patientChange, 204);
                using (var patientRestore = await patients[7].PostAsJsonAsync("/api/auth/password", new { currentPassword = "1234", newPassword = Password })) Expect(patientRestore, 204);
            });

            await Check("Desativar, reativar e redefinir senha de conta OBS: confirmação, sessões, concessões preservadas e autorização", async () =>
            {
                using var grant = await municipal.PutAsync($"/api/municipal/staff/{staffId}/units/{u1}", null); Expect(grant, 204);
                using var session = Client(app); await Login(session, "obs", "nova@example.test", "Senha-nova-123");
                var path = $"/api/municipal/staff/{staffId}/active";
                using (var anonymousPatch = await anonymous.PatchAsJsonAsync(path, new { isActive = false, confirmed = true })) Expect(anonymousPatch, 401);
                using (var obsPatch = await obs.PatchAsJsonAsync(path, new { isActive = false, confirmed = true })) Expect(obsPatch, 403);
                using (var unconfirmed = await municipal.PatchAsJsonAsync(path, new { isActive = false, confirmed = false })) Expect(unconfirmed, 400);
                using (var missingField = await municipal.PatchAsJsonAsync(path, new { isActive = false })) Expect(missingField, 400);
                using (var notStaff = await municipal.PatchAsJsonAsync($"/api/municipal/staff/{patientIds[1]}/active", new { isActive = false, confirmed = true })) Expect(notStaff, 404);
                using (var unknown = await municipal.PatchAsJsonAsync($"/api/municipal/staff/{Guid.NewGuid()}/active", new { isActive = false, confirmed = true })) Expect(unknown, 404);
                using (var deactivate = await municipal.PatchAsJsonAsync(path, new { isActive = false, confirmed = true })) Expect(deactivate, 204);
                using (var oldSession = await session.GetAsync("/api/auth/me")) Expect(oldSession, 401);
                using (var blockedLogin = await anonymous.PostAsJsonAsync("/api/auth/login", new { scope = "obs", identifier = "nova@example.test", password = "Senha-nova-123" })) Expect(blockedLogin, 401);
                using (var list = await municipal.GetAsync("/api/municipal/staff")) { Expect(list, 200); Require(!(await Read(list)).EnumerateArray().Single(a => a.GetProperty("id").GetGuid() == staffId).GetProperty("isActive").GetBoolean(), "Conta inativa deve continuar listada como inativa."); }
                Require(await Scalar<long>("SELECT count(*) FROM nora.staff_units WHERE account_id=$1 AND unit_id=$2", staffId, u1) == 1, "Desativar removeu a concessão de UBS.");
                using (var repeat = await municipal.PatchAsJsonAsync(path, new { isActive = false, confirmed = true })) Expect(repeat, 204);
                Require(await Scalar<long>("SELECT count(*) FROM nora.staff_account_events WHERE account_id=$1 AND action='deactivated'", staffId) == 1, "Repetir a desativação duplicou o evento.");
                using (var reactivate = await municipal.PatchAsJsonAsync(path, new { isActive = true, confirmed = true })) Expect(reactivate, 204);
                using (var back = Client(app)) { await Login(back, "obs", "nova@example.test", "Senha-nova-123"); using var units = await back.GetAsync("/api/obs/units"); Expect(units, 200); Require((await Read(units)).EnumerateArray().Any(u => u.GetProperty("id").GetGuid() == u1), "Concessão não voltou com a conta."); }
                using var resetSession = Client(app); await Login(resetSession, "obs", "nova@example.test", "Senha-nova-123");
                var passwordPath = $"/api/municipal/staff/{staffId}/password";
                using (var anonymousReset = await anonymous.PostAsJsonAsync(passwordPath, new { password = "Provisoria-456", confirmed = true })) Expect(anonymousReset, 401);
                using (var obsReset = await obs.PostAsJsonAsync(passwordPath, new { password = "Provisoria-456", confirmed = true })) Expect(obsReset, 403);
                using (var unconfirmedReset = await municipal.PostAsJsonAsync(passwordPath, new { password = "Provisoria-456", confirmed = false })) Expect(unconfirmedReset, 400);
                using (var longReset = await municipal.PostAsJsonAsync(passwordPath, new { password = new string('x', 257), confirmed = true })) Expect(longReset, 400);
                using (var unknownReset = await municipal.PostAsJsonAsync($"/api/municipal/staff/{Guid.NewGuid()}/password", new { password = "Provisoria-456", confirmed = true })) Expect(unknownReset, 404);
                using (var reset = await municipal.PostAsJsonAsync(passwordPath, new { password = "Provisoria-456", confirmed = true })) Expect(reset, 204);
                using (var revoked = await resetSession.GetAsync("/api/auth/me")) Expect(revoked, 401);
                using (var oldPassword = await anonymous.PostAsJsonAsync("/api/auth/login", new { scope = "obs", identifier = "nova@example.test", password = "Senha-nova-123" })) Expect(oldPassword, 401);
                using var again = Client(app); await Login(again, "obs", "nova@example.test", "Provisoria-456");
                using (var mustChange = await again.GetAsync("/api/obs/units")) await ExpectProblem(mustChange, 403, "password-change-required");
                var events = await Scalar<long>("SELECT count(DISTINCT action) FROM nora.staff_account_events WHERE account_id=$1 AND actor_id=$2", staffId, managerAccount.Id);
                Require(events == 4, "Eventos de conta (created, deactivated, reactivated, password_reset) incompletos.");
            });

            await Check("Senha sem restrições: curta e sem número aceita; vazia e acima de 256 recusadas (regra única, equipe, provisionamento, cadastro pelo app e redefinição)", async () =>
            {
                Require(AccountService.ValidPassword("a") && AccountService.ValidPassword("1234") && AccountService.ValidPassword("abcd") && AccountService.ValidPassword("   ") && AccountService.ValidPassword(new string('x', 256)), "Senha curta, sem número ou só de espaços deveria valer.");
                Require(!AccountService.ValidPassword(null) && !AccountService.ValidPassword("") && !AccountService.ValidPassword(new string('x', 257)), "Senha nula, vazia ou acima de 256 deveria ser recusada.");
                var hasher = app.Services.GetRequiredService<Microsoft.AspNetCore.Identity.IPasswordHasher<Nora.Api.Models.Account>>();
                async Task<bool> Matches(Guid id, string password)
                {
                    var hash = await Scalar<string>("SELECT password_hash FROM nora.accounts WHERE id=$1", id);
                    return hasher.VerifyHashedPassword(new(new(id, "", "", ""), "", "", ""), hash, password) != Microsoft.AspNetCore.Identity.PasswordVerificationResult.Failed;
                }
                async Task Rejected(Func<Task> action, string message)
                {
                    try { await action(); }
                    catch (ApiInputException) { return; }
                    throw new InvalidOperationException(message);
                }
                // Equipe (redefinição pela Secretaria): vazia e 257 → 400; "a" vale e depois volta à senha provisória que o check anterior deixou.
                var staffService = app.Services.GetRequiredService<StaffAccountService>();
                await Rejected(() => staffService.ResetPasswordAsync(managerAccount.Id, staffId, new("", true), default), "Redefinição da equipe aceitou senha vazia.");
                await Rejected(() => staffService.ResetPasswordAsync(managerAccount.Id, staffId, new(new string('x', 257), true), default), "Redefinição da equipe aceitou 257 caracteres.");
                await staffService.ResetPasswordAsync(managerAccount.Id, staffId, new("a", true), default);
                Require(await Matches(staffId, "a"), "Senha curta da equipe não foi gravada.");
                await staffService.ResetPasswordAsync(managerAccount.Id, staffId, new("Provisoria-456", true), default);
                await Rejected(() => staffService.CreateAsync(managerAccount.Id, new("Equipe Vazia", "vazia@example.test", "", "attendant"), default), "Conta da equipe criada com senha vazia.");
                await Rejected(() => staffService.CreateAsync(managerAccount.Id, new("Equipe Longa", "longa@example.test", new string('x', 257), "attendant"), default), "Conta da equipe criada com 257 caracteres.");
                // Provisionamento (--provision e contas de teste): curta aceita, vazia e 257 recusadas.
                var provisioned = await accounts.ProvisionStaffAsync("municipal", "manager", "Gestora senha curta", "senha.curta@example.test", "1234");
                Require(await Matches(provisioned.Id, "1234"), "Provisionamento não gravou a senha curta.");
                await Rejected(() => accounts.ProvisionStaffAsync("municipal", "manager", "Gestora vazia", "gestora.vazia@example.test", ""), "Provisionamento aceitou senha vazia.");
                await Rejected(() => accounts.ProvisionStaffAsync("municipal", "manager", "Gestora longa", "gestora.longa@example.test", new string('x', 257)), "Provisionamento aceitou 257 caracteres.");
                // Cadastro pelo app (primeiro acesso): vazia e 257 → 400; "a" passa da validação (o resto depende de chave e e-mail configurados).
                var registration = app.Services.GetRequiredService<PatientRegistrationService>();
                await Rejected(() => registration.StartAsync(new("Paciente Senha", Cpf(97), "", [u1]), default), "Cadastro pelo app aceitou senha vazia.");
                await Rejected(() => registration.StartAsync(new("Paciente Senha", Cpf(97), new string('x', 257), [u1]), default), "Cadastro pelo app aceitou 257 caracteres.");
                try { await registration.StartAsync(new("Paciente Senha", Cpf(97), "a", [u1]), default); }
                catch (ApiInputException) { throw new InvalidOperationException("Cadastro pelo app recusou senha curta."); }
                catch (EmailDeliveryUnavailableException) { }
                // Redefinição por código: vazia e 257 → 400; "a" passa da validação (código malformado só devolve codeValid=false).
                var reset = app.Services.GetRequiredService<PasswordResetService>();
                await Rejected(() => reset.FinishAsync(new(Guid.NewGuid(), "000000", ""), default), "Redefinição por código aceitou senha vazia.");
                await Rejected(() => reset.FinishAsync(new(Guid.NewGuid(), "000000", new string('x', 257)), default), "Redefinição por código aceitou 257 caracteres.");
                Require(!(await reset.FinishAsync(new(Guid.NewGuid(), "x", "a"), default)).CodeValid, "Código malformado não pode valer.");
            });

            await Check("Cadastros de referência e vínculos dos pacientes de teste", async () =>
            {
                foreach (var unit in new[] { u1, u2 }) { using var grant = await municipal.PutAsync($"/api/municipal/staff/{obsAccount.Id}/units/{unit}", null); Expect(grant, 204); }
                foreach (var n in new[] { 1, 2, 3, 5, 7, 8, 9 }) await Register(u1, n);
                foreach (var n in new[] { 1, 2, 4, 5, 6 }) await Register(u2, n);
                await Register(u2, 20, "Paciente de origem");
                foreach (var n in new[] { 1, 2, 3, 4, 5, 6, 7 })
                { using var link = await patients[n].PostAsJsonAsync("/api/patient/units/link", new { unitIds = new[] { u1, u2 } }); Expect(link, 200); }
                Require(await Scalar<long>("SELECT count(*) FROM nora.patient_units WHERE unit_id=$1", u2) == 5 && await Scalar<long>("SELECT count(*) FROM nora.patient_units WHERE unit_id=$1", u1) == 5, "Vínculos de teste incorretos.");
                // P8 só existe (e ficará desativado) na UBS 1; P6 só na UBS 2 e é desativado no passo do vínculo.
                using var deactivate = await obs.PatchAsJsonAsync($"/api/obs/units/{u1}/registry/active", new { cpf = Cpf(8), isActive = false, confirmed = true }); Expect(deactivate, 204);
            });

            // ---- UBS 2: fila, vagas, ofertas, cancelamento e saída do paciente (relógio em minutos desde T0) ----
            Guid slot1 = Guid.Empty, slot2 = Guid.Empty;
            await Check("Vaga Relâmpago automática: oferta a quem tem consulta posterior (a mais distante primeiro), recusa, aceite com antecipação e cascata; consulta a antecipar cancelada passa ao próximo; ofertas da vaga para o OBS", async () =>
            {
                var a1 = await Book(u2, Cpf(20), "Neurologia", "Prof Neuro 1", t0.AddDays(3).AddHours(-2));
                var a2 = await Book(u2, Cpf(20), "Neurologia", "Prof Neuro 2", t0.AddDays(4));
                // Candidatos (mesma especialidade, início posterior, sem histórico = mesmo score): a mais distante primeiro → P2, P4, P1 (especialidade escrita de outro jeito).
                var p2Later = await Book(u2, Cpf(2), "Neurologia", "Prof Neuro Futuro 2", t0.AddDays(30));
                var p4Later = await Book(u2, Cpf(4), "Neurologia", "Prof Neuro Futuro 4", t0.AddDays(20));
                var p1Later = await Book(u2, Cpf(1), "neurologia", "Prof Neuro Futuro 1", t0.AddDays(10));
                clock.AdvanceTo(t0.AddMinutes(30));
                await Status(u2, a1, "cancelled");
                using (var slotResponse = await obs.PostAsJsonAsync($"/api/obs/units/{u2}/lightning-slots", new { appointmentId = a1, confirmed = true })) { Expect(slotResponse, 201); slot1 = (await Read(slotResponse)).GetProperty("id").GetGuid(); }
                var offersPath = $"/api/patient/units/{u2}/lightning-offers";
                Guid offerP2;
                using (var offers = await patients[2].GetAsync(offersPath))
                {
                    Expect(offers, 200); var pending = (await Read(offers)).GetProperty("offers").EnumerateArray().Single(o => o.GetProperty("status").GetString() == "pending");
                    offerP2 = pending.GetProperty("id").GetGuid(); Require(pending.GetProperty("replacesAppointmentId").GetGuid() == p2Later, "A oferta deveria dizer qual consulta será antecipada.");
                }
                clock.AdvanceTo(t0.AddMinutes(31));
                using (var decline = await patients[2].PostAsJsonAsync($"{offersPath}/{offerP2}/respond", new { accept = false })) Expect(decline, 200);
                Require(await UnitScalar<string>(u2, "SELECT status FROM nora_unit.appointments WHERE id=$1", p2Later) == "scheduled", "Recusar não mexe na consulta marcada.");
                Guid offerP4;
                using (var offers = await patients[4].GetAsync(offersPath)) { Expect(offers, 200); offerP4 = (await Read(offers)).GetProperty("offers").EnumerateArray().Single(o => o.GetProperty("status").GetString() == "pending").GetProperty("id").GetGuid(); }
                clock.AdvanceTo(t0.AddMinutes(33));
                using (var accept = await patients[4].PostAsJsonAsync($"{offersPath}/{offerP4}/respond", new { accept = true })) { Expect(accept, 200); Require((await Read(accept)).GetProperty("accepted").GetBoolean(), "Aceite não confirmado."); }
                Require(await UnitScalar<string>(u2, "SELECT a.status||':'||e.source FROM nora_unit.appointments a JOIN nora_unit.appointment_events e ON e.appointment_id=a.id AND e.status='cancelled' WHERE a.id=$1", p4Later) == "cancelled:rescheduled-lightning", "O aceite deveria cancelar a consulta antecipada.");
                // Cascata: o horário antigo do P4 virou vaga e foi oferecido ao P2 (que só recusou a outra vaga).
                Require(await UnitScalar<string>(u2, "SELECT s.origin||':'||o.cpf||':'||o.status FROM nora_unit.lightning_slots s JOIN nora_unit.lightning_offers o ON o.slot_id=s.id WHERE s.appointment_id=$1", p4Later) == $"rescheduled:{Cpf(2)}:pending", "O horário liberado deveria virar vaga oferecida ao próximo.");
                // Ofertas da vaga 1 para o OBS: ordem cronológica, nome e situação, sem CPF.
                using (var slotOffers = await obs.GetAsync($"/api/obs/units/{u2}/lightning-slots/{slot1}/offers"))
                {
                    Expect(slotOffers, 200); var text = await slotOffers.Content.ReadAsStringAsync(); var rows = JsonDocument.Parse(text).RootElement.EnumerateArray().ToArray();
                    Require(rows.Length == 2 && rows[0].GetProperty("patientName").GetString() == "Paciente 2" && rows[0].GetProperty("status").GetString() == "declined" && rows[1].GetProperty("patientName").GetString() == "Paciente 4" && rows[1].GetProperty("status").GetString() == "accepted", "Ofertas da vaga fora de ordem ou com situação errada.");
                    Require(rows[0].GetProperty("expiresAt").GetDateTimeOffset() < rows[1].GetProperty("expiresAt").GetDateTimeOffset() && !text.Contains(Cpf(2)) && !text.Contains("cpf") && rows[1].GetProperty("replacesStartsAt").GetDateTimeOffset() == t0.AddDays(20), "Ofertas expõem CPF, fora de ordem cronológica ou sem a consulta antecipada.");
                }
                using (var unknownSlot = await obs.GetAsync($"/api/obs/units/{u2}/lightning-slots/{Guid.NewGuid()}/offers")) Expect(unknownSlot, 404);
                using (var deniedFree = await free.GetAsync($"/api/obs/units/{u2}/lightning-slots/{slot1}/offers")) Expect(deniedFree, 403);
                using (var deniedManager = await municipal.GetAsync($"/api/obs/units/{u2}/lightning-slots/{slot1}/offers")) Expect(deniedManager, 403);
                using (var deniedPatient = await patients[1].GetAsync($"/api/obs/units/{u2}/lightning-slots/{slot1}/offers")) Expect(deniedPatient, 403);
                using (var anonymousOffers = await anonymous.GetAsync($"/api/obs/units/{u2}/lightning-slots/{slot1}/offers")) Expect(anonymousOffers, 401);
                // Segunda vaga: o P2 já tem oferta pendente; vai ao P1 (consulta mais distante que a do P5). O P1 cancela a consulta que seria antecipada e a vaga passa ao P5.
                clock.AdvanceTo(t0.AddMinutes(35));
                await Book(u2, Cpf(5), "Neurologia", "Prof Neuro Futuro 5", t0.AddDays(5));
                clock.AdvanceTo(t0.AddMinutes(40));
                await Status(u2, a2, "cancelled");
                using (var slotResponse = await obs.PostAsJsonAsync($"/api/obs/units/{u2}/lightning-slots", new { appointmentId = a2, confirmed = true })) { Expect(slotResponse, 201); slot2 = (await Read(slotResponse)).GetProperty("id").GetGuid(); }
                Require(await UnitScalar<string>(u2, "SELECT cpf FROM nora_unit.lightning_offers WHERE slot_id=$1 AND status='pending'", slot2) == Cpf(1), "A segunda vaga deveria ter sido ofertada ao P1.");
                clock.AdvanceTo(t0.AddMinutes(41));
                // Sair da fila não existe mais: a rota do paciente responde 410 (com sessão de paciente), 401 sem sessão e 403 para outro perfil.
                var cancelPath = $"/api/patient/units/{u2}/waiting-list/1/cancel";
                using (var anonymousCancel = await anonymous.PostAsJsonAsync(cancelPath, new { confirmed = true })) Expect(anonymousCancel, 401);
                using (var obsCancel = await obs.PostAsJsonAsync(cancelPath, new { confirmed = true })) Expect(obsCancel, 403);
                using (var removed = await patients[1].PostAsJsonAsync(cancelPath, new { confirmed = true })) await ExpectProblem(removed, 410, "manual-queue-removed");
                using (var cancelOwn = await patients[1].PostAsJsonAsync($"/api/patient/units/{u2}/appointments/{p1Later}/cancel", new { confirmed = true })) Expect(cancelOwn, 200);
                await Advance(u2);
                Require(await UnitScalar<string>(u2, "SELECT source FROM nora_unit.appointment_events WHERE appointment_id=$1 AND status='cancelled'", p1Later) == "patient", "Cancelamento do paciente não gravou origem 'patient'.");
                using (var slotOffers = await obs.GetAsync($"/api/obs/units/{u2}/lightning-slots/{slot2}/offers"))
                {
                    Expect(slotOffers, 200); var rows = (await Read(slotOffers)).EnumerateArray().ToArray();
                    Require(rows.Length == 2 && rows[0].GetProperty("patientName").GetString() == "Paciente 1" && rows[0].GetProperty("status").GetString() == "cancelled" && rows[1].GetProperty("patientName").GetString() == "Paciente 5" && rows[1].GetProperty("status").GetString() == "pending", "Ofertas da segunda vaga incorretas após o cancelamento da consulta a antecipar.");
                }
            });

            await Check("Paciente remove o vínculo: cancela consultas futuras e filas com origem 'patient' e peso normal; funciona com cadastro desativado; pode vincular de novo", async () =>
            {
                var accepted = await UnitScalar<Guid>(u2, "SELECT id FROM nora_unit.appointments WHERE cpf=$1 AND status='scheduled'", Cpf(4));
                var extra = await Book(u2, Cpf(4), "Clínica", "Prof Extra", t0.AddDays(2).AddHours(-2));
                await Status(u2, extra, "called");
                clock.AdvanceTo(t0.AddMinutes(45));
                var unlinkPath = $"/api/patient/units/{u2}/unlink";
                using (var anonymousUnlink = await anonymous.PostAsJsonAsync(unlinkPath, new { confirmed = true })) Expect(anonymousUnlink, 401);
                using (var obsUnlink = await obs.PostAsJsonAsync(unlinkPath, new { confirmed = true })) Expect(obsUnlink, 403);
                using (var withoutLink = await patients[3].PostAsJsonAsync(unlinkPath, new { confirmed = true })) Expect(withoutLink, 403);
                using (var unconfirmed = await patients[4].PostAsJsonAsync(unlinkPath, new { confirmed = false })) Expect(unconfirmed, 400);
                using (var missing = await patients[4].PostAsJsonAsync(unlinkPath, new { })) Expect(missing, 400);
                using (var done = await patients[4].PostAsJsonAsync(unlinkPath, new { confirmed = true }))
                { Expect(done, 200); var body = await Read(done); Require(body.GetProperty("cancelledAppointments").GetInt32() == 2 && body.GetProperty("cancelledEntries").GetInt32() == 0, "Contagens da remoção do vínculo incorretas (sem fila manual, nenhuma entrada)."); }
                using (var again = await patients[4].PostAsJsonAsync(unlinkPath, new { confirmed = true })) Expect(again, 403);
                using (var units = await patients[4].GetAsync("/api/patient/units")) { Expect(units, 200); Require(!(await Read(units)).GetProperty("units").EnumerateArray().Any(u => u.GetProperty("id").GetGuid() == u2), "UBS removida ainda aparece para o paciente."); }
                using (var forbidden = await patients[4].GetAsync($"/api/patient/units/{u2}/lightning-offers")) Expect(forbidden, 403);
                Require(await Scalar<long>("SELECT count(*) FROM nora.patient_units WHERE patient_id=$1 AND unit_id=$2", patientIds[4], u2) == 0, "Linha de vínculo não foi removida.");
                Require(await Scalar<long>("SELECT count(*) FROM nora.patient_unit_link_events WHERE patient_id=$1 AND unit_id=$2 AND action='linked'", patientIds[4], u2) == 1 && await Scalar<long>("SELECT count(*) FROM nora.patient_unit_link_events WHERE patient_id=$1 AND unit_id=$2 AND action='unlinked'", patientIds[4], u2) == 1, "Eventos de vínculo (linked/unlinked) incorretos.");
                Require(await UnitScalar<long>(u2, "SELECT count(*) FROM nora_unit.appointment_events WHERE status='cancelled' AND source='patient' AND actor_id=$1 AND appointment_id=ANY($2)", patientIds[4], new[] { accepted, extra }) == 2, "Cancelamentos do vínculo sem origem 'patient'.");
                Require(await UnitScalar<long>(u2, "SELECT count(*) FROM nora_unit.lightning_offers WHERE cpf=$1 AND status='pending'", Cpf(4)) == 0, "Ofertas pendentes sobreviveram à remoção do vínculo.");
                var weight = await Attendance(u2, 4);
                Require(weight.GetProperty("cancelledByPatient").GetInt64() == 2 && Math.Abs(weight.GetProperty("cancellationWeight").GetDouble() - 0.3) < 1e-9 && weight.GetProperty("points").GetInt32() == 690 && weight.GetProperty("score").GetDouble() == 0.69, "Cancelamentos da remoção do vínculo devem pesar pela tabela normal: antecedência de ~70 h = 0 e de ~45 h = −10 → 690 (" + weight + ").");
                using (var relink = await patients[4].PostAsJsonAsync("/api/patient/units/link", new { unitIds = new[] { u2 } })) { Expect(relink, 200); Require((await Read(relink)).GetProperty("linkedUnits").GetArrayLength() == 1, "Não foi possível vincular de novo."); }
                Require(await Scalar<long>("SELECT count(*) FROM nora.patient_unit_link_events WHERE patient_id=$1 AND unit_id=$2 AND action='linked'", patientIds[4], u2) == 2, "Novo vínculo não registrou evento.");
                // Cadastro desativado na UBS não impede a saída.
                using (var deactivate = await obs.PatchAsJsonAsync($"/api/obs/units/{u2}/registry/active", new { cpf = Cpf(6), isActive = false, confirmed = true })) Expect(deactivate, 204);
                using (var inactiveUnlink = await patients[6].PostAsJsonAsync(unlinkPath, new { confirmed = true })) { Expect(inactiveUnlink, 200); var body = await Read(inactiveUnlink); Require(body.GetProperty("cancelledAppointments").GetInt32() == 0 && body.GetProperty("cancelledEntries").GetInt32() == 0, "Contagens com cadastro desativado incorretas."); }
                Require(await Scalar<long>("SELECT count(*) FROM nora.patient_units WHERE patient_id=$1 AND unit_id=$2", patientIds[6], u2) == 0, "Cadastro desativado impediu remover o vínculo.");
            });

            await Check("Indicadores da UBS 2: vagas da janela (as automáticas incluídas) e espera da fila manual vazia", async () =>
            {
                var window = $"from={Uri.EscapeDataString(t0.ToString("O"))}&to={Uri.EscapeDataString(t0.AddDays(1).ToString("O"))}";
                using var response = await obs.GetAsync($"/api/obs/units/{u2}/metrics?{window}"); Expect(response, 200);
                var body = await Read(response); var waiting = body.GetProperty("waiting"); var lightning = body.GetProperty("lightning");
                // Sem fila manual: não há entrada, espera nem saída por oferta aceita.
                Require(waiting.GetProperty("acceptedEntries").GetInt64() == 0 && waiting.GetProperty("averageWaitMinutes").ValueKind == JsonValueKind.Null && waiting.GetProperty("activeEntries").GetInt64() == 0 && waiting.GetProperty("waitingPatients").GetInt64() == 0 && waiting.GetProperty("longestActiveWaitMinutes").ValueKind == JsonValueKind.Null, "Sem fila manual a espera deveria vir vazia.");
                // Vagas criadas na janela: a1 e a2 (equipe), a cascata do P4, a consulta cancelada do P1 e as duas consultas que a remoção do vínculo do P4 cancelou.
                Require(lightning.GetProperty("registeredSlots").GetInt64() == 6 && lightning.GetProperty("acceptedOffers").GetInt64() == 1 && lightning.GetProperty("declinedOffers").GetInt64() == 1 && lightning.GetProperty("expiredOffers").GetInt64() == 1, "Contagens de vagas e ofertas incorretas (a oferta da cascata ao P2, feita em +33 min, venceu no passe da remoção do vínculo em +45 min): " + lightning);
                var later = $"from={Uri.EscapeDataString(t0.AddDays(1).ToString("O"))}&to={Uri.EscapeDataString(t0.AddDays(2).ToString("O"))}";
                using var empty = await obs.GetAsync($"/api/obs/units/{u2}/metrics?{later}"); Expect(empty, 200);
                Require((await Read(empty)).GetProperty("lightning").GetProperty("registeredSlots").GetInt64() == 0, "Vagas fora da janela entraram na contagem.");
            });

            // ---- UBS 1: consultas, score e comparecimento ----
            var weights = new (string Lead, TimeSpan Span, double Weight)[]
            {
                ("96 h", TimeSpan.FromHours(96), 0.0), ("72 h", TimeSpan.FromHours(72), 0.02), ("48 h", TimeSpan.FromHours(48), 0.1), ("24 h", TimeSpan.FromHours(24), 0.2), ("12 h", TimeSpan.FromHours(12), 0.4),
                ("6 h", TimeSpan.FromHours(6), 0.6), ("2 h", TimeSpan.FromHours(2), 0.8), ("1 h", TimeSpan.FromHours(1), 0.9), ("30 min", TimeSpan.FromMinutes(30), 1.0)
            };
            await Check("Score: só cancelamentos do paciente pesam, por faixa de antecedência; equipe, agendada e chamada não entram", async () =>
            {
                // (paciente, especialidade, antecedência, cancelado por quem). A i-ésima é cancelada em T0 + 1 h + 20 min × i, dentro da vida da sessão (8 h).
                var plan = new List<(int Patient, string Specialty, TimeSpan Lead, bool ByPatient)>();
                foreach (var w in weights) plan.Add((3, "Ortopedia", w.Span, true));
                plan.Add((1, "Cardiologia", TimeSpan.FromHours(24), true)); plan.Add((1, "Cardiologia", TimeSpan.FromMinutes(30), false));
                plan.Add((2, "Dermatologia", TimeSpan.FromHours(2), true)); plan.Add((2, "Dermatologia", TimeSpan.FromMinutes(30), true));
                DateTimeOffset At(int days, int hour) => new DateTimeOffset(t0.Year, t0.Month, t0.Day, hour, 0, 0, TimeSpan.Zero).AddDays(days);
                var c1 = await Book(u1, Cpf(1), "Cardiologia", "Prof C1", At(2, 1).AddMinutes(30)); var c2 = await Book(u1, Cpf(1), "Cardiologia", "Prof C2", At(3, 12));
                var c3 = await Book(u1, Cpf(1), "cardiologia", "Prof C3", At(4, 12)); var c4 = await Book(u1, Cpf(1), "Cardiologia", "Prof C4", At(5, 12));
                var c7 = await Book(u1, Cpf(1), "Cardiologia", "Prof C7", At(6, 12)); var c8 = await Book(u1, Cpf(1), "Cardiologia", "Prof C8", At(7, 12));
                var d1 = await Book(u1, Cpf(2), "Dermatologia", "Prof D1", At(8, 12)); var o1 = await Book(u1, Cpf(3), "Ortopedia", "Prof O1", At(8, 15)); var o2 = await Book(u1, Cpf(3), "Ortopedia", "Prof O2", At(9, 15));
                var planned = new List<Guid>();
                for (var i = 0; i < plan.Count; i++) planned.Add(await Book(u1, Cpf(plan[i].Patient), plan[i].Specialty, $"Prof P{i}", t0.AddHours(1).AddMinutes(20 * i) + plan[i].Lead, 5));
                foreach (var id in new[] { c1, c2, c3, d1, o1, o2 }) { await Status(u1, id, "called"); await Status(u1, id, "completed"); }
                await Status(u1, c4, "missed"); await Status(u1, c8, "called");
                // c7 permanece scheduled. Cancelamentos na antecedência exata, avançando o relógio.
                for (var i = 0; i < plan.Count; i++)
                {
                    clock.AdvanceTo(t0.AddHours(1).AddMinutes(20 * i));
                    if (plan[i].ByPatient)
                    { using var cancel = await patients[plan[i].Patient].PostAsJsonAsync($"/api/patient/units/{u1}/appointments/{planned[i]}/cancel", new { confirmed = true }); Expect(cancel, 200); }
                    else await Status(u1, planned[i], "cancelled");
                }
                Require(await UnitScalar<long>(u1, "SELECT count(*) FROM nora_unit.appointment_events WHERE status='cancelled' AND source='patient'") == 12 && await UnitScalar<long>(u1, "SELECT count(*) FROM nora_unit.appointment_events WHERE status='cancelled' AND source='staff'") == 1, "Origem dos cancelamentos (paciente x equipe) incorreta.");
                Require(await UnitScalar<long>(u2, "SELECT count(*) FROM nora_unit.appointment_events WHERE status='scheduled' AND source='patient'") == 1, "A consulta criada pelo aceite da vaga deveria ter origem 'patient'.");
                // Consultas fora da janela de métricas: uma antiga demais para os 12 meses e uma recente demais para a janela (T0 em diante).
                await InsertAppointment(u1, Cpf(9), "Clínica", t0.AddDays(-400), "missed"); await InsertAppointment(u1, Cpf(9), "Clínica", t0.AddDays(-100), "missed");
            });

            await Check("Score por paciente no cadastro do OBS e no app (12 meses); a Secretaria não recebe o campo", async () =>
            {
                var p3 = await Attendance(u1, 3);
                Require(p3.GetProperty("completed").GetInt64() == 2 && p3.GetProperty("missed").GetInt64() == 0 && p3.GetProperty("cancelledByPatient").GetInt64() == 9 && Math.Abs(p3.GetProperty("cancellationWeight").GetDouble() - 4.02) < 1e-9 && p3.GetProperty("points").GetInt32() == 680 && p3.GetProperty("score").GetDouble() == 0.68 && p3.GetProperty("band").GetString() == "good", "Score do P3 incorreto (9 cancelamentos: 0+0+0−10−20−20−30−30−40 = −150 a partir de 700, depois +60 e +70 → 680): " + p3);
                var p1 = await Attendance(u1, 1);
                Require(p1.GetProperty("completed").GetInt64() == 3 && p1.GetProperty("missed").GetInt64() == 1 && p1.GetProperty("cancelledByPatient").GetInt64() == 1 && Math.Abs(p1.GetProperty("cancellationWeight").GetDouble() - 0.2) < 1e-9 && p1.GetProperty("points").GetInt32() == 840 && p1.GetProperty("score").GetDouble() == 0.84, "Score do P1 incorreto (cancelamento com 24 h = −10, a da equipe não pesa; +60 +70 +80 de sequência e a falta −60 → 840; agendada e chamada não entram): " + p1);
                var p2 = await Attendance(u1, 2);
                Require(p2.GetProperty("completed").GetInt64() == 1 && p2.GetProperty("cancelledByPatient").GetInt64() == 2 && Math.Abs(p2.GetProperty("cancellationWeight").GetDouble() - 1.8) < 1e-9 && p2.GetProperty("points").GetInt32() == 690, "Score do P2 incorreto (−30 com 2 h, −40 com 30 min, +60 → 690): " + p2);
                var p7 = await Attendance(u1, 7);
                Require(p7.GetProperty("completed").GetInt64() == 0 && p7.GetProperty("cancellationWeight").GetDouble() == 0 && p7.GetProperty("score").ValueKind == JsonValueKind.Null && p7.GetProperty("points").GetInt32() == 700 && !p7.GetProperty("hasHistory").GetBoolean(), "Paciente sem consulta deve ter score nulo e 700 pontos.");
                var p9 = await Attendance(u1, 9);
                Require(p9.GetProperty("missed").GetInt64() == 1 && p9.GetProperty("points").GetInt32() == 670, "A janela de 12 meses deve incluir a falta de 100 dias (pela metade: −30) e excluir a de 400 dias: " + p9);
                using (var managerRegistry = await municipal.GetAsync($"/api/municipal/units/{u1}/registry")) { Expect(managerRegistry, 200); var text = await managerRegistry.Content.ReadAsStringAsync(); Require(!text.Contains("attendance"), "A rota municipal de cadastro não deve receber o score."); }
                var own = $"/api/patient/units/{u1}/attendance";
                using (var ownResponse = await patients[3].GetAsync(own))
                {
                    Expect(ownResponse, 200); var body = await Read(ownResponse);
                    Require(body.GetProperty("completed").GetInt64() == 2 && body.GetProperty("cancelledByPatient").GetInt64() == 9 && Math.Abs(body.GetProperty("score").GetDouble() - p3.GetProperty("score").GetDouble()) < 1e-9 && body.GetProperty("windowMonths").GetInt32() == 12, "Score do próprio paciente incorreto.");
                }
                using (var otherOwn = await patients[1].GetAsync(own)) { Expect(otherOwn, 200); Require((await Read(otherOwn)).GetProperty("completed").GetInt64() == 3, "O paciente deveria ver apenas o próprio score."); }
                using (var withoutLink = await patients[4].GetAsync(own)) Expect(withoutLink, 403);
                using (var obsOwn = await obs.GetAsync(own)) Expect(obsOwn, 403);
                using (var anonymousOwn = await anonymous.GetAsync(own)) Expect(anonymousOwn, 401);
            });

            var fromQ = Uri.EscapeDataString(t0.ToString("O")); var toQ = Uri.EscapeDataString(t0.AddDays(300).ToString("O"));
            await Check("Indicadores da UBS 1: contagens, peso, score, especialidades agrupadas e série diária no fuso configurado", async () =>
            {
                using var response = await obs.GetAsync($"/api/obs/units/{u1}/metrics?from={fromQ}&to={toQ}"); Expect(response, 200);
                var body = await Read(response); var a = body.GetProperty("appointments");
                Require(a.GetProperty("scheduled").GetInt64() == 1 && a.GetProperty("called").GetInt64() == 1 && a.GetProperty("completed").GetInt64() == 6 && a.GetProperty("missed").GetInt64() == 1 && a.GetProperty("cancelled").GetInt64() == 13 && a.GetProperty("cancelledByPatient").GetInt64() == 12, "Contagens de consultas incorretas.");
                Require(Math.Abs(a.GetProperty("cancellationWeight").GetDouble() - 6.02) < 1e-9 && Math.Abs(a.GetProperty("attendanceScore").GetDouble() - Math.Round(6 / 13.02, 4)) < 1e-9, "Peso ou score da UBS incorretos.");
                var specialties = body.GetProperty("bySpecialty").EnumerateArray().ToDictionary(s => s.GetProperty("specialty").GetString()!, s => s);
                Require(specialties.Keys.Count(k => k.StartsWith("cardio", StringComparison.OrdinalIgnoreCase)) == 1 && specialties.ContainsKey("Cardiologia"), "Cardiologia/cardiologia deveria ser um grupo com a grafia mais frequente.");
                var cardio = specialties["Cardiologia"];
                Require(cardio.GetProperty("completed").GetInt64() == 3 && cardio.GetProperty("missed").GetInt64() == 1 && cardio.GetProperty("cancelled").GetInt64() == 2 && Math.Abs(cardio.GetProperty("cancellationWeight").GetDouble() - 0.2) < 1e-9 && Math.Abs(cardio.GetProperty("attendanceScore").GetDouble() - Math.Round(3 / 4.2, 4)) < 1e-9 && cardio.GetProperty("activeEntries").GetInt64() == 0, "Indicadores de Cardiologia incorretos.");
                Require(Math.Abs(specialties["Ortopedia"].GetProperty("cancellationWeight").GetDouble() - 4.02) < 1e-9 && specialties["Dermatologia"].GetProperty("completed").GetInt64() == 1, "Indicadores de Ortopedia/Dermatologia incorretos.");
                var daily = body.GetProperty("daily").EnumerateArray().ToDictionary(d => d.GetProperty("date").GetString()!, d => d);
                Require(daily["2026-10-08"].GetProperty("completed").GetInt64() == 1 && (!daily.TryGetValue("2026-10-09", out var ninth) || ninth.GetProperty("completed").GetInt64() == 0), "Consulta às 01h30 UTC deveria cair no dia anterior no fuso America/Sao_Paulo.");
                Require(daily["2026-10-15"].GetProperty("completed").GetInt64() == 2 && daily["2026-10-12"].GetProperty("missed").GetInt64() == 1 && daily["2026-10-13"].GetProperty("scheduled").GetInt64() == 1, "Série diária incorreta.");
                Require(body.GetProperty("daily").EnumerateArray().Sum(d => d.GetProperty("completed").GetInt64()) == 6 && body.GetProperty("daily").EnumerateArray().Sum(d => d.GetProperty("cancelled").GetInt64()) == 13 && daily.Keys.All(k => k.Length == 10 && k[4] == '-'), "A série diária não fecha com os totais ou usa formato incorreto.");
                var w = body.GetProperty("waiting");
                Require(w.GetProperty("activeEntries").GetInt64() == 0 && w.GetProperty("averageWaitMinutes").ValueKind == JsonValueKind.Null && w.GetProperty("acceptedEntries").GetInt64() == 0, "Fila da UBS 1 incorreta (sem fila manual).");
                Require(body.GetProperty("lightning").GetProperty("registeredSlots").GetInt64() == 0, "UBS 1 não tem vagas.");
                // Janela: pela data marcada; a falta de 100 dias antes não entra e o limite superior é exclusivo.
                using var narrow = await obs.GetAsync($"/api/obs/units/{u1}/metrics?from={fromQ}&to={Uri.EscapeDataString(t0.AddDays(2).ToString("O"))}"); Expect(narrow, 200);
                var narrowBody = (await Read(narrow)).GetProperty("appointments");
                Require(narrowBody.GetProperty("completed").GetInt64() == 1 && narrowBody.GetProperty("cancelled").GetInt64() == 10 && narrowBody.GetProperty("cancelledByPatient").GetInt64() == 9 && Math.Abs(narrowBody.GetProperty("cancellationWeight").GetDouble() - 5.9) < 1e-9 && Math.Abs(narrowBody.GetProperty("attendanceScore").GetDouble() - Math.Round(1 / 6.9, 4)) < 1e-9, "Janela estreita (só consultas marcadas até T0 + 2 dias) incorreta.");
                // Validações e autorização.
                foreach (var invalid in new[] { $"from={toQ}&to={fromQ}", $"from={fromQ}&to={fromQ}", $"from={fromQ}&to={Uri.EscapeDataString(t0.AddDays(7306).ToString("O"))}", $"from={fromQ}", "" })
                { using var rejected = await obs.GetAsync($"/api/obs/units/{u1}/metrics?{invalid}"); Expect(rejected, 400); }
                // Desde 2026-11-26 o limite é de 20 anos (7.305 dias): um ano e um dia a mais passam, 20 anos e 1 dia não (ver MetricsWindowChecks).
                foreach (var accepted in new[] { 366, 367, 7305 })
                { using var maximum = await obs.GetAsync($"/api/obs/units/{u1}/metrics?from={fromQ}&to={Uri.EscapeDataString(t0.AddDays(accepted).ToString("O"))}"); Expect(maximum, 200); }
                using (var deniedFree = await free.GetAsync($"/api/obs/units/{u1}/metrics?from={fromQ}&to={toQ}")) Expect(deniedFree, 403);
                using (var deniedManager = await municipal.GetAsync($"/api/obs/units/{u1}/metrics?from={fromQ}&to={toQ}")) Expect(deniedManager, 403);
                using (var deniedPatient = await patients[1].GetAsync($"/api/obs/units/{u1}/metrics?from={fromQ}&to={toQ}")) Expect(deniedPatient, 403);
                using (var anonymousMetrics = await anonymous.GetAsync($"/api/obs/units/{u1}/metrics?from={fromQ}&to={toQ}")) Expect(anonymousMetrics, 401);
            });

            await Check("Indicadores da rede: totais, por UBS, por região, por especialidade, filtros e CPF distinto na espera", async () =>
            {
                using var response = await municipal.GetAsync($"/api/municipal/metrics?from={fromQ}&to={toQ}"); Expect(response, 200);
                var text = await response.Content.ReadAsStringAsync(); var body = JsonDocument.Parse(text).RootElement; var totals = body.GetProperty("totals");
                Require(!text.Contains(Cpf(1)) && !text.Contains("example.test") && !text.Contains("Paciente 1"), "Indicadores da rede expõem dado pessoal.");
                Require(totals.GetProperty("appointments").GetProperty("completed").GetInt64() == 6 && totals.GetProperty("appointments").GetProperty("cancelled").GetInt64() == 19 && totals.GetProperty("appointments").GetProperty("cancelledByPatient").GetInt64() == 15, "Totais de consultas da rede incorretos (UBS 1: 13 e 12; UBS 2: a1, a2, a antecipada do P4, a do P1 e as duas da remoção do vínculo).");
                var weight = 6.02 + 0.3; var score = Math.Round(6 / (6 + 1 + weight), 4);
                Require(Math.Abs(totals.GetProperty("appointments").GetProperty("cancellationWeight").GetDouble() - weight) < 1e-9 && Math.Abs(totals.GetProperty("appointments").GetProperty("attendanceScore").GetDouble() - score) < 1e-9, "Peso ou score da rede incorretos.");
                var waiting = totals.GetProperty("waiting");
                Require(waiting.GetProperty("activeEntries").GetInt64() == 0 && waiting.GetProperty("waitingPatients").GetInt64() == 0 && waiting.GetProperty("acceptedEntries").GetInt64() == 0 && waiting.GetProperty("averageWaitMinutes").ValueKind == JsonValueKind.Null, "Espera da rede: sem fila manual.");
                Require(totals.GetProperty("lightning").GetProperty("registeredSlots").GetInt64() == 6 && totals.GetProperty("lightning").GetProperty("acceptedOffers").GetInt64() == 1, "Vagas da rede incorretas.");
                var byUnit = body.GetProperty("byUnit").EnumerateArray().ToDictionary(u => u.GetProperty("unitId").GetGuid(), u => u);
                Require(byUnit.ContainsKey(u1) && byUnit.ContainsKey(u2) && !byUnit.ContainsKey(pendingUnit) && byUnit[u1].GetProperty("name").GetString() == "UBS Um" && byUnit[u2].GetProperty("region").GetString() == "Norte" && byUnit[u1].GetProperty("waiting").GetProperty("waitingPatients").GetInt64() == 0, "Resumo por UBS incorreto.");
                var regions = body.GetProperty("byRegion").EnumerateArray().ToDictionary(r => r.GetProperty("region").GetString()!, r => r);
                Require(regions.ContainsKey("Centro") && regions.ContainsKey("Norte") && regions["Norte"].GetProperty("appointments").GetProperty("cancelledByPatient").GetInt64() == 3 && regions["Centro"].GetProperty("appointments").GetProperty("completed").GetInt64() == 6, "Resumo por região incorreto.");
                var specialties = body.GetProperty("bySpecialty").EnumerateArray().Select(s => s.GetProperty("specialty").GetString()!).ToList();
                Require(specialties.Contains("Neurologia") || specialties.Any(s => s.Equals("neurologia", StringComparison.OrdinalIgnoreCase)), "Especialidade de outra UBS ausente do agrupamento da rede.");
                Require(body.GetProperty("unitsWithoutDatabase").GetInt64() == 1, "UBS pending deveria ser contada em unitsWithoutDatabase.");
                // Filtros: UBS, região (sem diferenciar maiúsculas) e especialidade.
                using (var one = await municipal.GetAsync($"/api/municipal/metrics?from={fromQ}&to={toQ}&unitId={u2}")) { Expect(one, 200); var oneBody = await Read(one); Require(oneBody.GetProperty("byUnit").GetArrayLength() == 1 && oneBody.GetProperty("totals").GetProperty("appointments").GetProperty("completed").GetInt64() == 0 && oneBody.GetProperty("totals").GetProperty("appointments").GetProperty("cancelledByPatient").GetInt64() == 3, "Filtro por UBS incorreto."); }
                using (var region = await municipal.GetAsync($"/api/municipal/metrics?from={fromQ}&to={toQ}&region=NORTE")) { Expect(region, 200); var regionBody = await Read(region); Require(regionBody.GetProperty("byUnit").GetArrayLength() == 1 && regionBody.GetProperty("byUnit")[0].GetProperty("unitId").GetGuid() == u2 && regionBody.GetProperty("byRegion").GetArrayLength() == 1, "Filtro por região deveria ignorar maiúsculas."); }
                using (var specialty = await municipal.GetAsync($"/api/municipal/metrics?from={fromQ}&to={toQ}&specialty=CARDIOLOGIA"))
                {
                    Expect(specialty, 200); var specialtyBody = await Read(specialty); var appointments = specialtyBody.GetProperty("totals").GetProperty("appointments");
                    Require(appointments.GetProperty("completed").GetInt64() == 3 && appointments.GetProperty("cancelled").GetInt64() == 2 && specialtyBody.GetProperty("bySpecialty").GetArrayLength() == 1 && specialtyBody.GetProperty("totals").GetProperty("lightning").GetProperty("registeredSlots").GetInt64() == 0, "Filtro por especialidade incorreto.");
                }
                using (var unknown = await municipal.GetAsync($"/api/municipal/metrics?from={fromQ}&to={toQ}&unitId={Guid.NewGuid()}")) Expect(unknown, 404);
                foreach (var invalid in new[] { $"from={toQ}&to={fromQ}", $"from={fromQ}&to={Uri.EscapeDataString(t0.AddDays(7306).ToString("O"))}", "" })
                { using var rejected = await municipal.GetAsync($"/api/municipal/metrics?{invalid}"); Expect(rejected, 400); }
                using (var longWindow = await municipal.GetAsync($"/api/municipal/metrics?from={fromQ}&to={Uri.EscapeDataString(t0.AddDays(7305).ToString("O"))}")) Expect(longWindow, 200);
                using (var obsDenied = await obs.GetAsync($"/api/municipal/metrics?from={fromQ}&to={toQ}")) Expect(obsDenied, 403);
                using (var patientDenied = await patients[1].GetAsync($"/api/municipal/metrics?from={fromQ}&to={toQ}")) Expect(patientDenied, 403);
                using (var anonymousDenied = await anonymous.GetAsync($"/api/municipal/metrics?from={fromQ}&to={toQ}")) Expect(anonymousDenied, 401);
            });

            await Check("Resumo: pacientes ativos distintos, desativados, pessoas aguardando, entradas e UBS sem banco", async () =>
            {
                using var response = await municipal.GetAsync("/api/municipal/summary"); Expect(response, 200);
                var body = await Read(response);
                // Ativos: P1, P2, P3, P5, P7, P9 (UBS 1) e P1, P2, P4, P5, S1 (UBS 2). Só desativados: P8 (UBS 1) e P6 (UBS 2).
                Require(body.GetProperty("patients").GetInt64() == 8 && body.GetProperty("inactivePatients").GetInt64() == 2, $"Pacientes ativos/desativados incorretos ({body.GetProperty("patients").GetInt64()}/{body.GetProperty("inactivePatients").GetInt64()}).");
                Require(body.GetProperty("waitingPatients").GetInt64() == 0 && body.GetProperty("waitingEntries").GetInt64() == 0, "Sem fila manual, ninguém aguarda.");
                Require(body.GetProperty("unitsWithoutDatabase").GetInt64() == 1, "unitsWithoutDatabase incorreto.");
                using var one = await municipal.GetAsync($"/api/municipal/summary?unitId={u2}"); Expect(one, 200);
                var oneBody = await Read(one);
                Require(oneBody.GetProperty("patients").GetInt64() == 5 && oneBody.GetProperty("inactivePatients").GetInt64() == 1 && oneBody.GetProperty("waitingEntries").GetInt64() == 0 && oneBody.GetProperty("unitsWithoutDatabase").GetInt64() == 0, "Resumo filtrado por UBS incorreto.");
                using var pendingOnly = await municipal.GetAsync($"/api/municipal/summary?unitId={pendingUnit}"); Expect(pendingOnly, 200);
                var pendingBody = await Read(pendingOnly);
                Require(pendingBody.GetProperty("patients").GetInt64() == 0 && pendingBody.GetProperty("unitsWithoutDatabase").GetInt64() == 1, "Resumo de UBS pending deveria vir vazio e informar a pendência.");
            });

            await Check("Histórico de ações: união ordenada, filtros, paginação por instante, sem CPF e e-mail, só Secretaria", async () =>
            {
                using (var read = await municipal.GetAsync($"/api/municipal/units/{u1}/registry")) Expect(read, 200);
                using (var scheduleRead = await municipal.GetAsync($"/api/municipal/units/{u1}/appointments?from={fromQ}&to={Uri.EscapeDataString(t0.AddDays(20).ToString("O"))}")) Expect(scheduleRead, 200);
                using var response = await municipal.GetAsync("/api/municipal/history?limit=200"); Expect(response, 200);
                var text = await response.Content.ReadAsStringAsync(); var rows = JsonDocument.Parse(text).RootElement.EnumerateArray().ToArray();
                var kinds = rows.Select(r => r.GetProperty("kind").GetString()!).ToHashSet();
                foreach (var kind in new[] { "staff-granted", "registry-read", "appointment-read", "staff-created", "staff-deactivated", "staff-reactivated", "staff-password-reset", "unit-database-created" })
                    Require(kinds.Contains(kind), $"Histórico sem eventos '{kind}'.");
                Require(!text.Contains("example.test") && !text.Contains("cpf") && !text.Contains("email") && !text.Contains(Cpf(60)), "Histórico expõe CPF ou e-mail.");
                for (var i = 1; i < rows.Length; i++) Require(rows[i - 1].GetProperty("occurredAt").GetDateTimeOffset() >= rows[i].GetProperty("occurredAt").GetDateTimeOffset(), "Histórico fora de ordem (mais recentes primeiro).");
                var created = rows.First(r => r.GetProperty("kind").GetString() == "staff-created");
                Require(created.GetProperty("actorName").GetString() == "Gestora de teste" && created.GetProperty("accountName").GetString() == "Atendente Nova" && created.GetProperty("unitId").ValueKind == JsonValueKind.Null, "Evento de conta sem autor/conta.");
                var read1 = rows.First(r => r.GetProperty("kind").GetString() == "registry-read");
                Require(read1.GetProperty("unitName").GetString() == "UBS Um" && read1.GetProperty("actorName").GetString() == "Gestora de teste" && read1.GetProperty("unitId").GetGuid() == u1, "Evento de leitura sem unidade/autor.");
                var database = rows.First(r => r.GetProperty("kind").GetString() == "unit-database-created" && r.GetProperty("unitId").GetGuid() == u1);
                Require(database.GetProperty("actorName").GetString() == "Gestora de teste", "Evento de banco criado sem autor.");
                // Padrão 100, limite 200, paginação por instante.
                using (var page = await municipal.GetAsync("/api/municipal/history?limit=3")) { Expect(page, 200); Require((await Read(page)).GetArrayLength() == 3, "Limite não respeitado."); }
                var firstPage = JsonDocument.Parse(await (await municipal.GetAsync("/api/municipal/history?limit=3")).Content.ReadAsStringAsync()).RootElement.EnumerateArray().ToArray();
                var cursor = firstPage[^1].GetProperty("occurredAt").GetDateTimeOffset();
                using (var next = await municipal.GetAsync($"/api/municipal/history?limit=200&before={Uri.EscapeDataString(cursor.ToString("O"))}"))
                { Expect(next, 200); var older = (await Read(next)).EnumerateArray().ToArray(); Require(older.Length > 0 && older.All(r => r.GetProperty("occurredAt").GetDateTimeOffset() < cursor), "Cursor 'before' deve devolver só eventos anteriores."); }
                using (var byUnit = await municipal.GetAsync($"/api/municipal/history?unitId={u1}&limit=200")) { Expect(byUnit, 200); var filtered = (await Read(byUnit)).EnumerateArray().ToArray(); Require(filtered.Length > 0 && filtered.All(r => r.GetProperty("unitId").GetGuid() == u1), "Filtro por UBS incorreto."); }
                var firstTime = rows[^1].GetProperty("occurredAt").GetDateTimeOffset();
                using (var byRange = await municipal.GetAsync($"/api/municipal/history?from={Uri.EscapeDataString(firstTime.AddDays(-1).ToString("O"))}&to={Uri.EscapeDataString(firstTime.AddDays(-1).AddMinutes(1).ToString("O"))}")) { Expect(byRange, 200); Require((await Read(byRange)).GetArrayLength() == 0, "Filtro por período incorreto."); }
                foreach (var invalid in new[] { "limit=0", "limit=201", "limit=-1", $"from={Uri.EscapeDataString(t0.AddDays(2).ToString("O"))}&to={Uri.EscapeDataString(t0.ToString("O"))}" })
                { using var rejected = await municipal.GetAsync($"/api/municipal/history?{invalid}"); Expect(rejected, 400); }
                using (var defaultPage = await municipal.GetAsync("/api/municipal/history")) { Expect(defaultPage, 200); Require((await Read(defaultPage)).GetArrayLength() <= 100, "Padrão de 100 itens excedido."); }
                using (var obsDenied = await obs.GetAsync("/api/municipal/history")) Expect(obsDenied, 403);
                using (var patientDenied = await patients[1].GetAsync("/api/municipal/history")) Expect(patientDenied, 403);
                using (var anonymousDenied = await anonymous.GetAsync("/api/municipal/history")) Expect(anonymousDenied, 401);
            });

            await Check("UBS com banco pronto e fora do ar responde 503 (sem total parcial) e volta com nova tentativa idempotente", async () =>
            {
                var down = (await CreateUnit("UBS Queda", "Oeste")).GetProperty("id").GetGuid();
                var name = "nora_ubs_" + down.ToString("N");
                await using (var admin = await database.OpenAsync()) await database.ExecuteAsync(admin, $"DROP DATABASE \"{name}\" WITH (FORCE)", default);
                using (var summary = await municipal.GetAsync("/api/municipal/summary")) await ExpectProblem(summary, 503, "service-unavailable");
                using (var metrics = await municipal.GetAsync($"/api/municipal/metrics?from={fromQ}&to={toQ}")) await ExpectProblem(metrics, 503, "service-unavailable");
                using (var scoped = await municipal.GetAsync($"/api/municipal/summary?unitId={u1}")) Expect(scoped, 200);
                var restored = false;
                for (var attempt = 0; attempt < 3 && !restored; attempt++)
                { using var retry = await municipal.PostAsync($"/api/municipal/units/{down}/database", null); restored = retry.StatusCode == HttpStatusCode.OK; }
                Require(restored, "Nova tentativa não recriou o banco da UBS.");
                Require(await Scalar<long>("SELECT count(*) FROM nora.unit_database_events WHERE unit_id=$1", down) == 1, "Recriar o banco não deve repetir o evento de criação.");
                using (var summary = await municipal.GetAsync("/api/municipal/summary")) Expect(summary, 200);
                // Fecha a UBS que ficou pending: nova tentativa com a criação automática ativa.
                using (var final = await municipal.PostAsync($"/api/municipal/units/{pendingUnit}/database", null)) { Expect(final, 200); Require((await Read(final)).GetProperty("databaseState").GetString() == "ready", "UBS pending não ficou pronta na nova tentativa."); }
                using (var catalog = await anonymous.GetAsync("/api/registration/units")) { Expect(catalog, 200); Require((await Read(catalog)).GetProperty("units").EnumerateArray().Any(u => u.GetProperty("id").GetGuid() == pendingUnit), "UBS pronta deveria entrar no catálogo."); }
                using (var summary = await municipal.GetAsync("/api/municipal/summary")) { Expect(summary, 200); Require((await Read(summary)).GetProperty("unitsWithoutDatabase").GetInt64() == 0, "Resumo ainda conta UBS sem banco."); }
            });
        }
        catch (Exception error)
        {
            failures++;
            Console.WriteLine($"FAIL Setup plataforma: {error.GetType().Name}: {error.Message}");
        }
        finally
        {
            await app.StopAsync();
            await using var connection = await database.OpenAsync();
            await database.ExecuteAsync(connection, $"DROP SCHEMA IF EXISTS {schema} CASCADE", default);
        }
        Console.WriteLine($"{checks - failures}/{checks} verificações de plataforma passaram.");
        return failures;

        async Task Check(string name, Func<Task> action)
        {
            checks++;
            try { await action(); Console.WriteLine($"PASS [PostgreSQL] {name}"); }
            catch (Exception error) { failures++; Console.WriteLine($"FAIL [PostgreSQL] {name}: {error.Message}"); }
        }

        async Task InsertAppointment(Guid unit, string cpf, string specialty, DateTimeOffset starts, string status)
        {
            await using var connection = await app.Services.GetRequiredService<UnitDatabaseRouter>().OpenAsync(unit);
            await using var insert = new NpgsqlCommand("INSERT INTO nora_unit.appointments(id,cpf,specialty,professional,starts_at,ends_at,status,created_by,created_at) VALUES($1,$2,$3,'Prof histórico',$4,$5,$6,$7,$8)", connection);
            foreach (var value in new object[] { Guid.NewGuid(), cpf, specialty, starts, starts.AddMinutes(30), status, Guid.NewGuid(), starts.AddDays(-1) }) insert.Parameters.AddWithValue(value);
            await insert.ExecuteNonQueryAsync();
        }
    }

    private static WebApplication Create(string schema, string connection, CheckClock clock)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Nora.Api", "Nora.Api.csproj"))) directory = directory.Parent;
        return NoraApiHost.Create(new()
        {
            // Sem verificação periódica: a redistribuição acontece só pelas rotas, com o relógio de teste.
            Args = ["--urls", "http://127.0.0.1:0", "--ConnectionStrings:Nora", connection, "--Nora:Schema", schema, "--Nora:UnitLightningWorker:Enabled", "false", "--Nora:ObsDevices:RequireForAttendants", "false",
                "--Logging:LogLevel:Default", "Warning", "--Logging:LogLevel:Nora.Api.Services.UnitDatabaseProvisioner", "None",
                "--Nora:Registration:HashKey", "synthetic-platform-registration-key-32-bytes"],
            EnvironmentName = "Development",
            ApplicationName = typeof(NoraApiHost).Assembly.GetName().Name,
            ContentRootPath = Path.Combine(directory!.FullName, "Nora.Api")
        }, services =>
        {
            services.Replace(ServiceDescriptor.Singleton<TimeProvider>(clock));
            services.Replace(ServiceDescriptor.Singleton<IEmailSender>(new NoMail()));
        });
    }

    private static async Task<(int ExitCode, string Output)> RunCli(string dll, string connection, string schema, params string[] arguments)
    {
        var start = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        start.ArgumentList.Add(dll);
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        foreach (var argument in new[] { "--ConnectionStrings:Nora", connection, "--Nora:Schema", schema, "--Logging:LogLevel:Default", "Warning" }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
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
        Require(response.Content.Headers.ContentType?.MediaType == "application/problem+json", "Erro sem application/problem+json.");
        var body = await Read(response);
        Require(body.GetProperty("code").GetString() == code && body.GetProperty("type").GetString() == $"urn:nora:problem:{code}" && body.GetProperty("status").GetInt32() == status, $"Código de erro esperado '{code}', recebido '{body.GetProperty("code").GetString()}'.");
    }
    private static void Require(bool valid, string message) { if (!valid) throw new InvalidOperationException(message); }
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

    private sealed class CheckClock : TimeProvider
    {
        private long ticks = new DateTimeOffset(2026, 10, 7, 12, 0, 0, TimeSpan.Zero).UtcTicks;
        public override DateTimeOffset GetUtcNow() => new(Interlocked.Read(ref ticks), TimeSpan.Zero);
        public void AdvanceTo(DateTimeOffset moment)
        {
            if (moment.UtcTicks < Interlocked.Read(ref ticks)) throw new InvalidOperationException("O relógio de teste só avança.");
            Interlocked.Exchange(ref ticks, moment.UtcTicks);
        }
    }
}
