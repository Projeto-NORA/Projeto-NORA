using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Nora.Api.Database;
using Nora.Api.Services;

/// <summary>
/// Testes que faltavam na Família (P-043, 2026-11-16): limite por conta (429) das escritas delegadas, corrida "encerrar o vínculo espera a operação delegada em andamento" reproduzida
/// sempre (o teste segura a linha da consulta no banco da UBS e observa as esperas em <c>pg_stat_activity</c>) e recusa de oferta de Vaga Relâmpago pelo responsável.
/// Mesmo fixture e mesmo relógio de <see cref="FamilyChecks"/>, num schema próprio. As consultas são de texto livre (a UBS não tem profissional cadastrado).
/// </summary>
internal static partial class FamilyChecks
{
    public static async Task<int> RunGapsAsync(string root)
    {
        var connectionString = Environment.GetEnvironmentVariable("NORA_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Console.WriteLine("SKIP lacunas da família PostgreSQL: configure NORA_TEST_CONNECTION somente para uma base descartável.");
            return 0;
        }

        checks = 0; failures = 0;
        await using var f = await Fixture.StartAsync(connectionString);
        var u1 = f.Unit1;
        var accounts = f.App.Services.GetRequiredService<AccountService>();
        string Base(Guid link, Guid unit) => $"/api/patient/dependents/{link}/units/{unit}";
        async Task Register(int cpf, string name) { using var register = await f.Admin.PostAsJsonAsync($"/api/obs/units/{u1}/registry", new { cpf = Cpf(cpf), fullName = name, email = $"sem-conta{cpf}@example.test" }); Expect(register, 201); }
        async Task<HttpClient> Guardian(int number, string name, bool ownLink = false)
        {
            await accounts.ProvisionAsync("patient", "patient", new(name, Cpf(number), $"g{number}@example.test", Password));
            if (ownLink) await Register(number, name);
            var client = await LoginAsync(f, Cpf(number));
            if (ownLink) { using var link = await client.PostAsJsonAsync("/api/patient/units/link", new { unitIds = new[] { u1 } }); Expect(link, 200); }
            return client;
        }
        async Task<Guid> Linked(HttpClient guardian, int dependent)
        {
            Guid id;
            using (var response = await guardian.PostAsJsonAsync("/api/patient/dependents", new { cpf = Cpf(dependent), unitId = u1 })) { Expect(response, 201); id = (await Read(response)).GetProperty("id").GetGuid(); }
            using (var approve = await f.Attendant.PostAsJsonAsync($"/api/obs/units/{u1}/family-links/{id}/approve", new { confirmed = true })) Expect(approve, 200);
            return id;
        }
        async Task<Guid> StaffAppointment(int cpf, string specialty, string startsAt, string endsAt)
        {
            using var created = await f.Attendant.PostAsJsonAsync($"/api/obs/units/{u1}/appointments", new { cpf = Cpf(cpf), specialty, professional = "Dr. Bruno Lima", startsAt, endsAt });
            Expect(created, 201); return (await Read(created)).GetProperty("id").GetGuid();
        }

        await Check("Preparação: dependentes sem conta na UBS 1", async () =>
        {
            foreach (var (cpf, name) in new[] { (91, "Dependente Noventa e Um"), (92, "Dependente Noventa e Dois"), (93, "Dependente Noventa e Três") }) await Register(cpf, name);
        });

        // ======== 1. Limite por conta (429) das escritas delegadas ========
        await Check("Escritas delegadas: 60 escritas por 10 minutos por conta, somando cancelar consulta, sair da fila, responder oferta e cancelar pedido; a 61ª de qualquer delas → 429; outra conta não é afetada", async () =>
        {
            using var guardian = await Guardian(61, "Responsável Sessenta e Um");
            var link = await Linked(guardian, 91);
            Func<Task<HttpResponseMessage>>[] writes =
            [
                () => guardian.PostAsJsonAsync($"{Base(link, u1)}/appointments/{Guid.NewGuid()}/cancel", new { confirmed = true }),
                () => guardian.PostAsJsonAsync($"{Base(link, u1)}/waiting-list/999999/cancel", new { confirmed = true }),
                () => guardian.PostAsJsonAsync($"{Base(link, u1)}/lightning-offers/{Guid.NewGuid()}/respond", new { accept = false }),
                () => guardian.PostAsJsonAsync($"/api/patient/dependents/{Guid.NewGuid()}/cancel", new { confirmed = true })
            ];
            for (var i = 0; i < 60; i++) { using var response = await writes[i % writes.Length](); Expect(response, 404); }
            foreach (var write in writes) { using var limited = await write(); await ExpectProblem(limited, 429, "too-many-requests"); }
            using (var other = await f.G1.PostAsJsonAsync($"/api/patient/dependents/{Guid.NewGuid()}/cancel", new { confirmed = true })) Expect(other, 404);
        });

        await Check("Agendamento delegado: o contador de 20 por 10 minutos é o mesmo das marcações do próprio responsável (10 dele e 10 pelo dependente esgotam os dois); confirmação delegada: 40 e a 41ª → 429", async () =>
        {
            using var guardian = await Guardian(62, "Responsável Sessenta e Dois", ownLink: true);
            var link = await Linked(guardian, 92);
            for (var i = 0; i < 10; i++)
            {
                using (var own = await guardian.PostAsJsonAsync($"/api/patient/units/{u1}/appointments", new { })) Expect(own, 400);
                using (var delegated = await guardian.PostAsJsonAsync($"{Base(link, u1)}/appointments", new { })) Expect(delegated, 400);
            }
            using (var limitedDelegated = await guardian.PostAsJsonAsync($"{Base(link, u1)}/appointments", new { })) await ExpectProblem(limitedDelegated, 429, "too-many-requests");
            using (var limitedOwn = await guardian.PostAsJsonAsync($"/api/patient/units/{u1}/appointments", new { })) await ExpectProblem(limitedOwn, 429, "too-many-requests");
            for (var i = 0; i < 40; i++) { using var confirm = await guardian.PostAsync($"{Base(link, u1)}/appointments/{Guid.NewGuid()}/confirm", null); Expect(confirm, 404); }
            using (var limitedConfirm = await guardian.PostAsync($"{Base(link, u1)}/appointments/{Guid.NewGuid()}/confirm", null)) await ExpectProblem(limitedConfirm, 429, "too-many-requests");
            using (var other = await f.G1.PostAsJsonAsync($"/api/patient/units/{u1}/appointments", new { })) Expect(other, 400); // o limite é por conta
        });

        // ======== 2. Encerrar o vínculo espera a operação delegada em andamento ========
        await Check("Corrida: o encerramento do vínculo pela equipe espera a operação delegada que já passou pela autorização (FOR SHARE do vínculo) e só termina depois dela; a operação vale (200) e fica na auditoria do responsável", async () =>
        {
            using var guardian = await Guardian(63, "Responsável Sessenta e Três");
            var link = await Linked(guardian, 92);
            var appointment = await StaffAppointment(92, "Clínica Geral", "2026-11-20T12:00:00Z", "2026-11-20T12:30:00Z");
            var router = f.App.Services.GetRequiredService<UnitDatabaseRouter>();
            var unitDatabase = router.DerivedDatabaseName(u1);
            var direct = new NpgsqlConnectionStringBuilder(f.ConnectionString) { Database = unitDatabase, Pooling = false }.ConnectionString;
            const string waitingIn = "SELECT count(*) FROM pg_stat_activity WHERE wait_event_type='Lock' AND datname=$1";
            var central = new NpgsqlConnectionStringBuilder(f.ConnectionString).Database!;
            // Sem depender de temporização: o teste segura a linha da consulta no banco da UBS. O cancelamento delegado passa pela autorização central (vínculo FOR SHARE,
            // transação ainda aberta) e para na UBS; o encerramento chega depois e tem de esperar a linha do vínculo no banco central.
            await using var hold = new NpgsqlConnection(direct);
            await hold.OpenAsync();
            await using var holding = await hold.BeginTransactionAsync();
            await using (var lockRow = new NpgsqlCommand("SELECT 1 FROM nora_unit.appointments WHERE id=$1 FOR UPDATE", hold, holding)) { lockRow.Parameters.AddWithValue(appointment); await lockRow.ExecuteScalarAsync(); }
            var cancel = guardian.PostAsJsonAsync($"{Base(link, u1)}/appointments/{appointment}/cancel", new { confirmed = true });
            var deadline = DateTime.UtcNow.AddSeconds(20);
            while (await f.Scalar<long>(waitingIn, unitDatabase) < 1)
            {
                Require(DateTime.UtcNow < deadline && !cancel.IsCompleted, "O cancelamento delegado devia ficar esperando a linha que o teste segura na UBS.");
                await Task.Delay(25);
            }
            var end = f.Attendant.PostAsJsonAsync($"/api/obs/units/{u1}/family-links/{link}/end", new { confirmed = true });
            while (await f.Scalar<long>(waitingIn + " AND query ILIKE '%family_links%'", central) < 1)
            {
                Require(DateTime.UtcNow < deadline && !end.IsCompleted, "O encerramento devia esperar, no banco central, a linha do vínculo que a operação delegada segura.");
                await Task.Delay(25);
            }
            await Task.Delay(200);
            Require(!end.IsCompleted && !cancel.IsCompleted, "Com a operação delegada em andamento, o encerramento não pode terminar.");
            Require(await f.Scalar<string>("SELECT status FROM nora.family_links WHERE id=$1", link) == "approved", "O vínculo continua aprovado enquanto a operação delegada não termina.");
            await holding.RollbackAsync();
            using (var cancelled = await cancel) Expect(cancelled, 200);
            using (var ended = await end) Expect(ended, 200);
            Require(await f.Scalar<string>("SELECT status FROM nora.family_links WHERE id=$1", link) == "ended", "O encerramento conclui depois da operação.");
            Require(await f.UnitScalar<string>(u1, "SELECT status FROM nora_unit.appointments WHERE id=$1", appointment) == "cancelled", "A operação delegada vale.");
            Require(await f.UnitScalar<long>(u1, "SELECT count(*) FROM nora_unit.appointment_events WHERE appointment_id=$1 AND status='cancelled' AND source='patient' AND guardian_id IS NOT NULL", appointment) == 1
                && await f.UnitScalar<long>(u1, "SELECT count(*) FROM nora_unit.appointment_events WHERE appointment_id=$1 AND source='system'", appointment) == 0, "O cancelamento é do responsável (origem patient, com guardian_id), não do sistema.");
            using (var after = await guardian.PostAsJsonAsync($"{Base(link, u1)}/waiting-list/1/cancel", new { confirmed = true })) Expect(after, 403);
        });

        // ======== 3. Recusa de oferta pelo responsável ========
        await Check("Recusa de oferta pelo responsável: 200 sem consulta, oferta declined, entrada da fila encerrada com autor e guardian_id, vaga passa ao próximo; responder de novo → 409", async () =>
        {
            using var guardian = await Guardian(64, "Responsável Sessenta e Quatro");
            var link = await Linked(guardian, 93);
            using (var queue = await f.Attendant.PostAsJsonAsync($"/api/obs/units/{u1}/waiting-list", new { cpf = Cpf(93), specialty = "Ortopedia" })) Expect(queue, 201);
            using (var queueNext = await f.Attendant.PostAsJsonAsync($"/api/obs/units/{u1}/waiting-list", new { cpf = Cpf(1), specialty = "Ortopedia" })) Expect(queueNext, 201);
            var source = await StaffAppointment(31, "Ortopedia", "2026-11-21T12:00:00Z", "2026-11-21T12:30:00Z");
            using (var cancelled = await f.Attendant.PatchAsJsonAsync($"/api/obs/units/{u1}/appointments/{source}/status", new { status = "cancelled", confirmed = true })) Expect(cancelled, 200);
            using (var slot = await f.Attendant.PostAsJsonAsync($"/api/obs/units/{u1}/lightning-slots", new { appointmentId = source, confirmed = true })) Expect(slot, 201);
            Guid offer;
            using (var offers = await guardian.GetAsync($"{Base(link, u1)}/lightning-offers"))
            {
                Expect(offers, 200); var list = (await Read(offers)).GetProperty("offers").EnumerateArray().ToList();
                Require(list.Count == 1 && list[0].GetProperty("status").GetString() == "pending", "O responsável vê a oferta pendente do dependente.");
                offer = list[0].GetProperty("id").GetGuid();
            }
            using (var decline = await guardian.PostAsJsonAsync($"{Base(link, u1)}/lightning-offers/{offer}/respond", new { accept = false }))
            {
                Expect(decline, 200); var body = await Read(decline);
                Require(!body.GetProperty("accepted").GetBoolean() && body.GetProperty("appointmentId").ValueKind == JsonValueKind.Null, $"Recusa sem consulta: {body}.");
            }
            Require(await f.UnitScalar<string>(u1, "SELECT status FROM nora_unit.lightning_offers WHERE id=$1", offer) == "declined", "A oferta fica recusada.");
            Require(await f.UnitScalar<long>(u1, "SELECT count(*) FROM nora_unit.appointments WHERE cpf=$1", Cpf(93)) == 0, "A recusa não cria consulta para o dependente.");
            Require(await f.UnitScalar<string>(u1, "SELECT status FROM nora_unit.waiting_list WHERE cpf=$1 ORDER BY id DESC LIMIT 1", Cpf(93)) == "completed", "A entrada do dependente encerra o ciclo.");
            Require(await f.UnitScalar<long>(u1, "SELECT count(*) FROM nora_unit.waiting_list_events e JOIN nora_unit.waiting_list q ON q.id=e.entry_id WHERE q.cpf=$1 AND e.status='completed' AND e.source='patient' AND e.actor_id=e.guardian_id AND e.guardian_id IS NOT NULL", Cpf(93)) == 1, "Auditoria: autor e guardian_id = responsável, origem patient.");
            Require(await f.UnitScalar<long>(u1, "SELECT count(*) FROM nora_unit.lightning_offers o JOIN nora_unit.waiting_list q ON q.id=o.entry_id WHERE q.cpf=$1 AND o.status='pending'", Cpf(1)) == 1, "A vaga passa ao próximo da fila.");
            using (var again = await guardian.PostAsJsonAsync($"{Base(link, u1)}/lightning-offers/{offer}/respond", new { accept = false })) Expect(again, 409);
        });

        Console.WriteLine($"{checks - failures}/{checks} verificações das lacunas da Família passaram.");
        return failures;
    }
}
