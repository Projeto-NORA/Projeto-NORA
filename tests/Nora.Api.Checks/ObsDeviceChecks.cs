using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Nora.Api.Services;
using static RoundFixture;

/// <summary>
/// Computadores do OBS (P-014): ativação pelo administrador da UBS (neste computador ou por código), "manter conectado", exigência de computador ativado para a atendente, revogação, UBS do computador,
/// concorrência, limites e auditoria. Roda com <c>--devices-only</c>; precisa de <c>NORA_TEST_CONNECTION</c> (cluster descartável).
/// </summary>
internal static partial class ObsDeviceChecks
{
    private static readonly DateTimeOffset T0 = RoundClock.T0;

    public static async Task<int> RunAsync(string root)
    {
        var connection = Environment.GetEnvironmentVariable("NORA_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection))
        {
            Console.WriteLine("SKIP computadores do OBS: configure NORA_TEST_CONNECTION somente para uma base descartável.");
            return 0;
        }
        var suite = new Suite("computadores do OBS");
        await using var f = await StartAsync(connection, "devices");
        var u1 = f.Unit1; var u2 = f.Unit2;

        string Url(Guid unit, string suffix = "") => $"/api/obs/units/{unit}/devices{suffix}";
        async Task<JsonElement> Create(HttpClient admin, Guid unit, string name, bool activateThis)
        {
            using var response = await admin.PostAsJsonAsync(Url(unit), new { name, activateThisComputer = activateThis });
            Expect(response, 201); return await Read(response);
        }
        async Task<(Guid Id, string Header)> ActivateThis(HttpClient admin, Guid unit, string name)
        {
            var body = await Create(admin, unit, name, true);
            var credential = body.GetProperty("credential");
            var id = credential.GetProperty("deviceId").GetGuid();
            return (id, ObsDeviceHelper.HeaderValue(id, credential.GetProperty("secret").GetString()!));
        }
        async Task<HttpResponseMessage> Activate(string? code) => await f.Anonymous.PostAsJsonAsync("/api/obs/devices/activate", new { code });
        async Task<HttpResponseMessage> Current(string? header)
        {
            using var client = f.NewClient(); ObsDeviceHelper.Send(client, header);
            return await client.GetAsync("/api/obs/devices/current");
        }
        async Task<HttpResponseMessage> Me(string token, string? header)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
            request.Headers.Authorization = new("Bearer", token);
            if (header is not null) request.Headers.Add(ObsDeviceHelper.HeaderName, header);
            return await f.Anonymous.SendAsync(request);
        }
        string Token(HttpClient client) => client.DefaultRequestHeaders.Authorization!.Parameter!;
        static string Sha(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

        // ======== 1. Entrada sem computador ========
        await suite.Check("Administrador entra sem computador (8 h, device nulo); atendente sem cabeçalho → 403 device-required; com a exigência desligada entra", async () =>
        {
            var admin = await f.LoginAsync(f.NewClient(), "obs", "admin@example.test");
            Ensure(admin.GetProperty("device").ValueKind == JsonValueKind.Null && !admin.GetProperty("keepSignedIn").GetBoolean() && admin.GetProperty("expiresAt").GetDateTimeOffset() == T0.AddHours(8), "Login do administrador sem computador: 8 h, device nulo.");
            using (var attendant = await f.Anonymous.PostAsJsonAsync("/api/auth/login", new { scope = "obs", identifier = "atendente@example.test", password = Password })) await ExpectProblem(attendant, 403, "device-required");
            using (var wrongPassword = await f.Anonymous.PostAsJsonAsync("/api/auth/login", new { scope = "obs", identifier = "atendente@example.test", password = "errada-1234" })) Expect(wrongPassword, 401);
            await using var relaxed = await StartAsync(connection, "devices_off", unitCount: 0, extraArgs: ["--Nora:ObsDevices:RequireForAttendants", "false"]);
            var body = await relaxed.LoginAsync(relaxed.NewClient(), "obs", "atendente@example.test", keepSignedIn: true);
            Ensure(body.GetProperty("device").ValueKind == JsonValueKind.Null && !body.GetProperty("keepSignedIn").GetBoolean() && body.GetProperty("expiresAt").GetDateTimeOffset() == T0.AddHours(8), "Exigência desligada: entra sem computador e o manter conectado é ignorado.");
            relaxed.Anonymous.DefaultRequestHeaders.Authorization = new("Bearer", body.GetProperty("token").GetString()!);
            using var me = await relaxed.Anonymous.GetAsync("/api/auth/me"); Expect(me, 200);
        });

        // ======== 2. Ativar este computador ========
        await suite.Check("Ativar este computador: 201 com o segredo uma única vez, só o hash no banco; repetir com o cabeçalho → 409 device-already-active; nome e corpo validados; lista com isThisDevice", async () =>
        {
            var (admin, _) = await f.StaffAsync("administrator", 11, [u1, u2]);
            var body = await Create(admin, u1, "  Recepção   1 ", true);
            var device = body.GetProperty("device"); var secret = body.GetProperty("credential").GetProperty("secret").GetString()!; var id = body.GetProperty("credential").GetProperty("deviceId").GetGuid();
            Ensure(device.GetProperty("id").GetGuid() == id && device.GetProperty("name").GetString() == "Recepção 1" && device.GetProperty("status").GetString() == "active" && secret.Length == 64 && secret.All(c => Uri.IsHexDigit(c) && !char.IsLower(c))
                && device.GetProperty("createdByName").GetString() == "Equipe 11" && device.GetProperty("codeExpiresAt").ValueKind == JsonValueKind.Null && !device.GetProperty("isThisDevice").GetBoolean(), "Resposta da criação ativada.");
            var row = await f.Scalar<string>("SELECT row_to_json(d)::text FROM nora.obs_devices d WHERE d.id=$1", id);
            var events = await f.Scalar<string>("SELECT string_agg(row_to_json(e)::text,'') FROM nora.obs_device_events e WHERE e.device_id=$1", id);
            Ensure(!row.Contains(secret, StringComparison.OrdinalIgnoreCase) && !events.Contains(secret, StringComparison.OrdinalIgnoreCase) && row.Contains(Sha(secret), StringComparison.OrdinalIgnoreCase), "O banco guarda só o hash do segredo.");
            var header = ObsDeviceHelper.HeaderValue(id, secret);
            using var withDevice = f.NewClient(); await f.LoginAsync(withDevice, "obs", "equipe11@example.test", header);
            using (var again = await withDevice.PostAsJsonAsync(Url(u1), new { name = "Outro", activateThisComputer = true })) await ExpectProblem(again, 409, "device-already-active");
            using (var withCode = await withDevice.PostAsJsonAsync(Url(u1), new { name = "Para outro", activateThisComputer = false })) Expect(withCode, 201);
            foreach (var invalid in new object[] { new { name = "", activateThisComputer = true }, new { name = new string('x', 61), activateThisComputer = true }, new { name = "Nome\u0007Controle", activateThisComputer = false }, new { name = "Sem opção" } })
            { using var response = await admin.PostAsJsonAsync(Url(u1), invalid); Expect(response, 400); }
            using (var list = await withDevice.GetAsync(Url(u1)))
            {
                Expect(list, 200); var items = (await Read(list)).GetProperty("devices").EnumerateArray().ToList();
                Ensure(items.Count == 2 && items[0].GetProperty("status").GetString() == "active" && items[0].GetProperty("id").GetGuid() == id && items[0].GetProperty("isThisDevice").GetBoolean() && items[1].GetProperty("status").GetString() == "pending" && !items[1].GetProperty("isThisDevice").GetBoolean()
                    && items[1].GetProperty("codeExpiresAt").GetDateTimeOffset() == T0.AddMinutes(15), "Lista: ativos antes dos pendentes, isThisDevice só do computador da requisição, código vigente do pendente.");
            }
            using (var listOther = await admin.GetAsync(Url(u1))) Ensure(!(await Read(listOther)).GetProperty("devices").EnumerateArray().Any(item => item.GetProperty("isThisDevice").GetBoolean()), "Sem cabeçalho nenhum computador é 'este'.");
            using (var current = await Current(header)) { Expect(current, 200); var info = await Read(current); Ensure(info.GetProperty("id").GetGuid() == id && info.GetProperty("name").GetString() == "Recepção 1" && info.GetProperty("unitId").GetGuid() == u1 && info.GetProperty("unitName").GetString() == "UBS da rodada 1", "GET devices/current."); }
        });

        // ======== 3. Código de ativação ========
        Guid codeDevice = Guid.Empty; string codeDeviceHeader = "";
        await suite.Check("Código para outro computador: formato XXXX-XXXX, ativação 200 com o segredo, reutilizar 400, minúsculas e hífen aceitos até 14:59, vencido no instante exato (15:00) → 400", async () =>
        {
            var (admin, _) = await f.StaffAsync("administrator", 12, [u1, u2]);
            var created = await Create(admin, u1, "Sala de espera", false);
            var code = created.GetProperty("activationCode").GetString()!;
            Ensure(Regex.IsMatch(code, "^[A-HJ-NP-Z2-9]{4}-[A-HJ-NP-Z2-9]{4}$") && created.GetProperty("device").GetProperty("status").GetString() == "pending" && created.GetProperty("codeExpiresAt").GetDateTimeOffset() == T0.AddMinutes(15) && !created.TryGetProperty("credential", out _), "Código novo e computador pendente.");
            codeDevice = created.GetProperty("device").GetProperty("id").GetGuid();
            using (var ok = await Activate(code)) // 1º uso do IP: código certo ativa
            {
                Expect(ok, 200); var body = await Read(ok);
                Ensure(body.GetProperty("deviceId").GetGuid() == codeDevice && body.GetProperty("secret").GetString()!.Length == 64 && body.GetProperty("name").GetString() == "Sala de espera" && body.GetProperty("unitId").GetGuid() == u1 && body.GetProperty("unitName").GetString() == "UBS da rodada 1", "Corpo da ativação.");
                codeDeviceHeader = ObsDeviceHelper.HeaderValue(codeDevice, body.GetProperty("secret").GetString()!);
            }
            using (var reuse = await Activate(code)) await ExpectProblem(reuse, 400, "invalid-activation-code");
            using (var active = await Current(codeDeviceHeader)) Expect(active, 200);
            // Minúsculas, hífen e espaço aceitos; ainda vale aos 14:59.
            var second = await Create(admin, u1, "Sala 2", false); var secondCode = second.GetProperty("activationCode").GetString()!;
            f.Clock.Advance(TimeSpan.FromMinutes(14) + TimeSpan.FromSeconds(59));
            using (var lower = await Activate(" " + secondCode.ToLowerInvariant().Replace("-", " - ") + " ")) Expect(lower, 200);
            f.Clock.Set(T0);
            // Vencido: no instante exato dos 15 minutos o código já não vale.
            var third = await Create(admin, u1, "Sala 3", false); var thirdCode = third.GetProperty("activationCode").GetString()!;
            f.Clock.Advance(TimeSpan.FromMinutes(15));
            using (var late = await Activate(thirdCode)) await ExpectProblem(late, 400, "invalid-activation-code");
            f.Clock.Set(T0);
            using (var neverNull = await Activate(null)) await ExpectProblem(neverNull, 400, "invalid-activation-code");
        });

        // ======== 4. Atendente presa à UBS do computador ========
        await suite.Check("Atendente com computador da UBS A e concessão em A entra; só com concessão em B → 403 device-required; presa à UBS do computador (rotas de B 403, lista e comunicados só de A)", async () =>
        {
            var (admin, _) = await f.StaffAsync("administrator", 13, [u1, u2]);
            var (_, deviceA) = await ActivateThis(admin, u1, "Atendimento A");
            var body = await f.LoginAsync(f.NewClient(), "obs", "atendente@example.test", deviceA);
            Ensure(body.GetProperty("device").GetProperty("unitId").GetGuid() == u1 && body.GetProperty("device").GetProperty("name").GetString() == "Atendimento A" && !body.GetProperty("keepSignedIn").GetBoolean() && body.GetProperty("expiresAt").GetDateTimeOffset() == T0.AddHours(8), "Atendente entra com o computador da UBS A.");
            var onlyB = await f.Service<AccountService>().ProvisionAsync("obs", "attendant", new("Atendente só B", Cpf(14), "so-b@example.test", Password)); await f.GrantAsync(onlyB.Id, u2);
            ObsDeviceHelper.Send(f.Anonymous, deviceA);
            using (var denied = await f.Anonymous.PostAsJsonAsync("/api/auth/login", new { scope = "obs", identifier = "so-b@example.test", password = Password })) await ExpectProblem(denied, 403, "device-required");
            ObsDeviceHelper.Send(f.Anonymous, null);
            var both = await f.Service<AccountService>().ProvisionAsync("obs", "attendant", new("Atendente A e B", Cpf(15), "a-e-b@example.test", Password)); await f.GrantAsync(both.Id, u1); await f.GrantAsync(both.Id, u2);
            var client = f.NewClient(); await f.LoginAsync(client, "obs", "a-e-b@example.test", deviceA);
            using (var inA = await client.GetAsync($"/api/obs/units/{u1}/registry")) Expect(inA, 200);
            using (var inB = await client.GetAsync($"/api/obs/units/{u2}/registry")) Expect(inB, 403);
            using (var units = await client.GetAsync("/api/obs/units")) { Expect(units, 200); var items = (await Read(units)).EnumerateArray().ToList(); Ensure(items.Count == 1 && items[0].GetProperty("id").GetGuid() == u1, "A atendente só vê a UBS do computador."); }
            using (var adminUnits = await admin.GetAsync("/api/obs/units")) Ensure((await Read(adminUnits)).GetArrayLength() == 2, "O administrador não é restringido.");
            using (var adminInB = await admin.GetAsync($"/api/obs/units/{u2}/registry")) Expect(adminInB, 200);
            // Comunicado só da UBS B: o administrador vê, a atendente presa à UBS A não.
            using (var publish = await f.Municipal.PostAsJsonAsync("/api/municipal/announcements", new { title = "Aviso da UBS B", body = "Só para a UBS B.", audience = "staff", scope = "units", unitIds = new[] { u2 } })) Expect(publish, 201);
            using (var seenByAdmin = await admin.GetAsync("/api/obs/announcements")) Ensure((await seenByAdmin.Content.ReadAsStringAsync()).Contains("Aviso da UBS B"), "O administrador com concessão em B vê o comunicado.");
            using (var seenByAttendant = await client.GetAsync("/api/obs/announcements")) { Expect(seenByAttendant, 200); Ensure(!(await seenByAttendant.Content.ReadAsStringAsync()).Contains("Aviso da UBS B"), "A atendente presa à UBS A não vê o comunicado da B."); }
        });

        // ======== 5. Manter conectado ========
        await suite.Check("Manter conectado na equipe: 30 dias só com computador (senão 8 h e keepSignedIn false); a Secretaria ignora cabeçalho e opção; o paciente ignora o cabeçalho, mas mantém a opção (30 dias, 24 h sem ela); KeepSignedInDays validado", async () =>
        {
            var (admin, _) = await f.StaffAsync("administrator", 16, [u1, u2]);
            var (_, device) = await ActivateThis(admin, u1, "Guichê");
            var kept = await f.LoginAsync(f.NewClient(), "obs", "atendente@example.test", device, keepSignedIn: true);
            Ensure(kept.GetProperty("keepSignedIn").GetBoolean() && kept.GetProperty("expiresAt").GetDateTimeOffset() == T0.AddDays(30) && kept.GetProperty("device").GetProperty("unitId").GetGuid() == u1, "Com computador, manter conectado vale 30 dias.");
            var plain = await f.LoginAsync(f.NewClient(), "obs", "atendente@example.test", device, keepSignedIn: false);
            Ensure(!plain.GetProperty("keepSignedIn").GetBoolean() && plain.GetProperty("expiresAt").GetDateTimeOffset() == T0.AddHours(8), "Sem pedir, 8 h.");
            var adminNoDevice = await f.LoginAsync(f.NewClient(), "obs", "admin@example.test", keepSignedIn: true);
            Ensure(!adminNoDevice.GetProperty("keepSignedIn").GetBoolean() && adminNoDevice.GetProperty("expiresAt").GetDateTimeOffset() == T0.AddHours(8) && adminNoDevice.GetProperty("device").ValueKind == JsonValueKind.Null, "Sem computador o manter conectado é ignorado e o login continua valendo 8 h.");
            var manager = await f.LoginAsync(f.NewClient(), "municipal", "gestora@example.test", device, keepSignedIn: true);
            Ensure(!manager.GetProperty("keepSignedIn").GetBoolean() && manager.GetProperty("device").ValueKind == JsonValueKind.Null && manager.GetProperty("expiresAt").GetDateTimeOffset() == T0.AddHours(8), "A Secretaria ignora o computador e a opção.");
            await f.Service<AccountService>().ProvisionAsync("patient", "patient", new("Paciente do login", Cpf(17), "paciente17@example.test", Password));
            var patient = await f.LoginAsync(f.NewClient(), "patient", Cpf(17), device, keepSignedIn: true);
            // Desde 2026-11-26 o paciente fica conectado como nos desktops: 30 dias com a opção e 24 h sem ela (PatientSessionChecks); o computador do OBS continua ignorado.
            Ensure(patient.GetProperty("keepSignedIn").GetBoolean() && patient.GetProperty("device").ValueKind == JsonValueKind.Null && patient.GetProperty("expiresAt").GetDateTimeOffset() == T0.AddDays(30), "O paciente ignora o computador e mantém a opção por 30 dias.");
            var patientPlain = await f.LoginAsync(f.NewClient(), "patient", Cpf(17), device);
            Ensure(!patientPlain.GetProperty("keepSignedIn").GetBoolean() && patientPlain.GetProperty("device").ValueKind == JsonValueKind.Null && patientPlain.GetProperty("expiresAt").GetDateTimeOffset() == T0.AddHours(24), "Sem a opção o paciente fica 24 h.");
            Ensure(patient.TryGetProperty("user", out _) && patient.TryGetProperty("token", out _), "Os campos de sempre continuam na resposta.");
            ObsDeviceOptions Options(string? days, string? enabled = null) => new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Nora:ObsDevices:KeepSignedInDays"] = days, ["Nora:ObsDevices:Enabled"] = enabled, ["Nora:ObsDevices:RequireForAttendants"] = enabled is null ? "true" : null
            }.Where(pair => pair.Value is not null)).Build());
            Ensure(Options(null).KeepSignedInDays == 30 && Options("7").KeepSignedInDays == 7 && Options("90").KeepSignedInDays == 90, "Padrão do KeepSignedInDays.");
            // Desde 24/11/2026 os computadores vêm desligados, e desligados não exigem nada, mesmo com a exigência configurada.
            Ensure(!Options(null).Enabled && !Options(null).RequireForAttendants && Options(null, "true") is { Enabled: true, RequireForAttendants: true }, "Padrões do recurso e da exigência.");
            foreach (var invalid in new[] { "0", "91", "x", "-1", "" + 1.5 })
            {
                try { Options(invalid); throw new InvalidOperationException($"KeepSignedInDays '{invalid}' foi aceito."); }
                catch (InvalidOperationException error) when (error.Message.Contains("KeepSignedInDays")) { }
            }
        });

        // ======== 6. Sessão ligada ao computador ========
        await suite.Check("Sessão ligada ao computador: token com cabeçalho ausente, malformado, de outro computador ou com segredo errado → 401; sessão de atendente sem computador (antiga) → 401 com a exigência ligada", async () =>
        {
            var (admin, _) = await f.StaffAsync("administrator", 18, [u1, u2]);
            var (_, header) = await ActivateThis(admin, u1, "Balcão"); var (_, other) = await ActivateThis(admin, u1, "Balcão 2");
            var client = f.NewClient(); await f.LoginAsync(client, "obs", "atendente@example.test", header);
            var token = Token(client);
            using (var ok = await Me(token, header)) Expect(ok, 200);
            using (var missing = await Me(token, null)) Expect(missing, 401);
            using (var malformed = await Me(token, "isto-nao-e-um-cabecalho")) Expect(malformed, 401);
            using (var tooLong = await Me(token, new string('A', 400))) Expect(tooLong, 401);
            using (var otherDevice = await Me(token, other)) Expect(otherDevice, 401);
            using (var wrongSecret = await Me(token, header[..^1] + (header[^1] == 'A' ? 'B' : 'A'))) Expect(wrongSecret, 401);
            using (var lower = await Me(token, header.ToLowerInvariant())) Expect(lower, 200); // hexadecimal sem diferença de caixa
            // Sessão de atendente sem computador (como as anteriores ao deploy): o servidor não aceita mais.
            var raw = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            await f.Exec("INSERT INTO nora.sessions(token_hash,account_id,expires_at) VALUES($1,$2,$3)", Sha(raw), f.AttendantId, T0.AddHours(8));
            using (var old = await Me(raw, null)) Expect(old, 401);
            using (var oldWithDevice = await Me(raw, header)) Expect(oldWithDevice, 401);
            // O administrador sem computador continua entrando e operando.
            var adminRaw = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            await f.Exec("INSERT INTO nora.sessions(token_hash,account_id,expires_at) VALUES($1,$2,$3)", Sha(adminRaw), f.AdminId, T0.AddHours(8));
            using (var adminOld = await Me(adminRaw, null)) Expect(adminOld, 200);
        });

        // ======== 7. Revogação ========
        await suite.Check("Revogar: as sessões do computador recebem 401, devices/current → 401 device-revoked, o computador deixa de valer no login (atendente 403, administrador entra), revogar duas vezes 409; o administrador revoga o próprio computador e entra de novo", async () =>
        {
            var (admin, _) = await f.StaffAsync("administrator", 19, [u1, u2]);
            var (deviceId, header) = await ActivateThis(admin, u1, "Guichê 2");
            var attendant = f.NewClient(); await f.LoginAsync(attendant, "obs", "atendente@example.test", header);
            var boundAdmin = f.NewClient(); await f.LoginAsync(boundAdmin, "obs", "admin@example.test", header);
            using (var before = await attendant.GetAsync($"/api/obs/units/{u1}/registry")) Expect(before, 200);
            using (var noConfirm = await admin.PostAsJsonAsync(Url(u1, $"/{deviceId}/revoke"), new { confirmed = false })) Expect(noConfirm, 400);
            using (var noField = await admin.PostAsJsonAsync(Url(u1, $"/{deviceId}/revoke"), new { })) Expect(noField, 400);
            using (var revoke = await admin.PostAsJsonAsync(Url(u1, $"/{deviceId}/revoke"), new { confirmed = true }))
            {
                Expect(revoke, 200); var device = (await Read(revoke)).GetProperty("device");
                Ensure(device.GetProperty("status").GetString() == "revoked" && device.GetProperty("revokedAt").GetDateTimeOffset() == T0 && device.GetProperty("revokedByName").GetString() == "Equipe 19", "Resposta da revogação.");
            }
            using (var after = await attendant.GetAsync($"/api/obs/units/{u1}/registry")) Expect(after, 401);
            using (var adminAfter = await boundAdmin.GetAsync("/api/obs/units")) Expect(adminAfter, 401);
            Ensure(await f.Scalar<long>("SELECT count(*) FROM nora.sessions WHERE device_id=$1 AND revoked_at IS NULL", deviceId) == 0 && await f.Scalar<string>("SELECT coalesce(secret_hash,'-') FROM nora.obs_devices WHERE id=$1", deviceId) == "-", "Sessões do computador revogadas e segredo apagado.");
            using (var current = await Current(header)) await ExpectProblem(current, 401, "device-revoked");
            using (var noHeader = await Current(null)) await ExpectProblem(noHeader, 401, "device-revoked");
            using (var garbage = await Current("lixo")) await ExpectProblem(garbage, 401, "device-revoked");
            using (var attendantLogin = f.NewClient()) { ObsDeviceHelper.Send(attendantLogin, header); using var response = await attendantLogin.PostAsJsonAsync("/api/auth/login", new { scope = "obs", identifier = "atendente@example.test", password = Password }); await ExpectProblem(response, 403, "device-required"); }
            var adminAgain = await f.LoginAsync(f.NewClient(), "obs", "admin@example.test", header);
            Ensure(adminAgain.GetProperty("device").ValueKind == JsonValueKind.Null, "Com o computador revogado o administrador entra, sem computador.");
            using (var twice = await admin.PostAsJsonAsync(Url(u1, $"/{deviceId}/revoke"), new { confirmed = true })) await ExpectProblem(twice, 409, "conflict");
            // O administrador revoga o próprio computador e nunca fica trancado.
            var (ownId, ownHeader) = await ActivateThis(admin, u1, "Computador do administrador");
            var own = f.NewClient(); await f.LoginAsync(own, "obs", "equipe19@example.test", ownHeader);
            using (var revokeOwn = await own.PostAsJsonAsync(Url(u1, $"/{ownId}/revoke"), new { confirmed = true })) Expect(revokeOwn, 200);
            using (var sessionEnded = await own.GetAsync("/api/auth/me")) Expect(sessionEnded, 401);
            var back = f.NewClient(); await f.LoginAsync(back, "obs", "equipe19@example.test");
            using (var works = await back.GetAsync(Url(u1))) Expect(works, 200);
        });

        // ======== 8. Reativar com código novo ========
        await suite.Check("Reativar um computador ativo com código novo: o segredo antigo vale até o código ser usado e depois para; as sessões antigas são revogadas; computador revogado → 409; de outra UBS ou inexistente → 404", async () =>
        {
            var (admin, _) = await f.StaffAsync("administrator", 20, [u1, u2]);
            var (deviceId, oldHeader) = await ActivateThis(admin, u1, "Computador trocado");
            var oldSession = f.NewClient(); await f.LoginAsync(oldSession, "obs", "atendente@example.test", oldHeader);
            string code; using (var issue = await admin.PostAsync(Url(u1, $"/{deviceId}/activation-code"), null)) { Expect(issue, 200); var body = await Read(issue); code = body.GetProperty("activationCode").GetString()!; Ensure(body.GetProperty("codeExpiresAt").GetDateTimeOffset() == T0.AddMinutes(15) && Regex.IsMatch(code, "^[A-Z2-9]{4}-[A-Z2-9]{4}$"), "Código da reativação."); }
            using (var stillValid = await Current(oldHeader)) Expect(stillValid, 200);
            using (var oldOk = await oldSession.GetAsync($"/api/obs/units/{u1}/registry")) Expect(oldOk, 200);
            string newHeader; using (var activate = await Activate(code)) { Expect(activate, 200); var body = await Read(activate); Ensure(body.GetProperty("deviceId").GetGuid() == deviceId, "Mesmo computador, segredo novo."); newHeader = ObsDeviceHelper.HeaderValue(deviceId, body.GetProperty("secret").GetString()!); }
            using (var oldCurrent = await Current(oldHeader)) await ExpectProblem(oldCurrent, 401, "device-revoked");
            using (var oldAfter = await oldSession.GetAsync($"/api/obs/units/{u1}/registry")) Expect(oldAfter, 401);
            using (var newCurrent = await Current(newHeader)) Expect(newCurrent, 200);
            Ensure(await f.Scalar<long>("SELECT count(*) FROM nora.sessions WHERE device_id=$1 AND revoked_at IS NULL", deviceId) == 0, "As sessões do segredo antigo foram revogadas.");
            await f.LoginAsync(f.NewClient(), "obs", "atendente@example.test", newHeader);
            var (revokedId, _) = await ActivateThis(admin, u1, "Será revogado");
            using (var revoke = await admin.PostAsJsonAsync(Url(u1, $"/{revokedId}/revoke"), new { confirmed = true })) Expect(revoke, 200);
            using (var onRevoked = await admin.PostAsync(Url(u1, $"/{revokedId}/activation-code"), null)) await ExpectProblem(onRevoked, 409, "conflict");
            var (otherUnitId, _) = await ActivateThis(admin, u2, "Da UBS 2");
            using (var wrongUnit = await admin.PostAsync(Url(u1, $"/{otherUnitId}/activation-code"), null)) Expect(wrongUnit, 404);
            using (var wrongUnitRevoke = await admin.PostAsJsonAsync(Url(u1, $"/{otherUnitId}/revoke"), new { confirmed = true })) Expect(wrongUnitRevoke, 404);
            using (var unknown = await admin.PostAsync(Url(u1, $"/{Guid.NewGuid()}/activation-code"), null)) Expect(unknown, 404);
        });

        // ======== 9. Permissões ========
        await suite.Check("Permissão: atendente 403 em todas as rotas de computadores; administrador sem concessão na UBS 403; Secretaria 403; paciente 403; sem sessão 401", async () =>
        {
            var (admin, _) = await f.StaffAsync("administrator", 21, [u1, u2]);
            var (deviceId, header) = await ActivateThis(admin, u1, "Computador da permissão");
            var attendant = f.NewClient(); await f.LoginAsync(attendant, "obs", "atendente@example.test", header);
            var onlyU1 = await f.StaffAsync("administrator", 22, [u1]);
            var patient = await f.PatientAsync(23, u1);
            foreach (var (client, label) in new[] { (attendant, "atendente"), (f.Municipal, "Secretaria"), (patient, "paciente") })
            {
                using var list = await client.GetAsync(Url(u1)); Expect(list, 403);
                using var create = await client.PostAsJsonAsync(Url(u1), new { name = "Nao", activateThisComputer = false }); Expect(create, 403);
                using var code = await client.PostAsync(Url(u1, $"/{deviceId}/activation-code"), null); Expect(code, 403);
                using var revoke = await client.PostAsJsonAsync(Url(u1, $"/{deviceId}/revoke"), new { confirmed = true }); Expect(revoke, 403);
                Ensure(true, label);
            }
            using (var noGrant = await onlyU1.Client.GetAsync(Url(u2))) Expect(noGrant, 403);
            using (var noGrantCreate = await onlyU1.Client.PostAsJsonAsync(Url(u2), new { name = "Nao", activateThisComputer = false })) Expect(noGrantCreate, 403);
            using (var anonymous = await f.Anonymous.GetAsync(Url(u1))) Expect(anonymous, 401);
            Ensure(await f.Scalar<string>("SELECT status FROM nora.obs_devices WHERE id=$1", deviceId) == "active", "Nenhuma recusa mudou o computador.");
        });

        // ======== 10. Concorrência ========
        await suite.Check("Concorrência: dois activate com o mesmo código → um 200; a revogação espera a operação em andamento e depois a sessão cai; login concorrente com revogação nunca deixa sessão ativa em computador revogado", async () =>
        {
            var (admin, _) = await f.StaffAsync("administrator", 24, [u1, u2]);
            var created = await Create(admin, u1, "Corrida do código", false); var code = created.GetProperty("activationCode").GetString()!;
            var results = await Task.WhenAll(Enumerable.Range(0, 2).Select(async _ => { using var response = await Activate(code); return (int)response.StatusCode; }));
            Ensure(results.Count(status => status == 200) == 1 && results.Count(status => status == 400) == 1, $"Dois activate com o mesmo código: {string.Join(",", results)}.");
            // Operação em andamento: segura a sessão da atendente como o executor de acesso faz (FOR SHARE) e dispara a revogação; ela espera.
            var (deviceId, header) = await ActivateThis(admin, u1, "Revogar em andamento");
            var attendant = f.NewClient(); await f.LoginAsync(attendant, "obs", "atendente@example.test", header);
            await using (var held = await f.Database.OpenAsync())
            await using (var transaction = await held.BeginTransactionAsync())
            {
                await using (var share = f.Database.Command(held, "SELECT 1 FROM nora.sessions WHERE token_hash=$1 FOR SHARE", Sha(Token(attendant)))) await share.ExecuteScalarAsync();
                var revoke = admin.PostAsJsonAsync(Url(u1, $"/{deviceId}/revoke"), new { confirmed = true });
                await Task.Delay(1500);
                Ensure(!revoke.IsCompleted, "A revogação deveria esperar a operação em andamento.");
                await transaction.CommitAsync();
                using var done = await revoke.WaitAsync(TimeSpan.FromSeconds(20)); Expect(done, 200);
            }
            using (var next = await attendant.GetAsync($"/api/obs/units/{u1}/registry")) Expect(next, 401);
            // Login contra revogação, várias vezes, no mesmo instante.
            for (var i = 0; i < 5; i++)
            {
                var (id, loginHeader) = await ActivateThis(admin, u1, $"Corrida {i}");
                var client = f.NewClient(); ObsDeviceHelper.Send(client, loginHeader);
                var login = client.PostAsJsonAsync("/api/auth/login", new { scope = "obs", identifier = "atendente@example.test", password = Password });
                var revoke = admin.PostAsJsonAsync(Url(u1, $"/{id}/revoke"), new { confirmed = true });
                using var loginResponse = await login; using var revokeResponse = await revoke;
                Expect(revokeResponse, 200); Ensure((int)loginResponse.StatusCode is 200 or 403, $"Login concorrente: {(int)loginResponse.StatusCode}.");
            }
            Ensure(await f.Scalar<long>("SELECT count(*) FROM nora.sessions s JOIN nora.obs_devices d ON d.id=s.device_id WHERE d.status='revoked' AND s.revoked_at IS NULL") == 0, "Nunca fica sessão ativa ligada a computador revogado.");
        });

        await suite.Check("Sem impasse: o administrador revoga o computador em que está enquanto outra requisição dele segura a sessão (a revogação responde 200 sem esperá-la); dois logins de contas diferentes no mesmo computador, em corrida, entram os dois", async () =>
        {
            var (admin, adminId) = await f.StaffAsync("administrator", 26, [u1]);
            var (ownId, ownHeader) = await ActivateThis(admin, u1, "Revogar o próprio");
            var own = f.NewClient(); await f.LoginAsync(own, "obs", "equipe26@example.test", ownHeader);
            // Outra requisição do mesmo administrador em andamento: já revalidou a sessão (FOR SHARE) e ainda vai pedir a concessão da UBS que a revogação segura. A revogação não pode esperar por ela.
            await using (var held = await f.Database.OpenAsync())
            await using (var transaction = await held.BeginTransactionAsync())
            {
                await using (var share = f.Database.Command(held, "SELECT 1 FROM nora.sessions WHERE token_hash=$1 FOR SHARE", Sha(Token(own)))) await share.ExecuteScalarAsync();
                using var revoke = await own.PostAsJsonAsync(Url(u1, $"/{ownId}/revoke"), new { confirmed = true }).WaitAsync(TimeSpan.FromSeconds(20));
                Expect(revoke, 200);
                await using (var grant = f.Database.Command(held, "SELECT 1 FROM nora.staff_units WHERE account_id=$1 AND unit_id=$2 FOR UPDATE", adminId, u1)) await grant.ExecuteScalarAsync();
                await transaction.CommitAsync();
            }
            using (var ended = await own.GetAsync("/api/auth/me")) Expect(ended, 401);
            Ensure(await f.Scalar<string>("SELECT status FROM nora.obs_devices WHERE id=$1", ownId) == "revoked", "O computador ficou revogado.");
            // Dois logins de contas diferentes no mesmo computador ao mesmo tempo.
            var second = await f.Service<AccountService>().ProvisionAsync("obs", "attendant", new("Equipe 27", Cpf(27), "equipe27@example.test", Password));
            await f.GrantAsync(second.Id, u1);
            var (_, header) = await ActivateThis(admin, u1, "Logins simultâneos");
            var one = f.NewClient(); var two = f.NewClient(); ObsDeviceHelper.Send(one, header); ObsDeviceHelper.Send(two, header);
            async Task<int> Login(HttpClient client, string identifier) { using var response = await client.PostAsJsonAsync("/api/auth/login", new { scope = "obs", identifier, password = Password }); return (int)response.StatusCode; }
            for (var round = 0; round < 8; round++)
            {
                var statuses = await Task.WhenAll(Login(one, "atendente@example.test"), Login(two, "equipe27@example.test"));
                Ensure(statuses.All(status => status == 200), $"Logins simultâneos na rodada {round}: {string.Join(",", statuses)}.");
            }
        });

        // ======== 12. Auditoria e manutenção ========
        await suite.Check("Auditoria: criado, código emitido, ativado (sem autor) e revogado ficam gravados; a manutenção limpa códigos vencidos há mais de 1 dia e só eles", async () =>
        {
            var events = new List<string>();
            await using (var connectionRead = await f.Database.OpenAsync())
            await using (var read = f.Database.Command(connectionRead, "SELECT action||':'||(actor_id IS NOT NULL)::text FROM nora.obs_device_events WHERE device_id=$1 ORDER BY id", codeDevice))
            await using (var reader = await read.ExecuteReaderAsync())
                while (await reader.ReadAsync()) events.Add(reader.GetString(0));
            Ensure(events.SequenceEqual(["created:true", "code-issued:true", "activated:false"]), $"Eventos do computador por código: {string.Join(",", events)}.");
            Ensure(await f.Scalar<long>("SELECT count(*) FROM nora.obs_device_events WHERE action='revoked' AND actor_id IS NOT NULL") >= 3, "Revogações com o autor.");
            var (admin, _) = await f.StaffAsync("administrator", 25, [u1, u2]);
            var old = await Create(admin, u1, "Código antigo", false); var oldId = old.GetProperty("device").GetProperty("id").GetGuid();
            f.Clock.Advance(TimeSpan.FromHours(23));
            var recent = await f.Service<MaintenanceService>().RunOnceAsync(default);
            Ensure(await f.Scalar<string>("SELECT coalesce(code_hash,'-') FROM nora.obs_devices WHERE id=$1", oldId) != "-", "Código vencido há menos de 1 dia fica.");
            f.Clock.Advance(TimeSpan.FromHours(2));
            var cleaned = await f.Service<MaintenanceService>().RunOnceAsync(default);
            Ensure(recent.DeviceCodes == 0 && cleaned.DeviceCodes >= 1 && await f.Scalar<string>("SELECT coalesce(code_hash,'-') FROM nora.obs_devices WHERE id=$1", oldId) == "-" && await f.Scalar<string>("SELECT status FROM nora.obs_devices WHERE id=$1", oldId) == "pending",
                "Código vencido há mais de 1 dia é limpo e o computador pendente continua.");
            Ensure(await f.Scalar<string>("SELECT status FROM nora.obs_devices WHERE id=$1", codeDevice) == "active", "A manutenção não mexe em computador ativo.");
            f.Clock.Set(T0);
        });

        // ======== 11. Limites (hosts próprios: o limite por IP e por conta é de cada processo) ========
        await using (var limits = await StartAsync(connection, "devices_limit", unitCount: 1))
        {
            await suite.Check("Limites: 11ª ativação em 10 min do mesmo IP → 429 (formato inválido também conta); 31ª geração de código pela mesma conta → 429, outra conta não é afetada", async () =>
            {
                foreach (var bad in new[] { "ABCD-EFG0", "ABCDEFGI", "ABC", "", "ZZZZ-ZZZZ", "AAAA-AAAA", "BBBB-BBBB", "CCCC-CCCC", "DDDD-DDDD", "EEEE-EEEE" })
                {
                    using var response = await limits.Anonymous.PostAsJsonAsync("/api/obs/devices/activate", new { code = bad });
                    await ExpectProblem(response, 400, "invalid-activation-code");
                }
                using (var eleventh = await limits.Anonymous.PostAsJsonAsync("/api/obs/devices/activate", new { code = "FFFF-FFFF" })) await ExpectProblem(eleventh, 429, "too-many-requests");
                var (first, _) = await limits.StaffAsync("administrator", 31, [limits.Unit1]); var (second, _) = await limits.StaffAsync("administrator", 32, [limits.Unit1]);
                for (var i = 0; i < 30; i++) { using var response = await first.PostAsJsonAsync($"/api/obs/units/{limits.Unit1}/devices", new { name = $"Computador {i}", activateThisComputer = false }); Expect(response, 201); }
                using (var overLimit = await first.PostAsJsonAsync($"/api/obs/units/{limits.Unit1}/devices", new { name = "Demais", activateThisComputer = false })) await ExpectProblem(overLimit, 429, "too-many-requests");
                using (var other = await second.PostAsJsonAsync($"/api/obs/units/{limits.Unit1}/devices", new { name = "Outra conta", activateThisComputer = false })) Expect(other, 201);
                using var list = await first.GetAsync($"/api/obs/units/{limits.Unit1}/devices"); Expect(list, 200);
            });
        }
        await CheckDesktopAccessAsync(suite, connection);
        return suite.Report() == 0 ? 0 : 1;
    }
}
