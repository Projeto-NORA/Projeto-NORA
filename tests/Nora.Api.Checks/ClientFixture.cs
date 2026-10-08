using System.Text.Json;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
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
        var schema = "nora_clients_" + Guid.NewGuid().ToString("N");
        var metadata = Path.Combine(root, ".build", "client-fixture.json");
        var stop = Path.Combine(root, ".build", "client-fixture.stop");
        if (File.Exists(stop)) File.Delete(stop);
        await using var app = NoraApiHost.Create(new()
        {
            Args = ["--urls", "http://127.0.0.1:0", "--ConnectionStrings:Nora", connection, "--Nora:Schema", schema, "--AllowedHosts", "localhost;127.0.0.1;10.0.2.2", "--Logging:LogLevel:Default", "Warning"],
            EnvironmentName = "Development",
            ApplicationName = typeof(NoraApiHost).Assembly.GetName().Name,
            ContentRootPath = Path.Combine(root, "Nora.Api")
        });
        var database = app.Services.GetRequiredService<DatabaseHelper>();
        try
        {
            await app.Services.GetRequiredService<MigrationDao>().MigrateAsync();
            var accounts = app.Services.GetRequiredService<AccountService>();
            const string password = "Disposable-test-Account!29";
            var operatorAccount = new CreatePatientRequest("Operador de teste", "52998224725", "operator@example.test", password);
            await accounts.ProvisionAsync("obs", "administrator", operatorAccount);
            await accounts.ProvisionAsync("municipal", "manager", operatorAccount);
            await app.StartAsync();
            var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            // O paciente de teste entra pelo mesmo endpoint OBS consumido pelo desktop.
            using var http = new HttpClient { BaseAddress = new(address) };
            var login = await app.Services.GetRequiredService<AuthService>().LoginAsync(new("obs", operatorAccount.Email, password), default);
            http.DefaultRequestHeaders.Authorization = new("Bearer", login!.Token);
            using var response = await System.Net.Http.Json.HttpClientJsonExtensions.PostAsJsonAsync(http, "/api/patients", new CreatePatientRequest("Paciente de teste Android", "12345678143", "patient1@example.test", password));
            response.EnsureSuccessStatusCode();
            var patient = await System.Net.Http.Json.HttpContentJsonExtensions.ReadFromJsonAsync<PatientInfo>(response.Content);
            var workflow = app.Services.GetRequiredService<WorkflowService>();
            await workflow.AddQueueAsync(new(patient!.Id), default);
            await workflow.AddSlotAsync(new("Vaga de teste Android"), default);
            File.WriteAllText(metadata, JsonSerializer.Serialize(new { address, port = new Uri(address).Port, schema }));
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
}
