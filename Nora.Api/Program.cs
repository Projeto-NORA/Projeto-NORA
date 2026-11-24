using Nora.Api;
using Nora.Api.DAOs;
using Nora.Api.Models;
using Nora.Api.Services;

await using var app=NoraApiHost.Create(new WebApplicationOptions
{
    Args=args.Where(value => value is not ("--migrate" or "--provision" or "--reset-password" or "--migrate-all-units") && !value.StartsWith("--migrate-unit=", StringComparison.Ordinal)).ToArray(),
    ContentRootPath=AppContext.BaseDirectory
});
var unitMigrations = args.Where(value => value.StartsWith("--migrate-unit=", StringComparison.Ordinal)).ToArray();
var migrateAllUnits = args.Contains("--migrate-all-units", StringComparer.Ordinal);
if (migrateAllUnits)
{
    if (unitMigrations.Length > 0 || args.Contains("--migrate", StringComparer.Ordinal) || args.Contains("--provision", StringComparer.Ordinal))
        throw new InvalidOperationException("--migrate-all-units não combina com outros comandos de migração.");
    // Todas as unidades "ready" (chave explícita ou database_ready_at); as "pending" nunca tiveram banco e ficam de fora.
    var migrations = app.Services.GetRequiredService<UnitMigrationDao>(); var units = app.Services.GetRequiredService<HealthUnitDao>();
    int migrated = 0, failed = 0;
    foreach (var unit in (await units.ListAsync(default)).Where(unit => unit.DatabaseState == "ready"))
    {
        try { await migrations.MigrateAsync(unit.Id); await units.MarkDatabaseReadyAsync(unit.Id, null, default, recordEvent: false); migrated++; }
        catch (Exception) { failed++; Console.Error.WriteLine($"Falha ao migrar a UBS {unit.Id}; confira a conexão e as permissões."); }
    }
    Console.WriteLine($"Bancos de UBS atualizados: {migrated}; falhas: {failed}.");
    Environment.ExitCode = failed == 0 ? 0 : 1;
    return;
}
if (unitMigrations.Length > 0)
{
    if (unitMigrations.Length != 1 || args.Contains("--migrate", StringComparer.Ordinal) || args.Contains("--provision", StringComparer.Ordinal) ||
        !Guid.TryParse(unitMigrations[0]["--migrate-unit=".Length..], out var unitId) || unitId == Guid.Empty)
        throw new InvalidOperationException("Informe uma única UBS válida para a migração.");
    await app.Services.GetRequiredService<UnitMigrationDao>().MigrateAsync(unitId);
    Console.WriteLine("Banco da UBS atualizado.");
    try { await app.Services.GetRequiredService<HealthUnitDao>().MarkDatabaseReadyAsync(unitId, null, default, recordEvent: false); }
    catch (ApiNotFoundException) { Console.Error.WriteLine("A UBS não está cadastrada no banco central; o banco foi migrado, mas o estado não foi marcado."); Environment.ExitCode = 1; }
    return;
}
if(args.Contains("--migrate",StringComparer.Ordinal))
{
    await app.Services.GetRequiredService<MigrationDao>().MigrateAsync();
    Console.WriteLine("Schema NORA atualizado.");
    return;
}
if(args.Contains("--provision",StringComparer.Ordinal))
{
    if(Console.IsInputRedirected) throw new InvalidOperationException("Provisionamento exige terminal interativo privado.");
    Console.Write("Escopo (obs/municipal): "); var scope=Console.ReadLine()??"";
    if(scope is not("obs" or "municipal")) throw new ApiInputException();
    var municipalityName = "";
    if (scope == "municipal") { Console.Write("Nome do município: "); municipalityName = Console.ReadLine() ?? ""; }
    Console.Write(scope == "municipal" ? "Nome completo do funcionário: " : "Nome completo: "); var name=Console.ReadLine()??"";
    Console.Write(scope == "municipal" ? "E-mail do funcionário: " : "E-mail: "); var email=Console.ReadLine()??"";
    var password = ReadHidden(scope == "municipal" ? "Senha do funcionário (entrada oculta): " : "Senha (entrada oculta): ");
    var accounts = app.Services.GetRequiredService<AccountService>();
    if (scope == "municipal") await accounts.ProvisionMunicipalAsync(municipalityName, name, email, password);
    else await accounts.ProvisionStaffAsync(scope, "administrator", name, email, password);
    Console.WriteLine("Conta criada. Pacientes são cadastrados pelo OBS.");
    return;
}
if (args.Contains("--reset-password", StringComparer.Ordinal))
{
    // Para a equipe técnica quando alguém perde o acesso: a senha fica só como hash no banco e não dá para lê-la, só para trocá-la.
    if (Console.IsInputRedirected) throw new InvalidOperationException("A redefinição de senha exige terminal interativo privado.");
    Console.Write("Escopo da conta (municipal/obs): "); var resetScope = Console.ReadLine() ?? "";
    Console.Write("E-mail da conta: "); var resetEmail = Console.ReadLine() ?? "";
    var first = ReadHidden("Senha nova provisória (entrada oculta, mínimo de 8 caracteres): ");
    var second = ReadHidden("Repita a senha nova: ");
    if (first != second) { Console.Error.WriteLine("As senhas não conferem. Nada foi alterado."); Environment.ExitCode = 1; return; }
    try { await app.Services.GetRequiredService<AccountService>().ResetStaffPasswordAsync(resetScope, resetEmail, first); }
    catch (ApiNotFoundException) { Console.Error.WriteLine("Conta não encontrada, inativa ou de outro escopo. Nada foi alterado."); Environment.ExitCode = 1; return; }
    catch (ApiInputException) { Console.Error.WriteLine("Dados inválidos: informe municipal ou obs, um e-mail válido e uma senha de 8 a 256 caracteres. Nada foi alterado."); Environment.ExitCode = 1; return; }
    Console.WriteLine("Senha redefinida. As sessões abertas da conta foram encerradas e a troca da senha será exigida no próximo login.");
    return;
}
await app.RunAsync();

static string ReadHidden(string prompt)
{
    Console.Write(prompt);
    var value = "";
    while (true)
    {
        var key = Console.ReadKey(true);
        if (key.Key == ConsoleKey.Enter) break;
        if (key.Key == ConsoleKey.Backspace) { if (value.Length > 0) value = value[..^1]; continue; }
        if (!char.IsControl(key.KeyChar)) value += key.KeyChar;
    }
    Console.WriteLine();
    return value;
}
