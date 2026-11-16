using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Nora.Api.Services;

/// <summary>Conta de serviço do Google lida da configuração (só o necessário para assinar a asserção OAuth2). Nunca é registrada em log.</summary>
internal sealed record ServiceAccountCredential(string ClientEmail, string PrivateKeyPem, string? PrivateKeyId, Uri TokenUri);

/// <summary>
/// Configuração do push pelo servidor (P-042, FCM HTTP v1). Fica DESLIGADO enquanto não houver projeto e credencial: nada é enfileirado, nada é enviado e nada falha.
/// <list type="bullet">
/// <item><c>Nora:Push:ProjectId</c>: projeto do Firebase (na falta, o <c>project_id</c> da credencial).</item>
/// <item><c>Nora:Push:ServiceAccountJson</c> (o JSON da conta de serviço) ou <c>Nora:Push:ServiceAccountPath</c> (caminho do arquivo); o JSON tem precedência.</item>
/// <item><c>Nora:Push:Enabled</c> (padrão verdadeiro) liga o worker; <c>IntervalSeconds</c> (30; 5 a 3600), <c>MaxAttempts</c> (5; 1 a 20), <c>LookbackHours</c> (24; 1 a 168) e
/// <c>PresenceScanMinutes</c> (5; 0 a 1440, 0 = a cada passe).</item>
/// </list>
/// Configuração incompleta (projeto sem credencial, credencial sem projeto, JSON ou chave inválidos, arquivo inexistente) ou número fora da faixa impede a inicialização.
/// </summary>
public sealed partial class PushOptions
{
    public const string DefaultTokenUri = "https://oauth2.googleapis.com/token";

    public bool Configured => Credential is not null;
    public string? ProjectId { get; }
    internal ServiceAccountCredential? Credential { get; }
    public bool WorkerEnabled { get; }
    public TimeSpan Interval { get; }
    public int MaxAttempts { get; }
    public TimeSpan Lookback { get; }
    public TimeSpan PresenceScanEvery { get; }

    public PushOptions(IConfiguration configuration)
    {
        WorkerEnabled = configuration.GetValue("Nora:Push:Enabled", true);
        Interval = TimeSpan.FromSeconds(Number(configuration, "IntervalSeconds", 30, 5, 3600));
        MaxAttempts = Number(configuration, "MaxAttempts", 5, 1, 20);
        Lookback = TimeSpan.FromHours(Number(configuration, "LookbackHours", 24, 1, 168));
        PresenceScanEvery = TimeSpan.FromMinutes(Number(configuration, "PresenceScanMinutes", 5, 0, 1440));

        var json = configuration["Nora:Push:ServiceAccountJson"];
        var path = configuration["Nora:Push:ServiceAccountPath"];
        if (string.IsNullOrWhiteSpace(json) && !string.IsNullOrWhiteSpace(path))
        {
            if (!File.Exists(path)) throw new InvalidOperationException("Nora:Push:ServiceAccountPath não aponta para um arquivo existente.");
            json = File.ReadAllText(path);
        }
        var project = configuration["Nora:Push:ProjectId"];
        if (string.IsNullOrWhiteSpace(json))
        {
            if (!string.IsNullOrWhiteSpace(project)) throw new InvalidOperationException("Nora:Push:ProjectId sem credencial: informe Nora:Push:ServiceAccountJson ou Nora:Push:ServiceAccountPath.");
            return;
        }
        (Credential, var jsonProject) = ParseCredential(json);
        ProjectId = string.IsNullOrWhiteSpace(project) ? jsonProject : project.Trim();
        if (ProjectId is null || !ProjectPattern().IsMatch(ProjectId)) throw new InvalidOperationException("Nora:Push:ProjectId ausente ou inválido.");
    }

    private static (ServiceAccountCredential, string?) ParseCredential(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            string? Text(string name) => root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
            var email = Text("client_email"); var key = Text("private_key");
            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(key)) throw new InvalidOperationException();
            using (var rsa = RSA.Create()) rsa.ImportFromPem(key); // confere a chave na subida, sem guardá-la em log
            var tokenUri = Text("token_uri") is { Length: > 0 } uri ? new Uri(uri, UriKind.Absolute) : new Uri(DefaultTokenUri);
            if (tokenUri.Scheme != Uri.UriSchemeHttps) throw new InvalidOperationException();
            return (new(email, key, Text("private_key_id"), tokenUri), Text("project_id"));
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or ArgumentException or CryptographicException or UriFormatException)
        {
            throw new InvalidOperationException("Credencial do push (conta de serviço do Google) inválida: confira o JSON baixado do console do Firebase.");
        }
    }

    private static int Number(IConfiguration configuration, string name, int fallback, int min, int max)
    {
        var value = configuration[$"Nora:Push:{name}"];
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) && parsed >= min && parsed <= max
            ? parsed : throw new InvalidOperationException($"Nora:Push:{name} inválido: informe um inteiro de {min} a {max}.");
    }

    [GeneratedRegex("^[a-z][a-z0-9-]{4,61}[a-z0-9]$")]
    private static partial Regex ProjectPattern();
}
