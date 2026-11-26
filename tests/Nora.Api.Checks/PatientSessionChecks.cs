using System.Net.Http.Json;
using System.Text.Json;
using Nora.Api.DAOs;
using Nora.Api.Services;
using static RoundFixture;

/// <summary>
/// Sessão do paciente (2026-11-26): sem "manter conectado" vale 24 h; com ele vale <c>KeepSignedInDays</c> dias (30 por padrão), no login por CPF, por e-mail e (em <c>GooglePushChecks</c>) com Google.
/// Confere também o que depende da duração: o token vale até o último instante, a limpeza só apaga depois do prazo, a presença, a revogação (sair, trocar a senha, desativar) e que desktop e equipe
/// continuam como antes. Roda com <c>--patient-session-only</c>.
/// </summary>
internal static class PatientSessionChecks
{
    internal static readonly TimeSpan Day = TimeSpan.FromHours(24);

    public static async Task<int> RunAsync(string root)
    {
        var connection = Environment.GetEnvironmentVariable("NORA_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection))
        {
            Console.WriteLine("SKIP sessão do paciente: configure NORA_TEST_CONNECTION somente para uma base descartável.");
            return 0;
        }
        var suite = new Suite("sessão do paciente");
        // O fluxo global antigo (só em Development e ligado por configuração) é onde a desativação da conta revoga as sessões: é ele que o teste de revogação usa.
        await using var f = await StartAsync(connection, "patient_session", extraArgs: ["--Nora:LegacyGlobalWorkflow:Enabled", "true"]);
        var u1 = f.Unit1;
        var t0 = RoundClock.T0;
        await f.PatientAsync(1, u1);
        var cpf = Cpf(1); var email = "paciente1@example.test";

        async Task<JsonElement> Login(string identifier, bool? keep, string scope = "patient", string? device = null) => await f.LoginAsync(f.NewClient(), scope, identifier, device, keep);
        HttpClient Signed(JsonElement login) { var client = f.NewClient(); client.DefaultRequestHeaders.Authorization = new("Bearer", login.GetProperty("token").GetString()); return client; }
        async Task<int> Me(HttpClient client) { using var response = await client.GetAsync("/api/auth/me"); return (int)response.StatusCode; }

        await suite.Check("Paciente por CPF e por e-mail: sem keepSignedIn (ausente ou falso) a sessão vale 24 h e a resposta traz keepSignedIn falso", async () =>
        {
            foreach (var identifier in new[] { cpf, email })
                foreach (bool? keep in new bool?[] { null, false })
                {
                    var login = await Login(identifier, keep);
                    Ensure(login.GetProperty("expiresAt").GetDateTimeOffset() == t0 + Day && !login.GetProperty("keepSignedIn").GetBoolean() && login.GetProperty("device").ValueKind == JsonValueKind.Null,
                        $"Login de {identifier} ({keep?.ToString() ?? "sem a opção"}) deveria valer 24 h sem manter conectado.");
                }
        });

        await suite.Check("Paciente por CPF e por e-mail: keepSignedIn verdadeiro vale 30 dias (KeepSignedInDays) e a resposta traz keepSignedIn verdadeiro", async () =>
        {
            foreach (var identifier in new[] { cpf, email })
            {
                var login = await Login(identifier, true);
                Ensure(login.GetProperty("expiresAt").GetDateTimeOffset() == t0.AddDays(30) && login.GetProperty("keepSignedIn").GetBoolean() && login.GetProperty("user").GetProperty("scope").GetString() == "patient",
                    $"Login de {identifier} deveria valer 30 dias com manter conectado.");
            }
            // Cabeçalho de computador do OBS no login do paciente é ignorado; a opção continua valendo.
            var (_, header) = await ObsDeviceHelper.ActivateAsync(f.Admin, u1, "Guichê do paciente");
            var withHeader = await Login(cpf, true, device: header);
            Ensure(withHeader.GetProperty("device").ValueKind == JsonValueKind.Null && withHeader.GetProperty("keepSignedIn").GetBoolean() && withHeader.GetProperty("expiresAt").GetDateTimeOffset() == t0.AddDays(30), "O paciente ignora o computador e mantém a opção.");
        });

        await suite.Check("O token do paciente vale até o último instante do prazo: 24 h (23 h 59 min vale, 24 h não) e 30 dias (29 dias vale, 30 dias não); a sessão curta vence sem afetar a longa", async () =>
        {
            var shortLogin = await Login(cpf, null); var longLogin = await Login(cpf, true);
            var shortClient = Signed(shortLogin); var longClient = Signed(longLogin);
            try
            {
                Ensure(await Me(shortClient) == 200 && await Me(longClient) == 200, "As duas sessões valem no início.");
                f.Clock.Set(t0 + Day - TimeSpan.FromMinutes(1));
                Ensure(await Me(shortClient) == 200, "Aos 23 h 59 min a sessão de 24 h ainda vale.");
                f.Clock.Set(t0 + Day);
                Ensure(await Me(shortClient) == 401 && await Me(longClient) == 200, "Às 24 h a sessão curta vence e a de 30 dias continua.");
                f.Clock.Set(t0.AddDays(29));
                Ensure(await Me(longClient) == 200, "Aos 29 dias a sessão de 30 dias ainda vale.");
                f.Clock.Set(t0.AddDays(30));
                Ensure(await Me(longClient) == 401, "Aos 30 dias a sessão longa vence (sem renovação).");
            }
            finally { f.Clock.Set(t0); }
        });

        await suite.Check("Sessão longa e rotas de paciente: a UBS vinculada, a presença e a oferta continuam valendo no dia 29 e deixam de valer no 30", async () =>
        {
            var login = await Login(cpf, true); var client = Signed(login);
            try
            {
                using (var online = await client.PostAsJsonAsync("/api/patient/presence", new { online = true })) Ensure((int)online.StatusCode is 200 or 204, $"Presença no início: HTTP {(int)online.StatusCode}.");
                f.Clock.Set(t0.AddDays(29));
                using (var units = await client.GetAsync("/api/patient/units")) Expect(units, 200);
                using (var offers = await client.GetAsync($"/api/patient/units/{u1}/lightning-offers")) Expect(offers, 200);
                using (var presence = await client.PostAsJsonAsync("/api/patient/presence", new { online = true })) Ensure((int)presence.StatusCode is 200 or 204, $"Presença no dia 29: HTTP {(int)presence.StatusCode}.");
                f.Clock.Set(t0.AddDays(30));
                using (var gone = await client.GetAsync("/api/patient/units")) Expect(gone, 401);
                using (var presenceGone = await client.PostAsJsonAsync("/api/patient/presence", new { online = true })) Expect(presenceGone, 401);
            }
            finally { f.Clock.Set(t0); }
        });

        await suite.Check("Revogação não depende do prazo: trocar a senha (as outras sessões saem, a atual fica), sair e desativar a conta derrubam as sessões de 24 h e de 30 dias", async () =>
        {
            var changed = Password + "-nova";
            var current = Signed(await Login(cpf, true)); var other24 = Signed(await Login(cpf, null)); var other30 = Signed(await Login(cpf, true));
            using (var change = await current.PostAsJsonAsync("/api/auth/password", new { currentPassword = Password, newPassword = changed })) Ensure((int)change.StatusCode is 200 or 204, $"Troca de senha: HTTP {(int)change.StatusCode}.");
            Ensure(await Me(current) == 200 && await Me(other24) == 401 && await Me(other30) == 401, "A troca de senha mantém só a sessão atual, seja qual for o prazo.");
            using (var logout = await current.PostAsync("/api/auth/logout", null)) Expect(logout, 204);
            Ensure(await Me(current) == 401, "Sair encerra a sessão de 30 dias.");
            var s24 = Signed(await f.LoginAsync(f.NewClient(), "patient", cpf, password: changed)); var s30 = Signed(await f.LoginAsync(f.NewClient(), "patient", cpf, password: changed, keepSignedIn: true));
            Ensure(await Me(s24) == 200 && await Me(s30) == 200, "As duas sessões valem antes de desativar.");
            // Desativar a conta (rota do fluxo legado, ativa no Development do teste) é a revogação por desativação: não olha o prazo da sessão.
            using (var deactivate = await f.Admin.PatchAsJsonAsync($"/api/patients/{f.PatientIds[1]}/active", new { isActive = false })) Expect(deactivate, 204);
            Ensure(await Me(s24) == 401 && await Me(s30) == 401, "Desativar a conta derruba as sessões de 24 h e de 30 dias.");
            using (var blocked = await f.NewClient().PostAsJsonAsync("/api/auth/login", new { scope = "patient", identifier = cpf, password = changed, keepSignedIn = true })) Expect(blocked, 401);
            using (var reactivate = await f.Admin.PatchAsJsonAsync($"/api/patients/{f.PatientIds[1]}/active", new { isActive = true })) Expect(reactivate, 204);
            var again = await f.LoginAsync(f.NewClient(), "patient", cpf, password: changed, keepSignedIn: true);
            Ensure(again.GetProperty("expiresAt").GetDateTimeOffset() == t0.AddDays(30), "Reativada, a conta volta a entrar e a manter a sessão.");
            using (var restore = await Signed(again).PostAsJsonAsync("/api/auth/password", new { currentPassword = changed, newPassword = Password })) Ensure((int)restore.StatusCode is 200 or 204, "A senha original volta para as próximas verificações.");
        });

        await suite.Check("Desktop e equipe inalterados: OBS fora do desktop 8 h (opção ignorada sem computador, 30 dias com computador), Secretaria 8 h, desktop da Secretaria 24 h e 30 dias, desktop do OBS 24 h e 30 dias", async () =>
        {
            var admin = await Login("admin@example.test", true, "obs");
            Ensure(admin.GetProperty("expiresAt").GetDateTimeOffset() == t0.AddHours(8) && !admin.GetProperty("keepSignedIn").GetBoolean(), "O administrador do OBS sem computador continua com 8 h e ignora a opção.");
            var manager = await Login("gestora@example.test", true, "municipal");
            Ensure(manager.GetProperty("expiresAt").GetDateTimeOffset() == t0.AddHours(8) && !manager.GetProperty("keepSignedIn").GetBoolean(), "A Secretaria fora do desktop continua com 8 h e ignora a opção.");
            var (_, header) = await ObsDeviceHelper.ActivateAsync(f.Admin, u1, "Guichê da equipe");
            var withDevice = await Login("atendente@example.test", true, "obs", header);
            Ensure(withDevice.GetProperty("expiresAt").GetDateTimeOffset() == t0.AddDays(30) && withDevice.GetProperty("keepSignedIn").GetBoolean() && withDevice.GetProperty("device").ValueKind == JsonValueKind.Object, "Com computador, o OBS mantém 30 dias.");
            var plain = await Login("atendente@example.test", false, "obs", header);
            Ensure(plain.GetProperty("expiresAt").GetDateTimeOffset() == t0.AddHours(8) && !plain.GetProperty("keepSignedIn").GetBoolean(), "Com computador e sem a opção, o OBS fica com 8 h.");
            // Desktop: Secretaria sem computador; OBS com o cabeçalho do computador (a atendente tem uma única UBS, então entra sem escolher).
            var desktopHeader = ObsDeviceHelper.HeaderValue(Guid.NewGuid(), ObsDeviceCredentials.NewSecret());
            foreach (var (identifier, scope, desktopDevice) in new (string, string, string?)[] { ("gestora@example.test", "municipal", null), ("atendente@example.test", "obs", desktopHeader) })
            {
                var client = f.NewClient(); ObsDeviceHelper.Send(client, desktopDevice);
                using var common = await client.PostAsJsonAsync("/api/auth/login", new { scope, identifier, password = Password, desktopSession = true, deviceName = "Recepção" });
                Expect(common, 200);
                Ensure((await Read(common)).GetProperty("expiresAt").GetDateTimeOffset() == t0 + Day, $"O desktop ({scope}) sem a opção vale 24 h.");
                using var kept = await client.PostAsJsonAsync("/api/auth/login", new { scope, identifier, password = Password, keepSignedIn = true, desktopSession = true, deviceName = "Recepção" });
                Expect(kept, 200);
                var body = await Read(kept);
                Ensure(body.GetProperty("expiresAt").GetDateTimeOffset() == t0.AddDays(30) && body.GetProperty("keepSignedIn").GetBoolean(), $"O desktop ({scope}) com a opção vale 30 dias.");
            }
        });

        await suite.Check("KeepSignedInDays configurado (7 dias) vale também para o paciente; o desktop continua com 30 dias fixos", async () =>
        {
            await using var seven = await StartAsync(connection, "patient_session_days", unitCount: 1, extraArgs: ["--Nora:ObsDevices:KeepSignedInDays", "7"]);
            await seven.PatientAsync(2);
            var login = await seven.LoginAsync(seven.NewClient(), "patient", Cpf(2), keepSignedIn: true);
            Ensure(login.GetProperty("expiresAt").GetDateTimeOffset() == t0.AddDays(7) && login.GetProperty("keepSignedIn").GetBoolean(), "KeepSignedInDays = 7 deveria valer para o paciente.");
            var plain = await seven.LoginAsync(seven.NewClient(), "patient", Cpf(2));
            Ensure(plain.GetProperty("expiresAt").GetDateTimeOffset() == t0 + Day, "Sem a opção continua 24 h.");
            using var desktop = await seven.NewClient().PostAsJsonAsync("/api/auth/login", new { scope = "municipal", identifier = "gestora@example.test", password = Password, keepSignedIn = true, desktopSession = true });
            Expect(desktop, 200);
            Ensure((await Read(desktop)).GetProperty("expiresAt").GetDateTimeOffset() == t0.AddDays(30), "O desktop não segue o KeepSignedInDays.");
        });

        // Por último: a limpeza apaga também as sessões de equipe vencidas há mais de 30 dias do relógio avançado, e os clientes logados antes dela perdem o token.
        await suite.Check("Limpeza: a sessão do paciente só é apagada 30 dias depois de vencer (a curta e a longa), e a sessão ativa nunca", async () =>
        {
            var maintenance = f.Service<MaintenanceService>();
            var shortLogin = await Login(cpf, null); var longLogin = await Login(cpf, true);
            var shortHash = Hash(shortLogin); var longHash = Hash(longLogin);
            async Task<(bool Short, bool Long)> Exists() => (await f.Scalar<long>("SELECT count(*) FROM nora.sessions WHERE token_hash=$1", shortHash) == 1, await f.Scalar<long>("SELECT count(*) FROM nora.sessions WHERE token_hash=$1", longHash) == 1);
            try
            {
                f.Clock.Set(t0 + Day + TimeSpan.FromDays(29)); await maintenance.RunOnceAsync(default);
                Ensure(await Exists() == (true, true), "Vencida há menos de 30 dias a sessão curta ainda fica.");
                f.Clock.Set(t0 + Day + TimeSpan.FromDays(31)); await maintenance.RunOnceAsync(default);
                Ensure(await Exists() == (false, true), "Vencida há mais de 30 dias a sessão curta sai; a longa, ainda ativa, fica.");
                f.Clock.Set(t0.AddDays(61).AddMinutes(1)); await maintenance.RunOnceAsync(default);
                Ensure(await Exists() == (false, false), "Vencida há mais de 30 dias a sessão longa sai.");
            }
            finally { f.Clock.Set(t0); }
        });

        return suite.Report() == 0 ? 0 : 1;

        static string Hash(JsonElement login) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(login.GetProperty("token").GetString()!)));
    }
}
