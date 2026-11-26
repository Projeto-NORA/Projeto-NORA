using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Nora.Api.Services;
using static RoundFixture;

/// <summary>
/// Janela dos indicadores de 20 anos (2026-11-26): <c>GET /api/obs/units/{id}/metrics</c> e <c>GET /api/municipal/metrics</c> aceitam até 7.305 dias (os períodos "5 anos" e "máximo" do OBS e do Painel
/// Municipal); 7.305 dias e 1 segundo, ou 20 anos e 1 dia, são recusados com a mensagem nova. As outras listas por período (consultas, bloqueios) e a janela dos alertas continuam em 366 dias.
/// A última verificação mede a consulta com 20 anos de dados de uma UBS grande (linhas <c>MEDIDA</c>). Roda com <c>--metrics-window-only</c>.
/// </summary>
internal static class MetricsWindowChecks
{
    private const int WindowDays = 7305;

    /// <summary>Contagens de consultas: no OBS ficam em <c>appointments</c>; na Secretaria, em <c>totals.appointments</c>.</summary>
    private static JsonElement Totals(JsonElement body) => body.TryGetProperty("appointments", out var own) ? own : body.GetProperty("totals").GetProperty("appointments");
    private const int Professionals = 8, SlotsPerProfessionalDay = 10;

    public static async Task<int> RunAsync(string root)
    {
        var connection = Environment.GetEnvironmentVariable("NORA_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection))
        {
            Console.WriteLine("SKIP janela de 20 anos dos indicadores: configure NORA_TEST_CONNECTION somente para uma base descartável.");
            return 0;
        }
        var suite = new Suite("janela dos indicadores");
        await using var f = await StartAsync(connection, "metrics_window");
        var u1 = f.Unit1;
        var t0 = RoundClock.T0;
        string Q(DateTimeOffset moment) => Uri.EscapeDataString(moment.ToString("O", CultureInfo.InvariantCulture));
        string Window(DateTimeOffset from, TimeSpan span) => $"from={Q(from)}&to={Q(from + span)}";
        var start = t0.AddDays(-WindowDays);

        await suite.Check("Indicadores da UBS e da rede aceitam 20 anos (7.305 dias); a Secretaria e o OBS recebem o mesmo contrato", async () =>
        {
            foreach (var (client, path) in new[] { (f.Admin, $"/api/obs/units/{u1}/metrics"), (f.Municipal, "/api/municipal/metrics") })
            {
                using var response = await client.GetAsync($"{path}?{Window(start, TimeSpan.FromDays(WindowDays))}");
                Expect(response, 200);
                var body = await Read(response);
                Ensure(Totals(body).GetProperty("completed").GetInt64() == 0 && body.GetProperty("daily").GetArrayLength() == 0 && body.TryGetProperty("capacity", out _) && body.TryGetProperty("goals", out _),
                    $"{path}: o contrato da resposta mudou.");
            }
            using var network = await f.Municipal.GetAsync($"/api/municipal/metrics?{Window(start, TimeSpan.FromDays(WindowDays))}&unitId={u1}&region=Centro");
            Expect(network, 200);
        });

        await suite.Check("Mais de 20 anos é recusado (20 anos e 1 dia; 7.305 dias e 1 segundo) com 400 invalid-input e a mensagem do período; início igual ao fim ou invertido também", async () =>
        {
            foreach (var (client, path) in new[] { (f.Admin, $"/api/obs/units/{u1}/metrics"), (f.Municipal, "/api/municipal/metrics") })
            {
                foreach (var span in new[] { TimeSpan.FromDays(WindowDays + 1), TimeSpan.FromDays(WindowDays) + TimeSpan.FromSeconds(1), TimeSpan.FromDays(36500) })
                {
                    using var response = await client.GetAsync($"{path}?{Window(start, span)}");
                    var problem = await ExpectProblem(response, 400, "invalid-input");
                    Ensure(problem.GetProperty("title").GetString() == "Período inválido" && problem.GetProperty("detail").GetString()!.Contains("20 anos") && problem.GetProperty("detail").GetString()!.Contains("7.305"),
                        $"{path}: a mensagem do período deveria citar o limite de 20 anos ({problem.GetProperty("detail").GetString()}).");
                }
                using var equal = await client.GetAsync($"{path}?from={Q(start)}&to={Q(start)}"); await ExpectProblem(equal, 400, "invalid-input");
                using var inverted = await client.GetAsync($"{path}?from={Q(t0)}&to={Q(start)}"); await ExpectProblem(inverted, 400, "invalid-input");
            }
            // Sem "to" a requisição para na leitura dos parâmetros, antes da conferência do período: é o 400 genérico.
            using var missing = await f.Admin.GetAsync($"/api/obs/units/{u1}/metrics?from={Q(start)}");
            Ensure((await ExpectProblem(missing, 400, "invalid-input")).GetProperty("title").GetString() == "Dados inválidos", "Sem to a resposta deveria ser o 400 genérico.");
        });

        await suite.Check("As outras rotas por período não mudaram: consultas da UBS (OBS e Secretaria) e bloqueios (leitura e criação) em 366 dias, disponibilidade (OBS e paciente) em 92 dias e a janela dos alertas em 366", async () =>
        {
            var year = TimeSpan.FromDays(366); var more = TimeSpan.FromDays(367);
            foreach (var (client, path) in new[] { (f.Admin, $"/api/obs/units/{u1}/appointments"), (f.Admin, $"/api/obs/units/{u1}/schedule-blocks"), (f.Municipal, $"/api/municipal/units/{u1}/appointments"), (f.Municipal, $"/api/municipal/units/{u1}/schedule-blocks") })
            {
                using (var accepted = await client.GetAsync($"{path}?{Window(t0, year)}")) Expect(accepted, 200);
                using (var rejected = await client.GetAsync($"{path}?{Window(t0, more)}")) Expect(rejected, 400);
                using (var twenty = await client.GetAsync($"{path}?{Window(start, TimeSpan.FromDays(WindowDays))}")) Expect(twenty, 400);
            }
            var clinical = await f.SpecialtyAsync("Clínica geral");
            var professional = await f.ProfessionalAsync(u1, "Prof limite", clinical);
            using (var create = await f.Admin.PostAsJsonAsync($"/api/obs/units/{u1}/schedule-blocks", new { professionalId = professional, startsAt = t0.AddDays(10), endsAt = t0.AddDays(10) + more, reason = "Longo demais" })) Expect(create, 400);
            using (var createOk = await f.Admin.PostAsJsonAsync($"/api/obs/units/{u1}/schedule-blocks", new { professionalId = professional, startsAt = t0.AddDays(10), endsAt = t0.AddDays(10) + year, reason = "Um ano" })) Expect(createOk, 201);
            // A disponibilidade (horários livres) continua com 92 dias por pedido, para a equipe e para o paciente: 92 dias valem, 93 dias e 20 anos não.
            var patient = await f.PatientAsync(2, u1);
            foreach (var (client, path) in new[] { (f.Admin, $"/api/obs/units/{u1}/availability"), (patient, $"/api/patient/units/{u1}/availability") })
            {
                using (var accepted = await client.GetAsync($"{path}?{Window(t0, TimeSpan.FromDays(SchedulingService.MaxAvailabilityDays))}&specialtyId={clinical}")) Expect(accepted, 200);
                using (var rejected = await client.GetAsync($"{path}?{Window(t0, TimeSpan.FromDays(SchedulingService.MaxAvailabilityDays + 1))}&specialtyId={clinical}")) Expect(rejected, 400);
                using (var twenty = await client.GetAsync($"{path}?{Window(t0, TimeSpan.FromDays(WindowDays))}&specialtyId={clinical}")) Expect(twenty, 400);
            }
            // A janela dos alertas (Nora:Alerts:WindowDays) continua com teto de 366 dias, mesmo com os indicadores em 20 anos.
            AlertOptions Alerts(string days) => new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Nora:Alerts:WindowDays"] = days }).Build());
            Ensure(Alerts("366").WindowDays == 366 && Alerts("30").WindowDays == 30, "A janela dos alertas aceita de 1 a 366 dias.");
            foreach (var invalid in new[] { "367", "7305", "0" })
            {
                try { Alerts(invalid); throw new InvalidOperationException($"WindowDays '{invalid}' foi aceito."); }
                catch (InvalidOperationException error) when (error.Message.Contains("Nora:Alerts:WindowDays")) { }
            }
            Ensure(AlertOptions.MaxWindowDays == 366 && MetricsService.MaxWindowDays == WindowDays, "Os limites dos alertas e dos indicadores ficaram trocados.");
        });

        await suite.Check("20 anos de dados de uma UBS grande: a série diária traz só os dias com registro, a resposta fica pequena e a consulta termina em poucos segundos (MEDIDA)", async () =>
        {
            await f.RegisterAsync(1, u1);
            var specialty = await f.SpecialtyAsync("Cardiologia");
            var seeded = new List<string>();
            for (var number = 1; number <= Professionals; number++)
            {
                var professional = await f.ProfessionalAsync(u1, $"Prof {number}", specialty); var room = await f.RoomAsync(u1, $"Sala {number}");
                foreach (var weekday in new[] { 1, 2, 3, 4, 5 })
                {
                    await f.TemplateAsync(u1, professional, weekday, "08:00", "12:00", 20, room, specialty);
                    await f.TemplateAsync(u1, professional, weekday, "13:00", "17:00", 20, room, specialty);
                }
                seeded.Add($"Prof {number}");
            }
            var first = start.AddDays(5).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture); var last = t0.AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var watch = Stopwatch.StartNew();
            await f.UnitExec(u1,
                "INSERT INTO nora_unit.appointments(id,cpf,specialty,professional,starts_at,ends_at,status,created_by,created_at) " +
                "SELECT gen_random_uuid(),$1,'Cardiologia','Prof '||p,((g.d + time '08:00' + j*interval '20 minutes') AT TIME ZONE 'America/Sao_Paulo'),((g.d + time '08:00' + (j+1)*interval '20 minutes') AT TIME ZONE 'America/Sao_Paulo')," +
                "(ARRAY['completed','completed','missed','completed','scheduled'])[1+((j+p) % 5)],$2,$3 " +
                $"FROM generate_series('{first}'::timestamp,'{last}'::timestamp,interval '1 day') AS g(d), generate_series(1,{Professionals}) p, generate_series(0,{SlotsPerProfessionalDay - 1}) j WHERE extract(dow from g.d) BETWEEN 1 AND 5",
                Cpf(1), f.AdminId, t0);
            var rows = await f.UnitScalar<long>(u1, "SELECT count(*) FROM nora_unit.appointments");
            await f.UnitExec(u1, "ANALYZE nora_unit.appointments");
            Console.WriteLine($"MEDIDA janela 20 anos: dados de teste com {rows} consultas em {WindowDays} dias ({Professionals} profissionais, grade de segunda a sexta, 24 atendimentos por dia cada), carga em {watch.Elapsed.TotalSeconds:F1} s");

            // Aquecimento (JIT, pool de conexões, estatísticas) e depois a medição de cada janela, nos dois endpoints.
            using (var warm = await f.Admin.GetAsync($"/api/obs/units/{u1}/metrics?{Window(t0.AddDays(-30), TimeSpan.FromDays(30))}")) Expect(warm, 200);
            foreach (var (label, span) in new[] { ("1 ano (366 dias)", TimeSpan.FromDays(366)), ("5 anos (1.827 dias)", TimeSpan.FromDays(1827)), ("20 anos (7.305 dias)", TimeSpan.FromDays(WindowDays)) })
                foreach (var (client, path, name) in new[] { (f.Admin, $"/api/obs/units/{u1}/metrics", "OBS"), (f.Municipal, "/api/municipal/metrics", "Secretaria") })
                {
                    var from = t0 - span;
                    long milliseconds = long.MaxValue, bytes = 0, allocated = 0; JsonElement body = default;
                    for (var attempt = 0; attempt < 3; attempt++)
                    {
                        GC.Collect(); var before = GC.GetTotalAllocatedBytes(true); watch.Restart();
                        using var response = await client.GetAsync($"{path}?from={Q(from)}&to={Q(t0)}"); Expect(response, 200);
                        var text = await response.Content.ReadAsStringAsync(); watch.Stop();
                        if (watch.ElapsedMilliseconds < milliseconds) { milliseconds = watch.ElapsedMilliseconds; bytes = text.Length; allocated = GC.GetTotalAllocatedBytes(true) - before; }
                        using var json = JsonDocument.Parse(text); body = json.RootElement.Clone();
                    }
                    var daily = body.GetProperty("daily").GetArrayLength();
                    var capacity = body.GetProperty("capacity");
                    Console.WriteLine($"MEDIDA janela {label} {name}: {milliseconds} ms (melhor de 3), resposta {bytes / 1024.0:F1} KiB, série diária com {daily} dias com registro, {allocated / 1048576.0:F1} MiB alocados, capacidade {capacity.GetProperty("offeredSlots").GetInt64()} atendimentos ofertados");
                    if (span.Days == WindowDays)
                    {
                        // Só os dias úteis com consulta aparecem (a série é esparsa; o cliente completa os dias que faltam com zero), então ela fica abaixo de 20 anos × 261 dias úteis.
                        Ensure(daily > 0 && daily <= 5220, $"A série diária deveria ter só os dias com registro (recebido {daily}).");
                        Ensure(bytes < 1024 * 1024, $"A resposta de 20 anos ficou grande demais ({bytes} bytes).");
                        Ensure(milliseconds < 20000, $"A consulta de 20 anos levou {milliseconds} ms.");
                        var total = Totals(body); var sum = body.GetProperty("daily").EnumerateArray().Sum(day => day.GetProperty("completed").GetInt64() + day.GetProperty("missed").GetInt64() + day.GetProperty("scheduled").GetInt64());
                        Ensure(sum == total.GetProperty("completed").GetInt64() + total.GetProperty("missed").GetInt64() + total.GetProperty("scheduled").GetInt64(), "A série diária não fecha com os totais.");
                    }
                }
        });

        // Depende da grade semeada acima: sem grade, a capacidade nem gera atendimentos e o defeito não aparece.
        // Antes da revisão de 26/11/2026, até 31/12/9999 23:59:59 respondia 500 (to + 240 min na leitura da grade passava do limite de data); o começo do calendário só falhava num fuso a leste do UTC.
        await suite.Check("Datas nos extremos do calendário (antes de 02/01/0001 e depois de 30/12/9999) são recusadas com o 400 do período, nunca com 500; um dia antes de cada ponta continua valendo", async () =>
        {
            var edges = new (DateTimeOffset From, DateTimeOffset To)[]
            {
                (DateTimeOffset.MinValue, DateTimeOffset.MinValue.AddDays(30)),
                (new DateTimeOffset(9999, 12, 1, 0, 0, 0, TimeSpan.Zero), DateTimeOffset.MaxValue),
                (new DateTimeOffset(9999, 12, 1, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(9999, 12, 31, 12, 0, 0, TimeSpan.Zero))
            };
            foreach (var (client, path) in new[] { (f.Admin, $"/api/obs/units/{u1}/metrics"), (f.Municipal, "/api/municipal/metrics") })
            {
                foreach (var (from, to) in edges)
                {
                    using var response = await client.GetAsync($"{path}?from={Q(from)}&to={Q(to)}");
                    var problem = await ExpectProblem(response, 400, "invalid-input");
                    Ensure(problem.GetProperty("title").GetString() == "Período inválido", $"{path} de {from:O} a {to:O}: esperava o 400 do período.");
                }
                // Um dia de folga de cada lado do calendário continua valendo (o fuso da API nunca passa de 14 h).
                using (var low = await client.GetAsync($"{path}?from={Q(DateTimeOffset.MinValue.AddDays(1))}&to={Q(DateTimeOffset.MinValue.AddDays(31))}")) Expect(low, 200);
                using (var high = await client.GetAsync($"{path}?from={Q(new DateTimeOffset(9999, 11, 30, 0, 0, 0, TimeSpan.Zero))}&to={Q(DateTimeOffset.MaxValue.AddDays(-1))}")) Expect(high, 200);
            }
        });

        return suite.Report() == 0 ? 0 : 1;
    }
}
