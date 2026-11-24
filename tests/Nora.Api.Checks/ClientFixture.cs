using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using Nora.Api;
using Nora.Api.DAOs;
using Nora.Api.Database;
using Nora.Api.Models;
using Nora.Api.Services;

internal static class ClientFixture
{
    public static async Task<int> RunAsync(string root)
    {
        var connection = Environment.GetEnvironmentVariable("NORA_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) throw new InvalidOperationException("Fixture exige NORA_TEST_CONNECTION descartável.");
        var registrationTest = Environment.GetEnvironmentVariable("NORA_TEST_REGISTRATION") == "1";
        var registrationConnections = new[] { "A", "B", "C" }.Select(letter => Environment.GetEnvironmentVariable($"NORA_TEST_UNIT_{letter}_CONNECTION")).ToArray();
        if (registrationTest)
        {
            foreach (var value in registrationConnections.Prepend(connection))
            {
                if (string.IsNullOrWhiteSpace(value)) throw new InvalidOperationException("A fixture de cadastro exige bancos locais descartáveis.");
                var parts = new NpgsqlConnectionStringBuilder(value);
                if (parts.Host != "127.0.0.1" || (value != connection && !parts.Database!.StartsWith("nora_unit", StringComparison.Ordinal)))
                    throw new InvalidOperationException("A fixture de cadastro aceita somente bancos de teste locais.");
            }
        }
        var testMail = new FixtureRegistrationMail();
        var schema = "nora_clients_" + Guid.NewGuid().ToString("N");
        var metadata = Path.Combine(root, ".build", "client-fixture.json");
        var stop = Path.Combine(root, ".build", "client-fixture.stop");
        if (File.Exists(stop)) File.Delete(stop);
        await using var app = NoraApiHost.Create(new()
        {
            Args = ["--urls", "http://127.0.0.1:0", "--ConnectionStrings:Nora", connection, "--Nora:Schema", schema, "--Nora:LegacyGlobalWorkflow:Enabled", registrationTest ? "false" : "true", "--AllowedHosts", "localhost;127.0.0.1;10.0.2.2", "--Logging:LogLevel:Default", "Warning",
                // O E2E dos clientes cobre os computadores do OBS, guardados e desligados no padrão do servidor (24/11/2026); aqui o recurso é ligado de propósito.
                "--Nora:ObsDevices:Enabled", "true"],
            EnvironmentName = "Development",
            ApplicationName = typeof(NoraApiHost).Assembly.GetName().Name,
            ContentRootPath = Path.Combine(root, "Nora.Api")
        }, services => { if (registrationTest) services.Replace(ServiceDescriptor.Singleton<IEmailSender>(testMail)); });
        if (registrationTest)
        {
            app.Configuration["Nora:Registration:HashKey"] = "synthetic-fixture-registration-key-32-bytes";
            app.MapGet("/__checks/registration-code", () => Results.Ok(new { code = testMail.Code }));
        }
        var database = app.Services.GetRequiredService<DatabaseHelper>();
        try
        {
            await app.Services.GetRequiredService<MigrationDao>().MigrateAsync();
            var accounts = app.Services.GetRequiredService<AccountService>();
            const string password = "Disposable-test-Account!29";
            // A equipe (OBS e Secretaria) não tem CPF: entra só por e-mail (P-053).
            var operatorUser = await accounts.ProvisionStaffAsync("obs", "administrator", "Operador de teste", "operator@example.test", password);
            var managerUser = await accounts.ProvisionStaffAsync("municipal", "manager", "Operador de teste", "operator@example.test", password);
            if (registrationTest)
            {
                // Atendente sintética do E2E de computadores do OBS: só entra em computador ativado (a exigência fica ligada) e tem concessão nas três UBS.
                var attendantUser = await accounts.ProvisionStaffAsync("obs", "attendant", "Atendente de teste", "attendant@example.test", password);
                var units = app.Services.GetRequiredService<HealthUnitService>();
                var unitRecords = app.Services.GetRequiredService<HealthUnitDao>();
                var registry = app.Services.GetRequiredService<UnitPatientRegistryDao>();
                for (var i = 0; i < 3; i++)
                {
                    // Cadastro direto no DAO: o banco desta UBS vem da chave explícita abaixo, sem criar um banco derivado.
                    var unit = await unitRecords.CreateAsync(HealthUnitService.Validate(Guid.NewGuid(), new($"UBS Cadastro E2E {i + 1}", "Centro", "Rua sintética", -23.5, -46.6)), default);
                    app.Configuration[$"Nora:UnitDatabases:{unit.Id:N}"] = registrationConnections[i];
                    await app.Services.GetRequiredService<UnitMigrationDao>().MigrateAsync(unit.Id);
                    await units.SetStaffAccessAsync(operatorUser.Id, unit.Id, managerUser.Id, true, default);
                    await units.SetStaffAccessAsync(attendantUser.Id, unit.Id, managerUser.Id, true, default);
                    if (i < 2) await registry.CreateAsync(unit.Id, operatorUser.Id, new("52998224725", "Paciente Sintético", i == 0 ? "first@example.test" : "second@example.test"));
                    if (i == 0)
                    {
                        var starts = DateTimeOffset.UtcNow.AddDays(3);
                        await app.Services.GetRequiredService<AppointmentDao>().CreateAsync(unit.Id, operatorUser.Id, new("52998224725", "Clínica de teste", "Profissional de teste Android", starts, starts.AddMinutes(30)), default);
                        await app.Services.GetRequiredService<UnitWaitingListDao>().AddAsync(unit.Id, operatorUser.Id, new("52998224725", "Cardiologia"), default);
                        await registry.CreateAsync(unit.Id, operatorUser.Id, new("12345678143", "Paciente de origem", "source@example.test"));
                        var slotStart = starts.AddDays(2);
                        var cancelled = await app.Services.GetRequiredService<AppointmentDao>().CreateAsync(unit.Id, operatorUser.Id, new("12345678143", "Cardiologia", "Profissional da vaga Android", slotStart, slotStart.AddMinutes(30)), default);
                        await app.Services.GetRequiredService<AppointmentDao>().ChangeStatusAsync(unit.Id, operatorUser.Id, cancelled.Id, new("cancelled", true), default);
                        await app.Services.GetRequiredService<UnitLightningDao>().CreateAsync(unit.Id, operatorUser.Id, new(cancelled.Id, true), default);
                    }
                }
            }
            await app.StartAsync();
            var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            if (!registrationTest)
            {
                // Esta fixture histórica habilita rotas globais somente no ambiente de teste.
                using var http = new HttpClient { BaseAddress = new(address) };
                var login = await app.Services.GetRequiredService<AuthService>().LoginAsync(new("obs", "operator@example.test", password), default);
                http.DefaultRequestHeaders.Authorization = new("Bearer", login!.Token);
                using var response = await System.Net.Http.Json.HttpClientJsonExtensions.PostAsJsonAsync(http, "/api/patients", new CreatePatientRequest("Paciente de teste Android", "12345678143", "patient1@example.test", password));
                response.EnsureSuccessStatusCode();
                var patient = await System.Net.Http.Json.HttpContentJsonExtensions.ReadFromJsonAsync<PatientInfo>(response.Content);
                var workflow = app.Services.GetRequiredService<WorkflowService>();
                await workflow.AddQueueAsync(new(patient!.Id), default);
                await workflow.AddSlotAsync(new("Vaga de teste Android"), default);
            }
            File.WriteAllText(metadata, JsonSerializer.Serialize(new { address, port = new Uri(address).Port, schema, identifier = "operator@example.test", attendantIdentifier = "attendant@example.test" }));
            Console.WriteLine("Fixture PostgreSQL/API pronta; dados sintéticos, metadata em .build/client-fixture.json.");
            while (!File.Exists(stop)) await Task.Delay(500);
            return 0;
        }
        finally
        {
            await app.StopAsync();
            await using var cleanup = await database.OpenAsync();
            await database.ExecuteAsync(cleanup, $"DROP SCHEMA IF EXISTS {schema} CASCADE", default);
            if (File.Exists(metadata)) File.Delete(metadata);
        }
    }
    private sealed class FixtureRegistrationMail : IEmailSender
    {
        public bool IsConfigured => true;
        public string? Code { get; private set; }
        public Task SendAsync(string address, string subject, string body, CancellationToken ct)
        {
            Code = System.Text.RegularExpressions.Regex.Match(body, @"\b[0-9]{6}\b").Value;
            return Task.CompletedTask;
        }
    }
}
