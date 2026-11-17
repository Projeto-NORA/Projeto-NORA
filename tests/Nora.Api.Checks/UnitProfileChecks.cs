using System.Net.Http.Json;
using System.Text.Json;
using Nora.Api.DAOs;
using Nora.Api.Models;
using static RoundFixture;

/// <summary>
/// Serviços e horários da UBS: o administrador edita no OBS o horário de funcionamento por dia da semana e um texto de serviços; o app lê por UBS vinculada (e o responsável, pela UBS do dependente);
/// as especialidades vêm da grade. Roda com <c>--unit-profile-only</c>.
/// </summary>
internal static class UnitProfileChecks
{
    public static async Task<int> RunAsync(string root)
    {
        var connection = Environment.GetEnvironmentVariable("NORA_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection))
        {
            Console.WriteLine("SKIP serviços e horários da UBS: configure NORA_TEST_CONNECTION somente para uma base descartável.");
            return 0;
        }
        var suite = new Suite("serviços e horários da UBS");
        await using var f = await StartAsync(connection, "profile");
        var u1 = f.Unit1; var u2 = f.Unit2;
        var (_, attendantDevice) = await ObsDeviceHelper.ActivateAsync(f.Admin, u1);
        var attendant = f.NewClient(); await f.LoginAsync(attendant, "obs", "atendente@example.test", attendantDevice);
        var patient = await f.PatientAsync(1, u1); var otherUnitPatient = await f.PatientAsync(2, u2); await f.PatientAsync(3, u1);
        var onlyU1 = await f.StaffAsync("administrator", 40, [u1]);

        object Hour(int weekday, string opens, string closes) => new { weekday, opens, closes };
        async Task<HttpResponseMessage> Put(HttpClient client, Guid unit, object[]? hours, string? services = null, bool omitHours = false) =>
            omitHours ? await client.PutAsJsonAsync($"/api/obs/units/{unit}/profile", new { services }) : await client.PutAsJsonAsync($"/api/obs/units/{unit}/profile", new { openingHours = hours, services });
        string Hours(JsonElement profile) => string.Join(";", profile.GetProperty("openingHours").EnumerateArray().Select(item => $"{item.GetProperty("weekday").GetInt32()} {item.GetProperty("opens").GetString()}-{item.GetProperty("closes").GetString()}"));

        await suite.Check("Antes de informar: informed falso (diferente de fechada); o administrador grava e lê; horários ordenados, texto como veio sem espaços nas pontas", async () =>
        {
            using (var before = await f.Admin.GetAsync($"/api/obs/units/{u1}/profile"))
            {
                Expect(before, 200); var body = await Read(before);
                Ensure(!body.GetProperty("informed").GetBoolean() && body.GetProperty("openingHours").GetArrayLength() == 0 && body.GetProperty("services").ValueKind == JsonValueKind.Null && body.GetProperty("updatedAt").ValueKind == JsonValueKind.Null && body.GetProperty("unitId").GetGuid() == u1
                    && !body.GetProperty("slotApprovalRequired").GetBoolean() && body.GetProperty("specialties").GetArrayLength() == 0, "UBS que nunca informou: informed falso, sem horários, sem texto.");
            }
            f.Clock.Advance(TimeSpan.FromMinutes(5));
            using (var saved = await Put(f.Admin, u1, [Hour(6, "08:00", "12:00"), Hour(1, "13:00", "17:00"), Hour(1, "08:00", "12:00"), Hour(0, "09:00", "11:00")], "  Vacinação\nCurativos e inalação\r\nTeste do pezinho  "))
            {
                Expect(saved, 200); var body = await Read(saved);
                Ensure(Hours(body) == "0 09:00-11:00;1 08:00-12:00;1 13:00-17:00;6 08:00-12:00" && body.GetProperty("informed").GetBoolean() && body.GetProperty("services").GetString() == "Vacinação\nCurativos e inalação\r\nTeste do pezinho"
                    && body.GetProperty("updatedAt").GetDateTimeOffset() == RoundClock.T0.AddMinutes(5) && !body.TryGetProperty("slotApprovalRequired", out _), "PUT devolve o perfil ordenado, com o carimbo.");
            }
            foreach (var client in new[] { f.Admin, attendant })
            {
                using var read = await client.GetAsync($"/api/obs/units/{u1}/profile"); Expect(read, 200); var body = await Read(read);
                Ensure(Hours(body) == "0 09:00-11:00;1 08:00-12:00;1 13:00-17:00;6 08:00-12:00" && body.GetProperty("slotApprovalRequired").ValueKind == JsonValueKind.False, "A equipe lê o que o administrador gravou.");
            }
            // Lista vazia = fechada todos os dias (informed continua verdadeiro) e texto vazio vira nulo.
            using (var closed = await Put(f.Admin, u1, [], "   "))
            { Expect(closed, 200); var body = await Read(closed); Ensure(body.GetProperty("informed").GetBoolean() && body.GetProperty("openingHours").GetArrayLength() == 0 && body.GetProperty("services").ValueKind == JsonValueKind.Null, "Fechada todos os dias."); }
            using (var again = await Put(f.Admin, u1, [Hour(2, "07:30", "11:30")], "Consultas")) Expect(again, 200);
        });

        await suite.Check("Permissões: atendente lê mas não grava (403); sem concessão 403; Secretaria e paciente 403 nas rotas do OBS; sem sessão 401", async () =>
        {
            using (var denied = await Put(attendant, u1, [Hour(1, "08:00", "12:00")])) Expect(denied, 403);
            using (var noGrantPut = await Put(onlyU1.Client, u2, [Hour(1, "08:00", "12:00")])) Expect(noGrantPut, 403);
            using (var noGrantGet = await onlyU1.Client.GetAsync($"/api/obs/units/{u2}/profile")) Expect(noGrantGet, 403);
            using (var attendantOtherUnit = await attendant.GetAsync($"/api/obs/units/{u2}/profile")) Expect(attendantOtherUnit, 403);
            using (var manager = await f.Municipal.GetAsync($"/api/obs/units/{u1}/profile")) Expect(manager, 403);
            using (var managerPut = await Put(f.Municipal, u1, [])) Expect(managerPut, 403);
            using (var byPatient = await patient.GetAsync($"/api/obs/units/{u1}/profile")) Expect(byPatient, 403);
            using (var anonymous = await f.Anonymous.GetAsync($"/api/obs/units/{u1}/profile")) Expect(anonymous, 401);
            using (var unknownUnit = await f.Admin.GetAsync($"/api/obs/units/{Guid.NewGuid()}/profile")) Expect(unknownUnit, 403);
            using (var read = await f.Admin.GetAsync($"/api/obs/units/{u1}/profile")) Ensure(Hours(await Read(read)) == "2 07:30-11:30", "As recusas não mudaram nada.");
        });

        await suite.Check("Validação: 3 intervalos no dia, sobreposição, closes ≤ opens, 25:00, 8:00, dia 7 ou -1, campos faltando, texto de 1.001 caracteres e caractere de controle → 400 sem gravar; encostar, 2 intervalos e 1.000 caracteres valem", async () =>
        {
            var invalid = new (string Label, object[]? Hours, string? Services, bool Omit)[]
            {
                ("três intervalos", [Hour(1, "06:00", "08:00"), Hour(1, "09:00", "11:00"), Hour(1, "12:00", "14:00")], null, false),
                ("sobreposição", [Hour(1, "08:00", "12:00"), Hour(1, "11:59", "14:00")], null, false),
                ("closes igual", [Hour(1, "08:00", "08:00")], null, false), ("closes antes", [Hour(1, "12:00", "08:00")], null, false), ("25:00", [Hour(1, "08:00", "25:00")], null, false),
                ("8:00", [Hour(1, "8:00", "12:00")], null, false), ("hora vazia", [Hour(1, "", "12:00")], null, false), ("dia 7", [Hour(7, "08:00", "12:00")], null, false), ("dia -1", [Hour(-1, "08:00", "12:00")], null, false),
                ("sem a lista", null, "Texto", true), ("lista nula", null, "Texto", false), ("texto longo", [], new string('x', 1001), false), ("controle", [], "Texto\u0007com controle", false), ("tab", [], "Texto\tcom tab", false)
            };
            foreach (var (label, hours, services, omit) in invalid)
            {
                using var response = await Put(f.Admin, u1, hours, services, omit);
                Ensure((int)response.StatusCode == 400, $"{label}: esperado 400, recebido {(int)response.StatusCode}.");
            }
            using (var missingDay = await f.Admin.PutAsJsonAsync($"/api/obs/units/{u1}/profile", new { openingHours = new object[] { new { opens = "08:00", closes = "12:00" } } })) Expect(missingDay, 400);
            using (var read = await f.Admin.GetAsync($"/api/obs/units/{u1}/profile")) { var body = await Read(read); Ensure(Hours(body) == "2 07:30-11:30" && body.GetProperty("services").GetString() == "Consultas", "Nenhuma recusa gravou."); }
            using (var valid = await Put(f.Admin, u1, [Hour(3, "08:00", "12:00"), Hour(3, "12:00", "18:00")], new string('y', 1000)))
            { Expect(valid, 200); Ensure(Hours(await Read(valid)) == "3 08:00-12:00;3 12:00-18:00", "Dois intervalos que encostam valem."); }
            using (var weekdays = await Put(f.Admin, u1, Enumerable.Range(0, 7).SelectMany(day => new[] { Hour(day, "08:00", "12:00"), Hour(day, "14:00", "18:00") }).ToArray(), "Todos os dias")) { Expect(weekdays, 200); Ensure((await Read(weekdays)).GetProperty("openingHours").GetArrayLength() == 14, "Sete dias, dois intervalos."); }
        });

        await suite.Check("App: o paciente vinculado lê a UBS dele; sem vínculo ou com o cadastro inativo 403; sem sessão 401; a UBS que nunca informou responde informed falso", async () =>
        {
            using (var linked = await patient.GetAsync($"/api/patient/units/{u1}/about"))
            {
                Expect(linked, 200); var body = await Read(linked);
                Ensure(body.GetProperty("unitId").GetGuid() == u1 && body.GetProperty("informed").GetBoolean() && body.GetProperty("openingHours").GetArrayLength() == 14 && body.GetProperty("services").GetString() == "Todos os dias" && !body.TryGetProperty("slotApprovalRequired", out _), "Perfil da UBS vinculada, sem campos do OBS.");
            }
            using (var notLinked = await patient.GetAsync($"/api/patient/units/{u2}/about")) Expect(notLinked, 403);
            using (var otherUnitRead = await otherUnitPatient.GetAsync($"/api/patient/units/{u2}/about"))
            { Expect(otherUnitRead, 200); var body = await Read(otherUnitRead); Ensure(!body.GetProperty("informed").GetBoolean() && body.GetProperty("openingHours").GetArrayLength() == 0 && body.GetProperty("services").ValueKind == JsonValueKind.Null, "UBS que nunca informou."); }
            using (var anonymous = await f.Anonymous.GetAsync($"/api/patient/units/{u1}/about")) Expect(anonymous, 401);
            using (var staff = await attendant.GetAsync($"/api/patient/units/{u1}/about")) Expect(staff, 403);
            await f.UnitExec(u1, "UPDATE nora_unit.patient_registry SET is_active=false WHERE cpf=$1", Cpf(3));
            using (var inactive = await f.Patients[3].GetAsync($"/api/patient/units/{u1}/about")) Expect(inactive, 403);
            using (var unknown = await patient.GetAsync($"/api/patient/units/{Guid.NewGuid()}/about")) Expect(unknown, 403);
        });

        await suite.Check("Responsável: lê pela rota do dependente com vínculo aprovado; vínculo de outro responsável, encerrado ou de outra UBS → 403", async () =>
        {
            var guardian = await f.PatientAsync(10); var stranger = await f.PatientAsync(11);
            await f.RegisterAsync(12, u1); await f.RegisterAsync(12, u2);
            var link = Guid.NewGuid(); var otherUnitLink = Guid.NewGuid();
            await f.Exec("INSERT INTO nora.family_links(id,guardian_id,dependent_cpf,unit_id,status,requested_at,decided_by,decided_at) VALUES($1,$2,$3,$4,'approved',$5,$6,$5)", link, f.PatientIds[10], Cpf(12), u1, RoundClock.T0, f.AdminId);
            await f.Exec("INSERT INTO nora.family_links(id,guardian_id,dependent_cpf,unit_id,status,requested_at,decided_by,decided_at) VALUES($1,$2,$3,$4,'approved',$5,$6,$5)", otherUnitLink, f.PatientIds[10], Cpf(12), u2, RoundClock.T0, f.AdminId);
            using (var ok = await guardian.GetAsync($"/api/patient/dependents/{link}/units/{u1}/about")) { Expect(ok, 200); Ensure((await Read(ok)).GetProperty("unitId").GetGuid() == u1, "O responsável lê a UBS do dependente."); }
            using (var wrongUnit = await guardian.GetAsync($"/api/patient/dependents/{link}/units/{u2}/about")) Expect(wrongUnit, 403);
            using (var fromOtherUnit = await guardian.GetAsync($"/api/patient/dependents/{otherUnitLink}/units/{u2}/about")) Expect(fromOtherUnit, 200);
            using (var byStranger = await stranger.GetAsync($"/api/patient/dependents/{link}/units/{u1}/about")) Expect(byStranger, 403);
            using (var unknown = await guardian.GetAsync($"/api/patient/dependents/{Guid.NewGuid()}/units/{u1}/about")) Expect(unknown, 403);
            await f.Exec("UPDATE nora.family_links SET status='ended',closed_by=$2,closed_at=$3 WHERE id=$1", link, f.AdminId, RoundClock.T0);
            using (var ended = await guardian.GetAsync($"/api/patient/dependents/{link}/units/{u1}/about")) Expect(ended, 403);
            using (var anonymous = await f.Anonymous.GetAsync($"/api/patient/dependents/{link}/units/{u1}/about")) Expect(anonymous, 401);
        });

        await suite.Check("Especialidades vêm da grade: só de profissional e consultório ativos, ignoram grade vencida e especialidade desativada, por nome (sem diferenciar acento); sem grade ou sem banco pronto → vazio", async () =>
        {
            var allergy = await f.SpecialtyAsync("Álergia"); var cardiology = await f.SpecialtyAsync("Cardiologia"); var gynecology = await f.SpecialtyAsync("Ginecologia"); var orthopedics = await f.SpecialtyAsync("Ortopedia"); var retired = await f.SpecialtyAsync("Aposentada");
            var doctor = await f.ProfessionalAsync(u1, "Dr. Grade", allergy, cardiology, gynecology, orthopedics, retired); var inactiveDoctor = await f.ProfessionalAsync(u1, "Dra. Inativa", cardiology);
            var room = await f.RoomAsync(u1, "Sala A"); var room2 = await f.RoomAsync(u1, "Sala B"); var room3 = await f.RoomAsync(u1, "Sala C"); var room4 = await f.RoomAsync(u1, "Sala D");
            async Task<Guid> Template(Guid professional, int weekday, string start, string end, Guid roomId, Guid specialty, string? until = null)
            {
                using var response = await f.Admin.PostAsJsonAsync($"/api/obs/units/{u1}/schedule", new { professionalId = professional, weekday, startTime = start, endTime = end, slotMinutes = 30, roomId, specialtyId = specialty, validUntil = until });
                Expect(response, 201); return (await Read(response)).GetProperty("id").GetGuid();
            }
            string Names(JsonElement profile) => string.Join(",", profile.GetProperty("specialties").EnumerateArray().Select(item => item.GetProperty("name").GetString()));
            async Task<JsonElement> Read1() { using var response = await patient.GetAsync($"/api/patient/units/{u1}/about"); Expect(response, 200); return await Read(response); }
            Ensure(Names(await Read1()) == "", "Sem grade: nenhuma especialidade.");
            await Template(doctor, 1, "08:00", "10:00", room, orthopedics); await Template(doctor, 2, "08:00", "10:00", room, cardiology); await Template(doctor, 3, "08:00", "10:00", room, gynecology); await Template(doctor, 4, "08:00", "10:00", room, allergy);
            var expiredTemplate = await Template(doctor, 5, "08:00", "10:00", room, retired, "2026-11-15"); // venceu ontem (hoje é 2026-11-16)
            await Template(inactiveDoctor, 1, "10:00", "12:00", room2, cardiology);
            var body = await Read1();
            Ensure(Names(body) == "Álergia,Cardiologia,Ginecologia,Ortopedia" && body.GetProperty("specialties").EnumerateArray().All(item => item.GetProperty("id").ValueKind == JsonValueKind.String), $"Ordem por nome sem diferenciar acento, sem a grade vencida: {Names(body)}.");
            using (var today = await f.Admin.PutAsJsonAsync($"/api/obs/units/{u1}/schedule/{expiredTemplate}", new { professionalId = doctor, weekday = 5, startTime = "08:00", endTime = "10:00", slotMinutes = 30, roomId = room, specialtyId = retired, validUntil = "2026-11-16" })) Expect(today, 200);
            Ensure(Names(await Read1()).Contains("Aposentada", StringComparison.Ordinal), "Vigência até hoje ainda conta.");
            using (var off = await f.Municipal.PatchAsJsonAsync($"/api/municipal/specialties/{retired}/active", new { isActive = false })) Expect(off, 200);
            Ensure(!Names(await Read1()).Contains("Aposentada", StringComparison.Ordinal), "Especialidade desativada sai da lista.");
            using (var doctorOff = await f.Admin.PatchAsJsonAsync($"/api/obs/units/{u1}/professionals/{doctor}/active", new { isActive = false })) Expect(doctorOff, 200);
            Ensure(Names(await Read1()) == "Cardiologia", "Sem o Dr. Grade ativo, só resta a especialidade da outra profissional ativa.");
            using (var inactiveProfessional = await f.Admin.PatchAsJsonAsync($"/api/obs/units/{u1}/professionals/{inactiveDoctor}/active", new { isActive = false })) Expect(inactiveProfessional, 200);
            Ensure(Names(await Read1()) == "", "Nenhum profissional ativo: lista vazia.");
            using (var doctorOn = await f.Admin.PatchAsJsonAsync($"/api/obs/units/{u1}/professionals/{doctor}/active", new { isActive = true })) Expect(doctorOn, 200);
            using (var roomOff = await f.Admin.PatchAsJsonAsync($"/api/obs/units/{u1}/rooms/{room}/active", new { isActive = false })) Expect(roomOff, 200);
            Ensure(Names(await Read1()) == "", "Consultório inativo: lista vazia.");
            // UBS sem banco pronto (só cadastro): o perfil responde, sem especialidades.
            var pending = (await f.Service<HealthUnitDao>().CreateAsync(new(Guid.NewGuid(), "UBS sem banco", "Centro", "Rua nova, 2", -23.5, -46.6), default)).Id;
            await f.GrantAsync(f.AdminId, pending);
            using (var withoutDatabase = await f.Admin.GetAsync($"/api/obs/units/{pending}/profile")) { Expect(withoutDatabase, 200); Ensure((await Read(withoutDatabase)).GetProperty("specialties").GetArrayLength() == 0, "UBS sem banco: sem especialidades."); }
            Ensure(f.Units.Count == 2 && room2 != room3 && room4 != room, "Cadastros de apoio distintos.");
        });

        await suite.Check("Gravações simultâneas: o último vence e os horários e o texto nunca misturam duas versões", async () =>
        {
            var variants = Enumerable.Range(0, 6).ToArray();
            var results = await Task.WhenAll(variants.Select(async i =>
            {
                using var response = await Put(f.Admin, u1, [Hour(i, "08:00", "12:00"), Hour(i, "13:00", "17:00")], $"versão {i}");
                return (int)response.StatusCode;
            }));
            Ensure(results.All(status => status == 200), $"Gravações simultâneas: {string.Join(",", results)}.");
            using var read = await f.Admin.GetAsync($"/api/obs/units/{u1}/profile"); var body = await Read(read);
            var version = int.Parse(body.GetProperty("services").GetString()!["versão ".Length..]);
            Ensure(Hours(body) == $"{version} 08:00-12:00;{version} 13:00-17:00" && await f.Scalar<long>("SELECT count(*) FROM nora.unit_opening_hours WHERE unit_id=$1", u1) == 2, $"Versão misturada: {Hours(body)} / {body.GetProperty("services").GetString()}.");
        });
        return suite.Report() == 0 ? 0 : 1;
    }
}
