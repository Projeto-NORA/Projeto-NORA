using System.IO;
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
    // Valores fixos do campo "code" do problem+json; qualquer outro texto do servidor é ignorado.
    private static readonly HashSet<string> ErrorCodes = ["invalid-input", "unauthorized", "forbidden", "not-found", "conflict", "too-many-requests", "internal-error", "service-unavailable"];
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
    public Task<AddressInfo> LookupCepAsync(string cep) => SendAsync<AddressInfo>(HttpMethod.Get, $"api/address/cep/{Uri.EscapeDataString(BrazilianDataValidator.NormalizeCep(cep))}", authenticated: false);
    public Task<HealthUnit[]> HealthUnitsAsync() => SendAsync<HealthUnit[]>(HttpMethod.Get, "api/municipal/units");
    public Task<HealthUnit> CreateHealthUnitAsync(string name, string region, string address, double latitude, double longitude) => SendAsync<HealthUnit>(HttpMethod.Post, "api/municipal/units", new { name, region, address, latitude, longitude });
    public Task<HealthUnit> UpdateHealthUnitAsync(Guid id, string name, string region, string address, double latitude, double longitude) => SendAsync<HealthUnit>(HttpMethod.Put, $"api/municipal/units/{id}", new { name, region, address, latitude, longitude });
    public Task<HealthUnit[]> ObsUnitsAsync() => SendAsync<HealthUnit[]>(HttpMethod.Get, "api/obs/units");
    public Task<UnitPatientRecord[]> ObsRegistryAsync(Guid unitId) => SendAsync<UnitPatientRecord[]>(HttpMethod.Get, $"api/obs/units/{unitId}/registry");
    public Task<object> CreateObsRegistryAsync(Guid unitId, string fullName, string cpf, string email, string? phone = null, AddressInfo? address = null) => SendAsync<object>(HttpMethod.Post, $"api/obs/units/{unitId}/registry", new { fullName, cpf, email, phone, address });
    public Task<object> UpdateObsRegistryAsync(Guid unitId, string fullName, string cpf, string email, string? phone = null, AddressInfo? address = null) => SendAsync<object>(HttpMethod.Put, $"api/obs/units/{unitId}/registry", new { fullName, cpf, email, phone, address });
    public Task<object> UpdateObsRegistryEmailAsync(Guid unitId, string cpf, string email) => SendAsync<object>(HttpMethod.Patch, $"api/obs/units/{unitId}/registry/email", new { cpf, email });
    public Task<object> SetObsRegistryActiveAsync(Guid unitId, string cpf, bool isActive) => SendAsync<object>(HttpMethod.Patch, $"api/obs/units/{unitId}/registry/active", new { cpf, isActive, confirmed = true });
    public Task<AppointmentInfo[]> AppointmentsAsync(Guid unitId, DateTimeOffset from, DateTimeOffset to) => SendAsync<AppointmentInfo[]>(HttpMethod.Get, $"api/obs/units/{unitId}/appointments?from={Uri.EscapeDataString(from.ToString("O"))}&to={Uri.EscapeDataString(to.ToString("O"))}");
    public Task<AppointmentInfo> CreateAppointmentAsync(Guid unitId, string cpf, string specialty, string professional, DateTimeOffset startsAt, DateTimeOffset endsAt) => SendAsync<AppointmentInfo>(HttpMethod.Post, $"api/obs/units/{unitId}/appointments", new { cpf, specialty, professional, startsAt, endsAt });
    public Task<AppointmentInfo> ChangeAppointmentStatusAsync(Guid unitId, Guid id, string status, bool confirmed) => SendAsync<AppointmentInfo>(HttpMethod.Patch, $"api/obs/units/{unitId}/appointments/{id}/status", new { status, confirmed });
    public Task<UnitWaitingEntry[]> UnitWaitingListAsync(Guid unitId) => SendAsync<UnitWaitingEntry[]>(HttpMethod.Get, $"api/obs/units/{unitId}/waiting-list");
    public Task<UnitWaitingEntry> AddUnitWaitingEntryAsync(Guid unitId, string cpf, string specialty) => SendAsync<UnitWaitingEntry>(HttpMethod.Post, $"api/obs/units/{unitId}/waiting-list", new { cpf, specialty });
    public Task<object> CancelUnitWaitingEntryAsync(Guid unitId, long id) => SendAsync<object>(HttpMethod.Post, $"api/obs/units/{unitId}/waiting-list/{id}/cancel", new { confirmed = true });
    public Task<UnitLightningSlot[]> UnitLightningSlotsAsync(Guid unitId) => SendAsync<UnitLightningSlot[]>(HttpMethod.Get, $"api/obs/units/{unitId}/lightning-slots");
    public Task<UnitLightningSlot> CreateUnitLightningSlotAsync(Guid unitId, Guid appointmentId) => SendAsync<UnitLightningSlot>(HttpMethod.Post, $"api/obs/units/{unitId}/lightning-slots", new { appointmentId, confirmed = true });
    public Task<UnitPatientRecord[]> UnitRegistryAsync(Guid unitId) => SendAsync<UnitPatientRecord[]>(HttpMethod.Get, $"api/municipal/units/{unitId}/registry");
    public Task<AppointmentInfo[]> UnitAppointmentsAsync(Guid unitId, DateTimeOffset from, DateTimeOffset to) => SendAsync<AppointmentInfo[]>(HttpMethod.Get, $"api/municipal/units/{unitId}/appointments?from={Uri.EscapeDataString(from.ToString("O"))}&to={Uri.EscapeDataString(to.ToString("O"))}");
    public Task<StaffAccount[]> StaffAsync() => SendAsync<StaffAccount[]>(HttpMethod.Get, "api/municipal/staff");
    public Task<HealthUnit[]> StaffUnitsAsync(Guid id) => SendAsync<HealthUnit[]>(HttpMethod.Get, $"api/municipal/staff/{id}/units");
    public Task<object> SetStaffUnitAsync(Guid staffId, Guid unitId, bool grant) => SendAsync<object>(grant ? HttpMethod.Put : HttpMethod.Delete, $"api/municipal/staff/{staffId}/units/{unitId}");
    public Task<MunicipalSummary> SummaryAsync(Guid? unitId = null) => SendAsync<MunicipalSummary>(HttpMethod.Get, unitId is { } id ? $"api/municipal/summary?unitId={id}" : "api/municipal/summary");

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
                var unauthorized = response.StatusCode == HttpStatusCode.Unauthorized;
                if (unauthorized) ClearSession();
                // Nunca exibe corpo arbitrário do servidor: a mensagem sai do status, do code conhecido e do contexto da operação.
                var code = await ReadErrorCodeAsync(response);
                throw new NoraApiException(ErrorMessage(method, path, response.StatusCode, code, authenticated), unauthorized);
            }
            if (response.StatusCode == HttpStatusCode.NoContent && typeof(T) == typeof(object)) return (T)new object();
            // Só JSON é aceito como sucesso: HTML de proxy ou texto simples com status 200 vira erro tratado.
            if (!IsJson(response.Content.Headers.ContentType?.MediaType)) throw new NoraApiException("A API retornou dados em um formato inesperado.");
            return await response.Content.ReadFromJsonAsync<T>() ?? throw new NoraApiException("A API retornou uma resposta incompleta.");
        }
        catch (HttpRequestException) { throw new NoraApiException("Não foi possível conectar à API. Confira o endereço e a conexão."); }
        catch (IOException) { throw new NoraApiException("A conexão com a API foi interrompida. Tente novamente."); }
        catch (TaskCanceledException) { throw new NoraApiException("A API demorou para responder. Tente novamente."); }
        // Corpo que não é JSON (por exemplo, HTML de um proxy) lança NotSupportedException em ReadFromJsonAsync.
        catch (Exception error) when (error is JsonException or NotSupportedException) { throw new NoraApiException("A API retornou dados em um formato inesperado."); }
    }

    private static bool IsJson(string? mediaType) => mediaType is not null && (mediaType.Equals("application/json", StringComparison.OrdinalIgnoreCase) || mediaType.EndsWith("+json", StringComparison.OrdinalIgnoreCase));

    private static async Task<string?> ReadErrorCodeAsync(HttpResponseMessage response)
    {
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync();
            var buffer = new byte[4096];
            var total = 0;
            int read;
            while (total < buffer.Length && (read = await stream.ReadAsync(buffer.AsMemory(total))) > 0) total += read;
            using var document = JsonDocument.Parse(buffer.AsMemory(0, total));
            if (document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty("code", out var code)
                && code.ValueKind == JsonValueKind.String && code.GetString() is { } value && ErrorCodes.Contains(value)) return value;
        }
        catch (Exception error) when (error is JsonException or IOException or HttpRequestException or ObjectDisposedException or InvalidOperationException or NotSupportedException) { }
        return null;
    }

    private static string ErrorMessage(HttpMethod method, string path, HttpStatusCode status, string? code, bool authenticated)
    {
        var kind = code ?? status switch
        {
            HttpStatusCode.BadRequest => "invalid-input",
            HttpStatusCode.Unauthorized => "unauthorized",
            HttpStatusCode.Forbidden => "forbidden",
            HttpStatusCode.NotFound => "not-found",
            HttpStatusCode.Conflict => "conflict",
            HttpStatusCode.TooManyRequests => "too-many-requests",
            HttpStatusCode.ServiceUnavailable => "service-unavailable",
            _ => "internal-error"
        };
        return kind switch
        {
            "unauthorized" => authenticated ? "Sua sessão terminou. Entre novamente." : "CPF/e-mail ou senha não conferem.",
            "forbidden" => path.StartsWith("api/obs/units/", StringComparison.Ordinal)
                ? "Sua conta não tem acesso a esta UBS. Peça a concessão à Secretaria."
                : "Esta conta não tem acesso a este aplicativo ou operação.",
            "conflict" => ConflictMessage(method, path),
            "not-found" => path.StartsWith("api/address/cep/", StringComparison.Ordinal) ? "CEP não encontrado. Confira os oito dígitos." : "Registro não encontrado. Atualize os dados e tente novamente.",
            "invalid-input" => "Confira os dados informados e tente novamente.",
            "too-many-requests" => "Muitas tentativas. Aguarde e tente novamente.",
            "service-unavailable" => "O serviço ou a base de dados da unidade está indisponível no momento. Tente novamente em instantes.",
            _ => "Não foi possível concluir a operação. Verifique a API e tente novamente."
        };
    }

    private static string ConflictMessage(HttpMethod method, string path)
    {
        var create = method == HttpMethod.Post;
        if (create && path.EndsWith("/registry", StringComparison.Ordinal)) return "Já existe um cadastro com este CPF nesta UBS.";
        if (create && path.EndsWith("/appointments", StringComparison.Ordinal)) return "Horário em conflito: o paciente ou o profissional já tem consulta ou vaga neste intervalo.";
        if (path.Contains("/appointments/", StringComparison.Ordinal) && path.EndsWith("/status", StringComparison.Ordinal)) return "A consulta já foi alterada por outra pessoa. Atualize a agenda.";
        if (create && path.EndsWith("/waiting-list", StringComparison.Ordinal)) return "Este paciente já está na fila desta especialidade.";
        if (create && path.EndsWith("/lightning-slots", StringComparison.Ordinal)) return "Já existe uma Vaga Relâmpago gerada para esta consulta.";
        if (path.Contains("/waiting-list/", StringComparison.Ordinal) && path.EndsWith("/cancel", StringComparison.Ordinal)) return "A entrada já foi encerrada. Atualize a fila.";
        return "O registro já existe ou sua situação mudou. Atualize a lista.";
    }
    public void Dispose() { disposed = true; ClearSession(); http.Dispose(); }
}
