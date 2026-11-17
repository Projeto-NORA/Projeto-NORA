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
        var registrationMail = new TestRegistrationMail();
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
            await Check("Secretaria cadastra e altera UBS com coordenadas e autorização", async () =>
            {
                var draft = new { name = "UBS sintética", region = "Centro", address = "Rua de teste, 10", latitude = -23.55, longitude = -46.63 };
                using var anonymousList = await anonymous.GetAsync("/api/municipal/units"); Expect(anonymousList, 401);
                using var wrongScope = await obs.PostAsJsonAsync("/api/municipal/units", draft); Expect(wrongScope, 403);
                using var invalid = await municipal.PostAsJsonAsync("/api/municipal/units", new { name = "UBS", region = "Centro", address = "Rua", latitude = 99, longitude = -46.63 }); Expect(invalid, 400);
                using var created = await municipal.PostAsJsonAsync("/api/municipal/units", draft); Expect(created, 201);
                using var body = await Json(created); var id = body.RootElement.GetProperty("id").GetGuid();
                using var changed = await municipal.PutAsJsonAsync($"/api/municipal/units/{id}", new { name = "UBS atualizada", region = "Norte", address = "Rua 20", latitude = -21.5, longitude = -45.5 }); Expect(changed, 200);
                using var list = await municipal.GetAsync("/api/municipal/units"); Expect(list, 200);
                using var units = await Json(list); var unit = units.RootElement.EnumerateArray().Single(u => u.GetProperty("id").GetGuid() == id);
                Require(unit.GetProperty("name").GetString() == "UBS atualizada" && unit.GetProperty("latitude").GetDouble() == -21.5, "Unidade alterada não persistiu.");
                using var missing = await municipal.PutAsJsonAsync($"/api/municipal/units/{Guid.NewGuid()}", draft); Expect(missing, 404);
                using var catalog = await anonymous.GetAsync("/api/registration/units"); Expect(catalog, 200);
                using var catalogBody = await Json(catalog);
                var catalogUnit = catalogBody.RootElement.GetProperty("units").EnumerateArray().Single(u => u.GetProperty("id").GetGuid() == id);
                Require(catalogUnit.GetProperty("name").GetString() == "UBS atualizada" && catalogUnit.EnumerateObject().Count() == 3, "Catálogo público retornou dados incorretos ou adicionais.");

            });
            await Check("Gestão controla UBS autorizadas por conta OBS e registra autoria", async () =>
            {
                using var identity = await obs.GetAsync("/api/auth/me"); Expect(identity, 200);
                using var identityBody = await Json(identity); var obsId = identityBody.RootElement.GetProperty("id").GetGuid();
                using var unitResponse = await municipal.GetAsync("/api/municipal/units"); Expect(unitResponse, 200);
                using var unitsBody = await Json(unitResponse); var unitId = unitsBody.RootElement.EnumerateArray().Single(u => u.GetProperty("name").GetString() == "UBS atualizada").GetProperty("id").GetGuid();
                using var before = await obs.GetAsync("/api/obs/units"); Expect(before, 200);
                using var beforeBody = await Json(before); Require(beforeBody.RootElement.GetArrayLength() == 0, "Conta recebeu acesso sem concessão.");
                using var denied = await obs.PutAsync($"/api/municipal/staff/{obsId}/units/{unitId}", null); Expect(denied, 403);
                using var grant = await municipal.PutAsync($"/api/municipal/staff/{obsId}/units/{unitId}", null); Expect(grant, 204);
                using var assigned = await obs.GetAsync("/api/obs/units"); Expect(assigned, 200);
                using var assignedBody = await Json(assigned); Require(assignedBody.RootElement.EnumerateArray().Any(u => u.GetProperty("id").GetGuid() == unitId), "Concessão não apareceu na conta.");
                using var revoke = await municipal.DeleteAsync($"/api/municipal/staff/{obsId}/units/{unitId}"); Expect(revoke, 204);
                using var after = await obs.GetAsync("/api/obs/units"); Expect(after, 200);
                using var afterBody = await Json(after); Require(afterBody.RootElement.GetArrayLength() == 0, "Revogação não removeu o vínculo.");
                await using var connection = await database.OpenAsync();
                await using var audit = database.Command(connection, "SELECT count(*) FROM nora.staff_unit_events e JOIN nora.accounts a ON a.id=e.actor_id WHERE e.account_id=$1 AND e.unit_id=$2 AND a.scope='municipal'", obsId, unitId);
                Require((long)(await audit.ExecuteScalarAsync())! == 2, "Histórico sem concessão e revogação com autor.");
            });
            var unitDatabaseConnection = Environment.GetEnvironmentVariable("NORA_TEST_UNIT_C_CONNECTION");
            if (!string.IsNullOrWhiteSpace(unitDatabaseConnection))
            await Check("Cadastro da UBS exige vínculo OBS e grava apenas no banco da unidade", async () =>
            {
                using var identity = await obs.GetAsync("/api/auth/me"); Expect(identity, 200);
                using var identityBody = await Json(identity); var obsId = identityBody.RootElement.GetProperty("id").GetGuid();
                using var unitResponse = await municipal.GetAsync("/api/municipal/units"); Expect(unitResponse, 200);
                using var unitsBody = await Json(unitResponse); var unitId = unitsBody.RootElement.EnumerateArray().Single(u => u.GetProperty("name").GetString() == "UBS atualizada").GetProperty("id").GetGuid();
                app.Configuration[$"Nora:UnitDatabases:{unitId:N}"] = unitDatabaseConnection;
                await app.Services.GetRequiredService<UnitMigrationDao>().MigrateAsync(unitId);
                var path = $"/api/obs/units/{unitId}/registry";
                using var unauthenticated = await anonymous.GetAsync(path); Expect(unauthenticated, 401);
                using var denied = await obs.GetAsync(path); Expect(denied, 403);
                using var deniedQueue = await obs.GetAsync($"/api/obs/units/{unitId}/waiting-list"); Expect(deniedQueue, 403);
                using var wrongScope = await municipal.GetAsync(path); Expect(wrongScope, 403);
                using var grant = await municipal.PutAsync($"/api/municipal/staff/{obsId}/units/{unitId}", null); Expect(grant, 204);
                var data = new { cpf = "52998224725", fullName = "Cadastro UBS Sintético", email = "registered@example.test", phone = "+55 (48) 99999-0028", address = new { cep = "01001-000", street = "Praça da Sé", number = "10", complement = (string?)null, neighborhood = "Sé", city = "São Paulo", state = "SP", ibge = "3550308" } };
                using var created = await obs.PostAsJsonAsync(path, data); Expect(created, 201);
                using var duplicate = await obs.PostAsJsonAsync(path, data); Expect(duplicate, 409);
                using var listed = await obs.GetAsync(path); Expect(listed, 200);
                using var body = await Json(listed);
                Require(body.RootElement.EnumerateArray().Single().GetProperty("cpf").GetString() == "52998224725", "Cadastro não foi retornado para a UBS autorizada.");
                var registeredRecord = body.RootElement.EnumerateArray().Single();
                Require(registeredRecord.GetProperty("phone").GetString() == "+5548999990028" && registeredRecord.GetProperty("address").GetProperty("cep").GetString() == "01001000", "Contato e endereço não foram persistidos na UBS.");
                var edit = new { cpf = "52998224725", fullName = "Cadastro Atualizado", email = "changed@example.test", phone = "(11) 99999-0022", address = new { cep = "01001000", street = "Praça da Sé", number = "20", complement = (string?)null, neighborhood = "Sé", city = "São Paulo", state = "SP", ibge = "3550308" }, isActive = false };
                using (var edited = await obs.PutAsJsonAsync(path, edit)) Expect(edited, 204);
                using (var afterEdit = await obs.GetAsync(path))
                {
                    Expect(afterEdit, 200); using var editedBody = await Json(afterEdit); var record = editedBody.RootElement.EnumerateArray().Single();
                    Require(record.GetProperty("fullName").GetString() == "Cadastro Atualizado" && record.GetProperty("phone").GetString() == "11999990022" && record.GetProperty("address").GetProperty("number").GetString() == "20" && record.GetProperty("isActive").GetBoolean(), "Edição não persistiu ou alterou a situação sem confirmação.");
                }
                await using (var unitDb = await app.Services.GetRequiredService<UnitDatabaseRouter>().OpenAsync(unitId))
                await using (var editAudit = new Npgsql.NpgsqlCommand("SELECT actor_id FROM nora_unit.patient_registry_events WHERE action='updated'", unitDb))
                    Require(await editAudit.ExecuteScalarAsync() is Guid editActor && editActor == obsId, "Edição não registrou autoria.");
                var emailPath = path + "/email";
                using (var wrongRole = await municipal.PatchAsJsonAsync(emailPath, new { cpf = "52998224725", email = "new@example.test" })) Expect(wrongRole, 403);
                using (var invalidEmail = await obs.PatchAsJsonAsync(emailPath, new { cpf = "52998224725", email = "inválido" })) Expect(invalidEmail, 400);
                using (var changed = await obs.PatchAsJsonAsync(emailPath, new { cpf = "52998224725", email = "NEW@example.test" })) Expect(changed, 204);
                using (var updated = await obs.GetAsync(path))
                {
                    Expect(updated, 200); using var updatedBody = await Json(updated);
                    Require(updatedBody.RootElement.EnumerateArray().Single().GetProperty("email").GetString() == "new@example.test", "E-mail corrigido não foi persistido.");
                }
                await using (var unitDb = await app.Services.GetRequiredService<UnitDatabaseRouter>().OpenAsync(unitId))
                await using (var changedAudit = new Npgsql.NpgsqlCommand("SELECT actor_id FROM nora_unit.patient_registry_events WHERE action='email_updated'", unitDb))
                    Require(await changedAudit.ExecuteScalarAsync() is Guid editor && editor == obsId, "Correção de e-mail não registrou autoria.");
                var schedulePath = $"/api/obs/units/{unitId}/appointments";
                var start = clock.GetUtcNow().AddDays(2); var end = start.AddMinutes(30);
                var appointment = new { cpf = "52998224725", specialty = "Clínica Geral", professional = "Profissional Sintético", startsAt = start, endsAt = end };
                using var createdAppointment = await obs.PostAsJsonAsync(schedulePath, appointment); Expect(createdAppointment, 201);
                using var appointmentBody = await Json(createdAppointment); var appointmentId = appointmentBody.RootElement.GetProperty("id").GetGuid();
                var waitingPath = $"/api/obs/units/{unitId}/waiting-list";
                using var firstWaiting = await obs.PostAsJsonAsync(waitingPath, new { cpf = "52998224725", specialty = "Cardiologia" }); Expect(firstWaiting, 201);
                using var firstWaitingBody = await Json(firstWaiting); var waitingId = firstWaitingBody.RootElement.GetProperty("id").GetInt64();
                using (var duplicateWaiting = await obs.PostAsJsonAsync(waitingPath, new { cpf = "52998224725", specialty = "cardiologia" })) Expect(duplicateWaiting, 409);
                using (var missingPatient = await obs.PostAsJsonAsync(waitingPath, new { cpf = Cpf(1), specialty = "Cardiologia" })) Expect(missingPatient, 404);
                await app.Services.GetRequiredService<UnitPatientRegistryDao>().CreateAsync(unitId, obsId, new UnitPatientRecord(Cpf(9), "Segundo paciente", "queue2@example.test"));
                using var nextWaiting = await obs.PostAsJsonAsync(waitingPath, new { cpf = Cpf(9), specialty = "Cardiologia" }); Expect(nextWaiting, 201);
                using var nextBody = await Json(nextWaiting); var nextId = nextBody.RootElement.GetProperty("id").GetInt64();
                using (var withoutConfirm = await obs.PostAsJsonAsync($"{waitingPath}/{waitingId}/cancel", new { confirmed = false })) Expect(withoutConfirm, 400);
                using (var waitingList = await obs.GetAsync(waitingPath))
                { Expect(waitingList, 200); using var listBody = await Json(waitingList); var entries = listBody.RootElement.EnumerateArray().ToArray(); Require(entries.Length == 2 && entries[0].GetProperty("id").GetInt64() == waitingId && entries[1].GetProperty("id").GetInt64() == nextId && entries[0].GetProperty("status").GetString() == "waiting", "Ordem de entrada ou confirmação não foram preservadas."); }
                using (var confirmedCancel = await obs.PostAsJsonAsync($"{waitingPath}/{waitingId}/cancel", new { confirmed = true })) Expect(confirmedCancel, 204);
                await using (var unitDb = await app.Services.GetRequiredService<UnitDatabaseRouter>().OpenAsync(unitId))
                await using (var waitingAudit = new Npgsql.NpgsqlCommand("SELECT count(*) FROM nora_unit.waiting_list_events WHERE entry_id=$1 AND actor_id=$2", unitDb))
                { waitingAudit.Parameters.AddWithValue(waitingId); waitingAudit.Parameters.AddWithValue(obsId); Require((long)(await waitingAudit.ExecuteScalarAsync())! == 2, "Fila não registrou inclusão e cancelamento com autor."); }
                var managementSchedule = $"/api/municipal/units/{unitId}/appointments?from={Uri.EscapeDataString(start.AddDays(-1).ToString("O"))}&to={Uri.EscapeDataString(end.AddDays(1).ToString("O"))}";
                using (var deniedManagement = await obs.GetAsync(managementSchedule)) Expect(deniedManagement, 403);
                using (var management = await municipal.GetAsync(managementSchedule))
                {
                    Expect(management, 200); using var managementBody = await Json(management);
                    Require(managementBody.RootElement.EnumerateArray().Any(item => item.GetProperty("id").GetGuid() == appointmentId), "Secretaria não consultou a agenda da UBS.");
                }
                await using (var auditConnection = await database.OpenAsync())
                await using (var audit = database.Command(auditConnection, "SELECT count(*) FROM nora.unit_access_events WHERE unit_id=$1 AND action='appointment_read'", unitId))
                    Require((long)(await audit.ExecuteScalarAsync())! == 1, "Consulta da agenda pela Secretaria não foi auditada.");
                using (var overlap = await obs.PostAsJsonAsync(schedulePath, new { cpf = "52998224725", specialty = "Clínica Geral", professional = "profissional sintético", startsAt = start.AddMinutes(10), endsAt = end.AddMinutes(10) })) Expect(overlap, 409);
                using (var patientOverlap = await obs.PostAsJsonAsync(schedulePath, new { cpf = "52998224725", specialty = "Clínica Geral", professional = "Outro profissional", startsAt = start.AddMinutes(10), endsAt = end.AddMinutes(10) })) Expect(patientOverlap, 409);
                using (var noConfirmation = await obs.PatchAsJsonAsync($"{schedulePath}/{appointmentId}/status", new { status = "cancelled", confirmed = false })) Expect(noConfirmation, 400);
                using (var called = await obs.PatchAsJsonAsync($"{schedulePath}/{appointmentId}/status", new { status = "called", confirmed = false })) Expect(called, 200);
                using (var complete = await obs.PatchAsJsonAsync($"{schedulePath}/{appointmentId}/status", new { status = "completed", confirmed = false })) Expect(complete, 200);
                using (var invalidTransition = await obs.PatchAsJsonAsync($"{schedulePath}/{appointmentId}/status", new { status = "cancelled", confirmed = true })) Expect(invalidTransition, 409);
                var concurrentDraft = new { cpf = "52998224725", specialty = "Clínica Geral", professional = "Profissional concorrente", startsAt = start.AddHours(1), endsAt = end.AddHours(1) };
                var competing = await Task.WhenAll(obs.PostAsJsonAsync(schedulePath, concurrentDraft), obs.PostAsJsonAsync(schedulePath, concurrentDraft));
                try
                {
                    Require(competing.Count(response => response.StatusCode == HttpStatusCode.Created) == 1 && competing.Count(response => response.StatusCode == HttpStatusCode.Conflict) == 1, "Agendamento concorrente criou consultas sobrepostas.");
                    using var winnerBody = await Json(competing.Single(response => response.StatusCode == HttpStatusCode.Created));
                    var winner = winnerBody.RootElement.GetProperty("id").GetGuid();
                    using var cancelled = await obs.PatchAsJsonAsync($"{schedulePath}/{winner}/status", new { status = "cancelled", confirmed = true }); Expect(cancelled, 200);
                    using var replacement = await obs.PostAsJsonAsync(schedulePath, concurrentDraft); Expect(replacement, 201);
                }
                finally { foreach (var response in competing) response.Dispose(); }
                await using (var unitDb = await app.Services.GetRequiredService<UnitDatabaseRouter>().OpenAsync(unitId))
                await using (var auditEvents = new Npgsql.NpgsqlCommand("SELECT count(*) FROM nora_unit.appointment_events WHERE appointment_id=$1 AND actor_id=$2", unitDb))
                { auditEvents.Parameters.AddWithValue(appointmentId); auditEvents.Parameters.AddWithValue(obsId); Require((long)(await auditEvents.ExecuteScalarAsync())! == 3, "Agenda não registrou todas as mudanças e seus autores."); }
                var managerPath = $"/api/municipal/units/{unitId}/registry";
                using var anonymousManager = await anonymous.GetAsync(managerPath); Expect(anonymousManager, 401);
                using var obsManager = await obs.GetAsync(managerPath); Expect(obsManager, 403);
                using var managerView = await municipal.GetAsync(managerPath); Expect(managerView, 200);
                using var managerBody = await Json(managerView);
                Require(managerBody.RootElement.EnumerateArray().Any(p => p.GetProperty("cpf").GetString() == "52998224725"), "Gestão não recebeu o cadastro da UBS.");
                using var managerIdentity = await municipal.GetAsync("/api/auth/me"); Expect(managerIdentity, 200);
                using var managerIdentityBody = await Json(managerIdentity); var managerId = managerIdentityBody.RootElement.GetProperty("id").GetGuid();
                await using (var auditConnection = await database.OpenAsync())
                await using (var auditRead = database.Command(auditConnection, "SELECT count(*) FROM nora.unit_access_events WHERE actor_id=$1 AND unit_id=$2 AND action='registry_read'", managerId, unitId))
                    Require((long)(await auditRead.ExecuteScalarAsync())! == 1, "Consulta nominal da gestão não registrou autoria.");

                await using (var unitDb = await app.Services.GetRequiredService<UnitDatabaseRouter>().OpenAsync(unitId))
                await using (var audit = new Npgsql.NpgsqlCommand("SELECT actor_id FROM nora_unit.patient_registry_events WHERE action='created'", unitDb))
                    Require(await audit.ExecuteScalarAsync() is Guid actor && actor == obsId, "Cadastro da UBS não registrou o autor.");
                var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                // O executor revalida conta e sessão na transação: a operação segurada usa a sessão real do cliente OBS.
                var heldSession = (await app.Services.GetRequiredService<AuthService>().AuthenticateAsync(obs.DefaultRequestHeaders.Authorization!.Parameter!, default))!;
                Require(heldSession.User.Id == obsId, "Sessão do OBS não foi resolvida.");
                var held = app.Services.GetRequiredService<UnitAccessService>().ExecuteAsync(heldSession, unitId, async () =>
                {
                    started.TrySetResult(); await release.Task; return true;
                }, CancellationToken.None);
                await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
                var revocation = municipal.DeleteAsync($"/api/municipal/staff/{obsId}/units/{unitId}");
                try
                {
                    await Task.Delay(150);
                    Require(!revocation.IsCompleted, "Revogação não aguardou operação em andamento.");
                }
                finally { release.TrySetResult(); }
                await held;
                using var revoke = await revocation; Expect(revoke, 204);
                using var after = await obs.GetAsync(path); Expect(after, 403);
                using var scheduleAfterRevoke = await obs.PostAsJsonAsync(schedulePath, appointment); Expect(scheduleAfterRevoke, 403);
                using var waitingAfterRevoke = await obs.GetAsync(waitingPath); Expect(waitingAfterRevoke, 403);
            });
            else Console.WriteLine("SKIP cadastro HTTP da UBS: configure NORA_TEST_UNIT_C_CONNECTION descartável.");
            await Check("Cadastro pelo OBS persiste e paciente não cadastra terceiros", async () =>
            {
                firstId = await CreatePatient(obs, 1, password, "+55 (48) 99999-0028");
                secondId = await CreatePatient(obs, 2, password);
                await Login(patient, "patient", Cpf(1), password);
                await Login(secondPatient, "patient", "patient2@example.test", password);
                await using (var unitConnection = await database.OpenAsync())
                await using (var link = database.Command(unitConnection, "INSERT INTO nora.patient_units(patient_id,unit_id) SELECT $1,id FROM nora.health_units WHERE name='UBS atualizada'", firstId))
                    await link.ExecuteNonQueryAsync();
                if (!string.IsNullOrWhiteSpace(unitDatabaseConnection))
                {
                    await using var unitConnection = await database.OpenAsync();
                    await using var unitQuery = database.Command(unitConnection, "SELECT id FROM nora.health_units WHERE name='UBS atualizada'");
                    var unitId = (Guid)(await unitQuery.ExecuteScalarAsync())!; var registry = app.Services.GetRequiredService<UnitPatientRegistryDao>(); var actor = Guid.NewGuid();
                    await registry.CreateAsync(unitId, actor, new UnitPatientRecord(Cpf(1), "Paciente Um", "patient1@example.test"));
                    await registry.CreateAsync(unitId, actor, new UnitPatientRecord(Cpf(2), "Paciente Dois", "patient2@example.test"));
                }
                using var publicUnits = await anonymous.GetAsync("/api/patient/units"); Expect(publicUnits, 401);
                using var obsUnits = await obs.GetAsync("/api/patient/units"); Expect(obsUnits, 403);
                using var patientUnits = await patient.GetAsync("/api/patient/units"); Expect(patientUnits, 200); // sem chave explícita o banco é derivado da central e criado ao cadastrar a UBS
                using var patientUnitsBody = await Json(patientUnits);
                using var otherPatientUnits = await secondPatient.GetAsync("/api/patient/units"); Expect(otherPatientUnits, 200);
                using var otherUnitsBody = await Json(otherPatientUnits);
                Require(otherUnitsBody.RootElement.GetProperty("units").GetArrayLength() == 0, "Paciente recebeu UBS sem vínculo.");
                // UBS marcada como pronta cujo banco não está acessível (sem chave explícita e com a criação automática desligada) responde 503: não é omitida em silêncio.
                // (UBS ainda pendente, sem banco, é omitida da lista; isso é conferido em ReviewChecks.)
                await using (var unitLookup = await database.OpenAsync())
                await using (var unitIdQuery = database.Command(unitLookup, "SELECT id FROM nora.health_units WHERE name='UBS atualizada'"))
                {
                    var readyUnit = (Guid)(await unitIdQuery.ExecuteScalarAsync())!; var explicitKey = $"Nora:UnitDatabases:{readyUnit:N}"; var savedKey = app.Configuration[explicitKey];
                    app.Configuration[explicitKey] = ""; app.Configuration["Nora:UnitDatabases:AutoProvision"] = "false";
                    try { using var unavailable = await patient.GetAsync("/api/patient/units"); Expect(unavailable, 503); }
                    finally { app.Configuration[explicitKey] = savedKey; app.Configuration["Nora:UnitDatabases:AutoProvision"] = "true"; }
                }
                if (!string.IsNullOrWhiteSpace(unitDatabaseConnection)) Require(patientUnitsBody.RootElement.GetProperty("units").EnumerateArray().Any(u => u.GetProperty("name").GetString() == "UBS atualizada"), "Paciente não recebeu unidade cadastrada pela Secretaria.");
                if (!string.IsNullOrWhiteSpace(unitDatabaseConnection))
                {
                    var unitId = patientUnitsBody.RootElement.GetProperty("units").EnumerateArray().Single(u => u.GetProperty("name").GetString() == "UBS atualizada").GetProperty("id").GetGuid();
                    var schedule = app.Services.GetRequiredService<AppointmentDao>();
                    var actor = Guid.NewGuid();
                    var start = clock.GetUtcNow().AddDays(3);
                    var ownAppointment = await schedule.CreateAsync(unitId, actor, new CreateAppointmentRequest(Cpf(1), "Clínica", "Profissional 1", start, start.AddMinutes(30)), default);
                    var otherAppointment = await schedule.CreateAsync(unitId, actor, new CreateAppointmentRequest(Cpf(2), "Clínica", "Profissional 2", start, start.AddMinutes(30)), default);
                    var from = Uri.EscapeDataString(clock.GetUtcNow().ToString("O")); var to = Uri.EscapeDataString(clock.GetUtcNow().AddDays(4).ToString("O"));
                    var appointmentsPath = $"/api/patient/units/{unitId}/appointments?from={from}&to={to}";
                    using var own = await patient.GetAsync(appointmentsPath); Expect(own, 200);
                    using var ownBody = await Json(own); var ownList = ownBody.RootElement.GetProperty("appointments");
                    Require(ownList.GetArrayLength() == 1 && ownList[0].GetProperty("cpf").GetString() == Cpf(1), "Paciente viu consulta de outra pessoa ou não recebeu a própria consulta.");
                    using var unlinked = await secondPatient.GetAsync(appointmentsPath); Expect(unlinked, 403);
                    using var anonAppointments = await anonymous.GetAsync(appointmentsPath); Expect(anonAppointments, 401);
                    var cancelPath = $"/api/patient/units/{unitId}/appointments";
                    using (var noConfirm = await patient.PostAsJsonAsync($"{cancelPath}/{ownAppointment.Id}/cancel", new { confirmed = false })) Expect(noConfirm, 400);
                    using (var otherCancel = await patient.PostAsJsonAsync($"{cancelPath}/{otherAppointment.Id}/cancel", new { confirmed = true })) Expect(otherCancel, 404);
                    using (var deniedCancel = await secondPatient.PostAsJsonAsync($"{cancelPath}/{ownAppointment.Id}/cancel", new { confirmed = true })) Expect(deniedCancel, 403);
                    using (var cancelled = await patient.PostAsJsonAsync($"{cancelPath}/{ownAppointment.Id}/cancel", new { confirmed = true })) Expect(cancelled, 200);
                    await using var unitDb = await app.Services.GetRequiredService<UnitDatabaseRouter>().OpenAsync(unitId);
                    await using var audit = new Npgsql.NpgsqlCommand("SELECT actor_id FROM nora_unit.appointment_events WHERE appointment_id=$1 AND status='cancelled'", unitDb);
                    audit.Parameters.AddWithValue(ownAppointment.Id);
                    Require(await audit.ExecuteScalarAsync() is Guid patientActor && patientActor == firstId, "Cancelamento do paciente não registrou autoria.");
                    await using (var linkSecond = database.Command(await database.OpenAsync(), "INSERT INTO nora.patient_units(patient_id,unit_id) VALUES($1,$2)", secondId, unitId))
                    { await using var ownedConnection = linkSecond.Connection!; await linkSecond.ExecuteNonQueryAsync(); }
                    var waiting = new UnitWaitingListDao(app.Services.GetRequiredService<UnitDatabaseRouter>(), clock);
                    await waiting.AddAsync(unitId, actor, new AddUnitWaitingEntryRequest(Cpf(1), "Dermatologia"), default);
                    var earlierEligible = await waiting.AddAsync(unitId, actor, new AddUnitWaitingEntryRequest(Cpf(2), "Cardiologia"), default);
                    var laterEligible = await waiting.AddAsync(unitId, actor, new AddUnitWaitingEntryRequest(Cpf(1), "Cardiologia"), default);
                    var slotStart = clock.GetUtcNow().AddDays(5);
                    var cancelledSource = await schedule.CreateAsync(unitId, actor, new CreateAppointmentRequest("52998224725", "Cardiologia", "Profissional da vaga", slotStart, slotStart.AddMinutes(30)), default);
                    await schedule.ChangeStatusAsync(unitId, actor, cancelledSource.Id, new AppointmentStatusRequest("cancelled", true), default);
                    using var obsIdentity = await obs.GetAsync("/api/auth/me"); using var obsIdentityBody = await Json(obsIdentity); var staffId = obsIdentityBody.RootElement.GetProperty("id").GetGuid();
                    using (var grant = await municipal.PutAsync($"/api/municipal/staff/{staffId}/units/{unitId}", null)) Expect(grant, 204);
                    using var slotResponse = await obs.PostAsJsonAsync($"/api/obs/units/{unitId}/lightning-slots", new { appointmentId = cancelledSource.Id, confirmed = true }); Expect(slotResponse, 201);
                    using var slotBody = await Json(slotResponse); var slotId = slotBody.RootElement.GetProperty("id").GetGuid();
                    var dispatchClock = new CheckClock(); var dispatcher = new UnitLightningDispatcher(database, app.Services.GetRequiredService<UnitDatabaseRouter>(), dispatchClock);
                    await dispatcher.AdvanceAsync(unitId, default);
                    await using (var offerQuery = new Npgsql.NpgsqlCommand("SELECT entry_id,expires_at FROM nora_unit.lightning_offers WHERE slot_id=$1 AND status='pending'", unitDb))
                    {
                        offerQuery.Parameters.AddWithValue(slotId); await using var offerReader = await offerQuery.ExecuteReaderAsync();
                        Require(await offerReader.ReadAsync() && offerReader.GetInt64(0) == earlierEligible.Id && offerReader.GetFieldValue<DateTimeOffset>(1) == dispatchClock.GetUtcNow().AddMinutes(10), "Vaga ignorou especialidade, vínculo, ordem ou prazo.");
                    }
                    dispatchClock.Advance(TimeSpan.FromMinutes(10)); await dispatcher.AdvanceAsync(unitId, default);
                    Guid nextOffer;
                    await using (var nextQuery = new Npgsql.NpgsqlCommand("SELECT id,entry_id FROM nora_unit.lightning_offers WHERE slot_id=$1 AND status='pending'", unitDb))
                    { nextQuery.Parameters.AddWithValue(slotId); await using var nextReader = await nextQuery.ExecuteReaderAsync(); Require(await nextReader.ReadAsync() && nextReader.GetInt64(1) == laterEligible.Id, "Expiração não avançou ao próximo paciente elegível."); nextOffer = nextReader.GetGuid(0); }
                    var offersPath = $"/api/patient/units/{unitId}/lightning-offers";
                    using (var ownOffers = await patient.GetAsync(offersPath))
                    { Expect(ownOffers, 200); using var offersBody = await Json(ownOffers); Require(offersBody.RootElement.GetProperty("offers").EnumerateArray().Any(o => o.GetProperty("id").GetGuid() == nextOffer), "Paciente não recebeu sua oferta."); }
                    using (var wrongPatient = await secondPatient.PostAsJsonAsync($"{offersPath}/{nextOffer}/respond", new { accept = true })) Expect(wrongPatient, 404);
                    var acceptances = await Task.WhenAll(patient.PostAsJsonAsync($"{offersPath}/{nextOffer}/respond", new { accept = true }), patient.PostAsJsonAsync($"{offersPath}/{nextOffer}/respond", new { accept = true }));
                    Guid acceptedId;
                    try
                    {
                        Require(acceptances.Count(response => response.StatusCode == HttpStatusCode.OK) == 1 && acceptances.Count(response => response.StatusCode == HttpStatusCode.Conflict) == 1, "Aceites concorrentes não produziram uma única consulta.");
                        using var acceptedBody = await Json(acceptances.Single(response => response.StatusCode == HttpStatusCode.OK));
                        acceptedId = acceptedBody.RootElement.GetProperty("appointmentId").GetGuid();
                    }
                    finally { foreach (var acceptanceResponse in acceptances) acceptanceResponse.Dispose(); }
                    var patientSchedule = await schedule.ListAsync(unitId, slotStart.AddHours(-1), slotStart.AddHours(1), Cpf(1), default);
                    Require(patientSchedule.Count == 1 && patientSchedule[0].Id == acceptedId && patientSchedule[0].Status == "scheduled", "Aceite não criou a consulta do paciente correto.");
                    var secondDerm = await waiting.AddAsync(unitId, actor, new AddUnitWaitingEntryRequest(Cpf(2), "Dermatologia"), default);
                    var dermStart = clock.GetUtcNow().AddDays(7);
                    var dermSource = await schedule.CreateAsync(unitId, actor, new CreateAppointmentRequest("52998224725", "Dermatologia", "Profissional da recusa", dermStart, dermStart.AddMinutes(30)), default);
                    await schedule.ChangeStatusAsync(unitId, actor, dermSource.Id, new AppointmentStatusRequest("cancelled", true), default);
                    using var dermSlot = await obs.PostAsJsonAsync($"/api/obs/units/{unitId}/lightning-slots", new { appointmentId = dermSource.Id, confirmed = true }); Expect(dermSlot, 201);
                    Guid firstDermOffer;
                    using (var firstDerm = await patient.GetAsync(offersPath))
                    { Expect(firstDerm, 200); using var firstDermBody = await Json(firstDerm); firstDermOffer = firstDermBody.RootElement.GetProperty("offers").EnumerateArray().Single(o => o.GetProperty("specialty").GetString() == "Dermatologia" && o.GetProperty("status").GetString() == "pending").GetProperty("id").GetGuid(); }
                    using (var decline = await patient.PostAsJsonAsync($"{offersPath}/{firstDermOffer}/respond", new { accept = false })) Expect(decline, 200);
                    Guid secondDermOffer;
                    using (var secondDermList = await secondPatient.GetAsync(offersPath))
                    { Expect(secondDermList, 200); using var secondDermBody = await Json(secondDermList); secondDermOffer = secondDermBody.RootElement.GetProperty("offers").EnumerateArray().Single(o => o.GetProperty("specialty").GetString() == "Dermatologia" && o.GetProperty("status").GetString() == "pending").GetProperty("id").GetGuid(); }
                    using (var cancelWaiting = await obs.PostAsJsonAsync($"/api/obs/units/{unitId}/waiting-list/{secondDerm.Id}/cancel", new { confirmed = true })) Expect(cancelWaiting, 204);
                    using (var staleOffer = await secondPatient.PostAsJsonAsync($"{offersPath}/{secondDermOffer}/respond", new { accept = true })) Expect(staleOffer, 409);
                    await waiting.AddAsync(unitId, actor, new AddUnitWaitingEntryRequest(Cpf(2), "Cardiologia"), default);
                    var workerStart = clock.GetUtcNow().AddDays(6);
                    var workerSource = await schedule.CreateAsync(unitId, actor, new CreateAppointmentRequest("52998224725", "Cardiologia", "Profissional do prazo", workerStart, workerStart.AddMinutes(30)), default);
                    await schedule.ChangeStatusAsync(unitId, actor, workerSource.Id, new AppointmentStatusRequest("cancelled", true), default);
                    using var workerSlotResponse = await obs.PostAsJsonAsync($"/api/obs/units/{unitId}/lightning-slots", new { appointmentId = workerSource.Id, confirmed = true }); Expect(workerSlotResponse, 201);
                    using var workerSlotBody = await Json(workerSlotResponse); var workerSlotId = workerSlotBody.RootElement.GetProperty("id").GetGuid();
                    await using (var checkOffer = new Npgsql.NpgsqlCommand("SELECT count(*) FROM nora_unit.lightning_offers WHERE slot_id=$1 AND status='pending'", unitDb))
                    { checkOffer.Parameters.AddWithValue(workerSlotId); Require((long)(await checkOffer.ExecuteScalarAsync())! == 1, "Vaga de teste não foi ofertada."); }
                    clock.Advance(TimeSpan.FromMinutes(10));
                    var expiredByWorker = false;
                    for (var attempt = 0; attempt < 24 && !expiredByWorker; attempt++)
                    {
                        await Task.Delay(1000);
                        await using var checkExpiration = new Npgsql.NpgsqlCommand("SELECT EXISTS(SELECT 1 FROM nora_unit.lightning_offers WHERE slot_id=$1 AND status='expired')", unitDb);
                        checkExpiration.Parameters.AddWithValue(workerSlotId); expiredByWorker = await checkExpiration.ExecuteScalarAsync() is true;
                    }
                    Require(expiredByWorker, "Verificação periódica não expirou a oferta.");
                    app.Configuration["Nora:LegacyGlobalWorkflow:Enabled"] = "false";
                    try
                    {
                        using (var oldPatients = await obs.GetAsync("/api/patients")) Expect(oldPatients, 403);
                        using (var oldOverview = await patient.GetAsync("/api/patient/overview")) Expect(oldOverview, 403);
                        using var networkSummary = await municipal.GetAsync($"/api/municipal/summary?unitId={unitId}"); Expect(networkSummary, 200);
                        using var networkBody = await Json(networkSummary); var network = networkBody.RootElement;
                        Require(network.GetProperty("patients").GetInt64() == 4 && network.GetProperty("waitingPatients").GetInt64() == 1 && network.GetProperty("acceptedOffers").GetInt64() == 1, "Resumo não refletiu os dados da UBS sem duplicação de pacientes.");
                    }
                    finally { app.Configuration["Nora:LegacyGlobalWorkflow:Enabled"] = "true"; }
                    using (var revoke = await municipal.DeleteAsync($"/api/municipal/staff/{staffId}/units/{unitId}")) Expect(revoke, 204);
                }
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
                Require(json.RootElement.GetProperty("registeredSlots").GetInt64() == 1 && json.RootElement.GetProperty("createdOffers").GetInt64() == 2, "Vagas ou ofertas criadas incorretas.");
                Require(json.RootElement.GetProperty("declinedOffers").GetInt64() == 1 && json.RootElement.GetProperty("expiredOffers").GetInt64() == 0 && json.RootElement.GetProperty("pendingOffers").GetInt64() == 0, "Estados das ofertas incorretos.");
                Require(!body.Contains("example.test") && !body.Contains("Paciente") && !body.Contains(Cpf(1)), "Resumo expõe identidade.");
            });
            await Check("Coortes de vagas filtram pela criação e preservam estados atuais", async () =>
            {
                await using var connection = await database.OpenAsync();
                await using (var age = database.Command(connection, "UPDATE nora.slots SET created_at=$1 WHERE status='accepted'", clock.GetUtcNow().AddDays(-8)))
                    await age.ExecuteNonQueryAsync();
                try
                {
                    using var response = await municipal.GetAsync("/api/municipal/summary");
                    Expect(response, 200);
                    using var json = await Json(response);
                    var cohorts = json.RootElement.GetProperty("lightningCohorts").EnumerateArray().ToArray();
                    var week = cohorts.Single(c => c.GetProperty("days").GetInt32() == 7);
                    var month = cohorts.Single(c => c.GetProperty("days").GetInt32() == 30);
                    Require(week.GetProperty("registeredSlots").GetInt64() == 0 && week.GetProperty("createdOffers").GetInt64() == 0, "Coorte semanal incluiu vaga antiga.");
                    Require(month.GetProperty("registeredSlots").GetInt64() == 1 && month.GetProperty("acceptedOffers").GetInt64() == 1 && month.GetProperty("declinedOffers").GetInt64() == 1, "Coorte mensal perdeu vaga ou estados.");
                }
                finally
                {
                    await using var restore = database.Command(connection, "UPDATE nora.slots SET created_at=$1 WHERE status='accepted'", clock.GetUtcNow());
                    await restore.ExecuteNonQueryAsync();
                }
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
                Require((await app.Services.GetRequiredService<SessionDao>().CreateAsync(new(firstId, "patient", "patient", new string('A',64), clock.GetUtcNow().AddHours(8), clock.GetUtcNow().AddDays(30), false, null, false, clock.GetUtcNow()), default)).Outcome == SessionOutcome.AccountInactive, "Login concorrente persistiu sessão de conta desativada.");
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
            var registrationConnections = new[] { Environment.GetEnvironmentVariable("NORA_TEST_UNIT_D_CONNECTION"), Environment.GetEnvironmentVariable("NORA_TEST_UNIT_E_CONNECTION"), Environment.GetEnvironmentVariable("NORA_TEST_UNIT_F_CONNECTION") };
            if (registrationConnections.All(value => !string.IsNullOrWhiteSpace(value)))
            await Check("Cadastro do paciente confirma e-mail e vincula somente UBS com CPF existente", async () =>
            {
                app.Configuration["Nora:Registration:HashKey"] = "synthetic-registration-key-at-least-32-bytes";
                using var identity = await obs.GetAsync("/api/auth/me"); Expect(identity, 200);
                using var identityBody = await Json(identity); var actorId = identityBody.RootElement.GetProperty("id").GetGuid();
                var units = new List<Guid>();
                for (var i = 0; i < 3; i++)
                {
                    using var unit = await municipal.PostAsJsonAsync("/api/municipal/units", new { name = $"Cadastro UBS {i}", region = "Centro", address = "Rua sintética", latitude = -23.5, longitude = -46.6 }); Expect(unit, 201);
                    using var body = await Json(unit); var id = body.RootElement.GetProperty("id").GetGuid(); units.Add(id);
                    app.Configuration[$"Nora:UnitDatabases:{id:N}"] = registrationConnections[i];
                    await app.Services.GetRequiredService<UnitMigrationDao>().MigrateAsync(id);
                    if (i < 2) await app.Services.GetRequiredService<UnitPatientRegistryDao>().CreateAsync(id, actorId, new UnitPatientRecord(Cpf(8), "Paciente de cadastro", i == 0 ? "one@example.test" : "second@example.test"));
                }
                if (!string.IsNullOrWhiteSpace(unitDatabaseConnection))
                {
                    var summaryDao = app.Services.GetRequiredService<UnitNetworkSummaryDao>();
                    var network = await summaryDao.ReadAsync(null, default); var oneUnit = await summaryDao.ReadAsync(units[0], default);
                    Require(network.Patients == 5 && oneUnit.Patients == 1, "Resumo da rede duplicou paciente presente em duas UBS ou não filtrou a unidade.");
                }
                var initial = new { fullName = "Paciente Cadastro", cpf = Cpf(8), password = "Synthetic-Patient-Password!29", unitIds = units };
                using var begin = await anonymous.PostAsJsonAsync("/api/registration/start", initial); Expect(begin, 200);
                using var beginBody = await Json(begin); var challenge = beginBody.RootElement.GetProperty("challengeId").GetGuid();
                var options = beginBody.RootElement.GetProperty("emails").EnumerateArray().ToArray();
                Require(options.Length == 2 && options.All(o => !o.GetProperty("maskedEmail").GetString()!.Contains("one@example.test")), "Opções de e-mail não foram mascaradas.");
                var selected = options.Single(o => o.GetProperty("maskedEmail").GetString()!.StartsWith("o***", StringComparison.Ordinal));
                using var send = await anonymous.PostAsJsonAsync("/api/registration/send-code", new { challengeId = challenge, emailOptionId = selected.GetProperty("id").GetString() }); Expect(send, 204);
                Require(registrationMail.Recipient == "one@example.test" && registrationMail.Code?.Length == 6, "Código não foi enviado ao e-mail cadastrado escolhido.");
                using var notYet = await anonymous.PostAsJsonAsync("/api/auth/login", new { scope = "patient", identifier = Cpf(8), password = "Synthetic-Patient-Password!29" }); Expect(notYet, 401);
                var wrongCode = registrationMail.Code == "000000" ? "111111" : "000000";
                using var wrong = await anonymous.PostAsJsonAsync("/api/registration/confirm", new { challengeId = challenge, code = wrongCode }); Expect(wrong, 200);
                using var wrongBody = await Json(wrong); Require(!wrongBody.RootElement.GetProperty("codeValid").GetBoolean(), "Código incorreto foi aceito.");
                var confirmations = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => anonymous.PostAsJsonAsync("/api/registration/confirm", new { challengeId = challenge, code = registrationMail.Code })));
                try
                {
                    var results = new List<JsonElement>();
                    foreach (var confirmation in confirmations) { Expect(confirmation, 200); using var body = await Json(confirmation); results.Add(body.RootElement.Clone()); }
                    var success = results.Single(r => r.GetProperty("codeValid").GetBoolean());
                    Require(results.Count(r => r.GetProperty("codeValid").GetBoolean()) == 1, "Código foi usado mais de uma vez.");
                    Require(success.GetProperty("linkedUnits").GetArrayLength() == 2 && success.GetProperty("unmatchedUnits").EnumerateArray().Single().GetGuid() == units[2], "Cadastro parcial não preservou as unidades corretas.");
                }
                finally { foreach (var confirmation in confirmations) confirmation.Dispose(); }
                using var registered = Client(app); await Login(registered, "patient", Cpf(8), "Synthetic-Patient-Password!29");
                using var listed = await registered.GetAsync("/api/patient/units"); Expect(listed, 200);
                using var list = await Json(listed); var linked = list.RootElement.GetProperty("units").EnumerateArray().Select(u => u.GetProperty("id").GetGuid()).ToHashSet();
                Require(linked.SetEquals(units.Take(2)), "Sessão não recebeu apenas as UBS vinculadas.");
                using (var grant = await municipal.PutAsync($"/api/municipal/staff/{actorId}/units/{units[0]}", null)) Expect(grant, 204);
                using (var heartbeat = await registered.PostAsync("/api/patient/presence", null)) Expect(heartbeat, 204);
                using (var staffRegistry = await obs.GetAsync($"/api/obs/units/{units[0]}/registry"))
                { Expect(staffRegistry, 200); using var staffBody = await Json(staffRegistry); Require(staffBody.RootElement.EnumerateArray().Single(p => p.GetProperty("cpf").GetString() == Cpf(8)).GetProperty("isOnline").GetBoolean(), "Presença recente não apareceu no cadastro autorizado da UBS."); }
                var registry = app.Services.GetRequiredService<UnitPatientRegistryDao>();
                await registry.CreateAsync(units[0], actorId, new UnitPatientRecord(Cpf(11), "Paciente de origem", "source11@example.test"));
                var schedule = app.Services.GetRequiredService<AppointmentDao>(); var starts = clock.GetUtcNow().AddDays(10);
                var preserved = await schedule.CreateAsync(units[0], actorId, new CreateAppointmentRequest(Cpf(8), "Clínica", "Profissional preservado", starts, starts.AddMinutes(30)), default);
                var source = await schedule.CreateAsync(units[0], actorId, new CreateAppointmentRequest(Cpf(11), "Cardiologia", "Profissional da vaga", starts.AddHours(1), starts.AddHours(1.5)), default);
                await schedule.ChangeStatusAsync(units[0], actorId, source.Id, new AppointmentStatusRequest("cancelled", true), default);
                using (var queued = await obs.PostAsJsonAsync($"/api/obs/units/{units[0]}/waiting-list", new { cpf = Cpf(8), specialty = "Cardiologia" })) Expect(queued, 201);
                using (var opened = await obs.PostAsJsonAsync($"/api/obs/units/{units[0]}/lightning-slots", new { appointmentId = source.Id, confirmed = true })) Expect(opened, 201);
                var statusPath = $"/api/obs/units/{units[0]}/registry/active";
                using (var noConfirmation = await obs.PatchAsJsonAsync(statusPath, new { cpf = Cpf(8), isActive = false, confirmed = false })) Expect(noConfirmation, 400);
                using (var wrongScope = await registered.PatchAsJsonAsync(statusPath, new { cpf = Cpf(8), isActive = false, confirmed = true })) Expect(wrongScope, 403);
                var patientSession = await app.Services.GetRequiredService<AuthService>().AuthenticateAsync(registered.DefaultRequestHeaders.Authorization!.Parameter!, default) ?? throw new InvalidOperationException("Sessão de teste ausente.");
                var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var heldAccess = app.Services.GetRequiredService<UnitAccessService>().ExecutePatientAsync(patientSession, units[0], async _ => { entered.TrySetResult(); await release.Task; return true; }, default);
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                var deactivate = obs.PatchAsJsonAsync(statusPath, new { cpf = Cpf(8), isActive = false, confirmed = true });
                try { await Task.Delay(150); Require(!deactivate.IsCompleted, "Desativação não aguardou operação do paciente na UBS."); }
                finally { release.TrySetResult(); }
                await heldAccess; using (var deactivated = await deactivate) Expect(deactivated, 204);
                using (var stillLoggedIn = await registered.GetAsync("/api/auth/me")) Expect(stillLoggedIn, 200);
                using (var staffRegistry = await obs.GetAsync($"/api/obs/units/{units[0]}/registry"))
                { Expect(staffRegistry, 200); using var staffBody = await Json(staffRegistry); var row = staffBody.RootElement.EnumerateArray().Single(p => p.GetProperty("cpf").GetString() == Cpf(8)); Require(!row.GetProperty("isActive").GetBoolean() && row.GetProperty("isOnline").GetBoolean(), "Situação local e presença no aplicativo foram confundidas."); }
                using (var remainingUnits = await registered.GetAsync("/api/patient/units"))
                { Expect(remainingUnits, 200); using var remaining = await Json(remainingUnits); Require(remaining.RootElement.GetProperty("units").EnumerateArray().Select(u => u.GetProperty("id").GetGuid()).SequenceEqual(new[] { units[1] }), "Bloqueio local afetou as outras UBS ou não ocultou a unidade bloqueada."); }
                using (var deniedLink = await registered.PostAsJsonAsync("/api/patient/units/link", new { unitIds = new[] { units[0], units[2] } }))
                { Expect(deniedLink, 200); using var body = await Json(deniedLink); Require(body.RootElement.GetProperty("linkedUnits").GetArrayLength() == 0 && body.RootElement.GetProperty("unmatchedUnits").GetArrayLength() == 2, "Vínculo ignorou situação local ou cadastro ausente."); }
                var dateFrom = Uri.EscapeDataString(clock.GetUtcNow().ToString("O")); var dateTo = Uri.EscapeDataString(clock.GetUtcNow().AddDays(20).ToString("O"));
                using (var blocked = await registered.GetAsync($"/api/patient/units/{units[0]}/appointments?from={dateFrom}&to={dateTo}")) Expect(blocked, 403);
                using (var allowed = await registered.GetAsync($"/api/patient/units/{units[1]}/appointments?from={dateFrom}&to={dateTo}")) Expect(allowed, 200);
                await using (var local = await app.Services.GetRequiredService<UnitDatabaseRouter>().OpenAsync(units[0]))
                await using (var check = new Npgsql.NpgsqlCommand("SELECT (SELECT count(*) FROM nora_unit.waiting_list WHERE cpf=$1 AND status='cancelled'),(SELECT count(*) FROM nora_unit.lightning_offers o JOIN nora_unit.waiting_list q ON q.id=o.entry_id WHERE q.cpf=$1 AND o.status='cancelled'),(SELECT count(*) FROM nora_unit.appointments a WHERE a.id=$2 AND a.status='cancelled' AND (SELECT count(*) FROM nora_unit.appointment_events e WHERE e.appointment_id=a.id AND e.status='cancelled' AND e.source='staff' AND e.actor_id=$3)=1),(SELECT count(*) FROM nora_unit.patient_registry_events WHERE cpf=$1 AND actor_id=$3 AND action='deactivated')", local))
                {
                    check.Parameters.AddWithValue(Cpf(8)); check.Parameters.AddWithValue(preserved.Id); check.Parameters.AddWithValue(actorId); await using var result = await check.ExecuteReaderAsync();
                    Require(await result.ReadAsync() && result.GetInt64(0) == 1 && result.GetInt64(1) == 1 && result.GetInt64(2) == 1 && result.GetInt64(3) == 1, "Desativação local (decisão de 2026-11-16) não cancelou a consulta futura como cancelamento da unidade (origem staff) ou não cancelou/auditou pendências.");
                }
                using (var restored = await obs.PatchAsJsonAsync(statusPath, new { cpf = Cpf(8), isActive = true, confirmed = true })) Expect(restored, 204);
                using (var restoredUnits = await registered.GetAsync("/api/patient/units"))
                { Expect(restoredUnits, 200); using var restoredBody = await Json(restoredUnits); Require(restoredBody.RootElement.GetProperty("units").GetArrayLength() == 2, "Reativação não restaurou o acesso à UBS."); }
                using (var restoredQueue = await registered.GetAsync($"/api/patient/units/{units[0]}/waiting-list"))
                { Expect(restoredQueue, 200); using var queueBody = await Json(restoredQueue); Require(queueBody.RootElement.GetProperty("entries").EnumerateArray().Single().GetProperty("status").GetString() == "cancelled", "Reativação restaurou entrada cancelada."); }
                using (var anonymousLink = await anonymous.PostAsJsonAsync("/api/patient/units/link", new { unitIds = new[] { units[2] } })) Expect(anonymousLink, 401);
                using (var invalidLink = await registered.PostAsJsonAsync("/api/patient/units/link", new { unitIds = new[] { Guid.NewGuid() } })) Expect(invalidLink, 400);
                await registry.CreateAsync(units[2], actorId, new UnitPatientRecord(Cpf(8), "Paciente de cadastro", "third@example.test"));
                for (var repeat = 0; repeat < 2; repeat++)
                using (var newLink = await registered.PostAsJsonAsync("/api/patient/units/link", new { unitIds = new[] { units[2] } }))
                { Expect(newLink, 200); using var body = await Json(newLink); Require(body.RootElement.GetProperty("linkedUnits").EnumerateArray().Single().GetGuid() == units[2], "Não vinculou o CPF cadastrado posteriormente na UBS."); }
                using (var nowLinked = await registered.GetAsync("/api/patient/units"))
                { Expect(nowLinked, 200); using var body = await Json(nowLinked); Require(body.RootElement.GetProperty("units").GetArrayLength() == 3, "Nova UBS não apareceu na sessão."); }
                var central = app.Services.GetRequiredService<DatabaseHelper>();
                await using (var connection = await central.OpenAsync())
                await using (var audit = central.Command(connection, "SELECT count(*) FROM nora.patient_unit_link_events WHERE patient_id=$1 AND unit_id=$2", patientSession.User.Id, units[2]))
                    Require((long)(await audit.ExecuteScalarAsync())! == 1, "Vínculo repetido duplicou o histórico.");
                await registry.CreateAsync(units[0], actorId, new UnitPatientRecord(Cpf(12), "Paciente em ativação", "before-status@example.test"));
                using var beforeStatus = await anonymous.PostAsJsonAsync("/api/registration/start", new { fullName = "Paciente em ativação", cpf = Cpf(12), password = "Synthetic-Patient-Password!29", unitIds = new[] { units[0] } }); Expect(beforeStatus, 200);
                using var beforeStatusBody = await Json(beforeStatus); var pendingStatusId = beforeStatusBody.RootElement.GetProperty("challengeId").GetGuid();
                using (var sendPending = await anonymous.PostAsJsonAsync("/api/registration/send-code", new { challengeId = pendingStatusId, emailOptionId = beforeStatusBody.RootElement.GetProperty("emails")[0].GetProperty("id").GetString() })) Expect(sendPending, 204);
                var pendingStatusCode = registrationMail.Code;
                using (var blockPending = await obs.PatchAsJsonAsync(statusPath, new { cpf = Cpf(12), isActive = false, confirmed = true })) Expect(blockPending, 204);
                using (var rejectedPending = await anonymous.PostAsJsonAsync("/api/registration/confirm", new { challengeId = pendingStatusId, code = pendingStatusCode })) Expect(rejectedPending, 409);
                using (var reactivatePending = await obs.PatchAsJsonAsync(statusPath, new { cpf = Cpf(12), isActive = true, confirmed = true })) Expect(reactivatePending, 204);
                await registry.CreateAsync(units[0], actorId, new UnitPatientRecord(Cpf(13), "Paciente com e-mail corrigido", "before-email@example.test"));
                using var beforeEmail = await anonymous.PostAsJsonAsync("/api/registration/start", new { fullName = "Paciente com e-mail corrigido", cpf = Cpf(13), password = "Synthetic-Patient-Password!29", unitIds = new[] { units[0] } }); Expect(beforeEmail, 200);
                using var beforeEmailBody = await Json(beforeEmail); var pendingEmailId = beforeEmailBody.RootElement.GetProperty("challengeId").GetGuid();
                using (var sendOld = await anonymous.PostAsJsonAsync("/api/registration/send-code", new { challengeId = pendingEmailId, emailOptionId = beforeEmailBody.RootElement.GetProperty("emails")[0].GetProperty("id").GetString() })) Expect(sendOld, 204);
                var oldEmailCode = registrationMail.Code;
                using (var correctEmail = await obs.PatchAsJsonAsync($"/api/obs/units/{units[0]}/registry/email", new { cpf = Cpf(13), email = "after-email@example.test" })) Expect(correctEmail, 204);
                using (var rejectOld = await anonymous.PostAsJsonAsync("/api/registration/confirm", new { challengeId = pendingEmailId, code = oldEmailCode })) Expect(rejectOld, 409);
                await app.Services.GetRequiredService<UnitMigrationDao>().MigrateAsync(units[0]);
                using (var revoke = await municipal.DeleteAsync($"/api/municipal/staff/{actorId}/units/{units[0]}")) Expect(revoke, 204);
                using (var noMatch = await anonymous.PostAsJsonAsync("/api/registration/start", new { fullName = "Paciente sem registro", cpf = Cpf(9), password = "Synthetic-Patient-Password!29", unitIds = units }))
                {
                    Expect(noMatch, 200); using var noMatchBody = await Json(noMatch);
                    Require(noMatchBody.RootElement.GetProperty("challengeId").ValueKind == JsonValueKind.Null && noMatchBody.RootElement.GetProperty("emails").GetArrayLength() == 0, "Cadastro sem UBS correspondente iniciou desafio.");
                }
                await app.Services.GetRequiredService<UnitPatientRegistryDao>().CreateAsync(units[0], actorId, new UnitPatientRecord(Cpf(9), "Paciente de expiração", "expiry@example.test"));
                using var expStart = await anonymous.PostAsJsonAsync("/api/registration/start", new { fullName = "Paciente de expiração", cpf = Cpf(9), password = "Synthetic-Patient-Password!29", unitIds = new[] { units[0] } }); Expect(expStart, 200);
                using var expBody = await Json(expStart); var expId = expBody.RootElement.GetProperty("challengeId").GetGuid(); var expOption = expBody.RootElement.GetProperty("emails")[0].GetProperty("id").GetString();
                registrationMail.Fail = true;
                using (var failedSend = await anonymous.PostAsJsonAsync("/api/registration/send-code", new { challengeId = expId, emailOptionId = expOption })) Expect(failedSend, 503);
                var failedCode = registrationMail.Code; registrationMail.Fail = false;
                using (var failedConfirm = await anonymous.PostAsJsonAsync("/api/registration/confirm", new { challengeId = expId, code = failedCode }))
                { Expect(failedConfirm, 200); using var body = await Json(failedConfirm); Require(!body.RootElement.GetProperty("codeValid").GetBoolean(), "Código de envio não confirmado foi aceito."); }
                using (var resent = await anonymous.PostAsJsonAsync("/api/registration/send-code", new { challengeId = expId, emailOptionId = expOption })) Expect(resent, 204);
                var expCode = registrationMail.Code; clock.Advance(TimeSpan.FromMinutes(11));
                using (var expiredCode = await anonymous.PostAsJsonAsync("/api/registration/confirm", new { challengeId = expId, code = expCode }))
                { Expect(expiredCode, 200); using var body = await Json(expiredCode); Require(!body.RootElement.GetProperty("codeValid").GetBoolean(), "Código expirado foi aceito."); }
                await app.Services.GetRequiredService<UnitPatientRegistryDao>().CreateAsync(units[0], actorId, new UnitPatientRecord(Cpf(10), "Paciente de tentativas", "attempts@example.test"));
                using var limitStart = await anonymous.PostAsJsonAsync("/api/registration/start", new { fullName = "Paciente de tentativas", cpf = Cpf(10), password = "Synthetic-Patient-Password!29", unitIds = new[] { units[0] } }); Expect(limitStart, 200);
                using var limitBody = await Json(limitStart); var limitId = limitBody.RootElement.GetProperty("challengeId").GetGuid(); var limitOption = limitBody.RootElement.GetProperty("emails")[0].GetProperty("id").GetString();
                using (var limitSend = await anonymous.PostAsJsonAsync("/api/registration/send-code", new { challengeId = limitId, emailOptionId = limitOption })) Expect(limitSend, 204);
                var correct = registrationMail.Code; var invalid = correct == "000000" ? "111111" : "000000";
                for (var i = 0; i < 5; i++)
                {
                    using var attempt = await anonymous.PostAsJsonAsync("/api/registration/confirm", new { challengeId = limitId, code = invalid }); Expect(attempt, 200);
                    using var attemptBody = await Json(attempt); Require(!attemptBody.RootElement.GetProperty("codeValid").GetBoolean(), "Tentativa incorreta foi aceita.");
                }
                using (var locked = await anonymous.PostAsJsonAsync("/api/registration/confirm", new { challengeId = limitId, code = correct }))
                { Expect(locked, 200); using var body = await Json(locked); Require(!body.RootElement.GetProperty("codeValid").GetBoolean(), "Código foi aceito após o limite de tentativas."); }

            });
            else Console.WriteLine("SKIP cadastro público: configure três bancos de UBS descartáveis.");
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
                Require(new[] { firstId, secondId, thirdId, fourthId }.All(id => json.RootElement.EnumerateArray().Any(p => p.GetProperty("id").GetGuid() == id)), "Reabertura perdeu cadastro persistido.");
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
            // Só este host legado (rotas globais, sem UBS) entra como atendente sem computador ativado: a exigência de computador é testada em ObsDeviceChecks e as demais suítes ativam um computador.
            Args = ["--urls", "http://127.0.0.1:0", "--ConnectionStrings:Nora", connectionString, "--Nora:Schema", schema, "--Nora:LegacyGlobalWorkflow:Enabled", "true", "--Nora:ObsDevices:RequireForAttendants", "false"],
            EnvironmentName = "Development",
            ApplicationName = typeof(NoraApiHost).Assembly.GetName().Name,
            ContentRootPath = Path.Combine(root, "Nora.Api")
        }, services => { services.Replace(ServiceDescriptor.Singleton<TimeProvider>(clock)); services.Replace(ServiceDescriptor.Singleton<IEmailSender>(registrationMail)); });

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
    private sealed class TestRegistrationMail : IEmailSender
    {
        public bool IsConfigured => true;
        public string? Recipient { get; private set; }
        public string? Code { get; private set; }
        public bool Fail { get; set; }
        public Task SendAsync(string address, string subject, string body, CancellationToken ct)
        {
            Recipient = address;
            Code = System.Text.RegularExpressions.Regex.Match(body, @"\b[0-9]{6}\b").Value;
            if (Fail) throw new IOException("Synthetic email failure.");
            return Task.CompletedTask;
        }
    }
    private sealed class CheckClock : TimeProvider
    {
        private long ticks = new DateTimeOffset(2026, 10, 7, 12, 0, 0, TimeSpan.Zero).UtcTicks;
        public override DateTimeOffset GetUtcNow() => new(Interlocked.Read(ref ticks), TimeSpan.Zero);
        public void Advance(TimeSpan value) => Interlocked.Add(ref ticks, value.Ticks);
    }
}
