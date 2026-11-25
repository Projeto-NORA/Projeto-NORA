using System.Net.Http.Json;
using Nora.Api.DAOs;
using static RoundFixture;

internal static class RetentionChecks
{
    private const string MunicipalPath = "/api/municipal/retention";

    public static async Task<int> RunAsync()
    {
        var connection = Environment.GetEnvironmentVariable("NORA_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection))
        {
            Console.WriteLine("SKIP exclusão programada: exige banco PostgreSQL descartável.");
            return 0;
        }

        await using var fixture = await StartAsync(connection, "retention");
        var suite = new Suite("exclusão programada");
        var dao = fixture.Service<RetentionDao>();
        var unit = fixture.Unit1;
        var unitRooms = $"/api/obs/units/{unit}/rooms";
        var unitRetention = $"/api/obs/units/{unit}/retention";
        var start = fixture.Clock.GetUtcNow();

        await suite.Check("Prazo começa desligado, exige confirmação e fica no escopo do gestor", async () =>
        {
            using var initial = await fixture.Municipal.GetAsync(MunicipalPath);
            Expect(initial, 200);
            Ensure((await Read(initial)).GetProperty("policy").GetProperty("days").ValueKind == System.Text.Json.JsonValueKind.Null, "Prazo inicial ativo.");
            using var denied = await fixture.Admin.PutAsJsonAsync(MunicipalPath, new { days = 3, confirmed = true });
            Expect(denied, 403);
            var (otherAdmin, _) = await fixture.StaffAsync("administrator", 115, [fixture.Unit2]);
            using var otherUnit = await otherAdmin.GetAsync(unitRetention);
            Expect(otherUnit, 403);
            using var invalid = await fixture.Municipal.PutAsJsonAsync(MunicipalPath, new { days = 0, confirmed = true });
            Expect(invalid, 400);
            using var notConfirmed = await fixture.Municipal.PutAsJsonAsync(MunicipalPath, new { days = 2, confirmed = false });
            Expect(notConfirmed, 400);
            using var enabled = await fixture.Municipal.PutAsJsonAsync(MunicipalPath, new { days = 2, confirmed = true });
            Expect(enabled, 200);
            using var disabled = await fixture.Municipal.PutAsJsonAsync(MunicipalPath, new { days = (int?)null, confirmed = true });
            Expect(disabled, 200);
        });

        await suite.Check("Contas OBS sem uso são excluídas no prazo. Histórico de uso e desativação são preservados", async () =>
        {
            var unused = await CreateStaffAsync(fixture, "Sem vínculos", "retention-unused@example.test");
            var used = await CreateStaffAsync(fixture, "Com histórico", "retention-used@example.test");
            using var usedClient = fixture.NewClient();
            // A abertura da sessão já registra o histórico de uso.
            await fixture.LoginAsync(usedClient, "obs", "retention-used@example.test");
            foreach (var id in new[] { unused, used })
            {
                using var disabled = await fixture.Municipal.PatchAsJsonAsync($"/api/municipal/staff/{id}/active", new { isActive = false, confirmed = true });
                Expect(disabled, 204);
            }
            using var eligible = await fixture.Municipal.PutAsJsonAsync($"{MunicipalPath}/{unused}", new { days = 2, confirmed = true });
            Expect(eligible, 204);
            using var protectedItem = await fixture.Municipal.PutAsJsonAsync($"{MunicipalPath}/{used}", new { days = 1, confirmed = true });
            await ExpectProblem(protectedItem, 409, "retention-item-unavailable");
            var overview = await dao.ReadAsync(null, default);
            Ensure(overview.Items.Single(item => item.Id == unused).ScheduledAt == start.AddDays(2), "A data de exclusão diverge do prazo solicitado.");
            fixture.Clock.Set(start.AddDays(2).AddSeconds(-1));
            Ensure(await dao.PurgeDueAsync(null, default) == 0, "Exclusão antes do prazo.");
            fixture.Clock.Set(start.AddDays(2));
            Ensure(await dao.PurgeDueAsync(null, default) == 1 && await dao.PurgeDueAsync(null, default) == 0, "Exclusão não ocorreu exatamente uma vez.");
            Ensure(await fixture.Scalar<long>("SELECT count(*) FROM nora.accounts WHERE id=$1", used) == 1, "Conta com histórico foi excluída.");
            Ensure(await fixture.Scalar<long>("SELECT count(*) FROM nora.staff_account_events WHERE account_id IS NULL AND action='deactivated'") == 1, "Evento de desativação foi perdido.");
            fixture.Clock.Set(start);
        });

        await suite.Check("Consultórios vinculados, reativados ou com exclusão cancelada são preservados", async () =>
        {
            var empty = await fixture.RoomAsync(unit, "Consultório disponível");
            var cancel = await fixture.RoomAsync(unit, "Consultório com exclusão cancelada");
            var linked = await fixture.RoomAsync(unit, "Consultório da grade");
            var reactivated = await fixture.RoomAsync(unit, "Consultório reativado");
            var specialty = await fixture.SpecialtyAsync("Clínica da exclusão");
            var professional = await fixture.ProfessionalAsync(unit, "Profissional da grade", specialty);
            await fixture.TemplateAsync(unit, professional, 3, "08:00", "09:00", 30, linked, specialty);
            foreach (var id in new[] { empty, cancel, reactivated })
            {
                using var disabled = await fixture.Admin.PatchAsJsonAsync($"{unitRooms}/{id}/active", new { isActive = false, confirmed = true });
                Expect(disabled, 200);
                using var schedule = await fixture.Admin.PutAsJsonAsync($"{unitRetention}/{id}", new { days = 1, confirmed = true });
                Expect(schedule, 204);
            }
            // Simula um cadastro legado inativo que continua referenciado por uma grade.
            await using (var db = await fixture.Router.OpenAsync(unit))
                await fixture.Database.ExecuteAsync(db, "UPDATE nora_unit.rooms SET is_active=false WHERE id=$1", default, linked);
            using var denied = await fixture.Admin.PutAsJsonAsync($"{unitRetention}/{linked}", new { days = 1, confirmed = true });
            await ExpectProblem(denied, 409, "retention-item-unavailable");
            using var cancelled = await fixture.Admin.PutAsJsonAsync($"{unitRetention}/{cancel}", new { days = (int?)null, confirmed = true });
            Expect(cancelled, 204);
            using var active = await fixture.Admin.PatchAsJsonAsync($"{unitRooms}/{reactivated}/active", new { isActive = true, confirmed = true });
            Expect(active, 200);
            fixture.Clock.Set(start.AddDays(1));
            Ensure(await dao.PurgeDueAsync(unit, default) == 1, "Só o consultório elegível deveria ser excluído.");
            var rooms = await dao.ReadAsync(unit, default);
            Ensure(rooms.Items.Any(item => item.Id == linked && !item.Eligible) && rooms.Items.Any(item => item.Id == cancel && item.Cancelled), "Itens protegidos sumiram da lista.");
        });

        await suite.Check("Prazo geral: diminuir o prazo recomeça a contagem e nada vence antes do prazo novo inteiro", async () =>
        {
            // A sessão do administrador vale 8 h: o cadastro acontece no início e as mudanças de prazo nas datas futuras vão direto pelo DAO, onde está a regra.
            fixture.Clock.Set(start);
            var room = await fixture.RoomAsync(unit, "Consultório do prazo geral");
            using (var disabled = await fixture.Admin.PatchAsJsonAsync($"{unitRooms}/{room}/active", new { isActive = false, confirmed = true }))
                Expect(disabled, 200);
            // Desativado há 100 dias. O valor fixo evita misturar o relógio do banco (gatilho) com o relógio simulado da API.
            await using (var db = await fixture.Router.OpenAsync(unit))
                await fixture.Database.ExecuteAsync(db, "UPDATE nora_unit.rooms SET inactive_since=$2 WHERE id=$1", default, room, start.AddDays(-100));
            using (var year = await fixture.Admin.PutAsJsonAsync(unitRetention, new { days = 365, confirmed = true })) Expect(year, 200);
            try
            {
                fixture.Clock.Set(start.AddDays(200));
                await dao.ChangePolicyAsync(unit, 30, default);
                Ensure(await dao.PurgeDueAsync(unit, default) == 0, "Diminuir o prazo apagou na hora o que já tinha passado do prazo novo.");
                Ensure((await dao.ReadAsync(unit, default)).Items.Single(item => item.Id == room).ScheduledAt == start.AddDays(230), "A exclusão deveria contar 30 dias a partir da mudança.");
                // Aumentar mantém o início da contagem: 60 dias contados da última redução.
                fixture.Clock.Set(start.AddDays(210));
                await dao.ChangePolicyAsync(unit, 60, default);
                Ensure((await dao.ReadAsync(unit, default)).Items.Single(item => item.Id == room).ScheduledAt == start.AddDays(260), "Aumentar o prazo deveria manter o início da contagem.");
                fixture.Clock.Set(start.AddDays(260).AddSeconds(-1));
                Ensure(await dao.PurgeDueAsync(unit, default) == 0, "Exclusão antes do prazo novo.");
                fixture.Clock.Set(start.AddDays(260));
                Ensure(await dao.PurgeDueAsync(unit, default) == 1, "O consultório deveria ser excluído no fim do prazo.");
            }
            finally
            {
                await dao.ChangePolicyAsync(unit, null, default);
                fixture.Clock.Set(start);
            }
        });
        return suite.Report();
    }

    private static async Task<Guid> CreateStaffAsync(RoundFixture fixture, string name, string email)
    {
        using var response = await fixture.Municipal.PostAsJsonAsync("/api/municipal/staff", new { fullName = name, email, role = "administrator", password = Password });
        Expect(response, 201);
        return (await Read(response)).GetProperty("id").GetGuid();
    }
}
