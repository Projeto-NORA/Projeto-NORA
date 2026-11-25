using System.Net.Http.Json;
using System.Text.Json;
using static RoundFixture;

/// <summary>
/// Vaga Relâmpago automática (2026-11-24): a consulta cancelada vira vaga sozinha, os candidatos são quem tem consulta marcada na mesma especialidade com início posterior, a ordem é score e depois a consulta mais
/// distante, o aceite antecipa (cancela a consulta antiga sem pesar no score) e o horário antigo vira vaga nova (cascata), a recusa e o vencimento passam ao próximo, a cascata para perto do horário e o caminho manual
/// responde 410. Roda com <c>--lightning-only</c>.
/// </summary>
internal static class LightningAutoChecks
{
    public static async Task<int> RunAsync(string root)
    {
        var connection = Environment.GetEnvironmentVariable("NORA_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection))
        {
            Console.WriteLine("SKIP vaga relâmpago automática: configure NORA_TEST_CONNECTION somente para uma base descartável.");
            return 0;
        }
        var suite = new Suite("vaga relâmpago automática");
        await using var f = await StartAsync(connection, "lightauto");
        var u1 = f.Unit1;
        var cardiology = await f.SpecialtyAsync("Cardiologia"); var clinical = await f.SpecialtyAsync("Clínica geral");
        var ana = await f.ProfessionalAsync(u1, "Dra. Ana Souza", cardiology); var caio = await f.ProfessionalAsync(u1, "Dr. Caio Prado", cardiology); var bruno = await f.ProfessionalAsync(u1, "Dr. Bruno Lima", clinical);
        var room1 = await f.RoomAsync(u1, "Sala 1"); var room2 = await f.RoomAsync(u1, "Sala 2"); var room3 = await f.RoomAsync(u1, "Sala 3");
        await f.TemplateAsync(u1, ana, 3, "08:00", "10:00", 30, room1, cardiology);   // quarta
        await f.TemplateAsync(u1, ana, 1, "09:10", "10:10", 30, room1, cardiology);   // segunda (hoje), 10 minutos depois do relógio de teste
        await f.TemplateAsync(u1, caio, 4, "08:00", "10:00", 30, room2, cardiology);  // quinta
        await f.TemplateAsync(u1, bruno, 4, "08:00", "10:00", 30, room3, clinical);
        var wed8 = Wednesday("08:00"); var wed9 = Wednesday("09:00"); var wed930 = Wednesday("09:30");
        var thu8 = wed8.AddDays(1); var thu9 = wed9.AddDays(1);
        // P1 só tem cadastro (sem conta); P2 a P8 têm conta vinculada.
        await f.RegisterAsync(1, u1);
        for (var number = 2; number <= 8; number++) await f.PatientAsync(number, u1);
        async Task<Guid> Book(int number, Guid professional, Guid specialty, DateTimeOffset start)
        {
            using var response = await f.Admin.PostAsJsonAsync($"/api/obs/units/{u1}/appointments", new { cpf = Cpf(number), professionalId = professional, specialtyId = specialty, startsAt = start });
            Expect(response, 201); return (await Read(response)).GetProperty("id").GetGuid();
        }
        async Task Cancel(Guid appointment)
        {
            using var response = await f.Admin.PatchAsJsonAsync($"/api/obs/units/{u1}/appointments/{appointment}/status", new { status = "cancelled", confirmed = true });
            Expect(response, 200);
        }
        var p2 = await Book(2, ana, cardiology, wed8);    // será cancelada: vira a vaga
        var p3 = await Book(3, ana, cardiology, wed9);    // candidata (mesma especialidade, mais tarde)
        var p4 = await Book(4, caio, cardiology, thu8);   // candidata com outro profissional, mais distante
        var p6 = await Book(6, caio, cardiology, thu9);   // a mais distante, mas com score menor (falta no passado)
        await Book(5, bruno, clinical, thu8);             // outra especialidade: nunca é candidata
        await Book(1, ana, cardiology, wed930);           // sem conta: não recebe
        await f.UnitExec(u1, "INSERT INTO nora_unit.appointments(id,cpf,specialty,professional,starts_at,ends_at,status,created_by,created_at) VALUES(gen_random_uuid(),$1,'Cardiologia','Dr. Caio Prado',$2,$3,'missed',$4,$2)",
            Cpf(6), RoundClock.T0.AddDays(-7), RoundClock.T0.AddDays(-7).AddMinutes(30), f.AdminId);
        async Task<string> Offers(Guid slot)
        {
            using var response = await f.Admin.GetAsync($"/api/obs/units/{u1}/lightning-slots/{slot}/offers"); Expect(response, 200);
            return string.Join(",", (await Read(response)).EnumerateArray().Select(offer => offer.GetProperty("patientName").GetString() + ":" + offer.GetProperty("status").GetString()));
        }
        async Task<JsonElement> PendingOffer(int number)
        {
            using var response = await f.Patients[number].GetAsync($"/api/patient/units/{u1}/lightning-offers"); Expect(response, 200);
            return (await Read(response)).GetProperty("offers").EnumerateArray().Single(offer => offer.GetProperty("status").GetString() == "pending");
        }
        async Task<JsonElement> Respond(int number, bool accept)
        {
            var offer = await PendingOffer(number);
            f.Clock.Advance(TimeSpan.FromSeconds(1)); // a próxima oferta vence depois desta: a lista ordena por vencimento
            using var response = await f.Patients[number].PostAsJsonAsync($"/api/patient/units/{u1}/lightning-offers/{offer.GetProperty("id").GetGuid()}/respond", new { accept });
            Expect(response, 200); f.Clock.Advance(TimeSpan.FromSeconds(1)); return await Read(response);
        }
        Task<Guid> SlotOf(Guid appointment) => f.UnitScalar<Guid>(u1, "SELECT id FROM nora_unit.lightning_slots WHERE appointment_id=$1", appointment);

        Guid slotA = Guid.Empty;
        await suite.Check("Consulta cancelada vira vaga sozinha e a oferta vai a quem tem consulta posterior na especialidade: empate de score → a consulta mais distante; guarda a consulta antecipada", async () =>
        {
            await Cancel(p2); await f.AdvanceAsync(u1);
            slotA = await SlotOf(p2);
            Ensure(await f.UnitScalar<string>(u1, "SELECT status||':'||origin FROM nora_unit.lightning_slots WHERE id=$1", slotA) == "offered:cancellation", "A vaga nasce sozinha (origin cancellation) e já é ofertada.");
            // P4 (quinta 08:00, 700 pontos) e P3 (quarta 09:00, 700 pontos) empatam no score: vai a P4, que ganha mais ao antecipar. P6 é a mais distante, mas tem falta (score menor). P1 sem conta e P5 de outra especialidade ficam de fora.
            Ensure(await Offers(slotA) == "Paciente 4:pending", $"Primeira oferta: {await Offers(slotA)}");
            var offer = await PendingOffer(4);
            Ensure(offer.GetProperty("replacesAppointmentId").GetGuid() == p4 && offer.GetProperty("replacesStartsAt").GetDateTimeOffset() == thu8 && offer.GetProperty("startsAt").GetDateTimeOffset() == wed8, "A oferta diz qual consulta será antecipada.");
            Ensure(await f.UnitScalar<Guid>(u1, "SELECT appointment_id FROM nora_unit.lightning_offers WHERE slot_id=$1", slotA) == p4, "A coluna nova guarda a consulta antecipada.");
        });

        await suite.Check("Recusa passa ao próximo: score maior vence a distância (P3 antes de P6) e quem recusou não recebe a mesma vaga de novo", async () =>
        {
            await Respond(4, false);
            Ensure(await Offers(slotA) == "Paciente 4:declined,Paciente 3:pending", $"Depois da recusa: {await Offers(slotA)}");
            Ensure(await f.UnitScalar<string>(u1, "SELECT status FROM nora_unit.appointments WHERE id=$1", p4) == "scheduled", "Recusar não mexe na consulta marcada.");
        });

        Guid newP3 = Guid.Empty;
        await suite.Check("Aceite antecipa: a consulta antiga é cancelada (rescheduled-lightning), a nova nasce no horário da vaga e o horário antigo vira vaga (cascata) oferecida ao próximo", async () =>
        {
            var accepted = await Respond(3, true);
            newP3 = accepted.GetProperty("appointmentId").GetGuid();
            Ensure(await f.UnitScalar<string>(u1, "SELECT status||':'||booked_by||':'||starts_at::text FROM nora_unit.appointments WHERE id=$1", newP3) == "scheduled:lightning:" + await f.UnitScalar<string>(u1, "SELECT $1::timestamptz::text", wed8), "Consulta nova no horário da vaga.");
            Ensure(await f.UnitScalar<string>(u1, "SELECT a.status||':'||e.source FROM nora_unit.appointments a JOIN nora_unit.appointment_events e ON e.appointment_id=a.id AND e.status='cancelled' WHERE a.id=$1", p3) == "cancelled:rescheduled-lightning", "Consulta antiga cancelada pela antecipação.");
            Ensure(await f.UnitScalar<string>(u1, "SELECT status FROM nora_unit.lightning_slots WHERE id=$1", slotA) == "accepted", "Vaga aceita.");
            var slotB = await SlotOf(p3); // o endpoint da resposta já chamou o passe
            Ensure(await f.UnitScalar<string>(u1, "SELECT status||':'||origin FROM nora_unit.lightning_slots WHERE id=$1", slotB) == "offered:rescheduled", "O horário antigo (quarta 09:00) virou vaga com origem rescheduled.");
            // Candidatos posteriores a quarta 09:00: P4 (quinta 08:00) e P6 (quinta 09:00, score menor). P4 recusou a vaga A, não esta.
            Ensure(await Offers(slotB) == "Paciente 4:pending", $"Cascata: {await Offers(slotB)}");
            await Respond(4, true);
            var slotC = await SlotOf(p4);
            Ensure(await Offers(slotC) == "Paciente 6:pending", $"Segunda volta da cascata (quinta 08:00 → P6): {await Offers(slotC)}");
        });

        await suite.Check("Vencimento passa ao próximo e, sem ninguém posterior, a vaga fica aberta; o último da cascata não volta a receber", async () =>
        {
            var slotC = await SlotOf(p4);
            f.Clock.Advance(TimeSpan.FromMinutes(10) + TimeSpan.FromSeconds(1)); await f.AdvanceAsync(u1);
            Ensure(await Offers(slotC) == "Paciente 6:expired" && await f.UnitScalar<string>(u1, "SELECT status FROM nora_unit.lightning_slots WHERE id=$1", slotC) == "open", $"Vencida e sem próximo: {await Offers(slotC)}");
            Ensure(await f.UnitScalar<string>(u1, "SELECT status FROM nora_unit.appointments WHERE id=$1", p6) == "scheduled", "Deixar vencer não mexe na consulta.");
        });

        await suite.Check("Cancelamento por antecipação não pesa no score: P3 continua sem cancelamento e com os mesmos pontos de quem não tem histórico", async () =>
        {
            async Task<JsonElement> Attendance(int number)
            {
                using var response = await f.Patients[number].GetAsync($"/api/patient/units/{u1}/attendance"); Expect(response, 200); return await Read(response);
            }
            var p3Score = await Attendance(3); var fresh = await Attendance(8);
            Ensure(p3Score.GetProperty("cancelledByPatient").GetInt64() == 0 && p3Score.GetProperty("points").GetInt32() == fresh.GetProperty("points").GetInt32(), $"Score de P3: {p3Score}");
        });

        await suite.Check("A cascata para perto do horário: consulta cancelada a menos de 20 minutos (10 de oferta + 10 de antecedência) não vira vaga", async () =>
        {
            // Consulta já cancelada que começa daqui a 15 minutos (gravada direto: a agenda não marca no passado do relógio de teste).
            var near = Guid.NewGuid(); var start = f.Clock.GetUtcNow().AddMinutes(15);
            await f.UnitExec(u1, "INSERT INTO nora_unit.appointments(id,cpf,specialty,professional,starts_at,ends_at,status,created_by,created_at,professional_id) VALUES($1,$2,'Cardiologia','Dra. Ana Souza',$3,$4,'cancelled',$5,$6,$7)",
                near, Cpf(7), start, start.AddMinutes(30), f.AdminId, f.Clock.GetUtcNow(), ana);
            await f.AdvanceAsync(u1);
            Ensure(await f.UnitScalar<long>(u1, "SELECT count(*) FROM nora_unit.lightning_slots WHERE appointment_id=$1", near) == 0, "Horário a 10 minutos não deve gerar vaga.");
        });

        await suite.Check("Caminho manual removido: fila do OBS, chamar próximo, aprovação e sair da fila → 410 manual-queue-removed; a leitura da fila do app devolve vazio", async () =>
        {
            using (var r = await f.Admin.GetAsync($"/api/obs/units/{u1}/waiting-list")) await ExpectProblem(r, 410, "manual-queue-removed");
            using (var r = await f.Admin.PostAsJsonAsync($"/api/obs/units/{u1}/waiting-list", new { cpf = Cpf(2), specialty = "Cardiologia" })) await ExpectProblem(r, 410, "manual-queue-removed");
            using (var r = await f.Admin.GetAsync($"/api/obs/units/{u1}/waiting-list/next?specialtyId={cardiology}")) await ExpectProblem(r, 410, "manual-queue-removed");
            using (var r = await f.Admin.PostAsJsonAsync($"/api/obs/units/{u1}/waiting-list/call-next", new { confirmed = true })) await ExpectProblem(r, 410, "manual-queue-removed");
            using (var r = await f.Admin.PutAsJsonAsync($"/api/obs/units/{u1}/slot-approval", new { enabled = true, confirmed = true })) await ExpectProblem(r, 410, "manual-queue-removed");
            using (var r = await f.Admin.PostAsJsonAsync($"/api/obs/units/{u1}/lightning-slots/{slotA}/approve", new { confirmed = true })) await ExpectProblem(r, 410, "manual-queue-removed");
            using (var r = await f.Patients[2].PostAsJsonAsync($"/api/patient/units/{u1}/waiting-list/1/cancel", new { confirmed = true })) await ExpectProblem(r, 410, "manual-queue-removed");
            using (var r = await f.Patients[2].GetAsync($"/api/patient/units/{u1}/waiting-list")) { Expect(r, 200); Ensure((await Read(r)).GetProperty("entries").GetArrayLength() == 0, "Fila do app sempre vazia."); }
            using (var r = await f.Anonymous.GetAsync($"/api/obs/units/{u1}/waiting-list")) Expect(r, 401);
            using (var list = await f.Admin.GetAsync($"/api/obs/units/{u1}/lightning-slots")) { Expect(list, 200); Ensure((await Read(list)).GetProperty("items").GetArrayLength() >= 3, "A leitura das vagas continua."); }
        });
        await suite.Check("/api/auth/me e o login trazem cpfMasked (***.456.789-**, só paciente) e email da conta; equipe e Secretaria com cpfMasked nulo", async () =>
        {
            var cpf = Cpf(8); var masked = $"***.{cpf[3..6]}.{cpf[6..9]}-**";
            var login = await f.LoginAsync(f.Patients[8], "patient", cpf);
            Ensure(login.GetProperty("user").GetProperty("cpfMasked").GetString() == masked && login.GetProperty("user").GetProperty("email").GetString() == "paciente8@example.test", $"Login do paciente: {login.GetProperty("user")}.");
            using (var me = await f.Patients[8].GetAsync("/api/auth/me"))
            {
                Expect(me, 200); var body = await Read(me);
                Ensure(body.GetProperty("cpfMasked").GetString() == masked && body.GetProperty("email").GetString() == "paciente8@example.test" && !body.ToString().Contains(cpf), $"/me do paciente: {body}.");
            }
            foreach (var staff in new[] { f.Admin, f.Municipal })
            {
                using var me = await staff.GetAsync("/api/auth/me"); Expect(me, 200); var body = await Read(me);
                Ensure(body.GetProperty("cpfMasked").ValueKind == JsonValueKind.Null && body.GetProperty("email").GetString()!.Contains('@'), $"/me da equipe: {body}.");
            }
        });
        return suite.Report();
    }
}
