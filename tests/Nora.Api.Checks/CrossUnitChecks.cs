using System.Net.Http.Json;
using System.Text.Json;
using Nora.Api.DAOs;
using static RoundFixture;

/// <summary>
/// Conflito de horário entre UBS: a mesma pessoa não pode ter duas consultas que se sobrepõem em UBS diferentes (app e OBS), sem dizer qual UBS. A garantia contra corrida vem do lock de CPF
/// segurado até depois do commit da UBS, e o índice central de cadastros diz onde conferir. Roda com <c>--cross-unit-only</c>.
/// </summary>
internal static class CrossUnitChecks
{
    private const string Message = "Já tem consulta neste horário em outra UBS.";

    public static async Task<int> RunAsync(string root)
    {
        var connection = Environment.GetEnvironmentVariable("NORA_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection))
        {
            Console.WriteLine("SKIP conflito entre UBS: configure NORA_TEST_CONNECTION somente para uma base descartável.");
            return 0;
        }
        var suite = new Suite("conflito entre UBS");
        await using var f = await StartAsync(connection, "crossunit", unitCount: 3);
        var u1 = f.Unit1; var u2 = f.Unit2; var u3 = f.Units[2];
        var cardiology = await f.SpecialtyAsync("Cardiologia"); var clinical = await f.SpecialtyAsync("Clínica geral");
        var ana = await f.ProfessionalAsync(u1, "Dra. Ana Souza", cardiology); var carla = await f.ProfessionalAsync(u2, "Dra. Carla Dias", clinical);
        await f.TemplateAsync(u1, ana, 3, "08:00", "13:00", 30, await f.RoomAsync(u1, "Sala 1"), cardiology);
        await f.TemplateAsync(u2, carla, 3, "08:00", "13:00", 30, await f.RoomAsync(u2, "Sala 1"), clinical);
        var slots = Enumerable.Range(0, 10).Select(i => Wednesday("08:00").AddMinutes(30 * i)).ToArray(); // 08:00 a 12:30 locais, de 30 em 30

        async Task<HttpResponseMessage> AppBook(HttpClient client, Guid unit, Guid professional, Guid specialty, DateTimeOffset start) =>
            await client.PostAsJsonAsync($"/api/patient/units/{unit}/appointments", new { professionalId = professional, specialtyId = specialty, startsAt = start });
        async Task<HttpResponseMessage> StaffBook(Guid unit, int patient, Guid professional, Guid specialty, DateTimeOffset start, HttpClient? client = null) =>
            await (client ?? f.Admin).PostAsJsonAsync($"/api/obs/units/{unit}/appointments", new { cpf = Cpf(patient), professionalId = professional, specialtyId = specialty, startsAt = start });
        async Task ExpectOverlap(HttpResponseMessage response)
        {
            var body = await ExpectProblem(response, 409, "other-unit-overlap");
            Ensure(body.GetProperty("detail").GetString() == Message && !body.GetRawText().Contains("rodada", StringComparison.OrdinalIgnoreCase) && !body.GetRawText().Contains(u1.ToString(), StringComparison.OrdinalIgnoreCase) && !body.GetRawText().Contains(u2.ToString(), StringComparison.OrdinalIgnoreCase),
                "O erro nunca diz qual é a outra UBS.");
        }
        async Task<long> Count(Guid unit, int patient) => await f.UnitScalar<long>(unit, "SELECT count(*) FROM nora_unit.appointments WHERE cpf=$1 AND status IN ('scheduled','called','completed','missed')", Cpf(patient));

        var pa = await f.PatientAsync(1, u1, u2); await f.RegisterAsync(1, u3);
        await suite.Check("Pelo app: mesma pessoa no mesmo horário em outra UBS → 409 other-unit-overlap sem citar a UBS; horário encostado passa; consulta cancelada não bloqueia; o cadastro grava o índice central", async () =>
        {
            Ensure(await f.Scalar<long>("SELECT count(*) FROM nora.unit_registry_index WHERE cpf=$1", Cpf(1)) == 3, "Cada cadastro feito pela API grava a UBS no índice central.");
            Guid first;
            using (var ok = await AppBook(pa, u1, ana, cardiology, slots[0])) { Expect(ok, 201); first = (await Read(ok)).GetProperty("id").GetGuid(); }
            using (var overlap = await AppBook(pa, u2, carla, clinical, slots[0])) await ExpectOverlap(overlap);
            Ensure(await Count(u2, 1) == 0, "A recusa não gravou nada na outra UBS.");
            using (var touching = await AppBook(pa, u2, carla, clinical, slots[1])) Expect(touching, 201); // 08:30: começa quando a outra termina
            using (var cancel = await pa.PostAsJsonAsync($"/api/patient/units/{u1}/appointments/{first}/cancel", new { confirmed = true })) Expect(cancel, 200);
            // Cancelada na UBS 1: o horário 08:00 já não bloqueia a marcação da equipe na UBS 2.
            using (var afterCancel = await StaffBook(u2, 1, carla, clinical, slots[0])) Expect(afterCancel, 201);
            Ensure(await Count(u1, 1) == 0 && await Count(u2, 1) == 2, "Só as duas consultas da UBS 2 ficaram.");
        });

        await suite.Check("Pelo OBS: marcação pelo catálogo e em texto livre (UBS sem profissional) → 409 other-unit-overlap", async () =>
        {
            var pb = await f.PatientAsync(2, u1, u2); await f.RegisterAsync(2, u3);
            using (var app = await AppBook(pb, u1, ana, cardiology, slots[2])) Expect(app, 201);
            using (var catalog = await StaffBook(u2, 2, carla, clinical, slots[2])) await ExpectOverlap(catalog);
            Ensure(await Count(u2, 2) == 0, "Nada gravado na UBS 2.");
            var start = slots[2]; // a UBS 3 não tem profissional: texto livre
            using (var free = await f.Admin.PostAsJsonAsync($"/api/obs/units/{u3}/appointments", new { cpf = Cpf(2), specialty = "Clínica geral", professional = "Dr. Qualquer", startsAt = start, endsAt = start.AddMinutes(30) })) await ExpectOverlap(free);
            using (var freeTouching = await f.Admin.PostAsJsonAsync($"/api/obs/units/{u3}/appointments", new { cpf = Cpf(2), specialty = "Clínica geral", professional = "Dr. Qualquer", startsAt = start.AddMinutes(30), endsAt = start.AddMinutes(60) })) Expect(freeTouching, 201);
            using (var invalidCpf = await f.Admin.PostAsJsonAsync($"/api/obs/units/{u2}/appointments", new { cpf = "123", professionalId = carla, specialtyId = clinical, startsAt = slots[3] })) Expect(invalidCpf, 400);
            using (var samePoint = await StaffBook(u1, 2, ana, cardiology, slots[2])) Ensure((int)samePoint.StatusCode == 409, "Dentro da mesma UBS continua o conflito de sempre.");
        });

        await suite.Check("Aceitar oferta na outra UBS: 409 other-unit-overlap, tudo desfeito e a oferta continua pendente (a pessoa pode recusar)", async () =>
        {
            var pc = await f.PatientAsync(3, u1, u2);
            using (var app = await AppBook(pc, u1, ana, cardiology, slots[3])) Expect(app, 201);
            // Vaga automática na UBS 2 no mesmo horário (consulta cancelada de outra pessoa) e P3 com consulta posterior de Clínica geral lá: a oferta automática não consulta outras UBS.
            var later = Guid.NewGuid();
            await f.UnitExec(u2, "INSERT INTO nora_unit.appointments(id,cpf,specialty,professional,starts_at,ends_at,status,created_by,created_at) VALUES($1,$2,'Clínica geral','Agenda futura',$3,$4,'scheduled',$5,$6)", later, Cpf(3), slots[3].AddDays(30), slots[3].AddDays(30).AddMinutes(30), f.AdminId, f.Clock.GetUtcNow());
            await f.UnitExec(u2, "INSERT INTO nora_unit.appointments(id,cpf,specialty,specialty_id,professional,professional_id,starts_at,ends_at,status,created_by,created_at) VALUES(gen_random_uuid(),$1,'Clínica geral',$2,'Dra. Carla Dias',$3,$4,$5,'cancelled',$6,$7)", Cpf(2), clinical, carla, slots[3], slots[3].AddMinutes(30), f.AdminId, f.Clock.GetUtcNow());
            await f.AdvanceAsync(u2);
            using var offers = await pc.GetAsync($"/api/patient/units/{u2}/lightning-offers"); Expect(offers, 200);
            var offer = (await Read(offers)).GetProperty("offers").EnumerateArray().Last().GetProperty("id").GetGuid();
            using (var accept = await pc.PostAsJsonAsync($"/api/patient/units/{u2}/lightning-offers/{offer}/respond", new { accept = true })) await ExpectOverlap(accept);
            Ensure(await f.UnitScalar<string>(u2, "SELECT o.status||':'||s.status||':'||a.status FROM nora_unit.lightning_offers o JOIN nora_unit.lightning_slots s ON s.id=o.slot_id JOIN nora_unit.appointments a ON a.id=o.appointment_id WHERE o.id=$1", offer) == "pending:offered:scheduled" && await Count(u2, 3) == 1,
                "A oferta continua pendente, a consulta a antecipar continua marcada e nenhuma consulta nova foi criada.");
            using (var decline = await pc.PostAsJsonAsync($"/api/patient/units/{u2}/lightning-offers/{offer}/respond", new { accept = false })) Expect(decline, 200);
        });

        await suite.Check("Pessoa sem conta (só cadastros da equipe) e dependente pelo responsável também são barrados", async () =>
        {
            await f.RegisterAsync(20, u1); await f.RegisterAsync(20, u2);
            using (var first = await StaffBook(u1, 20, ana, cardiology, slots[4])) Expect(first, 201);
            using (var second = await StaffBook(u2, 20, carla, clinical, slots[4])) await ExpectOverlap(second);
            var guardian = await f.PatientAsync(30); await f.RegisterAsync(31, u1); await f.RegisterAsync(31, u2);
            var link1 = Guid.NewGuid(); var link2 = Guid.NewGuid();
            foreach (var (link, unit) in new[] { (link1, u1), (link2, u2) })
                await f.Exec("INSERT INTO nora.family_links(id,guardian_id,dependent_cpf,unit_id,status,requested_at,decided_by,decided_at) VALUES($1,$2,$3,$4,'approved',$5,$6,$5)", link, f.PatientIds[30], Cpf(31), unit, RoundClock.T0, f.AdminId);
            using (var dependentFirst = await guardian.PostAsJsonAsync($"/api/patient/dependents/{link1}/units/{u1}/appointments", new { professionalId = ana, specialtyId = cardiology, startsAt = slots[5] })) Expect(dependentFirst, 201);
            using (var dependentSecond = await guardian.PostAsJsonAsync($"/api/patient/dependents/{link2}/units/{u2}/appointments", new { professionalId = carla, specialtyId = clinical, startsAt = slots[5] })) await ExpectOverlap(dependentSecond);
            using (var dependentTouching = await guardian.PostAsJsonAsync($"/api/patient/dependents/{link2}/units/{u2}/appointments", new { professionalId = carla, specialtyId = clinical, startsAt = slots[6] })) Expect(dependentTouching, 201);
        });

        await suite.Check("Banco de outra UBS fora do ar → 503 e nada gravado; desatualizado → 503 unit-database-outdated; UBS sem banco pronto é ignorada", async () =>
        {
            var pd = await f.PatientAsync(4, u1, u2);
            await f.WithUnitDownAsync(u1, async () =>
            {
                using var down = await AppBook(pd, u2, carla, clinical, slots[7]);
                await ExpectProblem(down, 503, "service-unavailable");
            });
            Ensure(await Count(u2, 4) == 0, "Com a outra UBS fora do ar nada foi gravado (falha fechada).");
            var legacy = await f.CreateLegacyUnitAsync(14, ready: true);
            await f.Exec("INSERT INTO nora.unit_registry_index(cpf,unit_id) VALUES($1,$2)", Cpf(4), legacy);
            using (var outdated = await AppBook(pd, u2, carla, clinical, slots[7])) await ExpectProblem(outdated, 503, "unit-database-outdated");
            Ensure(await Count(u2, 4) == 0, "Banco desatualizado também falha fechado.");
            await f.Exec("UPDATE nora.health_units SET database_ready_at=NULL WHERE id=$1", legacy);
            using (var pending = await AppBook(pd, u2, carla, clinical, slots[7])) Expect(pending, 201);
        });

        await suite.Check("A migração das UBS preenche o índice de cadastros antigos (criados direto no banco) e a conferência passa a valer", async () =>
        {
            var cpf = Cpf(50);
            foreach (var unit in new[] { u1, u2 }) await f.UnitExec(unit, "INSERT INTO nora_unit.patient_registry(cpf,full_name,email,created_by) VALUES($1,'Paciente Antigo','antigo50@example.test',$2)", cpf, f.AdminId);
            Ensure(await f.Scalar<long>("SELECT count(*) FROM nora.unit_registry_index WHERE cpf=$1", cpf) == 0, "Cadastro criado direto no banco ainda não está no índice.");
            await f.Service<UnitMigrationDao>().MigrateAsync(u1); await f.Service<UnitMigrationDao>().MigrateAsync(u2);
            await f.Service<UnitMigrationDao>().MigrateAsync(u2); // reaplicável: o índice não duplica
            Ensure(await f.Scalar<long>("SELECT count(*) FROM nora.unit_registry_index WHERE cpf=$1", cpf) == 2, "A migração copiou os cadastros para o índice.");
            using (var first = await StaffBook(u1, 50, ana, cardiology, slots[8])) Expect(first, 201);
            using (var second = await StaffBook(u2, 50, carla, clinical, slots[8])) await ExpectOverlap(second);
        });

        await suite.Check("Concorrência: o app numa UBS e o OBS na outra, no mesmo horário, várias vezes → exatamente um 201 e um 409; nenhum impasse com desativação e correção de e-mail do mesmo CPF", async () =>
        {
            for (var i = 0; i < 4; i++)
            {
                var number = 100 + i; var client = await f.PatientAsync(number, u1, u2);
                var start = slots[i].AddDays(7); // a quarta seguinte (ainda dentro da janela de 90 dias): grade igual
                var app = AppBook(client, u1, ana, cardiology, start); var staff = StaffBook(u2, number, carla, clinical, start);
                using var appResponse = await app; using var staffResponse = await staff;
                var codes = new[] { (int)appResponse.StatusCode, (int)staffResponse.StatusCode }.Order().ToArray();
                Ensure(codes.SequenceEqual([201, 409]), $"Corrida {i}: {string.Join(",", codes)}.");
                Ensure(await Count(u1, number) + await Count(u2, number) == 1, $"Corrida {i}: duas consultas confirmadas para a mesma pessoa.");
            }
            var q = await f.PatientAsync(120, u1, u2);
            var tasks = new[]
            {
                AppBook(q, u2, carla, clinical, slots[2].AddDays(14)),
                f.Admin.PatchAsJsonAsync($"/api/obs/units/{u1}/registry/active", new { cpf = Cpf(120), isActive = false, confirmed = true }),
                f.Admin.PatchAsJsonAsync($"/api/obs/units/{u1}/registry/email", new { cpf = Cpf(120), email = "novo120@example.test" })
            };
            var finished = await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(60));
            Ensure(finished.All(response => (int)response.StatusCode is 200 or 201 or 204 or 403 or 409), $"Sem impasse e sem erro inesperado: {string.Join(",", finished.Select(response => (int)response.StatusCode))}.");
            foreach (var response in finished) response.Dispose();
        });
        return suite.Report() == 0 ? 0 : 1;
    }
}
