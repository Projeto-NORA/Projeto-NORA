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

        // ---- Score em pontos ----
        // número do paciente → (concluídas, faltas, pontos, faixa): 1 não tem histórico.
        var cases = new (int Number, int Completed, int Missed, int Points, string Band)[]
        {
            (2, 299, 701, 299, "low"), (3, 3, 7, 300, "regular"), (4, 599, 401, 599, "regular"), (5, 3, 2, 600, "good"), (6, 799, 201, 799, "good"), (7, 4, 1, 800, "excellent"), (8, 0, 1, 0, "low")
        };
        foreach (var (number, completed, missed, _, _) in cases) await Seed(f, u1, number, completed, missed);

        await suite.Check("Score em pontos: o registry traz points, band e hasHistory em cada paciente (299/300, 599/600, 799/800 nas bordas; sem histórico = 1000, excelente, hasHistory falso); o score antigo continua", async () =>
        {
            using var response = await f.Admin.GetAsync($"/api/obs/units/{u1}/registry"); Expect(response, 200);
            var rows = (await Read(response)).EnumerateArray().ToDictionary(row => row.GetProperty("cpf").GetString()!);
            var none = rows[Cpf(1)].GetProperty("attendance");
            Ensure(none.GetProperty("points").GetInt32() == 1000 && none.GetProperty("band").GetString() == "excellent" && !none.GetProperty("hasHistory").GetBoolean() && none.GetProperty("score").ValueKind == JsonValueKind.Null, "Sem consulta encerrada: 1000 pontos, excelente, sem histórico, score nulo.");
            foreach (var (number, completed, missed, points, band) in cases)
            {
                var attendance = rows[Cpf(number)].GetProperty("attendance");
                Ensure(attendance.GetProperty("points").GetInt32() == points && attendance.GetProperty("band").GetString() == band && attendance.GetProperty("hasHistory").GetBoolean(), $"Paciente {number} ({completed}/{missed}): esperado {points} pontos, faixa {band}; veio {attendance.GetProperty("points").GetInt32()} {attendance.GetProperty("band").GetString()}.");
                Ensure(attendance.GetProperty("completed").GetInt64() == completed && attendance.GetProperty("missed").GetInt64() == missed && Math.Abs(attendance.GetProperty("score").GetDouble() - Math.Round(completed / (double)(completed + missed), 4)) < 1e-9, $"Os campos antigos do paciente {number} mudaram.");
            }
        });

        await suite.Check("Score em pontos: a rota do paciente (/attendance) traz os mesmos points, band e hasHistory; a Secretaria continua sem attendance", async () =>
        {
            foreach (var (number, points, band, history) in new[] { (1, 1000, "excellent", false), (2, 299, "low", true), (3, 300, "regular", true), (5, 600, "good", true), (7, 800, "excellent", true) })
            {
                using var response = await f.Patients[number].GetAsync($"/api/patient/units/{u1}/attendance"); Expect(response, 200);
                var body = await Read(response);
                Ensure(body.GetProperty("points").GetInt32() == points && body.GetProperty("band").GetString() == band && body.GetProperty("hasHistory").GetBoolean() == history && body.GetProperty("windowMonths").GetInt32() == 12, $"Paciente {number} na própria rota: {body}.");
            }
            using var municipal = await f.Municipal.GetAsync($"/api/municipal/units/{u1}/registry"); Expect(municipal, 200);
            Ensure(!(await municipal.Content.ReadAsStringAsync()).Contains("points", StringComparison.Ordinal), "A listagem da Secretaria não traz o score.");
        });

        // ---- Fila do OBS: pontos e ordem de oferta ----
        // Cardiologia na ordem de entrada: 3 (300), 1 (1000), 6 (799). Pediatria: 8 (0), 10 (1000, sem histórico), 11 (1000, sem histórico). Ortopedia: 5 (600) sozinho. O 2 entra e sai.
        async Task Join(int number, string specialty) { using var join = await f.Admin.PostAsJsonAsync($"/api/obs/units/{u1}/waiting-list", new { cpf = Cpf(number), specialty }); Expect(join, 201); f.Clock.Advance(TimeSpan.FromSeconds(5)); }
        foreach (var number in new[] { 3, 1, 6 }) await Join(number, "Cardiologia");
        foreach (var number in new[] { 8, 10, 11 }) await Join(number, "Pediatria");
        await Join(5, "Ortopedia"); await Join(2, "Ortopedia");
        var cancelledEntry = await f.UnitScalar<long>(u1, "SELECT id FROM nora_unit.waiting_list WHERE cpf=$1", Cpf(2));
        using (var cancel = await f.Admin.PostAsJsonAsync($"/api/obs/units/{u1}/waiting-list/{cancelledEntry}/cancel", new { confirmed = true })) Expect(cancel, 204);

        async Task<Dictionary<string, JsonElement>> Queue(HttpClient? client = null, string query = "")
        {
            using var response = await (client ?? f.Admin).GetAsync($"/api/obs/units/{u1}/waiting-list{query}"); Expect(response, 200);
            return (await Read(response)).GetProperty("items").EnumerateArray().ToDictionary(item => item.GetProperty("cpf").GetString()! + "/" + item.GetProperty("specialty").GetString());
        }
        async Task<string> OfferOrder(string specialty, params int[] numbers)
        {
            var queue = await Queue();
            return string.Join(",", numbers.Select(number => queue[Cpf(number) + "/" + specialty].GetProperty("offerOrder") is { ValueKind: JsonValueKind.Number } order ? order.GetInt32().ToString() : "-"));
        }

        await suite.Check("Fila do OBS: cada entrada traz points e band do CPF na UBS e offerOrder (pontos desc, depois entrada) por especialidade; entrada encerrada tem offerOrder nulo", async () =>
        {
            var queue = await Queue();
            Ensure(queue[Cpf(3) + "/Cardiologia"].GetProperty("points").GetInt32() == 300 && queue[Cpf(3) + "/Cardiologia"].GetProperty("band").GetString() == "regular"
                && queue[Cpf(1) + "/Cardiologia"].GetProperty("points").GetInt32() == 1000 && queue[Cpf(1) + "/Cardiologia"].GetProperty("band").GetString() == "excellent"
                && queue[Cpf(6) + "/Cardiologia"].GetProperty("points").GetInt32() == 799 && queue[Cpf(6) + "/Cardiologia"].GetProperty("band").GetString() == "good", "Pontos e faixas da Cardiologia.");
            Ensure(await OfferOrder("Cardiologia", 1, 6, 3) == "1,2,3", "Cardiologia: 1000, depois 799, depois 300, mesmo tendo entrado por último.");
            Ensure(await OfferOrder("Pediatria", 10, 11, 8) == "1,2,3", "Pediatria: empate em 1000 segue a ordem de entrada (10 antes do 11); o de 0 ponto é o último.");
            Ensure(await OfferOrder("Ortopedia", 5) == "1" && queue[Cpf(2) + "/Ortopedia"].GetProperty("status").GetString() == "cancelled" && queue[Cpf(2) + "/Ortopedia"].GetProperty("offerOrder").ValueKind == JsonValueKind.Null, "A posição vale por especialidade e a entrada encerrada não tem posição.");
            using var own = await f.Patients[3].GetAsync($"/api/patient/units/{u1}/waiting-list"); Expect(own, 200);
            Ensure((await Read(own)).GetProperty("entries").EnumerateArray().All(entry => !entry.TryGetProperty("points", out _)), "As rotas do paciente seguem sem mudança.");
        });

        // ---- Prioridade da Vaga Relâmpago ----
        async Task<Guid> OpenSlot(string specialty, string startLocal, Guid? professionalId = null)
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
            await using var command = new NpgsqlCommand("SELECT q.cpf||':'||o.status FROM nora_unit.lightning_offers o JOIN nora_unit.waiting_list q ON q.id=o.entry_id WHERE o.slot_id=$1 ORDER BY o.expires_at,o.id", unit);
            command.Parameters.AddWithValue(slot);
            await using var reader = await command.ExecuteReaderAsync(); var rows = new List<string>();
            while (await reader.ReadAsync()) rows.Add(reader.GetString(0).Replace(Cpf(1), "P1").Replace(Cpf(3), "P3").Replace(Cpf(6), "P6").Replace(Cpf(8), "P8").Replace(Cpf(10), "P10").Replace(Cpf(11), "P11"));
            return string.Join(",", rows);
        }
        async Task Respond(int number, bool accept)
        {
            f.Clock.Advance(TimeSpan.FromSeconds(1));
            using var offers = await f.Patients[number].GetAsync($"/api/patient/units/{u1}/lightning-offers"); Expect(offers, 200);
            var offer = (await Read(offers)).GetProperty("offers").EnumerateArray().Single(item => item.GetProperty("status").GetString() == "pending").GetProperty("id").GetGuid();
            using var response = await f.Patients[number].PostAsJsonAsync($"/api/patient/units/{u1}/lightning-offers/{offer}/respond", new { accept }); Expect(response, 200);
        }

        var slotCardiology = Guid.Empty;
        await suite.Check("Prioridade: a vaga vai a quem tem mais pontos (não ao primeiro da fila); recusa e vencimento passam a vez ao próximo por pontos e não mudam o score", async () =>
        {
            slotCardiology = await OpenSlot("Cardiologia", "09:30");
            await f.AdvanceAsync(u1);
            Ensure(await Offers(slotCardiology) == "P1:pending", $"1000 pontos recebe antes de quem entrou primeiro (300): {await Offers(slotCardiology)}.");
            await Respond(1, false);
            Ensure(await Offers(slotCardiology) == "P1:declined,P6:pending", $"Depois do 1000, o de 799 (não o de 300): {await Offers(slotCardiology)}.");
            f.Clock.Advance(TimeSpan.FromMinutes(10) + TimeSpan.FromSeconds(1)); await f.AdvanceAsync(u1);
            Ensure(await Offers(slotCardiology) == "P1:declined,P6:expired,P3:pending", $"Vencida a oferta do 799, o de 300 recebe: {await Offers(slotCardiology)}.");
            await Respond(3, false);
            Ensure(await Offers(slotCardiology) == "P1:declined,P6:expired,P3:declined", "Todos já passaram pela vaga: ninguém a recebe de novo.");
            var queue = await Queue();
            Ensure(queue[Cpf(1) + "/Cardiologia"].GetProperty("points").GetInt32() == 1000 && queue[Cpf(6) + "/Cardiologia"].GetProperty("points").GetInt32() == 799 && queue[Cpf(3) + "/Cardiologia"].GetProperty("points").GetInt32() == 300, "Recusar e deixar vencer não baixam o score.");
            Ensure(await OfferOrder("Cardiologia", 1, 6, 3) == "1,2,3" && queue.Values.Where(item => item.GetProperty("specialty").GetString() == "Cardiologia").All(item => item.GetProperty("status").GetString() == "waiting"), "Todos voltam à fila na mesma posição de oferta.");
        });

        await suite.Check("Prioridade: empate de pontos segue a ordem de entrada na fila; quem tem 0 ponto fica por último mesmo entrando primeiro", async () =>
        {
            var slot = await OpenSlot("Pediatria", "10:00");
            await f.AdvanceAsync(u1);
            Ensure(await Offers(slot) == "P10:pending", $"Entre os dois de 1000, o que entrou antes (P10), apesar do P8 ter entrado primeiro: {await Offers(slot)}.");
            await Respond(10, false);
            Ensure(await Offers(slot) == "P10:declined,P11:pending", "O segundo de 1000.");
            await Respond(11, false);
            Ensure(await Offers(slot) == "P10:declined,P11:declined,P8:pending", "O de 0 ponto só recebe depois dos de 1000.");
        });

        await suite.Check("Chamar próximo: a prévia e a oferta seguem a mesma ordem da distribuição (mais pontos primeiro); com a oferta pendente a prévia passa ao próximo por pontos", async () =>
        {
            async Task<string> Preview(string startLocal)
            {
                using var response = await f.Admin.GetAsync($"/api/obs/units/{u1}/waiting-list/next?specialtyId={cardiology}&professionalId={ana}&startsAt={Uri.EscapeDataString(Wednesday(startLocal).ToString("O"))}"); Expect(response, 200);
                return (await Read(response)).GetProperty("next").GetProperty("patientName").GetString()!;
            }
            async Task<(string Patient, JsonElement Slot)> Call(string startLocal)
            {
                using var response = await f.Admin.PostAsJsonAsync($"/api/obs/units/{u1}/waiting-list/call-next", new { specialtyId = cardiology, professionalId = ana, startsAt = Wednesday(startLocal), confirmed = true }); Expect(response, 201);
                var body = await Read(response); return (body.GetProperty("offer").GetProperty("patientName").GetString()!, body.GetProperty("slot"));
            }
            Ensure(await Preview("08:00") == "Paciente 1", "A prévia mostra o de 1000 pontos, não o primeiro da fila (Paciente 3).");
            Ensure((await Call("08:00")).Patient == "Paciente 1", "A chamada oferece a quem a prévia mostrou.");
            Ensure(await Preview("08:30") == "Paciente 6", "Com o Paciente 1 já com oferta pendente, a prévia passa ao de 799.");
            Ensure((await Call("08:30")).Patient == "Paciente 6", "A chamada oferece a quem a prévia mostrou (799, não 300).");
            Ensure(await Preview("09:00") == "Paciente 3", "Por último, o de 300 pontos.");
            // Recusar a oferta do chamar próximo devolve o Paciente 1 à fila na mesma posição e com os mesmos pontos: na próxima chamada ele volta a ser o primeiro.
            await Respond(1, false);
            Ensure(await Preview("09:00") == "Paciente 1", "Quem recusou volta ao topo da ordem (mesmos pontos, mesma entrada).");
        });

        await suite.Check("Pontos mudam a ordem de verdade: um novo comparecimento concluído sobe, uma falta nova desce (o cálculo roda na própria UBS, sobre as consultas encerradas)", async () =>
        {
            // O de 300 pontos (P3) ganha 30 consultas concluídas: 33/(33+7) = 0,825 → 825 pontos, passa à frente do de 799 (P6) na Cardiologia, mas não do 1000 (P1).
            await Seed(f, u1, 3, 30, 0, daysBack: 9);
            var queue = await Queue();
            Ensure(queue[Cpf(3) + "/Cardiologia"].GetProperty("points").GetInt32() == 825 && queue[Cpf(3) + "/Cardiologia"].GetProperty("band").GetString() == "excellent", "O score novo aparece na fila.");
            Ensure(await OfferOrder("Cardiologia", 1, 3, 6) == "1,2,3", "A ordem de oferta acompanha os pontos novos.");
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

        await suite.Check("P-053: POST /api/municipal/staff cria sem CPF (cpf enviado é ignorado e nada dele é gravado), recusa e-mail repetido com 409, e as respostas não trazem cpf", async () =>
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
            using (var shortPassword = await f.Municipal.PostAsJsonAsync("/api/municipal/staff", new { fullName = "Senha Curta", email = "curta@example.test", password = "curta", role = "attendant" })) await ExpectProblem(shortPassword, 400, "invalid-input");
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

        // ---- P4: histórico paginado das listas ----
        var closedRows = 12;
        await f.RegisterAsync(20, u2); await f.RegisterAsync(21, u2); await f.RegisterAsync(22, u2);
        await suite.Check("P4: a fila do OBS devolve { items, historyCursor }; os ativos vêm sempre, o histórico em páginas com cursor, sem repetir nem pular (inclusive com a mesma data de entrada)", async () =>
        {
            // 12 encerradas do CPF 20 com datas de entrada repetidas de dois em dois; um ativo recente (pela API) e um ativo bem antigo (mais velho que todo o histórico).
            await f.UnitExec(u2, "INSERT INTO nora_unit.waiting_list(cpf,specialty,entered_at,status,created_by) SELECT $1,'Cardiologia',$2::timestamptz+(g/2)*interval '1 minute',CASE WHEN g%2=0 THEN 'cancelled' ELSE 'completed' END,$3 FROM generate_series(1,$4) g", Cpf(20), RoundClock.T0.AddDays(-3), f.AdminId, closedRows);
            await f.UnitExec(u2, "INSERT INTO nora_unit.waiting_list(cpf,specialty,entered_at,status,created_by) VALUES($1,'Neurologia',$2,'waiting',$3)", Cpf(22), RoundClock.T0.AddDays(-10), f.AdminId);
            using (var join = await f.Admin.PostAsJsonAsync($"/api/obs/units/{u2}/waiting-list", new { cpf = Cpf(21), specialty = "Cardiologia" })) Expect(join, 201);
            var expected = await ClosedIds(f, u2);
            Ensure(expected.Count == closedRows, "Pré-condição: 12 encerradas.");
            async Task<(List<long> Active, List<long> Closed, string? Cursor)> Page(string query)
            {
                using var response = await f.Admin.GetAsync($"/api/obs/units/{u2}/waiting-list{query}"); Expect(response, 200);
                var body = await Read(response); var items = body.GetProperty("items").EnumerateArray().ToList();
                Ensure(items.All(item => item.GetProperty("points").GetInt32() == 1000 && item.GetProperty("band").GetString() == "excellent"), "Cada item traz pontos e faixa.");
                return (items.Where(item => item.GetProperty("status").GetString() is "waiting" or "offered").Select(item => item.GetProperty("id").GetInt64()).ToList(), items.Where(item => item.GetProperty("status").GetString() is "completed" or "cancelled").Select(item => item.GetProperty("id").GetInt64()).ToList(),
                    body.GetProperty("historyCursor").GetString());
            }
            var first = await Page("?historyLimit=5");
            Ensure(first.Active.Count == 2 && first.Closed.Count == 5 && first.Cursor is not null, "Página 1: os 2 ativos (inclusive o mais antigo) e 5 encerradas.");
            var second = await Page($"?historyLimit=5&historyBefore={Uri.EscapeDataString(first.Cursor!)}");
            Ensure(second.Active.Count == 0 && second.Closed.Count == 5 && second.Cursor is not null, "Página 2: só encerradas, sem os ativos.");
            var third = await Page($"?historyLimit=5&historyBefore={Uri.EscapeDataString(second.Cursor!)}");
            Ensure(third.Active.Count == 0 && third.Closed.Count == 2 && third.Cursor is null, "Página 3: as 2 que restam e nenhum cursor.");
            var joined = first.Closed.Concat(second.Closed).Concat(third.Closed).ToList();
            Ensure(joined.Distinct().Count() == closedRows && joined.OrderBy(id => id).SequenceEqual(expected.OrderBy(id => id)), "As três páginas juntas têm cada encerrada exatamente uma vez.");
            Ensure(first.Closed.ToHashSet().SetEquals(expected.Take(5)) && second.Closed.ToHashSet().SetEquals(expected.Skip(5).Take(5)) && third.Closed.ToHashSet().SetEquals(expected.Skip(10)), "Cada página traz as mais recentes que ainda não vieram (por data de entrada, depois id).");
            // Padrão de 100 encerradas e limites.
            await f.UnitExec(u2, "INSERT INTO nora_unit.waiting_list(cpf,specialty,entered_at,status,created_by) SELECT $1,'Cardiologia',$2::timestamptz+g*interval '1 minute','cancelled',$3 FROM generate_series(1,100) g", Cpf(20), RoundClock.T0.AddDays(-30), f.AdminId);
            var defaults = await Page("");
            Ensure(defaults.Closed.Count == 100 && defaults.Active.Count == 2 && defaults.Cursor is not null, "Sem historyLimit: 100 encerradas e os ativos.");
            var rest = await Page($"?historyBefore={Uri.EscapeDataString(defaults.Cursor!)}");
            Ensure(rest.Closed.Count == 12 && rest.Cursor is null, "O resto do histórico vem na página seguinte.");
            foreach (var invalid in new[] { "?historyLimit=0", "?historyLimit=501", "?historyLimit=-1", "?historyBefore=nao-e-cursor", "?historyBefore=AAAA" })
            {
                using var response = await f.Admin.GetAsync($"/api/obs/units/{u2}/waiting-list{invalid}");
                Ensure((int)response.StatusCode == 400, $"{invalid}: HTTP {(int)response.StatusCode}.");
            }
            using (var max = await f.Admin.GetAsync($"/api/obs/units/{u2}/waiting-list?historyLimit=500")) Expect(max, 200);
            using (var unitOne = await f.Admin.GetAsync($"/api/obs/units/{u1}/waiting-list?historyLimit=1")) Expect(unitOne, 200);
        });

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
        await suite.Check("P5: a migração de UBS 017 cria os índices por CPF e por data de entrada; a versão esperada é 17 e a lista de um CPF usa o índice", async () =>
        {
            Ensure(UnitSchema.ExpectedVersion == 17, $"ExpectedVersion deveria ser 17, é {UnitSchema.ExpectedVersion}.");
            Ensure(await f.UnitScalar<int>(u1, "SELECT version FROM nora_unit.schema_version") == 17, "O banco da UBS criada pela API está na versão 17.");
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

        await suite.Check("P5: um banco de UBS na versão 16 sobe para a 17 com a migração (índices criados, dados intactos)", async () =>
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
            Ensure(await State() == (17, 2), "Depois da migração: versão 17 e os dois índices.");
            await f.Service<UnitMigrationDao>().MigrateAsync(legacy);
            Ensure(await State() == (17, 2), "Migrar de novo é inofensivo.");
        });

        // ---- Medidas de P4 e P5 (antes/depois de docs/desempenho.md) ----
        await suite.Check("MEDIDA P4 e P5: histórico grande (20.000 entradas e 5.000 vagas encerradas): primeira página contra a lista inteira; leitura por CPF com e sem o índice", async () =>
        {
            await f.UnitExec(u2, "INSERT INTO nora_unit.patient_registry(cpf,full_name,email,created_by) SELECT lpad((80000000000+g)::text,11,'0'),'Paciente '||g::text,'m'||g::text||'@example.test',$1 FROM generate_series(1,2000) g", f.AdminId);
            await f.UnitExec(u2, "INSERT INTO nora_unit.waiting_list(cpf,specialty,entered_at,status,created_by) SELECT lpad((80000000000+(g%2000)+1)::text,11,'0'),'Cardiologia',$1::timestamptz-g*interval '1 minute','cancelled',$2 FROM generate_series(1,20000) g", RoundClock.T0.AddDays(-60), f.AdminId);
            await f.UnitExec(u2, "INSERT INTO nora_unit.lightning_slots(id,appointment_id,status,created_at,created_by,origin,specialty,specialty_id,professional,professional_id,room_id,starts_at,ends_at,closed_reason) " +
                "SELECT gen_random_uuid(),NULL,'closed',$1::timestamptz-g*interval '1 minute',$2,'call-next','Cardiologia',NULL,'Dra. Ana',NULL,NULL,$1::timestamptz+g*interval '1 hour',$1::timestamptz+g*interval '1 hour'+interval '30 minutes','too-late' FROM generate_series(1,5000) g", RoundClock.T0.AddDays(-60), f.AdminId);
            await f.UnitExec(u2, "ANALYZE nora_unit.waiting_list");
            foreach (var (route, label) in new[] { ("waiting-list", "fila"), ("lightning-slots", "vagas") })
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

    private static async Task<List<long>> ClosedIds(RoundFixture f, Guid unit)
    {
        await using var connection = await f.Router.OpenAsync(unit);
        await using var command = new NpgsqlCommand("SELECT id FROM nora_unit.waiting_list WHERE status IN ('completed','cancelled') ORDER BY entered_at DESC,id DESC", connection);
        await using var reader = await command.ExecuteReaderAsync(); var ids = new List<long>();
        while (await reader.ReadAsync()) ids.Add(reader.GetInt64(0));
        return ids;
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
            Ensure(rows.Count == 5 && rows.Single(row => row.GetProperty("cpf").GetString() == Cpf(1)).GetProperty("attendance").GetProperty("points").GetInt32() == 750, "O cadastro e o score voltam completos.");
            Ensure(rows.Count(row => row.GetProperty("isOnline").GetBoolean()) == 0, "Ninguém online sem presença.");
            using var presence = await f.Patients[2].PostAsync("/api/patient/presence", null);
            using var again = await f.Admin.GetAsync($"/api/obs/units/{u1}/registry"); Expect(again, 200);
            Ensure((await Read(again)).EnumerateArray().Single(row => row.GetProperty("cpf").GetString() == Cpf(2)).GetProperty("isOnline").GetBoolean() && (int)presence.StatusCode == 204, "A presença continua refletida.");
            var watch = System.Diagnostics.Stopwatch.StartNew(); for (var i = 0; i < 50; i++) { using var each = await f.Admin.GetAsync($"/api/obs/units/{u1}/registry"); Expect(each, 200); } watch.Stop();
            Console.WriteLine($"MEDIDA P3 registry (5 pacientes, pool central de 1): {watch.Elapsed.TotalMilliseconds / 50:F2} ms por chamada.");
        });
    }

}
