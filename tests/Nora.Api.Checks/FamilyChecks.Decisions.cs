using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Nora.Api.Services;

/// <summary>
/// Decisões do usuário de 2026-11-16: o fim do vínculo de Família cancela as consultas marcadas pelos responsáveis quando o dependente fica sem ninguém que responda por ele (P-041),
/// a API diz quem marcou cada consulta do paciente (C1) e a prévia da remoção da UBS pelo paciente conta exatamente o que a remoção cancela (C2). Mesmo fixture e mesmo relógio de
/// <see cref="FamilyChecks"/>, num schema próprio.
/// </summary>
internal static partial class FamilyChecks
{
    public static async Task<int> RunDecisionsAsync(string root)
    {
        var connectionString = Environment.GetEnvironmentVariable("NORA_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Console.WriteLine("SKIP decisões da família PostgreSQL: configure NORA_TEST_CONNECTION somente para uma base descartável.");
            return 0;
        }

        checks = 0; failures = 0;
        await using var f = await Fixture.StartAsync(connectionString);
        var u1 = f.Unit1; var u2 = f.Unit2;
        Guid cardiology = Guid.Empty, clinical = Guid.Empty, ana = Guid.Empty;
        const string window = "from=2026-11-16T00:00:00Z&to=2026-12-31T00:00:00Z";
        string Base(Guid link, Guid unit) => $"/api/patient/dependents/{link}/units/{unit}";
        Task<HttpResponseMessage> Book(HttpClient client, Guid link, Guid unit, Guid professional, Guid specialty, string startsAt) =>
            client.PostAsJsonAsync($"{Base(link, unit)}/appointments", new { professionalId = professional, specialtyId = specialty, startsAt });
        async Task<Guid> BookedId(HttpResponseMessage response) { using (response) { Expect(response, 201); return (await Read(response)).GetProperty("id").GetGuid(); } }
        async Task<Guid> StaffBook(int cpf, Guid specialty, string startsAt) =>
            await BookedId(await f.Attendant.PostAsJsonAsync($"/api/obs/units/{u1}/appointments", new { cpf = Cpf(cpf), professionalId = ana, specialtyId = specialty, startsAt }));
        async Task<Guid> Linked(HttpClient guardian, int dependent, Guid unit)
        {
            Guid id;
            using (var response = await guardian.PostAsJsonAsync("/api/patient/dependents", new { cpf = Cpf(dependent), unitId = unit })) { Expect(response, 201); id = (await Read(response)).GetProperty("id").GetGuid(); }
            using (var approve = await f.Attendant.PostAsJsonAsync($"/api/obs/units/{unit}/family-links/{id}/approve", new { confirmed = true })) Expect(approve, 200);
            return id;
        }
        async Task Register(int cpf, string name) { using var register = await f.Admin.PostAsJsonAsync($"/api/obs/units/{u1}/registry", new { cpf = Cpf(cpf), fullName = name, email = $"sem-conta{cpf}@example.test" }); Expect(register, 201); }
        async Task<JsonElement> Appointment(HttpClient client, string path, Guid id)
        {
            using var response = await client.GetAsync($"{path}/appointments?{window}"); Expect(response, 200);
            return (await Read(response)).GetProperty("appointments").EnumerateArray().Single(item => item.GetProperty("id").GetGuid() == id);
        }
        void BookedBy(JsonElement appointment, bool byYou, string? guardianName, string label) =>
            Require(appointment.GetProperty("bookedByYou").GetBoolean() == byYou && (guardianName is null ? appointment.GetProperty("bookedByGuardianName").ValueKind == JsonValueKind.Null : appointment.GetProperty("bookedByGuardianName").GetString() == guardianName),
                $"{label}: esperado bookedByYou={byYou} e bookedByGuardianName={guardianName ?? "null"}; recebido {appointment}.");
        async Task<string> Status(Guid appointment) => await f.UnitScalar<string>(u1, "SELECT status FROM nora_unit.appointments WHERE id=$1", appointment);
        async Task<List<string>> FreeCardiology(string day) =>
            (await Read(await f.Attendant.GetAsync($"/api/obs/units/{u1}/availability?from={day}T00:00:00Z&to={day}T23:59:59Z&specialtyId={cardiology}"))).GetProperty("slots").EnumerateArray()
                .Select(s => s.GetProperty("startsAt").GetDateTimeOffset().ToString("HH:mm", CultureInfo.InvariantCulture)).ToList();
        async Task<JsonElement> Attendance(int cpf)
        {
            using var registry = await f.Attendant.GetAsync($"/api/obs/units/{u1}/registry"); Expect(registry, 200);
            return (await Read(registry)).EnumerateArray().Single(p => p.GetProperty("cpf").GetString() == Cpf(cpf)).GetProperty("attendance");
        }

        await Check("Preparação: especialidades, profissional, consultório e grade da UBS 1", async () =>
        {
            using (var c = await f.Municipal.PostAsJsonAsync("/api/municipal/specialties", new { name = "Cardiologia" })) { Expect(c, 201); cardiology = (await Read(c)).GetProperty("id").GetGuid(); }
            using (var c = await f.Municipal.PostAsJsonAsync("/api/municipal/specialties", new { name = "Clínica Geral" })) { Expect(c, 201); clinical = (await Read(c)).GetProperty("id").GetGuid(); }
            using (var a = await f.Admin.PostAsJsonAsync($"/api/obs/units/{u1}/professionals", new { fullName = "Dra. Ana Souza", specialtyIds = new[] { cardiology, clinical } })) { Expect(a, 201); ana = (await Read(a)).GetProperty("id").GetGuid(); }
            Guid room;
            using (var r = await f.Admin.PostAsJsonAsync($"/api/obs/units/{u1}/rooms", new { name = "Sala 1" })) { Expect(r, 201); room = (await Read(r)).GetProperty("id").GetGuid(); }
            // Quarta: cardiologia 09:00-12:00 local (12:00Z a 14:30Z, seis atendimentos); quinta: clínica 09:00-11:00 local (12:00Z a 13:30Z, quatro atendimentos). A grade vale para todas as semanas.
            using (var s = await f.Admin.PostAsJsonAsync($"/api/obs/units/{u1}/schedule", new { professionalId = ana, weekday = 3, startTime = "09:00", endTime = "12:00", slotMinutes = 30, roomId = room, specialtyId = cardiology })) Expect(s, 201);
            using (var s = await f.Admin.PostAsJsonAsync($"/api/obs/units/{u1}/schedule", new { professionalId = ana, weekday = 4, startTime = "09:00", endTime = "11:00", slotMinutes = 30, roomId = room, specialtyId = clinical })) Expect(s, 201);
            foreach (var (cpf, name) in new[] { (72, "Dependente Setenta e Dois"), (73, "Dependente Setenta e Três"), (74, "Dependente Setenta e Quatro"), (75, "Dependente Setenta e Cinco") }) await Register(cpf, name);
        });

        // ======== C1: quem marcou a consulta ========
        Guid ownBooking = Guid.Empty, guardianBooking = Guid.Empty, staffBooking = Guid.Empty, d2Link = Guid.Empty, g2Link = Guid.Empty;
        await Check("Quem marcou (C1): paciente para si (true/null), responsável pelo dependente (responsável: true/nome dele; dependente com conta e outro responsável: false/nome do responsável), equipe (false/null), também na resposta de agendar", async () =>
        {
            using (var own = await f.G1.PostAsJsonAsync($"/api/patient/units/{u1}/appointments", new { professionalId = ana, specialtyId = cardiology, startsAt = "2026-11-18T12:00:00Z" }))
            { Expect(own, 201); var body = await Read(own); ownBooking = body.GetProperty("id").GetGuid(); BookedBy(body, true, null, "Resposta da marcação do próprio paciente"); }
            BookedBy(await Appointment(f.G1, $"/api/patient/units/{u1}", ownBooking), true, null, "Lista do próprio paciente");

            d2Link = await Linked(f.G1, 12, u1);
            g2Link = await Linked(f.G2, 12, u1);
            using (var delegated = await Book(f.G1, d2Link, u1, ana, clinical, "2026-11-19T12:00:00Z"))
            { Expect(delegated, 201); var body = await Read(delegated); guardianBooking = body.GetProperty("id").GetGuid(); BookedBy(body, true, "Responsável Um", "Resposta da marcação pelo responsável"); }
            BookedBy(await Appointment(f.G1, Base(d2Link, u1), guardianBooking), true, "Responsável Um", "Lista delegada de quem marcou");
            BookedBy(await Appointment(f.D2, $"/api/patient/units/{u1}", guardianBooking), false, "Responsável Um", "Lista da conta própria do dependente");
            BookedBy(await Appointment(f.G2, Base(g2Link, u1), guardianBooking), false, "Responsável Um", "Lista delegada de outro responsável");

            staffBooking = await StaffBook(12, cardiology, "2026-11-18T12:30:00Z");
            BookedBy(await Appointment(f.D2, $"/api/patient/units/{u1}", staffBooking), false, null, "Consulta marcada pela equipe, vista pelo paciente");
            BookedBy(await Appointment(f.G1, Base(d2Link, u1), staffBooking), false, null, "Consulta marcada pela equipe, vista pelo responsável");
        });

        // ======== P-041: fim do vínculo e consultas ========
        await Check("Fim do vínculo pelo responsável, dependente sem mais ninguém: as consultas futuras marcadas por responsável são canceladas (origem system, autor e guardian_id = responsável, sem peso no score) e o horário volta; consulta da equipe e passada ficam", async () =>
        {
            var link = await Linked(f.G3, 72, u1);
            var guardianFuture = await BookedId(await Book(f.G3, link, u1, ana, cardiology, "2026-11-25T12:00:00Z"));
            var staffFuture = await StaffBook(72, clinical, "2026-11-26T12:00:00Z");
            var past = Guid.NewGuid();
            await f.UnitExec(u1, "INSERT INTO nora_unit.appointments(id,cpf,specialty,professional,starts_at,ends_at,status,created_by,created_at,booked_by) VALUES($1,$2,'Cardiologia','Dra. Ana Souza',$3,$4,'scheduled',$5,$3,'patient')", past, Cpf(72), T0.AddDays(-1), T0.AddDays(-1).AddMinutes(30), f.PatientIds[3]);
            await f.UnitExec(u1, "INSERT INTO nora_unit.appointment_events(appointment_id,actor_id,status,occurred_at,source,guardian_id) VALUES($1,$2,'scheduled',$3,'patient',$2)", past, f.PatientIds[3], T0.AddDays(-2));
            Require(!(await FreeCardiology("2026-11-25")).Contains("12:00"), "Pré-condição: o horário está ocupado.");
            using (var end = await f.G3.PostAsJsonAsync($"/api/patient/dependents/{link}/end", new { confirmed = true })) Expect(end, 200);
            Require(await Status(guardianFuture) == "cancelled", "A consulta futura marcada pelo responsável devia ser cancelada.");
            Require(await f.UnitScalar<long>(u1, "SELECT count(*) FROM nora_unit.appointment_events WHERE appointment_id=$1 AND status='cancelled' AND source='system' AND actor_id=$2 AND guardian_id=$2", guardianFuture, f.PatientIds[3]) == 1, "Auditoria: origem system, autor e guardian_id = responsável que encerrou.");
            Require(await Status(staffFuture) == "scheduled", "A consulta marcada pela equipe continua.");
            Require(await Status(past) == "scheduled", "A consulta que já começou não é tocada.");
            Require((await FreeCardiology("2026-11-25")).Contains("12:00"), "O horário cancelado volta à disponibilidade.");
            var attendance = await Attendance(72);
            Require(attendance.GetProperty("cancelledByPatient").GetInt64() == 0 && attendance.GetProperty("cancellationWeight").GetDouble() == 0, $"O cancelamento pelo fim do vínculo não pesa no score: {attendance}.");
        });

        await Check("Fim do vínculo pela equipe, dependente sem mais ninguém: consulta futura scheduled ou called marcada pelo responsável é cancelada (origem system, autor = equipe, sem guardian_id)", async () =>
        {
            var link = await Linked(f.G4, 73, u1);
            var called = await BookedId(await Book(f.G4, link, u1, ana, cardiology, "2026-11-25T12:30:00Z"));
            using (var call = await f.Attendant.PatchAsJsonAsync($"/api/obs/units/{u1}/appointments/{called}/status", new { status = "called", confirmed = false })) Expect(call, 200);
            using (var end = await f.Attendant.PostAsJsonAsync($"/api/obs/units/{u1}/family-links/{link}/end", new { confirmed = true })) Expect(end, 200);
            Require(await Status(called) == "cancelled", "A consulta chamada e futura marcada pelo responsável devia ser cancelada.");
            Require(await f.UnitScalar<long>(u1, "SELECT count(*) FROM nora_unit.appointment_events WHERE appointment_id=$1 AND status='cancelled' AND source='system' AND actor_id=$2 AND guardian_id IS NULL", called, f.AttendantId) == 1, "Auditoria: origem system, autor = equipe.");
        });

        await Check("Fim do vínculo de quem continua com alguém que responda (outro vínculo aprovado ou conta própria vinculada): nada é cancelado", async () =>
        {
            var first = await Linked(f.G3, 74, u1); await Linked(f.G4, 74, u1);
            var kept = await BookedId(await Book(f.G3, first, u1, ana, cardiology, "2026-11-25T13:00:00Z"));
            using (var end = await f.G3.PostAsJsonAsync($"/api/patient/dependents/{first}/end", new { confirmed = true })) Expect(end, 200);
            Require(await Status(kept) == "scheduled", "Outro responsável ainda responde: a consulta continua.");
            using (var endOther = await f.G2.PostAsJsonAsync($"/api/patient/dependents/{g2Link}/end", new { confirmed = true })) Expect(endOther, 200);
            using (var endLast = await f.G1.PostAsJsonAsync($"/api/patient/dependents/{d2Link}/end", new { confirmed = true })) Expect(endLast, 200);
            Require(await Status(guardianBooking) == "scheduled", "O dependente tem conta própria vinculada: a consulta marcada pelo responsável continua.");
        });

        await Check("Fim do vínculo que deixa o dependente sem ninguém com o banco da UBS fora do ar: 503, nada muda (vínculo aprovado, consulta marcada); repetir com a UBS de volta conclui", async () =>
        {
            var link = await Linked(f.G4, 75, u1);
            var booking = await BookedId(await Book(f.G4, link, u1, ana, cardiology, "2026-11-25T13:30:00Z"));
            await f.WithUnitDownAsync(u1, async () => { using var end = await f.G4.PostAsJsonAsync($"/api/patient/dependents/{link}/end", new { confirmed = true }); Expect(end, 503); });
            Require(await f.Scalar<string>("SELECT status FROM nora.family_links WHERE id=$1", link) == "approved" && await Status(booking) == "scheduled", "Com a UBS fora do ar o encerramento não pode valer pela metade.");
            using (var again = await f.G4.PostAsJsonAsync($"/api/patient/dependents/{link}/end", new { confirmed = true })) Expect(again, 200);
            Require(await Status(booking) == "cancelled", "Repetir com a UBS de volta cancela a consulta.");
        });

        // ======== C2: prévia da remoção da UBS pelo paciente ========
        await Check("Prévia da remoção (C2): conta exatamente o que a remoção cancela (consulta a mais de 12 meses inclusive; sem cancelada nem passada); sessão, vínculo, UBS fora do ar, perfil e limite de leitura", async () =>
        {
            var accounts = f.App.Services.GetRequiredService<AccountService>();
            async Task<HttpClient> Patient(int number, string name)
            {
                await accounts.ProvisionAsync("patient", "patient", new(name, Cpf(number), $"p{number}@example.test", Password));
                await Register(number, name);
                var client = await LoginAsync(f, Cpf(number));
                using (var link = await client.PostAsJsonAsync("/api/patient/units/link", new { unitIds = new[] { u1 } })) Expect(link, 200);
                return client;
            }
            using var p81 = await Patient(81, "Paciente Oitenta e Um");
            await StaffBook(81, cardiology, "2026-11-25T14:00:00Z");
            var called = await StaffBook(81, clinical, "2026-11-26T12:30:00Z");
            using (var call = await f.Attendant.PatchAsJsonAsync($"/api/obs/units/{u1}/appointments/{called}/status", new { status = "called", confirmed = false })) Expect(call, 200);
            await StaffBook(81, cardiology, "2027-12-01T12:00:00Z"); // mais de 12 meses à frente
            var cancelled = await StaffBook(81, cardiology, "2026-11-25T14:30:00Z");
            using (var cancel = await f.Attendant.PatchAsJsonAsync($"/api/obs/units/{u1}/appointments/{cancelled}/status", new { status = "cancelled", confirmed = true })) Expect(cancel, 200);
            await f.UnitExec(u1, "INSERT INTO nora_unit.appointments(id,cpf,specialty,professional,starts_at,ends_at,status,created_by,created_at) VALUES($1,$2,'Cardiologia','Dra. Ana Souza',$3,$4,'scheduled',$5,$3)", Guid.NewGuid(), Cpf(81), T0.AddDays(-1), T0.AddDays(-1).AddMinutes(30), f.AdminId);
            foreach (var specialty in new[] { "Cardiologia", "Ortopedia" }) { using var queue = await f.Attendant.PostAsJsonAsync($"/api/obs/units/{u1}/waiting-list", new { cpf = Cpf(81), specialty }); Expect(queue, 201); }
            var path = $"/api/patient/units/{u1}/unlink-preview";
            int appointments, entries;
            using (var preview = await p81.GetAsync(path)) { Expect(preview, 200); var body = await Read(preview); appointments = body.GetProperty("futureAppointments").GetInt32(); entries = body.GetProperty("activeWaitingEntries").GetInt32(); }
            Require(appointments == 3 && entries == 2, $"Prévia: esperado 3 consultas futuras e 2 entradas de fila; recebido {appointments} e {entries}.");
            Require(await f.UnitScalar<long>(u1, "SELECT count(*) FROM nora_unit.appointments WHERE cpf=$1 AND status IN ('scheduled','called')", Cpf(81)) == 4, "A prévia não grava nada.");
            using (var anonymous = await f.Anonymous.GetAsync(path)) Expect(anonymous, 401);
            using (var staff = await f.Admin.GetAsync(path)) Expect(staff, 403);
            using (var notLinked = await f.D2.GetAsync($"/api/patient/units/{u2}/unlink-preview")) Expect(notLinked, 403);
            await f.WithUnitDownAsync(u1, async () => { using var down = await p81.GetAsync(path); Expect(down, 503); });
            using (var unlink = await p81.PostAsJsonAsync($"/api/patient/units/{u1}/unlink", new { confirmed = true }))
            {
                Expect(unlink, 200); var body = await Read(unlink);
                Require(body.GetProperty("cancelledAppointments").GetInt32() == appointments && body.GetProperty("cancelledEntries").GetInt32() == entries, $"A remoção cancelou outra quantidade que a prévia mostrou: {body}.");
            }
            using (var after = await p81.GetAsync(path)) Expect(after, 403);

            // Cadastro desativado na UBS: como a remoção, a prévia continua respondendo. Limite de leitura: 60 por 10 minutos por conta.
            using var p82 = await Patient(82, "Paciente Oitenta e Dois");
            using (var off = await f.Admin.PatchAsJsonAsync($"/api/obs/units/{u1}/registry/active", new { cpf = Cpf(82), isActive = false, confirmed = true })) Expect(off, 204);
            for (var i = 0; i < 60; i++)
            {
                using var preview = await p82.GetAsync(path); Expect(preview, 200);
                if (i == 0) { var body = await Read(preview); Require(body.GetProperty("futureAppointments").GetInt32() == 0 && body.GetProperty("activeWaitingEntries").GetInt32() == 0, $"Prévia com cadastro desativado: {body}."); }
            }
            using (var limited = await p82.GetAsync(path)) await ExpectProblem(limited, 429, "too-many-requests");
            using (var other = await f.G1.GetAsync(path)) Expect(other, 200); // o limite é por conta
        });

        Console.WriteLine($"{checks - failures}/{checks} verificações das decisões da Família passaram.");
        return failures;
    }
}
