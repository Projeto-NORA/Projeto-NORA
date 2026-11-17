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
/// Comunicados (público, alcance, validade, leitura, incremental, texto puro, autorização) e Jornada (exames e vacinas por UBS).
/// Usa só NORA_TEST_CONNECTION (cluster descartável) com schema próprio e relógio de teste; os bancos de UBS ficam no cluster.
/// </summary>
internal static class CommunityChecks
{
    private const string Password = "Disposable-test-Account!29";

    public static async Task<int> RunAsync(string root)
    {
        var connectionString = Environment.GetEnvironmentVariable("NORA_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Console.WriteLine("SKIP comunicação e Jornada PostgreSQL: configure NORA_TEST_CONNECTION somente para uma base descartável.");
            return 0;
        }
        var schema = "nora_comm_" + Guid.NewGuid().ToString("N");
        var clock = new CheckClock();
        var checks = 0; var failures = 0;
        await using var app = Create(root, schema, connectionString, clock);
        var database = app.Services.GetRequiredService<DatabaseHelper>();
        var routers = app.Services.GetRequiredService<UnitDatabaseRouter>();
        var clients = new List<HttpClient>();
        try
        {
            await app.Services.GetRequiredService<MigrationDao>().MigrateAsync();
            var accounts = app.Services.GetRequiredService<AccountService>();
            foreach (var n in new[] { 90, 94, 95, 96 }) await accounts.ProvisionAsync("municipal", "manager", new($"Gestora {n}", Cpf(n), $"manager{n}@example.test", Password));
            var adminA = await accounts.ProvisionAsync("obs", "administrator", new("Administradora A", Cpf(91), "obs-a@example.test", Password));
            var attendantB = await accounts.ProvisionAsync("obs", "attendant", new("Atendente B", Cpf(92), "obs-b@example.test", Password));
            await accounts.ProvisionAsync("obs", "attendant", new("Atendente C sem UBS", Cpf(93), "obs-c@example.test", Password));
            foreach (var n in new[] { 1, 2, 3, 4, 5, 6, 7 }) await accounts.ProvisionAsync("patient", "patient", new($"Paciente {n}", Cpf(n), $"patient{n}@example.test", Password));
            await app.StartAsync();
            HttpClient NewClient() { var client = Client(app); clients.Add(client); return client; }
            var anonymous = NewClient();
            var m1 = NewClient(); var m2 = NewClient(); var m3 = NewClient(); var m4 = NewClient();
            var obsA = NewClient(); var obsB = NewClient(); var obsC = NewClient();
            await Login(m1, "municipal", "manager90@example.test"); await Login(m2, "municipal", "manager94@example.test");
            await Login(m3, "municipal", "manager95@example.test"); await Login(m4, "municipal", "manager96@example.test");
            await Login(obsA, "obs", "obs-a@example.test"); await Login(obsB, "obs", "obs-b@example.test"); await Login(obsC, "obs", "obs-c@example.test");
            var patients = new Dictionary<int, HttpClient>();
            foreach (var n in new[] { 1, 2, 3, 4, 5, 6, 7 }) { patients[n] = NewClient(); await Login(patients[n], "patient", Cpf(n)); }

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
            async Task<Guid> CreateUnit(string name, string region)
            {
                using var response = await m1.PostAsJsonAsync("/api/municipal/units", new { name, region, address = "Rua de teste, 10", latitude = -23.55, longitude = -46.63 });
                Expect(response, 201);
                var body = await Read(response);
                Require(body.GetProperty("databaseState").GetString() == "ready", "Banco da UBS não ficou pronto.");
                return body.GetProperty("id").GetGuid();
            }
            async Task Register(HttpClient obs, Guid unit, int n)
            {
                using var response = await obs.PostAsJsonAsync($"/api/obs/units/{unit}/registry", new { cpf = Cpf(n), fullName = $"Paciente {n}", email = $"patient{n}@example.test" });
                Expect(response, 201);
            }
            async Task Link(int n, params Guid[] units)
            {
                using var response = await patients[n].PostAsJsonAsync("/api/patient/units/link", new { unitIds = units });
                Expect(response, 200);
                Require((await Read(response)).GetProperty("linkedUnits").GetArrayLength() == units.Length, $"Paciente {n} não vinculou todas as UBS.");
            }
            async Task Grant(Guid account, Guid unit) { using var response = await m1.PutAsync($"/api/municipal/staff/{account}/units/{unit}", null); Expect(response, 204); }

            var u1 = await CreateUnit("UBS Centro", "Centro");
            var u2 = await CreateUnit("UBS Norte Um", "Norte");
            var u3 = await CreateUnit("UBS Norte Dois", "Norte");
            await Grant(adminA.Id, u1); await Grant(attendantB.Id, u2); await Grant(attendantB.Id, u3);
            foreach (var n in new[] { 1, 3, 6, 7 }) await Register(obsA, u1, n);
            await Register(obsB, u2, 2); foreach (var n in new[] { 5, 6 }) await Register(obsB, u3, n);
            await Link(1, u1); await Link(2, u2); await Link(3, u1); await Link(5, u3); await Link(6, u1, u3); await Link(7, u1);

            // ---- helpers de comunicado ----
            async Task<JsonElement> Publish(HttpClient who, string title, string audience, string scope, string[]? regions = null, Guid[]? unitIds = null, DateTimeOffset? expires = null, string? body = null, int status = 201)
            {
                using var response = await who.PostAsJsonAsync("/api/municipal/announcements", new { title, body = body ?? "Texto de " + title, audience, scope, regions, unitIds, expiresAt = expires });
                Expect(response, status);
                return status == 201 ? await Read(response) : default;
            }
            async Task<JsonElement> Page(HttpClient who, string route)
            {
                using var response = await who.GetAsync(route);
                Expect(response, 200);
                return await Read(response);
            }
            async Task<List<string>> Titles(HttpClient who, string route) => (await Page(who, route)).GetProperty("announcements").EnumerateArray().Select(a => a.GetProperty("title").GetString()!).ToList();
            async Task<List<string>> Own(HttpClient who, string route) => (await Titles(who, route)).Where(t => t.StartsWith('N')).ToList();
            void SameSet(IEnumerable<string> actual, string[] expected, string who) => Require(actual.Order().SequenceEqual(expected.Order()), $"{who}: esperado [{string.Join("; ", expected.Order())}], recebido [{string.Join("; ", actual.Order())}].");
            string ObsList = "/api/obs/announcements", PatientList = "/api/patient/announcements";

            // ================= Comunicados =================

            await Check("Migrações central (016) e de UBS (013) reaplicadas duas vezes; estruturas, índices e versão do schema presentes", async () =>
            {
                var migration = app.Services.GetRequiredService<MigrationDao>();
                await migration.MigrateAsync(); await migration.MigrateAsync();
                Require(await Scalar<long>("SELECT count(*) FROM information_schema.tables WHERE table_schema=$1 AND table_name IN ('announcements','announcement_regions','announcement_units','announcement_reads')", schema) == 4, "Tabelas da migração 016 ausentes.");
                Require(await Scalar<long>("SELECT count(*) FROM pg_indexes WHERE schemaname=$1 AND indexname IN ('announcements_open_seq_idx','announcements_creator_idx','announcement_units_unit_idx','announcement_regions_key_idx','health_units_region_key_idx')", schema) == 5, "Índices da migração 016 ausentes.");
                var unitMigration = app.Services.GetRequiredService<UnitMigrationDao>();
                await unitMigration.MigrateAsync(u1); await unitMigration.MigrateAsync(u1);
                Require(await UnitScalar<long>(u1, "SELECT count(*) FROM information_schema.tables WHERE table_schema='nora_unit' AND table_name IN ('health_exams','health_vaccines','health_record_events')") == 3, "Tabelas da migração de UBS 013 ausentes.");
                Require(await UnitScalar<long>(u1, "SELECT count(*) FROM pg_indexes WHERE schemaname='nora_unit' AND indexname IN ('health_exams_patient_idx','health_vaccines_patient_idx','health_record_events_record_idx')") == 3, "Índices da migração de UBS 013 ausentes.");
                Require(await UnitScalar<int>(u1, "SELECT version FROM nora_unit.schema_version WHERE singleton") == UnitSchema.ExpectedVersion && UnitSchema.ExpectedVersion >= 13, "Carimbo da versão do schema da UBS incorreto.");
                // O banco da UBS criado depois (U2) já nasce com a 013 aplicada pelo cadastro automático.
                Require(await UnitScalar<long>(u2, "SELECT count(*) FROM information_schema.tables WHERE table_schema='nora_unit' AND table_name='health_exams'") == 1, "UBS nova sem a migração 013.");
            });

            await Check("Criação: só a Secretaria; validações de título, texto, público, alcance e validade (400); campos devolvidos", async () =>
            {
                var good = new { title = "Aviso geral", body = "Texto", audience = "both", scope = "all", regions = (string[]?)null, unitIds = (Guid[]?)null, expiresAt = (DateTimeOffset?)null };
                using (var response = await anonymous.PostAsJsonAsync("/api/municipal/announcements", good)) Expect(response, 401);
                foreach (var forbidden in new[] { obsA, patients[1] }) using (var response = await forbidden.PostAsJsonAsync("/api/municipal/announcements", good)) Expect(response, 403);
                var tooLongBody = new string('x', 2001);
                (string Case, object Body)[] invalid =
                [
                    ("título vazio", new { title = "  ", body = "Texto", audience = "both", scope = "all" }),
                    ("título com 121", new { title = new string('t', 121), body = "Texto", audience = "both", scope = "all" }),
                    ("título com controle", new { title = "a\u0007b", body = "Texto", audience = "both", scope = "all" }),
                    ("texto vazio", new { title = "T", body = "", audience = "both", scope = "all" }),
                    ("texto com 2001", new { title = "T", body = tooLongBody, audience = "both", scope = "all" }),
                    ("texto com controle", new { title = "T", body = "a\u0000b", audience = "both", scope = "all" }),
                    ("público inválido", new { title = "T", body = "Texto", audience = "everyone", scope = "all" }),
                    ("alcance inválido", new { title = "T", body = "Texto", audience = "both", scope = "world" }),
                    ("região sem regiões", new { title = "T", body = "Texto", audience = "both", scope = "region" }),
                    ("região inexistente", new { title = "T", body = "Texto", audience = "both", scope = "region", regions = new[] { "Região que não existe" } }),
                    ("UBS sem ids", new { title = "T", body = "Texto", audience = "both", scope = "units" }),
                    ("UBS inexistente", new { title = "T", body = "Texto", audience = "both", scope = "units", unitIds = new[] { Guid.NewGuid() } }),
                    ("rede com regiões", new { title = "T", body = "Texto", audience = "both", scope = "all", regions = new[] { "Centro" } }),
                    ("região com UBS", new { title = "T", body = "Texto", audience = "both", scope = "region", regions = new[] { "Centro" }, unitIds = new[] { u1 } }),
                    ("validade no passado", new { title = "T", body = "Texto", audience = "both", scope = "all", expiresAt = clock.GetUtcNow().AddMinutes(-1) }),
                ];
                foreach (var (name, body) in invalid) { using var response = await m2.PostAsJsonAsync("/api/municipal/announcements", body); Expect(response, 400); Require(response.Content.Headers.ContentType?.MediaType == "application/problem+json", $"{name}: erro sem problem+json."); }
                var created = await Publish(m2, "Aviso da M2", "patients", "region", regions: ["  CENTRO "], expires: clock.GetUtcNow().AddDays(1));
                Require(created.GetProperty("audience").GetString() == "patients" && created.GetProperty("scope").GetString() == "region" && created.GetProperty("status").GetString() == "active", "Contrato do comunicado incorreto.");
                Require(created.GetProperty("regions").EnumerateArray().Select(r => r.GetString()).SequenceEqual(["Centro"]), "A região deve voltar com a grafia da UBS cadastrada.");
                Require(created.GetProperty("staffReads").GetInt64() == 0 && created.GetProperty("patientReads").GetInt64() == 0 && created.GetProperty("sequence").GetInt64() > 0, "Contagens iniciais incorretas.");
            });

            // Matriz de público x alcance.
            var rede = await Publish(m1, "N1 rede ambos", "both", "all");
            var centroPac = await Publish(m1, "N2 centro pacientes", "patients", "region", regions: ["Centro"]);
            var norteEq = await Publish(m1, "N3 norte equipes", "staff", "region", regions: ["norte"]);
            var u3Ambos = await Publish(m1, "N4 UBS3 ambos", "both", "units", unitIds: [u3]);
            var u1u2Eq = await Publish(m1, "N5 UBS1 e UBS2 equipes", "staff", "units", unitIds: [u1, u2, u2]);

            await Check("Público e alcance: rede, região e UBS chegam só a quem deve (equipes pela concessão, pacientes pelo vínculo); sem UBS não recebe nada", async () =>
            {
                SameSet(await Own(obsA, ObsList), ["N1 rede ambos", "N5 UBS1 e UBS2 equipes"], "OBS A (UBS Centro)");
                SameSet(await Own(obsB, ObsList), ["N1 rede ambos", "N3 norte equipes", "N4 UBS3 ambos", "N5 UBS1 e UBS2 equipes"], "OBS B (UBS Norte Um e Dois)");
                SameSet(await Own(obsC, ObsList), [], "OBS C (sem concessão)");
                SameSet(await Own(patients[1], PatientList), ["N1 rede ambos", "N2 centro pacientes"], "Paciente 1 (Centro)");
                SameSet(await Own(patients[2], PatientList), ["N1 rede ambos"], "Paciente 2 (Norte Um)");
                SameSet(await Own(patients[3], PatientList), ["N1 rede ambos", "N2 centro pacientes"], "Paciente 3 (Centro)");
                SameSet(await Own(patients[4], PatientList), [], "Paciente 4 (sem vínculo)");
                SameSet(await Own(patients[5], PatientList), ["N1 rede ambos", "N4 UBS3 ambos"], "Paciente 5 (Norte Dois)");
                SameSet(await Own(patients[6], PatientList), ["N1 rede ambos", "N2 centro pacientes", "N4 UBS3 ambos"], "Paciente 6 (Centro e Norte Dois)");
                Require(u1u2Eq.GetProperty("unitIds").GetArrayLength() == 2, "IDs de UBS repetidos não foram unificados.");
            });

            await Check("Alcance avaliado na leitura: mudar a região da UBS, revogar concessão e remover vínculo mudam o que cada um vê", async () =>
            {
                using (var move = await m1.PutAsJsonAsync($"/api/municipal/units/{u1}", new { name = "UBS Centro", region = "Leste", address = "Rua de teste, 10", latitude = -23.55, longitude = -46.63 })) Expect(move, 200);
                try { SameSet(await Own(patients[1], PatientList), ["N1 rede ambos"], "Paciente 1 depois de a UBS mudar de região"); }
                finally { using var back = await m1.PutAsJsonAsync($"/api/municipal/units/{u1}", new { name = "UBS Centro", region = "Centro", address = "Rua de teste, 10", latitude = -23.55, longitude = -46.63 }); Expect(back, 200); }
                SameSet(await Own(patients[1], PatientList), ["N1 rede ambos", "N2 centro pacientes"], "Paciente 1 com a região de volta");
                using (var revoke = await m1.DeleteAsync($"/api/municipal/staff/{adminA.Id}/units/{u1}")) Expect(revoke, 204);
                try { SameSet(await Own(obsA, ObsList), [], "OBS A sem concessão"); }
                finally { await Grant(adminA.Id, u1); }
                SameSet(await Own(obsA, ObsList), ["N1 rede ambos", "N5 UBS1 e UBS2 equipes"], "OBS A com a concessão de volta");
                SameSet(await Own(patients[7], PatientList), ["N1 rede ambos", "N2 centro pacientes"], "Paciente 7 antes de remover o vínculo");
                using (var unlink = await patients[7].PostAsJsonAsync($"/api/patient/units/{u1}/unlink", new { confirmed = true })) Expect(unlink, 200);
                SameSet(await Own(patients[7], PatientList), [], "Paciente 7 sem vínculo");
            });

            await Check("Leitura por conta: marca com instante, é idempotente, só do que a conta vê; a Secretaria vê contagens e nomes das equipes (sem e-mail)", async () =>
            {
                var n1 = rede.GetProperty("id").GetGuid(); var n3 = norteEq.GetProperty("id").GetGuid(); var n2 = centroPac.GetProperty("id").GetGuid();
                using (var anonymousRead = await anonymous.PostAsync($"/api/obs/announcements/{n1}/read", null)) Expect(anonymousRead, 401);
                using (var first = await obsA.PostAsync($"/api/obs/announcements/{n1}/read", null)) Expect(first, 204);
                var firstInstant = await Scalar<DateTime>("SELECT read_at FROM nora.announcement_reads WHERE announcement_id=$1 AND account_id=$2", n1, adminA.Id);
                clock.Advance(TimeSpan.FromMinutes(5));
                using (var again = await obsA.PostAsync($"/api/obs/announcements/{n1}/read", null)) Expect(again, 204);
                Require(await Scalar<DateTime>("SELECT read_at FROM nora.announcement_reads WHERE announcement_id=$1 AND account_id=$2", n1, adminA.Id) == firstInstant, "Reler mudou o instante da primeira leitura.");
                Require(firstInstant == clock.GetUtcNow().AddMinutes(-5).UtcDateTime, "O instante da leitura deve ser o do relógio da API.");
                using (var invisible = await obsA.PostAsync($"/api/obs/announcements/{n3}/read", null)) Expect(invisible, 404);
                using (var unknown = await obsA.PostAsync($"/api/obs/announcements/{Guid.NewGuid()}/read", null)) Expect(unknown, 404);
                using (var noGrant = await obsC.PostAsync($"/api/obs/announcements/{n1}/read", null)) Expect(noGrant, 404);
                using (var wrongAudience = await patients[1].PostAsync($"/api/patient/announcements/{n3}/read", null)) Expect(wrongAudience, 404);
                using (var unlinked = await patients[2].PostAsync($"/api/patient/announcements/{n2}/read", null)) Expect(unlinked, 404);
                using (var noLink = await patients[4].PostAsync($"/api/patient/announcements/{n1}/read", null)) Expect(noLink, 404);
                using (var b = await obsB.PostAsync($"/api/obs/announcements/{n1}/read", null)) Expect(b, 204);
                foreach (var id in new[] { n1, n2 }) using (var p = await patients[1].PostAsync($"/api/patient/announcements/{id}/read", null)) Expect(p, 204);
                using (var p3 = await patients[3].PostAsync($"/api/patient/announcements/{n1}/read", null)) Expect(p3, 204);
                var listed = (await Page(obsA, ObsList)).GetProperty("announcements").EnumerateArray().Single(a => a.GetProperty("id").GetGuid() == n1);
                Require(listed.GetProperty("isRead").GetBoolean() && listed.GetProperty("readAt").GetDateTimeOffset().UtcDateTime == firstInstant, "O OBS não devolveu a marcação de lido.");
                var unread = (await Page(obsA, ObsList)).GetProperty("announcements").EnumerateArray().Single(a => a.GetProperty("title").GetString() == "N5 UBS1 e UBS2 equipes");
                Require(!unread.GetProperty("isRead").GetBoolean() && unread.GetProperty("readAt").ValueKind == JsonValueKind.Null, "Comunicado não lido saiu como lido.");
                using var summary = await m1.GetAsync("/api/municipal/announcements?limit=100"); Expect(summary, 200);
                var row = (await Read(summary)).GetProperty("announcements").EnumerateArray().Single(a => a.GetProperty("id").GetGuid() == n1);
                Require(row.GetProperty("staffReads").GetInt64() == 2 && row.GetProperty("patientReads").GetInt64() == 2, "Contagem de leituras por público incorreta.");
                using var reads = await m1.GetAsync($"/api/municipal/announcements/{n1}/reads"); Expect(reads, 200);
                var raw = await reads.Content.ReadAsStringAsync(); var body = JsonDocument.Parse(raw).RootElement;
                Require(body.GetProperty("staffReads").GetInt64() == 2 && body.GetProperty("patientReads").GetInt64() == 2, "Contagens do detalhe incorretas.");
                Require(body.GetProperty("staff").EnumerateArray().Select(r => r.GetProperty("fullName").GetString()).Order().SequenceEqual(["Administradora A", "Atendente B"]), "Nomes das equipes que leram incorretos.");
                Require(!raw.Contains("@") && !raw.Contains("email", StringComparison.OrdinalIgnoreCase) && !raw.Contains("Paciente"), "O detalhe das leituras não pode expor e-mail nem nome de paciente.");
                using (var otherManager = await m2.GetAsync($"/api/municipal/announcements/{n1}/reads")) Expect(otherManager, 404);
                using (var forbidden = await obsA.GetAsync($"/api/municipal/announcements/{n1}/reads")) Expect(forbidden, 403);
            });

            await Check("Encerrar: confirmação obrigatória, não apaga, some para equipes e pacientes, 409 repetido, 404 de outra conta; validade vencida também some", async () =>
            {
                var closable = await Publish(m1, "N6 encerrável", "both", "all");
                var expiring = await Publish(m1, "N7 expira", "both", "all", expires: clock.GetUtcNow().AddHours(1));
                var id = closable.GetProperty("id").GetGuid();
                Require((await Own(obsA, ObsList)).Contains("N6 encerrável") && (await Own(patients[1], PatientList)).Contains("N6 encerrável"), "O comunicado deveria aparecer antes de encerrar.");
                using (var anonymousClose = await anonymous.PostAsJsonAsync($"/api/municipal/announcements/{id}/close", new { confirmed = true })) Expect(anonymousClose, 401);
                using (var obsClose = await obsA.PostAsJsonAsync($"/api/municipal/announcements/{id}/close", new { confirmed = true })) Expect(obsClose, 403);
                using (var withoutConfirm = await m1.PostAsJsonAsync($"/api/municipal/announcements/{id}/close", new { confirmed = false })) Expect(withoutConfirm, 400);
                using (var missingConfirm = await m1.PostAsJsonAsync($"/api/municipal/announcements/{id}/close", new { })) Expect(missingConfirm, 400);
                using (var otherManager = await m2.PostAsJsonAsync($"/api/municipal/announcements/{id}/close", new { confirmed = true })) Expect(otherManager, 404);
                using (var unknown = await m1.PostAsJsonAsync($"/api/municipal/announcements/{Guid.NewGuid()}/close", new { confirmed = true })) Expect(unknown, 404);
                using (var close = await m1.PostAsJsonAsync($"/api/municipal/announcements/{id}/close", new { confirmed = true })) Expect(close, 204);
                using (var twice = await m1.PostAsJsonAsync($"/api/municipal/announcements/{id}/close", new { confirmed = true })) Expect(twice, 409);
                Require(!(await Own(obsA, ObsList)).Contains("N6 encerrável") && !(await Own(patients[1], PatientList)).Contains("N6 encerrável"), "Comunicado encerrado continua aparecendo.");
                using (var readClosed = await obsA.PostAsync($"/api/obs/announcements/{id}/read", null)) Expect(readClosed, 404);
                Require(await Scalar<long>("SELECT count(*) FROM nora.announcements WHERE id=$1 AND closed_at IS NOT NULL AND closed_by IS NOT NULL", id) == 1, "Encerrar não pode apagar o comunicado.");
                Require((await Own(obsA, ObsList)).Contains("N7 expira"), "O comunicado dentro da validade deveria aparecer.");
                clock.Advance(TimeSpan.FromHours(2));
                Require(!(await Own(obsA, ObsList)).Contains("N7 expira") && !(await Own(patients[6], PatientList)).Contains("N7 expira"), "Comunicado vencido continua aparecendo.");
                var status = (await (await m1.GetAsync("/api/municipal/announcements?limit=100")).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("announcements").EnumerateArray().ToDictionary(a => a.GetProperty("title").GetString()!, a => a.GetProperty("status").GetString());
                Require(status["N6 encerrável"] == "closed" && status["N7 expira"] == "expired" && status["N1 rede ambos"] == "active", "Situação dos comunicados na Secretaria incorreta.");
            });

            await Check("A Secretaria lista só os seus, do mais recente ao mais antigo, com cursor e contagens", async () =>
            {
                var mine = (await Page(m1, "/api/municipal/announcements?limit=100")).GetProperty("announcements").EnumerateArray().Select(a => a.GetProperty("title").GetString()!).ToList();
                Require(mine.Count == 7 && mine[0] == "N7 expira" && mine[^1] == "N1 rede ambos" && !mine.Contains("Aviso da M2"), "A listagem deve trazer só os comunicados da própria conta, do mais novo ao mais antigo.");
                var others = (await Page(m2, "/api/municipal/announcements")).GetProperty("announcements").EnumerateArray().Select(a => a.GetProperty("title").GetString()).ToList();
                Require(others.SequenceEqual(["Aviso da M2"]), "A outra conta deve ver só o seu.");
                var walked = new List<string>(); string route = "/api/municipal/announcements?limit=3";
                for (var guard = 0; guard < 10; guard++)
                {
                    var page = await Page(m1, route);
                    walked.AddRange(page.GetProperty("announcements").EnumerateArray().Select(a => a.GetProperty("title").GetString()!));
                    if (page.GetProperty("nextBefore").ValueKind == JsonValueKind.Null) break;
                    route = $"/api/municipal/announcements?limit=3&before={page.GetProperty("nextBefore").GetInt64()}";
                }
                Require(walked.SequenceEqual(mine), "Paginar com before deve percorrer a mesma lista, sem repetir nem perder.");
                foreach (var bad in new[] { "limit=0", "limit=101", "before=0", "before=-3" }) { using var response = await m1.GetAsync("/api/municipal/announcements?" + bad); Expect(response, 400); }
                using (var obs = await obsA.GetAsync("/api/municipal/announcements")) Expect(obs, 403);
                using (var anonymousList = await anonymous.GetAsync("/api/municipal/announcements")) Expect(anonymousList, 401);
            });

            await Check("Incremental do paciente: só novos, cursor estável, não-visíveis avançam o cursor sem aparecer, paginação sem perda", async () =>
            {
                var first = await Page(patients[1], PatientList);
                var visible = first.GetProperty("announcements").EnumerateArray().ToList();
                var sequences = visible.Select(a => a.GetProperty("sequence").GetInt64()).ToList();
                Require(sequences.SequenceEqual(sequences.Order()) && sequences.Distinct().Count() == sequences.Count, "A lista deve vir em ordem crescente de sequência.");
                var c0 = first.GetProperty("cursor").GetInt64();
                Require(!first.GetProperty("hasMore").GetBoolean() && c0 >= sequences.Max(), "O cursor deve cobrir tudo que foi devolvido.");
                var quiet = await Page(patients[1], $"{PatientList}?after={c0}");
                Require(quiet.GetProperty("announcements").GetArrayLength() == 0 && quiet.GetProperty("cursor").GetInt64() == c0 && !quiet.GetProperty("hasMore").GetBoolean(), "Sem novidade o cursor deve ficar igual e a lista vazia.");
                await Publish(m4, "N8 novo para pacientes", "patients", "all");
                var delta = await Page(patients[1], $"{PatientList}?after={c0}");
                Require(delta.GetProperty("announcements").EnumerateArray().Select(a => a.GetProperty("title").GetString()).SequenceEqual(["N8 novo para pacientes"]), "O incremental deve devolver só o comunicado novo.");
                var c1 = delta.GetProperty("cursor").GetInt64(); Require(c1 > c0, "O cursor deve avançar.");
                await Publish(m4, "N9 só equipes", "staff", "all");
                var skipped = await Page(patients[1], $"{PatientList}?after={c1}");
                Require(skipped.GetProperty("announcements").GetArrayLength() == 0 && skipped.GetProperty("cursor").GetInt64() > c1, "Comunicado que o paciente não vê deve avançar o cursor sem aparecer.");
                var c2 = skipped.GetProperty("cursor").GetInt64();
                var stale = await Page(patients[1], $"{PatientList}?after={c2 + 1000}");
                Require(stale.GetProperty("announcements").GetArrayLength() == 0 && stale.GetProperty("cursor").GetInt64() == c2, "Cursor acima do máximo deve ressincronizar para o máximo.");
                var all = (await Page(patients[1], PatientList)).GetProperty("announcements").EnumerateArray().Select(a => a.GetProperty("id").GetGuid()).ToList();
                var walked = new List<Guid>(); long after = 0;
                for (var guard = 0; guard < 20; guard++)
                {
                    var page = await Page(patients[1], $"{PatientList}?after={after}&limit=1");
                    walked.AddRange(page.GetProperty("announcements").EnumerateArray().Select(a => a.GetProperty("id").GetGuid()));
                    after = page.GetProperty("cursor").GetInt64();
                    if (!page.GetProperty("hasMore").GetBoolean()) break;
                }
                Require(walked.SequenceEqual(all) && all.Count >= 3, "Paginar com limit=1 deve percorrer exatamente a lista completa.");
                foreach (var bad in new[] { "after=-1", "limit=0", "limit=101", "limit=abc" }) { using var response = await patients[1].GetAsync($"{PatientList}?{bad}"); Expect(response, 400); }
            });

            await Check("Publicações concorrentes: o incremental recebe todas, sem repetição e em ordem (a ordem de sequência é a de confirmação)", async () =>
            {
                var cursor = (await Page(patients[2], PatientList)).GetProperty("cursor").GetInt64();
                var m5 = NewClient(); await Login(m5, "municipal", "manager96@example.test");
                var titles = Enumerable.Range(1, 10).Select(i => $"N10 concorrente {i}").ToList();
                await Task.WhenAll(titles.Select((title, i) => Publish(i % 2 == 0 ? m4 : m5, title, "patients", "all")));
                var page = await Page(patients[2], $"{PatientList}?after={cursor}&limit=100");
                var received = page.GetProperty("announcements").EnumerateArray().ToList();
                var sequences = received.Select(a => a.GetProperty("sequence").GetInt64()).ToList();
                SameSet(received.Select(a => a.GetProperty("title").GetString()!), [.. titles], "Incremental após publicações concorrentes");
                Require(sequences.SequenceEqual(sequences.Order()) && sequences.Distinct().Count() == titles.Count && page.GetProperty("cursor").GetInt64() == sequences.Max(), "Sequência fora de ordem ou cursor incorreto.");
            });

            await Check("Texto puro: título e texto voltam exatamente como enviados (sem HTML interpretado, sem trim, sem normalização), no banco e em todas as leituras", async () =>
            {
                var title = "  <b>Aviso</b> & <script>alert(1)</script> \"x\" 'y' ";
                var body = "linha 1\n<img src=x onerror=alert(1)>\r\n\tcom tab &amp; &lt;i&gt; ção 😀 {{7*7}} ${x}  ";
                var created = await Publish(m4, title, "both", "units", unitIds: [u1], body: body);
                Require(created.GetProperty("title").GetString() == title && created.GetProperty("body").GetString() == body, "A criação alterou o texto.");
                var id = created.GetProperty("id").GetGuid();
                Require(await Scalar<string>("SELECT title FROM nora.announcements WHERE id=$1", id) == title && await Scalar<string>("SELECT body FROM nora.announcements WHERE id=$1", id) == body, "O banco guardou texto transformado.");
                foreach (var (who, route) in new[] { (obsA, ObsList), (patients[1], PatientList) })
                {
                    var item = (await Page(who, route + "?limit=100")).GetProperty("announcements").EnumerateArray().Single(a => a.GetProperty("id").GetGuid() == id);
                    Require(item.GetProperty("title").GetString() == title && item.GetProperty("body").GetString() == body, "A leitura alterou o texto.");
                }
                var own = (await Page(m4, "/api/municipal/announcements?limit=100")).GetProperty("announcements").EnumerateArray().Single(a => a.GetProperty("id").GetGuid() == id);
                Require(own.GetProperty("title").GetString() == title && own.GetProperty("body").GetString() == body, "A listagem da Secretaria alterou o texto.");
                using var response = await patients[1].GetAsync(PatientList + "?limit=100");
                Require(response.Content.Headers.ContentType?.MediaType == "application/json", "O texto deve sair como JSON, nunca como HTML.");
            });

            await Check("Autorização: perfis cruzados, sem sessão e conta sem UBS; o limite de publicação e de leitura é por conta", async () =>
            {
                foreach (var route in new[] { ObsList, PatientList, "/api/municipal/announcements" }) using (var response = await anonymous.GetAsync(route)) Expect(response, 401);
                using (var a = await patients[1].GetAsync(ObsList)) Expect(a, 403);
                using (var b = await obsA.GetAsync(PatientList)) Expect(b, 403);
                using (var c = await m1.GetAsync(ObsList)) Expect(c, 403);
                using (var d = await m1.GetAsync(PatientList)) Expect(d, 403);
                using (var e = await patients[1].PostAsync($"/api/obs/announcements/{Guid.NewGuid()}/read", null)) Expect(e, 403);
                using (var f = await obsA.PostAsync($"/api/patient/announcements/{Guid.NewGuid()}/read", null)) Expect(f, 403);
                // Publicação: 30 por conta; as inválidas também contam (o limite roda antes da validação). Outra conta não é afetada.
                for (var i = 0; i < 30; i++) using (var response = await m3.PostAsJsonAsync("/api/municipal/announcements", new { title = "", body = "x", audience = "both", scope = "all" })) Expect(response, 400);
                using (var limited = await m3.PostAsJsonAsync("/api/municipal/announcements", new { title = "", body = "x", audience = "both", scope = "all" })) await ExpectProblem(limited, 429, "too-many-requests");
                using (var other = await m2.GetAsync("/api/municipal/announcements")) Expect(other, 200);
                // Leitura do paciente: 120 por conta; o paciente 4 (sem vínculo) já gastou 1 e gasta o resto com 404; a 121ª é 429; o paciente 1 segue normal.
                for (var i = 0; i < 119; i++) using (var response = await patients[4].PostAsync($"/api/patient/announcements/{Guid.NewGuid()}/read", null)) Expect(response, 404);
                using (var rate = await patients[4].PostAsync($"/api/patient/announcements/{Guid.NewGuid()}/read", null)) await ExpectProblem(rate, 429, "too-many-requests");
                using (var unaffected = await patients[1].PostAsync($"/api/patient/announcements/{Guid.NewGuid()}/read", null)) Expect(unaffected, 404);
            });

            // ================= Jornada =================

            string Records(Guid unit) => $"/api/obs/units/{unit}/health-records";
            async Task<JsonElement> Query(HttpClient who, Guid unit, int n, bool includeVoided = false)
            {
                using var response = await who.PostAsJsonAsync(Records(unit) + "/query", new { cpf = Cpf(n), includeVoided });
                Expect(response, 200);
                return await Read(response);
            }
            async Task<JsonElement> NewExam(HttpClient who, Guid unit, int n, string name, string? note = null)
            {
                using var response = await who.PostAsJsonAsync(Records(unit) + "/exams", new { cpf = Cpf(n), name, note });
                Expect(response, 201);
                return await Read(response);
            }
            async Task<JsonElement> NewVaccine(HttpClient who, Guid unit, int n, string name, string dose, string appliedOn, string? note = null)
            {
                using var response = await who.PostAsJsonAsync(Records(unit) + "/vaccines", new { cpf = Cpf(n), name, dose, appliedOn, note });
                Expect(response, 201);
                return await Read(response);
            }
            async Task<HttpResponseMessage> ExamStatus(HttpClient who, Guid unit, Guid id, object body) => await who.PatchAsJsonAsync($"{Records(unit)}/exams/{id}/status", body);

            Guid examDone = Guid.Empty, examRequested = Guid.Empty, examCancelled = Guid.Empty, examVoided = Guid.Empty, vaccineKept = Guid.Empty, vaccineVoided = Guid.Empty;

            await Check("Exames: criação validada, transições requested→done/cancelled, demais 409, data de realização, anulação com motivo e eventos com autor", async () =>
            {
                using (var anonymousCreate = await anonymous.PostAsJsonAsync(Records(u1) + "/exams", new { cpf = Cpf(1), name = "Hemograma" })) Expect(anonymousCreate, 401);
                using (var patientCreate = await patients[1].PostAsJsonAsync(Records(u1) + "/exams", new { cpf = Cpf(1), name = "Hemograma" })) Expect(patientCreate, 403);
                using (var municipalCreate = await m1.PostAsJsonAsync(Records(u1) + "/exams", new { cpf = Cpf(1), name = "Hemograma" })) Expect(municipalCreate, 403);
                using (var otherUnit = await obsB.PostAsJsonAsync(Records(u1) + "/exams", new { cpf = Cpf(1), name = "Hemograma" })) Expect(otherUnit, 403);
                using (var noGrant = await obsC.PostAsJsonAsync(Records(u1) + "/exams", new { cpf = Cpf(1), name = "Hemograma" })) Expect(noGrant, 403);
                foreach (var invalid in new object[]
                {
                    new { cpf = "11111111111", name = "Hemograma" }, new { cpf = Cpf(1), name = "  " }, new { cpf = Cpf(1), name = new string('n', 161) },
                    new { cpf = Cpf(1), name = "Hemograma", note = new string('o', 281) }, new { cpf = Cpf(1), name = "Hemo\u0007grama" }, new { name = "Hemograma" }
                }) { using var response = await obsA.PostAsJsonAsync(Records(u1) + "/exams", invalid); Expect(response, 400); }
                using (var notRegistered = await obsA.PostAsJsonAsync(Records(u1) + "/exams", new { cpf = Cpf(2), name = "Hemograma" })) Expect(notRegistered, 404);
                var exam = await NewExam(obsA, u1, 1, "  Hemograma completo ", "Jejum de 8 horas");
                examDone = exam.GetProperty("id").GetGuid();
                Require(exam.GetProperty("status").GetString() == "requested" && exam.GetProperty("name").GetString() == "Hemograma completo" && exam.GetProperty("performedOn").ValueKind == JsonValueKind.Null && exam.GetProperty("note").GetString() == "Jejum de 8 horas", "Contrato do exame criado incorreto.");
                Require(exam.GetProperty("requestedAt").GetDateTimeOffset() == clock.GetUtcNow(), "O instante do pedido deve ser o do relógio da API.");
                foreach (var invalid in new object[] { new { status = "done" }, new { status = "done", performedOn = "2999-01-01" }, new { status = "done", performedOn = "1899-12-31" }, new { status = "cancelled", performedOn = "2026-10-01" },
                    new { status = "requested" }, new { status = "voided" }, new { status = "x" }, new { performedOn = "2026-10-01" } })
                { using var response = await ExamStatus(obsA, u1, examDone, invalid); Expect(response, 400); }
                using (var unknown = await ExamStatus(obsA, u1, Guid.NewGuid(), new { status = "cancelled" })) Expect(unknown, 404);
                using (var wrongUnit = await ExamStatus(obsB, u2, examDone, new { status = "cancelled" })) Expect(wrongUnit, 404);
                clock.Advance(TimeSpan.FromMinutes(10));
                using (var done = await ExamStatus(obsA, u1, examDone, new { status = "done", performedOn = "2026-10-01" }))
                {
                    Expect(done, 200); var body = await Read(done);
                    Require(body.GetProperty("status").GetString() == "done" && body.GetProperty("performedOn").GetString() == "2026-10-01", "Exame concluído com contrato incorreto.");
                }
                foreach (var after in new object[] { new { status = "cancelled" }, new { status = "done", performedOn = "2026-10-02" } })
                { using var response = await ExamStatus(obsA, u1, examDone, after); Expect(response, 409); }
                var cancelled = await NewExam(obsA, u1, 1, "Raio-X de tórax"); examCancelled = cancelled.GetProperty("id").GetGuid();
                using (var cancel = await ExamStatus(obsA, u1, examCancelled, new { status = "cancelled" })) { Expect(cancel, 200); Require((await Read(cancel)).GetProperty("status").GetString() == "cancelled", "Exame não ficou cancelado."); }
                using (var reopen = await ExamStatus(obsA, u1, examCancelled, new { status = "done", performedOn = "2026-10-03" })) Expect(reopen, 409);
                clock.Advance(TimeSpan.FromMinutes(1));
                examRequested = (await NewExam(obsA, u1, 1, "Ultrassom")).GetProperty("id").GetGuid();
                var toVoid = await NewExam(obsA, u1, 1, "Exame lançado por engano"); examVoided = toVoid.GetProperty("id").GetGuid();
                using (var missing = await obsA.PostAsJsonAsync($"{Records(u1)}/exams/{examVoided}/void", new { reason = "Lançado no paciente errado" })) Expect(missing, 400);
                using (var unconfirmed = await obsA.PostAsJsonAsync($"{Records(u1)}/exams/{examVoided}/void", new { reason = "Lançado no paciente errado", confirmed = false })) Expect(unconfirmed, 400);
                foreach (var reason in new[] { "", "ab", new string('m', 281), "motivo\u0007inválido" }) { using var response = await obsA.PostAsJsonAsync($"{Records(u1)}/exams/{examVoided}/void", new { reason, confirmed = true }); Expect(response, 400); }
                using (var otherUnitVoid = await obsB.PostAsJsonAsync($"{Records(u1)}/exams/{examVoided}/void", new { reason = "Lançado no paciente errado", confirmed = true })) Expect(otherUnitVoid, 403);
                clock.Advance(TimeSpan.FromMinutes(1));
                using (var voided = await obsA.PostAsJsonAsync($"{Records(u1)}/exams/{examVoided}/void", new { reason = "Lançado no paciente errado", confirmed = true }))
                {
                    Expect(voided, 200); var body = await Read(voided);
                    Require(body.GetProperty("status").GetString() == "voided" && body.GetProperty("voidReason").GetString() == "Lançado no paciente errado" && body.GetProperty("voidedAt").GetDateTimeOffset() == clock.GetUtcNow(), "Anulação com contrato incorreto.");
                }
                using (var again = await obsA.PostAsJsonAsync($"{Records(u1)}/exams/{examVoided}/void", new { reason = "Outro motivo qualquer", confirmed = true })) Expect(again, 409);
                using (var change = await ExamStatus(obsA, u1, examVoided, new { status = "cancelled" })) Expect(change, 409);
                using (var voidUnknown = await obsA.PostAsJsonAsync($"{Records(u1)}/exams/{Guid.NewGuid()}/void", new { reason = "Motivo qualquer", confirmed = true })) Expect(voidUnknown, 404);
                Require(await UnitScalar<long>(u1, "SELECT count(*) FROM nora_unit.health_exams WHERE id=$1", examVoided) == 1, "Anular não pode apagar o registro.");
                var events = await UnitScalar<long>(u1, "SELECT count(*) FROM nora_unit.health_record_events WHERE kind='exam' AND record_id=$1 AND actor_id=$2 AND action IN ('created','done')", examDone, adminA.Id);
                Require(events == 2, "Eventos de criação e conclusão do exame sem o autor.");
                Require(await UnitScalar<long>(u1, "SELECT count(*) FROM nora_unit.health_record_events WHERE kind='exam' AND record_id=$1 AND actor_id=$2 AND action='voided' AND occurred_at=$3", examVoided, adminA.Id, clock.GetUtcNow()) == 1, "Evento de anulação sem autor e instante.");
                Require(await UnitScalar<long>(u1, "SELECT count(*) FROM nora_unit.health_record_events WHERE kind='exam' AND record_id=$1 AND action='cancelled'", examCancelled) == 1, "Evento de cancelamento ausente.");
                Require(await UnitScalar<long>(u1, "SELECT count(*) FROM nora_unit.health_record_events WHERE record_id=$1", examDone) == 2, "Recusas (409) não podem gravar evento.");
            });

            await Check("Vacinas: doses válidas (1, 2, 3, reforço, única), datas e campos validados, anulação com motivo e eventos com autor", async () =>
            {
                foreach (var invalid in new object[]
                {
                    new { cpf = Cpf(1), name = "Tríplice", dose = "4", appliedOn = "2026-09-01" }, new { cpf = Cpf(1), name = "Tríplice", dose = "", appliedOn = "2026-09-01" }, new { cpf = Cpf(1), name = "Tríplice", dose = "BOOSTER", appliedOn = "2026-09-01" },
                    new { cpf = Cpf(1), name = "Tríplice", appliedOn = "2026-09-01" }, new { cpf = Cpf(1), name = "Tríplice", dose = "1" }, new { cpf = Cpf(1), name = "Tríplice", dose = "1", appliedOn = "2999-01-01" },
                    new { cpf = Cpf(1), name = "Tríplice", dose = "1", appliedOn = "1899-12-31" }, new { cpf = Cpf(1), name = "", dose = "1", appliedOn = "2026-09-01" }, new { cpf = Cpf(1), name = "Tríplice", dose = "1", appliedOn = "ontem" },
                    new { cpf = "123", name = "Tríplice", dose = "1", appliedOn = "2026-09-01" }, new { cpf = Cpf(1), name = "Tríplice", dose = "1", appliedOn = "2026-09-01", note = new string('o', 281) }
                }) { using var response = await obsA.PostAsJsonAsync(Records(u1) + "/vaccines", invalid); Expect(response, 400); }
                using (var notRegistered = await obsA.PostAsJsonAsync(Records(u1) + "/vaccines", new { cpf = Cpf(2), name = "Tríplice", dose = "1", appliedOn = "2026-09-01" })) Expect(notRegistered, 404);
                using (var patient = await patients[1].PostAsJsonAsync(Records(u1) + "/vaccines", new { cpf = Cpf(1), name = "Tríplice", dose = "1", appliedOn = "2026-09-01" })) Expect(patient, 403);
                using (var anonymousCreate = await anonymous.PostAsJsonAsync(Records(u1) + "/vaccines", new { cpf = Cpf(1), name = "Tríplice", dose = "1", appliedOn = "2026-09-01" })) Expect(anonymousCreate, 401);
                var first = await NewVaccine(obsA, u1, 1, "Hepatite B", "1", "2026-08-10", "Braço esquerdo");
                Require(first.GetProperty("status").GetString() == "applied" && first.GetProperty("dose").GetString() == "1" && first.GetProperty("appliedOn").GetString() == "2026-08-10" && first.GetProperty("note").GetString() == "Braço esquerdo", "Contrato da vacina criada incorreto.");
                vaccineKept = first.GetProperty("id").GetGuid();
                foreach (var (dose, date) in new[] { ("2", "2026-09-10"), ("3", "2026-09-20"), ("booster", "2026-10-02"), ("single", "2026-07-01") })
                    Require((await NewVaccine(obsA, u1, 1, "Vacina de teste " + dose, dose, date)).GetProperty("dose").GetString() == dose, $"Dose {dose} recusada.");
                var wrong = await NewVaccine(obsA, u1, 1, "Vacina lançada por engano", "single", "2026-10-01"); vaccineVoided = wrong.GetProperty("id").GetGuid();
                using (var unconfirmed = await obsA.PostAsJsonAsync($"{Records(u1)}/vaccines/{vaccineVoided}/void", new { reason = "Paciente errado", confirmed = false })) Expect(unconfirmed, 400);
                using (var shortReason = await obsA.PostAsJsonAsync($"{Records(u1)}/vaccines/{vaccineVoided}/void", new { reason = "x", confirmed = true })) Expect(shortReason, 400);
                using (var otherUnit = await obsB.PostAsJsonAsync($"{Records(u1)}/vaccines/{vaccineVoided}/void", new { reason = "Paciente errado", confirmed = true })) Expect(otherUnit, 403);
                clock.Advance(TimeSpan.FromMinutes(1));
                using (var voided = await obsA.PostAsJsonAsync($"{Records(u1)}/vaccines/{vaccineVoided}/void", new { reason = "Paciente errado", confirmed = true }))
                { Expect(voided, 200); var body = await Read(voided); Require(body.GetProperty("status").GetString() == "voided" && body.GetProperty("voidReason").GetString() == "Paciente errado", "Anulação da vacina com contrato incorreto."); }
                using (var again = await obsA.PostAsJsonAsync($"{Records(u1)}/vaccines/{vaccineVoided}/void", new { reason = "Paciente errado", confirmed = true })) Expect(again, 409);
                using (var unknown = await obsA.PostAsJsonAsync($"{Records(u1)}/vaccines/{Guid.NewGuid()}/void", new { reason = "Paciente errado", confirmed = true })) Expect(unknown, 404);
                Require(await UnitScalar<long>(u1, "SELECT count(*) FROM nora_unit.health_vaccines WHERE id=$1", vaccineVoided) == 1, "Anular não pode apagar a vacina.");
                Require(await UnitScalar<long>(u1, "SELECT count(*) FROM nora_unit.health_record_events WHERE kind='vaccine' AND record_id=$1 AND actor_id=$2 AND ((action='created') OR (action='voided' AND occurred_at=$3))", vaccineVoided, adminA.Id, clock.GetUtcNow()) == 2, "Eventos da vacina sem autor e instante.");
                Require(await UnitScalar<long>(u1, "SELECT count(*) FROM nora_unit.health_vaccines WHERE dose NOT IN ('1','2','3','booster','single')") == 0, "Dose fora do domínio no banco.");
            });

            await Check("Consulta da equipe por CPF no corpo: ordenada, sem anulados por padrão, com motivo quando pedidos; sem acesso cruzado entre UBS", async () =>
            {
                var standard = await Query(obsA, u1, 1);
                var exams = standard.GetProperty("exams").EnumerateArray().Select(e => e.GetProperty("name").GetString()).ToList();
                Require(exams.SequenceEqual(["Ultrassom", "Raio-X de tórax", "Hemograma completo"]), $"Exames devolvidos incorretos: {string.Join("; ", exams)}.");
                // A ordem é pela data do exame (realização ou, sem ela, a do pedido), do mais recente ao mais antigo.
                var dates = standard.GetProperty("exams").EnumerateArray().Select(e => e.GetProperty("performedOn").ValueKind == JsonValueKind.Null ? DateOnly.FromDateTime(e.GetProperty("requestedAt").GetDateTimeOffset().UtcDateTime) : DateOnly.Parse(e.GetProperty("performedOn").GetString()!)).ToList();
                Require(dates.SequenceEqual(dates.OrderDescending()), "Os exames devem vir do mais recente ao mais antigo.");
                Require(!standard.GetProperty("exams").EnumerateArray().Any(e => e.GetProperty("id").GetGuid() == examVoided) && !standard.GetProperty("vaccines").EnumerateArray().Any(v => v.GetProperty("id").GetGuid() == vaccineVoided), "Anulados não aparecem por padrão.");
                var vaccineDates = standard.GetProperty("vaccines").EnumerateArray().Select(v => DateOnly.Parse(v.GetProperty("appliedOn").GetString()!)).ToList();
                Require(vaccineDates.Count == 5 && vaccineDates.SequenceEqual(vaccineDates.OrderDescending()), "As vacinas devem vir da aplicação mais recente à mais antiga (5 não anuladas).");
                var withVoided = await Query(obsA, u1, 1, includeVoided: true);
                Require(withVoided.GetProperty("exams").EnumerateArray().Single(e => e.GetProperty("id").GetGuid() == examVoided).GetProperty("voidReason").GetString() == "Lançado no paciente errado", "A equipe deve ver o motivo da anulação.");
                Require(withVoided.GetProperty("vaccines").GetArrayLength() == 6, "includeVoided deveria trazer a vacina anulada.");
                var empty = await Query(obsA, u1, 3);
                Require(empty.GetProperty("exams").GetArrayLength() == 0 && empty.GetProperty("vaccines").GetArrayLength() == 0, "Paciente sem registros deve voltar vazio.");
                using (var unknown = await obsA.PostAsJsonAsync(Records(u1) + "/query", new { cpf = Cpf(2) })) Expect(unknown, 404);
                using (var invalid = await obsA.PostAsJsonAsync(Records(u1) + "/query", new { cpf = "123" })) Expect(invalid, 400);
                foreach (var denied in new[] { obsB, obsC, m1, patients[1] }) using (var response = await denied.PostAsJsonAsync(Records(u1) + "/query", new { cpf = Cpf(1) })) Expect(response, 403);
                using (var anonymousQuery = await anonymous.PostAsJsonAsync(Records(u1) + "/query", new { cpf = Cpf(1) })) Expect(anonymousQuery, 401);
                using (var noCpfInUrl = await obsA.GetAsync($"{Records(u1)}/{Cpf(1)}")) Require(noCpfInUrl.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed, "Não pode existir rota com CPF na URL.");
                using (var secretariat = await m1.GetAsync($"/api/municipal/units/{u1}/health-records")) Expect(secretariat, 404);
            });

            await Check("Paciente só visualiza: o próprio histórico, sem anulados por padrão e sem o motivo; sem vínculo → 403; escrita → 403/405; CPF de outro é ignorado", async () =>
            {
                var own = await Page(patients[1], $"/api/patient/units/{u1}/health-records");
                Require(own.GetProperty("exams").GetArrayLength() == 3 && own.GetProperty("vaccines").GetArrayLength() == 5, "O paciente deveria ver 3 exames e 5 vacinas (sem anulados).");
                var withVoided = await Page(patients[1], $"/api/patient/units/{u1}/health-records?includeVoided=true");
                var voidedRow = withVoided.GetProperty("exams").EnumerateArray().Single(e => e.GetProperty("id").GetGuid() == examVoided);
                Require(voidedRow.GetProperty("status").GetString() == "voided" && !voidedRow.TryGetProperty("voidReason", out _) && withVoided.GetProperty("vaccines").GetArrayLength() == 6, "O paciente não pode receber o motivo da anulação.");
                var third = await Page(patients[3], $"/api/patient/units/{u1}/health-records?cpf={Cpf(1)}");
                Require(third.GetProperty("exams").GetArrayLength() == 0 && third.GetProperty("vaccines").GetArrayLength() == 0, "O paciente 3 não pode ver registros de outro (o CPF da URL é ignorado).");
                foreach (var denied in new[] { 2, 4 }) using (var response = await patients[denied].GetAsync($"/api/patient/units/{u1}/health-records")) Expect(response, 403);
                using (var anonymousView = await anonymous.GetAsync($"/api/patient/units/{u1}/health-records")) Expect(anonymousView, 401);
                using (var obsView = await obsA.GetAsync($"/api/patient/units/{u1}/health-records")) Expect(obsView, 403);
                var route = $"/api/patient/units/{u1}/health-records";
                foreach (var method in new[] { HttpMethod.Post, HttpMethod.Put, HttpMethod.Patch, HttpMethod.Delete })
                {
                    using var request = new HttpRequestMessage(method, route) { Content = JsonContent.Create(new { name = "Exame", cpf = Cpf(1) }) };
                    using var response = await patients[1].SendAsync(request);
                    Require(response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.MethodNotAllowed, $"{method} pelo paciente deveria ser 403/405, foi {(int)response.StatusCode}.");
                }
                foreach (var path in new[] { "/exams", "/vaccines" }) using (var response = await patients[1].PostAsJsonAsync(route + path, new { name = "Exame", cpf = Cpf(1) })) Require(response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.MethodNotAllowed or HttpStatusCode.NotFound, $"Escrita pelo paciente em {path} deveria falhar, foi {(int)response.StatusCode}.");
                using (var obsRoute = await patients[1].PostAsJsonAsync(Records(u1) + "/exams", new { cpf = Cpf(1), name = "Exame do paciente" })) Expect(obsRoute, 403);
                Require(await UnitScalar<long>(u1, "SELECT count(*) FROM nora_unit.health_exams WHERE name='Exame do paciente' OR name='Exame'") == 0, "Uma escrita do paciente chegou ao banco.");
            });

            await Check("Jornada agregada: uma volta pelas UBS vinculadas (só as do paciente, só os próprios registros); pacientes sem vínculo recebem lista vazia", async () =>
            {
                await NewVaccine(obsB, u3, 6, "Influenza", "single", "2026-05-20");
                await NewExam(obsA, u1, 6, "Glicemia");
                var p6 = await Page(patients[6], "/api/patient/health-records");
                var units = p6.GetProperty("units").EnumerateArray().ToList();
                Require(units.Count == 2 && units.Select(u => u.GetProperty("unitId").GetGuid()).Order().SequenceEqual(new[] { u1, u3 }.Order()), "A Jornada agregada deve cobrir as duas UBS do paciente 6.");
                var centro = units.Single(u => u.GetProperty("unitId").GetGuid() == u1); var norte = units.Single(u => u.GetProperty("unitId").GetGuid() == u3);
                Require(centro.GetProperty("unitName").GetString() == "UBS Centro" && centro.GetProperty("exams").GetArrayLength() == 1 && centro.GetProperty("vaccines").GetArrayLength() == 0, "Registros da UBS Centro incorretos.");
                Require(norte.GetProperty("unitName").GetString() == "UBS Norte Dois" && norte.GetProperty("vaccines").GetArrayLength() == 1 && norte.GetProperty("exams").GetArrayLength() == 0, "Registros da UBS Norte Dois incorretos.");
                var p1 = (await Page(patients[1], "/api/patient/health-records")).GetProperty("units").EnumerateArray().ToList();
                Require(p1.Count == 1 && p1[0].GetProperty("unitId").GetGuid() == u1 && p1[0].GetProperty("exams").GetArrayLength() == 3, "O paciente 1 deve ver só a UBS Centro, com os próprios registros.");
                Require((await Page(patients[4], "/api/patient/health-records")).GetProperty("units").GetArrayLength() == 0, "Paciente sem vínculo deve receber lista vazia.");
                using (var anonymousView = await anonymous.GetAsync("/api/patient/health-records")) Expect(anonymousView, 401);
                using (var obsView = await obsA.GetAsync("/api/patient/health-records")) Expect(obsView, 403);
                using (var post = await patients[1].PostAsJsonAsync("/api/patient/health-records", new { name = "x" })) Require(post.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.MethodNotAllowed, "Escrita na rota agregada deveria falhar.");
            });
        }
        catch (Exception error)
        {
            failures++;
            Console.WriteLine($"FAIL Setup comunicação e Jornada: {error.GetType().Name}: {error.Message}");
        }
        finally
        {
            foreach (var client in clients) client.Dispose();
            await app.StopAsync();
            await using var connection = await database.OpenAsync();
            await database.ExecuteAsync(connection, $"DROP SCHEMA IF EXISTS {schema} CASCADE", default);
        }
        Console.WriteLine($"{checks - failures}/{checks} verificações de comunicação e Jornada passaram.");
        return failures;

        async Task Check(string name, Func<Task> action)
        {
            checks++;
            try { await action(); Console.WriteLine($"PASS [PostgreSQL] {name}"); }
            catch (Exception error) { failures++; Console.WriteLine($"FAIL [PostgreSQL] {name}: {error.Message}"); }
        }
    }

    private static WebApplication Create(string root, string schema, string connection, CheckClock clock) => NoraApiHost.Create(new()
    {
        // Sem verificação periódica de ofertas: nada aqui depende dela, e o relógio de teste não deve ser consultado por tarefas de fundo.
        Args = ["--urls", "http://127.0.0.1:0", "--ConnectionStrings:Nora", connection, "--Nora:Schema", schema, "--Nora:UnitLightningWorker:Enabled", "false", "--Nora:ObsDevices:RequireForAttendants", "false",
            "--Logging:LogLevel:Default", "Warning", "--Logging:LogLevel:Nora.Api.Services.UnitDatabaseProvisioner", "None",
            "--Nora:Registration:HashKey", "synthetic-community-registration-key-32-bytes"],
        EnvironmentName = "Development",
        ApplicationName = typeof(NoraApiHost).Assembly.GetName().Name,
        ContentRootPath = Path.Combine(root, "Nora.Api")
    }, services =>
    {
        services.Replace(ServiceDescriptor.Singleton<TimeProvider>(clock));
        services.Replace(ServiceDescriptor.Singleton<IEmailSender>(new NoMail()));
    });

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
        Require(body.GetProperty("code").GetString() == code && body.GetProperty("status").GetInt32() == status, $"Código de erro esperado '{code}', recebido '{body.GetProperty("code").GetString()}'.");
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
        public void Advance(TimeSpan span) => Interlocked.Add(ref ticks, span.Ticks);
    }
}
