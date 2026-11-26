using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using Nora.Api;
using Nora.Api.Services;

/// <summary>
/// P-042 (2026-11-16): Entrar com Google (vínculo, login, recusas do ID token, desvínculo) e push pelo servidor (aparelhos, quatro eventos, outbox sem repetição, token
/// <c>UNREGISTERED</c> removido, tentativas limitadas, corpo genérico, sem configuração nada é enviado). O Google é o <see cref="FakeGoogle"/>: tokens assinados por uma chave RSA
/// gerada aqui e JWKS, OAuth2 e FCM falsos injetados nos clientes HTTP da API. Reaproveita o fixture da Família (schema próprio, relógio fixo em <see cref="FamilyChecks.T0"/>).
/// </summary>
internal static class GooglePushChecks
{
    private const string ClientId = "nora-web-client.apps.googleusercontent.com";
    private static int checks;
    private static int failures;

    private static async Task Check(string name, Func<Task> action)
    {
        checks++;
        try { await action(); Console.WriteLine($"PASS [Google/push] {name}"); }
        catch (Exception error) { failures++; Console.WriteLine($"FAIL [Google/push] {name}: {error.GetType().Name}: {error.Message}"); }
    }
    private static void Require(bool valid, string message) { if (!valid) throw new InvalidOperationException(message); }
    private static void Expect(HttpResponseMessage response, int status) => Require((int)response.StatusCode == status, $"Esperado HTTP {status}; recebido {(int)response.StatusCode} em {response.RequestMessage?.Method} {response.RequestMessage?.RequestUri?.PathAndQuery}.");
    private static async Task<JsonElement> Read(HttpResponseMessage response) { using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()); return json.RootElement.Clone(); }
    private static async Task<JsonElement> ExpectProblem(HttpResponseMessage response, int status, string code)
    {
        Expect(response, status);
        Require(response.Content.Headers.ContentType?.MediaType == "application/problem+json", "Erro sem application/problem+json.");
        var body = await Read(response);
        Require(body.GetProperty("code").GetString() == code, $"Código esperado '{code}', recebido '{body.GetProperty("code").GetString()}'.");
        return body;
    }
    private static string Cpf(int number) => FamilyChecks.Cpf(number);

    public static async Task<int> RunAsync(string root)
    {
        var connectionString = Environment.GetEnvironmentVariable("NORA_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Console.WriteLine("SKIP Google e push PostgreSQL: configure NORA_TEST_CONNECTION somente para uma base descartável.");
            return 0;
        }
        checks = 0; failures = 0;
        var fake = new FakeGoogle();
        await using var f = await FamilyChecks.Fixture.StartAsync(connectionString,
            ["--Nora:Google:ClientIds", ClientId, "--Nora:Push:ProjectId", FakeGoogle.ProjectId, "--Nora:Push:ServiceAccountJson", fake.ServiceAccountJson(),
             "--Nora:Push:Enabled", "false", "--Nora:Push:MaxAttempts", "3", "--Nora:Push:PresenceScanMinutes", "0",
             // O login pelo Google divide o limite por IP do login (30 por minuto) com o fixture, que já entra com 15 contas: o cliente anônimo do Google usa outro IP de origem.
             "--Nora:ForwardedHeaders:KnownProxies", "127.0.0.1"],
            services =>
            {
                services.AddHttpClient(NoraHttpClients.Google).ConfigurePrimaryHttpMessageHandler(() => fake);
                services.AddHttpClient(NoraHttpClients.Push).ConfigurePrimaryHttpMessageHandler(() => fake);
            });
        var u1 = f.Unit1;
        var push = f.App.Services.GetRequiredService<PushService>();
        using var anonymousGoogle = new HttpClient { BaseAddress = f.Anonymous.BaseAddress };
        anonymousGoogle.DefaultRequestHeaders.Add("X-Forwarded-For", "203.0.113.7");
        var now = () => f.Clock.GetUtcNow();

        string IdToken(string sub, Action<JsonObject>? change = null, RSA? key = null, string kid = FakeGoogle.Kid)
        {
            var payload = new JsonObject { ["iss"] = "https://accounts.google.com", ["aud"] = ClientId, ["sub"] = sub, ["email"] = sub + "@gmail.example", ["email_verified"] = true, ["iat"] = now().ToUnixTimeSeconds(), ["exp"] = now().AddHours(1).ToUnixTimeSeconds() };
            change?.Invoke(payload);
            return FakeGoogle.Jwt(new JsonObject { ["alg"] = "RS256", ["kid"] = kid, ["typ"] = "JWT" }, payload, bytes => (key ?? fake.GoogleKey).SignData(bytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
        }
        Task<HttpResponseMessage> Link(HttpClient client, string token) => client.PostAsJsonAsync("/api/patient/google/link", new { idToken = token });
        Task<HttpResponseMessage> GoogleLogin(HttpClient client, string token) => client.PostAsJsonAsync("/api/auth/google", new { idToken = token, deviceName = "Pixel de teste" });
        async Task<JsonElement> Status(HttpClient client) { using var r = await client.GetAsync("/api/patient/google"); Expect(r, 200); return await Read(r); }
        // A recusa do login pelo Google tem de ser indistinguível de uma senha errada (mesmo status, código, título e texto).
        JsonElement passwordFailure;
        using (var wrong = await f.Anonymous.PostAsJsonAsync("/api/auth/login", new { scope = "patient", identifier = Cpf(21), password = "senha-errada-de-teste" })) { Expect(wrong, 401); passwordFailure = await Read(wrong); }
        async Task ExpectGenericLoginFailure(HttpResponseMessage response, string label)
        {
            Expect(response, 401); var body = await Read(response);
            foreach (var field in new[] { "code", "title", "detail", "type" })
                Require(body.GetProperty(field).GetString() == passwordFailure.GetProperty(field).GetString(), $"{label}: o 401 do Google difere do login com senha errada em '{field}'.");
        }

        await Check("Migração central 018 reaplicada: tabelas do Google, aparelhos, fila de saída e cursores, com a chave única de \"já avisado\"", async () =>
        {
            await f.App.Services.GetRequiredService<Nora.Api.DAOs.MigrationDao>().MigrateAsync();
            Require(await f.Scalar<long>("SELECT count(*) FROM information_schema.tables WHERE table_schema=$1 AND table_name IN ('google_identities','push_devices','push_outbox','push_cursors')", f.Schema) == 4, "Tabelas da migração 018.");
            Require(await f.Scalar<long>("SELECT count(*) FROM pg_indexes WHERE schemaname=$1 AND indexname IN ('push_outbox_event_key_device_id_key','push_outbox_due_idx','push_outbox_done_idx','push_devices_account_idx','family_links_decided_idx','google_identities_subject_key')", f.Schema) == 6, "Índices e chaves únicas da migração 018.");
        });

        // ================= Entrar com Google =================
        await Check("Google: estado inicial, vínculo com ID token válido (204), estado vinculado com o e-mail do Google e login pelo Google com a mesma resposta do login (scope patient)", async () =>
        {
            var initial = await Status(f.P21);
            Require(initial.GetProperty("enabled").GetBoolean() && !initial.GetProperty("linked").GetBoolean() && initial.GetProperty("email").ValueKind == JsonValueKind.Null, $"Estado inicial: {initial}.");
            using (var link = await Link(f.P21, IdToken("sub-21"))) Expect(link, 204);
            var linked = await Status(f.P21);
            Require(linked.GetProperty("linked").GetBoolean() && linked.GetProperty("email").GetString() == "sub-21@gmail.example", $"Estado vinculado: {linked}.");
            using var login = await GoogleLogin(anonymousGoogle, IdToken("sub-21")); Expect(login, 200);
            var body = await Read(login);
            Require(body.GetProperty("user").GetProperty("id").GetGuid() == f.PatientIds[21] && body.GetProperty("user").GetProperty("scope").GetString() == "patient" && body.GetProperty("token").GetString()!.Length == 64 && body.TryGetProperty("expiresAt", out _), $"Resposta do login: {body}.");
            using var session = new HttpClient { BaseAddress = f.Anonymous.BaseAddress };
            session.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body.GetProperty("token").GetString());
            using var me = await session.GetAsync("/api/auth/me"); Expect(me, 200);
            Require((await Read(me)).GetProperty("id").GetGuid() == f.PatientIds[21], "A sessão criada pelo Google é da conta vinculada.");
        });

        await Check("Google (2026-11-26): a sessão vale 24 h por padrão e 30 dias com keepSignedIn, a resposta informa o que foi aplicado, e a opção nunca vale para outra conta nem fora do login", async () =>
        {
            using var keepGoogle = new HttpClient { BaseAddress = f.Anonymous.BaseAddress }; keepGoogle.DefaultRequestHeaders.Add("X-Forwarded-For", "203.0.113.77");
            var moment = f.Clock.GetUtcNow();
            foreach (var (body, keepExpected, label) in new (object Body, bool Keep, string Label)[]
            {
                (new { idToken = IdToken("sub-21") }, false, "sem a opção"),
                (new { idToken = IdToken("sub-21"), keepSignedIn = false }, false, "keepSignedIn falso"),
                (new { idToken = IdToken("sub-21"), keepSignedIn = (bool?)null }, false, "keepSignedIn nulo"),
                (new { idToken = IdToken("sub-21"), keepSignedIn = true, deviceName = "Pixel de teste" }, true, "keepSignedIn verdadeiro")
            })
            {
                using var login = await keepGoogle.PostAsJsonAsync("/api/auth/google", body); Expect(login, 200);
                var json = await Read(login);
                Require(json.GetProperty("keepSignedIn").GetBoolean() == keepExpected && json.GetProperty("expiresAt").GetDateTimeOffset() == (keepExpected ? moment.AddDays(30) : moment.AddHours(24)) && json.GetProperty("device").ValueKind == JsonValueKind.Null,
                    $"Google ({label}): esperava {(keepExpected ? "30 dias" : "24 h")} e keepSignedIn {keepExpected}; recebido {json}.");
            }
            // A sessão longa do Google vale no dia 29 e vence no 30; a curta vence em 24 h.
            using var shortLogin = await keepGoogle.PostAsJsonAsync("/api/auth/google", new { idToken = IdToken("sub-21") }); Expect(shortLogin, 200);
            using var longResponse = await keepGoogle.PostAsJsonAsync("/api/auth/google", new { idToken = IdToken("sub-21"), keepSignedIn = true }); Expect(longResponse, 200);
            var shortToken = (await Read(shortLogin)).GetProperty("token").GetString(); var longToken = (await Read(longResponse)).GetProperty("token").GetString();
            async Task<int> Me(string? token) { using var client = new HttpClient { BaseAddress = f.Anonymous.BaseAddress }; client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token); using var response = await client.GetAsync("/api/auth/me"); return (int)response.StatusCode; }
            try
            {
                f.Clock.Set(moment.AddHours(24));
                Require(await Me(shortToken) == 401 && await Me(longToken) == 200, "Às 24 h a sessão do Google sem a opção vence e a longa continua.");
                f.Clock.Set(moment.AddDays(30));
                Require(await Me(longToken) == 401, "Aos 30 dias a sessão longa do Google vence.");
            }
            finally { f.Clock.Set(moment); }
            // O vínculo ignora a opção (não abre sessão) e o token inválido continua recusado com ou sem ela.
            using (var link = await f.P24.PostAsJsonAsync("/api/patient/google/link", new { idToken = IdToken("sub-24-opcao"), keepSignedIn = true })) Expect(link, 204);
            using (var bad = await keepGoogle.PostAsJsonAsync("/api/auth/google", new { idToken = "token-invalido", keepSignedIn = true })) Expect(bad, 401);
            using (var wrongType = await keepGoogle.PostAsJsonAsync("/api/auth/google", new { idToken = IdToken("sub-21"), keepSignedIn = "sim" })) Expect(wrongType, 400);
            using (var unlink = await f.P24.PostAsync("/api/patient/google/unlink", null)) Expect(unlink, 204);
        });

        await Check("Google: ID tokens recusados no vínculo (400) e no login (401 igual à senha errada): alg none, HS256 com a chave pública, kid desconhecido, aud errado, iss errado, expirado além da folga, e-mail não verificado, sem sub e lixo; dentro da folga de 60 s vale", async () =>
        {
            var publicPem = System.Text.Encoding.ASCII.GetBytes(fake.GoogleKey.ExportSubjectPublicKeyInfoPem());
            using var otherKey = RSA.Create(2048);
            var claims = new JsonObject { ["iss"] = "https://accounts.google.com", ["aud"] = ClientId, ["sub"] = "sub-24", ["email_verified"] = true, ["exp"] = now().AddHours(1).ToUnixTimeSeconds() };
            var bad = new (string Label, string Token)[]
            {
                ("alg none", FakeGoogle.Jwt(new JsonObject { ["alg"] = "none", ["kid"] = FakeGoogle.Kid }, (JsonObject)claims.DeepClone(), _ => [])),
                ("HS256", FakeGoogle.Jwt(new JsonObject { ["alg"] = "HS256", ["kid"] = FakeGoogle.Kid }, (JsonObject)claims.DeepClone(), bytes => HMACSHA256.HashData(publicPem, bytes))),
                ("kid desconhecido", IdToken("sub-24", key: otherKey, kid: "kid-desconhecido")),
                ("chave errada com o kid certo", IdToken("sub-24", key: otherKey)),
                ("aud errado", IdToken("sub-24", p => p["aud"] = "outro-cliente.apps.googleusercontent.com")),
                ("iss errado", IdToken("sub-24", p => p["iss"] = "https://accounts.example.com")),
                ("expirado", IdToken("sub-24", p => { p["iat"] = now().AddHours(-2).ToUnixTimeSeconds(); p["exp"] = now().AddSeconds(-61).ToUnixTimeSeconds(); })),
                ("e-mail não verificado", IdToken("sub-24", p => p["email_verified"] = false)),
                ("sem sub", IdToken("", p => p.Remove("sub"))),
                ("lixo", "isto.nao.e-um-jwt")
            };
            foreach (var (label, token) in bad)
            {
                using (var link = await Link(f.P24, token)) { Require((int)link.StatusCode == 400, $"{label}: vínculo devia ser 400, recebido {(int)link.StatusCode}."); }
                using (var login = await GoogleLogin(anonymousGoogle, token)) await ExpectGenericLoginFailure(login, label);
            }
            Require(!(await Status(f.P24)).GetProperty("linked").GetBoolean(), "Nenhum token recusado vinculou a conta.");
            using (var withinSkew = await Link(f.P24, IdToken("sub-24", p => p["exp"] = now().AddSeconds(-30).ToUnixTimeSeconds()))) Expect(withinSkew, 204);
        });

        await Check("Google: sub já vinculado a outra conta → 409 google-already-linked; revincular o mesmo sub na mesma conta é idempotente (204)", async () =>
        {
            using (var taken = await Link(f.P51, IdToken("sub-21"))) await ExpectProblem(taken, 409, "google-already-linked");
            Require(!(await Status(f.P51)).GetProperty("linked").GetBoolean(), "A outra conta continua sem vínculo.");
            using (var again = await Link(f.P21, IdToken("sub-21"))) Expect(again, 204);
        });

        await Check("Google: login com sub sem vínculo e com conta inativa → 401 igual à senha errada; conta com troca de senha pendente entra com mustChangePassword e as rotas pedem a troca", async () =>
        {
            using (var unknown = await GoogleLogin(anonymousGoogle, IdToken("sub-sem-vinculo"))) await ExpectGenericLoginFailure(unknown, "sub sem vínculo");
            using (var link = await Link(f.P51, IdToken("sub-51"))) Expect(link, 204);
            await f.Exec("UPDATE nora.accounts SET is_active=false WHERE id=$1", f.PatientIds[51]);
            try { using var inactive = await GoogleLogin(anonymousGoogle, IdToken("sub-51")); await ExpectGenericLoginFailure(inactive, "conta inativa"); }
            finally { await f.Exec("UPDATE nora.accounts SET is_active=true WHERE id=$1", f.PatientIds[51]); }
            await f.Exec("UPDATE nora.accounts SET must_change_password=true WHERE id=$1", f.PatientIds[24]);
            try
            {
                using var login = await GoogleLogin(anonymousGoogle, IdToken("sub-24")); Expect(login, 200);
                var body = await Read(login);
                Require(body.GetProperty("user").GetProperty("mustChangePassword").GetBoolean(), "A troca obrigatória aparece no login pelo Google.");
                using var session = new HttpClient { BaseAddress = f.Anonymous.BaseAddress };
                session.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body.GetProperty("token").GetString());
                using var blocked = await session.GetAsync("/api/patient/google"); await ExpectProblem(blocked, 403, "password-change-required");
            }
            finally { await f.Exec("UPDATE nora.accounts SET must_change_password=false WHERE id=$1", f.PatientIds[24]); }
        });

        await Check("Google: desvincular é idempotente (204, 204), depois o login pelo Google é 401; equipe 403 e anônimo 401 nas rotas do paciente; o JWKS foi buscado uma vez (cache pelo max-age) e de novo depois que ele venceu", async () =>
        {
            using (var unlink = await f.P21.PostAsync("/api/patient/google/unlink", null)) Expect(unlink, 204);
            using (var again = await f.P21.PostAsync("/api/patient/google/unlink", null)) Expect(again, 204);
            Require(!(await Status(f.P21)).GetProperty("linked").GetBoolean(), "Desvinculado.");
            using (var login = await GoogleLogin(anonymousGoogle, IdToken("sub-21"))) await ExpectGenericLoginFailure(login, "depois do desvínculo");
            using (var staff = await f.Admin.GetAsync("/api/patient/google")) Expect(staff, 403);
            using (var anonymous = await f.Anonymous.GetAsync("/api/patient/google")) Expect(anonymous, 401);
            Require(fake.JwksFetches == 1, $"O JWKS devia ser buscado uma vez e servido do cache (max-age 1 h); buscas: {fake.JwksFetches}.");
            f.Clock.Set(FamilyChecks.T0.AddMinutes(61));
            try { using var later = await GoogleLogin(anonymousGoogle, IdToken("sub-24")); Expect(later, 200); }
            finally { f.Clock.Set(FamilyChecks.T0); }
            Require(fake.JwksFetches == 2, $"Vencido o max-age, o JWKS é buscado de novo; buscas: {fake.JwksFetches}.");
        });

        // ================= Aparelhos =================
        async Task Register(HttpClient client, string token) { using var r = await client.PostAsJsonAsync("/api/patient/devices", new { token, platform = "android" }); Expect(r, 204); }
        async Task<Guid?> Owner(string token) => await f.Scalar<Guid?>("SELECT account_id FROM $schema.push_devices WHERE token=$1", token);
        await Check("Aparelhos: registrar (204), validações (plataforma, token vazio, com espaço ou longo → 400), estado do push, perfis; o token passa a ser da última conta; teto de 10 por conta; remover só o próprio e de forma idempotente", async () =>
        {
            await Register(f.P21, "tok-p21");
            Require(await Owner("tok-p21") == f.PatientIds[21], "O aparelho é da conta que o registrou.");
            foreach (var body in new object[] { new { token = "tok-x", platform = "ios" }, new { token = "", platform = "android" }, new { token = "tok com espaco", platform = "android" }, new { token = new string('a', 4097), platform = "android" }, new { token = "tok-y" } })
            { using var invalid = await f.P21.PostAsJsonAsync("/api/patient/devices", body); Expect(invalid, 400); }
            using (var state = await f.P21.GetAsync("/api/patient/devices/push")) { Expect(state, 200); Require((await Read(state)).GetProperty("enabled").GetBoolean(), "Push configurado no servidor."); }
            using (var staff = await f.Admin.PostAsJsonAsync("/api/patient/devices", new { token = "tok-staff", platform = "android" })) Expect(staff, 403);
            using (var anonymous = await f.Anonymous.PostAsJsonAsync("/api/patient/devices", new { token = "tok-anon", platform = "android" })) Expect(anonymous, 401);
            await Register(f.P24, "tok-p21");
            Require(await Owner("tok-p21") == f.PatientIds[24], "O token passa para a última conta que o registrou.");
            await Register(f.P21, "tok-p21");
            Require(await Owner("tok-p21") == f.PatientIds[21], "E volta quando a primeira o registra de novo.");
            try
            {
                for (var i = 0; i <= 10; i++) { f.Clock.Set(FamilyChecks.T0.AddSeconds(i + 1)); await Register(f.P51, $"tok-51-{i}"); }
            }
            finally { f.Clock.Set(FamilyChecks.T0); }
            Require(await f.Scalar<long>("SELECT count(*) FROM $schema.push_devices WHERE account_id=$1", f.PatientIds[51]) == 10 && await Owner("tok-51-0") is null, "Teto de 10 aparelhos: o mais antigo sai.");
            using (var foreign = await f.P21.PostAsJsonAsync("/api/patient/devices/remove", new { token = "tok-51-1" })) Expect(foreign, 204);
            Require(await Owner("tok-51-1") == f.PatientIds[51], "Remover o token de outra conta não faz nada.");
            for (var i = 1; i <= 10; i++) { using var remove = await f.P51.PostAsJsonAsync("/api/patient/devices/remove", new { token = $"tok-51-{i}" }); Expect(remove, 204); }
            using (var repeat = await f.P51.PostAsJsonAsync("/api/patient/devices/remove", new { token = "tok-51-1" })) Expect(repeat, 204);
            Require(await f.Scalar<long>("SELECT count(*) FROM $schema.push_devices WHERE account_id=$1", f.PatientIds[51]) == 0, "Removidos.");
        });

        // ================= Eventos =================
        Guid g2Link = Guid.Empty, g3Link = Guid.Empty;
        async Task<Guid> RequestLink(HttpClient guardian, int dependent)
        {
            using var response = await guardian.PostAsJsonAsync("/api/patient/dependents", new { cpf = Cpf(dependent), unitId = u1 }); Expect(response, 201);
            return (await Read(response)).GetProperty("id").GetGuid();
        }
        async Task<Guid> StaffAppointment(int cpf, string professional, DateTimeOffset start)
        {
            using var created = await f.Attendant.PostAsJsonAsync($"/api/obs/units/{u1}/appointments", new { cpf = Cpf(cpf), specialty = "Ortopedia", professional, startsAt = start, endsAt = start.AddMinutes(30) });
            Expect(created, 201); return (await Read(created)).GetProperty("id").GetGuid();
        }
        // Mensagens enviadas no passe, como (token, tipo, unitId, dependentId, appointmentId), em ordem estável.
        async Task<List<string>> Pass()
        {
            var before = fake.Count;
            await push.RunOnceAsync(default);
            return fake.Sent(before).Select(m =>
            {
                var data = m.Message.GetProperty("data");
                string? Field(string name) => data.TryGetProperty(name, out var value) ? value.GetString() : null;
                return $"{m.Token}|{Field("type")}|{Field("unitId")}|{Field("dependentId")}|{Field("appointmentId")}";
            }).Order(StringComparer.Ordinal).ToList();
        }
        void Same(List<string> actual, IEnumerable<string> expected, string label)
        {
            var wanted = expected.Order(StringComparer.Ordinal).ToList();
            Require(actual.SequenceEqual(wanted), $"{label}: esperado [{string.Join("; ", wanted)}]; enviado [{string.Join("; ", actual)}].");
        }
        await Check("Push — pedido de família decidido: aprovado e recusado avisam o responsável em cada aparelho (dependentId = vínculo), pendente não avisa; o passe seguinte não repete", async () =>
        {
            foreach (var (client, token) in new[] { (f.G2, "tok-g2a"), (f.G2, "tok-g2b"), (f.G3, "tok-g3"), (f.G4, "tok-g4"), (f.P24, "tok-p24") }) await Register(client, token);
            await Register(f.P21, "tok-p21");
            g2Link = await RequestLink(f.G2, 11);
            using (var approve = await f.Attendant.PostAsJsonAsync($"/api/obs/units/{u1}/family-links/{g2Link}/approve", new { confirmed = true })) Expect(approve, 200);
            g3Link = await RequestLink(f.G3, 13);
            using (var reject = await f.Attendant.PostAsJsonAsync($"/api/obs/units/{u1}/family-links/{g3Link}/reject", new { confirmed = true, reason = "Documento ilegível" })) Expect(reject, 200);
            await RequestLink(f.G4, 14); // fica pendente
            Same(await Pass(), [$"tok-g2a|family-decision|{u1}|{g2Link}|", $"tok-g2b|family-decision|{u1}|{g2Link}|", $"tok-g3|family-decision|{u1}|{g3Link}|"], "Primeiro passe");
            Same(await Pass(), [], "Passe seguinte");
        });

        await Check("Push — Vaga Relâmpago nova: a oferta do paciente avisa os aparelhos dele e a do dependente sem conta avisa os do responsável (com dependentId); o passe seguinte não repete", async () =>
        {
            // Candidatos por consulta posterior de Ortopedia (fila manual removida em 2026-11-24).
            foreach (var cpf in new[] { 11, 21 }) await f.LaterAsync(u1, cpf, "Ortopedia");
            foreach (var start in new[] { FamilyChecks.T0.AddDays(1), FamilyChecks.T0.AddDays(1).AddHours(1) })
            {
                var source = await StaffAppointment(31, "Dr. Bruno Lima", start);
                using (var cancelled = await f.Attendant.PatchAsJsonAsync($"/api/obs/units/{u1}/appointments/{source}/status", new { status = "cancelled", confirmed = true })) Expect(cancelled, 200);
                using (var slot = await f.Attendant.PostAsJsonAsync($"/api/obs/units/{u1}/lightning-slots", new { appointmentId = source, confirmed = true })) Expect(slot, 201);
            }
            Require(await f.UnitScalar<long>(u1, "SELECT count(*) FROM nora_unit.lightning_offers WHERE status='pending'") == 2, "Pré-condição: duas ofertas pendentes.");
            Same(await Pass(), [$"tok-g2a|lightning-offer|{u1}|{g2Link}|", $"tok-g2b|lightning-offer|{u1}|{g2Link}|", $"tok-p21|lightning-offer|{u1}||"], "Primeiro passe");
            Same(await Pass(), [], "Passe seguinte");
        });

        await Check("Push — janela de confirmação de presença: consulta scheduled sem confirmação entre 2 h e 48 h à frente avisa o paciente e o responsável do dependente (appointmentId); confirmada, fora da janela ou sem conta não avisa; não repete; avisa quando a janela abre depois", async () =>
        {
            var own = await StaffAppointment(24, "Dra. Clara Dias", FamilyChecks.T0.AddHours(30));
            var confirmed = await StaffAppointment(24, "Dra. Clara Dias", FamilyChecks.T0.AddHours(31));
            using (var confirm = await f.P24.PostAsync($"/api/patient/units/{u1}/appointments/{confirmed}/confirm", null)) Expect(confirm, 200);
            var dependent = await StaffAppointment(11, "Dra. Clara Dias", FamilyChecks.T0.AddHours(40));
            await StaffAppointment(31, "Dra. Clara Dias", FamilyChecks.T0.AddHours(32)); // sem conta nem responsável
            await StaffAppointment(21, "Dra. Clara Dias", FamilyChecks.T0.AddMinutes(90)); // a menos de 2 h: a confirmação já fechou
            var later = await StaffAppointment(21, "Dra. Clara Dias", FamilyChecks.T0.AddHours(72));
            Same(await Pass(), [$"tok-p24|presence-window|{u1}||{own}", $"tok-g2a|presence-window|{u1}|{g2Link}|{dependent}", $"tok-g2b|presence-window|{u1}|{g2Link}|{dependent}"], "Primeiro passe");
            Same(await Pass(), [], "Passe seguinte");
            f.Clock.Set(FamilyChecks.T0.AddHours(25));
            try { Same(await Pass(), [$"tok-p21|presence-window|{u1}||{later}"], "Quando a janela da consulta de 72 h abre"); }
            finally { f.Clock.Set(FamilyChecks.T0); }
        });

        await Check("Push — comunicado novo para pacientes: avisa os aparelhos dos pacientes alcançados (unitId da UBS deles), não os de quem não tem vínculo; comunicado só da equipe não avisa; não repete", async () =>
        {
            using (var staffOnly = await f.Municipal.PostAsJsonAsync("/api/municipal/announcements", new { title = "Reunião da equipe", body = "Texto interno", audience = "staff", scope = "all" })) Expect(staffOnly, 201);
            using (var patients = await f.Municipal.PostAsJsonAsync("/api/municipal/announcements", new { title = "Campanha de vacinação", body = "Texto do aviso aos pacientes", audience = "patients", scope = "units", unitIds = new[] { u1 } })) Expect(patients, 201);
            Same(await Pass(), [$"tok-p21|announcement|{u1}||", $"tok-p24|announcement|{u1}||"], "Primeiro passe");
            Same(await Pass(), [], "Passe seguinte");
        });

        await Check("Push — FCM responde UNREGISTERED: o aparelho é removido na hora e não recebe outra tentativa", async () =>
        {
            await Register(f.P21, "tok-p21-velho");
            fake.SetMode("tok-p21-velho", "unregistered");
            using (var notice = await f.Municipal.PostAsJsonAsync("/api/municipal/announcements", new { title = "Aviso geral", body = "Outro aviso", audience = "both", scope = "all" })) Expect(notice, 201);
            Same(await Pass(), [$"tok-p21|announcement|{u1}||", $"tok-p24|announcement|{u1}||"], "Envios aceitos");
            // O aparelho novo também recebe o aviso da oferta ainda pendente do P21: no mesmo passe pode haver mais de uma tentativa, todas recusadas.
            var attempts = fake.AttemptsFor("tok-p21-velho");
            Require(attempts >= 1 && await Owner("tok-p21-velho") is null, $"O token UNREGISTERED devia ser removido no primeiro passe: {attempts} tentativas.");
            Require(await f.Scalar<long>("SELECT count(*) FROM $schema.push_outbox o LEFT JOIN $schema.push_devices d ON d.id=o.device_id WHERE d.id IS NULL") == 0, "Nada fica na fila para o aparelho removido.");
            await Pass();
            Require(fake.AttemptsFor("tok-p21-velho") == attempts, "Sem nova tentativa ao aparelho removido.");
        });

        await Check("Push — falha transitória (503): tenta de novo só depois da espera e até o teto (3 tentativas); quem volta a responder recebe uma vez; o aparelho que esgotou não é removido", async () =>
        {
            fake.SetMode("tok-g3", "fail:2"); fake.SetMode("tok-g4", "fail");
            var g3Second = await RequestLink(f.G3, 41);
            using (var reject = await f.Attendant.PostAsJsonAsync($"/api/obs/units/{u1}/family-links/{g3Second}/reject", new { confirmed = true })) Expect(reject, 200);
            var g4Pending = await f.Scalar<Guid>("SELECT id FROM $schema.family_links WHERE guardian_id=$1 AND status='pending'", f.PatientIds[4]);
            using (var reject = await f.Attendant.PostAsJsonAsync($"/api/obs/units/{u1}/family-links/{g4Pending}/reject", new { confirmed = true })) Expect(reject, 200);
            var g3Before = fake.AttemptsFor("tok-g3"); var g4Before = fake.AttemptsFor("tok-g4");
            int G3() => fake.AttemptsFor("tok-g3") - g3Before; int G4() => fake.AttemptsFor("tok-g4") - g4Before;
            try
            {
                Same(await Pass(), [], "Primeira tentativa falha");
                Require(G3() == 1 && G4() == 1, $"Uma tentativa cada: {G3()}/{G4()}.");
                await Pass();
                Require(G3() == 1 && G4() == 1, "Antes da espera não há nova tentativa.");
                f.Clock.Set(FamilyChecks.T0.AddMinutes(2));
                Same(await Pass(), [], "Segunda tentativa falha");
                Require(G3() == 2 && G4() == 2, $"Duas tentativas cada: {G3()}/{G4()}.");
                f.Clock.Set(FamilyChecks.T0.AddMinutes(5));
                Same(await Pass(), [$"tok-g3|family-decision|{u1}|{g3Second}|"], "Terceira tentativa: o G3 recebe");
                Require(G3() == 3 && G4() == 3, $"Três tentativas cada: {G3()}/{G4()}.");
                f.Clock.Set(FamilyChecks.T0.AddMinutes(60));
                await Pass();
                Require(G3() == 3 && G4() == 3, "Teto de 3 tentativas: nenhuma a mais.");
            }
            finally { f.Clock.Set(FamilyChecks.T0); }
            Require(await f.Scalar<long>("SELECT count(*) FROM $schema.push_outbox o JOIN $schema.push_devices d ON d.id=o.device_id WHERE d.token='tok-g4' AND o.abandoned_at IS NOT NULL AND o.attempts=3") == 1, "A mensagem que esgotou fica marcada como abandonada.");
            Require(await Owner("tok-g4") == f.PatientIds[4], "Falha transitória não remove o aparelho.");
        });

        await Check("Push — privacidade e credenciais: nenhuma mensagem leva nome, CPF, e-mail, especialidade, profissional ou texto de comunicado; corpo e título genéricos; data só com type, unitId, dependentId e appointmentId; token OAuth2 em cache até perto de vencer com asserção RS256 válida", async () =>
        {
            List<(string Token, string Json, int Status)> all; lock (fake.Messages) all = [.. fake.Messages];
            Require(all.Count > 0, "Pré-condição: houve envios.");
            var forbidden = new List<string> { "Dependente", "Responsável", "Paciente", "Onze", "Ortopedia", "Bruno", "Clara", "Campanha", "Texto do aviso", "Outro aviso", "example.test", "gmail" };
            forbidden.AddRange(new[] { 1, 2, 3, 4, 11, 12, 13, 14, 21, 24, 31, 41, 51 }.Select(Cpf));
            var bodies = new HashSet<string>(StringComparer.Ordinal);
            foreach (var (_, json, _) in all)
            {
                foreach (var word in forbidden) Require(!json.Contains(word, StringComparison.OrdinalIgnoreCase), $"Mensagem com dado pessoal ou clínico ('{word}'): {json}");
                var message = JsonDocument.Parse(json).RootElement.GetProperty("message");
                Require(message.GetProperty("notification").GetProperty("title").GetString() == "NORA", "Título genérico.");
                bodies.Add(message.GetProperty("notification").GetProperty("body").GetString()!);
                Require(message.GetProperty("data").EnumerateObject().All(p => p.Name is "type" or "unitId" or "dependentId" or "appointmentId" && p.Value.ValueKind == JsonValueKind.String), $"Campos de data: {message.GetProperty("data")}.");
            }
            Require(bodies.All(b => b.EndsWith("Abra o NORA.", StringComparison.Ordinal)) && bodies.Count == 4, $"Quatro textos genéricos, um por evento: {string.Join(" | ", bodies)}.");
            // O token de acesso (3599 s) fica em cache até um minuto antes de vencer: um pedido em T0 e outro quando o relógio do teste pulou para T0+25 h.
            Require(fake.TokenRequests == 2 && fake.AssertionErrors == 0, $"Token de acesso em cache e asserção válida: {fake.TokenRequests} pedidos, {fake.AssertionErrors} asserções recusadas.");
        });

        // ================= Sem configuração =================
        await Check("Sem configuração (outra API no mesmo banco, sem chaves Google e push): estado desligado, vínculo e login pelo Google → 503 google-disabled, desvínculo 204, aparelho aceito, e o passe do push não envia nem enfileira nada", async () =>
        {
            var builder = new NpgsqlConnectionStringBuilder(f.ConnectionString) { MaxPoolSize = 2 };
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Nora.Api", "Nora.Api.csproj"))) directory = directory.Parent;
            await using var off = NoraApiHost.Create(new WebApplicationOptions
            {
                Args = ["--urls", "http://127.0.0.1:0", "--ConnectionStrings:Nora", builder.ConnectionString, "--Nora:Schema", f.Schema, "--Nora:UnitLightningWorker:Enabled", "false", "--Nora:Maintenance:Enabled", "false", "--Nora:UnitDatabases:MaxPoolSize", "1", "--Logging:LogLevel:Default", "Warning"],
                EnvironmentName = "Development", ApplicationName = typeof(NoraApiHost).Assembly.GetName().Name, ContentRootPath = Path.Combine(directory!.FullName, "Nora.Api")
            }, services =>
            {
                services.Replace(ServiceDescriptor.Singleton<TimeProvider>(f.Clock));
                services.AddHttpClient(NoraHttpClients.Google).ConfigurePrimaryHttpMessageHandler(() => fake);
                services.AddHttpClient(NoraHttpClients.Push).ConfigurePrimaryHttpMessageHandler(() => fake);
            });
            await off.StartAsync();
            try
            {
                var address = new Uri(off.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single());
                using var patient = new HttpClient { BaseAddress = address }; patient.DefaultRequestHeaders.Authorization = f.P24.DefaultRequestHeaders.Authorization;
                using var anonymous = new HttpClient { BaseAddress = address };
                var state = await Status(patient);
                Require(!state.GetProperty("enabled").GetBoolean(), $"Google desligado: {state}.");
                using (var link = await Link(patient, IdToken("sub-24"))) await ExpectProblem(link, 503, "google-disabled");
                using (var login = await GoogleLogin(anonymous, IdToken("sub-24"))) await ExpectProblem(login, 503, "google-disabled");
                using (var unlink = await patient.PostAsync("/api/patient/google/unlink", null)) Expect(unlink, 204);
                using (var pushState = await patient.GetAsync("/api/patient/devices/push")) { Expect(pushState, 200); Require(!(await Read(pushState)).GetProperty("enabled").GetBoolean(), "Push desligado."); }
                await Register(patient, "tok-sem-config");
                using (var notice = await f.Municipal.PostAsJsonAsync("/api/municipal/announcements", new { title = "Aviso sem push", body = "Sem push", audience = "patients", scope = "all" })) Expect(notice, 201);
                var requests = fake.Requests; var outbox = await f.Scalar<long>("SELECT count(*) FROM $schema.push_outbox");
                var result = await off.Services.GetRequiredService<PushService>().RunOnceAsync(default);
                Require(result == new PushRunResult(0, 0, 0, 0, 0) && fake.Requests == requests && await f.Scalar<long>("SELECT count(*) FROM $schema.push_outbox") == outbox, $"Sem configuração nada é enviado nem enfileirado: {result}, {fake.Requests - requests} requisições.");
            }
            finally { await off.StopAsync(); }
        });

        Console.WriteLine($"{checks - failures}/{checks} verificações de Google e push passaram.");
        return failures;
    }
}
