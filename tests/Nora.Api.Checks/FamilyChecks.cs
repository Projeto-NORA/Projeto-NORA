using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using Nora.Api;
using Nora.Api.DAOs;
using Nora.Api.Database;
using Nora.Api.Services;

/// <summary>
/// Família (o responsável age pelo dependente), desativação do paciente na UBS, troca do e-mail da conta, limpeza periódica e leitura de ofertas sem efeito colateral.
/// Só roda com NORA_TEST_CONNECTION (cluster descartável), em schema próprio e com relógio de teste (segunda-feira 2026-11-16 12:00 UTC = 09:00 em America/Sao_Paulo), que só se mexe
/// em minutos para as sessões (8 h) continuarem válidas. Os bancos das UBS são criados pela própria API no cluster descartável.
/// </summary>
internal static partial class FamilyChecks
{
    internal const string Password = "Disposable-test-Account!29";
    internal static readonly DateTimeOffset T0 = new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);
    private static int checks;
    private static int failures;

    internal static string Cpf(int number) => SchedulingChecks.Cpf(number);
    private static string Masked(string cpf) => $"***.{cpf[3..6]}.{cpf[6..9]}-**";

    public static async Task<int> RunAsync(string root)
    {
        var connectionString = Environment.GetEnvironmentVariable("NORA_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Console.WriteLine("SKIP família PostgreSQL: configure NORA_TEST_CONNECTION somente para uma base descartável.");
            return 0;
        }

        await using var f = await Fixture.StartAsync(connectionString);
        var u1 = f.Unit1; var u2 = f.Unit2;
        Guid cardiology = Guid.Empty, clinical = Guid.Empty, ana = Guid.Empty, carla = Guid.Empty;
        Guid l1 = Guid.Empty, l2 = Guid.Empty, l3 = Guid.Empty, l4 = Guid.Empty, l5 = Guid.Empty, g2Link = Guid.Empty;
        string Base(Guid link, Guid unit) => $"/api/patient/dependents/{link}/units/{unit}";
        Task<HttpResponseMessage> Book(HttpClient client, Guid link, Guid unit, Guid professional, Guid specialty, string startsAt) =>
            client.PostAsJsonAsync($"{Base(link, unit)}/appointments", new { professionalId = professional, specialtyId = specialty, startsAt });
        async Task<List<string>> FreeSlots(HttpClient client, string path, Guid specialty, string from, string to) =>
            (await Read(await client.GetAsync($"{path}/availability?from={from}&to={to}&specialtyId={specialty}"))).GetProperty("slots").EnumerateArray().Select(s => s.GetProperty("startsAt").GetDateTimeOffset().ToString("MM-dd HH:mm", CultureInfo.InvariantCulture)).ToList();
        async Task<Guid> Request(HttpClient client, int dependent, Guid unit)
        {
            using var response = await client.PostAsJsonAsync("/api/patient/dependents", new { cpf = Cpf(dependent), unitId = unit }); Expect(response, 201);
            return (await Read(response)).GetProperty("id").GetGuid();
        }
        async Task Approve(Guid unit, Guid link, HttpClient? staff = null) { using var response = await (staff ?? f.Attendant).PostAsJsonAsync($"/api/obs/units/{unit}/family-links/{link}/approve", new { confirmed = true }); Expect(response, 200); }

        // ======== 1. Migrações ========
        await Check("Migrações central 017 e de UBS 014 reaplicadas duas vezes; estruturas, índices, colunas de auditoria e versão do schema", async () =>
        {
            await f.App.Services.GetRequiredService<MigrationDao>().MigrateAsync();
            await f.App.Services.GetRequiredService<UnitMigrationDao>().MigrateAsync(u1);
            Require(await f.Scalar<long>("SELECT count(*) FROM information_schema.tables WHERE table_schema=$1 AND table_name IN ('family_links','email_change_suggestions')", f.Schema) == 2, "Tabelas centrais novas.");
            Require(await f.Scalar<long>("SELECT count(*) FROM pg_indexes WHERE schemaname=$1 AND indexname IN ('family_links_active_uidx','email_change_pending_uidx','sessions_expires_idx','sessions_revoked_idx','registration_challenges_expires_idx','patient_password_reset_expires_idx')", f.Schema) == 6, "Índices centrais novos.");
            Require(await f.UnitScalar<long>(u1, "SELECT count(*) FROM information_schema.columns WHERE table_schema='nora_unit' AND ((table_name='appointment_events' AND column_name='guardian_id') OR (table_name='waiting_list_events' AND column_name='guardian_id') OR (table_name='appointments' AND column_name='confirmed_by'))") == 3, "Colunas de auditoria do responsável.");
            Require(await f.UnitScalar<int>(u1, "SELECT version FROM nora_unit.schema_version WHERE singleton") == UnitSchema.ExpectedVersion && UnitSchema.ExpectedVersion >= 14, "Versão do schema da UBS.");
            Require(await f.UnitScalar<int>(u2, "SELECT version FROM nora_unit.schema_version WHERE singleton") == UnitSchema.ExpectedVersion, "A UBS criada pela API já nasce na versão atual.");
            try { await f.Exec("INSERT INTO nora.family_links(id,guardian_id,dependent_cpf,unit_id,status,requested_at) VALUES(gen_random_uuid(),$1,'123',$2,'pending',now())", f.PatientIds[1], u1); throw new InvalidOperationException("CPF inválido aceito."); }
            catch (PostgresException) { }
            try { await f.Exec("INSERT INTO nora.family_links(id,guardian_id,dependent_cpf,unit_id,status,requested_at) VALUES(gen_random_uuid(),$1,$2,$3,'approved',now())", f.PatientIds[1], Cpf(11), u1); throw new InvalidOperationException("Aprovado sem quem decidiu foi aceito."); }
            catch (PostgresException) { }
        });

        // ======== 2. Catálogo de agenda (necessário para agendar pelo responsável) ========
        await Check("Preparação: especialidades, profissionais, consultórios e grade das duas UBS", async () =>
        {
            using (var c = await f.Municipal.PostAsJsonAsync("/api/municipal/specialties", new { name = "Cardiologia" })) { Expect(c, 201); cardiology = (await Read(c)).GetProperty("id").GetGuid(); }
            using (var c = await f.Municipal.PostAsJsonAsync("/api/municipal/specialties", new { name = "Clínica Geral" })) { Expect(c, 201); clinical = (await Read(c)).GetProperty("id").GetGuid(); }
            using (var a = await f.Admin.PostAsJsonAsync($"/api/obs/units/{u1}/professionals", new { fullName = "Dra. Ana Souza", specialtyIds = new[] { cardiology, clinical } })) { Expect(a, 201); ana = (await Read(a)).GetProperty("id").GetGuid(); }
            using (var k = await f.Admin.PostAsJsonAsync($"/api/obs/units/{u2}/professionals", new { fullName = "Dra. Carla Reis", specialtyIds = new[] { cardiology } })) { Expect(k, 201); carla = (await Read(k)).GetProperty("id").GetGuid(); }
            Guid room1, roomA;
            using (var r = await f.Admin.PostAsJsonAsync($"/api/obs/units/{u1}/rooms", new { name = "Sala 1" })) { Expect(r, 201); room1 = (await Read(r)).GetProperty("id").GetGuid(); }
            using (var r = await f.Admin.PostAsJsonAsync($"/api/obs/units/{u2}/rooms", new { name = "Sala A" })) { Expect(r, 201); roomA = (await Read(r)).GetProperty("id").GetGuid(); }
            // Quarta (3): 09:00-12:00 local = 12:00Z..14:30Z em 6 atendimentos de 30 min; quinta (4): clínica 09:00-11:00 local = 12:00Z a 13:30Z em 4 atendimentos.
            using (var s = await f.Admin.PostAsJsonAsync($"/api/obs/units/{u1}/schedule", new { professionalId = ana, weekday = 3, startTime = "09:00", endTime = "12:00", slotMinutes = 30, roomId = room1, specialtyId = cardiology })) Expect(s, 201);
            using (var s = await f.Admin.PostAsJsonAsync($"/api/obs/units/{u1}/schedule", new { professionalId = ana, weekday = 4, startTime = "09:00", endTime = "11:00", slotMinutes = 30, roomId = room1, specialtyId = clinical })) Expect(s, 201);
            using (var s = await f.Admin.PostAsJsonAsync($"/api/obs/units/{u2}/schedule", new { professionalId = carla, weekday = 3, startTime = "09:00", endTime = "10:00", slotMinutes = 30, roomId = roomA, specialtyId = cardiology })) Expect(s, 201);
        });

        // ======== 3. Pedido do responsável ========
        await Check("Pedido: CPF e UBS no corpo, vale só com cadastro ativo; duplicado, próprio CPF, CPF sem cadastro, UBS inválida e perfis errados são recusados; CPF nunca volta inteiro", async () =>
        {
            using (var created = await f.G1.PostAsJsonAsync("/api/patient/dependents", new { cpf = Cpf(11), unitId = u1 }))
            {
                Expect(created, 201); var text = await created.Content.ReadAsStringAsync(); using var json = JsonDocument.Parse(text); var body = json.RootElement; l1 = body.GetProperty("id").GetGuid();
                Require(body.GetProperty("status").GetString() == "pending" && body.GetProperty("unitId").GetGuid() == u1 && body.GetProperty("unitName").GetString() == "UBS Família Um" && body.GetProperty("dependentCpfMasked").GetString() == Masked(Cpf(11)) && body.GetProperty("dependentName").ValueKind == JsonValueKind.Null, $"Pedido incorreto: {text}.");
                Require(!text.Contains(Cpf(11), StringComparison.Ordinal), "A resposta não pode devolver o CPF inteiro do dependente.");
            }
            using (var duplicate = await f.G1.PostAsJsonAsync("/api/patient/dependents", new { cpf = Cpf(11), unitId = u1 })) await ExpectProblem(duplicate, 409, "family-link-exists");
            var formatted = Cpf(11); formatted = $"{formatted[..3]}.{formatted[3..6]}.{formatted[6..9]}-{formatted[9..]}";
            using (var duplicateFormatted = await f.G1.PostAsJsonAsync("/api/patient/dependents", new { cpf = formatted, unitId = u1 })) await ExpectProblem(duplicateFormatted, 409, "family-link-exists");
            // As recusas abaixo usam G2 (limite de 10 pedidos por 10 minutos por conta, contando também as recusas).
            using (var own = await f.G2.PostAsJsonAsync("/api/patient/dependents", new { cpf = Cpf(2), unitId = u1 })) Expect(own, 400);
            using (var invalid = await f.G2.PostAsJsonAsync("/api/patient/dependents", new { cpf = "11111111111", unitId = u1 })) Expect(invalid, 400);
            using (var unregistered = await f.G2.PostAsJsonAsync("/api/patient/dependents", new { cpf = Cpf(50), unitId = u1 })) Expect(unregistered, 404);
            using (var otherUnit = await f.G2.PostAsJsonAsync("/api/patient/dependents", new { cpf = Cpf(12), unitId = u2 })) Expect(otherUnit, 404);
            using (var unknownUnit = await f.G2.PostAsJsonAsync("/api/patient/dependents", new { cpf = Cpf(11), unitId = Guid.NewGuid() })) Expect(unknownUnit, 400);
            using (var noUnit = await f.G2.PostAsJsonAsync("/api/patient/dependents", new { cpf = Cpf(11) })) Expect(noUnit, 400);
            using (var staff = await f.Admin.PostAsJsonAsync("/api/patient/dependents", new { cpf = Cpf(11), unitId = u1 })) Expect(staff, 403);
            using (var manager = await f.Municipal.PostAsJsonAsync("/api/patient/dependents", new { cpf = Cpf(11), unitId = u1 })) Expect(manager, 403);
            using (var anonymous = await f.Anonymous.PostAsJsonAsync("/api/patient/dependents", new { cpf = Cpf(11), unitId = u1 })) Expect(anonymous, 401);
            g2Link = await Request(f.G2, 11, u1); // outro responsável pelo mesmo dependente: permitido
            // Dependente com conta própria: o pedido é igual (a idade não é validada: a UBS decide).
            l5 = await Request(f.G1, 12, u1);
        });

        await Check("Limites: mais de 20 pedidos e vínculos ativos juntos → family-link-limit; 11º pedido em 10 minutos → 429", async () =>
        {
            for (var i = 0; i < 19; i++) await f.Exec("INSERT INTO nora.family_links(id,guardian_id,dependent_cpf,unit_id,status,requested_at) VALUES(gen_random_uuid(),$1,$2,$3,'pending',now())", f.PatientIds[4], Cpf(200 + i), u1);
            Require(await Request(f.G4, 11, u1) != Guid.Empty, "O 20º pedido ativo ainda cabe.");
            using (var over = await f.G4.PostAsJsonAsync("/api/patient/dependents", new { cpf = Cpf(13), unitId = u1 })) await ExpectProblem(over, 409, "family-link-limit");
            for (var i = 0; i < 10; i++) { using var spam = await f.G3.PostAsJsonAsync("/api/patient/dependents", new { cpf = Cpf(60 + i), unitId = u1 }); Expect(spam, 404); }
            using (var limited = await f.G3.PostAsJsonAsync("/api/patient/dependents", new { cpf = Cpf(13), unitId = u1 })) Expect(limited, 429);
        });

        await Check("Lista do responsável: só os próprios pedidos; o nome do dependente só aparece depois da aprovação", async () =>
        {
            using (var list = await f.G1.GetAsync("/api/patient/dependents"))
            {
                Expect(list, 200); var items = (await Read(list)).GetProperty("dependents").EnumerateArray().ToList();
                Require(items.Count == 2 && items.All(i => i.GetProperty("status").GetString() == "pending" && i.GetProperty("dependentName").ValueKind == JsonValueKind.Null && i.GetProperty("dependentCpfMasked").GetString()!.StartsWith("***.", StringComparison.Ordinal)), "Pedidos pendentes sem nome.");
                Require(items.Select(i => i.GetProperty("id").GetGuid()).ToHashSet().SetEquals([l1, l5]), "A lista mostrou pedido de outra conta.");
            }
            using (var other = await f.G3.GetAsync("/api/patient/dependents")) { Expect(other, 200); Require((await Read(other)).GetProperty("dependents").GetArrayLength() == 0, "G3 não pediu nenhum vínculo."); }
            using (var staff = await f.Admin.GetAsync("/api/patient/dependents")) Expect(staff, 403);
            using (var anonymous = await f.Anonymous.GetAsync("/api/patient/dependents")) Expect(anonymous, 401);
        });

        // ======== 4. Decisão da equipe ========
        await Check("Equipe: lista de pendentes com os dados para conferir os documentos; só quem tem concessão à UBS; aprovar e recusar com confirmação, autor e data; decisão repetida → 409", async () =>
        {
            using (var list = await f.Attendant.GetAsync($"/api/obs/units/{u1}/family-links"))
            {
                Expect(list, 200); var items = (await Read(list)).GetProperty("links").EnumerateArray().ToList();
                var first = items.Single(i => i.GetProperty("id").GetGuid() == l1);
                Require(first.GetProperty("guardianName").GetString() == "Responsável Um" && first.GetProperty("guardianCpf").GetString() == Cpf(1) && first.GetProperty("dependentCpf").GetString() == Cpf(11) && first.GetProperty("dependentName").GetString() == "Dependente Onze" && first.GetProperty("status").GetString() == "pending" && first.GetProperty("decidedAt").ValueKind == JsonValueKind.Null, "Pedido na tela da equipe.");
                Require(items.Count >= 3 && items.Select(i => i.GetProperty("requestedAt").GetDateTimeOffset()).SequenceEqual(items.Select(i => i.GetProperty("requestedAt").GetDateTimeOffset()).Order()), "Pendentes do mais antigo ao mais novo.");
            }
            using (var noGrant = await f.OtherAttendant.GetAsync($"/api/obs/units/{u1}/family-links")) Expect(noGrant, 403);
            using (var manager = await f.Municipal.GetAsync($"/api/obs/units/{u1}/family-links")) Expect(manager, 403);
            using (var patient = await f.G1.GetAsync($"/api/obs/units/{u1}/family-links")) Expect(patient, 403);
            using (var anonymous = await f.Anonymous.GetAsync($"/api/obs/units/{u1}/family-links")) Expect(anonymous, 401);
            using (var badStatus = await f.Attendant.GetAsync($"/api/obs/units/{u1}/family-links?status=ended")) Expect(badStatus, 400);
            using (var badLimit = await f.Attendant.GetAsync($"/api/obs/units/{u1}/family-links?limit=0")) Expect(badLimit, 400);
            using (var noConfirm = await f.Attendant.PostAsJsonAsync($"/api/obs/units/{u1}/family-links/{l1}/approve", new { confirmed = false })) Expect(noConfirm, 400);
            using (var noField = await f.Attendant.PostAsJsonAsync($"/api/obs/units/{u1}/family-links/{l1}/approve", new { })) Expect(noField, 400);
            using (var otherStaff = await f.OtherAttendant.PostAsJsonAsync($"/api/obs/units/{u1}/family-links/{l1}/approve", new { confirmed = true })) Expect(otherStaff, 403);
            using (var wrongUnit = await f.Admin.PostAsJsonAsync($"/api/obs/units/{u2}/family-links/{l1}/approve", new { confirmed = true })) Expect(wrongUnit, 404);
            using (var unknown = await f.Attendant.PostAsJsonAsync($"/api/obs/units/{u1}/family-links/{Guid.NewGuid()}/approve", new { confirmed = true })) Expect(unknown, 404);
            Require(await f.Scalar<string>("SELECT status FROM nora.family_links WHERE id=$1", l1) == "pending", "Nenhuma recusa pode ter decidido o pedido.");
            using (var approve = await f.Attendant.PostAsJsonAsync($"/api/obs/units/{u1}/family-links/{l1}/approve", new { confirmed = true }))
            {
                Expect(approve, 200); var body = await Read(approve);
                Require(body.GetProperty("status").GetString() == "approved" && body.GetProperty("decidedByName").GetString() == "Atendente Família" && body.GetProperty("decidedAt").GetDateTimeOffset() == T0, "Aprovação sem autor e data.");
            }
            Require(await f.Scalar<Guid>("SELECT decided_by FROM nora.family_links WHERE id=$1", l1) == f.AttendantId, "Quem decidiu foi gravado.");
            using (var again = await f.Attendant.PostAsJsonAsync($"/api/obs/units/{u1}/family-links/{l1}/approve", new { confirmed = true })) await ExpectProblem(again, 409, "family-link-closed");
            using (var reject = await f.Attendant.PostAsJsonAsync($"/api/obs/units/{u1}/family-links/{l1}/reject", new { confirmed = true })) await ExpectProblem(reject, 409, "family-link-closed");
            using (var approvedList = await f.Attendant.GetAsync($"/api/obs/units/{u1}/family-links?status=approved")) { Expect(approvedList, 200); Require((await Read(approvedList)).GetProperty("links").EnumerateArray().Single().GetProperty("id").GetGuid() == l1, "Lista de aprovados."); }
        });

        await Check("Recusa com motivo opcional; novo pedido depois da recusa; dependente com cadastro inativo não pode ser aprovado; cancelamento do pedido pelo responsável", async () =>
        {
            l2 = await Request(f.G1, 13, u1);
            using (var longReason = await f.Attendant.PostAsJsonAsync($"/api/obs/units/{u1}/family-links/{l2}/reject", new { confirmed = true, reason = new string('x', 281) })) Expect(longReason, 400);
            using (var noConfirm = await f.Attendant.PostAsJsonAsync($"/api/obs/units/{u1}/family-links/{l2}/reject", new { reason = "Documento ilegível" })) Expect(noConfirm, 400);
            using (var reject = await f.Attendant.PostAsJsonAsync($"/api/obs/units/{u1}/family-links/{l2}/reject", new { confirmed = true, reason = "Documento ilegível" }))
            { Expect(reject, 200); var body = await Read(reject); Require(body.GetProperty("status").GetString() == "rejected" && body.GetProperty("decisionReason").GetString() == "Documento ilegível", "Recusa com motivo."); }
            using (var mine = await f.G1.GetAsync("/api/patient/dependents")) { var rejected = (await Read(mine)).GetProperty("dependents").EnumerateArray().Single(i => i.GetProperty("id").GetGuid() == l2); Require(rejected.GetProperty("status").GetString() == "rejected" && rejected.GetProperty("decisionReason").GetString() == "Documento ilegível" && rejected.GetProperty("dependentName").ValueKind == JsonValueKind.Null, "O responsável vê a recusa, sem o nome."); }
            l2 = await Request(f.G1, 13, u1); // depois da recusa pode pedir de novo
            using (var deactivate = await f.Admin.PatchAsJsonAsync($"/api/obs/units/{u1}/registry/active", new { cpf = Cpf(13), isActive = false, confirmed = true })) Expect(deactivate, 204);
            using (var inactive = await f.Attendant.PostAsJsonAsync($"/api/obs/units/{u1}/family-links/{l2}/approve", new { confirmed = true })) await ExpectProblem(inactive, 409, "dependent-inactive");
            using (var reactivate = await f.Admin.PatchAsJsonAsync($"/api/obs/units/{u1}/registry/active", new { cpf = Cpf(13), isActive = true, confirmed = true })) Expect(reactivate, 204);
            await Approve(u1, l2);
            // Cancelamento: só o próprio responsável, só pendente, com confirmação.
            l3 = await Request(f.G1, 14, u1);
            using (var noConfirm = await f.G1.PostAsJsonAsync($"/api/patient/dependents/{l3}/cancel", new { confirmed = false })) Expect(noConfirm, 400);
            using (var other = await f.G2.PostAsJsonAsync($"/api/patient/dependents/{l3}/cancel", new { confirmed = true })) Expect(other, 404);
            using (var unknown = await f.G1.PostAsJsonAsync($"/api/patient/dependents/{Guid.NewGuid()}/cancel", new { confirmed = true })) Expect(unknown, 404);
            using (var pendingUse = await f.G1.GetAsync($"{Base(l3, u1)}/appointments?from=2026-11-16T00:00:00Z&to=2026-11-30T00:00:00Z")) Expect(pendingUse, 403);
            using (var cancel = await f.G1.PostAsJsonAsync($"/api/patient/dependents/{l3}/cancel", new { confirmed = true })) { Expect(cancel, 200); var body = await Read(cancel); Require(body.GetProperty("status").GetString() == "cancelled" && body.GetProperty("closedAt").GetDateTimeOffset() == T0, "Cancelamento sem data."); }
            Require(await f.Scalar<Guid>("SELECT closed_by FROM nora.family_links WHERE id=$1", l3) == f.PatientIds[1], "Quem cancelou foi gravado.");
            using (var again = await f.G1.PostAsJsonAsync($"/api/patient/dependents/{l3}/cancel", new { confirmed = true })) await ExpectProblem(again, 409, "family-link-closed");
            using (var decideCancelled = await f.Attendant.PostAsJsonAsync($"/api/obs/units/{u1}/family-links/{l3}/approve", new { confirmed = true })) await ExpectProblem(decideCancelled, 409, "family-link-closed");
            using (var cancelApproved = await f.G1.PostAsJsonAsync($"/api/patient/dependents/{l2}/cancel", new { confirmed = true })) await ExpectProblem(cancelApproved, 409, "family-link-closed");
            using (var endPending = await f.G2.PostAsJsonAsync($"/api/patient/dependents/{g2Link}/end", new { confirmed = true })) await ExpectProblem(endPending, 409, "family-link-closed");
            // Segundo vínculo do mesmo dependente numa outra UBS (para o limite de especialidade na rede) e dependente com conta própria.
            l4 = await Request(f.G1, 11, u2);
            await Approve(u2, l4, f.Admin);
            await Approve(u1, l5);
            using (var list = await f.G1.GetAsync("/api/patient/dependents"))
            {
                var items = (await Read(list)).GetProperty("dependents").EnumerateArray().ToList();
                Require(items.Single(i => i.GetProperty("id").GetGuid() == l1).GetProperty("dependentName").GetString() == "Dependente Onze" && items.Single(i => i.GetProperty("id").GetGuid() == l4).GetProperty("dependentName").GetString() == "Dependente Onze", "Depois da aprovação o nome aparece.");
                Require(items.Single(i => i.GetProperty("id").GetGuid() == l3).GetProperty("dependentName").ValueKind == JsonValueKind.Null, "Pedido cancelado não mostra nome.");
            }
        });

        // ======== 5. Autorização das rotas delegadas ========
        await Check("Autorização: outro responsável, UBS errada, vínculo inexistente, pendente, equipe, anônimo e CPF na URL são recusados; o dependente sem conta e o com conta funcionam", async () =>
        {
            var window = "from=2026-11-16T00:00:00Z&to=2026-12-31T00:00:00Z";
            using (var ok = await f.G1.GetAsync($"{Base(l1, u1)}/appointments?{window}")) Expect(ok, 200);
            using (var otherGuardian = await f.G2.GetAsync($"{Base(l1, u1)}/appointments?{window}")) Expect(otherGuardian, 403);
            using (var wrongUnit = await f.G1.GetAsync($"{Base(l1, u2)}/appointments?{window}")) Expect(wrongUnit, 403);
            using (var unknown = await f.G1.GetAsync($"{Base(Guid.NewGuid(), u1)}/appointments?{window}")) Expect(unknown, 403);
            using (var pending = await f.G2.GetAsync($"{Base(g2Link, u1)}/appointments?{window}")) Expect(pending, 403);
            using (var staff = await f.Admin.GetAsync($"{Base(l1, u1)}/appointments?{window}")) Expect(staff, 403);
            using (var manager = await f.Municipal.GetAsync($"{Base(l1, u1)}/appointments?{window}")) Expect(manager, 403);
            using (var anonymous = await f.Anonymous.GetAsync($"{Base(l1, u1)}/appointments?{window}")) Expect(anonymous, 401);
            using (var cpfInUrl = await f.G1.GetAsync($"/api/patient/dependents/{Cpf(11)}/units/{u1}/appointments?{window}")) Expect(cpfInUrl, 404);
            using (var badRange = await f.G1.GetAsync($"{Base(l1, u1)}/appointments?from=2026-12-01T00:00:00Z&to=2026-11-01T00:00:00Z")) Expect(badRange, 400);
            foreach (var route in new[] { "waiting-list", "lightning-offers", "health-records", "attendance" })
            {
                using var other = await f.G2.GetAsync($"{Base(l1, u1)}/{route}"); Expect(other, 403);
                using var mine = await f.G1.GetAsync($"{Base(l1, u1)}/{route}"); Expect(mine, 200);
            }
            // O dependente com conta própria (D2) continua vendo o que é dele, e o responsável também.
            using (var ownAccount = await f.D2.GetAsync($"/api/patient/units/{u1}/appointments?{window}")) Expect(ownAccount, 200);
            using (var guardian = await f.G1.GetAsync($"{Base(l5, u1)}/appointments?{window}")) Expect(guardian, 200);
        });

        // ======== 6. Agendar, confirmar, cancelar, score ========
        await Check("Agendar pelo dependente: janela, uma consulta futura por especialidade contada por CPF na rede inteira, dependentes e o próprio responsável contam à parte, autor e origem gravados", async () =>
        {
            // Só o passado cai fora da janela (a recusa de janela vem antes de qualquer outra).
            using (var past = await Book(f.G1, l1, u1, ana, cardiology, "2026-11-16T11:00:00Z")) await ExpectProblem(past, 409, "booking-window");
            using (var missing = await f.G1.PostAsJsonAsync($"{Base(l1, u1)}/appointments", new { specialtyId = cardiology, startsAt = "2026-11-18T12:00:00Z" })) Expect(missing, 400);
            Require((await FreeSlots(f.G1, Base(l1, u1), cardiology, "2026-11-18T00:00:00Z", "2026-11-19T00:00:00Z")).SequenceEqual(["11-18 12:00", "11-18 12:30", "11-18 13:00", "11-18 13:30", "11-18 14:00", "11-18 14:30"]), "Disponibilidade pelo responsável.");
            Guid appointment;
            using (var booked = await Book(f.G1, l1, u1, ana, cardiology, "2026-11-18T12:00:00Z"))
            {
                Expect(booked, 201); var body = await Read(booked); appointment = body.GetProperty("id").GetGuid();
                Require(body.GetProperty("cpf").GetString() == Cpf(11) && body.GetProperty("bookedBy").GetString() == "patient" && body.GetProperty("status").GetString() == "scheduled", "Consulta do dependente.");
            }
            f.D1Appointment = appointment;
            // Auditoria: autor = responsável, origem patient, guardian_id = responsável.
            Require(await f.UnitScalar<long>(u1, "SELECT count(*) FROM nora_unit.appointment_events WHERE appointment_id=$1 AND status='scheduled' AND actor_id=$2 AND source='patient' AND guardian_id=$2", appointment, f.PatientIds[1]) == 1, "Evento de agendamento do responsável sem auditoria.");
            Require(await f.UnitScalar<Guid>(u1, "SELECT created_by FROM nora_unit.appointments WHERE id=$1", appointment) == f.PatientIds[1], "created_by é o responsável.");
            using (var second = await Book(f.G1, l1, u1, ana, cardiology, "2026-11-18T12:30:00Z")) await ExpectProblem(second, 409, "specialty-limit");
            using (var otherUnit = await Book(f.G1, l4, u2, carla, cardiology, "2026-11-18T12:00:00Z")) await ExpectProblem(otherUnit, 409, "specialty-limit"); // contado na UBS 1, vale na rede
            Require(await f.UnitScalar<long>(u2, "SELECT count(*) FROM nora_unit.appointments") == 0, "Recusa não grava consulta.");
            using (var l2Booked = await Book(f.G1, l2, u1, ana, cardiology, "2026-11-18T12:30:00Z")) Expect(l2Booked, 201); // outro dependente: limite à parte
            using (var own = await f.G1.PostAsJsonAsync($"/api/patient/units/{u1}/appointments", new { professionalId = ana, specialtyId = cardiology, startsAt = "2026-11-18T13:00:00Z" })) { Expect(own, 201); f.G1OwnAppointment = (await Read(own)).GetProperty("id").GetGuid(); } // o responsável conta à parte
            Require(await f.UnitScalar<long>(u1, "SELECT count(*) FROM nora_unit.appointment_events WHERE appointment_id=$1 AND guardian_id IS NULL AND source='patient'", f.G1OwnAppointment) == 1, "Ação do próprio paciente não tem guardian_id.");
            using (var taken = await Book(f.G1, l5, u1, ana, cardiology, "2026-11-18T12:00:00Z")) await ExpectProblem(taken, 409, "slot-unavailable");
            using (var thursday = await Book(f.G1, l5, u1, ana, clinical, "2026-11-19T12:00:00Z")) { Expect(thursday, 201); f.D2Appointment = (await Read(thursday)).GetProperty("id").GetGuid(); }
            using (var seenByAccount = await f.D2.GetAsync($"/api/patient/units/{u1}/appointments?from=2026-11-16T00:00:00Z&to=2026-12-31T00:00:00Z")) { Expect(seenByAccount, 200); Require((await Read(seenByAccount)).GetProperty("appointments").EnumerateArray().Single().GetProperty("id").GetGuid() == f.D2Appointment, "A conta do próprio dependente enxerga a consulta marcada pelo responsável."); }
            using (var list = await f.G1.GetAsync($"{Base(l1, u1)}/appointments?from=2026-11-16T00:00:00Z&to=2026-12-31T00:00:00Z")) { Expect(list, 200); var items = (await Read(list)).GetProperty("appointments").EnumerateArray().ToList(); Require(items.Count == 1 && items[0].GetProperty("cpf").GetString() == Cpf(11), "Só as consultas do dependente."); }
            Require((await FreeSlots(f.G1, Base(l1, u1), cardiology, "2026-11-18T00:00:00Z", "2026-11-19T00:00:00Z")).SequenceEqual(["11-18 13:30", "11-18 14:00", "11-18 14:30"]), "Horários reservados saem da disponibilidade.");
        });

        await Check("Agendamentos simultâneos do mesmo dependente em horários diferentes: só um vale (limite por especialidade serializado pelo CPF)", async () =>
        {
            var results = await Task.WhenAll(Book(f.G1, l2, u1, ana, clinical, "2026-11-19T12:30:00Z"), Book(f.G1, l2, u1, ana, clinical, "2026-11-19T13:00:00Z"));
            try { Require(results.Count(r => (int)r.StatusCode == 201) == 1 && results.Count(r => (int)r.StatusCode == 409) == 1, $"Esperado um 201 e um 409; recebido {string.Join(",", results.Select(r => (int)r.StatusCode))}."); }
            finally { foreach (var response in results) response.Dispose(); }
            Require(await f.UnitScalar<long>(u1, "SELECT count(*) FROM nora_unit.appointments WHERE cpf=$1 AND specialty_id=$2 AND status='scheduled'", Cpf(13), clinical) == 1, "Um só agendamento de clínica para o dependente.");
        });

        await Check("Confirmar presença, cancelar consulta e participação (score) do dependente; autor e origem; liberar horário; outro dependente é 404", async () =>
        {
            var appointment = f.D1Appointment;
            using (var otherDependent = await f.G1.PostAsync($"{Base(l2, u1)}/appointments/{appointment}/confirm", null)) Expect(otherDependent, 404);
            using (var confirm = await f.G1.PostAsync($"{Base(l1, u1)}/appointments/{appointment}/confirm", null)) { Expect(confirm, 200); f.ConfirmedAt = (await Read(confirm)).GetProperty("confirmedAt").GetDateTimeOffset(); Require(f.ConfirmedAt == T0, "Confirmação."); }
            Require(await f.UnitScalar<Guid>(u1, "SELECT confirmed_by FROM nora_unit.appointments WHERE id=$1", appointment) == f.PatientIds[1], "confirmed_by é o responsável.");
            f.Clock.Set(T0.AddMinutes(1));
            using (var again = await f.G1.PostAsync($"{Base(l1, u1)}/appointments/{appointment}/confirm", null)) { Expect(again, 200); Require((await Read(again)).GetProperty("confirmedAt").GetDateTimeOffset() == f.ConfirmedAt, "Confirmação idempotente."); }
            f.Clock.Set(T0);
            using (var noConfirm = await f.G1.PostAsJsonAsync($"{Base(l1, u1)}/appointments/{appointment}/cancel", new { confirmed = false })) Expect(noConfirm, 400);
            using (var noBody = await f.G1.PostAsJsonAsync($"{Base(l1, u1)}/appointments/{appointment}/cancel", new { })) Expect(noBody, 400);
            using (var other = await f.G2.PostAsJsonAsync($"{Base(l1, u1)}/appointments/{appointment}/cancel", new { confirmed = true })) Expect(other, 403);
            using (var wrongDependent = await f.G1.PostAsJsonAsync($"{Base(l2, u1)}/appointments/{appointment}/cancel", new { confirmed = true })) Expect(wrongDependent, 404);
            using (var cancel = await f.G1.PostAsJsonAsync($"{Base(l1, u1)}/appointments/{appointment}/cancel", new { confirmed = true })) { Expect(cancel, 200); Require((await Read(cancel)).GetProperty("status").GetString() == "cancelled", "Cancelamento."); }
            Require(await f.UnitScalar<long>(u1, "SELECT count(*) FROM nora_unit.appointment_events WHERE appointment_id=$1 AND status='cancelled' AND actor_id=$2 AND source='patient' AND guardian_id=$2", appointment, f.PatientIds[1]) == 1, "Cancelamento do responsável sem auditoria.");
            using (var again2 = await f.G1.PostAsJsonAsync($"{Base(l1, u1)}/appointments/{appointment}/cancel", new { confirmed = true })) Expect(again2, 409);
            Require((await FreeSlots(f.G1, Base(l1, u1), cardiology, "2026-11-18T00:00:00Z", "2026-11-19T00:00:00Z")).Contains("11-18 12:00"), "O horário cancelado volta à disponibilidade.");
            // Cancelamento do paciente pesa no score pela tabela normal: 48 h de antecedência = 0,1.
            using (var attendance = await f.G1.GetAsync($"{Base(l1, u1)}/attendance")) { Expect(attendance, 200); var body = await Read(attendance); Require(body.GetProperty("cancelledByPatient").GetInt64() == 1 && Math.Abs(body.GetProperty("cancellationWeight").GetDouble() - 0.1) < 1e-9 && body.GetProperty("windowMonths").GetInt32() == 12, $"Participação do dependente: {body}."); }
            using (var other = await f.G1.GetAsync($"{Base(l2, u1)}/attendance")) { Expect(other, 200); Require((await Read(other)).GetProperty("cancelledByPatient").GetInt64() == 0, "O outro dependente não herda o cancelamento."); }
            // Cancelar liberou o limite: marca de novo.
            using (var rebook = await Book(f.G1, l1, u1, ana, cardiology, "2026-11-18T12:00:00Z")) { Expect(rebook, 201); f.D1Appointment = (await Read(rebook)).GetProperty("id").GetGuid(); }
        });

        // ======== 7. Fila, ofertas e Jornada ========
        await Check("Fila do dependente: o responsável lista só a do dependente e sai dela; outro dependente é 404; evento com autor e guardian_id; repetir é 409", async () =>
        {
            long d1Entry, d3Entry;
            using (var a = await f.Attendant.PostAsJsonAsync($"/api/obs/units/{u1}/waiting-list", new { cpf = Cpf(11), specialty = "Cardiologia" })) { Expect(a, 201); d1Entry = (await Read(a)).GetProperty("id").GetInt64(); }
            using (var b = await f.Attendant.PostAsJsonAsync($"/api/obs/units/{u1}/waiting-list", new { cpf = Cpf(13), specialty = "Clínica Geral" })) { Expect(b, 201); d3Entry = (await Read(b)).GetProperty("id").GetInt64(); }
            using (var list = await f.G1.GetAsync($"{Base(l1, u1)}/waiting-list")) { Expect(list, 200); var entries = (await Read(list)).GetProperty("entries").EnumerateArray().ToList(); Require(entries.Count == 1 && entries[0].GetProperty("id").GetInt64() == d1Entry && entries[0].GetProperty("cpf").GetString() == Cpf(11), "Fila do dependente."); }
            using (var noConfirm = await f.G1.PostAsJsonAsync($"{Base(l1, u1)}/waiting-list/{d1Entry}/cancel", new { confirmed = false })) Expect(noConfirm, 400);
            using (var otherDependent = await f.G1.PostAsJsonAsync($"{Base(l1, u1)}/waiting-list/{d3Entry}/cancel", new { confirmed = true })) Expect(otherDependent, 404);
            using (var otherGuardian = await f.G2.PostAsJsonAsync($"{Base(l1, u1)}/waiting-list/{d1Entry}/cancel", new { confirmed = true })) Expect(otherGuardian, 403);
            using (var leave = await f.G1.PostAsJsonAsync($"{Base(l1, u1)}/waiting-list/{d1Entry}/cancel", new { confirmed = true })) Expect(leave, 204);
            Require(await f.UnitScalar<long>(u1, "SELECT count(*) FROM nora_unit.waiting_list_events WHERE entry_id=$1 AND status='cancelled' AND actor_id=$2 AND source='patient' AND guardian_id=$2", d1Entry, f.PatientIds[1]) == 1, "Saída da fila do responsável sem auditoria.");
            Require(await f.UnitScalar<string>(u1, "SELECT status FROM nora_unit.waiting_list WHERE id=$1", d3Entry) == "waiting", "A entrada do outro dependente não foi tocada.");
            using (var again = await f.G1.PostAsJsonAsync($"{Base(l1, u1)}/waiting-list/{d1Entry}/cancel", new { confirmed = true })) Expect(again, 409);
        });

        await Check("Ofertas do dependente sem conta: ele recebe a oferta, o responsável a lista (por dependente e agregada, só pendentes), aceita e a consulta nasce com autor e guardian_id", async () =>
        {
            // O dependente volta à fila (a UBS o inclui) e uma consulta futura de outra pessoa é cancelada e vira Vaga Relâmpago: o dependente sem conta é elegível pelo vínculo aprovado.
            using (var queue = await f.Attendant.PostAsJsonAsync($"/api/obs/units/{u1}/waiting-list", new { cpf = Cpf(11), specialty = "Cardiologia" })) Expect(queue, 201);
            Guid source;
            using (var created = await f.Attendant.PostAsJsonAsync($"/api/obs/units/{u1}/appointments", new { cpf = Cpf(41), professionalId = ana, specialtyId = cardiology, startsAt = "2026-11-18T13:30:00Z" })) { Expect(created, 201); source = (await Read(created)).GetProperty("id").GetGuid(); }
            using (var cancelled = await f.Attendant.PatchAsJsonAsync($"/api/obs/units/{u1}/appointments/{source}/status", new { status = "cancelled", confirmed = true })) Expect(cancelled, 200);
            using (var slot = await f.Attendant.PostAsJsonAsync($"/api/obs/units/{u1}/lightning-slots", new { appointmentId = source, confirmed = true })) Expect(slot, 201);
            Guid offer;
            using (var offers = await f.G1.GetAsync($"{Base(l1, u1)}/lightning-offers"))
            {
                Expect(offers, 200); var items = (await Read(offers)).GetProperty("offers").EnumerateArray().ToList();
                Require(items.Count == 1 && items[0].GetProperty("status").GetString() == "pending", "A oferta chegou ao dependente sem conta.");
                offer = items[0].GetProperty("id").GetGuid();
            }
            using (var aggregate = await f.G1.GetAsync("/api/patient/dependents/lightning-offers"))
            {
                Expect(aggregate, 200); var items = (await Read(aggregate)).GetProperty("dependents").EnumerateArray().ToList();
                Require(items.Count == 1 && items[0].GetProperty("dependentId").GetGuid() == l1 && items[0].GetProperty("dependentName").GetString() == "Dependente Onze" && items[0].GetProperty("unitName").GetString() == "UBS Família Um" && items[0].GetProperty("offers").GetArrayLength() == 1 && items[0].GetProperty("offers")[0].GetProperty("id").GetGuid() == offer, "Consulta agregada das ofertas dos dependentes.");
            }
            using (var empty = await f.G2.GetAsync("/api/patient/dependents/lightning-offers")) { Expect(empty, 200); Require((await Read(empty)).GetProperty("dependents").GetArrayLength() == 0, "Outro responsável não vê."); }
            using (var staff = await f.Admin.GetAsync("/api/patient/dependents/lightning-offers")) Expect(staff, 403);
            using (var anonymous = await f.Anonymous.GetAsync("/api/patient/dependents/lightning-offers")) Expect(anonymous, 401);
            using (var otherGuardian = await f.G2.PostAsJsonAsync($"{Base(l1, u1)}/lightning-offers/{offer}/respond", new { accept = true })) Expect(otherGuardian, 403);
            using (var wrongDependent = await f.G1.PostAsJsonAsync($"{Base(l2, u1)}/lightning-offers/{offer}/respond", new { accept = true })) Expect(wrongDependent, 404);
            using (var noField = await f.G1.PostAsJsonAsync($"{Base(l1, u1)}/lightning-offers/{offer}/respond", new { })) Expect(noField, 400);
            Guid accepted;
            using (var accept = await f.G1.PostAsJsonAsync($"{Base(l1, u1)}/lightning-offers/{offer}/respond", new { accept = true })) { Expect(accept, 200); var body = await Read(accept); Require(body.GetProperty("accepted").GetBoolean(), "Aceite."); accepted = body.GetProperty("appointmentId").GetGuid(); }
            Require(await f.UnitScalar<string>(u1, "SELECT cpf||'/'||booked_by FROM nora_unit.appointments WHERE id=$1", accepted) == $"{Cpf(11)}/lightning", "A consulta da vaga é do dependente, marcada como lightning.");
            Require(await f.UnitScalar<long>(u1, "SELECT count(*) FROM nora_unit.appointment_events WHERE appointment_id=$1 AND status='scheduled' AND actor_id=$2 AND source='patient' AND guardian_id=$2", accepted, f.PatientIds[1]) == 1, "Evento de aceite com guardian_id.");
            Require(await f.UnitScalar<long>(u1, "SELECT count(*) FROM nora_unit.waiting_list_events e JOIN nora_unit.waiting_list q ON q.id=e.entry_id WHERE q.cpf=$1 AND e.status='completed' AND e.actor_id=$2 AND e.source='patient' AND e.guardian_id=$2", Cpf(11), f.PatientIds[1]) == 1, "Evento de fila do aceite com guardian_id.");
            using (var again = await f.G1.PostAsJsonAsync($"{Base(l1, u1)}/lightning-offers/{offer}/respond", new { accept = true })) Expect(again, 409);
            using (var emptyAfter = await f.G1.GetAsync("/api/patient/dependents/lightning-offers")) { Expect(emptyAfter, 200); Require((await Read(emptyAfter)).GetProperty("dependents").GetArrayLength() == 0, "Sem oferta pendente a consulta agregada fica vazia."); }
        });

        await Check("Jornada do dependente: exames e vacinas registrados pela equipe aparecem para o responsável, só os do dependente, sem motivo de anulação", async () =>
        {
            using (var exam = await f.Attendant.PostAsJsonAsync($"/api/obs/units/{u1}/health-records/exams", new { cpf = Cpf(11), name = "Hemograma", note = "Jejum" })) Expect(exam, 201);
            using (var vaccine = await f.Attendant.PostAsJsonAsync($"/api/obs/units/{u1}/health-records/vaccines", new { cpf = Cpf(11), name = "Influenza", dose = "single", appliedOn = "2026-11-10" })) Expect(vaccine, 201);
            using (var other = await f.Attendant.PostAsJsonAsync($"/api/obs/units/{u1}/health-records/exams", new { cpf = Cpf(13), name = "Raio-X" })) Expect(other, 201);
            using (var records = await f.G1.GetAsync($"{Base(l1, u1)}/health-records"))
            {
                Expect(records, 200); var body = await Read(records);
                Require(body.GetProperty("exams").GetArrayLength() == 1 && body.GetProperty("exams")[0].GetProperty("name").GetString() == "Hemograma" && body.GetProperty("vaccines").GetArrayLength() == 1 && body.GetProperty("vaccines")[0].GetProperty("name").GetString() == "Influenza", "Jornada do dependente.");
                Require(!body.GetRawText().Contains("voidReason", StringComparison.Ordinal), "O motivo de anulação nunca vai ao responsável.");
            }
            using (var denied = await f.G2.GetAsync($"{Base(l1, u1)}/health-records")) Expect(denied, 403);
            using (var write = await f.G1.PostAsJsonAsync($"{Base(l1, u1)}/health-records", new { name = "x" })) Expect(write, 405);
        });

        // ======== 8. Encerramento e cadastro inativo ========
        await Check("Encerrar vínculo: o responsável ou a equipe encerram um vínculo aprovado; depois disso as rotas respondem 403; o dependente com cadastro inativo bloqueia as rotas delegadas e a conta própria", async () =>
        {
            using (var noConfirm = await f.G1.PostAsJsonAsync($"/api/patient/dependents/{l4}/end", new { confirmed = false })) Expect(noConfirm, 400);
            using (var other = await f.G2.PostAsJsonAsync($"/api/patient/dependents/{l4}/end", new { confirmed = true })) Expect(other, 404);
            using (var end = await f.G1.PostAsJsonAsync($"/api/patient/dependents/{l4}/end", new { confirmed = true })) { Expect(end, 200); var body = await Read(end); Require(body.GetProperty("status").GetString() == "ended" && body.GetProperty("closedAt").GetDateTimeOffset() == T0 && body.GetProperty("dependentName").GetString() == "Dependente Onze", "Encerramento pelo responsável."); }
            using (var after = await f.G1.GetAsync($"{Base(l4, u2)}/appointments?from=2026-11-16T00:00:00Z&to=2026-12-31T00:00:00Z")) Expect(after, 403);
            using (var again = await f.G1.PostAsJsonAsync($"/api/patient/dependents/{l4}/end", new { confirmed = true })) await ExpectProblem(again, 409, "family-link-closed");
            using (var endByStaff = await f.Attendant.PostAsJsonAsync($"/api/obs/units/{u1}/family-links/{l2}/end", new { confirmed = true })) { Expect(endByStaff, 200); Require((await Read(endByStaff)).GetProperty("status").GetString() == "ended", "Encerramento pela equipe."); }
            Require(await f.Scalar<Guid>("SELECT closed_by FROM nora.family_links WHERE id=$1", l2) == f.AttendantId, "Quem encerrou foi gravado.");
            using (var afterStaff = await f.G1.GetAsync($"{Base(l2, u1)}/appointments?from=2026-11-16T00:00:00Z&to=2026-12-31T00:00:00Z")) Expect(afterStaff, 403);
            using (var endNonApproved = await f.Attendant.PostAsJsonAsync($"/api/obs/units/{u1}/family-links/{l2}/end", new { confirmed = true })) await ExpectProblem(endNonApproved, 409, "family-link-closed");
            using (var staffOther = await f.OtherAttendant.PostAsJsonAsync($"/api/obs/units/{u1}/family-links/{l5}/end", new { confirmed = true })) Expect(staffOther, 403);
            // Cadastro do dependente com conta própria inativo na UBS: bloqueia o responsável e a própria conta.
            var window = "from=2026-11-16T00:00:00Z&to=2026-12-31T00:00:00Z";
            using (var preview = await f.Admin.PostAsJsonAsync($"/api/obs/units/{u1}/registry/deactivation-preview", new { cpf = Cpf(12) })) { Expect(preview, 200); Require((await Read(preview)).GetProperty("futureAppointments").GetInt32() == 1, "Prévia: a consulta de quinta do dependente."); }
            using (var off = await f.Admin.PatchAsJsonAsync($"/api/obs/units/{u1}/registry/active", new { cpf = Cpf(12), isActive = false, confirmed = true })) Expect(off, 204);
            using (var guardianBlocked = await f.G1.GetAsync($"{Base(l5, u1)}/appointments?{window}")) Expect(guardianBlocked, 403);
            using (var ownBlocked = await f.D2.GetAsync($"/api/patient/units/{u1}/appointments?{window}")) Expect(ownBlocked, 403);
            using (var aggregate = await f.G1.GetAsync("/api/patient/dependents/lightning-offers")) Expect(aggregate, 200);
            using (var on = await f.Admin.PatchAsJsonAsync($"/api/obs/units/{u1}/registry/active", new { cpf = Cpf(12), isActive = true, confirmed = true })) Expect(on, 204);
            using (var guardianBack = await f.G1.GetAsync($"{Base(l5, u1)}/appointments?{window}")) { Expect(guardianBack, 200); Require((await Read(guardianBack)).GetProperty("appointments").EnumerateArray().All(a => a.GetProperty("status").GetString() == "cancelled"), "A consulta cancelada pela desativação não volta com a reativação."); }
        });

        // ======== 9. Desativação na UBS ========
        await Check("Desativar o paciente na UBS cancela as consultas futuras scheduled/called como cancelamento da unidade (origem staff, não pesa no score), libera o horário e não cria Vaga Relâmpago; prévia antes", async () =>
        {
            Guid scheduled, called, otherUnit;
            using (var a = await f.Attendant.PostAsJsonAsync($"/api/obs/units/{u1}/appointments", new { cpf = Cpf(31), professionalId = ana, specialtyId = cardiology, startsAt = "2026-11-18T14:00:00Z" })) { Expect(a, 201); scheduled = (await Read(a)).GetProperty("id").GetGuid(); }
            using (var b = await f.Attendant.PostAsJsonAsync($"/api/obs/units/{u1}/appointments", new { cpf = Cpf(31), professionalId = ana, specialtyId = clinical, startsAt = "2026-11-19T13:30:00Z" })) { Expect(b, 201); called = (await Read(b)).GetProperty("id").GetGuid(); }
            using (var c = await f.Attendant.PatchAsJsonAsync($"/api/obs/units/{u1}/appointments/{called}/status", new { status = "called", confirmed = false })) Expect(c, 200);
            await f.UnitExec(u1, "INSERT INTO nora_unit.appointments(id,cpf,specialty,professional,starts_at,ends_at,status,created_by,created_at) VALUES($1,$2,'Histórico','Prof Histórico',$3,$4,'completed',$5,$3)", Guid.NewGuid(), Cpf(31), T0.AddDays(-3), T0.AddDays(-3).AddMinutes(30), f.AdminId);
            otherUnit = Guid.NewGuid();
            await f.UnitExec(u2, "INSERT INTO nora_unit.patient_registry(cpf,full_name,email,created_by) VALUES($1,'Paciente Trinta e Um','p31u2@example.test',$2)", Cpf(31), f.AdminId);
            await f.UnitExec(u2, "INSERT INTO nora_unit.appointments(id,cpf,specialty,professional,starts_at,ends_at,status,created_by,created_at) VALUES($1,$2,'Cardiologia','Prof Outra',$3,$4,'scheduled',$5,$6)", otherUnit, Cpf(31), T0.AddDays(5), T0.AddDays(5).AddMinutes(30), f.AdminId, T0);
            using (var q = await f.Attendant.PostAsJsonAsync($"/api/obs/units/{u1}/waiting-list", new { cpf = Cpf(31), specialty = "Ortopedia" })) Expect(q, 201);
            var slotsBefore = await f.UnitScalar<long>(u1, "SELECT count(*) FROM nora_unit.lightning_slots");
            using (var preview = await f.Attendant.PostAsJsonAsync($"/api/obs/units/{u1}/registry/deactivation-preview", new { cpf = Cpf(31) }))
            { Expect(preview, 200); var body = await Read(preview); Require(body.GetProperty("futureAppointments").GetInt32() == 2 && body.GetProperty("activeWaitingEntries").GetInt32() == 1, $"Prévia: {body}."); }
            using (var noGrant = await f.OtherAttendant.PostAsJsonAsync($"/api/obs/units/{u1}/registry/deactivation-preview", new { cpf = Cpf(31) })) Expect(noGrant, 403);
            using (var patient = await f.G1.PostAsJsonAsync($"/api/obs/units/{u1}/registry/deactivation-preview", new { cpf = Cpf(31) })) Expect(patient, 403);
            using (var unknown = await f.Attendant.PostAsJsonAsync($"/api/obs/units/{u1}/registry/deactivation-preview", new { cpf = Cpf(55) })) Expect(unknown, 404);
            using (var invalid = await f.Attendant.PostAsJsonAsync($"/api/obs/units/{u1}/registry/deactivation-preview", new { cpf = "123" })) Expect(invalid, 400);
            Require(await f.UnitScalar<string>(u1, "SELECT status FROM nora_unit.appointments WHERE id=$1", scheduled) == "scheduled", "A prévia não grava nada.");
            using (var off = await f.Attendant.PatchAsJsonAsync($"/api/obs/units/{u1}/registry/active", new { cpf = Cpf(31), isActive = false, confirmed = true })) Expect(off, 204);
            Require(await f.UnitScalar<long>(u1, "SELECT count(*) FROM nora_unit.appointments WHERE cpf=$1 AND status='cancelled'", Cpf(31)) == 2, "As duas consultas futuras foram canceladas.");
            Require(await f.UnitScalar<long>(u1, "SELECT count(*) FROM nora_unit.appointment_events WHERE appointment_id=ANY($1) AND status='cancelled' AND source='staff' AND actor_id=$2 AND guardian_id IS NULL", new[] { scheduled, called }, f.AttendantId) == 2, "Cancelamento da unidade: origem staff e autor.");
            Require(await f.UnitScalar<long>(u1, "SELECT count(*) FROM nora_unit.appointments WHERE cpf=$1 AND status='completed'", Cpf(31)) == 1, "A consulta passada concluída não é tocada.");
            Require(await f.UnitScalar<string>(u2, "SELECT status FROM nora_unit.appointments WHERE id=$1", otherUnit) == "scheduled", "Consulta de outra UBS não é tocada.");
            Require(await f.UnitScalar<long>(u1, "SELECT count(*) FROM nora_unit.lightning_slots") == slotsBefore, "Nenhuma Vaga Relâmpago é criada automaticamente.");
            using (var registry = await f.Attendant.GetAsync($"/api/obs/units/{u1}/registry"))
            {
                var row = (await Read(registry)).EnumerateArray().Single(p => p.GetProperty("cpf").GetString() == Cpf(31)).GetProperty("attendance");
                Require(row.GetProperty("cancelledByPatient").GetInt64() == 0 && row.GetProperty("cancellationWeight").GetDouble() == 0 && row.GetProperty("completed").GetInt64() == 1 && row.GetProperty("score").GetDouble() == 1.0, $"O cancelamento da unidade não pesa no score: {row}.");
            }
            Require((await FreeSlots(f.Attendant, $"/api/obs/units/{u1}", cardiology, "2026-11-18T00:00:00Z", "2026-11-19T00:00:00Z")).Contains("11-18 14:00"), "O horário cancelado volta à disponibilidade.");
            using (var on = await f.Attendant.PatchAsJsonAsync($"/api/obs/units/{u1}/registry/active", new { cpf = Cpf(31), isActive = true, confirmed = true })) Expect(on, 204);
            Require(await f.UnitScalar<long>(u1, "SELECT count(*) FROM nora_unit.appointments WHERE cpf=$1 AND status='cancelled'", Cpf(31)) == 2, "Reativar não restaura as consultas.");
            using (var after = await f.Attendant.PostAsJsonAsync($"/api/obs/units/{u1}/registry/deactivation-preview", new { cpf = Cpf(31) })) Require((await Read(after)).GetProperty("futureAppointments").GetInt32() == 0, "Depois da reativação não há o que cancelar.");
        });

        // ======== 10. E-mail da conta ========
        await Check("Troca de e-mail: a correção da UBS gera sugestão só para conta vinculada à UBS e só se o e-mail mudou; leitura mascarada; código por e-mail; confirmação troca a conta e o login", async () =>
        {
            using (var none = await f.P21.GetAsync("/api/patient/email-suggestion")) { Expect(none, 200); Require((await Read(none)).GetProperty("suggestion").ValueKind == JsonValueKind.Null, "Sem correção não há sugestão."); }
            using (var correct = await f.Admin.PatchAsJsonAsync($"/api/obs/units/{u1}/registry/email", new { cpf = Cpf(21), email = "Novo21@Example.test" })) Expect(correct, 204);
            using (var read = await f.P21.GetAsync("/api/patient/email-suggestion"))
            {
                Expect(read, 200); var text = await read.Content.ReadAsStringAsync(); using var json = JsonDocument.Parse(text); var suggestion = json.RootElement.GetProperty("suggestion");
                Require(suggestion.GetProperty("maskedEmail").GetString() == "n***@example.test" && suggestion.GetProperty("unitId").GetGuid() == u1 && suggestion.GetProperty("unitName").GetString() == "UBS Família Um" && !text.Contains("novo21", StringComparison.OrdinalIgnoreCase), $"Leitura da sugestão: {text}.");
            }
            // Conta sem vínculo com a UBS, e correção que não muda o e-mail da conta, não geram sugestão.
            using (var unlinked = await f.Admin.PatchAsJsonAsync($"/api/obs/units/{u1}/registry/email", new { cpf = Cpf(22), email = "novo22@example.test" })) Expect(unlinked, 204);
            using (var sameAsAccount = await f.Admin.PatchAsJsonAsync($"/api/obs/units/{u1}/registry/email", new { cpf = Cpf(23), email = "P23@example.test" })) Expect(sameAsAccount, 204);
            using (var noAccount = await f.Admin.PatchAsJsonAsync($"/api/obs/units/{u1}/registry/email", new { cpf = Cpf(11), email = "dependente11@example.test" })) Expect(noAccount, 204);
            Require(await f.Scalar<long>("SELECT count(*) FROM nora.email_change_suggestions") == 1, "Só a conta vinculada ganhou sugestão.");
            using (var staff = await f.Admin.GetAsync("/api/patient/email-suggestion")) Expect(staff, 403);
            using (var anonymous = await f.Anonymous.GetAsync("/api/patient/email-suggestion")) Expect(anonymous, 401);
            // Sem SMTP: 503, e nada é gerado.
            f.Mail.Configured = false;
            using (var unavailable = await f.P21.PostAsync("/api/patient/email-suggestion/send-code", null)) Expect(unavailable, 503);
            f.Mail.Configured = true;
            Require(await f.Scalar<long>("SELECT count(*) FROM nora.email_change_suggestions WHERE code_hash IS NOT NULL") == 0, "Sem SMTP nenhum código foi gerado.");
            using (var send = await f.P21.PostAsync("/api/patient/email-suggestion/send-code", null)) Expect(send, 204);
            var mail = f.Mail.Sent.Single(); var code = Regex.Match(mail.Body, @"\d{6}").Value;
            Require(mail.To == "novo21@example.test" && code.Length == 6, "O código vai só ao endereço novo.");
            var stored = await f.Scalar<string>("SELECT code_hash FROM nora.email_change_suggestions WHERE status='pending' AND code_hash IS NOT NULL");
            Require(stored.Length == 64 && stored != code && !stored.Contains(code, StringComparison.Ordinal), "O código é guardado só como HMAC.");
            using (var wrong = await f.P21.PostAsJsonAsync("/api/patient/email-suggestion/confirm", new { code = code == "000000" ? "111111" : "000000" })) { Expect(wrong, 200); Require(!(await Read(wrong)).GetProperty("codeValid").GetBoolean(), "Código errado."); }
            using (var malformed = await f.P21.PostAsJsonAsync("/api/patient/email-suggestion/confirm", new { code = "12ab56" })) { Expect(malformed, 200); Require(!(await Read(malformed)).GetProperty("codeValid").GetBoolean(), "Código malformado."); }
            using (var other = await f.P24.PostAsJsonAsync("/api/patient/email-suggestion/confirm", new { code })) Expect(other, 404); // outra conta, sem sugestão pendente
            using (var good = await f.P21.PostAsJsonAsync("/api/patient/email-suggestion/confirm", new { code })) { Expect(good, 200); Require((await Read(good)).GetProperty("codeValid").GetBoolean(), "Código certo."); }
            Require(await f.Scalar<string>("SELECT email FROM nora.accounts WHERE id=$1", f.PatientIds[21]) == "novo21@example.test", "O e-mail da conta foi trocado.");
            using (var newLogin = await f.Anonymous.PostAsJsonAsync("/api/auth/login", new { scope = "patient", identifier = "novo21@example.test", password = Password })) Expect(newLogin, 200);
            using (var oldLogin = await f.Anonymous.PostAsJsonAsync("/api/auth/login", new { scope = "patient", identifier = "p21@example.test", password = Password })) Expect(oldLogin, 401);
            using (var gone = await f.P21.GetAsync("/api/patient/email-suggestion")) Require((await Read(gone)).GetProperty("suggestion").ValueKind == JsonValueKind.Null, "A sugestão confirmada some.");
            using (var reuse = await f.P21.PostAsJsonAsync("/api/patient/email-suggestion/confirm", new { code })) Expect(reuse, 404); // uso único
            Require(await f.Scalar<string>("SELECT status FROM nora.email_change_suggestions WHERE account_id=$1", f.PatientIds[21]) == "confirmed", "Sugestão confirmada.");
        });

        await Check("Troca de e-mail: código vence em 10 minutos, 5 tentativas, no máximo 3 envios, envio que falha invalida o código, dispensar, substituir pela correção mais recente e PUT do cadastro", async () =>
        {
            async Task Correct(int number, string email) { using var r = await f.Admin.PatchAsJsonAsync($"/api/obs/units/{u1}/registry/email", new { cpf = Cpf(number), email }); Expect(r, 204); }
            async Task<string> Send(HttpClient client) { var before = f.Mail.Sent.Count; using var r = await client.PostAsync("/api/patient/email-suggestion/send-code", null); Expect(r, 204); return Regex.Match(f.Mail.Sent[before].Body, @"\d{6}").Value; }
            async Task<bool> Confirm(HttpClient client, string code) { using var r = await client.PostAsJsonAsync("/api/patient/email-suggestion/confirm", new { code }); Expect(r, 200); return (await Read(r)).GetProperty("codeValid").GetBoolean(); }
            await Correct(21, "segundo21@example.test");
            var first = await Send(f.P21);
            f.Clock.Set(T0.AddMinutes(11));
            Require(!await Confirm(f.P21, first), "Código vencido não vale.");
            f.Clock.Set(T0);
            var second = await Send(f.P21);
            for (var i = 0; i < 5; i++) Require(!await Confirm(f.P21, second == "999999" ? "888888" : "999999"), "Tentativa errada.");
            Require(!await Confirm(f.P21, second), "Depois de cinco tentativas nem o código certo vale.");
            Require(await f.Scalar<string>("SELECT email FROM nora.accounts WHERE id=$1", f.PatientIds[21]) == "novo21@example.test", "Nada foi trocado.");
            _ = await Send(f.P21); // 3º envio
            using (var fourth = await f.P21.PostAsync("/api/patient/email-suggestion/send-code", null)) Expect(fourth, 429);
            // Falha no envio: 503 e o código fica invalidado (nova sugestão limpa para ter envios).
            await Correct(21, "terceiro21@example.test");
            Require(await f.Scalar<long>("SELECT count(*) FROM nora.email_change_suggestions WHERE account_id=$1 AND status='pending'", f.PatientIds[21]) == 1 && await f.Scalar<long>("SELECT count(*) FROM nora.email_change_suggestions WHERE account_id=$1 AND status='superseded'", f.PatientIds[21]) == 1, "A correção mais recente substitui a pendente.");
            _ = await Send(f.P21);
            Require(await f.Scalar<long>("SELECT count(*) FROM nora.email_change_suggestions WHERE account_id=$1 AND status='pending' AND code_hash IS NOT NULL", f.PatientIds[21]) == 1, "O envio bem-sucedido guardou o código.");
            f.Mail.Fail = true;
            using (var failed = await f.P21.PostAsync("/api/patient/email-suggestion/send-code", null)) Expect(failed, 503);
            f.Mail.Fail = false;
            Require(await f.Scalar<long>("SELECT count(*) FROM nora.email_change_suggestions WHERE account_id=$1 AND status='pending' AND code_hash IS NULL", f.PatientIds[21]) == 1, "O código de um envio que falhou foi invalidado.");
            using (var read = await f.P21.GetAsync("/api/patient/email-suggestion")) Require((await Read(read)).GetProperty("suggestion").GetProperty("maskedEmail").GetString() == "t***@example.test", "A sugestão pendente é a mais recente.");
            using (var noCode = await f.P21.PostAsJsonAsync("/api/patient/email-suggestion/confirm", new { code = "123456" })) { Expect(noCode, 200); Require(!(await Read(noCode)).GetProperty("codeValid").GetBoolean(), "Sem código enviado nada vale."); }
            using (var dismiss = await f.P21.PostAsync("/api/patient/email-suggestion/dismiss", null)) Expect(dismiss, 204);
            using (var again = await f.P21.PostAsync("/api/patient/email-suggestion/dismiss", null)) Expect(again, 404);
            using (var after = await f.P21.GetAsync("/api/patient/email-suggestion")) Require((await Read(after)).GetProperty("suggestion").ValueKind == JsonValueKind.Null, "Dispensada some.");
            Require(await f.Scalar<string>("SELECT email FROM nora.accounts WHERE id=$1", f.PatientIds[21]) == "novo21@example.test", "Dispensar não troca o e-mail.");
            // PUT do cadastro também gera a sugestão, e só quando o e-mail mudou.
            using (var put = await f.Admin.PutAsJsonAsync($"/api/obs/units/{u1}/registry", new { cpf = Cpf(26), fullName = "Paciente Vinte e Seis", email = "novo26@example.test" })) Expect(put, 204);
            var suggestionId = await f.Scalar<Guid>("SELECT id FROM nora.email_change_suggestions WHERE account_id=$1 AND status='pending'", f.PatientIds[26]);
            using (var same = await f.Admin.PutAsJsonAsync($"/api/obs/units/{u1}/registry", new { cpf = Cpf(26), fullName = "Paciente Vinte e Seis Editado", email = "novo26@example.test" })) Expect(same, 204);
            Require(await f.Scalar<Guid>("SELECT id FROM nora.email_change_suggestions WHERE account_id=$1 AND status='pending'", f.PatientIds[26]) == suggestionId && await f.Scalar<long>("SELECT count(*) FROM nora.email_change_suggestions WHERE account_id=$1", f.PatientIds[26]) == 1, "Edição sem mudar o e-mail não mexe na sugestão.");
        });

        await Check("Troca de e-mail: e-mail já usado por outra conta de paciente → 409, nada é trocado e a sugestão continua pendente", async () =>
        {
            using (var correct = await f.Admin.PatchAsJsonAsync($"/api/obs/units/{u1}/registry/email", new { cpf = Cpf(24), email = "p25@example.test" })) Expect(correct, 204);
            using (var send = await f.P24.PostAsync("/api/patient/email-suggestion/send-code", null)) Expect(send, 204);
            var code = Regex.Match(f.Mail.Sent[^1].Body, @"\d{6}").Value;
            using (var conflict = await f.P24.PostAsJsonAsync("/api/patient/email-suggestion/confirm", new { code })) Expect(conflict, 409);
            Require(await f.Scalar<string>("SELECT email FROM nora.accounts WHERE id=$1", f.PatientIds[24]) == "p24@example.test" && await f.Scalar<long>("SELECT count(*) FROM nora.email_change_suggestions WHERE account_id=$1 AND status='pending'", f.PatientIds[24]) == 1, "Conflito de unicidade preserva tudo.");
        });

        // ======== 11. Leitura de ofertas e limpeza ========
        await Check("A leitura das ofertas (própria, por dependente e agregada) não altera estado: nem oferta vencida, nem vaga, nem fila, nem eventos; quem redistribui é o worker/dispatcher", async () =>
        {
            using (var queue = await f.Attendant.PostAsJsonAsync($"/api/obs/units/{u1}/waiting-list", new { cpf = Cpf(51), specialty = "Cardiologia" })) Expect(queue, 201);
            Guid source;
            using (var created = await f.Attendant.PostAsJsonAsync($"/api/obs/units/{u1}/appointments", new { cpf = Cpf(41), professionalId = ana, specialtyId = cardiology, startsAt = "2026-11-18T14:30:00Z" })) { Expect(created, 201); source = (await Read(created)).GetProperty("id").GetGuid(); }
            using (var cancelled = await f.Attendant.PatchAsJsonAsync($"/api/obs/units/{u1}/appointments/{source}/status", new { status = "cancelled", confirmed = true })) Expect(cancelled, 200);
            using (var slot = await f.Attendant.PostAsJsonAsync($"/api/obs/units/{u1}/lightning-slots", new { appointmentId = source, confirmed = true })) Expect(slot, 201);
            const string snapshot = "SELECT COALESCE((SELECT string_agg(status,',' ORDER BY id) FROM nora_unit.lightning_offers),'')||'|'||COALESCE((SELECT string_agg(status,',' ORDER BY id) FROM nora_unit.lightning_slots),'')||'|'||COALESCE((SELECT string_agg(status,',' ORDER BY id) FROM nora_unit.waiting_list),'')||'|'||(SELECT count(*) FROM nora_unit.waiting_list_events)||'|'||(SELECT count(*) FROM nora_unit.lightning_offers)";
            Require((await f.UnitScalar<string>(u1, snapshot)).Contains("pending", StringComparison.Ordinal), "Há uma oferta pendente para P51.");
            f.Clock.Set(T0.AddMinutes(11)); // o prazo de 10 minutos passou
            var before = await f.UnitScalar<string>(u1, snapshot);
            using (var own = await f.P51.GetAsync($"/api/patient/units/{u1}/lightning-offers")) { Expect(own, 200); Require((await Read(own)).GetProperty("offers").EnumerateArray().Any(o => o.GetProperty("status").GetString() == "pending"), "A leitura mostra o que está gravado."); }
            using (var byDependent = await f.G1.GetAsync($"{Base(l1, u1)}/lightning-offers")) Expect(byDependent, 200);
            using (var aggregate = await f.G1.GetAsync("/api/patient/dependents/lightning-offers")) Expect(aggregate, 200);
            Require(await f.UnitScalar<string>(u1, snapshot) == before, "Ler as ofertas não pode alterar estado.");
            await f.App.Services.GetRequiredService<UnitLightningDispatcher>().AdvanceAsync(u1, default);
            Require(await f.UnitScalar<string>(u1, snapshot) != before, "O dispatcher (worker) é quem redistribui e expira.");
            f.Clock.Set(T0);
        });

        await Check("Limpeza periódica: sessões revogadas ou vencidas há mais de 30 dias e desafios vencidos há mais de 1 dia, em lotes, sem esperar linhas travadas", async () =>
        {
            var maintenance = f.App.Services.GetRequiredService<MaintenanceService>();
            string Token() => Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
            async Task Session(string token, DateTimeOffset expires, DateTimeOffset? revoked) => await f.Exec("INSERT INTO nora.sessions(token_hash,account_id,expires_at,revoked_at) VALUES($1,$2,$3,$4::timestamptz)", token, f.PatientIds[3], expires, revoked!);
            async Task<bool> Exists(string token) => await f.Scalar<long>("SELECT count(*) FROM nora.sessions WHERE token_hash=$1", token) == 1;
            string expired31 = Token(), revoked31 = Token(), revoked29 = Token(), expired29 = Token(), live = Token();
            await Session(expired31, T0.AddDays(-31), null); await Session(revoked31, T0.AddDays(1), T0.AddDays(-31)); await Session(revoked29, T0.AddDays(1), T0.AddDays(-29)); await Session(expired29, T0.AddDays(-29), null); await Session(live, T0.AddHours(8), null);
            var bulk = Enumerable.Range(0, 25).Select(_ => Token()).ToList();
            foreach (var token in bulk) await Session(token, T0.AddDays(-40), null);
            var liveBefore = await f.Scalar<long>("SELECT count(*) FROM nora.sessions WHERE revoked_at IS NULL AND expires_at>$1", T0);
            // Desafios de cadastro e de recuperação.
            async Task Challenge(Guid id, DateTimeOffset expires) => await f.Exec("INSERT INTO nora.patient_registration_challenges(id,cpf,full_name,password_hash,unit_ids,created_at,expires_at) VALUES($1,$2,'Teste','x',$3,$4,$5)", id, Cpf(70), new[] { u1 }, expires.AddMinutes(-15), expires);
            async Task Reset(Guid id, DateTimeOffset expires) => await f.Exec("INSERT INTO nora.patient_password_reset_codes(challenge_id,account_id,code_hash,created_at,expires_at) VALUES($1,$2,$3,$4,$5)", id, f.PatientIds[3], new string('a', 64), expires.AddMinutes(-10), expires);
            Guid oldChallenge = Guid.NewGuid(), freshChallenge = Guid.NewGuid(), oldReset = Guid.NewGuid(), freshReset = Guid.NewGuid();
            await Challenge(oldChallenge, T0.AddDays(-2)); await Challenge(freshChallenge, T0.AddHours(-12)); await Reset(oldReset, T0.AddDays(-2)); await Reset(freshReset, T0.AddHours(-12));
            var result = await maintenance.RunOnceAsync(default, batchSize: 10);
            Require(result.Sessions == 27 && result.RegistrationChallenges == 1 && result.PasswordResetCodes == 1, $"Contagens da limpeza: {result}.");
            Require(!await Exists(expired31) && !await Exists(revoked31) && await f.Scalar<long>("SELECT count(*) FROM nora.sessions WHERE token_hash=ANY($1)", (object)bulk.ToArray()) == 0 && await Exists(revoked29) && await Exists(expired29) && await Exists(live), "Só as sessões revogadas ou vencidas há mais de 30 dias saem.");
            Require(await f.Scalar<long>("SELECT count(*) FROM nora.sessions WHERE revoked_at IS NULL AND expires_at>$1", T0) == liveBefore, "Nenhuma sessão válida foi apagada.");
            Require(await f.Scalar<long>("SELECT count(*) FROM nora.patient_registration_challenges WHERE id=$1", oldChallenge) == 0 && await f.Scalar<long>("SELECT count(*) FROM nora.patient_registration_challenges WHERE id=$1", freshChallenge) == 1 && await f.Scalar<long>("SELECT count(*) FROM nora.patient_password_reset_codes WHERE challenge_id=$1", oldReset) == 0 && await f.Scalar<long>("SELECT count(*) FROM nora.patient_password_reset_codes WHERE challenge_id=$1", freshReset) == 1, "Desafios vencidos há mais de 1 dia saem; os recentes ficam.");
            var second = await maintenance.RunOnceAsync(default); Require(second == new MaintenanceResult(0, 0, 0), "Uma segunda passada não encontra nada.");
            // Sem lock longo: uma linha travada por outra transação é pulada, não esperada.
            var locked = Token(); await Session(locked, T0.AddDays(-40), null);
            await using (var holder = await f.OpenCentral())
            await using (var transaction = await holder.BeginTransactionAsync())
            {
                await using (var hold = f.Command(holder, "SELECT 1 FROM nora.sessions WHERE token_hash=$1 FOR UPDATE", locked)) await hold.ExecuteScalarAsync();
                var run = maintenance.RunOnceAsync(default); var finished = await Task.WhenAny(run, Task.Delay(TimeSpan.FromSeconds(8))) == run;
                Require(finished && run.Result.Sessions == 0, "A limpeza não espera linha travada.");
                await transaction.RollbackAsync();
            }
            Require((await maintenance.RunOnceAsync(default)).Sessions == 1 && !await Exists(locked), "Liberada a linha, a próxima passada a apaga.");
        });

        Console.WriteLine($"{checks - failures}/{checks} verificações da Família, e-mail e manutenção passaram.");
        return failures;
    }
}
