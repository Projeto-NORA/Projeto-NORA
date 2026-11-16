using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Nora.Api.Services;

/// <summary>Resultado de um envio ao FCM.</summary>
public enum PushOutcome { Sent, Unregistered, Transient, Permanent }

/// <summary>Um aviso a enviar: o tipo do evento e ids opacos para o app abrir a tela certa. Nunca leva nome, CPF, especialidade ou dado de saúde.</summary>
public sealed record PushMessage(string Type, Guid? UnitId, Guid? DependentId, Guid? AppointmentId);

/// <summary>
/// Envia pelo FCM HTTP v1 (<c>projects/{id}/messages:send</c>) com OAuth2 de conta de serviço: asserção JWT RS256 assinada aqui (System.Security.Cryptography, sem pacote),
/// trocada por um token de acesso em <c>token_uri</c> (escopo <c>firebase.messaging</c>) e guardada até um minuto antes de vencer. 404 ou <c>UNREGISTERED</c> → o token não
/// existe mais; 400 → mensagem recusada (não adianta repetir); 401 → o token de acesso é descartado e a mensagem volta a tentar; 429, 5xx, rede e tempo esgotado → transitório.
/// </summary>
public sealed class PushSender(PushOptions options, IHttpClientFactory http, TimeProvider time, ILogger<PushSender> log)
{
    public const string Title = "NORA";
    private const string Scope = "https://www.googleapis.com/auth/firebase.messaging";
    private readonly SemaphoreSlim gate = new(1, 1);
    private sealed record AccessToken(string Token, DateTimeOffset ExpiresAt);
    private volatile AccessToken? access;

    /// <summary>Texto genérico de cada evento (decisão de privacidade: nada que identifique a pessoa ou a consulta).</summary>
    public static string BodyFor(string type) => type switch
    {
        "lightning-offer" => "Há uma nova oportunidade de consulta para você. Abra o NORA.",
        "announcement" => "Há um novo comunicado para você. Abra o NORA.",
        "presence-window" => "Já é possível confirmar presença em uma consulta. Abra o NORA.",
        "family-decision" => "Um pedido de família foi respondido. Abra o NORA.",
        _ => throw new ArgumentOutOfRangeException(nameof(type))
    };

    public async Task<PushOutcome> SendAsync(string deviceToken, PushMessage message, CancellationToken ct)
    {
        string bearer;
        try { bearer = await AccessTokenAsync(ct); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception) { log.LogWarning("Não foi possível obter o token de acesso do Firebase; o envio será repetido."); return PushOutcome.Transient; }
        var data = new JsonObject { ["type"] = message.Type };
        if (message.UnitId is { } unit) data["unitId"] = unit.ToString();
        if (message.DependentId is { } dependent) data["dependentId"] = dependent.ToString();
        if (message.AppointmentId is { } appointment) data["appointmentId"] = appointment.ToString();
        var body = new JsonObject
        {
            ["message"] = new JsonObject
            {
                ["token"] = deviceToken,
                ["notification"] = new JsonObject { ["title"] = Title, ["body"] = BodyFor(message.Type) },
                ["data"] = data,
                ["android"] = new JsonObject { ["priority"] = "HIGH" }
            }
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, $"https://fcm.googleapis.com/v1/projects/{options.ProjectId}/messages:send")
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        try
        {
            using var response = await http.CreateClient(NoraHttpClients.Push).SendAsync(request, ct);
            if (response.IsSuccessStatusCode) return PushOutcome.Sent;
            var status = response.StatusCode;
            if (status == HttpStatusCode.NotFound || await ErrorCodeAsync(response, ct) == "UNREGISTERED") return PushOutcome.Unregistered;
            if (status == HttpStatusCode.Unauthorized) { Forget(bearer); return PushOutcome.Transient; }
            return status == HttpStatusCode.BadRequest ? PushOutcome.Permanent : PushOutcome.Transient;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException) { return PushOutcome.Transient; }
    }

    private static async Task<string?> ErrorCodeAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            if (!document.RootElement.TryGetProperty("error", out var error) || error.ValueKind != JsonValueKind.Object || !error.TryGetProperty("details", out var details) || details.ValueKind != JsonValueKind.Array) return null;
            foreach (var detail in details.EnumerateArray())
                if (detail.ValueKind == JsonValueKind.Object && detail.TryGetProperty("errorCode", out var code) && code.ValueKind == JsonValueKind.String) return code.GetString();
            return null;
        }
        catch (JsonException) { return null; }
    }

    private void Forget(string bearer) => Interlocked.CompareExchange(ref access, null, access is { } current && current.Token == bearer ? current : null);

    private async Task<string> AccessTokenAsync(CancellationToken ct)
    {
        var credential = options.Credential ?? throw new InvalidOperationException("Push sem credencial.");
        if (access is { } cached && time.GetUtcNow() < cached.ExpiresAt) return cached.Token;
        await gate.WaitAsync(ct);
        try
        {
            if (access is { } again && time.GetUtcNow() < again.ExpiresAt) return again.Token;
            var now = time.GetUtcNow();
            var header = new JsonObject { ["alg"] = "RS256", ["typ"] = "JWT" };
            if (credential.PrivateKeyId is { Length: > 0 } keyId) header["kid"] = keyId;
            var claims = new JsonObject { ["iss"] = credential.ClientEmail, ["scope"] = Scope, ["aud"] = credential.TokenUri.ToString(), ["iat"] = now.ToUnixTimeSeconds(), ["exp"] = now.AddHours(1).ToUnixTimeSeconds() };
            var input = GoogleIdTokenValidator.Encode(Encoding.UTF8.GetBytes(header.ToJsonString())) + "." + GoogleIdTokenValidator.Encode(Encoding.UTF8.GetBytes(claims.ToJsonString()));
            string assertion;
            using (var rsa = RSA.Create())
            {
                rsa.ImportFromPem(credential.PrivateKeyPem);
                assertion = input + "." + GoogleIdTokenValidator.Encode(rsa.SignData(Encoding.ASCII.GetBytes(input), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
            }
            using var content = new FormUrlEncodedContent([new("grant_type", "urn:ietf:params:oauth:grant-type:jwt-bearer"), new("assertion", assertion)]);
            using var response = await http.CreateClient(NoraHttpClients.Push).PostAsync(credential.TokenUri, content, ct);
            response.EnsureSuccessStatusCode();
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var token = document.RootElement.GetProperty("access_token").GetString() ?? throw new InvalidOperationException();
            var lifetime = document.RootElement.TryGetProperty("expires_in", out var seconds) && seconds.TryGetInt32(out var value) ? value : 3600;
            access = new(token, now.AddSeconds(Math.Max(lifetime - 60, 0)));
            return token;
        }
        finally { gate.Release(); }
    }
}
