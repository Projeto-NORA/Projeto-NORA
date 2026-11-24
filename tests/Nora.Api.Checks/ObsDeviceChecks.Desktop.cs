using System.Net.Http.Json;
using System.Text.Json;
using Nora.Api.Services;
using static RoundFixture;

internal static partial class ObsDeviceChecks
{
    private static async Task CheckDesktopAccessAsync(Suite suite, string connection)
    {
        await using var f = await StartAsync(connection, "desktop_access");

        static string Header(Guid deviceId) => ObsDeviceHelper.HeaderValue(deviceId, ObsDeviceCredentials.NewSecret());
        static void SetSession(HttpClient client, JsonElement response) =>
            client.DefaultRequestHeaders.Authorization = new("Bearer", response.GetProperty("token").GetString());
        static Task<HttpResponseMessage> Login(HttpClient client, string scope, string identifier, bool keepSignedIn = false,
            Guid? unitId = null, string password = Password) =>
            client.PostAsJsonAsync("/api/auth/login", new { scope, identifier, password, keepSignedIn, desktopSession = true, deviceName = "Recepção de teste", unitId });

        await suite.Check("Padrão desde 24/11/2026: computadores desligados, o login do OBS vale sozinho, mantém 30 dias e não registra computador", async () =>
        {
            await using var off = await StartAsync(connection, "desktop_login_only", extraArgs: ["--Nora:ObsDevices:Enabled", "false"]);
            var obs = off.NewClient();
            using (var kept = await Login(obs, "obs", "atendente@example.test", keepSignedIn: true))
            {
                Expect(kept, 200);
                var body = await Read(kept);
                Ensure(body.GetProperty("device").ValueKind == JsonValueKind.Null && body.GetProperty("keepSignedIn").GetBoolean()
                    && body.GetProperty("expiresAt").GetDateTimeOffset() == T0.AddDays(30), "Atendente sem computador deveria entrar e manter a sessão por 30 dias.");
                SetSession(obs, body);
            }
            using (var profile = await obs.GetAsync("/api/auth/profile"))
            {
                Expect(profile, 200);
                Ensure((await Read(profile)).GetProperty("units").GetArrayLength() == 1, "A sessão só pelo login deveria enxergar a UBS concedida.");
            }
            using (var common = await Login(off.NewClient(), "obs", "atendente@example.test"))
            {
                Expect(common, 200);
                Ensure((await Read(common)).GetProperty("expiresAt").GetDateTimeOffset() == T0.AddDays(1), "Sem manter conectado, a sessão de desktop vence em 24 horas.");
            }
            // Um OBS antigo ainda manda a identificação do computador: ela é ignorada e nada é registrado.
            var legacyId = Guid.NewGuid();
            var legacy = off.NewClient();
            ObsDeviceHelper.Send(legacy, Header(legacyId));
            using (var ignored = await Login(legacy, "obs", "atendente@example.test")) Expect(ignored, 200);
            Ensure(await off.Scalar<long>("SELECT count(*) FROM nora.obs_devices WHERE id=$1", legacyId) == 0, "Com os computadores desligados nenhum computador deveria ser registrado.");
            using (var plain = await off.Anonymous.PostAsJsonAsync("/api/auth/login", new { scope = "obs", identifier = "atendente@example.test", password = Password }))
                Expect(plain, 200);
        });

        await suite.Check("Desktop OBS: credenciais corretas ativam uma única unidade autorizada e a sessão comum vence em 24 horas", async () =>
        {
            var id = Guid.NewGuid();
            var obs = f.NewClient();
            ObsDeviceHelper.Send(obs, Header(id));
            using (var wrong = await Login(obs, "obs", "atendente@example.test", password: "Senha-incorreta"))
                Expect(wrong, 401);
            Ensure(await f.Scalar<long>("SELECT count(*) FROM nora.obs_devices WHERE id=$1", id) == 0, "Senha inválida não pode cadastrar o computador.");
            using var signedIn = await Login(obs, "obs", "atendente@example.test");
            Expect(signedIn, 200);
            var result = await Read(signedIn);
            Ensure(result.GetProperty("device").GetProperty("id").GetGuid() == id, "A API deve vincular a sessão à instalação apresentada.");
            Ensure(result.GetProperty("device").GetProperty("unitId").GetGuid() == f.Unit1, "Deve escolher a única concessão da atendente.");
            Ensure(result.GetProperty("expiresAt").GetDateTimeOffset() == T0.AddDays(1), "Sessão não persistente do desktop deve vencer em 24 horas.");
            SetSession(obs, result);
            using (var profile = await obs.GetAsync("/api/auth/profile"))
            {
                Expect(profile, 200);
                var data = await Read(profile);
                Ensure(data.GetProperty("email").GetString() == "atendente@example.test" && data.GetProperty("units").GetArrayLength() == 1, "O perfil traz o e-mail próprio e a unidade autorizada.");
            }
            f.Clock.Advance(TimeSpan.FromDays(1));
            try { using var expired = await obs.GetAsync("/api/auth/me"); Expect(expired, 401); }
            finally { f.Clock.Set(T0); }
        });

        await suite.Check("Desktop OBS: várias unidades exigem seleção autenticada e não permitem ativar em outra unidade", async () =>
        {
            var id = Guid.NewGuid();
            var obs = f.NewClient();
            ObsDeviceHelper.Send(obs, Header(id));
            using (var choose = await Login(obs, "obs", "admin@example.test"))
            {
                Expect(choose, 409);
                var selection = await Read(choose);
                Ensure(selection.GetProperty("code").GetString() == "device-unit-selection-required", "Esperava uma seleção de unidade.");
                var units = selection.GetProperty("units").EnumerateArray().Select(unit => unit.GetProperty("id").GetGuid()).ToArray();
                Ensure(units.Order().SequenceEqual(new[] { f.Unit1, f.Unit2 }.Order()), "Devem ser listadas apenas as unidades concedidas.");
            }
            using (var forbidden = await Login(obs, "obs", "admin@example.test", unitId: Guid.NewGuid()))
                await ExpectProblem(forbidden, 403, "device-unit-mismatch");
            Ensure(await f.Scalar<long>("SELECT count(*) FROM nora.obs_devices WHERE id=$1", id) == 0, "A seleção não pode cadastrar o computador antes da unidade válida.");
            using var selected = await Login(obs, "obs", "admin@example.test", keepSignedIn: true, unitId: f.Unit2);
            Expect(selected, 200);
            var login = await Read(selected);
            Ensure(login.GetProperty("expiresAt").GetDateTimeOffset() == T0.AddDays(30) && login.GetProperty("keepSignedIn").GetBoolean(), "A opção persistente dura 30 dias.");
            Ensure(login.GetProperty("device").GetProperty("unitId").GetGuid() == f.Unit2, "A sessão deve usar a unidade selecionada.");
        });

        await suite.Check("Desktop OBS: dois logins na mesma instalação criam um computador, e a revogação impede novo login nessa identidade", async () =>
        {
            var id = Guid.NewGuid();
            var header = Header(id);
            var one = f.NewClient();
            var two = f.NewClient();
            ObsDeviceHelper.Send(one, header);
            ObsDeviceHelper.Send(two, header);
            foreach (var response in await Task.WhenAll(Login(one, "obs", "atendente@example.test"),
                Login(two, "obs", "admin@example.test", unitId: f.Unit1)))
            {
                using (response) Expect(response, 200);
            }
            Ensure(await f.Scalar<long>("SELECT count(*) FROM nora.obs_device_events WHERE device_id=$1 AND action='activated'", id) == 1, "O registro e a auditoria da ativação devem ocorrer uma vez.");
            using (var revoke = await f.Admin.PostAsJsonAsync($"/api/obs/units/{f.Unit1}/devices/{id}/revoke", new { confirmed = true }))
                Expect(revoke, 200);
            using var forbidden = await Login(one, "obs", "atendente@example.test");
            await ExpectProblem(forbidden, 403, "device-revoked");
        });

        await suite.Check("Desktop Municipal: sessão padrão e persistente, edição do nome próprio, município e saída", async () =>
        {
            await using (var db = await f.Database.OpenAsync())
                await f.Database.ExecuteAsync(db, "UPDATE nora.accounts SET municipality_name=$2 WHERE id=$1", default, f.ManagerId, "Município de teste");
            var municipal = f.NewClient();
            using (var response = await Login(municipal, "municipal", "gestora@example.test"))
            {
                Expect(response, 200);
                var payload = await Read(response);
                Ensure(payload.GetProperty("expiresAt").GetDateTimeOffset() == T0.AddDays(1), "A sessão municipal comum dura 24 horas.");
            }
            using (var response = await Login(municipal, "municipal", "gestora@example.test", keepSignedIn: true))
            {
                Expect(response, 200);
                var payload = await Read(response);
                Ensure(payload.GetProperty("expiresAt").GetDateTimeOffset() == T0.AddDays(30), "A sessão municipal persistente dura 30 dias.");
                SetSession(municipal, payload);
            }
            using (var edited = await municipal.PatchAsJsonAsync("/api/auth/profile", new { fullName = "Gestora atualizada", email = "outro@example.test" }))
            {
                Expect(edited, 200);
                var profile = await Read(edited);
                Ensure(profile.GetProperty("fullName").GetString() == "Gestora atualizada", "O próprio nome deve ser gravado.");
                Ensure(profile.GetProperty("email").GetString() == "gestora@example.test", "O próprio e-mail é somente leitura.");
                Ensure(profile.GetProperty("municipalityName").GetString() == "Município de teste", "O perfil mostra o município informado no provisionamento.");
            }
            using (var me = await municipal.GetAsync("/api/auth/me"))
                Ensure((await Read(me)).GetProperty("fullName").GetString() == "Gestora atualizada", "A identidade atual deve refletir o nome gravado.");
            using (var logout = await municipal.PostAsync("/api/auth/logout", null)) Expect(logout, 204);
            using (var afterLogout = await municipal.GetAsync("/api/auth/profile")) Expect(afterLogout, 401);
            var patient = await f.PatientAsync(42);
            using var denied = await patient.GetAsync("/api/auth/profile");
            Expect(denied, 403);
        });
    }
}
