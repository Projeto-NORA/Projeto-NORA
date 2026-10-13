using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Nora.Desktop.Models;

namespace Nora.Desktop.Services;

public sealed class NoraApiException(string message, bool unauthorized = false) : Exception(message)
{
    public bool Unauthorized { get; } = unauthorized;
}

/// <summary>HTTP boundary. Session tokens remain in memory and never enter preferences.</summary>
public sealed class NoraApiClient : IDisposable
{
    private readonly HttpClient http;
    private Uri? endpoint;
    private string? token;
    private bool disposed;
    public NoraApiClient(HttpMessageHandler? handler = null)
    {
        http = new HttpClient(handler ?? new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(20) };
    }
    public static Uri ValidateEndpoint(string address)
    {
        if (!Uri.TryCreate(address.Trim(), UriKind.Absolute, out var uri)
            || (uri.Scheme != "https" && !(uri.Scheme == "http" && uri.IsLoopback))
            || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new NoraApiException("Informe o endereço HTTPS da API. HTTP é permitido somente no computador local para desenvolvimento.");
        return new Uri(uri.AbsoluteUri.TrimEnd('/') + "/");
    }
    public void Configure(string address)
    {
        endpoint = ValidateEndpoint(address);
        ClearSession();
    }
    public async Task<LoginSession> LoginAsync(string scope, string identifier, string password)
    {
        ClearSession();
        var session = await SendAsync<LoginSession>(HttpMethod.Post, "api/auth/login", new { scope, identifier, password }, false);
        if (disposed) throw new NoraApiException("O aplicativo foi fechado. Abra-o para entrar novamente.");
        if (string.IsNullOrWhiteSpace(session.Token) || session.ExpiresAt <= DateTimeOffset.UtcNow || session.User == null || session.User.Scope != scope)
            throw new NoraApiException("A API retornou uma sessão inválida. Tente entrar novamente.");
        token = session.Token;
        return session;
    }
    public Task<AccountUser> MeAsync() => SendAsync<AccountUser>(HttpMethod.Get, "api/auth/me");
    public async Task LogoutAsync()
    {
        try { if (token != null) await SendAsync<object>(HttpMethod.Post, "api/auth/logout"); }
        finally { ClearSession(); }
    }
    public void ClearSession() => token = null;
    public Task<PatientAccount[]> PatientsAsync() => SendAsync<PatientAccount[]>(HttpMethod.Get, "api/patients");
    public Task<PatientAccount> RegisterPatientAsync(string fullName, string cpf, string email, string password, string? phone = null) => SendAsync<PatientAccount>(HttpMethod.Post, "api/patients", new { fullName, cpf, email, password, phone });
    public Task<QueueEntry[]> QueueAsync() => SendAsync<QueueEntry[]>(HttpMethod.Get, "api/queue");
    public Task<QueueEntry> EnqueueAsync(Guid patientId) => SendAsync<QueueEntry>(HttpMethod.Post, "api/queue", new { patientId });
    public Task<object> CancelAsync(long queueId) => SendAsync<object>(HttpMethod.Post, $"api/queue/{queueId}/cancel");
    public Task<LightningSlot[]> SlotsAsync() => SendAsync<LightningSlot[]>(HttpMethod.Get, "api/slots");
    public Task<LightningSlot> CreateSlotAsync(string label) => SendAsync<LightningSlot>(HttpMethod.Post, "api/slots", new { label });
    public Task<MunicipalSummary> SummaryAsync() => SendAsync<MunicipalSummary>(HttpMethod.Get, "api/municipal/summary");

    private async Task<T> SendAsync<T>(HttpMethod method, string path, object? body = null, bool authenticated = true)
    {
        if (disposed) throw new NoraApiException("O aplicativo foi fechado. Abra-o para conectar novamente.");
        if (endpoint == null) throw new NoraApiException("Configure o endereço da API antes de conectar.");
        if (authenticated && token == null) throw new NoraApiException("Entre na sua conta para continuar.", true);
        try
        {
            using var request = new HttpRequestMessage(method, new Uri(endpoint, path));
            if (authenticated) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            if (body != null) request.Content = JsonContent.Create(body);
            using var response = await http.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode == HttpStatusCode.Unauthorized) ClearSession();
                // Never surface arbitrary server bodies, connection strings or stack traces.
                var message = response.StatusCode switch
                {
                    HttpStatusCode.Unauthorized => authenticated ? "Sua sessão terminou. Entre novamente." : "CPF/e-mail ou senha não conferem.",
                    HttpStatusCode.Forbidden => "Esta conta não tem acesso a este aplicativo ou operação.",
                    HttpStatusCode.Conflict => "O registro já existe ou sua situação mudou. Atualize a lista.",
                    HttpStatusCode.BadRequest => "Confira os dados informados e tente novamente.",
                    HttpStatusCode.TooManyRequests => "Muitas tentativas. Aguarde e tente novamente.",
                    _ => "Não foi possível concluir a operação. Verifique a API e tente novamente."
                };
                throw new NoraApiException(message, response.StatusCode == HttpStatusCode.Unauthorized);
            }
            if (response.StatusCode == HttpStatusCode.NoContent && typeof(T) == typeof(object)) return (T)new object();
            return await response.Content.ReadFromJsonAsync<T>() ?? throw new NoraApiException("A API retornou uma resposta incompleta.");
        }
        catch (HttpRequestException) { throw new NoraApiException("Não foi possível conectar à API. Confira o endereço e a conexão."); }
        catch (TaskCanceledException) { throw new NoraApiException("A API demorou para responder. Tente novamente."); }
        catch (JsonException) { throw new NoraApiException("A API retornou dados em um formato inesperado."); }
    }
    public void Dispose() { disposed = true; ClearSession(); http.Dispose(); }
}
