using Nora.Api;
using Nora.Api.DAOs;
using Nora.Api.Models;
using Nora.Api.Services;

await using var app=NoraApiHost.Create(new WebApplicationOptions
{
    Args=args.Where(value => value is not ("--migrate" or "--provision")).ToArray(),
    ContentRootPath=AppContext.BaseDirectory
});
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
