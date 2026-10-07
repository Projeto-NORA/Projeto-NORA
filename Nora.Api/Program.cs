using Nora.Api;

await using var app = NoraApiHost.Create(new WebApplicationOptions { Args = args });
await app.RunAsync();
