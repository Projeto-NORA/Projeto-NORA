using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

/// <summary>
/// Google falso dos testes de P-042, num só <see cref="HttpMessageHandler"/> injetado nos clientes HTTP da API: publica o JWKS de uma chave RSA gerada no teste
/// (com <c>Cache-Control: max-age</c>), emite tokens de acesso OAuth2 conferindo a assinatura RS256 da asserção da conta de serviço (chave também gerada no teste) e recebe
/// as mensagens do FCM HTTP v1, respondendo por token: sucesso, <c>UNREGISTERED</c> (404) ou falha transitória (503). Nenhuma credencial real existe aqui.
/// </summary>
internal sealed class FakeGoogle : HttpMessageHandler
{
    public const string JwksUrl = "https://www.googleapis.com/oauth2/v3/certs";
    public const string TokenUrl = "https://oauth2.googleapis.com/token";
    public const string ProjectId = "nora-test";
    public const string FcmUrl = "https://fcm.googleapis.com/v1/projects/" + ProjectId + "/messages:send";
    public const string Kid = "google-test-key";
    public const string ClientEmail = "nora-push@nora-test.iam.gserviceaccount.com";

    public RSA GoogleKey { get; } = RSA.Create(2048);
    public RSA ServiceAccountKey { get; } = RSA.Create(2048);
    public int Requests;
    public int JwksFetches;
    public int TokenRequests;
    public int AssertionErrors;
    /// <summary>Mensagens aceitas e recusadas pelo FCM falso: token, JSON cru da mensagem e status devolvido.</summary>
    public List<(string Token, string Json, int Status)> Messages { get; } = [];
    /// <summary>Comportamento por token: "unregistered" (404 UNREGISTERED), "fail" (503 sempre) ou "fail:N" (503 nas N primeiras tentativas).</summary>
    public Dictionary<string, string> Modes { get; } = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> attempts = new(StringComparer.Ordinal);
    private int issued;

    public string ServiceAccountJson() => new JsonObject
    {
        ["type"] = "service_account",
        ["project_id"] = ProjectId,
        ["private_key_id"] = "sa-test-key",
        ["private_key"] = ServiceAccountKey.ExportPkcs8PrivateKeyPem(),
        ["client_email"] = ClientEmail,
        ["token_uri"] = TokenUrl
    }.ToJsonString();

    public List<(string Token, JsonElement Message)> Sent(int from = 0)
    {
        lock (Messages)
            return Messages.Skip(from).Where(m => m.Status == 200).Select(m => (m.Token, JsonDocument.Parse(m.Json).RootElement.GetProperty("message").Clone())).ToList();
    }
    public int Count { get { lock (Messages) return Messages.Count; } }
    /// <summary>Define o comportamento do token e zera a contagem que o "fail:N" usa.</summary>
    public void SetMode(string token, string mode) { lock (Messages) { Modes[token] = mode; attempts[token] = 0; } }
    public int AttemptsFor(string token) { lock (Messages) return Messages.Count(m => m.Token == token); }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref Requests);
        var url = request.RequestUri!.GetLeftPart(UriPartial.Path);
        if (request.Method == HttpMethod.Get && url == JwksUrl)
        {
            Interlocked.Increment(ref JwksFetches);
            var p = GoogleKey.ExportParameters(false);
            var jwks = new JsonObject { ["keys"] = new JsonArray(new JsonObject { ["kty"] = "RSA", ["alg"] = "RS256", ["use"] = "sig", ["kid"] = Kid, ["n"] = B64(p.Modulus!), ["e"] = B64(p.Exponent!) }) };
            var response = Json(HttpStatusCode.OK, jwks.ToJsonString());
            response.Headers.CacheControl = new() { Public = true, MaxAge = TimeSpan.FromHours(1) };
            return response;
        }
        if (request.Method == HttpMethod.Post && url == TokenUrl)
        {
            Interlocked.Increment(ref TokenRequests);
            var form = (await request.Content!.ReadAsStringAsync(cancellationToken)).Split('&').Select(pair => pair.Split('=', 2)).ToDictionary(pair => Uri.UnescapeDataString(pair[0]), pair => Uri.UnescapeDataString(pair[1].Replace('+', ' ')));
            if (form.GetValueOrDefault("grant_type") != "urn:ietf:params:oauth:grant-type:jwt-bearer" || !ValidAssertion(form.GetValueOrDefault("assertion")))
            {
                Interlocked.Increment(ref AssertionErrors);
                return Json(HttpStatusCode.BadRequest, "{\"error\":\"invalid_grant\"}");
            }
            return Json(HttpStatusCode.OK, $"{{\"access_token\":\"fake-access-{Interlocked.Increment(ref issued)}\",\"expires_in\":3599,\"token_type\":\"Bearer\"}}");
        }
        if (request.Method == HttpMethod.Post && url == FcmUrl)
        {
            if (request.Headers.Authorization is not { Scheme: "Bearer", Parameter: { } bearer } || !bearer.StartsWith("fake-access-", StringComparison.Ordinal)) return Json(HttpStatusCode.Unauthorized, "{\"error\":{\"code\":401,\"status\":\"UNAUTHENTICATED\"}}");
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            var token = JsonDocument.Parse(body).RootElement.GetProperty("message").GetProperty("token").GetString()!;
            int status; int attempt;
            lock (Messages)
            {
                attempt = attempts[token] = attempts.GetValueOrDefault(token) + 1;
                var mode = Modes.GetValueOrDefault(token);
                status = mode switch
                {
                    "unregistered" => 404,
                    "fail" => 503,
                    { } m when m.StartsWith("fail:", StringComparison.Ordinal) && attempt <= int.Parse(m[5..]) => 503,
                    _ => 200
                };
                Messages.Add((token, body, status));
            }
            return status switch
            {
                404 => Json(HttpStatusCode.NotFound, "{\"error\":{\"code\":404,\"message\":\"Requested entity was not found.\",\"status\":\"NOT_FOUND\",\"details\":[{\"@type\":\"type.googleapis.com/google.firebase.fcm.v1.FcmError\",\"errorCode\":\"UNREGISTERED\"}]}}"),
                503 => Json(HttpStatusCode.ServiceUnavailable, "{\"error\":{\"code\":503,\"status\":\"UNAVAILABLE\"}}"),
                _ => Json(HttpStatusCode.OK, $"{{\"name\":\"projects/{ProjectId}/messages/{attempt}\"}}")
            };
        }
        return new HttpResponseMessage(HttpStatusCode.NotFound) { RequestMessage = request };
    }

    private bool ValidAssertion(string? jwt)
    {
        var parts = jwt?.Split('.') ?? [];
        if (parts.Length != 3) return false;
        var header = JsonDocument.Parse(Unb64(parts[0])).RootElement;
        var claims = JsonDocument.Parse(Unb64(parts[1])).RootElement;
        if (header.GetProperty("alg").GetString() != "RS256") return false;
        if (!ServiceAccountKey.VerifyData(Encoding.ASCII.GetBytes(parts[0] + "." + parts[1]), Unb64(parts[2]), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)) return false;
        return claims.GetProperty("iss").GetString() == ClientEmail && claims.GetProperty("aud").GetString() == TokenUrl
            && claims.GetProperty("scope").GetString() == "https://www.googleapis.com/auth/firebase.messaging" && claims.GetProperty("exp").GetInt64() > claims.GetProperty("iat").GetInt64();
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) => new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    /// <summary>Monta um JWT; <paramref name="sign"/> recebe os bytes de "cabeçalho.payload" e devolve a assinatura (vazia para alg none).</summary>
    public static string Jwt(JsonObject header, JsonObject payload, Func<byte[], byte[]> sign)
    {
        var signingInput = B64(Encoding.UTF8.GetBytes(header.ToJsonString())) + "." + B64(Encoding.UTF8.GetBytes(payload.ToJsonString()));
        return signingInput + "." + B64(sign(Encoding.ASCII.GetBytes(signingInput)));
    }
    public static string B64(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static byte[] Unb64(string value)
    {
        var s = value.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(s.PadRight(s.Length + (4 - s.Length % 4) % 4, '='));
    }
}
