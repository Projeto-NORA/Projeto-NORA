using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;
using Nora.Api.DAOs;
using Nora.Api.Database;
using Nora.Api.Services;
using static RoundFixture;

/// <summary>
/// Fechamento das pendências de 2026-11-17: score em pontos (0 a 1000 e faixas), prioridade da Vaga Relâmpago por pontos, contas de equipe sem CPF (P-053) e as otimizações P3, P4 e P5 de
/// <c>docs/desempenho.md</c>. Roda com <c>--closing-only</c> e imprime linhas <c>MEDIDA</c> para o antes/depois do documento de desempenho.
/// </summary>
internal static class ClosingChecks
{
    public static async Task<int> RunAsync(string root)
    {
        var connection = Environment.GetEnvironmentVariable("NORA_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection))
        {
            Console.WriteLine("SKIP fechamento: configure NORA_TEST_CONNECTION somente para uma base descartável.");
            return 0;
        }
        var suite = new Suite("fechamento");
        await using (var f = await StartAsync(connection, "closing"))
            await RunMainAsync(suite, f);
        await RunPoolAsync(suite, connection);
        return suite.Report() == 0 ? 0 : 1;
    }

    /// <summary>Cria <paramref name="completed"/> consultas concluídas e <paramref name="missed"/> faltas no passado recente do paciente na UBS (uma hora de intervalo entre elas, profissionais distintos).</summary>
    private static Task Seed(RoundFixture f, Guid unit, int number, int completed, int missed, int daysBack = 2) => f.UnitExec(unit,
        "INSERT INTO nora_unit.appointments(id,cpf,specialty,professional,starts_at,ends_at,status,created_by,created_at) " +
        "SELECT gen_random_uuid(),$1,'Histórico','Prof '||g::text||$1,$2::timestamptz-g*interval '1 hour',$2::timestamptz-g*interval '1 hour'+interval '30 minutes',CASE WHEN g<=$3 THEN 'completed' ELSE 'missed' END,$4,$2 FROM generate_series(1,$5) g",
        Cpf(number), RoundClock.T0.AddDays(-daysBack), completed, f.AdminId, completed + missed);

    private static async Task RunMainAsync(Suite suite, RoundFixture f)
    {
        var u1 = f.Unit1; var u2 = f.Unit2;
        var cardiology = await f.SpecialtyAsync("Cardiologia"); var pediatrics = await f.SpecialtyAsync("Pediatria");
        var ana = await f.ProfessionalAsync(u1, "Dra. Ana Souza", cardiology); var room = await f.RoomAsync(u1, "Sala 1");
        await f.TemplateAsync(u1, ana, 3, "08:00", "10:00", 30, room, cardiology);
        for (var number = 1; number <= 11; number++) await f.PatientAsync(number, u1);

        // ---- Score por eventos (2026-11-24) ----
        // Seed grava as faltas antes dos comparecimentos (mais antigas), todas há menos de 90 dias: 700 − 60 por falta (limitado a 0) e depois +60, +70, +80… por comparecimento.
        // número do paciente → (concluídas, faltas, pontos, faixa): 1 não tem histórico (700).
        var cases = new (int Number, int Completed, int Missed, int Points, string Band)[]
        {
            (2, 0, 7, 280, "low"), (3, 0, 6, 340, "regular"), (4, 0, 2, 580, "regular"), (5, 0, 1, 640, "good"), (6, 1, 0, 760, "good"), (7, 2, 0, 830, "excellent"), (8, 0, 12, 0, "low"), (9, 6, 0, 1000, "excellent")
        };
        foreach (var (number, completed, missed, _, _) in cases) await Seed(f, u1, number, completed, missed);

        await suite.Check("Score por eventos: o registry traz points, band e hasHistory (sem histórico = 700, good, hasHistory falso; limites 0 e 1000); score = pontos ÷ 1000; contagens antigas iguais", async () =>
        {
            using var response = await f.Admin.GetAsync($"/api/obs/units/{u1}/registry"); Expect(response, 200);
            var rows = (await Read(response)).EnumerateArray().ToDictionary(row => row.GetProperty("cpf").GetString()!);
            var none = rows[Cpf(1)].GetProperty("attendance");
            Ensure(none.GetProperty("points").GetInt32() == 700 && none.GetProperty("band").GetString() == "good" && !none.GetProperty("hasHistory").GetBoolean() && none.GetProperty("score").ValueKind == JsonValueKind.Null, "Sem histórico: 700 pontos, good, sem histórico, score nulo.");
            foreach (var (number, completed, missed, points, band) in cases)
            {
                var attendance = rows[Cpf(number)].GetProperty("attendance");
                Ensure(attendance.GetProperty("points").GetInt32() == points && attendance.GetProperty("band").GetString() == band && attendance.GetProperty("hasHistory").GetBoolean(), $"Paciente {number} ({completed}/{missed}): esperado {points} pontos, faixa {band}; veio {attendance.GetProperty("points").GetInt32()} {attendance.GetProperty("band").GetString()}.");
                Ensure(attendance.GetProperty("completed").GetInt64() == completed && attendance.GetProperty("missed").GetInt64() == missed && attendance.GetProperty("score").GetDouble() == points / 1000.0, $"Contagens ou score (pontos ÷ 1000) do paciente {number} incorretos.");
            }
        });

        await suite.Check("Score por eventos: a rota do paciente (/attendance) traz os mesmos points, band e hasHistory; a Secretaria continua sem attendance", async () =>
        {
            foreach (var (number, points, band, history) in new[] { (1, 700, "good", false), (2, 280, "low", true), (3, 340, "regular", true), (5, 640, "good", true), (7, 830, "excellent", true) })
            {
                using var response = await f.Patients[number].GetAsync($"/api/patient/units/{u1}/attendance"); Expect(response, 200);
                var body = await Read(response);
                Ensure(body.GetProperty("points").GetInt32() == points && body.GetProperty("band").GetString() == band && body.GetProperty("hasHistory").GetBoolean() == history && body.GetProperty("windowMonths").GetInt32() == 12, $"Paciente {number} na própria rota: {body}.");
            }
            using var municipal = await f.Municipal.GetAsync($"/api/municipal/units/{u1}/registry"); Expect(municipal, 200);
            Ensure(!(await municipal.Content.ReadAsStringAsync()).Contains("points", StringComparison.Ordinal), "A listagem da Secretaria não traz o score.");
        });

        // ---- Prioridade da Vaga Relâmpago (candidatos por consulta posterior) ----
        // Consultas marcadas mais adiante (2027), por especialidade; a distância de cada uma está em dias depois de 2027-01-01.
        async Task Later(int number, string specialty, int days) =>
            await f.UnitExec(u1, "INSERT INTO nora_unit.appointments(id,cpf,specialty,professional,starts_at,ends_at,status,created_by,created_at) VALUES(gen_random_uuid(),$1,$2,$3,$4,$5,'scheduled',$6,$7)",
                Cpf(number), specialty, $"Agenda futura {number}", new DateTimeOffset(2027, 1, 1, 12, 0, 0, TimeSpan.Zero).AddDays(days), new DateTimeOffset(2027, 1, 1, 12, 30, 0, TimeSpan.Zero).AddDays(days), f.AdminId, f.Clock.GetUtcNow());
        // Cardiologia: P3 (340) com a consulta mais distante, P1 (700) no meio e P6 (760) a mais perto. Pediatria: P8 (0) a mais distante, P11 e P10 (700) empatados.
        await Later(3, "Cardiologia", 30); await Later(1, "Cardiologia", 20); await Later(6, "Cardiologia", 10);
        await Later(8, "Pediatria", 30); await Later(11, "Pediatria", 20); await Later(10, "Pediatria", 10);
        async Task<Guid> OpenSlot(string specialty, string startLocal)
        {
            var start = Wednesday(startLocal); var id = Guid.NewGuid();
            await f.UnitExec(u1, "INSERT INTO nora_unit.lightning_slots(id,appointment_id,status,created_at,created_by,origin,specialty,specialty_id,professional,professional_id,room_id,starts_at,ends_at) VALUES($1,NULL,'open',$2,$3,'call-next',$4,NULL,'Prof prioridade',NULL,NULL,$5,$6)",
                id, f.Clock.GetUtcNow(), f.AdminId, specialty, start, start.AddMinutes(30));
            f.Clock.Advance(TimeSpan.FromSeconds(1));
            return id;
        }
        async Task<string> Offers(Guid slot)
        {
            await using var unit = await f.Router.OpenAsync(u1);
            await using var command = new NpgsqlCommand("SELECT o.cpf||':'||o.status FROM nora_unit.lightning_offers o WHERE o.slot_id=$1 ORDER BY o.expires_at,o.id", unit);
            command.Parameters.AddWithValue(slot);
            await using var reader = await command.ExecuteReaderAsync(); var rows = new List<string>();
            while (await reader.ReadAsync()) rows.Add(reader.GetString(0).Replace(Cpf(10), "P10").Replace(Cpf(11), "P11").Replace(Cpf(1), "P1").Replace(Cpf(3), "P3").Replace(Cpf(6), "P6").Replace(Cpf(8), "P8"));
            return string.Join(",", rows);
        }
        async Task Respond(int number, bool accept)
        {
            f.Clock.Advance(TimeSpan.FromSeconds(1));
            using var offers = await f.Patients[number].GetAsync($"/api/patient/units/{u1}/lightning-offers"); Expect(offers, 200);
            var offer = (await Read(offers)).GetProperty("offers").EnumerateArray().Single(item => item.GetProperty("status").GetString() == "pending").GetProperty("id").GetGuid();
            using var response = await f.Patients[number].PostAsJsonAsync($"/api/patient/units/{u1}/lightning-offers/{offer}/respond", new { accept }); Expect(response, 200);
        }
        async Task<int> Points(int number)
        {
            using var response = await f.Patients[number].GetAsync($"/api/patient/units/{u1}/attendance"); Expect(response, 200);
            return (await Read(response)).GetProperty("points").GetInt32();
        }

        await suite.Check("Prioridade: a vaga vai a quem tem mais pontos (não a quem ganha mais ao antecipar); recusa e vencimento passam a vez ao próximo por pontos e não mudam o score", async () =>
        {
            var slot = await OpenSlot("Cardiologia", "09:30");
            await f.AdvanceAsync(u1);
            Ensure(await Offers(slot) == "P6:pending", $"760 pontos recebe antes, mesmo com a consulta mais perto: {await Offers(slot)}.");
            await Respond(6, false);
            Ensure(await Offers(slot) == "P6:declined,P1:pending", $"Depois do 760, o de 700 (não o de 340, mais distante): {await Offers(slot)}.");
            f.Clock.Advance(TimeSpan.FromMinutes(10) + TimeSpan.FromSeconds(1)); await f.AdvanceAsync(u1);
            Ensure(await Offers(slot) == "P6:declined,P1:expired,P3:pending", $"Vencida a oferta do 700, o de 340 recebe: {await Offers(slot)}.");
            await Respond(3, false);
            Ensure(await Offers(slot) == "P6:declined,P1:expired,P3:declined", "Todos já passaram pela vaga: ninguém a recebe de novo.");
            Ensure(await Points(6) == 760 && await Points(1) == 700 && await Points(3) == 340, "Recusar e deixar vencer não mudam o score.");
        });

        await suite.Check("Prioridade: empate de pontos vai a quem tem a consulta mais distante; quem tem 0 ponto fica por último mesmo com a consulta mais distante de todas", async () =>
        {
            var slot = await OpenSlot("Pediatria", "10:00");
            await f.AdvanceAsync(u1);
            Ensure(await Offers(slot) == "P11:pending", $"Entre os dois de 700, o da consulta mais distante (P11): {await Offers(slot)}.");
            await Respond(11, false);
            Ensure(await Offers(slot) == "P11:declined,P10:pending", "O segundo de 700.");
            await Respond(10, false);
            Ensure(await Offers(slot) == "P11:declined,P10:declined,P8:pending", "O de 0 ponto só recebe depois dos de 700.");
        });

        await suite.Check("Pontos mudam a ordem de verdade: comparecimentos novos sobem o paciente na ordem de oferta (o cálculo roda na própria UBS)", async () =>
        {
            // P3 (340) ganha 6 comparecimentos depois das faltas: 340 + 60 + 70 + 80 + 90 + 100 + 100 = 840, acima do P6 (760) e do P1 (700).
            await Seed(f, u1, 3, 6, 0, daysBack: 1);
            Ensure(await Points(3) == 840, $"O score novo do P3 deveria ser 840: {await Points(3)}.");
            var slot = await OpenSlot("Cardiologia", "08:00");
            await f.AdvanceAsync(u1);
            Ensure(await Offers(slot) == "P3:pending", $"A ordem de oferta acompanha os pontos novos: {await Offers(slot)}.");
        });

        // ---- P-053: equipe sem CPF ----
        await suite.Check("P-053: a migração 022 apaga o CPF das contas de equipe que já existem, mantém o do paciente e passa a exigir CPF do paciente (CHECK)", async () =>
        {
            var legacy = Guid.NewGuid();
            await f.Exec("INSERT INTO nora.accounts(id,full_name,cpf,email,password_hash,scope,role) VALUES($1,'Atendente antigo',$2,'antigo@example.test','x','obs','attendant')", legacy, Cpf(60));
            await f.Exec("INSERT INTO nora.accounts(id,full_name,cpf,email,password_hash,scope,role) VALUES($1,'Gestora antiga',$2,'gestora.antiga@example.test','x','municipal','manager')", Guid.NewGuid(), Cpf(61));
            Ensure(await f.Scalar<long>("SELECT count(*) FROM nora.accounts WHERE scope IN ('obs','municipal') AND cpf IS NOT NULL") == 2, "Pré-condição: duas contas de equipe com CPF.");
            await f.Service<MigrationDao>().MigrateAsync();
            Ensure(await f.Scalar<long>("SELECT count(*) FROM nora.accounts WHERE scope IN ('obs','municipal') AND cpf IS NOT NULL") == 0, "A migração apaga o CPF de toda a equipe.");
            Ensure(await f.Scalar<long>("SELECT count(*) FROM nora.accounts WHERE scope='patient' AND cpf IS NULL") == 0 && await f.Scalar<string>("SELECT cpf FROM nora.accounts WHERE email='paciente1@example.test'") == Cpf(1), "O CPF do paciente continua.");
            try { await f.Exec("UPDATE nora.accounts SET cpf=NULL WHERE email='paciente1@example.test'"); throw new InvalidOperationException("Paciente sem CPF foi aceito."); }
            catch (PostgresException error) { Ensure(error.SqlState == PostgresErrorCodes.CheckViolation, $"Paciente sem CPF deve violar o CHECK, não {error.SqlState}."); }
            try { await f.Exec("INSERT INTO nora.accounts(id,full_name,cpf,email,password_hash,scope,role) VALUES($1,'Sem CPF','','sem.cpf@example.test','x','patient','patient')", Guid.NewGuid()); throw new InvalidOperationException("Paciente com CPF vazio foi aceito."); }
            catch (PostgresException) { }
            await f.Exec("INSERT INTO nora.accounts(id,full_name,email,password_hash,scope,role) VALUES($1,'Equipe sem CPF 1','a@example.test','x','obs','attendant')", Guid.NewGuid());
            await f.Exec("INSERT INTO nora.accounts(id,full_name,email,password_hash,scope,role) VALUES($1,'Equipe sem CPF 2','b@example.test','x','obs','attendant')", Guid.NewGuid());
        });

        await suite.Check("P-053: a equipe entra só por e-mail (CPF falha igual a credencial inválida, inclusive o CPF de um paciente); o paciente continua entrando por CPF ou e-mail", async () =>
        {
            await f.Service<AccountService>().ProvisionStaffAsync("obs", "administrator", "Equipe por e-mail", "equipe.email@example.test", Password);
            await f.Service<AccountService>().ProvisionStaffAsync("municipal", "manager", "Gestora por e-mail", "gestora.email@example.test", Password);
            async Task<int> Login(string scope, string identifier, string password = Password)
            {
                var client = f.NewClient(); using var response = await client.PostAsJsonAsync("/api/auth/login", new { scope, identifier, password }); return (int)response.StatusCode;
            }
            Ensure(await Login("obs", "equipe.email@example.test") == 200 && await Login("obs", "EQUIPE.EMAIL@example.test") == 200 && await Login("municipal", "gestora.email@example.test") == 200, "Login por e-mail da equipe.");
            Ensure(await Login("obs", Cpf(1)) == 401 && await Login("municipal", Cpf(1)) == 401 && await Login("obs", Cpf(60)) == 401, "CPF da equipe (ou de qualquer um) não entra: 401, igual a credencial inválida.");
            Ensure(await Login("obs", "equipe.email@example.test", "senha-errada-0000") == 401 && await Login("obs", "ninguem@example.test") == 401, "A resposta do e-mail com senha errada é a mesma 401.");
            Ensure(await Login("patient", Cpf(1)) == 200 && await Login("patient", "paciente1@example.test") == 200, "O paciente entra por CPF ou e-mail.");
        });

        await suite.Check("P-053: POST /api/municipal/staff cria sem CPF (cpf enviado é ignorado e nada dele é gravado), recusa e-mail repetido com 409, aceita senha curta, recusa senha vazia ou acima de 256, e as respostas não trazem cpf", async () =>
        {
            using (var created = await f.Municipal.PostAsJsonAsync("/api/municipal/staff", new { fullName = "Administrador Novo", email = "nova@example.test", password = Password, role = "administrator" }))
            {
                Expect(created, 201); var body = await Read(created);
                Ensure(body.GetProperty("email").GetString() == "nova@example.test" && !body.TryGetProperty("cpf", out _), "A resposta de criação não traz cpf.");
            }
            using (var ignored = await f.Municipal.PostAsJsonAsync("/api/municipal/staff", new { fullName = "Atendente Ignora", cpf = Cpf(70), email = "ignora@example.test", password = Password, role = "administrator" })) Expect(ignored, 201);
            Ensure(await f.Scalar<long>("SELECT count(*) FROM nora.accounts WHERE email IN ('nova@example.test','ignora@example.test') AND cpf IS NULL") == 2, "Nenhum CPF foi gravado.");
            using (var duplicate = await f.Municipal.PostAsJsonAsync("/api/municipal/staff", new { fullName = "Outra Pessoa", email = "NOVA@example.test", password = Password, role = "attendant" })) await ExpectProblem(duplicate, 409, "conflict");
            using (var withoutEmail = await f.Municipal.PostAsJsonAsync("/api/municipal/staff", new { fullName = "Sem Email", password = Password, role = "attendant" })) await ExpectProblem(withoutEmail, 400, "invalid-input");
            // Senha sem restrições (2026-11-24): curta e sem número é aceita; vazia e acima de 256 caracteres continuam 400.
            using (var shortPassword = await f.Municipal.PostAsJsonAsync("/api/municipal/staff", new { fullName = "Senha Curta", email = "curta@example.test", password = "a", role = "attendant" })) Expect(shortPassword, 201);
            using (var emptyPassword = await f.Municipal.PostAsJsonAsync("/api/municipal/staff", new { fullName = "Senha Vazia", email = "vazia@example.test", password = "", role = "attendant" })) await ExpectProblem(emptyPassword, 400, "invalid-input");
            using (var longPassword = await f.Municipal.PostAsJsonAsync("/api/municipal/staff", new { fullName = "Senha Longa", email = "longa@example.test", password = new string('x', 257), role = "attendant" })) await ExpectProblem(longPassword, 400, "invalid-input");
            using (var list = await f.Municipal.GetAsync("/api/municipal/staff")) { Expect(list, 200); Ensure(!(await list.Content.ReadAsStringAsync()).Contains("\"cpf\"", StringComparison.Ordinal), "A listagem da equipe não traz cpf."); }
            var service = f.Service<AccountService>();
            try { await service.ProvisionAsync("patient", "patient", new("Paciente sem CPF", "", "semcpf@example.test", Password)); throw new InvalidOperationException("Paciente sem CPF foi criado."); }
            catch (ApiInputException) { }
            try { await service.ProvisionStaffAsync("patient", "patient", "Paciente", "paciente.staff@example.test", Password); throw new InvalidOperationException("ProvisionStaffAsync criou paciente."); }
            catch (ApiInputException) { }
            await f.GrantAsync(await f.Scalar<Guid>("SELECT id FROM nora.accounts WHERE email='nova@example.test'"), u1);
            var fresh = f.NewClient(); var login = await f.LoginAsync(fresh, "obs", "nova@example.test");
            Ensure(login.GetProperty("user").GetProperty("mustChangePassword").GetBoolean(), "A conta criada pela Secretaria pede troca de senha.");
            using var blocked = await fresh.GetAsync($"/api/obs/units/{u1}/registry"); await ExpectProblem(blocked, 403, "password-change-required");
        });

        // ---- P4: histórico paginado das vagas (a paginação da fila do OBS saiu com a fila manual em 2026-11-24) ----
        await f.RegisterAsync(20, u2); await f.RegisterAsync(21, u2); await f.RegisterAsync(22, u2);
        await suite.Check("P4: as vagas do OBS devolvem { items, historyCursor }: ativas (open, offered, awaiting_approval) sempre, encerradas (accepted, closed) em páginas com cursor, sem repetir nem pular", async () =>
        {
            // 7 encerradas (accepted e closed) com datas de criação repetidas de duas em duas, mais uma vaga aberta bem antiga.
            await f.UnitExec(u2, "INSERT INTO nora_unit.lightning_slots(id,appointment_id,status,created_at,created_by,origin,specialty,specialty_id,professional,professional_id,room_id,starts_at,ends_at,closed_reason) " +
                "SELECT gen_random_uuid(),NULL,CASE WHEN g%2=0 THEN 'accepted' ELSE 'closed' END,$1::timestamptz+(g/2)*interval '1 minute',$2,'call-next','Cardiologia',NULL,'Dra. Ana',NULL,NULL,$3::timestamptz+g*interval '1 hour',$3::timestamptz+g*interval '1 hour'+interval '30 minutes',CASE WHEN g%2=0 THEN NULL ELSE 'too-late' END FROM generate_series(1,7) g",
                RoundClock.T0.AddDays(-3), f.AdminId, RoundClock.T0.AddDays(5));
            var oldOpen = Guid.NewGuid();
            await f.UnitExec(u2, "INSERT INTO nora_unit.lightning_slots(id,appointment_id,status,created_at,created_by,origin,specialty,specialty_id,professional,professional_id,room_id,starts_at,ends_at) VALUES($1,NULL,'open',$2,$3,'call-next','Cardiologia',NULL,'Dra. Ana',NULL,NULL,$4,$5)",
                oldOpen, RoundClock.T0.AddDays(-20), f.AdminId, RoundClock.T0.AddDays(6), RoundClock.T0.AddDays(6).AddMinutes(30));
            var expected = new List<Guid>();
            await using (var unit = await f.Router.OpenAsync(u2))
            {
                await using var command = new NpgsqlCommand("SELECT id FROM nora_unit.lightning_slots WHERE status IN ('accepted','closed') ORDER BY created_at DESC,id DESC", unit);
                await using var reader = await command.ExecuteReaderAsync(); while (await reader.ReadAsync()) expected.Add(reader.GetGuid(0));
            }
            Ensure(expected.Count == 7, "Pré-condição: 7 encerradas.");
            async Task<(List<Guid> Active, List<Guid> Closed, string? Cursor)> Page(string query)
            {
                using var response = await f.Admin.GetAsync($"/api/obs/units/{u2}/lightning-slots{query}"); Expect(response, 200);
                var body = await Read(response); var items = body.GetProperty("items").EnumerateArray().ToList();
                return (items.Where(item => item.GetProperty("status").GetString() is "open" or "offered" or "awaiting_approval").Select(item => item.GetProperty("id").GetGuid()).ToList(), items.Where(item => item.GetProperty("status").GetString() is "accepted" or "closed").Select(item => item.GetProperty("id").GetGuid()).ToList(),
                    body.GetProperty("historyCursor").GetString());
            }
            var first = await Page("?historyLimit=3");
            Ensure(first.Active.SequenceEqual(new[] { oldOpen }) && first.Closed.Count == 3 && first.Cursor is not null, "Página 1: a vaga ativa antiga e 3 encerradas.");
            var second = await Page($"?historyLimit=3&historyBefore={Uri.EscapeDataString(first.Cursor!)}");
            Ensure(second.Active.Count == 0 && second.Closed.Count == 3 && second.Cursor is not null, "Página 2: só encerradas.");
            var third = await Page($"?historyLimit=3&historyBefore={Uri.EscapeDataString(second.Cursor!)}");
            Ensure(third.Active.Count == 0 && third.Closed.Count == 1 && third.Cursor is null, "Página 3: a que resta e nenhum cursor.");
            Ensure(first.Closed.ToHashSet().SetEquals(expected.Take(3)) && second.Closed.ToHashSet().SetEquals(expected.Skip(3).Take(3)) && third.Closed.ToHashSet().SetEquals(expected.Skip(6)), "Cada página traz as mais recentes que ainda não vieram, sem repetir nem pular.");
            var all = await Page(""); Ensure(all.Active.Count == 1 && all.Closed.Count == 7 && all.Cursor is null, "Sem parâmetros: tudo, sem cursor.");
            foreach (var invalid in new[] { "?historyLimit=0", "?historyLimit=501", "?historyBefore=nao-e-cursor" })
            {
                using var response = await f.Admin.GetAsync($"/api/obs/units/{u2}/lightning-slots{invalid}"); Ensure((int)response.StatusCode == 400, $"{invalid}: HTTP {(int)response.StatusCode}.");
            }
        });

        // ---- P5 ----
        await suite.Check("P5: a migração de UBS 017 cria os índices por CPF e por data de entrada da fila (histórico); a versão esperada é a 19 (Vaga Relâmpago automática) e a lista de um CPF usa o índice", async () =>
        {
            Ensure(UnitSchema.ExpectedVersion == 19, $"ExpectedVersion deveria ser 19, é {UnitSchema.ExpectedVersion}.");
            Ensure(await f.UnitScalar<int>(u1, "SELECT version FROM nora_unit.schema_version") == 19, "O banco da UBS criada pela API está na versão 19.");
            Ensure(await f.UnitScalar<long>(u1, "SELECT count(*) FROM pg_indexes WHERE schemaname='nora_unit' AND indexname IN ('waiting_list_cpf_idx','waiting_list_entered_idx')") == 2, "Os dois índices existem.");
            await using var unit = await f.Router.OpenAsync(u1);
            await using var transaction = await unit.BeginTransactionAsync();
            await using (var off = new NpgsqlCommand("SET LOCAL enable_seqscan=off", unit)) await off.ExecuteNonQueryAsync();
            async Task<string> Plan(string sql, params object[] values)
            {
                await using var explain = new NpgsqlCommand("EXPLAIN " + sql, unit); foreach (var value in values) explain.Parameters.AddWithValue(value);
                await using var reader = await explain.ExecuteReaderAsync(); var lines = new List<string>(); while (await reader.ReadAsync()) lines.Add(reader.GetString(0));
                return string.Join("\n", lines);
            }
            Ensure((await Plan("SELECT id FROM nora_unit.waiting_list WHERE cpf=$1 ORDER BY entered_at,id", Cpf(1))).Contains("waiting_list_cpf_idx", StringComparison.Ordinal), "A leitura por CPF usa o índice por CPF.");
            Ensure((await Plan("SELECT id FROM nora_unit.waiting_list WHERE status IN ('completed','cancelled') ORDER BY entered_at DESC,id DESC LIMIT 100")).Contains("waiting_list_entered_idx", StringComparison.Ordinal), "A página de histórico usa o índice por data de entrada.");
        });

        await suite.Check("P5: um banco de UBS na versão 16 sobe para a 19 com a migração (índices criados, dados intactos)", async () =>
        {
            var legacy = await f.CreateLegacyUnitAsync(16);
            // O roteador recusa banco desatualizado (503), então a conferência usa uma conexão direta ao banco da UBS.
            async Task<(int Version, long Indexes)> State()
            {
                await using var raw = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(f.ConnectionString) { Database = f.Router.DerivedDatabaseName(legacy), Pooling = false }.ConnectionString);
                await raw.OpenAsync();
                await using var command = new NpgsqlCommand("SELECT (SELECT version FROM nora_unit.schema_version),(SELECT count(*) FROM pg_indexes WHERE indexname IN ('waiting_list_cpf_idx','waiting_list_entered_idx'))", raw);
                await using var reader = await command.ExecuteReaderAsync(); await reader.ReadAsync();
                return (reader.GetInt32(0), reader.GetInt64(1));
            }
            Ensure(await State() == (16, 0), "Pré-condição: versão 16, sem os índices.");
            await f.Service<UnitMigrationDao>().MigrateAsync(legacy);
            Ensure(await State() == (19, 2), "Depois da migração: versão 19 e os dois índices.");
            await f.Service<UnitMigrationDao>().MigrateAsync(legacy);
            Ensure(await State() == (19, 2), "Migrar de novo é inofensivo.");
        });

        // ---- Medidas de P4 e P5 (antes/depois de docs/desempenho.md) ----
        await suite.Check("MEDIDA P4 e P5: histórico grande (20.000 entradas antigas da fila e 5.000 vagas encerradas): primeira página das vagas contra a lista inteira; leitura por CPF da fila com e sem o índice", async () =>
        {
            await f.UnitExec(u2, "INSERT INTO nora_unit.patient_registry(cpf,full_name,email,created_by) SELECT lpad((80000000000+g)::text,11,'0'),'Paciente '||g::text,'m'||g::text||'@example.test',$1 FROM generate_series(1,2000) g", f.AdminId);
            await f.UnitExec(u2, "INSERT INTO nora_unit.waiting_list(cpf,specialty,entered_at,status,created_by) SELECT lpad((80000000000+(g%2000)+1)::text,11,'0'),'Cardiologia',$1::timestamptz-g*interval '1 minute','cancelled',$2 FROM generate_series(1,20000) g", RoundClock.T0.AddDays(-60), f.AdminId);
            await f.UnitExec(u2, "INSERT INTO nora_unit.lightning_slots(id,appointment_id,status,created_at,created_by,origin,specialty,specialty_id,professional,professional_id,room_id,starts_at,ends_at,closed_reason) " +
                "SELECT gen_random_uuid(),NULL,'closed',$1::timestamptz-g*interval '1 minute',$2,'call-next','Cardiologia',NULL,'Dra. Ana',NULL,NULL,$1::timestamptz+g*interval '1 hour',$1::timestamptz+g*interval '1 hour'+interval '30 minutes','too-late' FROM generate_series(1,5000) g", RoundClock.T0.AddDays(-60), f.AdminId);
            await f.UnitExec(u2, "ANALYZE nora_unit.waiting_list");
            foreach (var (route, label) in new[] { ("lightning-slots", "vagas") })
            {
                async Task<(double Milliseconds, long Bytes, int Items)> Fetch(string query)
                {
                    var watch = System.Diagnostics.Stopwatch.StartNew();
                    using var response = await f.Admin.GetAsync($"/api/obs/units/{u2}/{route}{query}"); Expect(response, 200);
                    var text = await response.Content.ReadAsStringAsync(); watch.Stop();
                    using var json = JsonDocument.Parse(text);
                    return (watch.Elapsed.TotalMilliseconds, text.Length, json.RootElement.GetProperty("items").GetArrayLength());
                }
                await Fetch("");
                var first = await Fetch("");
                // "Antes" do P4: a lista inteira, que equivale a seguir todos os cursores com 500 por vez.
                var watchAll = System.Diagnostics.Stopwatch.StartNew(); long bytes = 0; var items = 0; string? cursor = null;
                do
                {
                    using var response = await f.Admin.GetAsync($"/api/obs/units/{u2}/{route}?historyLimit=500" + (cursor is null ? "" : "&historyBefore=" + Uri.EscapeDataString(cursor))); Expect(response, 200);
                    var text = await response.Content.ReadAsStringAsync(); bytes += text.Length;
                    using var json = JsonDocument.Parse(text); items += json.RootElement.GetProperty("items").GetArrayLength();
                    cursor = json.RootElement.GetProperty("historyCursor").GetString();
                } while (cursor is not null);
                watchAll.Stop();
                Console.WriteLine($"MEDIDA P4 {label}: primeira página {first.Items} itens, {first.Bytes / 1024.0:F1} KiB, {first.Milliseconds:F1} ms; histórico inteiro {items} itens, {bytes / 1024.0:F1} KiB, {watchAll.Elapsed.TotalMilliseconds:F0} ms (a resposta de antes, a cada 20 s por computador).");
            }
            async Task<(string Node, double Milliseconds)> Explain()
            {
                var times = new List<double>(); var node = "";
                for (var run = 0; run < 15; run++)
                {
                    await using var unit = await f.Router.OpenAsync(u2);
                    await using var command = new NpgsqlCommand("EXPLAIN (ANALYZE, TIMING OFF) SELECT q.id,q.cpf,p.full_name,q.specialty,q.entered_at,q.status FROM nora_unit.waiting_list q JOIN nora_unit.patient_registry p ON p.cpf=q.cpf WHERE q.cpf=$1 ORDER BY q.entered_at,q.id", unit);
                    command.Parameters.AddWithValue("80000000123");
                    await using var reader = await command.ExecuteReaderAsync(); var lines = new List<string>(); while (await reader.ReadAsync()) lines.Add(reader.GetString(0));
                    times.Add(double.Parse(lines.Single(line => line.StartsWith("Execution Time:", StringComparison.Ordinal)).Split(' ')[2], System.Globalization.CultureInfo.InvariantCulture));
                    node = lines.First(line => line.Contains("waiting_list", StringComparison.Ordinal)).Trim();
                }
                times.Sort(); return (node, times[times.Count / 2]);
            }
            var with = await Explain();
            await f.UnitExec(u2, "DROP INDEX nora_unit.waiting_list_cpf_idx");
            var without = await Explain();
            await f.UnitExec(u2, "CREATE INDEX IF NOT EXISTS waiting_list_cpf_idx ON nora_unit.waiting_list(cpf, entered_at, id)");
            Console.WriteLine($"MEDIDA P5 leitura por CPF (20.000 entradas): sem o índice {without.Milliseconds:F3} ms ({without.Node}); com o índice {with.Milliseconds:F3} ms ({with.Node}).");
            Ensure(!with.Node.Contains("Seq Scan", StringComparison.Ordinal) && without.Node.Contains("Seq Scan", StringComparison.Ordinal), "Com o índice a leitura por CPF deixa de varrer a tabela (e sem ele varre).");
        });
    }

    /// <summary>
    /// P3: a listagem de cadastro do OBS reaproveita a conexão central do executor. Com o pool central limitado a UMA conexão, uma segunda abertura dentro da rota ficaria esperando a primeira (que só é devolvida no
    /// fim da rota) e a requisição estouraria o tempo; com a conexão reaproveitada ela responde. Também mede quantas conexões a rota usa.
    /// </summary>
    private static async Task RunPoolAsync(Suite suite, string connection)
    {
        var one = new NpgsqlConnectionStringBuilder(connection) { MaxPoolSize = 1, Timeout = 4 }.ConnectionString;
        await using var f = await StartAsync(connection, "closingpool", unitCount: 1, extraArgs: ["--ConnectionStrings:Nora", one]);
        var u1 = f.Unit1;
        // Preparo direto no banco: o cadastro e o vínculo pela API abrem uma segunda conexão central (o índice de cadastros e a conferência do vínculo), que o pool de 1 não comporta.
        for (var number = 1; number <= 5; number++)
        {
            var id = (await f.Service<AccountService>().ProvisionAsync("patient", "patient", new($"Paciente {number}", Cpf(number), $"paciente{number}@example.test", Password))).Id;
            await f.UnitExec(u1, "INSERT INTO nora_unit.patient_registry(cpf,full_name,email,created_by) VALUES($1,$2,$3,$4)", Cpf(number), $"Paciente {number}", $"paciente{number}@example.test", f.AdminId);
            await f.Exec("INSERT INTO nora.patient_units(patient_id,unit_id) VALUES($1,$2)", id, u1);
            var client = f.NewClient(); await f.LoginAsync(client, "patient", Cpf(number)); f.Patients[number] = client;
        }
        await Seed(f, u1, 1, 3, 1);
        await suite.Check("P3: o registry do OBS usa a conexão central do executor (funciona com o pool central de 1 conexão) e traz presença e score", async () =>
        {
            using var response = await f.Admin.GetAsync($"/api/obs/units/{u1}/registry"); Expect(response, 200);
            var rows = (await Read(response)).EnumerateArray().ToList();
            Ensure(rows.Count == 5 && rows.Single(row => row.GetProperty("cpf").GetString() == Cpf(1)).GetProperty("attendance").GetProperty("points").GetInt32() == 850, "O cadastro e o score voltam completos (falta −60, depois +60 +70 +80 → 850).");
            Ensure(rows.Count(row => row.GetProperty("isOnline").GetBoolean()) == 0, "Ninguém online sem presença.");
            using var presence = await f.Patients[2].PostAsync("/api/patient/presence", null);
            using var again = await f.Admin.GetAsync($"/api/obs/units/{u1}/registry"); Expect(again, 200);
            Ensure((await Read(again)).EnumerateArray().Single(row => row.GetProperty("cpf").GetString() == Cpf(2)).GetProperty("isOnline").GetBoolean() && (int)presence.StatusCode == 204, "A presença continua refletida.");
            var watch = System.Diagnostics.Stopwatch.StartNew(); for (var i = 0; i < 50; i++) { using var each = await f.Admin.GetAsync($"/api/obs/units/{u1}/registry"); Expect(each, 200); } watch.Stop();
            Console.WriteLine($"MEDIDA P3 registry (5 pacientes, pool central de 1): {watch.Elapsed.TotalMilliseconds / 50:F2} ms por chamada.");
        });
    }

}
