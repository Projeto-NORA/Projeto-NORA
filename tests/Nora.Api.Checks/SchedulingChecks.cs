using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using Nora.Api;
using Nora.Api.DAOs;
using Nora.Api.Database;
using Nora.Api.Models;
using Nora.Api.Services;

/// <summary>
/// Fase 3 — agenda e metas: especialidades, catálogo (profissionais, consultórios, grade, bloqueios), disponibilidade, agendamento pela equipe e pelo paciente,
/// confirmação de presença, capacidade, metas e alertas. As verificações sem banco rodam sempre; as demais exigem NORA_TEST_CONNECTION (cluster descartável) e usam
/// um relógio de teste (segunda-feira 2026-11-16 12:00 UTC = 09:00 em America/Sao_Paulo) que só se mexe em segundos/minutos, para as sessões (8 h) continuarem válidas.
/// </summary>
internal static class SchedulingChecks
{
    private const string Password = "Disposable-test-Account!29";
    private static readonly DateTimeOffset T0 = new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);
    private static int checks;
    private static int failures;

    private static DateTimeOffset At(string iso) => DateTimeOffset.Parse(iso, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal).ToUniversalTime();

    public static async Task<int> RunAsync(string root)
    {
        // ---- Sem banco ----
        await Check("Chave de especialidade ignora maiúsculas, acentos e espaços repetidos; nome é validado", () =>
        {
            Require(SpecialtyDao.Key("  Cardiología   CLÍNICA ") == "cardiologia clinica" && SpecialtyDao.Key("Cardiologia") == SpecialtyDao.Key("CARDIOLOGÍA"), "Chave de especialidade incorreta.");
            Require(SpecialtyDao.ValidName("  Clínica   Geral ") == "Clínica Geral", "Nome não foi normalizado.");
            foreach (var invalid in new[] { null, "", " a ", new string('x', 121), "Nome\u0007Controle" })
            {
                try { SpecialtyDao.ValidName(invalid); throw new InvalidOperationException($"Nome inválido '{invalid}' foi aceito."); }
                catch (ApiInputException) { }
            }
            return Task.CompletedTask;
        });

        await Check("Conjunto de intervalos: sobreposição estrita (intervalos que só se tocam não se cruzam), união e busca nas bordas", () =>
        {
            var set = IntervalSet.Build([new(10, 20), new(20, 30), new(50, 60), new(15, 25)]);
            Require(set.Overlaps(0, 11) && set.Overlaps(29, 31) && set.Overlaps(55, 56) && set.Overlaps(0, 100), "Sobreposição não detectada.");
            Require(!set.Overlaps(0, 10) && !set.Overlaps(30, 50) && !set.Overlaps(60, 70) && !IntervalSet.Empty.Overlaps(0, long.MaxValue), "Intervalo que só toca foi tratado como sobreposição.");
            return Task.CompletedTask;
        });

        await Check("Motor de horários: fuso America/Sao_Paulo, duração, resto da faixa descartado, vigência e dia da semana", () =>
        {
            var zone = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");
            var engine = new SlotEngine(zone);
            var professional = Guid.NewGuid(); var room = Guid.NewGuid(); var specialty = Guid.NewGuid();
            ScheduleTemplateRow Row(int weekday, string start, string end, int minutes, DateOnly? from = null, DateOnly? until = null) =>
                new(Guid.NewGuid(), professional, "Dra. Teste", "dra. teste", weekday, TimeOnly.Parse(start, CultureInfo.InvariantCulture), TimeOnly.Parse(end, CultureInfo.InvariantCulture), minutes, room, "Sala", specialty, from, until);
            // Quarta 08:00-09:10 de 30 em 30: 08:00 e 08:30 (os 10 minutos finais não formam um atendimento). Local -03:00 → 11:00Z e 11:30Z.
            var slots = engine.Generate([Row(3, "08:00", "09:10", 30)], At("2026-11-16T00:00:00Z"), At("2026-11-23T00:00:00Z")).ToList();
            Require(slots.Count == 2 && slots[0].Start == At("2026-11-18T11:00:00Z").UtcTicks && slots[1].Start == At("2026-11-18T11:30:00Z").UtcTicks && slots[0].End - slots[0].Start == TimeSpan.FromMinutes(30).Ticks, "Atendimentos da quarta incorretos.");
            // Janela [from, to): o início de um atendimento em `to` fica de fora e em `from` entra.
            Require(engine.Generate([Row(3, "08:00", "09:10", 30)], At("2026-11-18T11:30:00Z"), At("2026-11-18T11:30:01Z")).Count() == 1 && !engine.Generate([Row(3, "08:00", "09:10", 30)], At("2026-11-18T11:00:00Z"), At("2026-11-18T11:30:00Z")).Skip(1).Any(), "Bordas da janela incorretas.");
            // Vigência: só de 2026-11-25 a 2026-12-02 (quartas 25/11 e 02/12).
            var valid = engine.Generate([Row(3, "10:00", "10:30", 30, new(2026, 11, 25), new(2026, 12, 2))], At("2026-11-16T00:00:00Z"), At("2026-12-31T00:00:00Z")).ToList();
            Require(valid.Count == 2 && valid[0].Start == At("2026-11-25T13:00:00Z").UtcTicks && valid[1].Start == At("2026-12-02T13:00:00Z").UtcTicks, "Vigência não foi respeitada.");
            // Domingo (0) e sábado (6) existem.
            Require(engine.Generate([Row(0, "09:00", "10:00", 60), Row(6, "09:00", "10:00", 60)], At("2026-11-21T00:00:00Z"), At("2026-11-23T00:00:00Z")).Count() == 2, "Fim de semana não gerou atendimentos.");
            // Janela que começa à noite (dia local anterior): quarta 00:30Z ainda é terça no fuso local.
            Require(!engine.Generate([Row(3, "08:00", "09:00", 30)], At("2026-11-18T00:30:00Z"), At("2026-11-18T01:00:00Z")).Any(), "Dia local incorreto.");
            return Task.CompletedTask;
        });

        await Check("Status das metas: mínimo (score) e máximos (faltas, espera) nas bordas de 10%, sem erro de ponto flutuante; sem dado → no-data", () =>
        {
            (string Metric, decimal Target, double? Value, string Expected)[] cases =
            [
                ("attendance_score", 0.8m, 0.8, "met"), ("attendance_score", 0.8m, 0.9, "met"), ("attendance_score", 0.8m, 0.7999, "near"), ("attendance_score", 0.8m, 0.72, "near"), ("attendance_score", 0.8m, 0.7199, "off"), ("attendance_score", 0.8m, 0, "off"),
                ("attendance_score", 0m, 0, "met"), ("attendance_score", 0.9m, 0.81, "near"), ("attendance_score", 0.9m, 0.8099, "off"),
                ("missed_rate", 0.2m, 0, "met"), ("missed_rate", 0.2m, 0.2, "met"), ("missed_rate", 0.2m, 0.2001, "near"), ("missed_rate", 0.2m, 0.22, "near"), ("missed_rate", 0.2m, 0.2201, "off"), ("missed_rate", 0m, 0, "met"), ("missed_rate", 0m, 0.0001, "off"),
                ("average_wait_minutes", 30m, 30, "met"), ("average_wait_minutes", 30m, 30.1, "near"), ("average_wait_minutes", 30m, 33, "near"), ("average_wait_minutes", 30m, 33.1, "off"),
                ("attendance_score", 0.8m, null, "no-data"), ("missed_rate", 0.2m, null, "no-data"), ("average_wait_minutes", 30m, null, "no-data")
            ];
            foreach (var (metric, target, value, expected) in cases)
                Require(GoalService.StatusOf(metric, target, value) == expected, $"{metric} meta {target} valor {value}: esperado {expected}, obtido {GoalService.StatusOf(metric, target, value)}.");
            var network = new Goal("attendance_score", null, 0.9m); var unit = Guid.NewGuid(); var override1 = new Goal("attendance_score", unit, 0.8m);
            Require(GoalService.Evaluate([network, override1], unit, 0.8, null, null).Single().Status == "met" && GoalService.Evaluate([network, override1], Guid.NewGuid(), 0.8, null, null).Single().Status == "off" && GoalService.Evaluate([network, override1], null, 0.8, null, null).Single().Target == 0.9m, "Sobrescrita por UBS incorreta.");
            Require(GoalService.MissedRateOf(0, 0) is null && GoalService.MissedRateOf(8, 2) == 0.2 && GoalService.MissedRateOf(0, 3) == 1, "Taxa de faltas incorreta.");
            foreach (var (metric, target) in new[] { ("attendance_score", 1.01m), ("attendance_score", -0.1m), ("missed_rate", 1.5m), ("average_wait_minutes", 0m), ("average_wait_minutes", -1m) })
            {
                try { GoalService.ValidTarget(metric, target); throw new InvalidOperationException($"Meta inválida {metric}={target} foi aceita."); }
                catch (ApiInputException) { }
            }
            Require(GoalService.ValidTarget("attendance_score", 1m) == 1m && GoalService.ValidTarget("average_wait_minutes", 45.5m) == 45.5m, "Meta válida recusada.");
            return Task.CompletedTask;
        });

        await Check("Limites dos alertas: padrões decididos (20, 30 dias, 20%, 30 dias, 95%, 7 dias) e configuração inválida impede a inicialização", () =>
        {
            AlertOptions Options(params (string, string)[] values) => new(new ConfigurationBuilder().AddInMemoryCollection(values.ToDictionary(v => "Nora:Alerts:" + v.Item1, v => (string?)v.Item2)).Build());
            var defaults = Options();
            Require(defaults.HighQueue == 20 && defaults.LongWaitDays == 30 && defaults.MissedRate == 0.20 && defaults.WindowDays == 30 && defaults.Occupancy == 0.95 && defaults.OccupancyDays == 7, "Padrões dos alertas incorretos.");
            var custom = Options(("HighQueue", "5"), ("MissedRate", "0.5"));
            Require(custom.HighQueue == 5 && custom.MissedRate == 0.5, "Configuração dos alertas não foi lida.");
            foreach (var invalid in new[] { ("HighQueue", "0"), ("HighQueue", "x"), ("MissedRate", "1.5"), ("Occupancy", "-1"), ("LongWaitDays", "0") })
            {
                try { Options(invalid); throw new InvalidOperationException($"Configuração inválida {invalid} foi aceita."); }
                catch (InvalidOperationException error) when (error.Message.Contains("Nora:Alerts")) { }
            }
            return Task.CompletedTask;
        });

        var connectionString = Environment.GetEnvironmentVariable("NORA_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Console.WriteLine("SKIP agenda PostgreSQL: configure NORA_TEST_CONNECTION somente para uma base descartável.");
            Console.WriteLine($"{checks - failures}/{checks} verificações da agenda passaram.");
            return failures;
        }

        await using var f = await Fixture.StartAsync(connectionString);
        Guid cardiology = Guid.Empty, clinical = Guid.Empty, orthopedics = Guid.Empty, pediatrics = Guid.Empty;
        Guid ana = Guid.Empty, bruno = Guid.Empty, room1 = Guid.Empty, room2 = Guid.Empty, room3 = Guid.Empty;
        Guid tWed = Guid.Empty, tTue = Guid.Empty, tMon = Guid.Empty, tBruno = Guid.Empty, tSun = Guid.Empty;
        var u1 = f.Unit1; var u2 = f.Unit2; var u3 = f.Unit3;
        string Wed(string time) => $"2026-11-18T{time}:00Z";

        // ======== 1. Especialidades ========
        await Check("Especialidades: só a Secretaria mantém; nome único sem diferenciar maiúsculas/acentos/espaços; renomear, ativar e desativar sem excluir; OBS e paciente leem", async () =>
        {
            foreach (var client in new[] { f.Admin, f.Attendant, f.Patients[1] })
            {
                using var denied = await client.PostAsJsonAsync("/api/municipal/specialties", new { name = "Cardiologia" }); Expect(denied, 403);
                using var deniedPut = await client.PutAsJsonAsync($"/api/municipal/specialties/{Guid.NewGuid()}", new { name = "Qualquer" }); Expect(deniedPut, 403);
            }
            using (var anonymous = await f.Anonymous.PostAsJsonAsync("/api/municipal/specialties", new { name = "Cardiologia" })) Expect(anonymous, 401);
            Guid Id(JsonElement body) => body.GetProperty("id").GetGuid();
            using (var created = await f.Municipal.PostAsJsonAsync("/api/municipal/specialties", new { name = "  Cardiologia " })) { Expect(created, 201); var body = await Read(created); cardiology = Id(body); Require(body.GetProperty("name").GetString() == "Cardiologia" && body.GetProperty("isActive").GetBoolean(), "Especialidade criada incorreta."); }
            foreach (var duplicate in new[] { "cardiologia", "CARDIOLOGIA", "Cardiología", "  Cardiologia  " })
            { using var response = await f.Municipal.PostAsJsonAsync("/api/municipal/specialties", new { name = duplicate }); Expect(response, 409); }
            using (var short1 = await f.Municipal.PostAsJsonAsync("/api/municipal/specialties", new { name = "x" })) Expect(short1, 400);
            using (var missing = await f.Municipal.PostAsJsonAsync("/api/municipal/specialties", new { })) Expect(missing, 400);
            using (var c = await f.Municipal.PostAsJsonAsync("/api/municipal/specialties", new { name = "Clínica Geral" })) { Expect(c, 201); clinical = Id(await Read(c)); }
            using (var o = await f.Municipal.PostAsJsonAsync("/api/municipal/specialties", new { name = "Ortopedia" })) { Expect(o, 201); orthopedics = Id(await Read(o)); }
            using (var p = await f.Municipal.PostAsJsonAsync("/api/municipal/specialties", new { name = "Pediatria" })) { Expect(p, 201); pediatrics = Id(await Read(p)); }
            using (var clash = await f.Municipal.PutAsJsonAsync($"/api/municipal/specialties/{pediatrics}", new { name = "clinica geral" })) Expect(clash, 409);
            using (var rename = await f.Municipal.PutAsJsonAsync($"/api/municipal/specialties/{pediatrics}", new { name = "Pediatria Geral" })) { Expect(rename, 200); Require((await Read(rename)).GetProperty("name").GetString() == "Pediatria Geral", "Renomear não gravou."); }
            using (var sameName = await f.Municipal.PutAsJsonAsync($"/api/municipal/specialties/{pediatrics}", new { name = "PEDIATRIA GERAL" })) Expect(sameName, 200);
            using (var unknown = await f.Municipal.PutAsJsonAsync($"/api/municipal/specialties/{Guid.NewGuid()}", new { name = "Nada" })) Expect(unknown, 404);
            using (var off = await f.Municipal.PatchAsJsonAsync($"/api/municipal/specialties/{pediatrics}/active", new { isActive = false })) { Expect(off, 200); Require(!(await Read(off)).GetProperty("isActive").GetBoolean(), "Desativar não gravou."); }
            using (var noField = await f.Municipal.PatchAsJsonAsync($"/api/municipal/specialties/{pediatrics}/active", new { })) Expect(noField, 400);
            using (var delete = await f.Municipal.DeleteAsync($"/api/municipal/specialties/{pediatrics}")) Expect(delete, 405);
            using (var all = await f.Municipal.GetAsync("/api/municipal/specialties")) { Expect(all, 200); var items = (await Read(all)).GetProperty("specialties").EnumerateArray().ToList(); Require(items.Count == 4 && items.Any(i => i.GetProperty("id").GetGuid() == pediatrics && !i.GetProperty("isActive").GetBoolean()), "A lista da Secretaria deve trazer as inativas, sem excluir."); }
            using (var obsList = await f.Attendant.GetAsync("/api/obs/specialties")) { Expect(obsList, 200); Require((await Read(obsList)).GetProperty("specialties").GetArrayLength() == 4, "OBS lê a lista inteira com a situação."); }
            using (var patientList = await f.Patients[1].GetAsync("/api/patient/specialties")) { Expect(patientList, 200); var names = (await Read(patientList)).GetProperty("specialties").EnumerateArray().Select(i => i.GetProperty("name").GetString()).ToList(); Require(names.Count == 3 && !names.Contains("Pediatria Geral"), "O paciente só vê especialidades ativas."); }
            using (var patientDenied = await f.Patients[1].GetAsync("/api/obs/specialties")) Expect(patientDenied, 403);
            using (var anonymousRead = await f.Anonymous.GetAsync("/api/patient/specialties")) Expect(anonymousRead, 401);
        });

        // ======== 2. Profissionais e consultórios ========
        await Check("Profissionais e consultórios: administrador escreve, atendente só lê, Secretaria lê por UBS sem evento de acesso; validações do conselho, especialidades e nomes", async () =>
        {
            using (var denied = await f.Attendant.PostAsJsonAsync($"/api/obs/units/{u1}/professionals", new { fullName = "Dra. Ana Souza", specialtyIds = new[] { cardiology } })) Expect(denied, 403);
            using (var deniedRoom = await f.Attendant.PostAsJsonAsync($"/api/obs/units/{u1}/rooms", new { name = "Sala 1" })) Expect(deniedRoom, 403);
            using (var patientDenied = await f.Patients[1].PostAsJsonAsync($"/api/obs/units/{u1}/professionals", new { fullName = "X Y", specialtyIds = new[] { cardiology } })) Expect(patientDenied, 403);
            using (var managerDenied = await f.Municipal.PostAsJsonAsync($"/api/obs/units/{u1}/professionals", new { fullName = "X Y", specialtyIds = new[] { cardiology } })) Expect(managerDenied, 403);
            using (var notGranted = await f.Attendant.GetAsync($"/api/obs/units/{u2}/professionals")) Expect(notGranted, 403);
            foreach (var invalid in new object[]
            {
                new { fullName = "A", specialtyIds = new[] { cardiology } }, new { fullName = "Dra. Sem Especialidade", specialtyIds = Array.Empty<Guid>() }, new { fullName = "Dra. Desconhecida", specialtyIds = new[] { Guid.NewGuid() } },
                new { fullName = "Dra. Inativa", specialtyIds = new[] { pediatrics } }, new { fullName = "Dra. Conselho", specialtyIds = new[] { cardiology }, councilAcronym = "CRM", councilNumber = "", councilState = "" },
                new { fullName = "Dra. Conselho", specialtyIds = new[] { cardiology }, councilAcronym = "CRM", councilNumber = "123", councilState = "XX" }, new { fullName = "Dra. Conselho", specialtyIds = new[] { cardiology }, councilAcronym = "C", councilNumber = "123", councilState = "SC" }
            })
            { using var response = await f.Admin.PostAsJsonAsync($"/api/obs/units/{u1}/professionals", invalid); Expect(response, 400); }
            using (var created = await f.Admin.PostAsJsonAsync($"/api/obs/units/{u1}/professionals", new { fullName = "  Dra. Ana   Souza ", specialtyIds = new[] { cardiology, clinical }, councilAcronym = "crm", councilNumber = "12345", councilState = "sc" }))
            {
                Expect(created, 201); var body = await Read(created); ana = body.GetProperty("id").GetGuid();
                Require(body.GetProperty("fullName").GetString() == "Dra. Ana Souza" && body.GetProperty("councilAcronym").GetString() == "CRM" && body.GetProperty("councilState").GetString() == "SC" && body.GetProperty("specialtyIds").GetArrayLength() == 2 && body.GetProperty("isActive").GetBoolean(), "Profissional criado incorretamente.");
            }
            using (var optional = await f.Admin.PostAsJsonAsync($"/api/obs/units/{u1}/professionals", new { fullName = "Dr. Bruno Lima", specialtyIds = new[] { clinical } })) { Expect(optional, 201); var body = await Read(optional); bruno = body.GetProperty("id").GetGuid(); Require(body.GetProperty("councilAcronym").ValueKind == JsonValueKind.Null, "Conselho é opcional."); }
            using (var duplicate = await f.Admin.PostAsJsonAsync($"/api/obs/units/{u1}/professionals", new { fullName = "dr. bruno lima", specialtyIds = new[] { clinical } })) Expect(duplicate, 409);
            using (var update = await f.Admin.PutAsJsonAsync($"/api/obs/units/{u1}/professionals/{bruno}", new { fullName = "Dr. Bruno Lima Filho", specialtyIds = new[] { clinical } })) { Expect(update, 200); Require((await Read(update)).GetProperty("fullName").GetString() == "Dr. Bruno Lima Filho", "Atualização não gravou."); }
            using (var back = await f.Admin.PutAsJsonAsync($"/api/obs/units/{u1}/professionals/{bruno}", new { fullName = "Dr. Bruno Lima", specialtyIds = new[] { clinical } })) Expect(back, 200);
            using (var unknown = await f.Admin.PutAsJsonAsync($"/api/obs/units/{u1}/professionals/{Guid.NewGuid()}", new { fullName = "Dr. Fantasma", specialtyIds = new[] { clinical } })) Expect(unknown, 404);
            using (var attendantPatch = await f.Attendant.PatchAsJsonAsync($"/api/obs/units/{u1}/professionals/{bruno}/active", new { isActive = false })) Expect(attendantPatch, 403);
            using (var attendantRead = await f.Attendant.GetAsync($"/api/obs/units/{u1}/professionals")) { Expect(attendantRead, 200); Require((await Read(attendantRead)).GetProperty("professionals").GetArrayLength() == 2, "O atendente lê os profissionais."); }
            foreach (var (name, expected) in new[] { ("Sala 1", 201), ("Sala 2", 201), ("Sala 3", 201), ("sala 1", 409), (" ", 400) })
            {
                using var response = await f.Admin.PostAsJsonAsync($"/api/obs/units/{u1}/rooms", new { name }); Expect(response, expected);
                if (expected == 201) { var id = (await Read(response)).GetProperty("id").GetGuid(); if (name == "Sala 1") room1 = id; else if (name == "Sala 2") room2 = id; else room3 = id; }
            }
            using (var rename = await f.Admin.PutAsJsonAsync($"/api/obs/units/{u1}/rooms/{room3}", new { name = "Sala 3 (nova)" })) { Expect(rename, 200); Require((await Read(rename)).GetProperty("name").GetString() == "Sala 3 (nova)", "Renomear sala não gravou."); }
            using (var clash = await f.Admin.PutAsJsonAsync($"/api/obs/units/{u1}/rooms/{room3}", new { name = "SALA 2" })) Expect(clash, 409);
            using (var roomOff = await f.Admin.PatchAsJsonAsync($"/api/obs/units/{u1}/rooms/{room3}/active", new { isActive = false })) Expect(roomOff, 200);
            using (var roomOn = await f.Admin.PatchAsJsonAsync($"/api/obs/units/{u1}/rooms/{room3}/active", new { isActive = true })) Expect(roomOn, 200);
            using (var attendantRooms = await f.Attendant.GetAsync($"/api/obs/units/{u1}/rooms")) { Expect(attendantRooms, 200); Require((await Read(attendantRooms)).GetProperty("rooms").GetArrayLength() == 3, "O atendente lê os consultórios."); }
            var accessEvents = await f.ScalarAsync<long>("SELECT count(*) FROM nora.unit_access_events");
            using (var municipalProfessionals = await f.Municipal.GetAsync($"/api/municipal/units/{u1}/professionals")) { Expect(municipalProfessionals, 200); Require((await Read(municipalProfessionals)).GetProperty("professionals").GetArrayLength() == 2, "A Secretaria lê os profissionais."); }
            using (var municipalRooms = await f.Municipal.GetAsync($"/api/municipal/units/{u1}/rooms")) { Expect(municipalRooms, 200); Require((await Read(municipalRooms)).GetProperty("rooms").GetArrayLength() == 3, "A Secretaria lê os consultórios."); }
            using (var unknownUnit = await f.Municipal.GetAsync($"/api/municipal/units/{Guid.NewGuid()}/professionals")) Expect(unknownUnit, 404);
            using (var adminOnMunicipal = await f.Admin.GetAsync($"/api/municipal/units/{u1}/professionals")) Expect(adminOnMunicipal, 403);
            using (var patientOnMunicipal = await f.Patients[1].GetAsync($"/api/municipal/units/{u1}/rooms")) Expect(patientOnMunicipal, 403);
            Require(await f.ScalarAsync<long>("SELECT count(*) FROM nora.unit_access_events") == accessEvents, "A leitura do catálogo pela Secretaria não deve gerar evento de acesso a dado pessoal.");
        });

        await Check("Especialidade desativada já vinculada (C3): o PUT do profissional pode mantê-la e ela continua vinculada; vincular uma inativa nova no PUT ou qualquer inativa no POST → 400", async () =>
        {
            bool Has(JsonElement professional, Guid specialty) => professional.GetProperty("specialtyIds").EnumerateArray().Any(id => id.GetGuid() == specialty);
            using (var on = await f.Municipal.PatchAsJsonAsync($"/api/municipal/specialties/{pediatrics}/active", new { isActive = true })) Expect(on, 200);
            using (var linked = await f.Admin.PutAsJsonAsync($"/api/obs/units/{u1}/professionals/{bruno}", new { fullName = "Dr. Bruno Lima", specialtyIds = new[] { clinical, pediatrics } })) { Expect(linked, 200); Require(Has(await Read(linked), pediatrics), "Pré-condição: Pediatria vinculada enquanto ativa."); }
            using (var off = await f.Municipal.PatchAsJsonAsync($"/api/municipal/specialties/{pediatrics}/active", new { isActive = false })) Expect(off, 200);
            using (var keep = await f.Admin.PutAsJsonAsync($"/api/obs/units/{u1}/professionals/{bruno}", new { fullName = "Dr. Bruno Lima", specialtyIds = new[] { clinical, pediatrics } }))
            { Expect(keep, 200); var body = await Read(keep); Require(Has(body, pediatrics) && Has(body, clinical), $"A especialidade inativa já vinculada continua vinculada: {body}."); }
            using (var addInactive = await f.Admin.PutAsJsonAsync($"/api/obs/units/{u1}/professionals/{ana}", new { fullName = "Dra. Ana Souza", specialtyIds = new[] { cardiology, clinical, pediatrics }, councilAcronym = "CRM", councilNumber = "12345", councilState = "SC" })) Expect(addInactive, 400);
            using (var postInactive = await f.Admin.PostAsJsonAsync($"/api/obs/units/{u1}/professionals", new { fullName = "Dra. Nova Pediatra", specialtyIds = new[] { clinical, pediatrics } })) Expect(postInactive, 400);
            using (var list = await f.Admin.GetAsync($"/api/obs/units/{u1}/professionals"))
            {
                Expect(list, 200); var items = (await Read(list)).GetProperty("professionals").EnumerateArray().ToList();
                Require(items.Count == 2 && !Has(items.Single(p => p.GetProperty("id").GetGuid() == ana), pediatrics), "As recusas não gravam nada.");
            }
            using (var remove = await f.Admin.PutAsJsonAsync($"/api/obs/units/{u1}/professionals/{bruno}", new { fullName = "Dr. Bruno Lima", specialtyIds = new[] { clinical } })) { Expect(remove, 200); Require(!Has(await Read(remove), pediatrics), "Tirar a inativa continua permitido."); }
        });

        // ======== 3. Grade e bloqueios ========
        await Check("Grade semanal: criação, sobreposição do profissional e do consultório, vigência, duração, especialidade do profissional e perfis; bloqueios do profissional e da UBS", async () =>
        {
            async Task<HttpResponseMessage> Post(HttpClient client, Guid professional, int weekday, string start, string end, int minutes, Guid room, Guid specialty, string? from = null, string? until = null) =>
                await client.PostAsJsonAsync($"/api/obs/units/{u1}/schedule", new { professionalId = professional, weekday, startTime = start, endTime = end, slotMinutes = minutes, roomId = room, specialtyId = specialty, validFrom = from, validUntil = until });
            using (var denied = await Post(f.Attendant, ana, 3, "08:00", "10:00", 30, room1, cardiology)) Expect(denied, 403);
            using (var wed = await Post(f.Admin, ana, 3, "08:00", "10:00", 30, room1, cardiology)) { Expect(wed, 201); var body = await Read(wed); tWed = body.GetProperty("id").GetGuid(); Require(body.GetProperty("startTime").GetString() == "08:00" && body.GetProperty("slotMinutes").GetInt32() == 30 && body.GetProperty("weekday").GetInt32() == 3, "Linha da grade incorreta."); }
            // Sobreposições: mesmo profissional e mesmo consultório (outro profissional) são recusados; encostar não é sobrepor.
            using (var sameProfessional = await Post(f.Admin, ana, 3, "09:30", "10:30", 30, room2, clinical)) Expect(sameProfessional, 409);
            using (var sameRoom = await Post(f.Admin, bruno, 3, "09:30", "10:30", 30, room1, clinical)) Expect(sameRoom, 409);
            using (var inside = await Post(f.Admin, ana, 3, "08:30", "09:00", 30, room2, cardiology)) Expect(inside, 409);
            using (var touching = await Post(f.Admin, ana, 3, "10:00", "11:00", 30, room1, cardiology)) { Expect(touching, 201); var id = (await Read(touching)).GetProperty("id").GetGuid(); using var cleanup = await f.Admin.DeleteAsync($"/api/obs/units/{u1}/schedule/{id}"); Expect(cleanup, 204); }
            using (var otherDay = await Post(f.Admin, ana, 4, "08:00", "10:00", 30, room1, cardiology)) { Expect(otherDay, 201); var id = (await Read(otherDay)).GetProperty("id").GetGuid(); using var cleanup = await f.Admin.DeleteAsync($"/api/obs/units/{u1}/schedule/{id}"); Expect(cleanup, 204); }
            // Vigência: períodos que não se cruzam convivem; cruzados não.
            using (var january = await Post(f.Admin, ana, 5, "08:00", "09:00", 30, room1, cardiology, "2027-01-01", "2027-01-31")) { Expect(january, 201); }
            using (var february = await Post(f.Admin, ana, 5, "08:00", "09:00", 30, room1, cardiology, "2027-02-01", "2027-02-28")) { Expect(february, 201); }
            using (var crossing = await Post(f.Admin, ana, 5, "08:00", "09:00", 30, room1, cardiology, "2027-01-31", "2027-02-01")) Expect(crossing, 409);
            using (var openEnded = await Post(f.Admin, ana, 5, "08:00", "09:00", 30, room1, cardiology, "2027-03-01", null)) Expect(openEnded, 201);
            using (var overlapsOpen = await Post(f.Admin, ana, 5, "08:00", "09:00", 30, room1, cardiology, "2027-06-01", "2027-06-30")) Expect(overlapsOpen, 409);
            using (var list0 = await f.Admin.GetAsync($"/api/obs/units/{u1}/schedule?professionalId={ana}")) { Expect(list0, 200); foreach (var item in (await Read(list0)).GetProperty("schedule").EnumerateArray().Where(i => i.GetProperty("weekday").GetInt32() == 5)) { using var cleanup = await f.Admin.DeleteAsync($"/api/obs/units/{u1}/schedule/{item.GetProperty("id").GetGuid()}"); Expect(cleanup, 204); } }
            // Validações.
            foreach (var invalid in new (string Start, string End, int Minutes)[] { ("08:00", "08:00", 30), ("10:00", "08:00", 30), ("08:00", "10:00", 4), ("08:00", "10:00", 241), ("08:00", "08:20", 30), ("8:00", "10:00", 30), ("08:00", "25:00", 30) })
            { using var response = await Post(f.Admin, ana, 6, invalid.Start, invalid.End, invalid.Minutes, room1, cardiology); Expect(response, 400); }
            using (var weekday = await Post(f.Admin, ana, 7, "08:00", "09:00", 30, room1, cardiology)) Expect(weekday, 400);
            using (var wrongSpecialty = await Post(f.Admin, bruno, 6, "08:00", "09:00", 30, room1, cardiology)) Expect(wrongSpecialty, 400);
            using (var inactiveSpecialty = await Post(f.Admin, ana, 6, "08:00", "09:00", 30, room1, pediatrics)) Expect(inactiveSpecialty, 400);
            using (var unknownRoom = await Post(f.Admin, ana, 6, "08:00", "09:00", 30, Guid.NewGuid(), cardiology)) Expect(unknownRoom, 400);
            using (var unknownProfessional = await Post(f.Admin, Guid.NewGuid(), 6, "08:00", "09:00", 30, room1, cardiology)) Expect(unknownProfessional, 400);
            using (var badDates = await Post(f.Admin, ana, 6, "08:00", "09:00", 30, room1, cardiology, "2027-02-01", "2027-01-01")) Expect(badDates, 400);
            using (var badDate = await Post(f.Admin, ana, 6, "08:00", "09:00", 30, room1, cardiology, "01/02/2027", null)) Expect(badDate, 400);
            using (var missingFields = await f.Admin.PostAsJsonAsync($"/api/obs/units/{u1}/schedule", new { professionalId = ana })) Expect(missingFields, 400);
            // O restante da grade principal.
            using (var tue = await Post(f.Admin, ana, 2, "08:30", "10:00", 30, room1, cardiology)) { Expect(tue, 201); tTue = (await Read(tue)).GetProperty("id").GetGuid(); }
            using (var mon = await Post(f.Admin, ana, 1, "10:00", "12:00", 30, room1, clinical)) { Expect(mon, 201); tMon = (await Read(mon)).GetProperty("id").GetGuid(); }
            using (var wedBruno = await Post(f.Admin, bruno, 3, "08:00", "09:00", 30, room2, clinical)) { Expect(wedBruno, 201); tBruno = (await Read(wedBruno)).GetProperty("id").GetGuid(); }
            using (var sun = await Post(f.Admin, ana, 0, "09:00", "10:00", 30, room1, cardiology)) { Expect(sun, 201); tSun = (await Read(sun)).GetProperty("id").GetGuid(); }
            using (var update = await f.Admin.PutAsJsonAsync($"/api/obs/units/{u1}/schedule/{tWed}", new { professionalId = ana, weekday = 3, startTime = "08:00", endTime = "10:00", slotMinutes = 30, roomId = room1, specialtyId = cardiology })) Expect(update, 200);
            using (var conflictingUpdate = await f.Admin.PutAsJsonAsync($"/api/obs/units/{u1}/schedule/{tBruno}", new { professionalId = bruno, weekday = 3, startTime = "08:00", endTime = "09:00", slotMinutes = 30, roomId = room1, specialtyId = clinical })) Expect(conflictingUpdate, 409);
            using (var unknownUpdate = await f.Admin.PutAsJsonAsync($"/api/obs/units/{u1}/schedule/{Guid.NewGuid()}", new { professionalId = ana, weekday = 3, startTime = "08:00", endTime = "10:00", slotMinutes = 30, roomId = room1, specialtyId = cardiology })) Expect(unknownUpdate, 404);
            using (var attendantDelete = await f.Attendant.DeleteAsync($"/api/obs/units/{u1}/schedule/{tWed}")) Expect(attendantDelete, 403);
            using (var unknownDelete = await f.Admin.DeleteAsync($"/api/obs/units/{u1}/schedule/{Guid.NewGuid()}")) Expect(unknownDelete, 404);
            using (var attendantRead = await f.Attendant.GetAsync($"/api/obs/units/{u1}/schedule")) { Expect(attendantRead, 200); Require((await Read(attendantRead)).GetProperty("schedule").GetArrayLength() == 5, "Grade principal deveria ter cinco linhas."); }
            using (var municipalRead = await f.Municipal.GetAsync($"/api/municipal/units/{u1}/schedule?professionalId={bruno}")) { Expect(municipalRead, 200); Require((await Read(municipalRead)).GetProperty("schedule").GetArrayLength() == 1, "A Secretaria lê a grade filtrada por profissional."); }
            // Remover especialidade que ainda tem grade é recusado.
            using (var strip = await f.Admin.PutAsJsonAsync($"/api/obs/units/{u1}/professionals/{ana}", new { fullName = "Dra. Ana Souza", specialtyIds = new[] { clinical }, councilAcronym = "CRM", councilNumber = "12345", councilState = "SC" })) Expect(strip, 409);
            // Bloqueios.
            var blockWindow = $"from=2026-11-01T00:00:00Z&to=2026-12-01T00:00:00Z";
            using (var attendantBlock = await f.Attendant.PostAsJsonAsync($"/api/obs/units/{u1}/schedule-blocks", new { startsAt = Wed("11:30"), endsAt = Wed("12:00"), reason = "Feriado" })) Expect(attendantBlock, 403);
            foreach (var invalid in new object[] { new { startsAt = Wed("12:00"), endsAt = Wed("12:00"), reason = "Vazio" }, new { startsAt = Wed("12:30"), endsAt = Wed("12:00"), reason = "Invertido" }, new { startsAt = Wed("12:00"), endsAt = Wed("12:30"), reason = " " }, new { startsAt = Wed("12:00"), endsAt = Wed("12:30"), reason = "R", professionalId = Guid.NewGuid() }, new { startsAt = Wed("12:00") } })
            { using var response = await f.Admin.PostAsJsonAsync($"/api/obs/units/{u1}/schedule-blocks", invalid); Expect(response, 400); }
            using (var list = await f.Attendant.GetAsync($"/api/obs/units/{u1}/schedule-blocks?{blockWindow}")) { Expect(list, 200); Require((await Read(list)).GetProperty("blocks").GetArrayLength() == 0, "Ainda não há bloqueios."); }
            using (var badWindow = await f.Attendant.GetAsync($"/api/obs/units/{u1}/schedule-blocks?from=2026-12-01T00:00:00Z&to=2026-11-01T00:00:00Z")) Expect(badWindow, 400);
            using (var municipalBlocks = await f.Municipal.GetAsync($"/api/municipal/units/{u1}/schedule-blocks?{blockWindow}")) Expect(municipalBlocks, 200);
        });

        // ======== 4. Disponibilidade, equipe e bloqueios ========
        Guid blockAna = Guid.Empty, blockUnit = Guid.Empty, lightningAppointment = Guid.Empty, staffAppointmentP2 = Guid.Empty;

        async Task<List<JsonElement>> Slots(HttpClient client, Guid unit, string from, string to, Guid specialty, Guid? professional = null, string kind = "obs")
        {
            using var response = await client.GetAsync($"/api/{kind}/units/{unit}/availability?from={from}&to={to}&specialtyId={specialty}" + (professional is { } p ? $"&professionalId={p}" : ""));
            Expect(response, 200); return (await Read(response)).GetProperty("slots").EnumerateArray().ToList();
        }
        string StartOf(JsonElement slot) => slot.GetProperty("startsAt").GetDateTimeOffset().ToUniversalTime().ToString("HH:mm", CultureInfo.InvariantCulture);
        const string WedFrom = "2026-11-18T00:00:00Z", WedTo = "2026-11-19T00:00:00Z";

        await Check("Disponibilidade: grade do dia no fuso local, duração, filtro por especialidade e profissional; exclui consulta, bloqueio (do profissional e da UBS) e vaga relâmpago", async () =>
        {
            var cardio = await Slots(f.Attendant, u1, WedFrom, WedTo, cardiology);
            Require(cardio.Select(StartOf).SequenceEqual(["11:00", "11:30", "12:00", "12:30"]) && cardio.All(s => s.GetProperty("professionalId").GetGuid() == ana && s.GetProperty("specialtyName").GetString() == "Cardiologia" && s.GetProperty("roomId").GetGuid() == room1 && s.GetProperty("roomName").GetString() == "Sala 1" &&
                (s.GetProperty("endsAt").GetDateTimeOffset() - s.GetProperty("startsAt").GetDateTimeOffset()) == TimeSpan.FromMinutes(30)), "Cardiologia de quarta deveria ter quatro atendimentos de 30 min às 08:00..09:30 locais (11:00Z a 12:30Z).");
            Require((await Slots(f.Attendant, u1, WedFrom, WedTo, clinical)).Select(StartOf).SequenceEqual(["11:00", "11:30"]), "Clínica geral de quarta é só a do Bruno.");
            Require((await Slots(f.Attendant, u1, WedFrom, WedTo, clinical, bruno)).Count == 2 && (await Slots(f.Attendant, u1, WedFrom, WedTo, clinical, ana)).Count == 0 && (await Slots(f.Attendant, u1, WedFrom, WedTo, cardiology, bruno)).Count == 0, "Filtro por profissional incorreto.");
            Require((await Slots(f.Attendant, u1, "2026-11-18T11:30:00Z", "2026-11-18T12:00:00Z", cardiology)).Select(StartOf).SequenceEqual(["11:30"]), "Janela [from, to) incorreta.");
            Require((await Slots(f.Attendant, u1, WedFrom, WedTo, orthopedics)).Count == 0 && (await Slots(f.Attendant, u1, WedFrom, WedTo, pediatrics)).Count == 0, "Especialidade sem grade ou inativa não oferece horário.");
            // Parâmetros inválidos.
            foreach (var query in new[] { $"from={WedFrom}&to={WedTo}", $"from={WedTo}&to={WedFrom}&specialtyId={cardiology}", $"from=2026-11-18T00:00:00Z&to=2027-03-01T00:00:00Z&specialtyId={cardiology}", $"from={WedFrom}&to={WedTo}&specialtyId={Guid.NewGuid()}", $"from={WedFrom}&to={WedTo}&specialtyId={cardiology}&professionalId={Guid.Empty}" })
            { using var response = await f.Attendant.GetAsync($"/api/obs/units/{u1}/availability?{query}"); Expect(response, 400); }
            using (var missingFrom = await f.Attendant.GetAsync($"/api/obs/units/{u1}/availability?to={WedTo}&specialtyId={cardiology}")) Expect(missingFrom, 400);
            using (var notGranted = await f.Attendant.GetAsync($"/api/obs/units/{u2}/availability?from={WedFrom}&to={WedTo}&specialtyId={cardiology}")) Expect(notGranted, 403);
            // Consulta da equipe tira o atendimento da lista (e exige o catálogo na UBS).
            using (var text = await f.Attendant.PostAsJsonAsync($"/api/obs/units/{u1}/appointments", new { cpf = Cpf(2), specialty = "Cardiologia", professional = "Dra. Ana Souza", startsAt = Wed("11:00"), endsAt = Wed("11:30") })) await ExpectProblem(text, 400, "catalog-required");
            using (var onlyProfessional = await f.Attendant.PostAsJsonAsync($"/api/obs/units/{u1}/appointments", new { cpf = Cpf(2), startsAt = Wed("11:00"), endsAt = Wed("11:30"), professionalId = ana })) await ExpectProblem(onlyProfessional, 400, "catalog-required");
            using (var offGrade = await f.Attendant.PostAsJsonAsync($"/api/obs/units/{u1}/appointments", new { cpf = Cpf(2), startsAt = Wed("11:15"), professionalId = ana, specialtyId = cardiology })) await ExpectProblem(offGrade, 409, "slot-unavailable");
            using (var wrongProfessional = await f.Attendant.PostAsJsonAsync($"/api/obs/units/{u1}/appointments", new { cpf = Cpf(2), startsAt = Wed("11:00"), professionalId = bruno, specialtyId = cardiology })) await ExpectProblem(wrongProfessional, 409, "slot-unavailable");
            using (var wrongEnd = await f.Attendant.PostAsJsonAsync($"/api/obs/units/{u1}/appointments", new { cpf = Cpf(2), startsAt = Wed("11:00"), endsAt = Wed("11:45"), professionalId = ana, specialtyId = cardiology })) Expect(wrongEnd, 400);
            using (var inactiveSpecialty = await f.Attendant.PostAsJsonAsync($"/api/obs/units/{u1}/appointments", new { cpf = Cpf(2), startsAt = Wed("11:00"), professionalId = ana, specialtyId = pediatrics })) Expect(inactiveSpecialty, 400);
            using (var unknownProfessional = await f.Attendant.PostAsJsonAsync($"/api/obs/units/{u1}/appointments", new { cpf = Cpf(2), startsAt = Wed("11:00"), professionalId = Guid.NewGuid(), specialtyId = cardiology })) Expect(unknownProfessional, 400);
            using (var unknownPatient = await f.Attendant.PostAsJsonAsync($"/api/obs/units/{u1}/appointments", new { cpf = Cpf(99), startsAt = Wed("11:00"), professionalId = ana, specialtyId = cardiology })) Expect(unknownPatient, 404);
            using (var booked = await f.Attendant.PostAsJsonAsync($"/api/obs/units/{u1}/appointments", new { cpf = Cpf(2), startsAt = Wed("11:00"), endsAt = Wed("11:30"), professionalId = ana, specialtyId = cardiology }))
            {
                Expect(booked, 201); var body = await Read(booked); staffAppointmentP2 = body.GetProperty("id").GetGuid();
                Require(body.GetProperty("bookedBy").GetString() == "staff" && body.GetProperty("confirmedAt").ValueKind == JsonValueKind.Null && body.GetProperty("professional").GetString() == "Dra. Ana Souza" && body.GetProperty("specialty").GetString() == "Cardiologia" && body.GetProperty("professionalId").GetGuid() == ana, "Consulta da equipe incorreta.");
                Require(await f.UnitScalarAsync<string>(u1, "SELECT source FROM nora_unit.appointment_events WHERE appointment_id=$1", staffAppointmentP2) == "staff", "Evento de origem da equipe.");
            }
            using (var again = await f.Attendant.PostAsJsonAsync($"/api/obs/units/{u1}/appointments", new { cpf = Cpf(3), startsAt = Wed("11:00"), professionalId = ana, specialtyId = cardiology })) await ExpectProblem(again, 409, "slot-unavailable");
            Require((await Slots(f.Attendant, u1, WedFrom, WedTo, cardiology)).Select(StartOf).SequenceEqual(["11:30", "12:00", "12:30"]), "A consulta deveria tirar o atendimento da lista.");
            // O mesmo paciente não marca dois atendimentos que se cruzam em UBS com o mesmo horário de outro profissional.
            using (var overlapPatient = await f.Attendant.PostAsJsonAsync($"/api/obs/units/{u1}/appointments", new { cpf = Cpf(2), startsAt = Wed("11:00"), professionalId = bruno, specialtyId = clinical })) await ExpectProblem(overlapPatient, 409, "patient-overlap");
            // Vaga relâmpago aberta segura o horário: P3 marca 11:30, cancela e a vaga fica aberta.
            using (var p3 = await f.Attendant.PostAsJsonAsync($"/api/obs/units/{u1}/appointments", new { cpf = Cpf(3), startsAt = Wed("11:30"), professionalId = ana, specialtyId = cardiology })) { Expect(p3, 201); lightningAppointment = (await Read(p3)).GetProperty("id").GetGuid(); }
            using (var cancel = await f.Attendant.PatchAsJsonAsync($"/api/obs/units/{u1}/appointments/{lightningAppointment}/status", new { status = "cancelled", confirmed = true })) Expect(cancel, 200);
            Require((await Slots(f.Attendant, u1, WedFrom, WedTo, cardiology)).Select(StartOf).SequenceEqual(["11:30", "12:00", "12:30"]), "Consulta cancelada libera o horário.");
            using (var slot = await f.Attendant.PostAsJsonAsync($"/api/obs/units/{u1}/lightning-slots", new { appointmentId = lightningAppointment, confirmed = true })) Expect(slot, 201);
            Require((await Slots(f.Attendant, u1, WedFrom, WedTo, cardiology)).Select(StartOf).SequenceEqual(["12:00", "12:30"]), "Vaga relâmpago aberta deveria segurar o horário.");
            using (var heldBooking = await f.Attendant.PostAsJsonAsync($"/api/obs/units/{u1}/appointments", new { cpf = Cpf(6), startsAt = Wed("11:30"), professionalId = ana, specialtyId = cardiology })) await ExpectProblem(heldBooking, 409, "slot-unavailable");
            // Bloqueios.
            using (var profBlock = await f.Admin.PostAsJsonAsync($"/api/obs/units/{u1}/schedule-blocks", new { professionalId = ana, startsAt = Wed("12:30"), endsAt = Wed("13:00"), reason = "Congresso" })) { Expect(profBlock, 201); blockAna = (await Read(profBlock)).GetProperty("id").GetGuid(); }
            Require((await Slots(f.Attendant, u1, WedFrom, WedTo, cardiology)).Select(StartOf).SequenceEqual(["12:00"]), "Bloqueio do profissional deveria tirar o atendimento.");
            Require((await Slots(f.Attendant, u1, WedFrom, WedTo, clinical)).Count == 2, "O bloqueio do profissional não afeta o outro.");
            using (var unitBlock = await f.Admin.PostAsJsonAsync($"/api/obs/units/{u1}/schedule-blocks", new { startsAt = Wed("11:30"), endsAt = Wed("12:00"), reason = "Feriado" })) { Expect(unitBlock, 201); blockUnit = (await Read(unitBlock)).GetProperty("id").GetGuid(); }
            Require((await Slots(f.Attendant, u1, WedFrom, WedTo, clinical)).Select(StartOf).SequenceEqual(["11:00"]), "Bloqueio da UBS inteira deveria tirar o atendimento de todos.");
            using (var blocks = await f.Attendant.GetAsync($"/api/obs/units/{u1}/schedule-blocks?from=2026-11-18T00:00:00Z&to=2026-11-19T00:00:00Z")) { Expect(blocks, 200); var items = (await Read(blocks)).GetProperty("blocks").EnumerateArray().ToList(); Require(items.Count == 2 && items.Count(i => i.GetProperty("professionalId").ValueKind == JsonValueKind.Null) == 1, "Lista de bloqueios incorreta."); }
            using (var bookBlocked = await f.Attendant.PostAsJsonAsync($"/api/obs/units/{u1}/appointments", new { cpf = Cpf(6), startsAt = Wed("12:30"), professionalId = ana, specialtyId = cardiology })) await ExpectProblem(bookBlocked, 409, "slot-unavailable");
            // Bloqueio parcial: um bloqueio de 10 minutos dentro do atendimento já o tira (sobreposição, não só início).
            using (var tiny = await f.Admin.PostAsJsonAsync($"/api/obs/units/{u1}/schedule-blocks", new { professionalId = bruno, startsAt = Wed("11:10"), endsAt = Wed("11:20"), reason = "Reunião" })) { Expect(tiny, 201); var id = (await Read(tiny)).GetProperty("id").GetGuid();
                Require((await Slots(f.Attendant, u1, WedFrom, WedTo, clinical)).Count == 0, "Bloqueio dentro do atendimento deveria tirá-lo.");
                using var cleanup = await f.Admin.DeleteAsync($"/api/obs/units/{u1}/schedule-blocks/{id}"); Expect(cleanup, 204); }
            Require((await Slots(f.Attendant, u1, WedFrom, WedTo, clinical)).Count == 1, "Remover o bloqueio devolve o atendimento.");
            using (var unknownBlock = await f.Admin.DeleteAsync($"/api/obs/units/{u1}/schedule-blocks/{Guid.NewGuid()}")) Expect(unknownBlock, 404);
            using (var attendantDeleteBlock = await f.Attendant.DeleteAsync($"/api/obs/units/{u1}/schedule-blocks/{blockAna}")) Expect(attendantDeleteBlock, 403);
        });

        await Check("Capacidade nos indicadores (OBS, Secretaria, byUnit, rede, região e filtro de especialidade) e metas ausentes; consultórios ativos (em uso e previstos hoje)", async () =>
        {
            // Quarta: Ana 11:00 (consulta), 11:30 (bloqueio UBS), 12:00 (livre), 12:30 (bloqueio da Ana); Bruno 11:00 (livre), 11:30 (bloqueio UBS) → ofertados 3, ocupados 1.
            using (var response = await f.Attendant.GetAsync($"/api/obs/units/{u1}/metrics?from={WedFrom}&to={WedTo}"))
            {
                Expect(response, 200); var body = await Read(response); var capacity = body.GetProperty("capacity");
                Require(capacity.GetProperty("offeredSlots").GetInt64() == 3 && capacity.GetProperty("bookedSlots").GetInt64() == 1 && Math.Abs(capacity.GetProperty("occupancy").GetDouble() - 0.3333) < 1e-9, $"Capacidade da UBS incorreta: {capacity}.");
                Require(body.GetProperty("goals").GetArrayLength() == 0 && body.TryGetProperty("appointments", out _) && body.TryGetProperty("waiting", out _) && body.TryGetProperty("bySpecialty", out _) && body.TryGetProperty("daily", out _), "Campos antigos preservados e metas vazias sem meta definida.");
            }
            using (var empty = await f.Attendant.GetAsync($"/api/obs/units/{u1}/metrics?from=2026-12-19T00:00:00Z&to=2026-12-20T00:00:00Z")) { Expect(empty, 200); var capacity = (await Read(empty)).GetProperty("capacity"); Require(capacity.GetProperty("offeredSlots").GetInt64() == 0 && capacity.GetProperty("occupancy").ValueKind == JsonValueKind.Null, "Sem grade no período a ocupação é nula."); }
            using (var network = await f.Municipal.GetAsync($"/api/municipal/metrics?from={WedFrom}&to={WedTo}"))
            {
                Expect(network, 200); var body = await Read(network);
                Require(body.GetProperty("capacity").GetProperty("offeredSlots").GetInt64() == 3 && body.GetProperty("capacity").GetProperty("bookedSlots").GetInt64() == 1, "Capacidade da rede incorreta.");
                var u1Row = body.GetProperty("byUnit").EnumerateArray().Single(u => u.GetProperty("unitId").GetGuid() == u1); var u2Row = body.GetProperty("byUnit").EnumerateArray().Single(u => u.GetProperty("unitId").GetGuid() == u2);
                Require(u1Row.GetProperty("capacity").GetProperty("offeredSlots").GetInt64() == 3 && u2Row.GetProperty("capacity").GetProperty("offeredSlots").GetInt64() == 0 && u2Row.GetProperty("capacity").GetProperty("occupancy").ValueKind == JsonValueKind.Null, "byUnit sem capacidade correta.");
                Require(body.GetProperty("byRegion").EnumerateArray().All(r => r.TryGetProperty("capacity", out _)) && body.GetProperty("goals").GetArrayLength() == 0, "byRegion com capacidade e sem metas.");
            }
            using (var filtered = await f.Municipal.GetAsync($"/api/municipal/metrics?from={WedFrom}&to={WedTo}&specialty=cardiología")) { Expect(filtered, 200); var capacity = (await Read(filtered)).GetProperty("capacity"); Require(capacity.GetProperty("offeredSlots").GetInt64() == 2 && capacity.GetProperty("bookedSlots").GetInt64() == 1, "Filtro de especialidade na capacidade."); }
            using (var none = await f.Municipal.GetAsync($"/api/municipal/metrics?from={WedFrom}&to={WedTo}&specialty=Inexistente")) { Expect(none, 200); Require((await Read(none)).GetProperty("capacity").GetProperty("offeredSlots").GetInt64() == 0, "Especialidade desconhecida não oferta."); }
            using (var byUnit = await f.Municipal.GetAsync($"/api/municipal/metrics?from={WedFrom}&to={WedTo}&unitId={u2}")) { Expect(byUnit, 200); Require((await Read(byUnit)).GetProperty("capacity").GetProperty("offeredSlots").GetInt64() == 0, "Filtro por UBS na capacidade."); }
            // Consultórios ativos: segunda-feira (hoje) só a Sala 1 tem grade; nenhuma consulta chamada ainda.
            using (var before = await f.Attendant.GetAsync($"/api/obs/units/{u1}/active-rooms")) { Expect(before, 200); var body = await Read(before); Require(body.GetProperty("roomsInUse").GetInt32() == 0 && body.GetProperty("roomsPlannedToday").GetInt32() == 1, $"Consultórios hoje incorretos: {body}."); }
            using (var booked = await f.Attendant.PostAsJsonAsync($"/api/obs/units/{u1}/appointments", new { cpf = Cpf(3), startsAt = "2026-11-16T13:00:00Z", professionalId = ana, specialtyId = clinical })) { Expect(booked, 201); var id = (await Read(booked)).GetProperty("id").GetGuid(); using var called = await f.Attendant.PatchAsJsonAsync($"/api/obs/units/{u1}/appointments/{id}/status", new { status = "called", confirmed = false }); Expect(called, 200); }
            using (var after = await f.Attendant.GetAsync($"/api/obs/units/{u1}/active-rooms")) { Expect(after, 200); var body = await Read(after); Require(body.GetProperty("roomsInUse").GetInt32() == 1 && body.GetProperty("roomsPlannedToday").GetInt32() == 1, $"Consultório em uso incorreto: {body}."); }
            using (var denied = await f.Patients[1].GetAsync($"/api/obs/units/{u1}/active-rooms")) Expect(denied, 403);
        });

        // ======== 5. UBS sem profissional: texto livre continua ========
        await Check("UBS sem profissional ativo mantém o texto livre; consultas antigas continuam com o texto; ao cadastrar o primeiro profissional passa a exigir o catálogo, e voltar a texto livre sem profissional ativo", async () =>
        {
            Require(!await f.UsesCatalogAsync(u2), "A UBS 2 ainda não tem profissional.");
            using (var legacy = await f.Admin.PostAsJsonAsync($"/api/obs/units/{u2}/appointments", new { cpf = Cpf(1), specialty = "Cardiologia", professional = "Dr. Antigo", startsAt = "2026-11-20T13:00:00Z", endsAt = "2026-11-20T13:30:00Z" }))
            { Expect(legacy, 201); var body = await Read(legacy); Require(body.GetProperty("professional").GetString() == "Dr. Antigo" && body.GetProperty("bookedBy").GetString() == "staff" && body.GetProperty("professionalId").ValueKind == JsonValueKind.Null, "Consulta de texto livre incorreta."); }
            using (var extraFields = await f.Admin.PostAsJsonAsync($"/api/obs/units/{u2}/appointments", new { cpf = Cpf(1), specialty = "Clínica", professional = "Dr. Antigo", startsAt = "2026-11-21T13:00:00Z", endsAt = "2026-11-21T13:30:00Z", professionalId = Guid.NewGuid() })) Expect(extraFields, 201);
            using (var carla = await f.Admin.PostAsJsonAsync($"/api/obs/units/{u2}/professionals", new { fullName = "Dra. Carla Reis", specialtyIds = new[] { cardiology, clinical } })) Expect(carla, 201);
            using (var room = await f.Admin.PostAsJsonAsync($"/api/obs/units/{u2}/rooms", new { name = "Sala A" })) { Expect(room, 201); f.RoomA = (await Read(room)).GetProperty("id").GetGuid(); }
            f.Carla = await f.ScalarUnitAsync<Guid>(u2, "SELECT id FROM nora_unit.professionals WHERE full_name='Dra. Carla Reis'");
            Require(await f.UsesCatalogAsync(u2), "Com profissional ativo a UBS usa o catálogo.");
            using (var text = await f.Admin.PostAsJsonAsync($"/api/obs/units/{u2}/appointments", new { cpf = Cpf(1), specialty = "Cardiologia", professional = "Dr. Antigo", startsAt = "2026-11-22T13:00:00Z", endsAt = "2026-11-22T13:30:00Z" })) await ExpectProblem(text, 400, "catalog-required");
            using (var list = await f.Admin.GetAsync($"/api/obs/units/{u2}/appointments?from=2026-11-01T00:00:00Z&to=2026-12-31T00:00:00Z")) { Expect(list, 200); var items = (await Read(list)).EnumerateArray().ToList(); Require(items.Count == 2 && items.Any(i => i.GetProperty("professional").GetString() == "Dr. Antigo" && i.GetProperty("specialty").GetString() == "Cardiologia"), "Consultas antigas devem continuar com o texto que tinham."); }
            using (var deactivate = await f.Admin.PatchAsJsonAsync($"/api/obs/units/{u2}/professionals/{f.Carla}/active", new { isActive = false })) Expect(deactivate, 200);
            Require(!await f.UsesCatalogAsync(u2), "Sem profissional ativo volta ao texto livre.");
            using (var legacyAgain = await f.Admin.PostAsJsonAsync($"/api/obs/units/{u2}/appointments", new { cpf = Cpf(1), specialty = "Pediatria", professional = "Dr. Antigo", startsAt = "2026-11-23T13:00:00Z", endsAt = "2026-11-23T13:30:00Z" })) Expect(legacyAgain, 201);
            using (var activate = await f.Admin.PatchAsJsonAsync($"/api/obs/units/{u2}/professionals/{f.Carla}/active", new { isActive = true })) Expect(activate, 200);
            using (var template = await f.Admin.PostAsJsonAsync($"/api/obs/units/{u2}/schedule", new { professionalId = f.Carla, weekday = 0, startTime = "09:00", endTime = "10:00", slotMinutes = 30, roomId = f.RoomA, specialtyId = cardiology })) Expect(template, 201);
            using (var tuesday = await f.Admin.PostAsJsonAsync($"/api/obs/units/{u2}/schedule", new { professionalId = f.Carla, weekday = 2, startTime = "09:00", endTime = "10:00", slotMinutes = 30, roomId = f.RoomA, specialtyId = clinical })) Expect(tuesday, 201);
        });

        // ======== 6. Agendamento pelo paciente ========
        await Check("Paciente: disponibilidade de qualquer horário livre e futuro, sem antecedência mínima nem máxima; outro paciente, UBS não vinculada e sem sessão são recusados", async () =>
        {
            // Terça: 08:30 (11:30Z, 23,5 h à frente), 09:00 (12:00Z = exatamente 24 h) e 09:30. Paciente e equipe veem os três: não há antecedência mínima.
            const string tueFrom = "2026-11-17T00:00:00Z", tueTo = "2026-11-18T00:00:00Z";
            Require((await Slots(f.Attendant, u1, tueFrom, tueTo, cardiology)).Select(StartOf).SequenceEqual(["11:30", "12:00", "12:30"]), "A equipe vê o atendimento de 23,5 h.");
            Require((await Slots(f.Patients[1], u1, tueFrom, tueTo, cardiology, null, "patient")).Select(StartOf).SequenceEqual(["11:30", "12:00", "12:30"]), "O paciente vê o mesmo que a equipe, inclusive o atendimento de 23,5 h.");
            // Domingo 2027-02-14 09:00 local = 12:00Z = agora + 90 dias exatos: o paciente vê o atendimento dessa data e o seguinte (não há antecedência máxima).
            Require((await Slots(f.Patients[1], u1, "2027-02-14T00:00:00Z", "2027-02-15T00:00:00Z", cardiology, null, "patient")).Select(StartOf).SequenceEqual(["12:00", "12:30"]), "O paciente vê os atendimentos de 90 dias e não só o primeiro.");
            Require((await Slots(f.Attendant, u1, "2027-02-14T00:00:00Z", "2027-02-15T00:00:00Z", cardiology)).Count == 2, "A equipe também não tem limite de 90 dias na disponibilidade.");
            // 498 dias depois de T0 é uma terça-feira (2028-03-28): o paciente vê os mesmos atendimentos que a equipe, quase 500 dias adiante.
            var farDay = T0.AddDays(498); Require(farDay.DayOfWeek == DayOfWeek.Tuesday, "498 dias depois de T0 deveria ser terça-feira.");
            string farFrom = farDay.ToString("yyyy-MM-dd") + "T00:00:00Z", farTo = farDay.AddDays(1).ToString("yyyy-MM-dd") + "T00:00:00Z";
            var farStaff = (await Slots(f.Attendant, u1, farFrom, farTo, cardiology)).Select(StartOf).ToList();
            Require(farStaff.Count > 0 && (await Slots(f.Patients[1], u1, farFrom, farTo, cardiology, null, "patient")).Select(StartOf).SequenceEqual(farStaff), "Quase 500 dias à frente o paciente vê os atendimentos livres, como a equipe.");
            using (var other = await f.Patients[2].GetAsync($"/api/patient/units/{u2}/availability?from={tueFrom}&to={tueTo}&specialtyId={cardiology}")) Expect(other, 403);
            using (var anonymous = await f.Anonymous.GetAsync($"/api/patient/units/{u1}/availability?from={tueFrom}&to={tueTo}&specialtyId={cardiology}")) Expect(anonymous, 401);
            using (var staff = await f.Attendant.GetAsync($"/api/patient/units/{u1}/availability?from={tueFrom}&to={tueTo}&specialtyId={cardiology}")) Expect(staff, 403);
            using (var badRequest = await f.Patients[1].GetAsync($"/api/patient/units/{u1}/availability?from={tueTo}&to={tueFrom}&specialtyId={cardiology}")) Expect(badRequest, 400);
            // Teto de data (20 anos): um intervalo curto em 31/12/9999 estourava o limite de DateTime na grade (500); agora é 400 para equipe e paciente.
            foreach (var kind in new[] { "obs", "patient" })
            { using var beyond = await (kind == "obs" ? f.Attendant : f.Patients[1]).GetAsync($"/api/{kind}/units/{u1}/availability?from=9999-12-01T00:00:00Z&to=9999-12-31T12:00:00Z&specialtyId={cardiology}"); Expect(beyond, 400); }
            // Atendimento ocupado some também para o paciente.
            Require((await Slots(f.Patients[1], u1, WedFrom, WedTo, cardiology, null, "patient")).Select(StartOf).SequenceEqual(["12:00"]), "O paciente vê o mesmo que a equipe.");
        });


        Task<HttpResponseMessage> Book(int patient, Guid unit, Guid professional, Guid specialty, string startsAt) =>
            f.Patients[patient].PostAsJsonAsync($"/api/patient/units/{unit}/appointments", new { professionalId = professional, specialtyId = specialty, startsAt });

        await Check("Paciente agenda: vale na hora, source patient, só o passado é recusado (sem antecedência mínima nem máxima), uma por especialidade na rede, outra UBS, cancelamento libera e consultas da equipe não contam", async () =>
        {
            // Só o passado é recusado pela janela: um horário que já passou ou que começa agora (T0 = 12:00Z). Um horário um segundo adiante já passa pela janela e é julgado pela grade.
            using (var past = await Book(1, u1, ana, cardiology, "2026-11-16T11:30:00Z")) await ExpectProblem(past, 409, "booking-window");
            using (var startsNow = await Book(1, u1, ana, cardiology, "2026-11-16T12:00:00Z")) await ExpectProblem(startsNow, 409, "booking-window");
            using (var oneSecondAhead = await Book(1, u1, ana, cardiology, "2026-11-16T12:00:01Z")) await ExpectProblem(oneSecondAhead, 409, "slot-unavailable");
            Require(await f.UnitScalarAsync<long>(u1, "SELECT count(*) FROM nora_unit.appointments WHERE booked_by='patient'") == 0, "Recusas não gravam consulta.");
            Guid tueAppointment;
            using (var tue = await Book(1, u1, ana, cardiology, "2026-11-17T12:00:00Z"))
            {
                Expect(tue, 201); var body = await Read(tue); tueAppointment = body.GetProperty("id").GetGuid();
                Require(body.GetProperty("bookedBy").GetString() == "patient" && body.GetProperty("status").GetString() == "scheduled" && body.GetProperty("cpf").GetString() == Cpf(1) && body.GetProperty("confirmedAt").ValueKind == JsonValueKind.Null && body.GetProperty("professionalId").GetGuid() == ana, "Consulta do paciente incorreta.");
            }
            Require(await f.UnitScalarAsync<string>(u1, "SELECT source FROM nora_unit.appointment_events WHERE appointment_id=$1", tueAppointment) == "patient" && await f.UnitScalarAsync<Guid>(u1, "SELECT room_id FROM nora_unit.appointments WHERE id=$1", tueAppointment) == room1, "Evento 'patient' e consultório da consulta.");
            Require((await Slots(f.Patients[2], u1, "2026-11-17T00:00:00Z", "2026-11-18T00:00:00Z", cardiology, null, "patient")).Select(StartOf).SequenceEqual(["11:30", "12:30"]), "O horário reservado (12:00) sai da lista dos demais pacientes e o de 11:30, antes escondido por estar a menos de 24 h, aparece.");
            // Limite: P1 já tem Cardiologia marcada pelo app; outra na mesma UBS e na outra UBS é recusada; outra especialidade passa.
            using (var second = await Book(1, u1, ana, cardiology, "2026-11-17T12:30:00Z")) await ExpectProblem(second, 409, "specialty-limit");
            using (var otherUnit = await Book(1, u2, f.Carla, cardiology, "2026-11-22T12:00:00Z")) await ExpectProblem(otherUnit, 409, "specialty-limit");
            // A mesma pessoa não pode ter dois atendimentos no mesmo horário em UBS diferentes (conflito entre UBS): a outra especialidade, no mesmo horário da consulta da UBS 1, é recusada sem dizer qual UBS.
            using (var otherSpecialty = await Book(1, u2, f.Carla, clinical, "2026-11-17T12:00:00Z")) await ExpectProblem(otherSpecialty, 409, "other-unit-overlap");
            // Cancelar libera a especialidade na rede toda.
            using (var cancel = await f.Patients[1].PostAsJsonAsync($"/api/patient/units/{u1}/appointments/{tueAppointment}/cancel", new { confirmed = true })) Expect(cancel, 200);
            using (var afterCancel = await Book(1, u2, f.Carla, cardiology, "2026-11-22T12:00:00Z")) Expect(afterCancel, 201);
            using (var blockedAgain = await Book(1, u1, ana, cardiology, "2026-11-22T12:30:00Z")) await ExpectProblem(blockedAgain, 409, "specialty-limit");
            // Consulta marcada pela equipe não conta no limite: P2 tem Cardiologia da equipe (quarta 11:00) e marca pelo app.
            using (var staffDoesNotCount = await Book(2, u1, ana, cardiology, "2026-11-29T12:00:00Z")) Expect(staffDoesNotCount, 201);
            // Sem antecedência mínima: 11:30Z da terça (23,5 h à frente) vale. Sem antecedência máxima: 12:30Z do domingo 2027-02-14 (mais de 90 dias) e uma terça a 498 dias (quase 500) valem.
            using (var soonEnough = await Book(12, u1, ana, cardiology, "2026-11-17T11:30:00Z")) Expect(soonEnough, 201);
            using (var beyondNinety = await Book(3, u1, ana, cardiology, "2027-02-14T12:30:00Z")) Expect(beyondNinety, 201);
            using (var nearlyFiveHundred = await Book(13, u1, ana, cardiology, T0.AddDays(498).ToString("yyyy-MM-dd") + "T12:00:00Z")) Expect(nearlyFiveHundred, 201);
            // Autorização e validação.
            using (var notLinked = await Book(2, u2, f.Carla, clinical, "2026-11-17T12:30:00Z")) Expect(notLinked, 403);
            using (var anonymous = await f.Anonymous.PostAsJsonAsync($"/api/patient/units/{u1}/appointments", new { professionalId = ana, specialtyId = cardiology, startsAt = "2026-11-24T12:00:00Z" })) Expect(anonymous, 401);
            using (var staff = await f.Attendant.PostAsJsonAsync($"/api/patient/units/{u1}/appointments", new { professionalId = ana, specialtyId = cardiology, startsAt = "2026-11-24T12:00:00Z" })) Expect(staff, 403);
            using (var missing = await f.Patients[4].PostAsJsonAsync($"/api/patient/units/{u1}/appointments", new { specialtyId = cardiology, startsAt = "2026-11-24T12:00:00Z" })) Expect(missing, 400);
            using (var unknownSpecialty = await Book(4, u1, ana, Guid.NewGuid(), "2026-11-24T12:00:00Z")) Expect(unknownSpecialty, 400);
            using (var inactiveSpecialty = await Book(4, u1, ana, pediatrics, "2026-11-24T12:00:00Z")) Expect(inactiveSpecialty, 400);
            using (var offGrade = await Book(4, u1, ana, cardiology, "2026-11-23T12:00:00Z")) await ExpectProblem(offGrade, 409, "slot-unavailable");
            using (var wrongProfessional = await Book(4, u1, bruno, cardiology, "2026-11-22T12:00:00Z")) await ExpectProblem(wrongProfessional, 409, "slot-unavailable");
            using (var misaligned = await Book(4, u1, ana, cardiology, "2026-11-22T12:15:00Z")) await ExpectProblem(misaligned, 409, "slot-unavailable");
            using (var list = await f.Attendant.GetAsync($"/api/obs/units/{u1}/appointments?from=2026-11-01T00:00:00Z&to=2027-03-01T00:00:00Z"))
            {
                Expect(list, 200); var items = (await Read(list)).EnumerateArray().ToList();
                Require(items.Count(i => i.GetProperty("bookedBy").GetString() == "patient") == 4 && items.Count(i => i.GetProperty("bookedBy").GetString() == "staff") >= 3, "A listagem do OBS traz bookedBy (patient, staff); a consulta de 2028 fica fora do período pedido.");
            }
        });

        await Check("Limite por especialidade na rede resiste a duas UBS em paralelo e o mesmo horário não vai a dois pacientes", async () =>
        {
            // Duas rodadas (P4 e P5, vinculados às duas UBS): Cardiologia nas duas UBS ao mesmo tempo em domingos livres; exatamente uma passa e a outra é specialty-limit.
            foreach (var (patient, day) in new[] { (4, "2026-12-06"), (5, "2026-12-13") })
            {
                var a = Book(patient, u1, ana, cardiology, $"{day}T12:00:00Z"); var b = Book(patient, u2, f.Carla, cardiology, $"{day}T12:00:00Z");
                var responses = await Task.WhenAll(a, b);
                try
                {
                    Require(responses.Select(r => (int)r.StatusCode).OrderBy(code => code).SequenceEqual([201, 409]), $"Paciente {patient}: esperado uma consulta e um 409, recebido {string.Join('/', responses.Select(r => (int)r.StatusCode))}.");
                    await ExpectProblem(responses.Single(r => r.StatusCode == HttpStatusCode.Conflict), 409, "specialty-limit");
                }
                finally { foreach (var response in responses) response.Dispose(); }
                var total = await f.UnitScalarAsync<long>(u1, "SELECT count(*) FROM nora_unit.appointments WHERE cpf=$1 AND specialty_id=$2 AND booked_by='patient' AND status='scheduled'", Cpf(patient), cardiology)
                    + await f.UnitScalarAsync<long>(u2, "SELECT count(*) FROM nora_unit.appointments WHERE cpf=$1 AND specialty_id=$2 AND booked_by='patient' AND status='scheduled'", Cpf(patient), cardiology);
                Require(total == 1, $"O paciente {patient} ficou com {total} consultas de Cardiologia marcadas pelo app na rede.");
                using var again = await Book(patient, u1, ana, cardiology, "2026-12-27T12:00:00Z"); await ExpectProblem(again, 409, "specialty-limit");
            }
            // O mesmo horário disputado por dois pacientes: um só leva.
            var contenders = await Task.WhenAll(new[] { 9, 10 }.Select(p => Book(p, u1, ana, cardiology, "2027-01-03T12:00:00Z")));
            try
            {
                Require(contenders.Select(r => (int)r.StatusCode).OrderBy(code => code).SequenceEqual([201, 409]), "Dois pacientes no mesmo horário: um 201 e um 409.");
                await ExpectProblem(contenders.Single(r => r.StatusCode == HttpStatusCode.Conflict), 409, "slot-unavailable");
            }
            finally { foreach (var response in contenders) response.Dispose(); }
            Require(await f.UnitScalarAsync<long>(u1, "SELECT count(*) FROM nora_unit.appointments WHERE starts_at='2027-01-03T12:00:00Z' AND status='scheduled'") == 1, "Só uma consulta por horário.");
        });

        await Check("Confirmação de presença: janela de 48 h a 2 h (bordas), idempotência, dono, situação e nada é cancelado sem confirmação; o OBS vê confirmedAt", async () =>
        {
            async Task<Guid> Create(int patient, string start, Guid professional, Guid specialty)
            {
                using var response = await f.Attendant.PostAsJsonAsync($"/api/obs/units/{u1}/appointments", new { cpf = Cpf(patient), startsAt = start, professionalId = professional, specialtyId = specialty });
                Expect(response, 201); return (await Read(response)).GetProperty("id").GetGuid();
            }
            // P6: quarta 12:00Z = agora + 48 h exatas. P7: segunda 14:00Z = agora + 2 h exatas. P8: segunda 13:30Z (limite de 2 h já passou).
            var lower = await Create(6, Wed("12:00"), ana, cardiology); var upper = await Create(7, "2026-11-16T14:00:00Z", ana, clinical); var late = await Create(8, "2026-11-16T13:30:00Z", ana, clinical);
            string Path(Guid id) => $"/api/patient/units/{u1}/appointments/{id}/confirm";
            f.Clock.Set(T0.AddSeconds(-1)); using (var early = await f.Patients[6].PostAsync(Path(lower), null)) await ExpectProblem(early, 409, "confirmation-window");
            f.Clock.Set(T0);
            Require(await f.UnitScalarAsync<DateTimeOffset?>(u1, "SELECT confirmed_at FROM nora_unit.appointments WHERE id=$1", lower) is null, "Confirmação recusada não grava.");
            DateTimeOffset confirmedAt;
            using (var exact = await f.Patients[6].PostAsync(Path(lower), null)) { Expect(exact, 200); var body = await Read(exact); confirmedAt = body.GetProperty("confirmedAt").GetDateTimeOffset(); Require(confirmedAt == T0 && body.GetProperty("status").GetString() == "scheduled", "Confirmação exata a 48 h deveria valer e gravar o instante."); }
            f.Clock.Set(T0.AddMinutes(10));
            using (var repeat = await f.Patients[6].PostAsync(Path(lower), null)) { Expect(repeat, 200); Require((await Read(repeat)).GetProperty("confirmedAt").GetDateTimeOffset() == confirmedAt, "Repetir a confirmação não muda o instante (idempotente)."); }
            f.Clock.Set(T0);
            // Outro paciente, consulta inexistente, outra UBS, sem sessão e equipe.
            using (var stranger = await f.Patients[7].PostAsync(Path(lower), null)) Expect(stranger, 404);
            using (var unknown = await f.Patients[6].PostAsync(Path(Guid.NewGuid()), null)) Expect(unknown, 404);
            using (var otherUnit = await f.Patients[2].PostAsync($"/api/patient/units/{u2}/appointments/{lower}/confirm", null)) Expect(otherUnit, 403);
            using (var anonymous = await f.Anonymous.PostAsync(Path(lower), null)) Expect(anonymous, 401);
            using (var staff = await f.Attendant.PostAsync(Path(lower), null)) Expect(staff, 403);
            // Borda de 2 h: 1 s depois do limite não vale; no instante exato vale; 13:30Z (limite já passou) não.
            f.Clock.Set(T0.AddSeconds(1)); using (var lateByOneSecond = await f.Patients[7].PostAsync(Path(upper), null)) await ExpectProblem(lateByOneSecond, 409, "confirmation-window");
            f.Clock.Set(T0); using (var atTwoHours = await f.Patients[7].PostAsync(Path(upper), null)) { Expect(atTwoHours, 200); Require((await Read(atTwoHours)).GetProperty("confirmedAt").GetDateTimeOffset() == T0, "Confirmação exata a 2 h deveria valer."); }
            using (var tooLate = await f.Patients[8].PostAsync(Path(late), null)) await ExpectProblem(tooLate, 409, "confirmation-window");
            // Consulta cancelada não confirma.
            using (var cancel = await f.Attendant.PatchAsJsonAsync($"/api/obs/units/{u1}/appointments/{late}/status", new { status = "cancelled", confirmed = true })) Expect(cancel, 200);
            using (var cancelled = await f.Patients[8].PostAsync(Path(late), null)) Expect(cancelled, 409);
            // Sem confirmação nada é cancelado: perto do horário a consulta continua agendada e sem confirmação; o OBS vê confirmedAt e bookedBy.
            f.Clock.Set(T0.AddHours(3));
            using (var list = await f.Attendant.GetAsync($"/api/obs/units/{u1}/appointments?from=2026-11-16T00:00:00Z&to=2026-11-20T00:00:00Z"))
            {
                Expect(list, 200); var items = (await Read(list)).EnumerateArray().ToList();
                var confirmed = items.Single(i => i.GetProperty("id").GetGuid() == lower); var notConfirmed = items.Single(i => i.GetProperty("id").GetGuid() == staffAppointmentP2);
                Require(confirmed.GetProperty("confirmedAt").GetDateTimeOffset() == T0 && confirmed.GetProperty("bookedBy").GetString() == "staff" && notConfirmed.GetProperty("confirmedAt").ValueKind == JsonValueKind.Null && notConfirmed.GetProperty("status").GetString() == "scheduled", "Listagem do OBS: confirmedAt e bookedBy; sem confirmação nada muda.");
            }
            f.Clock.Set(T0);
            Require(await f.UnitScalarAsync<long>(u1, "SELECT count(*) FROM nora_unit.appointment_events WHERE source='system'") == 0, "O sistema nunca cancela consulta por falta de confirmação.");
            using (var municipalList = await f.Municipal.GetAsync($"/api/municipal/units/{u1}/appointments?from=2026-11-16T00:00:00Z&to=2026-11-20T00:00:00Z")) { Expect(municipalList, 200); Require((await Read(municipalList)).EnumerateArray().Any(i => i.GetProperty("confirmedAt").ValueKind != JsonValueKind.Null), "A Secretaria também lê confirmedAt."); }
            using (var patientList = await f.Patients[6].GetAsync($"/api/patient/units/{u1}/appointments?from=2026-11-16T00:00:00Z&to=2026-11-20T00:00:00Z")) { Expect(patientList, 200); Require((await Read(patientList)).GetProperty("appointments").EnumerateArray().Single().GetProperty("confirmedAt").ValueKind != JsonValueKind.Null, "O paciente vê a própria confirmação."); }
        });

        await Check("Aceite de vaga relâmpago cria a consulta com bookedBy lightning", async () =>
        {
            // P6 tem consulta de Cardiologia mais tarde (2026-12-02): a vaga aberta de Cardiologia mais cedo (a de P3 ou uma que o distribuidor criou de um cancelamento anterior) é oferecida sozinha a ele;
            // aceitar antecipa: cria a consulta 'lightning' no horário da vaga e cancela a antiga.
            var later = Guid.NewGuid();
            await f.UnitExecAsync(u1, "INSERT INTO nora_unit.appointments(id,cpf,specialty,professional,starts_at,ends_at,status,created_by,created_at) VALUES($1,$2,'Cardiologia','Dr. Futuro',$3,$4,'scheduled',$5,$6)", later, Cpf(6), At("2026-12-02T12:00:00Z"), At("2026-12-02T12:30:00Z"), f.AdminId, T0);
            // P6 com histórico bom (6 comparecimentos há 200 dias, fora das janelas dos indicadores) fica à frente dos outros candidatos de Cardiologia pelo score.
            await f.UnitExecAsync(u1, "INSERT INTO nora_unit.appointments(id,cpf,specialty,professional,starts_at,ends_at,status,created_by,created_at) SELECT gen_random_uuid(),$1,'Histórico','Prof antigo '||g,$2::timestamptz-g*interval '1 hour',$2::timestamptz-g*interval '1 hour'+interval '30 minutes','completed',$3,$2 FROM generate_series(1,6) g", Cpf(6), T0.AddDays(-200), f.AdminId);
            await f.App.Services.GetRequiredService<UnitLightningDispatcher>().AdvanceAsync(u1, default);
            Guid offer; DateTimeOffset offerStart;
            using (var offers = await f.Patients[6].GetAsync($"/api/patient/units/{u1}/lightning-offers"))
            {
                Expect(offers, 200); var pending = (await Read(offers)).GetProperty("offers").EnumerateArray().Where(o => o.GetProperty("status").GetString() == "pending").ToList();
                Require(pending.Count == 1, "P6 deveria ter uma oferta pendente; vagas e ofertas: " + await f.UnitScalarAsync<string>(u1, "SELECT COALESCE(string_agg(s.status||'@'||s.starts_at::text||'/'||s.specialty||'/'||COALESCE(o.cpf||':'||o.status,'-'),' ; '),'') FROM nora_unit.lightning_slots s LEFT JOIN nora_unit.lightning_offers o ON o.slot_id=s.id"));
                offer = pending[0].GetProperty("id").GetGuid(); offerStart = pending[0].GetProperty("startsAt").GetDateTimeOffset();
            }
            Guid acceptedId;
            using (var accept = await f.Patients[6].PostAsJsonAsync($"/api/patient/units/{u1}/lightning-offers/{offer}/respond", new { accept = true })) { Expect(accept, 200); var body = await Read(accept); Require(body.GetProperty("accepted").GetBoolean(), "Oferta aceita."); acceptedId = body.GetProperty("appointmentId").GetGuid(); }
            using (var list = await f.Attendant.GetAsync($"/api/obs/units/{u1}/appointments?from={Uri.EscapeDataString(offerStart.AddHours(-1).ToString("O"))}&to={Uri.EscapeDataString(offerStart.AddHours(1).ToString("O"))}"))
            {
                Expect(list, 200); var items = (await Read(list)).EnumerateArray().ToList(); var created = items.SingleOrDefault(i => i.GetProperty("id").GetGuid() == acceptedId);
                Require(created.ValueKind == JsonValueKind.Object && created.GetProperty("bookedBy").GetString() == "lightning" && created.GetProperty("cpf").GetString() == Cpf(6) && created.GetProperty("status").GetString() == "scheduled" && created.GetProperty("startsAt").GetDateTimeOffset() == offerStart && offerStart < At("2026-12-02T12:00:00Z"),
                    "Consulta da vaga relâmpago incorreta: " + await f.UnitScalarAsync<string>(u1, "SELECT status||'/'||booked_by||'/'||starts_at::text FROM nora_unit.appointments WHERE id=$1", acceptedId));
            }
            Require(await f.UnitScalarAsync<string>(u1, "SELECT a.status||'/'||e.source FROM nora_unit.appointments a JOIN nora_unit.appointment_events e ON e.appointment_id=a.id AND e.status='cancelled' WHERE a.id=$1", later) == "cancelled/rescheduled-lightning", "A consulta antecipada deveria ficar cancelada (rescheduled-lightning).");
        });

        await Check("Limite de requisições por conta nas rotas de escrita do paciente (agendamento 20 e confirmação 40 por 10 min); outro paciente não é afetado", async () =>
        {
            for (var i = 0; i < 20; i++) { using var response = await f.Patients[11].PostAsJsonAsync($"/api/patient/units/{u1}/appointments", new { professionalId = ana, specialtyId = Guid.NewGuid(), startsAt = "2026-11-24T12:00:00Z" }); Expect(response, 400); }
            using (var limited = await f.Patients[11].PostAsJsonAsync($"/api/patient/units/{u1}/appointments", new { professionalId = ana, specialtyId = Guid.NewGuid(), startsAt = "2026-11-24T12:00:00Z" })) await ExpectProblem(limited, 429, "too-many-requests");
            using (var unaffected = await f.Patients[9].PostAsJsonAsync($"/api/patient/units/{u1}/appointments", new { professionalId = ana, specialtyId = Guid.NewGuid(), startsAt = "2026-11-24T12:00:00Z" })) Expect(unaffected, 400);
            for (var i = 0; i < 40; i++) { using var response = await f.Patients[11].PostAsync($"/api/patient/units/{u1}/appointments/{Guid.NewGuid()}/confirm", null); Expect(response, 404); }
            using (var limitedConfirm = await f.Patients[11].PostAsync($"/api/patient/units/{u1}/appointments/{Guid.NewGuid()}/confirm", null)) await ExpectProblem(limitedConfirm, 429, "too-many-requests");
        });

        // ======== 7. Metas ========
        await Check("Metas: só a Secretaria define; validação por métrica; rede e sobrescrita por UBS; status met/near/off/no-data nos indicadores (OBS, rede e byUnit)", async () =>
        {
            foreach (var client in new[] { f.Admin, f.Attendant, f.Patients[1] })
            {
                using var denied = await client.PutAsJsonAsync("/api/municipal/goals/attendance_score", new { target = 0.9 }); Expect(denied, 403);
                using var deniedList = await client.GetAsync("/api/municipal/goals"); Expect(deniedList, 403);
            }
            using (var anonymous = await f.Anonymous.PutAsJsonAsync("/api/municipal/goals/attendance_score", new { target = 0.9 })) Expect(anonymous, 401);
            foreach (var (metric, body) in new (string, object)[] { ("attendance_score", new { target = 1.5 }), ("attendance_score", new { }), ("missed_rate", new { target = -0.1 }), ("average_wait_minutes", new { target = 0 }), ("unknown_metric", new { target = 0.5 }), ("missed_rate", new { target = 0.2, unitId = Guid.Empty }) })
            { using var response = await f.Municipal.PutAsJsonAsync($"/api/municipal/goals/{metric}", body); Expect(response, 400); }
            using (var unknownUnit = await f.Municipal.PutAsJsonAsync("/api/municipal/goals/missed_rate", new { target = 0.2, unitId = Guid.NewGuid() })) Expect(unknownUnit, 404);
            // Histórico de novembro da UBS 1 (inserido direto): 8 concluídas e 2 faltas de P1 → score 0,8; taxa de faltas 0,2.
            for (var i = 0; i < 10; i++) await f.InsertAppointmentAsync(u1, Cpf(1), i < 8 ? "completed" : "missed", T0.AddDays(-10 + i).AddHours(-5));
            const string window = "from=2026-11-01T00:00:00Z&to=2026-11-16T00:00:00Z";
            using (var plain = await f.Attendant.GetAsync($"/api/obs/units/{u1}/metrics?{window}")) { Expect(plain, 200); var body = await Read(plain); Require(body.GetProperty("appointments").GetProperty("attendanceScore").GetDouble() == 0.8 && body.GetProperty("goals").GetArrayLength() == 0, "Sem meta, nada em goals."); }
            using (var network = await f.Municipal.PutAsJsonAsync("/api/municipal/goals/attendance_score", new { target = 0.9 })) { Expect(network, 200); Require((await Read(network)).GetProperty("target").GetDecimal() == 0.9m, "Meta da rede gravada."); }
            using (var again = await f.Municipal.PutAsJsonAsync("/api/municipal/goals/attendance_score", new { target = 0.85 })) Expect(again, 200);
            using (var back = await f.Municipal.PutAsJsonAsync("/api/municipal/goals/attendance_score", new { target = 0.9 })) Expect(back, 200);
            using (var missed = await f.Municipal.PutAsJsonAsync("/api/municipal/goals/missed_rate", new { target = 0.2 })) Expect(missed, 200);
            using (var wait = await f.Municipal.PutAsJsonAsync("/api/municipal/goals/average_wait_minutes", new { target = 30 })) Expect(wait, 200);
            using (var overrideScore = await f.Municipal.PutAsJsonAsync("/api/municipal/goals/attendance_score", new { target = 0.8, unitId = u1 })) Expect(overrideScore, 200);
            using (var overrideMissed = await f.Municipal.PutAsJsonAsync("/api/municipal/goals/missed_rate", new { target = 0.19, unitId = u1 })) Expect(overrideMissed, 200);
            Require(await f.ScalarAsync<long>("SELECT count(*) FROM nora.goals") == 5, "Gravação repetida atualiza, não duplica.");
            using (var list = await f.Municipal.GetAsync("/api/municipal/goals")) { Expect(list, 200); var goals = (await Read(list)).GetProperty("goals").EnumerateArray().ToList(); Require(goals.Count == 5 && goals.Count(g => g.GetProperty("unitId").ValueKind == JsonValueKind.Null) == 3, "Lista de metas incorreta."); }
            JsonElement Goal(JsonElement goals, string metric) => goals.EnumerateArray().Single(g => g.GetProperty("metric").GetString() == metric);
            // OBS da UBS 1: a sobrescrita vale (score 0,8 ≥ 0,8 → met; faltas 0,2 ≤ 0,19 × 1,1 → near); espera sem dado → no-data.
            using (var obs = await f.Attendant.GetAsync($"/api/obs/units/{u1}/metrics?{window}"))
            {
                Expect(obs, 200); var goals = (await Read(obs)).GetProperty("goals");
                Require(Goal(goals, "attendance_score").GetProperty("status").GetString() == "met" && Goal(goals, "attendance_score").GetProperty("target").GetDecimal() == 0.8m && Goal(goals, "attendance_score").GetProperty("value").GetDouble() == 0.8 &&
                    Goal(goals, "missed_rate").GetProperty("status").GetString() == "near" && Goal(goals, "missed_rate").GetProperty("value").GetDouble() == 0.2 && Goal(goals, "average_wait_minutes").GetProperty("status").GetString() == "no-data" && Goal(goals, "average_wait_minutes").GetProperty("value").ValueKind == JsonValueKind.Null, $"Metas da UBS 1 incorretas: {goals}.");
            }
            // Rede (totais com a meta da rede) e byUnit (UBS 1 com sobrescrita; UBS 2 sem dado).
            using (var network = await f.Municipal.GetAsync($"/api/municipal/metrics?{window}"))
            {
                Expect(network, 200); var body = await Read(network); var totals = body.GetProperty("goals");
                Require(Goal(totals, "attendance_score").GetProperty("status").GetString() == "off" && Goal(totals, "attendance_score").GetProperty("target").GetDecimal() == 0.9m && Goal(totals, "missed_rate").GetProperty("status").GetString() == "met" && Goal(totals, "average_wait_minutes").GetProperty("status").GetString() == "no-data", $"Metas da rede incorretas: {totals}.");
                var rows = body.GetProperty("byUnit").EnumerateArray().ToList(); var u1Goals = rows.Single(r => r.GetProperty("unitId").GetGuid() == u1).GetProperty("goals"); var u2Goals = rows.Single(r => r.GetProperty("unitId").GetGuid() == u2).GetProperty("goals");
                Require(Goal(u1Goals, "attendance_score").GetProperty("status").GetString() == "met" && Goal(u1Goals, "missed_rate").GetProperty("status").GetString() == "near" && Goal(u2Goals, "attendance_score").GetProperty("status").GetString() == "no-data" && Goal(u2Goals, "attendance_score").GetProperty("target").GetDecimal() == 0.9m, "byUnit com sobrescrita por UBS e meta da rede nas demais.");
            }
            // Remover a sobrescrita volta à meta da rede; remover o que não existe → 404.
            using (var removeOverride = await f.Municipal.DeleteAsync($"/api/municipal/goals/attendance_score?unitId={u1}")) Expect(removeOverride, 204);
            using (var removeAgain = await f.Municipal.DeleteAsync($"/api/municipal/goals/attendance_score?unitId={u1}")) Expect(removeAgain, 404);
            using (var afterRemoval = await f.Attendant.GetAsync($"/api/obs/units/{u1}/metrics?{window}")) { Expect(afterRemoval, 200); Require(Goal((await Read(afterRemoval)).GetProperty("goals"), "attendance_score").GetProperty("status").GetString() == "off", "Sem sobrescrita vale a meta da rede."); }
            using (var badMetric = await f.Municipal.DeleteAsync("/api/municipal/goals/inexistente")) Expect(badMetric, 400);
            using (var attendantDelete = await f.Attendant.DeleteAsync("/api/municipal/goals/missed_rate")) Expect(attendantDelete, 403);
        });

        // ======== 8. Alertas ========
        await Check("Alertas: fila alta (19 não, 20 sim), espera longa (30 dias exatos não, mais que isso sim), faltas (20% não, acima sim), ocupação (95% não, acima sim) e meta fora; sem dado pessoal; perfis e filtros", async () =>
        {
            async Task<List<JsonElement>> Alerts(HttpClient client, string path) { using var response = await client.GetAsync(path); Expect(response, 200); return (await Read(response)).GetProperty("alerts").EnumerateArray().ToList(); }
            Task<List<JsonElement>> UnitAlerts(Guid unit) => Alerts(f.Admin, $"/api/obs/units/{unit}/alerts");
            List<JsonElement> Of(List<JsonElement> items, string kind) => items.Where(a => a.GetProperty("kind").GetString() == kind).ToList();
            // Acesso.
            using (var patientDenied = await f.Patients[1].GetAsync($"/api/obs/units/{u1}/alerts")) Expect(patientDenied, 403);
            using (var attendantOther = await f.Attendant.GetAsync($"/api/obs/units/{u2}/alerts")) Expect(attendantOther, 403);
            using (var adminOnMunicipal = await f.Admin.GetAsync("/api/municipal/alerts")) Expect(adminOnMunicipal, 403);
            using (var anonymous = await f.Anonymous.GetAsync("/api/municipal/alerts")) Expect(anonymous, 401);
            using (var unknownUnit = await f.Municipal.GetAsync($"/api/municipal/alerts?unitId={Guid.NewGuid()}")) Expect(unknownUnit, 404);
            Require((await Alerts(f.Attendant, $"/api/obs/units/{u1}/alerts")).Count >= 0, "O atendente lê os alertas da própria UBS.");
            // UBS 3 vazia: nenhum alerta.
            Require((await UnitAlerts(u3)).Count == 0, "UBS sem dados não tem alerta.");
            // Fila: 19 pessoas aguardando em Ortopedia → sem alerta; a 20ª dispara (valor 20, limite 20).
            await f.UnitExecAsync(u3, "INSERT INTO nora_unit.patient_registry(cpf,full_name,email,created_by) SELECT lpad((70000000000+g)::text,11,'0'),'Paciente Fila '||g,'fila'||g||'@example.test',$1 FROM generate_series(1,40) g", f.AdminId);
            await f.UnitExecAsync(u3, "INSERT INTO nora_unit.waiting_list(cpf,specialty,entered_at,status,created_by) SELECT lpad((70000000000+g)::text,11,'0'),'Ortopedia',$2,'waiting',$1 FROM generate_series(1,19) g", f.AdminId, T0.AddDays(-2));
            Require(Of(await UnitAlerts(u3), "high-queue").Count == 0, "19 pessoas aguardando não é fila alta.");
            await f.UnitExecAsync(u3, "INSERT INTO nora_unit.waiting_list(cpf,specialty,entered_at,status,created_by) VALUES(lpad((70000000020)::text,11,'0'),'ortopedia',$2,'offered',$1)", f.AdminId, T0.AddDays(-1));
            var queue = Of(await UnitAlerts(u3), "high-queue");
            Require(queue.Count == 1 && queue[0].GetProperty("value").GetDouble() == 20 && queue[0].GetProperty("threshold").GetDouble() == 20 && queue[0].GetProperty("unitId").GetGuid() == u3 && string.Equals(queue[0].GetProperty("specialty").GetString(), "Ortopedia", StringComparison.OrdinalIgnoreCase), $"Fila alta incorreta: {string.Join(',', queue)}.");
            // Espera longa: exatamente 30 dias não; um minuto a mais sim (desde quando a mais antiga entrou).
            await f.UnitExecAsync(u3, "INSERT INTO nora_unit.waiting_list(cpf,specialty,entered_at,status,created_by) VALUES(lpad((70000000025)::text,11,'0'),'Neurologia',$2,'waiting',$1)", f.AdminId, T0.AddDays(-30));
            Require(Of(await UnitAlerts(u3), "long-wait").Count == 0, "Exatamente 30 dias não é espera longa.");
            await f.UnitExecAsync(u3, "UPDATE nora_unit.waiting_list SET entered_at=$1 WHERE specialty='Neurologia'", T0.AddDays(-30).AddMinutes(-1));
            var longWait = Of(await UnitAlerts(u3), "long-wait");
            Require(longWait.Count == 1 && longWait[0].GetProperty("specialty").GetString() == "Neurologia" && longWait[0].GetProperty("threshold").GetDouble() == 30 && longWait[0].GetProperty("value").GetDouble() >= 30 && longWait[0].GetProperty("since").GetDateTimeOffset() == T0.AddDays(-30).AddMinutes(-1), $"Espera longa incorreta: {string.Join(',', longWait)}.");
            // Faltas (UBS 1): 8 concluídas e 2 faltas nos últimos 30 dias = 20% → não; uma falta a mais → 27% → sim.
            Require(Of(await UnitAlerts(u1), "missed-rate").Count == 0, "Exatamente 20% de faltas não dispara.");
            await f.InsertAppointmentAsync(u1, Cpf(1), "missed", T0.AddDays(-3));
            var missedAlert = Of(await UnitAlerts(u1), "missed-rate");
            Require(missedAlert.Count == 1 && Math.Abs(missedAlert[0].GetProperty("value").GetDouble() - 0.2727) < 1e-9 && missedAlert[0].GetProperty("threshold").GetDouble() == 0.2, $"Alerta de faltas incorreto: {string.Join(',', missedAlert)}.");
            // Meta fora: score da rede 0,9 (valor 0,727...) fora → alerta goal-off com a métrica; a sobrescrita de faltas 0,19 está fora também.
            var off = Of(await UnitAlerts(u1), "goal-off");
            Require(off.Select(a => a.GetProperty("metric").GetString()).OrderBy(m => m).SequenceEqual(["attendance_score", "missed_rate"]) && off.All(a => a.GetProperty("unitId").GetGuid() == u1), $"Alertas de meta fora incorretos: {string.Join(',', off)}.");
            // Ocupação (UBS 3): Dr. Carga terça 08:00-18:00 de 30 em 30 = 20 atendimentos na semana seguinte; 19 ocupados = 95% (não dispara), 20 = 100% (dispara).
            using (var prof = await f.Admin.PostAsJsonAsync($"/api/obs/units/{u3}/professionals", new { fullName = "Dr. Carga", specialtyIds = new[] { orthopedics } })) Expect(prof, 201);
            using (var room = await f.Admin.PostAsJsonAsync($"/api/obs/units/{u3}/rooms", new { name = "Sala Única" })) Expect(room, 201);
            var carga = await f.ScalarUnitAsync<Guid>(u3, "SELECT id FROM nora_unit.professionals WHERE full_name='Dr. Carga'"); var sala = await f.ScalarUnitAsync<Guid>(u3, "SELECT id FROM nora_unit.rooms WHERE name='Sala Única'");
            using (var template = await f.Admin.PostAsJsonAsync($"/api/obs/units/{u3}/schedule", new { professionalId = carga, weekday = 2, startTime = "08:00", endTime = "18:00", slotMinutes = 30, roomId = sala, specialtyId = orthopedics })) Expect(template, 201);
            Require(Of(await UnitAlerts(u3), "occupancy").Count == 0, "Grade vazia não tem ocupação alta.");
            await f.UnitExecAsync(u3, "INSERT INTO nora_unit.appointments(id,cpf,specialty,professional,starts_at,ends_at,status,created_by,created_at) SELECT gen_random_uuid(),lpad((70000000000+g)::text,11,'0'),'Ortopedia','Dr. Carga',$2::timestamptz+(g-1)*interval '30 minutes',$2::timestamptz+g*interval '30 minutes','scheduled',$1,$3 FROM generate_series(1,19) g", f.AdminId, At("2026-11-17T11:00:00Z"), T0);
            Require(Of(await UnitAlerts(u3), "occupancy").Count == 0, "19 de 20 atendimentos (95%) não passa de 95%.");
            await f.UnitExecAsync(u3, "INSERT INTO nora_unit.appointments(id,cpf,specialty,professional,starts_at,ends_at,status,created_by,created_at) VALUES(gen_random_uuid(),lpad((70000000020)::text,11,'0'),'Ortopedia','Dr. Carga',$2,$3,'scheduled',$1,$4)", f.AdminId, At("2026-11-17T20:30:00Z"), At("2026-11-17T21:00:00Z"), T0);
            var occupancy = Of(await UnitAlerts(u3), "occupancy");
            Require(occupancy.Count == 1 && occupancy[0].GetProperty("value").GetDouble() == 1 && occupancy[0].GetProperty("threshold").GetDouble() == 0.95, $"Alerta de ocupação incorreto: {string.Join(',', occupancy)}.");
            // Rede: filtros por UBS e por região; sem dado pessoal em nenhuma resposta.
            var allAlerts = await Alerts(f.Municipal, "/api/municipal/alerts");
            Require(allAlerts.Select(a => a.GetProperty("unitId").GetGuid()).Distinct().Count() == 2 && allAlerts.Count(a => a.GetProperty("unitId").GetGuid() == u3) == 3, "A rede traz os alertas das UBS com dado.");
            Require((await Alerts(f.Municipal, $"/api/municipal/alerts?unitId={u3}")).All(a => a.GetProperty("unitId").GetGuid() == u3) && (await Alerts(f.Municipal, "/api/municipal/alerts?region=norte")).All(a => a.GetProperty("unitId").GetGuid() == u3) && (await Alerts(f.Municipal, "/api/municipal/alerts?region=Sul")).Count == 0, "Filtros de UBS e região.");
            using (var raw = await f.Municipal.GetAsync("/api/municipal/alerts")) { var text = await raw.Content.ReadAsStringAsync(); Require(!text.Contains("Paciente Fila") && !text.Contains(Cpf(1)) && !text.Contains("70000000001") && !text.Contains("@example.test"), "Alertas não podem trazer dado pessoal."); }
            foreach (var item in allAlerts) Require(item.TryGetProperty("kind", out _) && item.TryGetProperty("unitName", out _) && item.TryGetProperty("value", out _) && item.TryGetProperty("threshold", out _), "Formato do alerta.");
        });

        // ======== 9. Migrações ========
        await Check("Migrações reaplicadas duas vezes: dados e catálogo preservados, versão do schema da UBS = maior número (12) e colunas novas presentes", async () =>
        {
            var migrations = f.App.Services.GetRequiredService<UnitMigrationDao>();
            var before = await f.UnitScalarAsync<long>(u1, "SELECT (SELECT count(*) FROM nora_unit.professionals)+(SELECT count(*) FROM nora_unit.schedule_templates)+(SELECT count(*) FROM nora_unit.appointments)");
            await migrations.MigrateAsync(u1); await migrations.MigrateAsync(u1);
            await f.App.Services.GetRequiredService<MigrationDao>().MigrateAsync(); await f.App.Services.GetRequiredService<MigrationDao>().MigrateAsync();
            Require(await f.UnitScalarAsync<long>(u1, "SELECT (SELECT count(*) FROM nora_unit.professionals)+(SELECT count(*) FROM nora_unit.schedule_templates)+(SELECT count(*) FROM nora_unit.appointments)") == before, "A reaplicação das migrações alterou dados.");
            Require(await f.UnitScalarAsync<int>(u1, "SELECT version FROM nora_unit.schema_version WHERE singleton") == UnitSchema.ExpectedVersion && UnitSchema.ExpectedVersion >= 12, "Versão do schema da UBS.");
            Require(await f.UnitScalarAsync<long>(u1, "SELECT count(*) FROM information_schema.columns WHERE table_schema='nora_unit' AND table_name='appointments' AND column_name IN ('professional_id','specialty_id','room_id','booked_by','confirmed_at')") == 5, "Colunas novas da consulta.");
            Require(await f.ScalarAsync<long>("SELECT count(*) FROM information_schema.tables WHERE table_schema=$1 AND table_name IN ('specialties','goals')", f.Schema) == 2, "Tabelas centrais novas.");
            try { await f.UnitExecAsync(u1, "INSERT INTO nora_unit.appointments(id,cpf,specialty,professional,starts_at,ends_at,status,created_by,created_at,booked_by) VALUES(gen_random_uuid(),$1,'x','y',now(),now()+interval '1 hour','scheduled',$2,now(),'robo')", Cpf(1), f.AdminId); throw new InvalidOperationException("booked_by inválido foi aceito."); }
            catch (PostgresException) { }
        });

        Console.WriteLine($"{checks - failures}/{checks} verificações da agenda passaram.");
        return failures;
    }

    // ================= Infraestrutura =================

    private static async Task Check(string name, Func<Task> action)
    {
        checks++;
        try { await action(); Console.WriteLine($"PASS [Agenda] {name}"); }
        catch (Exception error) { failures++; Console.WriteLine($"FAIL [Agenda] {name}: {error.GetType().Name}: {error.Message}"); }
    }

    private static void Require(bool valid, string message) { if (!valid) throw new InvalidOperationException(message); }

    private static async Task<JsonElement> Read(HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.Clone();
    }

    private static void Expect(HttpResponseMessage response, int status) => Require((int)response.StatusCode == status, $"Esperado HTTP {status}; recebido {(int)response.StatusCode} em {response.RequestMessage?.Method} {response.RequestMessage?.RequestUri?.PathAndQuery}.");

    private static async Task ExpectProblem(HttpResponseMessage response, int status, string code)
    {
        Expect(response, status);
        Require(response.Content.Headers.ContentType?.MediaType == "application/problem+json", "Erro sem application/problem+json.");
        var body = await Read(response);
        Require(body.GetProperty("code").GetString() == code && body.GetProperty("type").GetString() == $"urn:nora:problem:{code}" && body.GetProperty("status").GetInt32() == status && !string.IsNullOrWhiteSpace(body.GetProperty("title").GetString()), $"Código de erro esperado '{code}', recebido '{body.GetProperty("code").GetString()}'.");
    }

    internal static string Cpf(int number)
    {
        var cpf = (123456780 + number).ToString(CultureInfo.InvariantCulture);
        for (var count = 9; count <= 10; count++)
        {
            var sum = Enumerable.Range(0, count).Sum(i => (cpf[i] - '0') * (count + 1 - i));
            var digit = sum * 10 % 11;
            cpf += (digit == 10 ? 0 : digit).ToString(CultureInfo.InvariantCulture);
        }
        return cpf;
    }

    private sealed class TestClock : TimeProvider
    {
        private long ticks = T0.UtcTicks;
        public override DateTimeOffset GetUtcNow() => new(Interlocked.Read(ref ticks), TimeSpan.Zero);
        public void Set(DateTimeOffset moment) => Interlocked.Exchange(ref ticks, moment.UtcTicks);
    }

    private sealed class NoMail : IEmailSender
    {
        public bool IsConfigured => true;
        public Task SendAsync(string address, string subject, string body, CancellationToken ct) => Task.CompletedTask;
    }

    /// <summary>API de teste num schema próprio com relógio fixo, três UBS (bancos criados pela API), Secretaria, administrador e atendente OBS e dez pacientes já logados.</summary>
    private sealed class Fixture : IAsyncDisposable
    {
        public required WebApplication App { get; init; }
        public required string Schema { get; init; }
        public required TestClock Clock { get; init; }
        public required HttpClient Anonymous { get; init; }
        public required HttpClient Municipal { get; init; }
        public required HttpClient Admin { get; init; }
        public required HttpClient Attendant { get; init; }
        public Dictionary<int, HttpClient> Patients { get; } = [];
        public Guid AdminId { get; set; }
        public Guid Unit1 { get; set; }
        public Guid Unit2 { get; set; }
        public Guid Unit3 { get; set; }
        public Guid Carla { get; set; }
        public Guid RoomA { get; set; }
        private DatabaseHelper Database => App.Services.GetRequiredService<DatabaseHelper>();
        private UnitDatabaseRouter Router => App.Services.GetRequiredService<UnitDatabaseRouter>();

        public static async Task<Fixture> StartAsync(string connection)
        {
            var schema = "nora_agenda_" + Guid.NewGuid().ToString("N");
            var clock = new TestClock();
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Nora.Api", "Nora.Api.csproj"))) directory = directory.Parent;
            // Pools enxutos: o cluster descartável tem max_connections=16 e esta API mantém conexões a três UBS.
            var small = new NpgsqlConnectionStringBuilder(connection) { MaxPoolSize = 6 }.ConnectionString;
            var app = NoraApiHost.Create(new()
            {
                Args = ["--urls", "http://127.0.0.1:0", "--ConnectionStrings:Nora", small, "--Nora:Schema", schema, "--Nora:UnitLightningWorker:Enabled", "false", "--Nora:UnitDatabases:MaxPoolSize", "2",
                    "--Logging:LogLevel:Default", "Warning", "--Logging:LogLevel:Nora.Api.Services.UnitDatabaseProvisioner", "None", "--Nora:Registration:HashKey", "synthetic-agenda-registration-key-32-bytes"],
                EnvironmentName = "Development", ApplicationName = typeof(NoraApiHost).Assembly.GetName().Name, ContentRootPath = Path.Combine(directory!.FullName, "Nora.Api")
            }, services =>
            {
                services.Replace(ServiceDescriptor.Singleton<TimeProvider>(clock));
                services.Replace(ServiceDescriptor.Singleton<IEmailSender>(new NoMail()));
            });
            await app.Services.GetRequiredService<MigrationDao>().MigrateAsync();
            var accounts = app.Services.GetRequiredService<AccountService>();
            var manager = await accounts.ProvisionAsync("municipal", "manager", new("Gestora da agenda", Cpf(901), "gestora@example.test", Password));
            var admin = await accounts.ProvisionAsync("obs", "administrator", new("Administrador da agenda", Cpf(902), "admin@example.test", Password));
            var attendant = await accounts.ProvisionAsync("obs", "attendant", new("Atendente da agenda", Cpf(903), "atendente@example.test", Password));
            await app.StartAsync();
            var f = new Fixture { App = app, Schema = schema, Clock = clock, Anonymous = NewClient(app), Municipal = NewClient(app), Admin = NewClient(app), Attendant = NewClient(app), AdminId = admin.Id };
            await Login(f.Municipal, "municipal", "gestora@example.test"); await Login(f.Admin, "obs", "admin@example.test");
            f.Unit1 = await f.CreateUnitAsync("UBS Agenda Um", "Centro"); f.Unit2 = await f.CreateUnitAsync("UBS Agenda Dois", "Centro"); f.Unit3 = await f.CreateUnitAsync("UBS Agenda Três", "Norte");
            foreach (var unit in new[] { f.Unit1, f.Unit2, f.Unit3 }) { using var grant = await f.Municipal.PutAsync($"/api/municipal/staff/{admin.Id}/units/{unit}", null); Expect(grant, 204); }
            using (var grant = await f.Municipal.PutAsync($"/api/municipal/staff/{attendant.Id}/units/{f.Unit1}", null)) Expect(grant, 204);
            // A atendente só entra em computador ativado da UBS dela.
            await ObsDeviceHelper.ActivateAndLoginAsync(f.Admin, f.Attendant, f.Unit1, "atendente@example.test", Password);
            // P1, P4 e P5 vivem nas duas primeiras UBS; os demais só na UBS 1 (P12 e P13 existem só para as provas de marcação sem antecedência mínima nem máxima).
            foreach (var number in Enumerable.Range(1, 13))
            {
                var units = number is 1 or 4 or 5 ? new[] { f.Unit1, f.Unit2 } : [f.Unit1];
                await accounts.ProvisionAsync("patient", "patient", new($"Paciente Agenda {number}", Cpf(number), $"agenda{number}@example.test", Password));
                foreach (var unit in units) { using var register = await f.Admin.PostAsJsonAsync($"/api/obs/units/{unit}/registry", new { cpf = Cpf(number), fullName = $"Paciente Agenda {number}", email = $"agenda{number}@example.test" }); Expect(register, 201); }
                var client = NewClient(app); await Login(client, "patient", Cpf(number));
                using var link = await client.PostAsJsonAsync("/api/patient/units/link", new { unitIds = units }); Expect(link, 200);
                f.Patients[number] = client;
            }
            return f;
        }

        public async Task<Guid> CreateUnitAsync(string name, string region)
        {
            using var response = await Municipal.PostAsJsonAsync("/api/municipal/units", new { name, region, address = "Rua de teste, 10", latitude = -23.55, longitude = -46.63 });
            Expect(response, 201);
            return (await Read(response)).GetProperty("id").GetGuid();
        }

        public async Task<bool> UsesCatalogAsync(Guid unit) => await App.Services.GetRequiredService<SchedulingService>().UsesCatalogAsync(unit, default);
        public async Task<T> ScalarAsync<T>(string sql, params object[] values) { await using var connection = await Database.OpenAsync(); await using var command = Database.Command(connection, sql.Replace("$schema", Schema), values); return (T)(await command.ExecuteScalarAsync())!; }
        public async Task<T> UnitScalarAsync<T>(Guid unit, string sql, params object[] values) { await using var connection = await Router.OpenAsync(unit); await using var command = new NpgsqlCommand(sql, connection); foreach (var value in values) command.Parameters.AddWithValue(value); var result = await command.ExecuteScalarAsync(); return result is DBNull ? default! : (T)result!; }
        public Task<T> ScalarUnitAsync<T>(Guid unit, string sql, params object[] values) => UnitScalarAsync<T>(unit, sql, values);
        public async Task UnitExecAsync(Guid unit, string sql, params object[] values) { await using var connection = await Router.OpenAsync(unit); await using var command = new NpgsqlCommand(sql, connection); foreach (var value in values) command.Parameters.AddWithValue(value is DateTimeOffset moment ? moment.ToUniversalTime() : value); await command.ExecuteNonQueryAsync(); }
        public Task InsertAppointmentAsync(Guid unit, string cpf, string status, DateTimeOffset startsAt) =>
            UnitExecAsync(unit, "INSERT INTO nora_unit.appointments(id,cpf,specialty,professional,starts_at,ends_at,status,created_by,created_at) VALUES(gen_random_uuid(),$1,'Histórico','Prof Histórico',$2,$3,$4,$5,$6)", cpf, startsAt, startsAt.AddMinutes(30), status, AdminId, startsAt.AddDays(-1));

        private static HttpClient NewClient(WebApplication app) => new()
        {
            BaseAddress = new(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single()),
            Timeout = TimeSpan.FromSeconds(60)
        };

        private static async Task Login(HttpClient client, string scope, string identifier)
        {
            using var response = await client.PostAsJsonAsync("/api/auth/login", new { scope, identifier, password = Password });
            Expect(response, 200);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await Read(response)).GetProperty("token").GetString()!);
        }

        public async ValueTask DisposeAsync()
        {
            foreach (var client in Patients.Values) client.Dispose();
            Anonymous.Dispose(); Municipal.Dispose(); Admin.Dispose(); Attendant.Dispose();
            await App.StopAsync();
            try { await using var connection = await Database.OpenAsync(); await Database.ExecuteAsync(connection, $"DROP SCHEMA IF EXISTS {Schema} CASCADE", default); }
            catch (Exception) { /* o cluster descartável é removido pelo script */ }
            await App.DisposeAsync();
        }
    }
}
