using Nora.Api;
using Nora.Api.DAOs;
using Nora.Api.Models;
using Nora.Api.Services;

await using var app=NoraApiHost.Create(new WebApplicationOptions
{
    Args=args.Where(value => value is not ("--migrate" or "--provision" or "--migrate-all-units") && !value.StartsWith("--migrate-unit=", StringComparison.Ordinal)).ToArray(),
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
        try { await migrations.MigrateAsync(unit.Id); await units.MarkDatabaseReadyAsync(unit.Id, null, default); migrated++; }
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
    try { await app.Services.GetRequiredService<HealthUnitDao>().MarkDatabaseReadyAsync(unitId, null, default); }
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
    Console.Write("Nome completo: "); var name=Console.ReadLine()??"";
    Console.Write("CPF: "); var cpf=Console.ReadLine()??"";
    Console.Write("E-mail: "); var email=Console.ReadLine()??"";
    Console.Write("Senha (entrada oculta): "); var password="";
    while(true)
    {
        var key=Console.ReadKey(true);
        if(key.Key==ConsoleKey.Enter) break;
        if(key.Key==ConsoleKey.Backspace) {if(password.Length>0) password=password[..^1];continue;}
        if(!char.IsControl(key.KeyChar)) password+=key.KeyChar;
    }
    Console.WriteLine();
    await app.Services.GetRequiredService<AccountService>().ProvisionAsync(scope,scope=="obs"?"administrator":"manager",new(name,cpf,email,password));
    Console.WriteLine("Conta criada. Pacientes são cadastrados pelo OBS.");
    return;
}
await app.RunAsync();
