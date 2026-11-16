using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Nora.Api.Database;

/// <summary>
/// Regressões da revisão crítica da Família (2026-11-16): limite por especialidade na rede pela conta própria e depois do fim do vínculo, autor da confirmação do próprio paciente, sugestão de e-mail
/// que sobrevive à falha do banco central, código de recuperação de senha invalidado pela troca de e-mail, recusa e encerramento que respondem 200 com o banco da UBS fora do ar, consulta periódica
/// das ofertas dos dependentes e oferta pendente cancelada quando o vínculo acaba. Mesmo fixture e mesmo relógio de <see cref="FamilyChecks"/>, num schema próprio.
/// </summary>
internal static partial class FamilyChecks
{
    public static async Task<int> RunReviewAsync(string root)
    {
        var connectionString = Environment.GetEnvironmentVariable("NORA_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Console.WriteLine("SKIP revisão da família PostgreSQL: configure NORA_TEST_CONNECTION somente para uma base descartável.");
            return 0;
        }

        checks = 0; failures = 0;
        await using var f = await Fixture.StartAsync(connectionString);
        var u1 = f.Unit1; var u2 = f.Unit2;
        Guid cardiology = Guid.Empty, clinical = Guid.Empty, ana = Guid.Empty, carla = Guid.Empty;
        string Base(Guid link, Guid unit) => $"/api/patient/dependents/{link}/units/{unit}";
        Task<HttpResponseMessage> Book(HttpClient client, Guid link, Guid unit, Guid professional, Guid specialty, string startsAt) =>
            client.PostAsJsonAsync($"{Base(link, unit)}/appointments", new { professionalId = professional, specialtyId = specialty, startsAt });
        async Task<Guid> Request(HttpClient client, int dependent, Guid unit)
        {
            using var response = await client.PostAsJsonAsync("/api/patient/dependents", new { cpf = Cpf(dependent), unitId = unit }); Expect(response, 201);
            return (await Read(response)).GetProperty("id").GetGuid();
        }
        async Task Approve(Guid unit, Guid link, HttpClient? staff = null) { using var response = await (staff ?? f.Attendant).PostAsJsonAsync($"/api/obs/units/{unit}/family-links/{link}/approve", new { confirmed = true }); Expect(response, 200); }
        async Task<Guid> Linked(HttpClient guardian, int dependent, Guid unit, HttpClient? staff = null) { var id = await Request(guardian, dependent, unit); await Approve(unit, id, staff); return id; }
        async Task DropTrigger() => await f.Exec("DROP TRIGGER IF EXISTS fail_suggestion ON nora.email_change_suggestions");
        // Banco central que falha ao gravar sugestões (a falha de infraestrutura que a revisão apontou).
        async Task FailSuggestions()
        {
            await f.Exec("CREATE OR REPLACE FUNCTION nora.fail_suggestion() RETURNS trigger LANGUAGE plpgsql AS 'BEGIN RAISE EXCEPTION ''falha simulada do banco central''; END'");
            await f.Exec("CREATE TRIGGER fail_suggestion BEFORE INSERT ON nora.email_change_suggestions FOR EACH ROW EXECUTE FUNCTION nora.fail_suggestion()");
        }
        // Tira o banco de uma UBS do ar para quem abre a conexão pela API: a identidade do banco deixa de ser a da UBS. Restaura no fim.
        async Task WithUnitDown(Guid unit, Func<Task> action)
        {
            var router = f.App.Services.GetRequiredService<UnitDatabaseRouter>();
            var direct = new NpgsqlConnectionStringBuilder(connectionString) { Database = router.DerivedDatabaseName(unit), Pooling = false }.ConnectionString;
            async Task Set(Guid value) { await using var raw = new NpgsqlConnection(direct); await raw.OpenAsync(); await using var update = new NpgsqlCommand("UPDATE nora_unit.identity SET unit_id=$1", raw); update.Parameters.AddWithValue(value); await update.ExecuteNonQueryAsync(); }
            await Set(Guid.NewGuid());
            try { await action(); }
            finally { await Set(unit); }
        }

        await Check("Preparação: especialidades, profissionais e grade das duas UBS", async () =>
        {
            using (var c = await f.Municipal.PostAsJsonAsync("/api/municipal/specialties", new { name = "Cardiologia" })) { Expect(c, 201); cardiology = (await Read(c)).GetProperty("id").GetGuid(); }
            using (var c = await f.Municipal.PostAsJsonAsync("/api/municipal/specialties", new { name = "Clínica Geral" })) { Expect(c, 201); clinical = (await Read(c)).GetProperty("id").GetGuid(); }
            using (var a = await f.Admin.PostAsJsonAsync($"/api/obs/units/{u1}/professionals", new { fullName = "Dra. Ana Souza", specialtyIds = new[] { cardiology, clinical } })) { Expect(a, 201); ana = (await Read(a)).GetProperty("id").GetGuid(); }
            using (var k = await f.Admin.PostAsJsonAsync($"/api/obs/units/{u2}/professionals", new { fullName = "Dra. Carla Reis", specialtyIds = new[] { cardiology } })) { Expect(k, 201); carla = (await Read(k)).GetProperty("id").GetGuid(); }
            Guid room1, roomA;
            using (var r = await f.Admin.PostAsJsonAsync($"/api/obs/units/{u1}/rooms", new { name = "Sala 1" })) { Expect(r, 201); room1 = (await Read(r)).GetProperty("id").GetGuid(); }
            using (var r = await f.Admin.PostAsJsonAsync($"/api/obs/units/{u2}/rooms", new { name = "Sala A" })) { Expect(r, 201); roomA = (await Read(r)).GetProperty("id").GetGuid(); }
            // Quarta: cardiologia 09:00-12:00 local (12:00Z a 14:30Z em seis atendimentos); quinta: clínica 09:00-11:00 local (quatro atendimentos); UBS 2, quarta 09:00-10:00 local (12:00Z e 12:30Z).
            using (var s = await f.Admin.PostAsJsonAsync($"/api/obs/units/{u1}/schedule", new { professionalId = ana, weekday = 3, startTime = "09:00", endTime = "12:00", slotMinutes = 30, roomId = room1, specialtyId = cardiology })) Expect(s, 201);
            using (var s = await f.Admin.PostAsJsonAsync($"/api/obs/units/{u1}/schedule", new { professionalId = ana, weekday = 4, startTime = "09:00", endTime = "11:00", slotMinutes = 30, roomId = room1, specialtyId = clinical })) Expect(s, 201);
            using (var s = await f.Admin.PostAsJsonAsync($"/api/obs/units/{u2}/schedule", new { professionalId = carla, weekday = 3, startTime = "09:00", endTime = "10:00", slotMinutes = 30, roomId = roomA, specialtyId = cardiology })) Expect(s, 201);
        });

        // ======== Achados 1 e 4: a conta própria do dependente conta as consultas marcadas pelo responsável em outra UBS ========
        await Check("Limite por especialidade: o dependente com conta própria numa UBS não fura o limite com a consulta que o responsável marcou em outra UBS da rede", async () =>
        {
            // O dependente 12 tem conta própria vinculada só à UBS 1; o responsável G2 tem vínculo aprovado com ele na UBS 2 e marca Cardiologia lá.
            using (var register = await f.Admin.PostAsJsonAsync($"/api/obs/units/{u2}/registry", new { cpf = Cpf(12), fullName = "Dependente Doze", email = "d12@example.test" })) Expect(register, 201);
            var link = await Linked(f.G2, 12, u2, f.Admin);
            using (var guardian = await Book(f.G2, link, u2, carla, cardiology, "2026-11-18T12:00:00Z")) Expect(guardian, 201);
            using (var own = await f.D2.PostAsJsonAsync($"/api/patient/units/{u1}/appointments", new { professionalId = ana, specialtyId = cardiology, startsAt = "2026-11-18T12:00:00Z" })) await ExpectProblem(own, 409, "specialty-limit");
            Require(await f.UnitScalar<long>(u1, "SELECT count(*) FROM nora_unit.appointments WHERE cpf=$1", Cpf(12)) == 0, "A recusa não pode gravar consulta na UBS 1.");
            // A ordem inversa já era recusada: continua.
            using (var other = await f.D2.PostAsJsonAsync($"/api/patient/units/{u1}/appointments", new { professionalId = ana, specialtyId = clinical, startsAt = "2026-11-19T13:30:00Z" })) Expect(other, 201); // outra especialidade segue livre
        });

        // ======== Achados 2 e 5: o fim do vínculo não apaga a consulta marcada pelo app ========
        await Check("Limite por especialidade: a consulta marcada por um vínculo que depois foi encerrado continua contando na rede", async () =>
        {
            var first = await Linked(f.G1, 11, u1);
            using (var booked = await Book(f.G1, first, u1, ana, cardiology, "2026-11-18T12:30:00Z")) Expect(booked, 201);
            using (var end = await f.G1.PostAsJsonAsync($"/api/patient/dependents/{first}/end", new { confirmed = true })) Expect(end, 200);
            var second = await Linked(f.G2, 11, u2, f.Admin);
            using (var again = await Book(f.G2, second, u2, carla, cardiology, "2026-11-18T12:30:00Z")) await ExpectProblem(again, 409, "specialty-limit");
            Require(await f.UnitScalar<long>(u2, "SELECT count(*) FROM nora_unit.appointments WHERE cpf=$1", Cpf(11)) == 0, "A recusa não pode gravar consulta na UBS 2.");
        });

        // ======== Achado 3: quem confirmou ========
        await Check("Confirmar presença pelo próprio paciente grava confirmed_by com a conta dele", async () =>
        {
            Guid id;
            using (var own = await f.G1.PostAsJsonAsync($"/api/patient/units/{u1}/appointments", new { professionalId = ana, specialtyId = cardiology, startsAt = "2026-11-18T13:00:00Z" })) { Expect(own, 201); id = (await Read(own)).GetProperty("id").GetGuid(); }
            f.Clock.Set(T0.AddHours(2)); // a confirmação abre 48 h antes do início
            try { using var confirm = await f.G1.PostAsync($"/api/patient/units/{u1}/appointments/{id}/confirm", null); Expect(confirm, 200); }
            finally { f.Clock.Set(T0); }
            Require(await f.UnitScalar<Guid>(u1, "SELECT confirmed_by FROM nora_unit.appointments WHERE id=$1 AND confirmed_at IS NOT NULL", id) == f.PatientIds[1], "confirmed_by deve ser a conta do paciente que confirmou.");
        });

        // ======== Achado 6: sugestão de e-mail e falha do banco central ========
        await Check("Sugestão de e-mail: se o banco central falha ao gravá-la, repetir a correção da UBS a cria (PATCH do e-mail e PUT do cadastro)", async () =>
        {
            await FailSuggestions();
            try
            {
                using (var failed = await f.Admin.PatchAsJsonAsync($"/api/obs/units/{u1}/registry/email", new { cpf = Cpf(24), email = "novo24@example.test" })) Require((int)failed.StatusCode >= 500, $"A falha do banco central devia virar erro 5xx; recebido {(int)failed.StatusCode}.");
                using (var failedPut = await f.Admin.PutAsJsonAsync($"/api/obs/units/{u1}/registry", new { cpf = Cpf(23), fullName = "Paciente Vinte e Três", email = "novo23@example.test" })) Require((int)failedPut.StatusCode >= 500, $"A falha do banco central devia virar erro 5xx; recebido {(int)failedPut.StatusCode}.");
            }
            finally { await DropTrigger(); }
            using (var retry = await f.Admin.PatchAsJsonAsync($"/api/obs/units/{u1}/registry/email", new { cpf = Cpf(24), email = "novo24@example.test" })) Expect(retry, 204);
            using (var retryPut = await f.Admin.PutAsJsonAsync($"/api/obs/units/{u1}/registry", new { cpf = Cpf(23), fullName = "Paciente Vinte e Três", email = "novo23@example.test" })) Expect(retryPut, 204);
            Require(await f.Scalar<string>("SELECT suggested_email FROM nora.email_change_suggestions WHERE account_id=$1 AND status='pending'", f.PatientIds[24]) == "novo24@example.test", "A repetição do PATCH devia criar a sugestão do P24.");
            Require(await f.Scalar<string>("SELECT suggested_email FROM nora.email_change_suggestions WHERE account_id=$1 AND status='pending'", f.PatientIds[23]) == "novo23@example.test", "A repetição do PUT devia criar a sugestão do P23.");
            Require(await f.Scalar<long>("SELECT count(*) FROM nora.email_change_suggestions WHERE account_id=ANY($1)", new[] { f.PatientIds[23], f.PatientIds[24] }) == 2, "Uma sugestão por conta, sem duplicar.");
        });

        // ======== Achado 7: código de recuperação de senha e troca de e-mail ========
        await Check("Trocar o e-mail da conta invalida os códigos de recuperação de senha já enviados ao endereço antigo", async () =>
        {
            using (var correct = await f.Admin.PatchAsJsonAsync($"/api/obs/units/{u1}/registry/email", new { cpf = Cpf(26), email = "novo26@example.test" })) Expect(correct, 204);
            Guid sent = Guid.NewGuid(), otherAccount = Guid.NewGuid();
            await f.Exec("INSERT INTO nora.patient_password_reset_codes(challenge_id,account_id,code_hash,created_at,expires_at) VALUES($1,$2,$3,$4,$5)", sent, f.PatientIds[26], new string('a', 64), T0, T0.AddMinutes(10));
            await f.Exec("INSERT INTO nora.patient_password_reset_codes(challenge_id,account_id,code_hash,created_at,expires_at) VALUES($1,$2,$3,$4,$5)", otherAccount, f.PatientIds[3], new string('b', 64), T0, T0.AddMinutes(10));
            var client = await LoginAsync(f, Cpf(26));
            using (var send = await client.PostAsync("/api/patient/email-suggestion/send-code", null)) Expect(send, 204);
            var code = Regex.Match(f.Mail.Sent.Last(m => m.To == "novo26@example.test").Body, @"\d{6}").Value;
            using (var confirm = await client.PostAsJsonAsync("/api/patient/email-suggestion/confirm", new { code })) { Expect(confirm, 200); Require((await Read(confirm)).GetProperty("codeValid").GetBoolean(), "Código certo."); }
            Require(await f.Scalar<long>("SELECT count(*) FROM nora.patient_password_reset_codes WHERE challenge_id=$1 AND consumed_at IS NOT NULL", sent) == 1, "O código de recuperação enviado ao endereço antigo ainda vale depois da troca.");
            Require(await f.Scalar<long>("SELECT count(*) FROM nora.patient_password_reset_codes WHERE challenge_id=$1 AND consumed_at IS NULL", otherAccount) == 1, "O código de outra conta não pode ser tocado.");
        });

        // ======== Achado 8: a decisão gravada não vira erro por causa do nome ========
        await Check("Recusar ou encerrar um vínculo com o banco da UBS fora do ar responde 200 (a decisão já foi gravada) e o nome do dependente vem vazio", async () =>
        {
            var toReject = await Request(f.G3, 13, u1); var toEnd = await Linked(f.G3, 14, u1);
            await WithUnitDown(u1, async () =>
            {
                using var reject = await f.Attendant.PostAsJsonAsync($"/api/obs/units/{u1}/family-links/{toReject}/reject", new { confirmed = true, reason = "Documento ilegível" });
                Expect(reject, 200); var body = await Read(reject);
                Require(body.GetProperty("status").GetString() == "rejected" && body.GetProperty("dependentName").ValueKind == JsonValueKind.Null, "Recusa com o banco da UBS fora do ar.");
                using var end = await f.Attendant.PostAsJsonAsync($"/api/obs/units/{u1}/family-links/{toEnd}/end", new { confirmed = true });
                Expect(end, 200); var ended = await Read(end);
                Require(ended.GetProperty("status").GetString() == "ended" && ended.GetProperty("dependentName").ValueKind == JsonValueKind.Null, "Encerramento com o banco da UBS fora do ar.");
            });
            Require(await f.Scalar<string>("SELECT status FROM nora.family_links WHERE id=$1", toReject) == "rejected" && await f.Scalar<string>("SELECT status FROM nora.family_links WHERE id=$1", toEnd) == "ended", "As decisões ficam gravadas.");
        });

        // ======== Achado 10: oferta pendente quando o vínculo acaba ========
        async Task EndedLinkReleasesOffer(string label, HttpClient guardian, int dependent, int waiter, string slotStart, bool endByStaff)
        {
            var link = await Linked(guardian, dependent, u1);
            Guid source;
            using (var queue = await f.Attendant.PostAsJsonAsync($"/api/obs/units/{u1}/waiting-list", new { cpf = Cpf(dependent), specialty = "Cardiologia" })) Expect(queue, 201);
            using (var created = await f.Attendant.PostAsJsonAsync($"/api/obs/units/{u1}/appointments", new { cpf = Cpf(41), professionalId = ana, specialtyId = cardiology, startsAt = slotStart })) { Expect(created, 201); source = (await Read(created)).GetProperty("id").GetGuid(); }
            using (var cancelled = await f.Attendant.PatchAsJsonAsync($"/api/obs/units/{u1}/appointments/{source}/status", new { status = "cancelled", confirmed = true })) Expect(cancelled, 200);
            using (var slot = await f.Attendant.PostAsJsonAsync($"/api/obs/units/{u1}/lightning-slots", new { appointmentId = source, confirmed = true })) Expect(slot, 201);
            Require(await f.UnitScalar<long>(u1, "SELECT count(*) FROM nora_unit.lightning_offers o JOIN nora_unit.waiting_list q ON q.id=o.entry_id WHERE q.cpf=$1 AND o.status='pending'", Cpf(dependent)) == 1, $"{label}: o dependente recebe a oferta pelo vínculo.");
            // Quem entra na fila depois fica esperando: a vaga só passa a ele quando a oferta do dependente cair.
            using (var queueWaiter = await f.Attendant.PostAsJsonAsync($"/api/obs/units/{u1}/waiting-list", new { cpf = Cpf(waiter), specialty = "Cardiologia" })) Expect(queueWaiter, 201);
            if (endByStaff) { using var end = await f.Attendant.PostAsJsonAsync($"/api/obs/units/{u1}/family-links/{link}/end", new { confirmed = true }); Expect(end, 200); }
            else { using var end = await guardian.PostAsJsonAsync($"/api/patient/dependents/{link}/end", new { confirmed = true }); Expect(end, 200); }
            Require(await f.UnitScalar<long>(u1, "SELECT count(*) FROM nora_unit.lightning_offers o JOIN nora_unit.waiting_list q ON q.id=o.entry_id WHERE q.cpf=$1 AND o.status='pending'", Cpf(dependent)) == 0, $"{label}: a oferta pendente do dependente sem acesso devia ser cancelada na hora.");
            Require(await f.UnitScalar<long>(u1, "SELECT count(*) FROM nora_unit.lightning_offers o JOIN nora_unit.waiting_list q ON q.id=o.entry_id WHERE q.cpf=$1 AND o.status='cancelled'", Cpf(dependent)) == 1, $"{label}: a oferta fica cancelada, não expirada.");
            Require(await f.UnitScalar<string>(u1, "SELECT status FROM nora_unit.waiting_list WHERE cpf=$1 ORDER BY id DESC LIMIT 1", Cpf(dependent)) == "cancelled", $"{label}: a entrada da fila do dependente deixa de ficar 'offered'.");
            Require(await f.UnitScalar<long>(u1, "SELECT count(*) FROM nora_unit.waiting_list_events e JOIN nora_unit.waiting_list q ON q.id=e.entry_id WHERE q.cpf=$1 AND e.status='cancelled'", Cpf(dependent)) == 1, $"{label}: o cancelamento da entrada fica na auditoria.");
            Require(await f.UnitScalar<long>(u1, "SELECT count(*) FROM nora_unit.lightning_offers o JOIN nora_unit.waiting_list q ON q.id=o.entry_id WHERE q.cpf=$1 AND o.status='pending'", Cpf(waiter)) == 1, $"{label}: a vaga passa ao próximo elegível da fila.");
            Require(await f.UnitScalar<string>(u1, "SELECT s.status FROM nora_unit.lightning_slots s WHERE s.appointment_id=$1", source) == "offered", $"{label}: a vaga volta a ser oferecida.");
        }
        await Check("Encerrar o vínculo pelo responsável cancela a oferta pendente do dependente, reabre a vaga e a oferece ao próximo da fila", async () => await EndedLinkReleasesOffer("Responsável", f.G4, 14, 21, "2026-11-18T14:00:00Z", endByStaff: false));
        await Check("Encerrar o vínculo pela equipe cancela a oferta pendente do dependente, reabre a vaga e a oferece ao próximo da fila", async () => await EndedLinkReleasesOffer("Equipe", f.G3, 31, 24, "2026-11-18T14:30:00Z", endByStaff: true));
        await Check("Encerrar o vínculo não toca na oferta de quem continua elegível (dependente com conta própria vinculada à UBS)", async () =>
        {
            var link = await Linked(f.G4, 12, u1);
            Guid source;
            using (var queue = await f.Attendant.PostAsJsonAsync($"/api/obs/units/{u1}/waiting-list", new { cpf = Cpf(12), specialty = "Clínica Geral" })) Expect(queue, 201);
            using (var created = await f.Attendant.PostAsJsonAsync($"/api/obs/units/{u1}/appointments", new { cpf = Cpf(41), professionalId = ana, specialtyId = clinical, startsAt = "2026-11-19T12:30:00Z" })) { Expect(created, 201); source = (await Read(created)).GetProperty("id").GetGuid(); }
            using (var cancelled = await f.Attendant.PatchAsJsonAsync($"/api/obs/units/{u1}/appointments/{source}/status", new { status = "cancelled", confirmed = true })) Expect(cancelled, 200);
            using (var slot = await f.Attendant.PostAsJsonAsync($"/api/obs/units/{u1}/lightning-slots", new { appointmentId = source, confirmed = true })) Expect(slot, 201);
            Require(await f.UnitScalar<long>(u1, "SELECT count(*) FROM nora_unit.lightning_offers o JOIN nora_unit.waiting_list q ON q.id=o.entry_id WHERE q.cpf=$1 AND o.status='pending'", Cpf(12)) == 1, "O dependente com conta recebe a oferta.");
            using (var end = await f.G4.PostAsJsonAsync($"/api/patient/dependents/{link}/end", new { confirmed = true })) Expect(end, 200);
            Require(await f.UnitScalar<long>(u1, "SELECT count(*) FROM nora_unit.lightning_offers o JOIN nora_unit.waiting_list q ON q.id=o.entry_id WHERE q.cpf=$1 AND o.status='pending'", Cpf(12)) == 1, "Ele ainda responde pela própria conta: a oferta continua pendente.");
        });

        // ======== Achado 9: consulta periódica das ofertas dos dependentes ========
        await Check("Consulta agregada das ofertas: dois dependentes da mesma UBS vêm juntos, na ordem dos vínculos, só com ofertas pendentes e sem escrever nada", async () =>
        {
            var firstLink = await Linked(f.G2, 41, u1); var secondLink = await Linked(f.G2, 13, u1);
            // Dois horários de clínica geral de quinta (12:00Z e 13:00Z) viram Vaga Relâmpago e vão aos dois dependentes, que entram na fila nessa ordem.
            foreach (var dependent in new[] { 41, 13 }) { using var queue = await f.Attendant.PostAsJsonAsync($"/api/obs/units/{u1}/waiting-list", new { cpf = Cpf(dependent), specialty = "Clínica Geral" }); Expect(queue, 201); }
            foreach (var start in new[] { "2026-11-19T12:00:00Z", "2026-11-19T13:00:00Z" })
            {
                Guid source;
                using (var created = await f.Attendant.PostAsJsonAsync($"/api/obs/units/{u1}/appointments", new { cpf = Cpf(31), professionalId = ana, specialtyId = clinical, startsAt = start })) { Expect(created, 201); source = (await Read(created)).GetProperty("id").GetGuid(); }
                using (var cancelled = await f.Attendant.PatchAsJsonAsync($"/api/obs/units/{u1}/appointments/{source}/status", new { status = "cancelled", confirmed = true })) Expect(cancelled, 200);
                using (var slot = await f.Attendant.PostAsJsonAsync($"/api/obs/units/{u1}/lightning-slots", new { appointmentId = source, confirmed = true })) Expect(slot, 201);
            }
            var before = await f.UnitScalar<string>(u1, "SELECT (SELECT count(*) FROM nora_unit.lightning_offers)||'/'||(SELECT count(*) FROM nora_unit.waiting_list_events)||'/'||(SELECT count(*) FROM nora_unit.lightning_slots WHERE status='offered')");
            using (var aggregate = await f.G2.GetAsync("/api/patient/dependents/lightning-offers"))
            {
                Expect(aggregate, 200); var body = await Read(aggregate); var items = body.GetProperty("dependents").EnumerateArray().ToList();
                // "Ordem dos vínculos" é a do banco: data do pedido e, no empate, o id. O relógio dos testes fica parado, então os dois pedidos
                // empatam na data e o id (UUID aleatório) decide; a ordem esperada vem da mesma regra, não da ordem em que o teste os criou.
                var expectedOrder = await f.Scalar<string>("SELECT string_agg(id::text, ',' ORDER BY requested_at, id) FROM nora.family_links WHERE id = ANY($1)", new[] { firstLink, secondLink });
                var names = new Dictionary<Guid, string> { [firstLink] = "Paciente Quarenta e Um", [secondLink] = "Dependente Treze" };
                Require(items.Count == 2 && string.Join(',', items.Select(i => i.GetProperty("dependentId").GetGuid().ToString())) == expectedOrder, $"Um item por dependente, na ordem dos vínculos. Esperado {expectedOrder}; recebido {body}");
                Require(items.All(i => i.GetProperty("dependentName").GetString() == names[i.GetProperty("dependentId").GetGuid()]) && items.All(i => i.GetProperty("unitName").GetString() == "UBS Família Um" && i.GetProperty("offers").GetArrayLength() == 1 && i.GetProperty("offers")[0].GetProperty("status").GetString() == "pending"), "Nome, UBS e oferta pendente de cada dependente.");
            }
            Require(before == await f.UnitScalar<string>(u1, "SELECT (SELECT count(*) FROM nora_unit.lightning_offers)||'/'||(SELECT count(*) FROM nora_unit.waiting_list_events)||'/'||(SELECT count(*) FROM nora_unit.lightning_slots WHERE status='offered')"), "A consulta não grava nada.");
            // Cadastro do dependente inativo na UBS: some da consulta, sem derrubá-la.
            using (var off = await f.Admin.PatchAsJsonAsync($"/api/obs/units/{u1}/registry/active", new { cpf = Cpf(41), isActive = false, confirmed = true })) Expect(off, 204);
            using (var after = await f.G2.GetAsync("/api/patient/dependents/lightning-offers")) { Expect(after, 200); Require((await Read(after)).GetProperty("dependents").EnumerateArray().Select(i => i.GetProperty("dependentId").GetGuid()).SequenceEqual([secondLink]), "Só o dependente com cadastro ativo."); }
        });

        Console.WriteLine($"{checks - failures}/{checks} verificações da revisão da Família passaram.");
        return failures;
    }

    private static async Task<HttpClient> LoginAsync(Fixture f, string cpf)
    {
        var client = new HttpClient { BaseAddress = f.Anonymous.BaseAddress, Timeout = TimeSpan.FromSeconds(60) };
        using var response = await client.PostAsJsonAsync("/api/auth/login", new { scope = "patient", identifier = cpf, password = Password });
        Expect(response, 200);
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", (await Read(response)).GetProperty("token").GetString()!);
        return client;
    }
}
