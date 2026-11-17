using System.Net.Http.Json;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using Nora.Api;
using Nora.Api.DAOs;
using Nora.Api.Models;
using Nora.Api.Services;

/// <summary>
/// Prepara o banco da DEMONSTRAÇÃO (<c>scripts/demo-local.ps1</c>) com o mínimo para o app do paciente funcionar: a UBS, a especialidade, contas de demonstração (Secretaria, administrador da UBS e um
/// paciente com conta) e o vínculo do paciente à UBS. Usa o host da API em processo contra o mesmo banco (as mesmas regras e rotas da API de verdade) e não envia e-mail nenhum.
/// Idempotente: rodar de novo reaproveita o que existe. Só aceita banco em 127.0.0.1. As senhas são geradas e gravadas em <c>acesso-demo.txt</c> (fora do Git); nada é impresso.
/// Uso: <c>NORA_DEMO_CONNECTION=... dotnet Nora.Api.Checks.dll --demo-bootstrap</c>.
/// </summary>
internal static class DemoBootstrap
{
    private const string UnitName = "UBS Escola Centro", SpecialtyName = "Clínica geral";
    private const string ManagerEmail = "secretaria.demo@example.test", AdminEmail = "admin.demo@example.test", PatientEmail = "paciente.demo@example.test";
    // CPFs fictícios que passam na verificação dos dígitos (exemplos públicos de teste).
    private const string ManagerCpf = "39053344705", AdminCpf = "16899535009", PatientCpf = "12345678909";
    private const string PasswordLabel = "Senha (todas as contas de demonstração): ";

    public static async Task<int> RunAsync(string root)
    {
        var connection = Environment.GetEnvironmentVariable("NORA_DEMO_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) throw new InvalidOperationException("Informe NORA_DEMO_CONNECTION (banco da demonstração).");
        if (new NpgsqlConnectionStringBuilder(connection).Host != "127.0.0.1") throw new InvalidOperationException("O preparo da demonstração só aceita banco em 127.0.0.1.");
        var folder = Environment.GetEnvironmentVariable("NORA_DEMO_FOLDER") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NORA", "demo");
        Directory.CreateDirectory(folder);
        var accessFile = Path.Combine(folder, "acesso-demo.txt");
        var password = ReadPassword(accessFile) ?? NewPassword();

        await using var app = NoraApiHost.Create(new()
        {
            Args = ["--urls", "http://127.0.0.1:0", "--ConnectionStrings:Nora", connection, "--Nora:UnitLightningWorker:Enabled", "false", "--Nora:Maintenance:Enabled", "false", "--Nora:Push:Enabled", "false",
                "--Nora:Registration:HashKey", "demo-bootstrap-registration-key-0123456789", "--AllowedHosts", "localhost;127.0.0.1", "--Logging:LogLevel:Default", "Warning"],
            EnvironmentName = "Production", ApplicationName = typeof(NoraApiHost).Assembly.GetName().Name, ContentRootPath = Path.Combine(root, "Nora.Api")
        }, services => services.Replace(ServiceDescriptor.Singleton<IEmailSender>(new RoundMail())));

        await app.Services.GetRequiredService<MigrationDao>().MigrateAsync();
        var accounts = app.Services.GetRequiredService<AccountService>();

        async Task<Guid> EnsureAccount(string scope, string role, string name, string cpf, string email)
        {
            if (await FindAsync(connection, "SELECT id FROM nora.accounts WHERE scope=$1 AND email=$2", scope, email) is { } id) return id;
            return (await accounts.ProvisionAsync(scope, role, new(name, cpf, email, password))).Id;
        }
        var managerId = await EnsureAccount("municipal", "manager", "Secretaria (demonstração)", ManagerCpf, ManagerEmail);
        var adminId = await EnsureAccount("obs", "administrator", "Administrador da UBS (demonstração)", AdminCpf, AdminEmail);
        var patientId = await EnsureAccount("patient", "patient", "Maria Souza (demonstração)", PatientCpf, PatientEmail);
        // Grava a senha já aqui: se algo falhar adiante, a próxima execução reaproveita as mesmas contas.
        File.WriteAllText(accessFile, AccessText(password));

        await app.StartAsync();
        var manager = RoundFixture.NewClient(app); var admin = RoundFixture.NewClient(app); var patient = RoundFixture.NewClient(app);
        async Task Login(HttpClient client, string scope, string identifier)
        {
            using var response = await client.PostAsJsonAsync("/api/auth/login", new { scope, identifier, password });
            RoundFixture.Expect(response, 200);
            client.DefaultRequestHeaders.Authorization = new("Bearer", (await RoundFixture.Read(response)).GetProperty("token").GetString()!);
        }
        await Login(manager, "municipal", ManagerEmail);

        using (var specialty = await manager.PostAsJsonAsync("/api/municipal/specialties", new { name = SpecialtyName }))
            RoundFixture.Ensure((int)specialty.StatusCode is 201 or 409, $"Especialidade: HTTP {(int)specialty.StatusCode}.");
        var unitId = await FindAsync(connection, "SELECT id FROM nora.health_units WHERE name=$1", UnitName);
        if (unitId is null)
        {
            using var created = await manager.PostAsJsonAsync("/api/municipal/units", new { name = UnitName, region = "Centro", address = "Rua da Escola, 100", latitude = -23.55, longitude = -46.63 });
            RoundFixture.Expect(created, 201);
            unitId = (await RoundFixture.Read(created)).GetProperty("id").GetGuid();
        }
        // O administrador de demonstração e todo administrador OBS que já existe (o do usuário) passam a ver a UBS.
        var admins = new List<Guid> { adminId };
        await using (var db = new NpgsqlConnection(connection))
        {
            await db.OpenAsync();
            await using var query = new NpgsqlCommand("SELECT id FROM nora.accounts WHERE scope='obs' AND role='administrator' AND is_active", db);
            await using var reader = await query.ExecuteReaderAsync();
            while (await reader.ReadAsync()) if (!admins.Contains(reader.GetGuid(0))) admins.Add(reader.GetGuid(0));
        }
        foreach (var account in admins)
        {
            using var grant = await manager.PutAsync($"/api/municipal/staff/{account}/units/{unitId}", null);
            RoundFixture.Expect(grant, 204);
        }

        await Login(admin, "obs", AdminEmail);
        using (var registry = await admin.PostAsJsonAsync($"/api/obs/units/{unitId}/registry", new { cpf = PatientCpf, fullName = "Maria Souza (demonstração)", email = PatientEmail }))
            RoundFixture.Ensure((int)registry.StatusCode is 201 or 409, $"Cadastro de referência: HTTP {(int)registry.StatusCode}.");

        await Login(patient, "patient", PatientCpf);
        using (var link = await patient.PostAsJsonAsync("/api/patient/units/link", new { unitIds = new[] { unitId } })) RoundFixture.Expect(link, 200);

        Console.WriteLine($"Demonstração preparada: UBS '{UnitName}', especialidade '{SpecialtyName}', {admins.Count} administrador(es) com acesso à UBS, paciente com conta e vínculo. Acessos em {accessFile}.");
        await app.StopAsync();
        return 0;
    }

    private static async Task<Guid?> FindAsync(string connection, string sql, params object[] values)
    {
        await using var db = new NpgsqlConnection(connection);
        await db.OpenAsync();
        await using var command = new NpgsqlCommand(sql, db);
        foreach (var value in values) command.Parameters.AddWithValue(value);
        return await command.ExecuteScalarAsync() is Guid id ? id : null;
    }

    private static string? ReadPassword(string file) => File.Exists(file)
        ? File.ReadLines(file).FirstOrDefault(line => line.StartsWith(PasswordLabel, StringComparison.Ordinal))?[PasswordLabel.Length..].Trim() : null;

    /// <summary>Senha de teste aleatória (letras, números e símbolos), só para as contas de demonstração.</summary>
    private static string NewPassword()
    {
        const string letters = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789";
        var chars = new char[14];
        for (var i = 0; i < chars.Length; i++) chars[i] = letters[RandomNumberGenerator.GetInt32(letters.Length)];
        return "Demo-" + new string(chars) + "!7";
    }

    private static string AccessText(string password) => string.Join(Environment.NewLine,
        "Acessos de DEMONSTRAÇÃO do NORA (contas de teste, só neste computador; não vão para o Git).",
        "",
        PasswordLabel + password,
        "",
        $"Painel Municipal (Secretaria):  e-mail {ManagerEmail}",
        $"OBS (administrador da UBS):     e-mail {AdminEmail}",
        $"App do paciente (Maria Souza):  CPF {PatientCpf}  (ou e-mail {PatientEmail})",
        "",
        "App no emulador: endereço http://10.0.2.2:5080 (permita o acesso à rede local quando o Android pedir).",
        $"UBS cadastrada: {UnitName} · especialidade: {SpecialtyName}.",
        "As contas que você criou pelo --provision continuam funcionando; o administrador OBS que você criou também foi liberado para esta UBS.") + Environment.NewLine;
}
