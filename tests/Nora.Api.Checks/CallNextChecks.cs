using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;
using Nora.Api.DAOs;
using Nora.Api.Database;
using Nora.Api.Models;
using static RoundFixture;

/// <summary>
/// "Chamar próximo" da Fila do OBS: a atendente escolhe um horário livre da grade e a oferta vai ao primeiro elegível da especialidade. Cobre também a vaga com dados próprios (migração 015),
/// a recusa e o vencimento seguindo para os próximos da fila, o aceite, as recusas 409, a concorrência e a leitura nos indicadores. Roda com <c>--call-next-only</c>.
/// </summary>
internal static class CallNextChecks
{
    public static async Task<int> RunAsync(string root)
    {
        var connection = Environment.GetEnvironmentVariable("NORA_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection))
        {
            Console.WriteLine("SKIP chamar próximo: configure NORA_TEST_CONNECTION somente para uma base descartável.");
            return 0;
        }
        var suite = new Suite("chamar próximo");
        await using var f = await StartAsync(connection, "callnext");
        var u1 = f.Unit1; var u2 = f.Unit2;
        var cardiology = await f.SpecialtyAsync("Cardiologia"); var clinical = await f.SpecialtyAsync("Clínica geral"); var pediatrics = await f.SpecialtyAsync("Pediatria");
        var ana = await f.ProfessionalAsync(u1, "Dra. Ana Souza", cardiology); var bruno = await f.ProfessionalAsync(u1, "Dr. Bruno Lima", clinical);
        var room1 = await f.RoomAsync(u1, "Sala 1"); var room2 = await f.RoomAsync(u1, "Sala 2");
        await f.TemplateAsync(u1, ana, 3, "08:00", "10:00", 30, room1, cardiology);      // quarta 08:00 a 10:00
        await f.TemplateAsync(u1, ana, 4, "08:00", "09:00", 30, room1, cardiology);      // quinta 08:00 a 09:00
        await f.TemplateAsync(u1, ana, 1, "09:10", "10:10", 30, room1, cardiology);      // segunda (hoje) 09:10: exatamente 10 minutos depois do relógio de teste
        await f.TemplateAsync(u1, bruno, 3, "08:00", "10:00", 30, room2, clinical);
        var t1 = Wednesday("08:00"); var t2 = Wednesday("08:30"); var t3 = Wednesday("09:00");
        var thursday1 = t1.AddDays(1); var thursday2 = t2.AddDays(1);
        var monday = new DateTimeOffset(2026, 11, 16, 9, 10, 0, TimeSpan.FromHours(-3)).ToUniversalTime();
        // P1 só tem cadastro (sem conta); P2 a P12 têm conta vinculada. Todos entram na fila de Cardiologia nesta ordem.
        await f.RegisterAsync(1, u1);
        for (var number = 2; number <= 12; number++) await f.PatientAsync(number, u1);
        var (_, attendantDevice) = await ObsDeviceHelper.ActivateAsync(f.Admin, u1);
        var attendant = f.NewClient(); await f.LoginAsync(attendant, "obs", "atendente@example.test", attendantDevice);
        async Task Join(int number, string specialty = "Cardiologia")
        {
            using var response = await f.Admin.PostAsJsonAsync($"/api/obs/units/{u1}/waiting-list", new { cpf = Cpf(number), specialty });
            Expect(response, 201); f.Clock.Advance(TimeSpan.FromSeconds(5));
        }
        for (var number = 1; number <= 12; number++) await Join(number);
        // P2 tem o cadastro desativado e P3 tem consultas com o Dr. Bruno às 08:00 e às 08:30 (cruzam T1 e T2 da Dra. Ana).
        await f.UnitExec(u1, "UPDATE nora_unit.patient_registry SET is_active=false WHERE cpf=$1", Cpf(2));
        foreach (var start in new[] { t1, t2 })
            using (var booked = await f.Admin.PostAsJsonAsync($"/api/obs/units/{u1}/appointments", new { cpf = Cpf(3), professionalId = bruno, specialtyId = clinical, startsAt = start })) Expect(booked, 201);

        async Task<HttpResponseMessage> Call(HttpClient client, DateTimeOffset start, Guid? specialty = null, Guid? professional = null, bool confirmed = true, Guid? unit = null) =>
            await client.PostAsJsonAsync($"/api/obs/units/{unit ?? u1}/waiting-list/call-next", new { specialtyId = specialty ?? cardiology, professionalId = professional ?? ana, startsAt = start, confirmed });
        async Task<JsonElement> CallOk(HttpClient client, DateTimeOffset start)
        {
            using var response = await Call(client, start); Expect(response, 201); return await Read(response);
        }
        async Task<string> Offers(Guid slot)
        {
            using var response = await f.Admin.GetAsync($"/api/obs/units/{u1}/lightning-slots/{slot}/offers"); Expect(response, 200);
            return string.Join(",", (await Read(response)).EnumerateArray().Select(offer => offer.GetProperty("patientName").GetString() + ":" + offer.GetProperty("status").GetString()));
        }
        async Task<JsonElement> PatientOffer(int number)
        {
            using var response = await f.Patients[number].GetAsync($"/api/patient/units/{u1}/lightning-offers"); Expect(response, 200);
            return (await Read(response)).GetProperty("offers").EnumerateArray().Last();
        }

        Guid slot1 = Guid.Empty, slot2 = Guid.Empty;
        await suite.Check("Prévia do primeiro da fila e a oferta ao primeiro elegível: pula quem não tem conta, cadastro inativo, consulta no horário e oferta pendente; a vaga nasce com dados próprios", async () =>
        {
            using (var preview = await attendant.GetAsync($"/api/obs/units/{u1}/waiting-list/next?specialtyId={cardiology}"))
            {
                Expect(preview, 200); var next = (await Read(preview)).GetProperty("next");
                Ensure(next.GetProperty("patientName").GetString() == "Paciente 3" && next.GetProperty("entryId").GetInt64() > 0 && next.GetProperty("enteredAt").GetDateTimeOffset() > RoundClock.T0, "A prévia ignora horário: sem conta e cadastro inativo ficam de fora, o 3º é o primeiro.");
            }
            // Volta 2 (#4): a prévia com o horário escolhido usa o mesmo critério da chamada e pula quem já tem consulta que cruze esse horário (P3 tem consulta às 08:00 e 08:30).
            async Task<string> NextAt(DateTimeOffset start, Guid? professional = null)
            {
                using var timed = await attendant.GetAsync($"/api/obs/units/{u1}/waiting-list/next?specialtyId={cardiology}&professionalId={professional ?? ana}&startsAt={Uri.EscapeDataString(start.ToString("O"))}");
                Expect(timed, 200); return (await Read(timed)).GetProperty("next").GetProperty("patientName").GetString()!;
            }
            Ensure(await NextAt(t1) == "Paciente 4" && await NextAt(t2) == "Paciente 4" && await NextAt(t3) == "Paciente 3", "A prévia com horário pula quem tem consulta que cruza (P3 às 08:00 e 08:30) e não pula no 09:00.");
            Ensure(await NextAt(t1.AddMinutes(45)) == "Paciente 3", "Horário fora da grade: a prévia volta ao critério sem horário (a chamada responderia slot-unavailable).");
            using (var onlyStart = await attendant.GetAsync($"/api/obs/units/{u1}/waiting-list/next?specialtyId={cardiology}&startsAt={Uri.EscapeDataString(t1.ToString("O"))}")) Expect(onlyStart, 400);
            using (var onlyProfessional = await attendant.GetAsync($"/api/obs/units/{u1}/waiting-list/next?specialtyId={cardiology}&professionalId={ana}")) Expect(onlyProfessional, 400);
            var created = await CallOk(attendant, t1); slot1 = created.GetProperty("slot").GetProperty("id").GetGuid();
            var slot = created.GetProperty("slot"); var offer = created.GetProperty("offer");
            Ensure(slot.GetProperty("status").GetString() == "offered" && slot.GetProperty("origin").GetString() == "call-next" && slot.GetProperty("appointmentId").ValueKind == JsonValueKind.Null && slot.GetProperty("professional").GetString() == "Dra. Ana Souza"
                && slot.GetProperty("specialty").GetString() == "Cardiologia" && slot.GetProperty("startsAt").GetDateTimeOffset() == t1 && slot.GetProperty("endsAt").GetDateTimeOffset() == t1.AddMinutes(30) && slot.GetProperty("source").ValueKind == JsonValueKind.Null
                && slot.GetProperty("approvalDeadline").ValueKind == JsonValueKind.Null && slot.GetProperty("closedReason").ValueKind == JsonValueKind.Null, "Corpo da vaga do chamar próximo.");
            Ensure(offer.GetProperty("patientName").GetString() == "Paciente 4" && offer.GetProperty("expiresAt").GetDateTimeOffset() == f.Clock.GetUtcNow().AddMinutes(10), "A oferta vai ao 4º, com 10 minutos.");
            Ensure(await f.UnitScalar<string>(u1, "SELECT status||':'||origin||':'||professional_id::text||':'||specialty_id::text||':'||room_id::text||':'||created_by::text FROM nora_unit.lightning_slots WHERE id=$1", slot1) == $"offered:call-next:{ana}:{cardiology}:{room1}:{f.AttendantId}", "Vaga com os ids próprios do atendimento.");
            Ensure(await f.UnitScalar<string>(u1, "SELECT q.status FROM nora_unit.waiting_list q WHERE q.cpf=$1", Cpf(4)) == "offered" && await f.UnitScalar<string>(u1, "SELECT e.source||':'||e.actor_id::text FROM nora_unit.waiting_list_events e JOIN nora_unit.waiting_list q ON q.id=e.entry_id WHERE q.cpf=$1 AND e.status='offered'", Cpf(4)) == $"staff:{f.AttendantId}", "Entrada oferecida e evento da fila com a atendente.");
            f.Clock.Advance(TimeSpan.FromSeconds(1)); // a distribuição segue a ordem de criação das vagas
            slot2 = (await CallOk(attendant, t2)).GetProperty("slot").GetProperty("id").GetGuid();
            Ensure(await Offers(slot2) == "Paciente 5:pending", "A segunda chamada pula quem já tem oferta pendente e vai ao 5º.");
            using (var list = await f.Admin.GetAsync($"/api/obs/units/{u1}/lightning-slots")) { Expect(list, 200); Ensure((await Read(list)).GetProperty("items").EnumerateArray().Count(item => item.GetProperty("origin").GetString() == "call-next") == 2, "A lista de vagas traz a origem."); }
        });

        await suite.Check("Recusar ou deixar vencer: o horário segue sozinho para os próximos da fila; aceitar cria a consulta com professional_id, specialty_id e room_id (booked_by lightning)", async () =>
        {
            var offer5 = await PatientOffer(5);
            f.Clock.Advance(TimeSpan.FromSeconds(1)); // a lista de ofertas ordena por vencimento: com o relógio parado as duas ofertas empatariam e a ordem dependeria do id sorteado
            using (var decline = await f.Patients[5].PostAsJsonAsync($"/api/patient/units/{u1}/lightning-offers/{offer5.GetProperty("id").GetGuid()}/respond", new { accept = false })) Expect(decline, 200);
            Ensure(await Offers(slot2) == "Paciente 5:declined,Paciente 6:pending", "Recusa: a vaga vai ao 6º sem nenhuma ação a mais.");
            // Vencimento (relógio + 10 min): as duas vagas voltam à distribuição automática.
            f.Clock.Advance(TimeSpan.FromMinutes(10) + TimeSpan.FromSeconds(1));
            await f.AdvanceAsync(u1);
            // R-030: quem recusou (5) ou deixou vencer (4, 6) volta à fila na mesma posição, mas não recebe de novo a MESMA vaga: T1 vai ao 5º (recusou só T2), T2 vai ao 4º (venceu só T1).
            Ensure(await Offers(slot1) == "Paciente 4:expired,Paciente 5:pending" && await Offers(slot2) == "Paciente 5:declined,Paciente 6:expired,Paciente 4:pending", $"Vencimento: a vaga segue para os próximos. T1={await Offers(slot1)} T2={await Offers(slot2)}");
            var offer7 = await PatientOffer(5);
            using (var accept = await f.Patients[5].PostAsJsonAsync($"/api/patient/units/{u1}/lightning-offers/{offer7.GetProperty("id").GetGuid()}/respond", new { accept = true }))
            {
                Expect(accept, 200); var body = await Read(accept); Ensure(body.GetProperty("accepted").GetBoolean() && body.GetProperty("appointmentId").ValueKind == JsonValueKind.String, "Aceite devolve a consulta.");
                var appointment = body.GetProperty("appointmentId").GetGuid();
                Ensure(await f.UnitScalar<string>(u1, "SELECT status||':'||booked_by||':'||professional_id::text||':'||specialty_id::text||':'||room_id::text||':'||professional||':'||specialty FROM nora_unit.appointments WHERE id=$1", appointment) == $"scheduled:lightning:{ana}:{cardiology}:{room1}:Dra. Ana Souza:Cardiologia", "Consulta criada com os ids da vaga.");
            }
            Ensure(await f.UnitScalar<string>(u1, "SELECT status FROM nora_unit.lightning_slots WHERE id=$1", slot1) == "accepted", "Vaga aceita.");
        });

        await suite.Check("Recusas 409: horário ocupado, bloqueado, segurado por vaga, fora da grade, profissional sem a especialidade ou inativo → slot-unavailable; fila vazia → queue-empty sem gravar nada", async () =>
        {
            using (var booked = await Call(attendant, t1)) await ExpectProblem(booked, 409, "slot-unavailable");
            using (var held = await Call(attendant, t2)) await ExpectProblem(held, 409, "slot-unavailable");
            using (var block = await f.Admin.PostAsJsonAsync($"/api/obs/units/{u1}/schedule-blocks", new { professionalId = ana, startsAt = t3, endsAt = t3.AddMinutes(30), reason = "Reunião" }))
            {
                Expect(block, 201); var blockId = (await Read(block)).GetProperty("id").GetGuid();
                using (var blocked = await Call(attendant, t3)) await ExpectProblem(blocked, 409, "slot-unavailable");
                using var remove = await f.Admin.DeleteAsync($"/api/obs/units/{u1}/schedule-blocks/{blockId}"); Expect(remove, 204);
            }
            using (var misaligned = await Call(attendant, t1.AddMinutes(45))) await ExpectProblem(misaligned, 409, "slot-unavailable");
            using (var wrongSpecialty = await Call(attendant, t2, professional: bruno)) await ExpectProblem(wrongSpecialty, 409, "slot-unavailable");
            using (var off = await f.Admin.PatchAsJsonAsync($"/api/obs/units/{u1}/professionals/{ana}/active", new { isActive = false })) Expect(off, 200);
            using (var inactive = await Call(attendant, t3)) await ExpectProblem(inactive, 409, "slot-unavailable");
            using (var on = await f.Admin.PatchAsJsonAsync($"/api/obs/units/{u1}/professionals/{ana}/active", new { isActive = true })) Expect(on, 200);
            // Ninguém na fila de Clínica geral: nada é gravado.
            var slots = await f.UnitScalar<long>(u1, "SELECT count(*) FROM nora_unit.lightning_slots"); var offers = await f.UnitScalar<long>(u1, "SELECT count(*) FROM nora_unit.lightning_offers");
            using (var empty = await Call(attendant, Wednesday("09:00"), clinical, bruno)) await ExpectProblem(empty, 409, "queue-empty");
            Ensure(await f.UnitScalar<long>(u1, "SELECT count(*) FROM nora_unit.lightning_slots") == slots && await f.UnitScalar<long>(u1, "SELECT count(*) FROM nora_unit.lightning_offers") == offers, "queue-empty não grava vaga nem oferta.");
            using (var nobody = await attendant.GetAsync($"/api/obs/units/{u1}/waiting-list/next?specialtyId={clinical}")) { Expect(nobody, 200); Ensure((await Read(nobody)).GetProperty("next").ValueKind == JsonValueKind.Null, "Prévia sem ninguém: next nulo."); }
        });

        await suite.Check("Validação e permissão: confirmed falso, campos faltando, especialidade ou profissional desconhecidos e especialidade inativa → 400; outra UBS, administrador sem concessão, Secretaria e paciente → 403", async () =>
        {
            using (var notConfirmed = await Call(attendant, t3, confirmed: false)) Expect(notConfirmed, 400);
            using (var missing = await attendant.PostAsJsonAsync($"/api/obs/units/{u1}/waiting-list/call-next", new { specialtyId = cardiology, confirmed = true })) Expect(missing, 400);
            using (var noConfirmedField = await attendant.PostAsJsonAsync($"/api/obs/units/{u1}/waiting-list/call-next", new { specialtyId = cardiology, professionalId = ana, startsAt = t3 })) Expect(noConfirmedField, 400);
            using (var unknownSpecialty = await Call(attendant, t3, specialty: Guid.NewGuid())) Expect(unknownSpecialty, 400);
            using (var unknownProfessional = await Call(attendant, t3, professional: Guid.NewGuid())) Expect(unknownProfessional, 400);
            using (var off = await f.Municipal.PatchAsJsonAsync($"/api/municipal/specialties/{pediatrics}/active", new { isActive = false })) Expect(off, 200);
            using (var inactiveSpecialty = await Call(attendant, t3, specialty: pediatrics)) Expect(inactiveSpecialty, 400);
            using (var previewInactive = await attendant.GetAsync($"/api/obs/units/{u1}/waiting-list/next?specialtyId={pediatrics}")) Expect(previewInactive, 400);
            using (var previewMissing = await attendant.GetAsync($"/api/obs/units/{u1}/waiting-list/next")) Expect(previewMissing, 400);
            using (var otherUnit = await Call(attendant, t3, unit: u2)) Expect(otherUnit, 403);
            var onlyU2 = await f.StaffAsync("administrator", 40, [u2]);
            using (var noGrant = await Call(onlyU2.Client, t3)) Expect(noGrant, 403);
            using (var noGrantPreview = await onlyU2.Client.GetAsync($"/api/obs/units/{u1}/waiting-list/next?specialtyId={cardiology}")) Expect(noGrantPreview, 403);
            using (var manager = await Call(f.Municipal, t3)) Expect(manager, 403);
            using (var patient = await Call(f.Patients[9], t3)) Expect(patient, 403);
            using (var anonymous = await Call(f.Anonymous, t3)) Expect(anonymous, 401);
        });

        await suite.Check("Concorrência: dois chamar próximo no mesmo horário → um 201 e um 409; em horários diferentes → duas pessoas diferentes; a vaga aparece nos indicadores, no resumo e fora da disponibilidade do paciente", async () =>
        {
            var same = await Task.WhenAll(Enumerable.Range(0, 2).Select(async _ => { using var response = await Call(f.Admin, t3); return (int)response.StatusCode; }));
            Ensure(same.Count(status => status == 201) == 1 && same.Count(status => status == 409) == 1, $"Dois cliques no mesmo horário: {string.Join(",", same)}.");
            var different = await Task.WhenAll(new[] { thursday1, thursday2 }.Select(async start => { using var response = await Call(f.Admin, start); Expect(response, 201); return (await Read(response)).GetProperty("offer").GetProperty("patientName").GetString(); }));
            Ensure(different.Distinct().Count() == 2, $"Dois horários diferentes ofereceram à mesma pessoa: {string.Join(",", different)}.");
            Ensure(await f.UnitScalar<long>(u1, "SELECT count(*) FROM (SELECT entry_id FROM nora_unit.lightning_offers WHERE status='pending' GROUP BY entry_id HAVING count(*)>1) x") == 0, "Ninguém tem duas ofertas pendentes.");
            // Disponibilidade do paciente: o horário segurado (oferta pendente) sai da lista.
            using (var availability = await f.Patients[12].GetAsync($"/api/patient/units/{u1}/availability?from=2026-11-19T00:00:00Z&to=2026-11-20T00:00:00Z&specialtyId={cardiology}"))
            {
                Expect(availability, 200); var starts = (await Read(availability)).GetProperty("slots").EnumerateArray().Select(slot => slot.GetProperty("startsAt").GetDateTimeOffset()).ToList();
                Ensure(!starts.Contains(thursday1) && !starts.Contains(thursday2), "O horário com oferta pendente não aparece para o paciente.");
            }
            // Indicadores e resumo da rede: a vaga do chamar próximo conta (não tem consulta de origem).
            var released = await f.UnitScalar<long>(u1, "SELECT count(*) FROM nora_unit.lightning_slots");
            using (var metrics = await f.Admin.GetAsync($"/api/obs/units/{u1}/metrics?from=2026-11-01T00:00:00Z&to=2026-12-31T00:00:00Z"))
            { Expect(metrics, 200); Ensure((await Read(metrics)).GetProperty("lightning").GetProperty("registeredSlots").GetInt64() == released, "Os indicadores da UBS contam a vaga sem consulta de origem."); }
            using (var summary = await f.Municipal.GetAsync($"/api/municipal/summary?unitId={u1}"))
            {
                Expect(summary, 200); var body = await Read(summary);
                Ensure(body.GetProperty("registeredSlots").GetInt64() == released && body.GetProperty("openSlots").GetInt64() == await f.UnitScalar<long>(u1, "SELECT count(*) FROM nora_unit.lightning_slots WHERE status IN ('open','offered')"), "O resumo da rede conta a vaga do chamar próximo.");
            }
        });

        await suite.Check("Antecedência de 10 minutos: exatamente 10 → oferece; 9 minutos → 409 call-next-too-late; a vaga de hoje fica registrada", async () =>
        {
            f.Clock.Set(RoundClock.T0);
            using (var exact = await Call(attendant, monday)) Expect(exact, 201);
            f.Clock.Set(RoundClock.T0.AddMinutes(1));
            using (var late = await Call(attendant, monday)) await ExpectProblem(late, 409, "call-next-too-late"); // 09:10 local com o relógio em 09:01: faltam 9 minutos
            f.Clock.Set(RoundClock.T0);
            Ensure(await f.UnitScalar<long>(u1, "SELECT count(*) FROM nora_unit.lightning_slots WHERE origin='call-next' AND starts_at=$1", monday) == 1, "A chamada em cima do limite gravou a vaga e a recusada não.");
        });

        await suite.Check("Resposta a uma oferta depois do início do horário: 409 e a vaga fecha com closed_reason too-late, o mesmo motivo do dispatcher", async () =>
        {
            // A vaga de hoje (09:10 local) criada no teste anterior tem uma oferta pendente. Com o relógio passando do início, o paciente responde antes do worker.
            var slot = await f.UnitScalar<Guid>(u1, "SELECT id FROM nora_unit.lightning_slots WHERE origin='call-next' AND starts_at=$1", monday);
            var offerId = await f.UnitScalar<Guid>(u1, "SELECT id FROM nora_unit.lightning_offers WHERE slot_id=$1 AND status='pending'", slot);
            var cpf = await f.UnitScalar<string>(u1, "SELECT q.cpf FROM nora_unit.lightning_offers o JOIN nora_unit.waiting_list q ON q.id=o.entry_id WHERE o.id=$1", offerId);
            var patient = f.Patients.Single(pair => Cpf(pair.Key) == cpf).Value;
            f.Clock.Set(monday.AddSeconds(5));
            using (var late = await patient.PostAsJsonAsync($"/api/patient/units/{u1}/lightning-offers/{offerId}/respond", new { accept = true })) Expect(late, 409);
            Ensure(await f.UnitScalar<string>(u1, "SELECT status||':'||coalesce(closed_reason,'-') FROM nora_unit.lightning_slots WHERE id=$1", slot) == "closed:too-late", "A vaga fechou com o motivo too-late.");
            Ensure(await f.UnitScalar<string>(u1, "SELECT status FROM nora_unit.lightning_offers WHERE id=$1", offerId) == "expired", "A oferta ficou vencida.");
            f.Clock.Set(RoundClock.T0);
        });

        await suite.Check("Vaga antiga migrada para o schema 015 mantém horário, especialidade e profissional (banco de UBS na versão 014 com vaga aberta e oferta pendente)", async () =>
        {
            var unitDao = f.Service<HealthUnitDao>(); var router = f.Router;
            var legacy = (await unitDao.CreateAsync(new(Guid.NewGuid(), "UBS Legada", "Centro", "Rua legada, 1", -23.5, -46.6), default)).Id;
            await using (var admin = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(connection) { Database = "postgres", Pooling = false }.ConnectionString))
            {
                await admin.OpenAsync();
                await using var create = new NpgsqlCommand($"CREATE DATABASE \"{router.DerivedDatabaseName(legacy)}\"", admin); await create.ExecuteNonQueryAsync();
            }
            await using (var raw = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(connection) { Database = router.DerivedDatabaseName(legacy), Pooling = false }.ConnectionString))
            {
                await raw.OpenAsync();
                foreach (var name in UnitSchema.ResourceNames.Where(name => int.Parse(name[(name.IndexOf(".UnitMigrations.", StringComparison.Ordinal) + ".UnitMigrations.".Length)..][..3]) <= 14))
                {
                    await using var stream = typeof(UnitSchema).Assembly.GetManifestResourceStream(name)!; using var reader = new StreamReader(stream);
                    await using var command = new NpgsqlCommand(await reader.ReadToEndAsync(), raw); await command.ExecuteNonQueryAsync();
                }
                var appointment = Guid.NewGuid(); var slot = Guid.NewGuid(); var start = Wednesday("10:00");
                async Task Seed(string sql, params object[] values)
                {
                    await using var command = new NpgsqlCommand(sql, raw);
                    foreach (var value in values) command.Parameters.AddWithValue(value);
                    await command.ExecuteNonQueryAsync();
                }
                await Seed("INSERT INTO nora_unit.identity(singleton,unit_id) VALUES(true,$1)", legacy);
                await Seed("INSERT INTO nora_unit.schema_version(singleton,version) VALUES(true,14)");
                await Seed("INSERT INTO nora_unit.patient_registry(cpf,full_name,email,created_by) VALUES($1,'Paciente Antigo','antigo@example.test',$2)", Cpf(60), f.AdminId);
                await Seed("INSERT INTO nora_unit.appointments(id,cpf,specialty,professional,starts_at,ends_at,status,created_by,created_at) VALUES($1,$2,'Ortopedia','Dr. Antigo',$3,$4,'cancelled',$5,$6)", appointment, Cpf(60), start, start.AddMinutes(30), f.AdminId, RoundClock.T0);
                await Seed("INSERT INTO nora_unit.lightning_slots(id,appointment_id,status,created_at,created_by) VALUES($1,$2,'open',$3,$4)", slot, appointment, RoundClock.T0, f.AdminId);
                await f.Service<UnitMigrationDao>().MigrateAsync(legacy);
                await f.Service<UnitMigrationDao>().MigrateAsync(legacy); // reaplicável
                await using var read = new NpgsqlCommand("SELECT specialty||'|'||professional||'|'||starts_at::text||'|'||ends_at::text||'|'||origin||'|'||status||'|'||(appointment_id IS NOT NULL)::text||'|'||(created_by IS NOT NULL)::text FROM nora_unit.lightning_slots WHERE id=$1", raw);
                read.Parameters.AddWithValue(slot);
                var row = (string)(await read.ExecuteScalarAsync())!;
                Ensure(row.StartsWith("Ortopedia|Dr. Antigo|", StringComparison.Ordinal) && row.EndsWith("|staff|open|true|true", StringComparison.Ordinal), $"A vaga antiga manteve os dados: {row}.");
                Ensure(Convert.ToInt32(await new NpgsqlCommand("SELECT version FROM nora_unit.schema_version", raw).ExecuteScalarAsync()) == UnitSchema.ExpectedVersion && UnitSchema.ExpectedVersion >= 16, "Versão do schema da UBS atualizada.");
            }
            Ensure(await f.Scalar<long>("SELECT count(*) FROM nora.unit_registry_index WHERE unit_id=$1 AND cpf=$2", legacy, Cpf(60)) == 1, "A migração copiou o cadastro antigo para o índice central.");
        });
        return suite.Report() == 0 ? 0 : 1;
    }
}
