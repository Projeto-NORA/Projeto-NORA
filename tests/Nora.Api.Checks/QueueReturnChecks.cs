using System.Net.Http.Json;
using System.Text.Json;
using static RoundFixture;

/// <summary>
/// R-030 (2026-11-17): na Vaga Relâmpago, recusar a oferta ou deixá-la vencer devolve a pessoa à fila na MESMA posição (<c>waiting</c>, <c>entered_at</c> intacto); só aceitar, cancelar a entrada ou a UBS tirar a retira.
/// A mesma pessoa não recebe de novo a oferta da MESMA vaga. Vale para a oferta automática e para o Chamar próximo. Roda com <c>--queue-return-only</c>.
/// </summary>
internal static class QueueReturnChecks
{
    public static async Task<int> RunAsync(string root)
    {
        var connection = Environment.GetEnvironmentVariable("NORA_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection))
        {
            Console.WriteLine("SKIP volta à fila: configure NORA_TEST_CONNECTION somente para uma base descartável.");
            return 0;
        }
        var suite = new Suite("volta à fila");
        await using var f = await StartAsync(connection, "queuereturn");
        var u1 = f.Unit1;
        var cardiology = await f.SpecialtyAsync("Cardiologia");
        var ana = await f.ProfessionalAsync(u1, "Dra. Ana Souza", cardiology); var room = await f.RoomAsync(u1, "Sala 1");
        await f.TemplateAsync(u1, ana, 3, "08:00", "10:00", 30, room, cardiology);
        for (var number = 1; number <= 4; number++) await f.PatientAsync(number, u1);
        for (var number = 1; number <= 4; number++)
        {
            using var join = await f.Admin.PostAsJsonAsync($"/api/obs/units/{u1}/waiting-list", new { cpf = Cpf(number), specialty = "Cardiologia" }); Expect(join, 201);
            f.Clock.Advance(TimeSpan.FromSeconds(5));
        }
        var entered = new Dictionary<int, DateTimeOffset>();
        for (var number = 1; number <= 4; number++) entered[number] = new DateTimeOffset(DateTime.SpecifyKind(await f.UnitScalar<DateTime>(u1, "SELECT entered_at FROM nora_unit.waiting_list WHERE cpf=$1", Cpf(number)), DateTimeKind.Utc));

        async Task<string> Status(int number) => await f.UnitScalar<string>(u1, "SELECT status FROM nora_unit.waiting_list WHERE cpf=$1 ORDER BY id DESC LIMIT 1", Cpf(number));
        async Task<string> Offers(Guid slot) => string.Join(",", (await ReadRows($"SELECT q.cpf||':'||o.status FROM nora_unit.lightning_offers o JOIN nora_unit.waiting_list q ON q.id=o.entry_id WHERE o.slot_id=$1 ORDER BY o.expires_at,o.id", slot)).Select(row => row.Replace(Cpf(1), "P1").Replace(Cpf(2), "P2").Replace(Cpf(3), "P3").Replace(Cpf(4), "P4")));
        async Task<List<string>> ReadRows(string sql, params object[] values)
        {
            await using var connectionToUnit = await f.Router.OpenAsync(u1); await using var command = new Npgsql.NpgsqlCommand(sql, connectionToUnit);
            foreach (var value in values) command.Parameters.AddWithValue(value);
            await using var reader = await command.ExecuteReaderAsync(); var rows = new List<string>();
            while (await reader.ReadAsync()) rows.Add(reader.GetString(0));
            return rows;
        }
        async Task<Guid> OpenSlot(string startLocal)
        {
            var start = Wednesday(startLocal); var id = Guid.NewGuid();
            await f.UnitExec(u1, "INSERT INTO nora_unit.lightning_slots(id,appointment_id,status,created_at,created_by,origin,specialty,specialty_id,professional,professional_id,room_id,starts_at,ends_at) VALUES($1,NULL,'open',$2,$3,'call-next','Cardiologia',$4,'Dra. Ana Souza',$5,$6,$7,$8)",
                id, f.Clock.GetUtcNow(), f.AdminId, cardiology, ana, room, start, start.AddMinutes(30));
            return id;
        }
        async Task<Guid> PendingOfferOf(int number)
        {
            using var response = await f.Patients[number].GetAsync($"/api/patient/units/{u1}/lightning-offers"); Expect(response, 200);
            return (await Read(response)).GetProperty("offers").EnumerateArray().Single(offer => offer.GetProperty("status").GetString() == "pending").GetProperty("id").GetGuid();
        }
        async Task Respond(int number, bool accept)
        {
            f.Clock.Advance(TimeSpan.FromSeconds(1)); // a lista de ofertas ordena por vencimento: com o relógio parado duas ofertas empatariam
            using var response = await f.Patients[number].PostAsJsonAsync($"/api/patient/units/{u1}/lightning-offers/{await PendingOfferOf(number)}/respond", new { accept }); Expect(response, 200);
        }

        var slotA = Guid.Empty;
        await suite.Check("Recusa: a entrada volta a waiting na mesma posição (entered_at intacto), com evento 'waiting' de origem patient, e a vaga vai ao próximo sem reoferecer ao mesmo", async () =>
        {
            slotA = await OpenSlot("08:00");
            await f.AdvanceAsync(u1);
            Ensure(await Offers(slotA) == "P1:pending", "A vaga aberta vai ao 1º da fila.");
            await Respond(1, false);
            Ensure(await Status(1) == "waiting" && await f.UnitScalar<bool>(u1, "SELECT entered_at=$2 FROM nora_unit.waiting_list WHERE cpf=$1", Cpf(1), entered[1]), "Quem recusa volta à fila (waiting) com o entered_at original.");
            Ensure(await f.UnitScalar<long>(u1, "SELECT count(*) FROM nora_unit.waiting_list_events e JOIN nora_unit.waiting_list q ON q.id=e.entry_id WHERE q.cpf=$1 AND e.status='waiting' AND e.source='patient'", Cpf(1)) == 1, "A volta à fila deixa evento 'waiting' de origem patient.");
            Ensure(await f.UnitScalar<long>(u1, "SELECT count(*) FROM nora_unit.waiting_list WHERE cpf=$1", Cpf(1)) == 1, "A volta à fila reaproveita a mesma entrada (não cria outra).");
            Ensure(await Offers(slotA) == "P1:declined,P2:pending", "A mesma vaga vai ao próximo (P2), não de volta ao P1 que recusou.");
        });

        await suite.Check("Vencimento (10 min): a entrada volta a waiting na mesma posição, com evento 'waiting' de origem system; a vaga segue para o próximo", async () =>
        {
            f.Clock.Advance(TimeSpan.FromMinutes(10) + TimeSpan.FromSeconds(1));
            await f.AdvanceAsync(u1);
            Ensure(await Status(2) == "waiting" && await f.UnitScalar<bool>(u1, "SELECT entered_at=$2 FROM nora_unit.waiting_list WHERE cpf=$1", Cpf(2), entered[2]), "Quem deixa vencer volta à fila (waiting) com o entered_at original.");
            Ensure(await f.UnitScalar<long>(u1, "SELECT count(*) FROM nora_unit.waiting_list_events e JOIN nora_unit.waiting_list q ON q.id=e.entry_id WHERE q.cpf=$1 AND e.status='waiting' AND e.source='system'", Cpf(2)) == 1, "O vencimento deixa evento 'waiting' de origem system.");
            Ensure(await Offers(slotA) == "P1:declined,P2:expired,P3:pending", "A vaga vai ao P3, sem voltar a P1 nem a P2.");
        });

        await suite.Check("Todos recusaram ou deixaram vencer a mesma vaga: ninguém a recebe de novo e todos continuam na fila na ordem original", async () =>
        {
            await Respond(3, false);
            Ensure(await Offers(slotA) == "P1:declined,P2:expired,P3:declined,P4:pending", "A vaga vai ao P4.");
            f.Clock.Advance(TimeSpan.FromMinutes(10) + TimeSpan.FromSeconds(1));
            await f.AdvanceAsync(u1); await f.AdvanceAsync(u1);
            Ensure(await Offers(slotA) == "P1:declined,P2:expired,P3:declined,P4:expired", "Com todos tendo passado pela vaga, ninguém a recebe de novo.");
            Ensure(await f.UnitScalar<string>(u1, "SELECT status FROM nora_unit.lightning_slots WHERE id=$1", slotA) == "open", "A vaga continua aberta, sem oferta pendente.");
            var order = string.Join(",", await ReadRows("SELECT cpf FROM nora_unit.waiting_list WHERE status='waiting' ORDER BY entered_at,id"));
            Ensure(order == string.Join(",", Enumerable.Range(1, 4).Select(Cpf)), $"Os quatro continuam na fila na ordem original: {order}");
        });

        var slotB = Guid.Empty;
        await suite.Check("Outra vaga: quem recusou a anterior volta a ser o 1º da fila e recebe a nova; aceitar tira da fila (completed) e o aceite não conta como recusa", async () =>
        {
            slotB = await OpenSlot("08:30");
            await f.AdvanceAsync(u1);
            Ensure(await Offers(slotB) == "P1:pending", "A vaga nova vai ao 1º da fila (P1), que só passou pela outra vaga.");
            await Respond(1, true);
            Ensure(await Status(1) == "completed" && await f.UnitScalar<long>(u1, "SELECT count(*) FROM nora_unit.waiting_list_events e JOIN nora_unit.waiting_list q ON q.id=e.entry_id WHERE q.cpf=$1 AND e.status='completed' AND e.source='patient'", Cpf(1)) == 1, "Aceitar tira a pessoa da fila (completed).");
        });

        await suite.Check("Chamar próximo: quem recusa volta à fila, a vaga do chamar próximo segue para o próximo e não volta a quem recusou; cancelar a própria entrada ainda tira da fila", async () =>
        {
            var start = Wednesday("09:00");
            using var call = await f.Admin.PostAsJsonAsync($"/api/obs/units/{u1}/waiting-list/call-next", new { specialtyId = cardiology, professionalId = ana, startsAt = start, confirmed = true }); Expect(call, 201);
            var slot = (await Read(call)).GetProperty("slot").GetProperty("id").GetGuid();
            Ensure(await Offers(slot) == "P2:pending", "O chamar próximo oferece ao 1º da fila que pode receber (P2, já que P1 saiu).");
            await Respond(2, false);
            Ensure(await Status(2) == "waiting" && await f.UnitScalar<bool>(u1, "SELECT entered_at=$2 FROM nora_unit.waiting_list WHERE cpf=$1", Cpf(2), entered[2]), "Recusa no chamar próximo: a entrada volta à fila na mesma posição.");
            Ensure(await Offers(slot) == "P2:declined,P3:pending", "A vaga do chamar próximo segue para o P3 e não volta ao P2.");
            f.Clock.Advance(TimeSpan.FromMinutes(10) + TimeSpan.FromSeconds(1));
            await f.AdvanceAsync(u1);
            Ensure(await Status(3) == "waiting" && await Offers(slot) == "P2:declined,P3:expired,P4:pending", "Vencimento no chamar próximo: P3 volta à fila e a vaga vai ao P4.");
            var entry = await f.UnitScalar<long>(u1, "SELECT id FROM nora_unit.waiting_list WHERE cpf=$1", Cpf(2));
            using (var leave = await f.Patients[2].PostAsJsonAsync($"/api/patient/units/{u1}/waiting-list/{entry}/cancel", new { confirmed = true })) Expect(leave, 204);
            Ensure(await Status(2) == "cancelled", "Cancelar a própria entrada tira da fila.");
        });

        return suite.Report() == 0 ? 0 : 1;
    }
}
