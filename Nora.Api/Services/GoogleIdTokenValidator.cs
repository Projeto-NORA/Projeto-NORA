using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Nora.Api.Services;

/// <summary>
/// Configuração do Entrar com Google (P-042). Desligado enquanto <c>Nora:Google:ClientIds</c> estiver vazio. <c>Nora:Google:Issuers</c> (padrão
/// <c>accounts.google.com</c> e <c>https://accounts.google.com</c>) e <c>Nora:Google:JwksUrl</c> (padrão o JWKS público do Google) existem para testes e casos especiais.
/// As listas aceitam índices (<c>ClientIds:0</c>) ou valores separados por vírgula. Valor inválido impede a inicialização.
/// </summary>
public sealed class GoogleSignInOptions
{
    public const string DefaultJwksUrl = "https://www.googleapis.com/oauth2/v3/certs";
    public static readonly string[] DefaultIssuers = ["accounts.google.com", "https://accounts.google.com"];

    public IReadOnlyList<string> ClientIds { get; }
    public IReadOnlyList<string> Issuers { get; }
    public Uri JwksUrl { get; }
    public bool Enabled => ClientIds.Count > 0;

    public GoogleSignInOptions(IConfiguration configuration)
    {
        ClientIds = ForwardedHeadersSetup.ReadList(configuration, "Nora:Google:ClientIds").Distinct(StringComparer.Ordinal).ToList();
        if (ClientIds.Any(id => id.Length > 255 || id.Any(char.IsWhiteSpace))) throw new InvalidOperationException("Nora:Google:ClientIds contém um client ID inválido.");
        var issuers = ForwardedHeadersSetup.ReadList(configuration, "Nora:Google:Issuers").Distinct(StringComparer.Ordinal).ToList();
        Issuers = issuers.Count > 0 ? issuers : DefaultIssuers;
        var url = configuration["Nora:Google:JwksUrl"];
        JwksUrl = string.IsNullOrWhiteSpace(url) ? new Uri(DefaultJwksUrl)
            : Uri.TryCreate(url, UriKind.Absolute, out var parsed) && (parsed.Scheme == Uri.UriSchemeHttps || (parsed.Scheme == Uri.UriSchemeHttp && parsed.IsLoopback)) ? parsed
            : throw new InvalidOperationException("Nora:Google:JwksUrl inválido: informe uma URL https absoluta.");
    }
}

/// <summary>Identidade extraída de um ID token do Google já validado.</summary>
public sealed record GoogleIdentity(string Subject, string? Email);

/// <summary>
/// Valida ID tokens do Google sem pacote externo: JWT assinado com RS256 (qualquer outro <c>alg</c>, inclusive <c>none</c> e HS256, é recusado), chave escolhida pelo
/// <c>kid</c> no JWKS (cache pelo <c>Cache-Control: max-age</c>, entre 1 minuto e 24 horas; sem o cabeçalho, 1 hora), <c>iss</c> entre os emissores aceitos, <c>aud</c> entre os
/// client IDs configurados, <c>exp</c> com folga de 60 s e <c>email_verified</c> verdadeiro. Um <c>kid</c> desconhecido com o cache em dia só provoca nova busca (rotação de
/// chaves) se a última tiver mais de 5 minutos. O JWKS fora do ar vira 503, nunca "token inválido".
/// </summary>
public sealed class GoogleIdTokenValidator(GoogleSignInOptions options, IHttpClientFactory http, TimeProvider time, ILogger<GoogleIdTokenValidator> log)
{
    public const int MaxTokenLength = 8192;
    private static readonly TimeSpan Skew = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan DefaultCache = TimeSpan.FromHours(1);
    private static readonly TimeSpan MinCache = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan MaxCache = TimeSpan.FromHours(24);
    private static readonly TimeSpan UnknownKidRefetch = TimeSpan.FromMinutes(5);
    private readonly SemaphoreSlim gate = new(1, 1);
    private volatile KeySet? cache;

    private sealed record KeySet(Dictionary<string, RSAParameters> Keys, DateTimeOffset FetchedAt, DateTimeOffset ExpiresAt);

    /// <summary>Identidade do token ou nulo se ele é inválido por qualquer motivo (o chamador não diz qual).</summary>
    public async Task<GoogleIdentity?> ValidateAsync(string token, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(token) || token.Length > MaxTokenLength) return null;
        var parts = token.Split('.');
        if (parts.Length != 3) return null;
        JsonElement header, claims; byte[] signature;
        try { header = ParseObject(parts[0]); claims = ParseObject(parts[1]); signature = Decode(parts[2]); }
        catch (Exception error) when (error is FormatException or JsonException) { return null; }
        if (header.ValueKind != JsonValueKind.Object || claims.ValueKind != JsonValueKind.Object || signature.Length == 0) return null;
        if (Text(header, "alg") != "RS256" || Text(header, "kid") is not { Length: > 0 and <= 200 } kid) return null;
        if (await FindKeyAsync(kid, ct) is not { } parameters) return null;
        try
        {
            using var rsa = RSA.Create(parameters);
            if (!rsa.VerifyData(Encoding.ASCII.GetBytes(parts[0] + "." + parts[1]), signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)) return null;
        }
        catch (CryptographicException) { return null; }

        if (Text(claims, "iss") is not { } issuer || !options.Issuers.Contains(issuer, StringComparer.Ordinal)) return null;
        if (!AudienceMatches(claims)) return null;
        var now = time.GetUtcNow().ToUnixTimeSeconds(); var skew = (long)Skew.TotalSeconds;
        if (Seconds(claims, "exp") is not { } exp || now > exp + skew) return null;
        if (Seconds(claims, "nbf") is { } nbf && now + skew < nbf) return null;
        if (Seconds(claims, "iat") is { } iat && now + skew < iat) return null;
        if (!claims.TryGetProperty("email_verified", out var verified) || !(verified.ValueKind == JsonValueKind.True || (verified.ValueKind == JsonValueKind.String && verified.GetString() == "true"))) return null;
        if (Text(claims, "sub") is not { Length: > 0 and <= 255 } subject) return null;
        var email = Text(claims, "email") is { Length: > 0 and <= 254 } value ? value.ToLowerInvariant() : null;
        return new(subject, email);
    }

    private bool AudienceMatches(JsonElement claims)
    {
        if (!claims.TryGetProperty("aud", out var aud)) return false;
        return aud.ValueKind switch
        {
            JsonValueKind.String => options.ClientIds.Contains(aud.GetString()!, StringComparer.Ordinal),
            JsonValueKind.Array => aud.EnumerateArray().Any(item => item.ValueKind == JsonValueKind.String && options.ClientIds.Contains(item.GetString()!, StringComparer.Ordinal)),
            _ => false
        };
    }

    private async Task<RSAParameters?> FindKeyAsync(string kid, CancellationToken ct)
    {
        if (Lookup(kid, out var found, out var mayRefetch)) return found;
        if (!mayRefetch) return null;
        await gate.WaitAsync(ct);
        try
        {
            // Outro pedido pode ter acabado de buscar enquanto este esperava.
            if (Lookup(kid, out found, out mayRefetch)) return found;
            if (!mayRefetch) return null;
            var fresh = cache = await FetchAsync(ct);
            return fresh.Keys.TryGetValue(kid, out var key) ? key : null;
        }
        finally { gate.Release(); }
    }

    /// <returns>Verdadeiro se a chave saiu do cache válido; <paramref name="mayRefetch"/> diz se vale buscar o JWKS de novo.</returns>
    private bool Lookup(string kid, out RSAParameters? key, out bool mayRefetch)
    {
        var current = cache; var now = time.GetUtcNow(); key = null;
        if (current is null || now >= current.ExpiresAt) { mayRefetch = true; return false; }
        if (current.Keys.TryGetValue(kid, out var cached)) { key = cached; mayRefetch = false; return true; }
        mayRefetch = now - current.FetchedAt >= UnknownKidRefetch;
        return false;
    }

    private async Task<KeySet> FetchAsync(CancellationToken ct)
    {
        try
        {
            using var response = await http.CreateClient(NoraHttpClients.Google).GetAsync(options.JwksUrl, ct);
            if (!response.IsSuccessStatusCode) throw new HttpRequestException($"JWKS respondeu {(int)response.StatusCode}.");
            using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(ct));
            var keys = new Dictionary<string, RSAParameters>(StringComparer.Ordinal);
            if (document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty("keys", out var list) && list.ValueKind == JsonValueKind.Array)
                foreach (var item in list.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object || Text(item, "kty") != "RSA" || Text(item, "kid") is not { Length: > 0 } kid) continue;
                    if (Text(item, "alg") is { } alg && alg != "RS256") continue;
                    if (Text(item, "use") is { } use && use != "sig") continue;
                    if (Text(item, "n") is not { } n || Text(item, "e") is not { } e) continue;
                    keys[kid] = new RSAParameters { Modulus = Decode(n), Exponent = Decode(e) };
                }
            var maxAge = response.Headers.CacheControl?.MaxAge ?? DefaultCache;
            var lifetime = maxAge < MinCache ? MinCache : maxAge > MaxCache ? MaxCache : maxAge;
            var now = time.GetUtcNow();
            return new(keys, now, now + lifetime);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException or JsonException or FormatException)
        {
            log.LogWarning("Não foi possível obter as chaves públicas do Google (JWKS).");
            throw new ApiCodedException(StatusCodes.Status503ServiceUnavailable, "service-unavailable");
        }
    }

    private static string? Text(JsonElement element, string name) => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static long? Seconds(JsonElement element, string name) => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var seconds) ? seconds : null;
    private static JsonElement ParseObject(string part) { using var document = JsonDocument.Parse(Decode(part)); return document.RootElement.Clone(); }

    /// <summary>base64url sem preenchimento (RFC 7515); caractere inválido → <see cref="FormatException"/>.</summary>
    internal static byte[] Decode(string value)
    {
        if (value.Length == 0) return [];
        var text = value.Replace('-', '+').Replace('_', '/');
        switch (text.Length % 4) { case 2: text += "=="; break; case 3: text += "="; break; case 1: throw new FormatException(); }
        return Convert.FromBase64String(text);
    }
    internal static string Encode(ReadOnlySpan<byte> bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
