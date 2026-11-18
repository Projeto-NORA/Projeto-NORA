using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Nora.Api;

var checks = 0;
var failures = 0;
var directory = new DirectoryInfo(AppContext.BaseDirectory);
while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Nora.Api", "Nora.Api.csproj")))
    directory = directory.Parent;
if (directory == null) throw new InvalidOperationException("Projeto Nora.Api não encontrado a partir do harness.");
if (args.Contains("--client-fixture")) return await ClientFixture.RunAsync(directory.FullName);
// Prepara o banco da demonstração (scripts/demo-local.ps1) com UBS, especialidade e um paciente com conta; ver DemoBootstrap.
if (args.Contains("--demo-bootstrap")) return await DemoBootstrap.RunAsync(directory.FullName);
// Itera só nas regressões da revisão crítica (o conjunto completo continua sendo o padrão).
if (args.Contains("--review-only")) return await ReviewChecks.RunAsync(directory.FullName) == 0 ? 0 : 1;
// Itera só nas verificações de comunicação e Jornada, ou só nas de agenda.
if (args.Contains("--community-only")) return await CommunityChecks.RunAsync(directory.FullName) == 0 ? 0 : 1;
if (args.Contains("--scheduling-only")) return await SchedulingChecks.RunAsync(directory.FullName) == 0 ? 0 : 1;
if (args.Contains("--family-only")) return await FamilyChecks.RunAsync(directory.FullName) == 0 ? 0 : 1;
if (args.Contains("--family-review-only")) return await FamilyChecks.RunReviewAsync(directory.FullName) == 0 ? 0 : 1;
if (args.Contains("--family-decisions-only")) return await FamilyChecks.RunDecisionsAsync(directory.FullName) == 0 ? 0 : 1;
if (args.Contains("--family-gaps-only")) return await FamilyChecks.RunGapsAsync(directory.FullName) == 0 ? 0 : 1;
if (args.Contains("--google-push-only")) return await GooglePushChecks.RunAsync(directory.FullName) == 0 ? 0 : 1;
// Rodada final: computadores do OBS, chamar próximo, atendimento ao público, conflito entre UBS, aprovação das vagas e caixa de saída do e-mail.
if (args.Contains("--devices-only")) return await ObsDeviceChecks.RunAsync(directory.FullName);
if (args.Contains("--call-next-only")) return await CallNextChecks.RunAsync(directory.FullName);
if (args.Contains("--slot-approval-only")) return await SlotApprovalChecks.RunAsync(directory.FullName);
if (args.Contains("--unit-profile-only")) return await UnitProfileChecks.RunAsync(directory.FullName);
if (args.Contains("--cross-unit-only")) return await CrossUnitChecks.RunAsync(directory.FullName);
if (args.Contains("--email-outbox-only")) return await EmailOutboxChecks.RunAsync(directory.FullName);
if (args.Contains("--performance-only")) return await PerformanceChecks.RunAsync(directory.FullName);
if (args.Contains("--workflow-only")) return await WorkflowChecks.RunAsync(directory.FullName) == 0 ? 0 : 1;
if (args.Contains("--queue-return-only")) return await QueueReturnChecks.RunAsync(directory.FullName);
// Fechamento das pendências de 2026-11-17: score em pontos, prioridade da Vaga Relâmpago, equipe sem CPF e P3/P4/P5.
if (args.Contains("--closing-only")) return await ClosingChecks.RunAsync(directory.FullName);

foreach (var environment in new[] { "Development", "Production" })
{
    await using var app = NoraApiHost.Create(new WebApplicationOptions
    {
        Args = ["--urls", "http://127.0.0.1:0"],
        EnvironmentName = environment,
        ApplicationName = typeof(NoraApiHost).Assembly.GetName().Name,
        ContentRootPath = Path.Combine(directory.FullName, "Nora.Api")
    });
    // Somente este processo de teste possui a rota que provoca falha; a API publicada não a mapeia.
    app.MapGet("/__checks/failure", () => ThrowFailure());
    await app.StartAsync();
    try
    {
        var addresses = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()
            ?? throw new InvalidOperationException("Kestrel não informou os endereços.");
        using var client = new HttpClient { BaseAddress = new Uri(addresses.Addresses.Single()), Timeout = TimeSpan.FromSeconds(10) };

        await Check("Cadastro de UBS valida coordenadas e campos", () =>
        {
            var id = Guid.NewGuid();
            var unit = Nora.Api.Services.HealthUnitService.Validate(id, new(" UBS Exemplo ", " Centro ", " Rua 10 ", -23.5, -46.6));
            Require(unit.Name == "UBS Exemplo" && unit.Id == id, "Dados não foram normalizados.");
            foreach (var invalid in new Nora.Api.Models.HealthUnitRequest[] { new("U", "Centro", "Rua", -23, -46), new("UBS", "Centro", "Rua", 86, -46), new("UBS", "Centro", "Rua", double.NaN, -46), new("UBS", "Centro", "Rua", -23, null) })
            {
                try { Nora.Api.Services.HealthUnitService.Validate(id, invalid); throw new InvalidOperationException("Registro inválido aceito."); }
                catch (Nora.Api.Services.ApiInputException) { }
            }
            return Task.CompletedTask;
        });
        await Check("CPF usa normalização e dígitos verificadores sem consulta externa", () =>
        {
            var normalized = Nora.Api.Services.AccountService.NormalizeCpf("529.982.247-25");
            Require(Nora.Api.Services.AccountService.ValidCpf(normalized), "CPF válido não passou.");
            Require(!Nora.Api.Services.AccountService.ValidCpf("52998224724") && !Nora.Api.Services.AccountService.ValidCpf("11111111111") && !Nora.Api.Services.AccountService.ValidCpf("5299822472x"), "CPF inválido passou.");
            Require(Nora.Api.Services.AccountService.NormalizeCpf("529x98224725") == "", "Caractere não permitido foi aceito.");
            return Task.CompletedTask;
        });
        await Check("CancellationWeight cobre cada faixa, cada limite inferior inclusivo e antecedência negativa", () =>
        {
            (TimeSpan Lead, double Weight)[] cases =
            [
                (TimeSpan.FromHours(-5), 1.0), (TimeSpan.FromSeconds(-1), 1.0), (TimeSpan.Zero, 1.0), (TimeSpan.FromMinutes(59), 1.0), (new TimeSpan(0, 59, 59), 1.0),
                (TimeSpan.FromHours(1), 0.9), (new TimeSpan(1, 59, 59), 0.9),
                (TimeSpan.FromHours(2), 0.8), (new TimeSpan(5, 59, 59), 0.8),
                (TimeSpan.FromHours(6), 0.6), (new TimeSpan(11, 59, 59), 0.6),
                (TimeSpan.FromHours(12), 0.4), (new TimeSpan(23, 59, 59), 0.4),
                (TimeSpan.FromHours(24), 0.2), (new TimeSpan(47, 59, 59), 0.2),
                (TimeSpan.FromHours(48), 0.1), (new TimeSpan(71, 59, 59), 0.1),
                (TimeSpan.FromHours(72), 0.02), (new TimeSpan(95, 59, 59), 0.02),
                (TimeSpan.FromHours(96), 0.0), (TimeSpan.FromDays(30), 0.0)
            ];
            foreach (var (lead, weight) in cases)
                Require(Nora.Api.Services.AttendanceScore.CancellationWeight(lead) == weight, $"Peso incorreto para antecedência {lead}.");
            var previous = 2.0;
            for (var minutes = -120; minutes <= 6000; minutes++)
            {
                var current = Nora.Api.Services.AttendanceScore.CancellationWeight(TimeSpan.FromMinutes(minutes));
                Require(current <= previous, "O peso do cancelamento cresceu com a antecedência.");
                previous = current;
            }
            return Task.CompletedTask;
        });
        await Check("Score de comparecimento: denominador zero é nulo; cancelamentos pesam fração de falta", () =>
        {
            Require(Nora.Api.Services.AttendanceScore.Compute(0, 0, 0) is null, "Sem consulta encerrada o score deve ser nulo.");
            Require(Nora.Api.Services.AttendanceScore.Compute(0, 0, 0.5) == 0.0, "Só cancelamentos pesados devem dar score zero, não nulo.");
            Require(Nora.Api.Services.AttendanceScore.Compute(3, 0, 0) == 1.0 && Nora.Api.Services.AttendanceScore.Compute(3, 1, 0) == 0.75, "Score sem cancelamentos incorreto.");
            Require(Math.Abs(Nora.Api.Services.AttendanceScore.Compute(2, 0, 4.02)!.Value - 2 / 6.02) < 1e-12, "Score com peso fracionário incorreto.");
            return Task.CompletedTask;
        });
        await Check("Score em pontos: round(score × 1000) meio para cima, sem histórico = 1000 e faixas nas bordas (299/300, 599/600, 799/800)", () =>
        {
            Require(Nora.Api.Services.AttendanceScore.Points(null) == 1000 && Nora.Api.Services.AttendanceScore.Band(1000) == "excellent", "Sem histórico: 1000 pontos, excelente.");
            Require(Nora.Api.Services.AttendanceScore.Points(0.0) == 0 && Nora.Api.Services.AttendanceScore.Points(1.0) == 1000, "Extremos 0 e 1000.");
            Require(Nora.Api.Services.AttendanceScore.Points(0.2995) == 300 && Nora.Api.Services.AttendanceScore.Points(0.29949) == 299 && Nora.Api.Services.AttendanceScore.Points(0.0005) == 1 && Nora.Api.Services.AttendanceScore.Points(0.00049) == 0, "Arredondamento: meio para cima.");
            Require(Nora.Api.Services.AttendanceScore.Points(Nora.Api.Services.AttendanceScore.Compute(299, 701, 0)) == 299 && Nora.Api.Services.AttendanceScore.Points(Nora.Api.Services.AttendanceScore.Compute(3, 7, 0)) == 300, "Razão exata em 299 e 300.");
            (int Points, string Band)[] cases = [(0, "low"), (299, "low"), (300, "regular"), (599, "regular"), (600, "good"), (799, "good"), (800, "excellent"), (1000, "excellent")];
            foreach (var (points, band) in cases) Require(Nora.Api.Services.AttendanceScore.Band(points) == band, $"{points} pontos deveria ser {band}.");
            var previous = -1;
            for (var points = 0; points <= 1000; points++) { var rank = Array.IndexOf(new[] { "low", "regular", "good", "excellent" }, Nora.Api.Services.AttendanceScore.Band(points)); Require(rank >= previous, "A faixa nunca desce quando os pontos sobem."); previous = rank; }
            return Task.CompletedTask;
        });
        await Check("GET liveness tem escopo explícito, sem afirmação de readiness", async () =>
        {
            using var response = await client.GetAsync("/health/live");
            Require(response.StatusCode == HttpStatusCode.OK, "Liveness não retornou 200.");
            Require(response.Content.Headers.ContentType?.MediaType == "application/json", "Liveness sem JSON.");
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Require(json.RootElement.GetProperty("status").GetString() == "alive" && json.RootElement.GetProperty("scope").GetString() == "process", "Escopo incorreto.");
            Require(json.RootElement.EnumerateObject().Count() == 2, "Liveness anunciou dependências não verificadas.");
            Require(response.Headers.CacheControl?.NoStore == true, "Liveness pode ser cacheado.");
            Require(!response.Headers.Contains("Server"), "Servidor publicou identificação desnecessária.");
        });
        await Check("HEAD liveness retorna 200 sem corpo", async () =>
        {
            using var request = new HttpRequestMessage(HttpMethod.Head, "/health/live");
            using var response = await client.SendAsync(request);
            Require(response.StatusCode == HttpStatusCode.OK && (await response.Content.ReadAsByteArrayAsync()).Length == 0, "HEAD inválido.");
        });
        await Check("GET inexistente retorna ProblemDetails sem ecoar entrada", async () =>
        {
            using var response = await client.GetAsync("/missing-sensitive-path?token=private-query");
            await RequireProblem(response, 404, "not-found");
            var body = await response.Content.ReadAsStringAsync();
            Require(!body.Contains("private-query") && !body.Contains("missing-sensitive-path"), "Erro vazou URL solicitada.");
        });
        await Check("POST inexistente continua 404", async () =>
        {
            using var response = await client.PostAsync("/missing", new StringContent("private-body"));
            await RequireProblem(response, 404, "not-found");
            Require(!(await response.Content.ReadAsStringAsync()).Contains("private-body"), "Erro ecoou payload.");
        });
        await Check("Consulta de CEP rejeita formato inválido antes de acessar serviço externo", async () =>
        {
            using var response = await client.GetAsync("/api/address/cep/12ABC");
            await RequireProblem(response, 400, "invalid-input");
        });
        await Check("POST liveness retorna 405 e preserva Allow", async () =>
        {
            using var response = await client.PostAsync("/health/live", null);
            await RequireProblem(response, 405, "method-not-allowed");
            Require(response.Content.Headers.Allow.ToHashSet().SetEquals(["GET", "HEAD"]), "Allow não corresponde aos métodos suportados.");
        });
        await Check("OPTIONS não concede CORS sem política definida", async () =>
        {
            using var request = new HttpRequestMessage(HttpMethod.Options, "/health/live");
            request.Headers.Add("Origin", "https://example.org");
            request.Headers.Add("Access-Control-Request-Method", "POST");
            using var response = await client.SendAsync(request);
            await RequireProblem(response, 405, "method-not-allowed");
            Require(!response.Headers.Contains("Access-Control-Allow-Origin"), "CORS aberto indevidamente.");
        });
        await Check("Contrato de erro JSON é preservado com Accept text/html", async () =>
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/missing");
            request.Headers.Accept.ParseAdd("text/html");
            using var response = await client.SendAsync(request);
            await RequireProblem(response, 404, "not-found");
        });
        await Check("Exceção retorna 500 seguro inclusive em Development", async () =>
        {
            using var response = await client.GetAsync("/__checks/failure");
            await RequireProblem(response, 500, "internal-error");
            var body = await response.Content.ReadAsStringAsync();
            Require(!body.Contains("private-exception") && !body.Contains("InvalidOperationException") && !body.Contains("stack"), "Diagnóstico interno exposto.");
        });
        await Check("Entrar com Google sem configuração responde 503 google-disabled antes de olhar o token ou o banco", async () =>
        {
            using var response = await client.PostAsync("/api/auth/google", new StringContent("{\"idToken\":\"a.b.c\"}", System.Text.Encoding.UTF8, "application/json"));
            await RequireProblem(response, 503, "google-disabled");
        });
        await Check("Readiness informa banco ausente e rotas clínicas exigem sessão", async () =>
        {
            using (var readiness = await client.GetAsync("/health/ready"))
            {
                Require((int)readiness.StatusCode == 503, "Readiness anunciou banco não configurado.");
            }
            using (var protectedRoute = await client.GetAsync("/api/patients"))
            {
                Require((int)protectedRoute.StatusCode == 401, "Pacientes não exigem autenticação.");
            }
            using (var patientUnits = await client.GetAsync("/api/patient/units"))
            {
                Require((int)patientUnits.StatusCode == 401, "Unidades do paciente não exigem autenticação.");
            }
            foreach (var route in new[] { "/api/obs/units", "/api/municipal/staff" })
            {
                using var response = await client.GetAsync(route);
                Require((int)response.StatusCode == 401, "Vínculos de unidades não exigem autenticação.");
            }
            using (var wrongMethod = await client.GetAsync("/api/auth/login"))
            {
                await RequireProblem(wrongMethod, 405, "method-not-allowed");
            }
        });
        await Check("32 solicitações concorrentes preservam respostas e traceIds", async () =>
        {
            var traceIds = await Task.WhenAll(Enumerable.Range(0, 32).Select(async index =>
            {
                using var response = await client.GetAsync(index % 2 == 0 ? "/health/live" : "/missing");
                if (index % 2 == 0)
                {
                    Require(response.StatusCode == HttpStatusCode.OK, "Liveness concorrente falhou.");
                    return null;
                }
                await RequireProblem(response, 404, "not-found");
                using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                return json.RootElement.GetProperty("traceId").GetString();
            }));
            Require(traceIds.Where(id => id != null).Distinct().Count() == 16, "traceId compartilhado entre solicitações.");
        });
    }
    finally { await app.StopAsync(); }

    async Task Check(string name, Func<Task> action)
    {
        checks++;
        try { await action(); Console.WriteLine($"PASS [{environment}] {name}"); }
        catch (Exception error) { failures++; Console.WriteLine($"FAIL [{environment}] {name}: {error.Message}"); }
    }
}

Console.WriteLine($"{checks - failures}/{checks} verificações HTTP passaram.");
var workflowFailures = await WorkflowChecks.RunAsync(directory.FullName);
var unitFailures = await UnitDatabaseChecks.RunAsync();
var platformFailures = await PlatformChecks.RunAsync(directory.FullName);
var reviewFailures = await ReviewChecks.RunAsync(directory.FullName);
var communityFailures = await CommunityChecks.RunAsync(directory.FullName);
var schedulingFailures = await SchedulingChecks.RunAsync(directory.FullName);
var familyFailures = await FamilyChecks.RunAsync(directory.FullName);
var familyReviewFailures = await FamilyChecks.RunReviewAsync(directory.FullName);
var familyDecisionFailures = await FamilyChecks.RunDecisionsAsync(directory.FullName);
var familyGapFailures = await FamilyChecks.RunGapsAsync(directory.FullName);
var googlePushFailures = await GooglePushChecks.RunAsync(directory.FullName);
var deviceFailures = await ObsDeviceChecks.RunAsync(directory.FullName);
var callNextFailures = await CallNextChecks.RunAsync(directory.FullName);
var profileFailures = await UnitProfileChecks.RunAsync(directory.FullName);
var crossUnitFailures = await CrossUnitChecks.RunAsync(directory.FullName);
var approvalFailures = await SlotApprovalChecks.RunAsync(directory.FullName);
var outboxFailures = await EmailOutboxChecks.RunAsync(directory.FullName);
var queueReturnFailures = await QueueReturnChecks.RunAsync(directory.FullName);
var performanceFailures = await PerformanceChecks.RunAsync(directory.FullName);
var closingFailures = await ClosingChecks.RunAsync(directory.FullName);
return closingFailures + queueReturnFailures + performanceFailures + deviceFailures + callNextFailures + profileFailures + crossUnitFailures + approvalFailures + outboxFailures == 0 && googlePushFailures == 0 && failures == 0 && workflowFailures == 0 && unitFailures == 0 && platformFailures == 0 && reviewFailures == 0 && communityFailures == 0 && schedulingFailures == 0 && familyFailures == 0 && familyReviewFailures == 0 && familyDecisionFailures == 0 && familyGapFailures == 0 ? 0 : 1;

static string ThrowFailure() => throw new InvalidOperationException("private-exception");

static async Task RequireProblem(HttpResponseMessage response, int status, string code)
{
    Require((int)response.StatusCode == status, $"Esperado {status}, recebido {(int)response.StatusCode}.");
    Require(response.Content.Headers.ContentType?.MediaType == "application/problem+json", "Erro sem application/problem+json.");
    Require(response.Headers.CacheControl?.NoStore == true, "Erro pode ser cacheado.");
    using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    var root = json.RootElement;
    Require(root.GetProperty("status").GetInt32() == status, "status JSON diverge de HTTP.");
    Require(root.GetProperty("code").GetString() == code && root.GetProperty("type").GetString() == $"urn:nora:problem:{code}", "Código/type de erro incoerente.");
    Require(!string.IsNullOrWhiteSpace(root.GetProperty("traceId").GetString()), "traceId ausente.");
    Require(!string.IsNullOrWhiteSpace(root.GetProperty("title").GetString()) && !string.IsNullOrWhiteSpace(root.GetProperty("detail").GetString()), "Erro sem descrição.");
}

static void Require(bool valid, string message)
{
    if (!valid) throw new InvalidOperationException(message);
}
