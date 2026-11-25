using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net.Http.Json;
using Npgsql;
using Nora.Api.DAOs;
using Nora.Api.Services;
using static RoundFixture;

/// <summary>
/// Medição e regressão da otimização de 2026-11-17 (P1: passe ocioso do distribuidor da Vaga Relâmpago; P2: leitura com <c>FOR SHARE</c> na concessão e no vínculo). Roda com <c>--performance-only</c>
/// e imprime linhas <c>MEDIDA</c> (contagem de comandos SQL do Npgsql, transações do banco central e tempo) para o antes/depois de <c>docs/desempenho.md</c>.
/// </summary>
internal static class PerformanceChecks
{
    private const int IdlePasses = 300;

    public static async Task<int> RunAsync(string root)
    {
        var connection = Environment.GetEnvironmentVariable("NORA_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection))
        {
            Console.WriteLine("SKIP desempenho: configure NORA_TEST_CONNECTION somente para uma base descartável.");
            return 0;
        }
        var suite = new Suite("desempenho");
        await using var f = await StartAsync(connection, "perf");
        var u1 = f.Unit1;
        for (var number = 1; number <= 20; number++) await f.PatientAsync(number, u1);
        var specialty = await f.SpecialtyAsync("Cardiologia");
        var dispatcher = f.Service<UnitLightningDispatcher>();
        // Candidatos da Vaga Relâmpago automática: cada paciente tem uma consulta de Cardiologia marcada mais adiante (fila manual removida em 2026-11-24).
        for (var number = 1; number <= 20; number++)
        {
            var later = new DateTimeOffset(2027, 1, 4, 12, 0, 0, TimeSpan.Zero).AddHours(number);
            await f.UnitExec(u1, "INSERT INTO nora_unit.appointments(id,cpf,specialty,professional,starts_at,ends_at,status,created_by,created_at) VALUES(gen_random_uuid(),$1,'Cardiologia',$2,$3,$4,'scheduled',$5,$6)", Cpf(number), $"Agenda futura {number}", later, later.AddMinutes(30), f.AdminId, f.Clock.GetUtcNow());
        }

        await suite.Check("P1: passe ocioso (UBS sem vaga, oferta nem proposta) não abre transação na central, não bloqueia vínculos e gasta poucos comandos", async () =>
        {
            for (var warm = 0; warm < 20; warm++) await dispatcher.AdvanceAsync(u1, default);
            var before = await f.Scalar<long>("SELECT txid_current()");
            var (commands, milliseconds) = await CountAsync(async () => { for (var pass = 0; pass < IdlePasses; pass++) await dispatcher.AdvanceAsync(u1, default); });
            var after = await f.Scalar<long>("SELECT txid_current()");
            Console.WriteLine($"MEDIDA P1 passe ocioso: {IdlePasses} passes, {commands} comandos SQL ({commands / (double)IdlePasses:F1} por passe), {milliseconds:F0} ms ({milliseconds / IdlePasses:F2} ms por passe), transações com XID na central: {after - before - 1}");
            Ensure(after - before - 1 == 0, $"Passe ocioso consumiu {after - before - 1} XIDs da central (esperado 0: nada de FOR SHARE nem escrita).");
            Ensure(commands <= IdlePasses * 4, $"Passe ocioso gastou {commands / (double)IdlePasses:F1} comandos (esperado até 4).");
        });

        await suite.Check("P1: com trabalho a fazer o passe roda inteiro (vaga aberta recebe oferta; oferta vencida passa ao próximo)", async () =>
        {
            var professional = await f.ProfessionalAsync(u1, "Dra. Medida", specialty); var room = await f.RoomAsync(u1, "Sala medida");
            await f.TemplateAsync(u1, professional, 3, "08:00", "10:00", 30, room, specialty);
            var start = Wednesday("08:00");
            await f.UnitExec(u1, "INSERT INTO nora_unit.lightning_slots(id,appointment_id,status,created_at,created_by,origin,specialty,specialty_id,professional,professional_id,room_id,starts_at,ends_at) VALUES($1,NULL,'open',$2,$3,'call-next','Cardiologia',$4,'Dra. Medida',$5,$6,$7,$8)",
                Guid.NewGuid(), f.Clock.GetUtcNow(), f.AdminId, specialty, professional, room, start, start.AddMinutes(30));
            await dispatcher.AdvanceAsync(u1, default);
            Ensure(await f.UnitScalar<long>(u1, "SELECT count(*) FROM nora_unit.lightning_offers WHERE status='pending'") == 1, "A vaga aberta não recebeu oferta.");
            f.Clock.Advance(TimeSpan.FromMinutes(11));
            await dispatcher.AdvanceAsync(u1, default);
            Ensure(await f.UnitScalar<long>(u1, "SELECT count(*) FROM nora_unit.lightning_offers WHERE status='expired'") == 1 && await f.UnitScalar<long>(u1, "SELECT count(*) FROM nora_unit.lightning_offers WHERE status='pending'") == 1, "O vencimento não passou a vaga ao próximo.");
        });

        await suite.Check("P2: 4 leituras do OBS em paralelo (a fila manual saiu em 2026-11-24), 40 rodadas (tempo total; o antes/depois fica em docs/desempenho.md)", async () =>
        {
            var routes = new[] { $"/api/obs/units/{u1}/registry", $"/api/obs/units/{u1}/lightning-slots", $"/api/obs/units/{u1}/appointments?from=2026-11-01T00:00:00Z&to=2026-12-31T00:00:00Z", $"/api/obs/units/{u1}/active-rooms" };
            var clients = Enumerable.Range(0, 5).Select(_ => f.Admin).ToArray();
            for (var warm = 0; warm < 3; warm++) await Task.WhenAll(routes.Select(route => f.Admin.GetAsync(route)));
            var watch = Stopwatch.StartNew();
            for (var round = 0; round < 40; round++)
            {
                var responses = await Task.WhenAll(routes.Select(route => f.Admin.GetAsync(route)));
                foreach (var response in responses) { Expect(response, 200); response.Dispose(); }
            }
            Console.WriteLine($"MEDIDA P2 leituras do OBS em paralelo: 40 rodadas de 4 GET em {watch.Elapsed.TotalMilliseconds:F0} ms ({watch.Elapsed.TotalMilliseconds / 40:F1} ms por rodada)");
        });

        await suite.Check("P2: leitura do OBS segura a concessão só em FOR SHARE (outra leitura passa, a revogação e a escrita esperam a leitura terminar)", async () =>
        {
            var (staff, staffId) = await f.StaffAsync("administrator", 40, [u1]);
            await using var holder = await f.Database.OpenAsync(); await using var transaction = await holder.BeginTransactionAsync();
            await using (var share = f.Database.Command(holder, "SELECT 1 FROM nora.staff_units WHERE account_id=$1 AND unit_id=$2 FOR SHARE", staffId, u1)) Ensure(await share.ExecuteScalarAsync() is not null, "Concessão não encontrada.");
            using (var read = await Within(staff.GetAsync($"/api/obs/units/{u1}/lightning-slots"), 5000)) Expect(read, 200);
            var revoke = f.Municipal.DeleteAsync($"/api/municipal/staff/{staffId}/units/{u1}");
            var write = staff.PostAsJsonAsync($"/api/obs/units/{u1}/lightning-slots", new { appointmentId = Guid.NewGuid(), confirmed = true }); // escrita do OBS: concessão em FOR UPDATE
            await Task.Delay(1500);
            Ensure(!revoke.IsCompleted, "A revogação não esperou a leitura em andamento.");
            Ensure(!write.IsCompleted, "A escrita (FOR UPDATE) não esperou a leitura em andamento.");
            await transaction.CommitAsync();
            using (var revoked = await Within(revoke, 10000)) Expect(revoked, 204);
            using (var written = await Within(write, 10000)) Ensure((int)written.StatusCode is 404 or 403 or 401, $"Escrita depois da revogação deveria ser recusada ou ter rodado antes (404: consulta inexistente) (recebido {(int)written.StatusCode}).");
        });

        await suite.Check("P2: leitura do paciente segura o vínculo só em FOR SHARE (outra leitura passa, sair da UBS espera a leitura terminar)", async () =>
        {
            var patient = f.Patients[7];
            await using var holder = await f.Database.OpenAsync(); await using var transaction = await holder.BeginTransactionAsync();
            await using (var share = f.Database.Command(holder, "SELECT 1 FROM nora.patient_units WHERE patient_id=$1 AND unit_id=$2 FOR SHARE", f.PatientIds[7], u1)) Ensure(await share.ExecuteScalarAsync() is not null, "Vínculo não encontrado.");
            using (var read = await Within(patient.GetAsync($"/api/patient/units/{u1}/lightning-offers"), 5000)) Expect(read, 200);
            var unlink = patient.PostAsJsonAsync($"/api/patient/units/{u1}/unlink", new { confirmed = true });
            await Task.Delay(1500);
            Ensure(!unlink.IsCompleted, "Sair da UBS não esperou a leitura em andamento.");
            await transaction.CommitAsync();
            using (var done = await Within(unlink, 10000)) Expect(done, 200);
        });

        await suite.Check("Volta 2 #5: impasse (40P01) na revogação, no código ou na ativação é repetido uma vez; segunda queda e outros erros sobem sem repetir", async () =>
        {
            var calls = 0;
            Ensure(await ObsDeviceService.RetryOnDeadlockAsync(() => Task.FromResult(++calls < 2 ? throw Deadlock() : "ok")) == "ok" && calls == 2, "Um impasse deveria ser repetido uma vez e concluir.");
            calls = 0;
            try { await ObsDeviceService.RetryOnDeadlockAsync<string>(() => throw CountAndThrow(ref calls, Deadlock())); throw new InvalidOperationException("Dois impasses seguidos não podem ser engolidos."); }
            catch (PostgresException) { }
            Ensure(calls == 2, $"Dois impasses seguidos: duas tentativas e o erro sobe (tentativas: {calls}).");
            calls = 0;
            try { await ObsDeviceService.RetryOnDeadlockAsync<string>(() => throw CountAndThrow(ref calls, new PostgresException("outro", "ERROR", "ERROR", PostgresErrorCodes.UniqueViolation))); throw new InvalidOperationException("Outro erro não pode ser engolido."); }
            catch (PostgresException) { }
            Ensure(calls == 1, $"Erro que não é impasse não é repetido (tentativas: {calls}).");
        });

        return suite.Report() == 0 ? 0 : 1;
    }

    private static PostgresException Deadlock() => new("deadlock detected", "ERROR", "ERROR", PostgresErrorCodes.DeadlockDetected);
    private static PostgresException CountAndThrow(ref int calls, PostgresException error) { calls++; return error; }

    private static async Task<HttpResponseMessage> Within(Task<HttpResponseMessage> task, int milliseconds)
    {
        if (await Task.WhenAny(task, Task.Delay(milliseconds)) != task) throw new InvalidOperationException($"A requisição não terminou em {milliseconds} ms (bloqueada por um lock).");
        return await task;
    }

    /// <summary>Conta os comandos SQL do Npgsql (histograma <c>db.client.operation.duration</c>) e o tempo gasto por <paramref name="action"/>.</summary>
    private static async Task<(long Commands, double Milliseconds)> CountAsync(Func<Task> action)
    {
        long commands = 0;
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) => { if (instrument.Meter.Name == "Npgsql" && instrument.Name == "db.client.operation.duration") meterListener.EnableMeasurementEvents(instrument); };
        listener.SetMeasurementEventCallback<double>((instrument, _, _, _) => { if (instrument.Name == "db.client.operation.duration") Interlocked.Increment(ref commands); });
        listener.Start();
        var watch = Stopwatch.StartNew();
        await action();
        watch.Stop();
        listener.Dispose();
        return (Interlocked.Read(ref commands), watch.Elapsed.TotalMilliseconds);
    }
}
