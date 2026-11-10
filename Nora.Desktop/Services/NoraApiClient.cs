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
    public Task<AddressInfo> LookupCepAsync(string cep) => SendAsync<AddressInfo>(HttpMethod.Get, $"api/address/cep/{Uri.EscapeDataString(BrazilianDataValidator.NormalizeCep(cep))}", authenticated: false);
    public Task<PatientAccount[]> PatientsAsync() => SendAsync<PatientAccount[]>(HttpMethod.Get, "api/patients");
    public Task<PatientAccount> RegisterPatientAsync(string fullName, string cpf, string email, string password, string? phone = null, AddressInfo? address = null) => SendAsync<PatientAccount>(HttpMethod.Post, "api/patients", new { fullName, cpf, email, password, phone, address });
    public Task<object> SetPatientActiveAsync(Guid patientId, bool isActive) => SendAsync<object>(HttpMethod.Patch, $"api/patients/{patientId}/active", new { isActive });
    public Task<QueueEntry[]> QueueAsync() => SendAsync<QueueEntry[]>(HttpMethod.Get, "api/queue");
    public Task<QueueEntry> EnqueueAsync(Guid patientId) => SendAsync<QueueEntry>(HttpMethod.Post, "api/queue", new { patientId });
    public Task<object> CancelAsync(long queueId) => SendAsync<object>(HttpMethod.Post, $"api/queue/{queueId}/cancel");
    public Task<LightningSlot[]> SlotsAsync() => SendAsync<LightningSlot[]>(HttpMethod.Get, "api/slots");
    public Task<LightningSlot> CreateSlotAsync(string label) => SendAsync<LightningSlot>(HttpMethod.Post, "api/slots", new { label });
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
                if (response.StatusCode == HttpStatusCode.Unauthorized) ClearSession();
                // Never surface arbitrary server bodies, connection strings or stack traces.
                var message = response.StatusCode switch
                {
                    HttpStatusCode.Unauthorized => authenticated ? "Sua sessão terminou. Entre novamente." : "CPF/e-mail ou senha não conferem.",
                    HttpStatusCode.Forbidden => "Esta conta não tem acesso a este aplicativo ou operação.",
                    HttpStatusCode.Conflict => "O registro já existe ou sua situação mudou. Atualize a lista.",
                    HttpStatusCode.NotFound => path.StartsWith("api/address/cep/", StringComparison.Ordinal) ? "CEP não encontrado. Confira os oito dígitos." : "Registro não encontrado. Atualize os dados e tente novamente.",
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
