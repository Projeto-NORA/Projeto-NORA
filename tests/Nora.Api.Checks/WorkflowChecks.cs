using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nora.Api;
using Nora.Api.DAOs;
using Nora.Api.Database;
using Nora.Api.Models;
using Nora.Api.Services;

internal static class WorkflowChecks
{
    public static async Task<int> RunAsync(string root)
    {
        var connectionString = Environment.GetEnvironmentVariable("NORA_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Console.WriteLine("SKIP PostgreSQL: configure NORA_TEST_CONNECTION somente para uma base descartável.");
            return 0;
        }
        var schema = "nora_checks_" + Guid.NewGuid().ToString("N");
        var clock = new CheckClock();
        var checks = 0;
        var failures = 0;
        await using var app = Create();
        var database = app.Services.GetRequiredService<DatabaseHelper>();
        try
        {
            await app.Services.GetRequiredService<MigrationDao>().MigrateAsync();
            await app.Services.GetRequiredService<MigrationDao>().MigrateAsync();
            var accounts = app.Services.GetRequiredService<AccountService>();
            const string password = "Disposable-test-Account!29";
            var accountRequest = new CreatePatientRequest("Operador de teste", "52998224725", "operator@example.test", password);
            await accounts.ProvisionAsync("obs", "administrator", accountRequest);
            await accounts.ProvisionAsync("municipal", "manager", accountRequest);
            await accounts.ProvisionAsync("obs", "attendant", new("Atendente de teste", Cpf(7), "attendant@example.test", password));
            await app.StartAsync();
            using var anonymous = Client(app);
            using var obs = Client(app);
            using var municipal = Client(app);
            using var patient = Client(app);
            using var secondPatient = Client(app);
            Guid firstId = Guid.Empty, secondId = Guid.Empty, thirdId = Guid.Empty, fourthId = Guid.Empty;

            await Check("PostgreSQL real readiness e migration reaplicável", async () =>
            {
                using var response = await anonymous.GetAsync("/health/ready");
                Expect(response, 200);
            });
            await Check("Credencial inválida não autentica", async () =>
            {
                using var response = await anonymous.PostAsJsonAsync("/api/auth/login", new { scope = "obs", identifier = accountRequest.Email, password = "invalid" });
                Expect(response, 401);
            });
            await Check("Login por e-mail e CPF formatado no mesmo escopo", async () =>
            {
                await Login(obs, "obs", accountRequest.Email, password);
                using var byCpf = Client(app);
                await Login(byCpf, "obs", "529.982.247-25", password);
                using var response = await byCpf.GetAsync("/api/auth/me");
                Expect(response, 200);
                using var json = await Json(response);
                Require(json.RootElement.GetProperty("scope").GetString() == "obs", "Escopo divergente.");
                Require(!json.RootElement.TryGetProperty("passwordHash", out _), "Hash de senha exposto.");
            });
            await Check("Municipal isolado e sem acesso nominal", async () =>
            {
                await Login(municipal, "municipal", accountRequest.Email, password);
                using var response = await municipal.GetAsync("/api/patients");
                Expect(response, 403);
                using var unauthorized = await anonymous.GetAsync("/api/municipal/summary");
                Expect(unauthorized, 401);
            });
            await Check("Cadastro pelo OBS persiste e paciente não cadastra terceiros", async () =>
            {
                firstId = await CreatePatient(obs, 1, password, "+55 (48) 99999-0028");
                secondId = await CreatePatient(obs, 2, password);
                await Login(patient, "patient", Cpf(1), password);
                await Login(secondPatient, "patient", "patient2@example.test", password);
                using var response = await patient.PostAsJsonAsync("/api/patients", PatientRequest(3, password));
                Expect(response, 403);
                using var list = await obs.GetAsync("/api/patients");
                Expect(list, 200);
                using var json = await Json(list);
                Require(json.RootElement.GetArrayLength() == 2, "Pacientes não persistidos.");
                Require(json.RootElement[0].GetProperty("id").GetGuid() == secondId, "Listagem não começa pelo cadastro mais recente.");
                Require(json.RootElement.EnumerateArray().All(p => p.GetProperty("createdAt").GetDateTimeOffset().Offset == TimeSpan.Zero), "Cadastro sem data UTC persistida.");
                Require(json.RootElement.EnumerateArray().All(p => p.GetProperty("isActive").GetBoolean()), "Cadastro anterior não preservado como ativo.");
            });
            await Check("Duplicidade CPF/e-mail é conflito e CPF inválido é rejeitado", async () =>
            {
                using var duplicate = await obs.PostAsJsonAsync("/api/patients", PatientRequest(1, password));
                Expect(duplicate, 409);
                using var invalid = await obs.PostAsJsonAsync("/api/patients", new { fullName = "Paciente inválido", cpf = "11111111111", email = "invalid@example.test", password });
                Expect(invalid, 400);
            });
            await Check("Telefone opcional é normalizado, persistido e validado", async () =>
            {
                using (var existing = await obs.GetAsync("/api/patients"))
                using (var list = await Json(existing))
                {
                    Require(list.RootElement.EnumerateArray().Single(p => p.GetProperty("id").GetGuid() == secondId).GetProperty("phone").ValueKind == JsonValueKind.Null, "Cadastro sem telefone incompatível.");
                    Require(list.RootElement.EnumerateArray().Single(p => p.GetProperty("id").GetGuid() == firstId).GetProperty("phone").GetString() == "+5548999990028", "Telefone do paciente não retornado/normalizado.");
                }
                foreach (var invalidPhone in new[] { "abc", "123", "+55<script>", "55+123456789", new string('1', 16) })
                {
                    using var invalid = await obs.PostAsJsonAsync("/api/patients", new { fullName = "Contato inválido", cpf = Cpf(8), email = "invalid-contact@example.test", password, phone = invalidPhone });
                    Expect(invalid, 400);
                }
                // Use uma conta em outro escopo para não alterar os totais de pacientes do fluxo.
                await accounts.ProvisionAsync("obs", "attendant", new("Contato de teste", Cpf(8), "contact@example.test", password, "+55 (48) 99999-0028"));
                await using var connection = await database.OpenAsync();
                await using var command = database.Command(connection, "SELECT phone FROM nora.accounts WHERE email=$1", "contact@example.test");
                Require((string?)await command.ExecuteScalarAsync() == "+5548999990028", "Telefone não normalizado/persistido.");
            });
            await Check("Presença exige paciente autenticado e expira em 90 segundos", async () =>
            {
                using var unauthorized = await anonymous.PostAsync("/api/patient/presence", null);
                Expect(unauthorized, 401);
                using var wrongScope = await obs.PostAsync("/api/patient/presence", null);
                Expect(wrongScope, 403);
                Require(!await Online(obs, firstId), "Login sozinho marcou paciente online.");
                using var heartbeat = await patient.PostAsync("/api/patient/presence", null);
                Expect(heartbeat, 204);
                Require(await Online(obs, firstId) && !await Online(obs, secondId), "Heartbeat alterou presença de outro paciente.");
                clock.Advance(TimeSpan.FromSeconds(89));
                Require(await Online(obs, firstId), "Presença expirou antes do prazo.");
                clock.Advance(TimeSpan.FromSeconds(1));
                Require(!await Online(obs, firstId), "Presença não expirou no limite.");
            });
            await Check("Presença por sessão preserva outro dispositivo e logout encerra presença", async () =>
            {
                using var otherDevice = Client(app);
                await Login(otherDevice, "patient", Cpf(1), password);
                using var firstBeat = await patient.PostAsync("/api/patient/presence", null);
                Expect(firstBeat, 204);
                using var otherBeat = await otherDevice.PostAsync("/api/patient/presence", null);
                Expect(otherBeat, 204);
                using var offline = await patient.PostAsync("/api/patient/presence/offline", null);
                Expect(offline, 204);
                Require(await Online(obs, firstId), "Um dispositivo encerrou a presença do outro.");
                using var logout = await otherDevice.PostAsync("/api/auth/logout", null);
                Expect(logout, 204);
                Require(!await Online(obs, firstId), "Sessão revogada manteve paciente online.");
                using var revokedBeat = await otherDevice.PostAsync("/api/patient/presence", null);
                Expect(revokedBeat, 401);
            });
            await Check("Atendente opera cadastro e fila", async () =>
            {
                using var attendant = Client(app);
                await Login(attendant, "obs", "attendant@example.test", password);
                using var response = await attendant.GetAsync("/api/patients");
                Expect(response, 200);
            });
            await Check("Fila preserva entrada e rejeita duplicidade ativa", async () =>
            {
                using var first = await obs.PostAsJsonAsync("/api/queue", new { patientId = firstId });
                Expect(first, 201);
                clock.Advance(TimeSpan.FromSeconds(1));
                using var second = await obs.PostAsJsonAsync("/api/queue", new { patientId = secondId });
                Expect(second, 201);
                using var duplicate = await obs.PostAsJsonAsync("/api/queue", new { patientId = firstId });
                Expect(duplicate, 409);
                using var list = await obs.GetAsync("/api/queue");
                using var json = await Json(list);
                Require(json.RootElement[0].GetProperty("patientId").GetGuid() == firstId, "Primeiro paciente fora de ordem.");
            });
            await Check("Vaga oferece ao primeiro e prazo é dez minutos", async () =>
            {
                using var response = await obs.PostAsJsonAsync("/api/slots", new { label = "Oportunidade administrativa de teste" });
                Expect(response, 201);
                using var overview = await patient.GetAsync("/api/patient/overview");
                using var json = await Json(overview);
                var offers = json.RootElement.GetProperty("offers");
                Require(offers.GetArrayLength() == 1, "Primeiro paciente sem oferta.");
                Require(offers[0].GetProperty("expiresAt").GetDateTimeOffset() == clock.GetUtcNow().AddMinutes(10), "Prazo não é dez minutos.");
                using var otherOverview = await secondPatient.GetAsync("/api/patient/overview");
                using var other = await Json(otherOverview);
                Require(other.RootElement.GetProperty("offers").GetArrayLength() == 0, "Paciente seguinte recebeu simultaneamente.");
            });
            await Check("Paciente não responde oferta alheia; recusa passa ao próximo", async () =>
            {
                var offer = await ActiveOffer(patient);
                using var missingDecision = await patient.PostAsJsonAsync($"/api/offers/{offer}/respond", new { });
                Expect(missingDecision, 400);
                Require(await ActiveOffer(patient) == offer, "Resposta sem decisão alterou a oferta.");
                using var forbidden = await secondPatient.PostAsJsonAsync($"/api/offers/{offer}/respond", new { accept = true });
                Require((int)forbidden.StatusCode is 403 or 404, "Oferta alheia acessível.");
                using var declined = await patient.PostAsJsonAsync($"/api/offers/{offer}/respond", new { accept = false });
                Expect(declined, 200);
                Require(await ActiveOffer(secondPatient) != Guid.Empty, "Recusa não passou oferta.");
            });
            await Check("Aceite concorrente confirma uma única vez", async () =>
            {
                var offer = await ActiveOffer(secondPatient);
                var responses = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => secondPatient.PostAsJsonAsync($"/api/offers/{offer}/respond", new { accept = true })));
                try
                {
                    Require(responses.Count(r => r.StatusCode == HttpStatusCode.OK) == 1, "Mais de um aceite confirmou.");
                    Require(responses.Count(r => (int)r.StatusCode == 409) == 1, "Segundo aceite não conflitou.");
                }
                finally { foreach (var response in responses) response.Dispose(); }
            });
            await Check("Municipal recebe totais agregados sem CPF/e-mail/nomes", async () =>
            {
                using var response = await municipal.GetAsync("/api/municipal/summary");
                Expect(response, 200);
                var body = await response.Content.ReadAsStringAsync();
                using var json = JsonDocument.Parse(body);
                Require(json.RootElement.GetProperty("patients").GetInt64() == 2, "Total incorreto.");
                Require(json.RootElement.GetProperty("acceptedOffers").GetInt64() == 1, "Aceites incorretos.");
                Require(!body.Contains("example.test") && !body.Contains("Paciente") && !body.Contains(Cpf(1)), "Resumo expõe identidade.");
            });
            await Check("Expiração no limite passa ao próximo sem aceitar oferta vencida", async () =>
            {
                thirdId = await CreatePatient(obs, 3, password);
                fourthId = await CreatePatient(obs, 4, password);
                // Cancele toda entrada ainda ativa; esta vaga testa um fluxo independente.
                using (var response = await obs.GetAsync("/api/queue"))
                using (var json = await Json(response))
                    foreach (var entry in json.RootElement.EnumerateArray().Where(e => e.GetProperty("status").GetString() == "waiting"))
                    {
                        using var cancel = await obs.PostAsync($"/api/queue/{entry.GetProperty("id").GetInt64()}/cancel", null);
                        Expect(cancel, 204);
                    }
                using var third = Client(app);
                using var fourth = Client(app);
                await Login(third, "patient", Cpf(3), password);
                await Login(fourth, "patient", Cpf(4), password);
                using var enqueueThird = await obs.PostAsJsonAsync("/api/queue", new { patientId = thirdId });
                Expect(enqueueThird, 201);
                clock.Advance(TimeSpan.FromSeconds(1));
                using var enqueueFourth = await obs.PostAsJsonAsync("/api/queue", new { patientId = fourthId });
                Expect(enqueueFourth, 201);
                using var newSlot = await obs.PostAsJsonAsync("/api/slots", new { label = "Teste de expiração" });
                Expect(newSlot, 201);
                var expiredOffer = await ActiveOffer(third);
                clock.Advance(TimeSpan.FromMinutes(10));
                using var lateAccept = await third.PostAsJsonAsync($"/api/offers/{expiredOffer}/respond", new { accept = true });
                Expect(lateAccept, 409);
                Require(await ActiveOffer(fourth) != Guid.Empty, "Expiração não passou oferta.");
            });
            await Check("Alteração de conta exige OBS e decisão explícita; atendente e administrador permitidos", async () =>
            {
                using var unauthorized = await anonymous.PatchAsJsonAsync($"/api/patients/{firstId}/active", new { isActive = false });
                Expect(unauthorized, 401);
                using var wrongScope = await municipal.PatchAsJsonAsync($"/api/patients/{firstId}/active", new { isActive = false });
                Expect(wrongScope, 403);
                using var patientScope = await patient.PatchAsJsonAsync($"/api/patients/{firstId}/active", new { isActive = false });
                Expect(patientScope, 403);
                using var missing = await obs.PatchAsJsonAsync($"/api/patients/{firstId}/active", new { });
                Expect(missing, 400);
                using var unknown = await obs.PatchAsJsonAsync($"/api/patients/{Guid.NewGuid()}/active", new { isActive = false });
                Expect(unknown, 404);
                using var me = await obs.GetAsync("/api/auth/me");
                using var operatorJson = await Json(me);
                using var operatorChange = await obs.PatchAsJsonAsync($"/api/patients/{operatorJson.RootElement.GetProperty("id").GetGuid()}/active", new { isActive = false });
                Expect(operatorChange, 404);
                using var attendant = Client(app);
                await Login(attendant, "obs", "attendant@example.test", password);
                using var allowed = await attendant.PatchAsJsonAsync($"/api/patients/{firstId}/active", new { isActive = true });
                Expect(allowed, 204);
            });
            await Check("Desativação revoga sessões, bloqueia login/fila/presença e reativação preserva cadastro", async () =>
            {
                var before = await PatientRow(obs, firstId);
                var session = await app.Services.GetRequiredService<AuthService>().AuthenticateAsync(patient.DefaultRequestHeaders.Authorization!.Parameter!, default);
                Require(session is not null, "Sessão de teste ausente.");
                using var enqueue = await obs.PostAsJsonAsync("/api/queue", new { patientId = firstId });
                Expect(enqueue, 201);
                using var disabled = await obs.PatchAsJsonAsync($"/api/patients/{firstId}/active", new { isActive = false });
                Expect(disabled, 204);
                using var login = await anonymous.PostAsJsonAsync("/api/auth/login", new { scope = "patient", identifier = Cpf(1), password });
                Expect(login, 401);
                using var oldSession = await patient.GetAsync("/api/auth/me");
                Expect(oldSession, 401);
                using var heartbeat = await patient.PostAsync("/api/patient/presence", null);
                Expect(heartbeat, 401);
                using var inactiveQueue = await obs.PostAsJsonAsync("/api/queue", new { patientId = firstId });
                Expect(inactiveQueue, 409);
                Require(await app.Services.GetRequiredService<SessionDao>().CreateAsync(firstId, new string('A',64), clock.GetUtcNow().AddHours(8), default) == 0, "Login concorrente persistiu sessão de conta desativada.");
                try
                {
                    await app.Services.GetRequiredService<WorkflowService>().OverviewAsync(session!, default);
                    throw new InvalidOperationException("Sessão pré-autenticada antiga conservou acesso.");
                }
                catch (ApiUnauthorizedException) { }
                await app.Services.GetRequiredService<MigrationDao>().MigrateAsync();
                var after = await PatientRow(obs, firstId);
                Require(!after.GetProperty("isActive").GetBoolean() && !after.GetProperty("isOnline").GetBoolean(), "Migração/presença reativou cadastro.");
                foreach (var field in new[] { "fullName", "cpf", "email", "phone", "createdAt" })
                    Require(before.GetProperty(field).GetRawText() == after.GetProperty(field).GetRawText(), "Desativação alterou dados do cadastro.");
                await NoActiveQueue(obs, firstId);
                using var enabled = await obs.PatchAsJsonAsync($"/api/patients/{firstId}/active", new { isActive = true });
                Expect(enabled, 204);
                using var stillRevoked = await patient.GetAsync("/api/auth/me");
                Expect(stillRevoked, 401);
                await Login(patient, "patient", Cpf(1), password);
                await NoActiveQueue(obs, firstId);
                Require((await PatientRow(obs, firstId)).GetProperty("isActive").GetBoolean(), "Reativação não liberou a conta.");
            });
            await Check("Desativar oferta pendente libera a mesma vaga ao próximo FIFO e preserva aceites", async () =>
            {
                using var fourth = Client(app);
                using var third = Client(app);
                await Login(fourth, "patient", Cpf(4), password);
                await Login(third, "patient", Cpf(3), password);
                var pendingId = await ActiveOffer(fourth);
                var oldSession = await app.Services.GetRequiredService<AuthService>().AuthenticateAsync(fourth.DefaultRequestHeaders.Authorization!.Parameter!,default);
                Require(pendingId != Guid.Empty, "Fixture sem oferta pendente.");
                using var enqueue = await obs.PostAsJsonAsync("/api/queue", new { patientId = thirdId });
                Expect(enqueue, 201);
                using var disabled = await obs.PatchAsJsonAsync($"/api/patients/{fourthId}/active", new { isActive = false });
                Expect(disabled, 204);
                using var oldAccess = await fourth.GetAsync("/api/patient/overview");
                Expect(oldAccess, 401);
                try
                {
                    await app.Services.GetRequiredService<WorkflowService>().RespondAsync(pendingId,oldSession!,new(true),default);
                    throw new InvalidOperationException("Resposta pré-autenticada confirmou depois da desativação.");
                }
                catch(ApiUnauthorizedException) { }
                var nextId = await ActiveOffer(third);
                Require(nextId != Guid.Empty, "Desativação não passou oferta ao próximo.");
                await using var connection = await database.OpenAsync();
                await using var sameSlot = database.Command(connection, "SELECT old.status='cancelled' AND old.slot_id=next.slot_id FROM nora.offers old JOIN nora.offers next ON next.id=$2 WHERE old.id=$1", pendingId, nextId);
                Require(await sameSlot.ExecuteScalarAsync() is true, "Oferta cancelada não liberou sua vaga original.");
                using var historical = await obs.PatchAsJsonAsync($"/api/patients/{secondId}/active", new { isActive = false });
                Expect(historical, 204);
                await using var history = database.Command(connection, "SELECT EXISTS(SELECT 1 FROM nora.offers o JOIN nora.queue_entries q ON q.id=o.queue_id JOIN nora.slots s ON s.id=o.slot_id WHERE o.patient_id=$1 AND o.status='accepted' AND q.status='completed' AND s.status='accepted')", secondId);
                Require(await history.ExecuteScalarAsync() is true, "Desativação apagou/alterou o aceite concluído.");
                using var enabled = await obs.PatchAsJsonAsync($"/api/patients/{secondId}/active", new { isActive = true });
                Expect(enabled, 204);
                using var oldToken = await secondPatient.GetAsync("/api/auth/me");
                Expect(oldToken, 401);
                await Login(secondPatient, "patient", Cpf(2), password);
                using var fourthEnabled = await obs.PatchAsJsonAsync($"/api/patients/{fourthId}/active", new { isActive = true });
                Expect(fourthEnabled, 204);
                await NoActiveQueue(obs, fourthId);
            });
            await Check("Corrida login/inclusão versus desativação não deixa sessões ou fila ativas", async () =>
            {
                var loginTask = anonymous.PostAsJsonAsync("/api/auth/login", new { scope = "patient", identifier = Cpf(1), password });
                var enqueueTask = obs.PostAsJsonAsync("/api/queue", new { patientId = firstId });
                var disableTask = obs.PatchAsJsonAsync($"/api/patients/{firstId}/active", new { isActive = false });
                await Task.WhenAll(loginTask,enqueueTask,disableTask);
                using var login = await loginTask;
                using var enqueue = await enqueueTask;
                using var disable = await disableTask;
                Expect(disable,204);
                Require((int)login.StatusCode is 200 or 401, "Login concorrente retornou erro inesperado.");
                Require((int)enqueue.StatusCode is 201 or 409, "Fila concorrente retornou erro inesperado.");
                await NoActiveQueue(obs, firstId);
                await using var connection = await database.OpenAsync();
                await using var liveSessions = database.Command(connection,"SELECT count(*) FROM nora.sessions WHERE account_id=$1 AND revoked_at IS NULL AND expires_at>$2",firstId,clock.GetUtcNow());
                Require((long)(await liveSessions.ExecuteScalarAsync())! == 0,"Corrida deixou sessão utilizável após desativação.");
                using var enable = await obs.PatchAsJsonAsync($"/api/patients/{firstId}/active",new { isActive = true });
                Expect(enable,204);
                await Login(patient,"patient",Cpf(1),password);
            });
            await Check("Resposta concorrente à desativação preserva uma única transição válida", async () =>
            {
                using var fourth = Client(app);
                await Login(fourth,"patient",Cpf(4),password);
                using var enqueue = await obs.PostAsJsonAsync("/api/queue",new { patientId=fourthId });
                Expect(enqueue,201);
                using var slot = await obs.PostAsJsonAsync("/api/slots",new { label="Corrida desativação versus resposta" });
                Expect(slot,201);
                var offerId = await ActiveOffer(fourth);
                Require(offerId!=Guid.Empty,"Fixture de corrida sem oferta.");
                var respondTask=fourth.PostAsJsonAsync($"/api/offers/{offerId}/respond",new { accept=true });
                var disableTask=obs.PatchAsJsonAsync($"/api/patients/{fourthId}/active",new { isActive=false });
                await Task.WhenAll(respondTask,disableTask);
                using var response=await respondTask;
                using var disabled=await disableTask;
                Expect(disabled,204);
                Require((int)response.StatusCode is 200 or 401,"Resposta concorrente produziu resultado inválido.");
                await NoActiveQueue(obs,fourthId);
                await using var connection=await database.OpenAsync();
                await using var state=database.Command(connection,"SELECT o.status,q.status,s.status FROM nora.offers o JOIN nora.queue_entries q ON q.id=o.queue_id JOIN nora.slots s ON s.id=o.slot_id WHERE o.id=$1",offerId);
                await using var reader=await state.ExecuteReaderAsync();
                Require(await reader.ReadAsync(),"Corrida perdeu histórico.");
                var accepted=response.StatusCode==HttpStatusCode.OK;
                Require(reader.GetString(0)==(accepted?"accepted":"cancelled") && reader.GetString(1)==(accepted?"completed":"cancelled"),"Resposta/desativação deixaram oferta e fila incompatíveis.");
                Require(accepted ? reader.GetString(2)=="accepted" : reader.GetString(2) is "open" or "offered","Resposta/desativação deixaram vaga incompatível.");
                using var oldAccess=await fourth.GetAsync("/api/auth/me");
                Expect(oldAccess,401);
            });
            await Check("Logout revoga token e token ausente/malformado é rejeitado", async () =>
            {
                using var logout = await obs.PostAsync("/api/auth/logout", null);
                Expect(logout, 204);
                using var me = await obs.GetAsync("/api/auth/me");
                Expect(me, 401);
                using var malformed = Client(app);
                malformed.DefaultRequestHeaders.Authorization = new("Bearer", "not-a-session");
                using var invalid = await malformed.GetAsync("/api/auth/me");
                Expect(invalid, 401);
            });
            await Check("Sessão expira e dados sobrevivem a novo host", async () =>
            {
                clock.Advance(TimeSpan.FromHours(8));
                using var expired = await municipal.GetAsync("/api/auth/me");
                Expect(expired, 401);
                await app.StopAsync();
                await using var reopened = Create();
                await reopened.StartAsync();
                using var newClient = Client(reopened);
                await Login(newClient, "obs", accountRequest.Email, password);
                using var list = await newClient.GetAsync("/api/patients");
                Expect(list, 200);
                using var json = await Json(list);
                Require(json.RootElement.GetArrayLength() == 4, "Reabertura perdeu persistência.");
                Require(json.RootElement.EnumerateArray().Single(p => p.GetProperty("id").GetGuid() == firstId).GetProperty("phone").GetString() == "+5548999990028", "Reabertura perdeu telefone.");
                await reopened.StopAsync();
            });
        }
        catch (Exception error)
        {
            failures++;
            Console.WriteLine($"FAIL Setup PostgreSQL: {error.GetType().Name}. Configuração e credenciais omitidas.");
        }
        finally
        {
            await app.StopAsync();
            // Somente o schema aleatório criado nesta execução é removido; nunca a base.
            await using var connection = await database.OpenAsync();
            await database.ExecuteAsync(connection, $"DROP SCHEMA IF EXISTS {schema} CASCADE", default);
        }
        Console.WriteLine($"{checks - failures}/{checks} verificações PostgreSQL/fluxos passaram.");
        return failures;

        WebApplication Create() => NoraApiHost.Create(new()
        {
            Args = ["--urls", "http://127.0.0.1:0", "--ConnectionStrings:Nora", connectionString, "--Nora:Schema", schema],
            EnvironmentName = "Development",
            ApplicationName = typeof(NoraApiHost).Assembly.GetName().Name,
            ContentRootPath = Path.Combine(root, "Nora.Api")
        }, services => services.Replace(ServiceDescriptor.Singleton<TimeProvider>(clock)));

        async Task Check(string name, Func<Task> action)
        {
            checks++;
            try { await action(); Console.WriteLine($"PASS [PostgreSQL] {name}"); }
            catch (Exception error) { failures++; Console.WriteLine($"FAIL [PostgreSQL] {name}: {error.Message}"); }
        }

        async Task<bool> Online(HttpClient obs, Guid id)
        {
            using var list = await obs.GetAsync("/api/patients");
            Expect(list, 200);
            using var json = await Json(list);
            return json.RootElement.EnumerateArray().Single(p => p.GetProperty("id").GetGuid() == id).GetProperty("isOnline").GetBoolean();
        }

        async Task<JsonElement> PatientRow(HttpClient obs, Guid id)
        {
            using var response = await obs.GetAsync("/api/patients");
            Expect(response,200);
            using var json = await Json(response);
            return json.RootElement.EnumerateArray().Single(p=>p.GetProperty("id").GetGuid()==id).Clone();
        }

        async Task NoActiveQueue(HttpClient obs, Guid id)
        {
            using var response = await obs.GetAsync("/api/queue");
            Expect(response,200);
            using var json = await Json(response);
            Require(!json.RootElement.EnumerateArray().Any(q=>q.GetProperty("patientId").GetGuid()==id && q.GetProperty("status").GetString() is "waiting" or "offered"),"Conta desativada/reativada conservou fila pendente.");
        }
    }

    private static HttpClient Client(WebApplication app) => new()
    {
        BaseAddress = new(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single()),
        Timeout = TimeSpan.FromSeconds(20)
    };
    private static async Task<string> Login(HttpClient client, string scope, string identifier, string password)
    {
        using var response = await client.PostAsJsonAsync("/api/auth/login", new { scope, identifier, password });
        Expect(response, 200);
        using var json = await Json(response);
        var token = json.RootElement.GetProperty("token").GetString()!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return token;
    }
    private static object PatientRequest(int number, string password) => new { fullName = $"Paciente de teste {number}", cpf = Cpf(number), email = $"patient{number}@example.test", password };
    private static async Task<Guid> CreatePatient(HttpClient obs, int number, string password, string? phone = null)
    {
        using var response = await obs.PostAsJsonAsync("/api/patients", new { fullName = $"Paciente de teste {number}", cpf = Cpf(number), email = $"patient{number}@example.test", password, phone });
        Expect(response, 201);
        using var json = await Json(response);
        Require(json.RootElement.GetProperty("createdAt").GetDateTimeOffset().Offset == TimeSpan.Zero, "Resposta de cadastro sem data UTC real.");
        Require(json.RootElement.GetProperty("isActive").GetBoolean(), "Cadastro novo não foi criado ativo.");
        return json.RootElement.GetProperty("id").GetGuid();
    }
    private static async Task<Guid> ActiveOffer(HttpClient patient)
    {
        using var response = await patient.GetAsync("/api/patient/overview");
        Expect(response, 200);
        using var json = await Json(response);
        var offers = json.RootElement.GetProperty("offers");
        var active = offers.EnumerateArray().FirstOrDefault(e => e.GetProperty("status").GetString() is "pending" or "offered");
        return active.ValueKind == JsonValueKind.Undefined ? Guid.Empty : active.GetProperty("id").GetGuid();
    }
    private static Task<JsonDocument> Json(HttpResponseMessage response) => Read(response);
    private static async Task<JsonDocument> Read(HttpResponseMessage response) => JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    private static void Expect(HttpResponseMessage response, int status) => Require((int)response.StatusCode == status, $"Esperado HTTP {status}; recebido {(int)response.StatusCode}.");
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
    private sealed class CheckClock : TimeProvider
    {
        private long ticks = new DateTimeOffset(2026, 10, 7, 12, 0, 0, TimeSpan.Zero).UtcTicks;
        public override DateTimeOffset GetUtcNow() => new(Interlocked.Read(ref ticks), TimeSpan.Zero);
        public void Advance(TimeSpan value) => Interlocked.Add(ref ticks, value.Ticks);
    }
}
