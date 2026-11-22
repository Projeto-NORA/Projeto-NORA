using System.Net.Http.Json;
using System.Text.Json;
using Nora.Api.DAOs;
using static RoundFixture;

/// <summary>
/// Aprovação opcional das vagas reaproveitadas, por UBS: com a chave ligada o cancelamento gera uma vaga que aguarda a aprovação da equipe (não é ofertada, não segura o horário) e fecha se ninguém aprovar até faltar 1 h.
/// Roda com <c>--slot-approval-only</c>. O relógio só anda para a frente: as verificações seguem a ordem do tempo (a linha do tempo de segunda-feira vem por último).
/// </summary>
internal static class SlotApprovalChecks
{
    public static async Task<int> RunAsync(string root)
    {
        var connection = Environment.GetEnvironmentVariable("NORA_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection))
        {
            Console.WriteLine("SKIP aprovação das vagas: configure NORA_TEST_CONNECTION somente para uma base descartável.");
            return 0;
        }
        var suite = new Suite("aprovação das vagas");
        await using var f = await StartAsync(connection, "approval");
        var u1 = f.Unit1; var u2 = f.Unit2;
        var cardiology = await f.SpecialtyAsync("Cardiologia");
        var ana = await f.ProfessionalAsync(u1, "Dra. Ana Souza", cardiology); var room = await f.RoomAsync(u1, "Sala 1");
        await f.TemplateAsync(u1, ana, 3, "08:00", "10:00", 30, room, cardiology);   // quarta: 08:00 08:30 09:00 09:30
        await f.TemplateAsync(u1, ana, 4, "08:00", "10:00", 30, room, cardiology);   // quinta
        await f.TemplateAsync(u1, ana, 1, "14:00", "16:00", 30, room, cardiology);   // segunda (hoje): 14:00 14:30 15:00 15:30 locais = 17:00Z a 18:30Z
        for (var number = 1; number <= 9; number++) await f.PatientAsync(number, u1);
        var (_, attendantDevice) = await ObsDeviceHelper.ActivateAsync(f.Admin, u1);
        var attendant = f.NewClient(); await f.LoginAsync(attendant, "obs", "atendente@example.test", attendantDevice);
        var wed = new[] { "08:00", "08:30", "09:00", "09:30" }.Select(Wednesday).ToArray();
        var thu = wed.Select(start => start.AddDays(1)).ToArray();
        var mon = new[] { 14, 14, 15, 15 }.Zip(new[] { 0, 30, 0, 30 }, (hour, minute) => new DateTimeOffset(2026, 11, 16, hour, minute, 0, TimeSpan.FromHours(-3)).ToUniversalTime()).ToArray();

        async Task<Guid> StaffBook(int patient, DateTimeOffset start)
        {
            using var response = await f.Admin.PostAsJsonAsync($"/api/obs/units/{u1}/appointments", new { cpf = Cpf(patient), professionalId = ana, specialtyId = cardiology, startsAt = start });
            Expect(response, 201); return (await Read(response)).GetProperty("id").GetGuid();
        }
        async Task<Guid> PatientBook(int patient, DateTimeOffset start)
        {
            using var response = await f.Patients[patient].PostAsJsonAsync($"/api/patient/units/{u1}/appointments", new { professionalId = ana, specialtyId = cardiology, startsAt = start });
            Expect(response, 201); return (await Read(response)).GetProperty("id").GetGuid();
        }
        async Task StaffCancel(Guid appointment)
        {
            using var response = await f.Admin.PatchAsJsonAsync($"/api/obs/units/{u1}/appointments/{appointment}/status", new { status = "cancelled", confirmed = true }); Expect(response, 200);
        }
        async Task<JsonElement> Slots(Guid unit) { using var response = await f.Admin.GetAsync($"/api/obs/units/{unit}/lightning-slots"); Expect(response, 200); return (await Read(response)).GetProperty("items"); }
        async Task<JsonElement> SlotOf(Guid appointment) => (await Slots(u1)).EnumerateArray().First(slot => slot.GetProperty("appointmentId").ValueKind == JsonValueKind.String && slot.GetProperty("appointmentId").GetGuid() == appointment);
        async Task<HttpResponseMessage> Approve(HttpClient client, Guid slot, Guid? unit = null, bool confirmed = true) => await client.PostAsJsonAsync($"/api/obs/units/{unit ?? u1}/lightning-slots/{slot}/approve", new { confirmed });
        async Task<HttpResponseMessage> Discard(HttpClient client, Guid slot, bool confirmed = true) => await client.PostAsJsonAsync($"/api/obs/units/{u1}/lightning-slots/{slot}/discard", new { confirmed });
        async Task<string> Row(Guid slot) => await f.UnitScalar<string>(u1, "SELECT status||':'||origin||':'||coalesce(closed_reason,'-') FROM nora_unit.lightning_slots WHERE id=$1", slot);
        async Task SetApproval(bool enabled)
        {
            using var response = await f.Admin.PutAsJsonAsync($"/api/obs/units/{u1}/slot-approval", new { enabled, confirmed = true }); Expect(response, 200);
        }
        async Task Join(int patient)
        {
            using var response = await f.Admin.PostAsJsonAsync($"/api/obs/units/{u1}/waiting-list", new { cpf = Cpf(patient), specialty = "Cardiologia" }); Expect(response, 201);
        }

        Guid appP1 = Guid.Empty, appP2 = Guid.Empty, appP3 = Guid.Empty, appP4 = Guid.Empty, beforeSince = Guid.Empty;
        await suite.Check("Desligada (padrão): cancelamento não gera vaga; a chave só liga pelo administrador, com confirmação, idempotente e com o instante guardado", async () =>
        {
            appP1 = await PatientBook(1, wed[0]); appP2 = await StaffBook(2, wed[1]); appP3 = await PatientBook(3, wed[2]); appP4 = await PatientBook(4, wed[3]); beforeSince = await StaffBook(5, thu[0]);
            await StaffCancel(beforeSince);
            await f.AdvanceAsync(u1);
            Ensure(await f.UnitScalar<long>(u1, "SELECT count(*) FROM nora_unit.lightning_slots") == 0, "Com a chave desligada nenhuma vaga nasce do cancelamento.");
            using (var read = await attendant.GetAsync($"/api/obs/units/{u1}/profile")) { Expect(read, 200); var body = await Read(read); Ensure(!body.GetProperty("slotApprovalRequired").GetBoolean() && body.GetProperty("slotApprovalSince").ValueKind == JsonValueKind.Null, "Chave desligada no perfil."); }
            using (var denied = await attendant.PutAsJsonAsync($"/api/obs/units/{u1}/slot-approval", new { enabled = true, confirmed = true })) Expect(denied, 403);
            using (var noConfirm = await f.Admin.PutAsJsonAsync($"/api/obs/units/{u1}/slot-approval", new { enabled = true, confirmed = false })) Expect(noConfirm, 400);
            using (var noFields = await f.Admin.PutAsJsonAsync($"/api/obs/units/{u1}/slot-approval", new { enabled = true })) Expect(noFields, 400);
            using (var manager = await f.Municipal.PutAsJsonAsync($"/api/obs/units/{u1}/slot-approval", new { enabled = true, confirmed = true })) Expect(manager, 403);
            var other = await f.StaffAsync("administrator", 41, [u2]);
            using (var noGrant = await other.Client.PutAsJsonAsync($"/api/obs/units/{u1}/slot-approval", new { enabled = true, confirmed = true })) Expect(noGrant, 403);
            f.Clock.Advance(TimeSpan.FromMinutes(1));
            using (var on = await f.Admin.PutAsJsonAsync($"/api/obs/units/{u1}/slot-approval", new { enabled = true, confirmed = true }))
            {
                Expect(on, 200); var body = await Read(on);
                Ensure(body.GetProperty("enabled").GetBoolean() && body.GetProperty("since").GetDateTimeOffset() == RoundClock.T0.AddMinutes(1) && body.GetProperty("updatedByName").GetString() == "Administrador da rodada", "Ligar guarda o instante.");
            }
            f.Clock.Advance(TimeSpan.FromMinutes(1));
            using (var again = await f.Admin.PutAsJsonAsync($"/api/obs/units/{u1}/slot-approval", new { enabled = true, confirmed = true })) Ensure((await Read(again)).GetProperty("since").GetDateTimeOffset() == RoundClock.T0.AddMinutes(1), "Ligar de novo mantém o instante.");
            using (var read = await attendant.GetAsync($"/api/obs/units/{u1}/profile")) { var body = await Read(read); Ensure(body.GetProperty("slotApprovalRequired").GetBoolean() && body.GetProperty("slotApprovalSince").GetDateTimeOffset() == RoundClock.T0.AddMinutes(1), "A atendente vê o estado em leitura."); }
        });

        Guid slotP1 = Guid.Empty, slotP2 = Guid.Empty, slotP3 = Guid.Empty, slotP4 = Guid.Empty;
        await suite.Check("Ligada: cancelamento pelo paciente, pela equipe, pela desativação e pela remoção de vínculo geram proposta com a origem certa; antes de ligar, a menos de 1 h ou sem mudança não geram; não é ofertada nem segura o horário", async () =>
        {
            // Quem aguarda na fila poderia receber: a proposta não é ofertada antes de aprovada.
            await Join(7);
            using (var cancel = await f.Patients[1].PostAsJsonAsync($"/api/patient/units/{u1}/appointments/{appP1}/cancel", new { confirmed = true })) Expect(cancel, 200);
            await StaffCancel(appP2);
            using (var deactivate = await f.Admin.PatchAsJsonAsync($"/api/obs/units/{u1}/registry/active", new { cpf = Cpf(3), isActive = false, confirmed = true })) Expect(deactivate, 204);
            using (var unlink = await f.Patients[4].PostAsJsonAsync($"/api/patient/units/{u1}/unlink", new { confirmed = true })) Expect(unlink, 200);
            // A menos de 1 h: cancelada depois de ligar, mas a consulta é daqui a 30 minutos.
            var soon = Guid.NewGuid(); var now = f.Clock.GetUtcNow();
            await f.UnitExec(u1, "INSERT INTO nora_unit.appointments(id,cpf,specialty,professional,starts_at,ends_at,status,created_by,created_at) VALUES($1,$2,'Cardiologia','Dra. Ana Souza',$3,$4,'cancelled',$5,$6)", soon, Cpf(8), now.AddMinutes(30), now.AddMinutes(60), f.AdminId, now);
            await f.UnitExec(u1, "INSERT INTO nora_unit.appointment_events(appointment_id,actor_id,status,occurred_at,source) VALUES($1,$2,'cancelled',$3,'staff')", soon, f.AdminId, now);
            await f.AdvanceAsync(u1);
            slotP1 = (await SlotOf(appP1)).GetProperty("id").GetGuid(); slotP2 = (await SlotOf(appP2)).GetProperty("id").GetGuid(); slotP3 = (await SlotOf(appP3)).GetProperty("id").GetGuid(); slotP4 = (await SlotOf(appP4)).GetProperty("id").GetGuid();
            var all = (await Slots(u1)).EnumerateArray().ToList();
            Ensure(all.Count == 4 && all.All(slot => slot.GetProperty("status").GetString() == "awaiting_approval" && slot.GetProperty("origin").GetString() == "cancellation" && slot.GetProperty("appointmentId").ValueKind == JsonValueKind.String), "Quatro propostas aguardando aprovação; as de antes da chave e a de menos de 1 h não nasceram.");
            Ensure(await f.UnitScalar<long>(u1, "SELECT count(*) FROM nora_unit.lightning_slots WHERE appointment_id=ANY($1)", new[] { beforeSince, soon }) == 0, "Cancelamento anterior à chave e consulta a menos de 1 h não geram proposta.");
            var one = await SlotOf(appP1); var source = one.GetProperty("source");
            Ensure(one.GetProperty("approvalDeadline").GetDateTimeOffset() == wed[0].AddHours(-1) && one.GetProperty("decidedAt").ValueKind == JsonValueKind.Null && one.GetProperty("decidedByName").ValueKind == JsonValueKind.Null && one.GetProperty("closedReason").ValueKind == JsonValueKind.Null
                && source.GetProperty("patientName").GetString() == "Paciente 1" && source.GetProperty("appointmentId").GetGuid() == appP1 && source.GetProperty("cancelledAt").GetDateTimeOffset() == f.Clock.GetUtcNow() && source.GetProperty("cancelledBy").GetString() == "patient"
                && one.GetProperty("professional").GetString() == "Dra. Ana Souza" && one.GetProperty("specialty").GetString() == "Cardiologia" && one.GetProperty("startsAt").GetDateTimeOffset() == wed[0] && one.GetProperty("createdAt").GetDateTimeOffset() == f.Clock.GetUtcNow(), "Vaga da consulta cancelada pelo paciente.");
            Ensure((await SlotOf(appP2)).GetProperty("source").GetProperty("cancelledBy").GetString() == "staff" && (await SlotOf(appP3)).GetProperty("source").GetProperty("cancelledBy").GetString() == "staff", "Cancelamento pela equipe e pela desativação vêm como staff.");
            foreach (var appointment in new[] { appP1, appP4 })
                Ensure((await SlotOf(appointment)).GetProperty("source").GetProperty("cancelledBy").GetString() == await f.UnitScalar<string>(u1, "SELECT source FROM nora_unit.appointment_events WHERE appointment_id=$1 AND status='cancelled' ORDER BY id DESC LIMIT 1", appointment), "cancelledBy vem do último evento cancelled.");
            Ensure(await f.UnitScalar<long>(u1, "SELECT count(*) FROM nora_unit.lightning_offers") == 0 && await f.UnitScalar<string>(u1, "SELECT status FROM nora_unit.waiting_list WHERE cpf=$1", Cpf(7)) == "waiting", "Proposta não é ofertada.");
            using (var availability = await f.Patients[6].GetAsync($"/api/patient/units/{u1}/availability?from=2026-11-18T00:00:00Z&to=2026-11-19T00:00:00Z&specialtyId={cardiology}"))
            {
                Expect(availability, 200); var starts = (await Read(availability)).GetProperty("slots").EnumerateArray().Select(slot => slot.GetProperty("startsAt").GetDateTimeOffset()).ToList();
                Ensure(starts.Contains(wed[0]) && starts.Contains(wed[1]), "A proposta não segura o horário: ele continua na agenda do app.");
            }
            using (var summary = await f.Municipal.GetAsync($"/api/municipal/summary?unitId={u1}"))
            {
                Expect(summary, 200); var body = await Read(summary);
                Ensure(body.GetProperty("awaitingApprovalSlots").GetInt64() == 4 && body.GetProperty("registeredSlots").GetInt64() == 0 && body.GetProperty("openSlots").GetInt64() == 0 && body.GetProperty("approvedSlots").GetInt64() == 0, "Proposta que nunca foi liberada não conta nas vagas registradas nem abertas.");
            }
        });

        await suite.Check("Aprovar e descartar: a atendente aprova (vaga open, oferta ao 1º da fila), o administrador descarta; repetir, confirmação, outra UBS e inexistente são recusados", async () =>
        {
            using (var noConfirm = await Approve(attendant, slotP1, confirmed: false)) Expect(noConfirm, 400);
            using (var approve = await Approve(attendant, slotP1))
            {
                Expect(approve, 200); var body = await Read(approve);
                Ensure(body.GetProperty("id").GetGuid() == slotP1 && body.GetProperty("status").GetString() == "open" && body.GetProperty("decidedAt").GetDateTimeOffset() == f.Clock.GetUtcNow() && body.GetProperty("decidedByName").GetString() == "Atendente da rodada" && body.GetProperty("approvalDeadline").ValueKind == JsonValueKind.Null,
                    "Aprovar devolve a vaga aberta com quem decidiu.");
            }
            // A aprovação já redistribuiu: o 7º da fila (conta vinculada, sem consulta no horário) recebeu a oferta.
            Ensure(await f.UnitScalar<string>(u1, "SELECT s.status FROM nora_unit.lightning_slots s WHERE s.id=$1", slotP1) == "offered" && await f.UnitScalar<string>(u1, "SELECT q.status FROM nora_unit.waiting_list q WHERE q.cpf=$1", Cpf(7)) == "offered", "Depois de aprovada a vaga é ofertada ao 1º da fila.");
            using (var again = await Approve(attendant, slotP1)) await ExpectProblem(again, 409, "conflict");
            using (var discard = await Discard(f.Admin, slotP2))
            {
                Expect(discard, 200); var body = await Read(discard);
                Ensure(body.GetProperty("status").GetString() == "closed" && body.GetProperty("closedReason").GetString() == "discarded" && body.GetProperty("decidedByName").GetString() == "Administrador da rodada", "Descartar fecha a vaga.");
            }
            using (var discardAgain = await Discard(f.Admin, slotP2)) await ExpectProblem(discardAgain, 409, "conflict");
            using (var approveClosed = await Approve(f.Admin, slotP2)) await ExpectProblem(approveClosed, 409, "conflict");
            using (var discardOffered = await Discard(f.Admin, slotP1)) await ExpectProblem(discardOffered, 409, "conflict");
            using (var discardNoConfirm = await Discard(f.Admin, slotP3, confirmed: false)) Expect(discardNoConfirm, 400);
            using (var unknown = await Approve(attendant, Guid.NewGuid())) Expect(unknown, 404);
            using (var otherUnit = await Approve(f.Admin, slotP3, u2)) Expect(otherUnit, 404);
            using (var attendantOtherUnit = await Approve(attendant, slotP3, u2)) Expect(attendantOtherUnit, 403);
            using (var manager = await Approve(f.Municipal, slotP3)) Expect(manager, 403);
            using (var patient = await Approve(f.Patients[5], slotP3)) Expect(patient, 403);
            using (var anonymous = await Approve(f.Anonymous, slotP3)) Expect(anonymous, 401);
        });

        await suite.Check("Horário reocupado antes da aprovação: a aprovação fecha a vaga (conflict) e responde 409 slot-unavailable; o resto continua", async () =>
        {
            _ = await PatientBook(6, wed[3]); // a UBS do 4º foi liberada pela remoção do vínculo; o 6º marca o mesmo horário pelo app
            using (var approve = await Approve(attendant, slotP4)) await ExpectProblem(approve, 409, "slot-unavailable");
            Ensure(await Row(slotP4) == "closed:cancellation:conflict", "A vaga ficou fechada por conflito (gravado, mesmo com a recusa).");
        });

        await suite.Check("Manual com a chave ligada nasce aberta; proposta já existente → 409 conflict; desligar não gera mais propostas e as que aguardam continuam aprováveis; ligar de novo só vale para os cancelamentos seguintes", async () =>
        {
            // Manual: cancela uma consulta e cria a vaga antes do passe do distribuidor.
            var manualAppointment = await StaffBook(5, thu[1]); await StaffCancel(manualAppointment);
            using (var manual = await f.Admin.PostAsJsonAsync($"/api/obs/units/{u1}/lightning-slots", new { appointmentId = manualAppointment, confirmed = true }))
            {
                Expect(manual, 201); var body = await Read(manual);
                Ensure(body.GetProperty("status").GetString() is "open" or "offered" && body.GetProperty("origin").GetString() == "staff" && body.GetProperty("decidedAt").ValueKind == JsonValueKind.Null, "A vaga manual nasce aberta (já é a aprovação da equipe).");
            }
            await f.AdvanceAsync(u1);
            Ensure(await f.UnitScalar<long>(u1, "SELECT count(*) FROM nora_unit.lightning_slots WHERE appointment_id=$1", manualAppointment) == 1, "Nenhuma proposta duplicada sobre a vaga manual.");
            using (var onProposal = await f.Admin.PostAsJsonAsync($"/api/obs/units/{u1}/lightning-slots", new { appointmentId = appP3, confirmed = true })) await ExpectProblem(onProposal, 409, "conflict");
            // Desligar: a proposta do 3º (aguardando) continua aprovável; um cancelamento novo não gera proposta.
            await SetApproval(false);
            using (var off = await attendant.GetAsync($"/api/obs/units/{u1}/profile")) Ensure(!(await Read(off)).GetProperty("slotApprovalRequired").GetBoolean(), "Chave desligada.");
            var whileOff = await StaffBook(5, thu[2]); await StaffCancel(whileOff); await f.AdvanceAsync(u1);
            Ensure(await f.UnitScalar<long>(u1, "SELECT count(*) FROM nora_unit.lightning_slots WHERE appointment_id=$1", whileOff) == 0, "Desligada: não nascem propostas.");
            // Ligar de novo: o cancelamento feito enquanto estava desligada não vira proposta.
            f.Clock.Advance(TimeSpan.FromMinutes(1)); await SetApproval(true);
            await f.AdvanceAsync(u1);
            Ensure(await f.UnitScalar<long>(u1, "SELECT count(*) FROM nora_unit.lightning_slots WHERE appointment_id=$1", whileOff) == 0, "O cancelamento anterior ao novo instante não gera proposta.");
            using (var approveOldProposal = await Approve(f.Admin, slotP3)) Expect(approveOldProposal, 200);
        });

        await suite.Check("Resumo da rede e indicadores: aguardando, aprovadas, descartadas e fechadas por prazo; vaga que nunca foi liberada não conta nas registradas nem nas abertas", async () =>
        {
            using var summary = await f.Municipal.GetAsync($"/api/municipal/summary?unitId={u1}"); Expect(summary, 200); var body = await Read(summary);
            var awaiting = await f.UnitScalar<long>(u1, "SELECT count(*) FROM nora_unit.lightning_slots WHERE status='awaiting_approval'");
            var released = await f.UnitScalar<long>(u1, "SELECT count(*) FROM nora_unit.lightning_slots s WHERE s.status<>'awaiting_approval' AND coalesce(s.closed_reason,'') NOT IN ('approval-timeout','discarded','conflict')");
            Ensure(body.GetProperty("awaitingApprovalSlots").GetInt64() == awaiting && body.GetProperty("discardedSlots").GetInt64() == 1 && body.GetProperty("approvalTimeoutSlots").GetInt64() == 0 && body.GetProperty("approvedSlots").GetInt64() == 2 && body.GetProperty("registeredSlots").GetInt64() == released,
                $"Resumo: {body.GetRawText()}");
            using var all = await f.Municipal.GetAsync("/api/municipal/summary"); Expect(all, 200);
            Ensure((await Read(all)).GetProperty("approvedSlots").GetInt64() == 2, "Sem filtro, a rede soma as UBS.");
            using var metrics = await f.Admin.GetAsync($"/api/obs/units/{u1}/metrics?from=2026-11-01T00:00:00Z&to=2026-12-31T00:00:00Z"); Expect(metrics, 200);
            Ensure((await Read(metrics)).GetProperty("lightning").GetProperty("registeredSlots").GetInt64() == released, "Os indicadores da UBS não contam vaga que nunca foi liberada.");
        });

        await suite.Check("Prazo de 1 h: o worker fecha a proposta que ninguém aprovou (approval-timeout); aprovar depois disso → 409 approval-expired; no último segundo ainda aprova; aprovação concorrente com o prazo termina consistente", async () =>
        {
            var m0 = await StaffBook(5, mon[0]); var m1 = await StaffBook(5, mon[1]); var m2 = await StaffBook(5, mon[2]); var m3 = await StaffBook(5, mon[3]);
            foreach (var appointment in new[] { m0, m1, m2, m3 }) await StaffCancel(appointment);
            await f.AdvanceAsync(u1);
            var s0 = (await SlotOf(m0)).GetProperty("id").GetGuid(); var s1 = (await SlotOf(m1)).GetProperty("id").GetGuid(); var s2 = (await SlotOf(m2)).GetProperty("id").GetGuid(); var s3 = (await SlotOf(m3)).GetProperty("id").GetGuid();
            // O worker (aqui, o passe do distribuidor) fecha a de 14:00 locais (prazo 16:00Z) e deixa as demais.
            f.Clock.Set(mon[0].AddHours(-1));
            await f.AdvanceAsync(u1);
            Ensure(await Row(s0) == "closed:cancellation:approval-timeout" && await Row(s1) == "awaiting_approval:cancellation:-", "O prazo fecha só a proposta que chegou a 1 h.");
            // Aprovar a fechada → 409 approval-expired; aprovar a que ainda tem prazo funciona.
            using (var expired = await Approve(f.Admin, s0)) await ExpectProblem(expired, 409, "conflict");
            f.Clock.Set(mon[1].AddHours(-1));
            using (var late = await Approve(f.Admin, s1)) await ExpectProblem(late, 409, "approval-expired");
            Ensure(await Row(s1) == "closed:cancellation:approval-timeout", "Aprovar no prazo exato fecha a vaga (gravado) e responde approval-expired.");
            f.Clock.Set(mon[2].AddHours(-1) - TimeSpan.FromSeconds(1));
            using (var lastSecond = await Approve(f.Admin, s2)) Expect(lastSecond, 200);
            Ensure(await Row(s2) is "open:cancellation:-" or "offered:cancellation:-", "No último segundo a aprovação ainda vale.");
            // Aprovação concorrente com o worker no instante do prazo: o estado final é consistente (fechada por prazo) e a aprovação não derruba nada.
            f.Clock.Set(mon[3].AddHours(-1));
            var approve = Approve(f.Admin, s3); var advance = f.AdvanceAsync(u1);
            using var response = await approve; await advance;
            Ensure((int)response.StatusCode == 409 && await Row(s3) == "closed:cancellation:approval-timeout", $"Prazo concorrente: {(int)response.StatusCode} {await Row(s3)}.");
            using (var summary = await f.Municipal.GetAsync($"/api/municipal/summary?unitId={u1}")) Ensure((await Read(summary)).GetProperty("approvalTimeoutSlots").GetInt64() == 3, "O resumo conta as fechadas por prazo.");
        });

        await suite.Check("Aprovação concorrente: duas aprovações da mesma vaga → uma 200 e uma 409", async () =>
        {
            f.Clock.Set(RoundClock.T0.AddMinutes(10));
            var appointment = await StaffBook(5, thu[3]); await StaffCancel(appointment); f.Clock.Advance(TimeSpan.FromSeconds(1)); await f.AdvanceAsync(u1);
            var slot = (await SlotOf(appointment)).GetProperty("id").GetGuid();
            var results = await Task.WhenAll(Enumerable.Range(0, 2).Select(async _ => { using var response = await Approve(f.Admin, slot); return (int)response.StatusCode; }));
            Ensure(results.Count(status => status == 200) == 1 && results.Count(status => status == 409) == 1, $"Duas aprovações: {string.Join(",", results)}.");
        });
        return suite.Report() == 0 ? 0 : 1;
    }
}
