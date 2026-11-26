using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using Nora.Desktop.Models;

namespace Nora.Desktop.Checks;

internal static partial class Program
{
    /// <summary>
    /// Rodada final de 2026-11-16 na fixture, exatamente como o contrato da API: computadores do OBS (ativação, código, revogação, cabeçalho X-Nora-Device, "manter conectado"),
    /// atendimento ao público e os cartões novos do resumo da Secretaria.
    /// </summary>
    private sealed partial class ApiFixture
    {
        public sealed class FixtureDevice
        {
            public Guid Id { get; init; } = Guid.NewGuid();
            public Guid UnitId { get; init; }
            public string Name { get; init; } = "";
            public string Status { get; set; } = "pending";
            public string? Secret { get; set; }
            public string? Code { get; set; }
            public DateTimeOffset? CodeExpiresAt { get; set; }
            public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
            public DateTimeOffset? ActivatedAt { get; set; }
            public DateTimeOffset? LastLoginAt { get; set; }
            public DateTimeOffset? RevokedAt { get; set; }
        }

        private readonly Dictionary<Guid, UnitProfile> profiles = [];
        private Guid? sessionDeviceId;
        public List<FixtureDevice> Devices { get; } = [];
        /// <summary>Cabeçalhos X-Nora-Device recebidos, um por requisição (nulo quando a requisição não o trouxe).</summary>
        public List<string?> DeviceHeaders { get; } = [];
        public string? LastDeviceHeader => DeviceHeaders.Count == 0 ? null : DeviceHeaders[^1];
        /// <summary>Endereços (host) que receberam o cabeçalho do computador; o teste confere que o segredo nunca vai a outro servidor.</summary>
        public List<string> DeviceHeaderHosts { get; } = [];
        public bool LastKeepSignedInRequested { get; private set; }
        /// <summary>Com o OBS de atendente, o login sem computador ativado responde 403 device-required (exigência ligada, como no servidor).</summary>
        public bool RequireDeviceForAttendants { get; set; }
        /// <summary>Recurso dos computadores ligado no servidor (<c>Nora:ObsDevices:Enabled</c>). Desligado, como no padrão, o OBS de desktop mantém a sessão sem computador.</summary>
        public bool ObsComputersEnabled { get; set; }
        /// <summary>A expiração devolvida pelo login "manter conectado" (30 dias) e a do login comum (1 hora na fixture).</summary>
        public DateTimeOffset? LastLoginExpiresAt { get; private set; }
        /// <summary>A conferência do computador (GET /devices/current) responde 500, como uma API instável na abertura do OBS.</summary>
        public bool FailDeviceCurrent { get; set; }
        /// <summary>O login ignora o cabeçalho do computador (device nulo), como no servidor com a exigência desligada para uma atendente sem acesso à UBS dele; o computador continua ativo.</summary>
        public bool IgnoreDeviceOnLogin { get; set; }
        public bool FailProfile { get; set; }
        public List<JsonElement> ProfileBodies { get; } = [];
        public UnitProfile? ProfileOf(Guid unitId) => profiles.GetValueOrDefault(unitId);

        public FixtureDevice AddDevice(Guid unitId, string name, string status = "active", string secret = "")
        {
            var device = new FixtureDevice { UnitId = unitId, Name = name, Status = status, Secret = status == "active" ? (secret.Length == 0 ? new string('A', 64) : secret) : null, ActivatedAt = status == "active" ? DateTimeOffset.UtcNow : null };
            Devices.Add(device); return device;
        }
        /// <summary>O cabeçalho que o OBS manda para o computador (id em hex de 32 e segredo de 64).</summary>
        public static string HeaderFor(FixtureDevice device) => device.Id.ToString("N") + "." + device.Secret;
        /// <summary>O administrador de outro computador revoga este por fora: as sessões ligadas a ele passam a receber 401.</summary>
        public void RevokeDevice(FixtureDevice device) { device.Status = "revoked"; device.Secret = null; device.Code = null; device.CodeExpiresAt = null; device.RevokedAt = DateTimeOffset.UtcNow; }

        private FixtureDevice? DeviceFromHeader(HttpRequestMessage request)
        {
            if (!request.Headers.TryGetValues("X-Nora-Device", out var values)) return null;
            var parts = values.Single().Split('.');
            return parts.Length == 2 && Guid.TryParseExact(parts[0], "N", out var id) ? Devices.FirstOrDefault(item => item.Id == id && item.Status == "active" && item.Secret == parts[1]) : null;
        }
        private static ObsDevice ToApi(FixtureDevice device, bool isThis = false) =>
            new(device.Id, device.Name, device.Status, device.CreatedAt, "Operador Fixture", device.ActivatedAt, device.LastLoginAt, device.RevokedAt, device.RevokedAt is null ? null : "Operador Fixture", device.CodeExpiresAt, isThis);
        private DeviceSummary Summary(FixtureDevice device) => new(device.Id, device.Name, device.UnitId, units.Single(unit => unit.Id == device.UnitId).Name);
        private static string NewCode() => "ABCD-" + Random.Shared.Next(1000, 9999).ToString(CultureInfo.InvariantCulture);

        /// <summary>Login com as regras do contrato: administrador entra sempre; atendente exige computador ativado; "manter conectado" só vale com computador.</summary>
        private HttpResponseMessage BuildLogin(HttpRequestMessage request, JsonElement body, string accountName)
        {
            var device = Scope == "obs" && !IgnoreDeviceOnLogin ? DeviceFromHeader(request) : null;
            var keepRequested = body.TryGetProperty("keepSignedIn", out var keepValue) && keepValue.ValueKind == JsonValueKind.True;
            var desktop = body.TryGetProperty("desktopSession", out var desktopValue) && desktopValue.ValueKind == JsonValueKind.True;
            LastKeepSignedInRequested = keepRequested;
            if (Scope == "obs" && ObsRole == "attendant" && RequireDeviceForAttendants && device is null) return Problem(HttpStatusCode.Forbidden, "device-required");
            sessionDeviceId = device?.Id;
            if (device is not null) device.LastLoginAt = DateTimeOffset.UtcNow;
            var keep = keepRequested && (device is not null || desktop && (Scope == "municipal" || !ObsComputersEnabled));
            LastLoginExpiresAt = DateTimeOffset.UtcNow.Add(keep ? TimeSpan.FromDays(30) : TimeSpan.FromHours(desktop ? 24 : 1));
            return Reply(new LoginSession("fixture-token", LastLoginExpiresAt.Value, new(AccountId, accountName, Scope == "obs" ? ObsRole : "manager", Scope, MustChangePassword), keep, device is null ? null : Summary(device)));
        }

        /// <summary>Rotas públicas dos computadores: conferência (cabeçalho) e ativação por código.</summary>
        private async Task<HttpResponseMessage?> HandleDevicePublicAsync(HttpRequestMessage request, string path, CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Get && path == "/api/obs/devices/current")
                return FailDeviceCurrent ? Problem(HttpStatusCode.InternalServerError, "internal-error") : DeviceFromHeader(request) is { } current ? Reply(Summary(current)) : Problem(HttpStatusCode.Unauthorized, "device-revoked");
            if (request.Method == HttpMethod.Post && path == "/api/obs/devices/activate")
            {
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
                var code = (Text(body.RootElement, "code") ?? "").Replace("-", "").Replace(" ", "").ToUpperInvariant();
                var device = Devices.FirstOrDefault(item => item.Code is not null && item.Code.Replace("-", "") == code && item.CodeExpiresAt > DateTimeOffset.UtcNow && item.Status is "pending" or "active");
                if (device is null) return Problem(HttpStatusCode.BadRequest, "invalid-activation-code");
                device.Secret = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"); device.Status = "active"; device.ActivatedAt = DateTimeOffset.UtcNow; device.Code = null; device.CodeExpiresAt = null;
                return Reply(new ActivatedDevice(device.Id, device.Secret, device.Name, device.UnitId, Summary(device).UnitName));
            }
            return null;
        }

        /// <summary>Depois do bearer: a sessão ligada a um computador deixa de valer se ele foi revogado ou se o cabeçalho não confere (401 genérico, como no servidor).</summary>
        private HttpResponseMessage? RejectDeviceSession(HttpRequestMessage request)
        {
            if (sessionDeviceId is not { } id) return null;
            var current = DeviceFromHeader(request);
            return current?.Id == id ? null : new(HttpStatusCode.Unauthorized);
        }

        private HttpResponseMessage? HandleUnitRound(HttpRequestMessage request, Guid unitId, string[] rest, JsonDocument? payload)
        {
            var method = request.Method; var state = State(unitId); var sched = Sched(unitId);
            JsonElement body = payload?.RootElement ?? default;
            var query = System.Web.HttpUtility.ParseQueryString(request.RequestUri!.Query);
            switch (rest)
            {
                // ---- Computadores ----
                case ["devices"] when method == HttpMethod.Get:
                    if (AdministratorOnly() is { } notAdmin) return notAdmin;
                    var thisDevice = DeviceFromHeader(request);
                    return Reply(new { devices = Devices.Where(item => item.UnitId == unitId).OrderBy(item => item.Status switch { "active" => 0, "pending" => 1, _ => 2 }).ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase).Select(item => ToApi(item, thisDevice?.Id == item.Id)).ToArray() });
                case ["devices"] when method == HttpMethod.Post:
                {
                    if (AdministratorOnly() is { } forbidden) return forbidden;
                    var name = string.Join(' ', (Text(body, "name") ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
                    if (name.Length is 0 or > 60 || name.Any(char.IsControl)) return Problem(HttpStatusCode.BadRequest, "invalid-input");
                    var activateThis = body.TryGetProperty("activateThisComputer", out var flag) && flag.ValueKind == JsonValueKind.True;
                    if (activateThis && DeviceFromHeader(request) is not null) return Problem(HttpStatusCode.Conflict, "device-already-active");
                    var device = AddDevice(unitId, name, "pending");
                    if (activateThis)
                    {
                        device.Status = "active"; device.Secret = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"); device.ActivatedAt = DateTimeOffset.UtcNow;
                        return Created(new { device = ToApi(device), credential = new DeviceCredential(device.Id, device.Secret) });
                    }
                    device.Code = NewCode(); device.CodeExpiresAt = DateTimeOffset.UtcNow.AddMinutes(15);
                    return Created(new { device = ToApi(device), activationCode = device.Code, codeExpiresAt = device.CodeExpiresAt });
                }
                case ["devices", var deviceText, "activation-code"] when method == HttpMethod.Post:
                {
                    if (AdministratorOnly() is { } forbidden) return forbidden;
                    var device = Devices.FirstOrDefault(item => item.Id == Guid.Parse(deviceText) && item.UnitId == unitId); if (device is null) return Problem(HttpStatusCode.NotFound, "not-found");
                    if (device.Status == "revoked") return Problem(HttpStatusCode.Conflict, "conflict");
                    device.Code = NewCode(); device.CodeExpiresAt = DateTimeOffset.UtcNow.AddMinutes(15);
                    return Reply(new ActivationCodeInfo(device.Code, device.CodeExpiresAt.Value));
                }
                case ["devices", var deviceText, "revoke"] when method == HttpMethod.Post:
                {
                    if (AdministratorOnly() is { } forbidden) return forbidden;
                    if (!(body.TryGetProperty("confirmed", out var confirmed) && confirmed.ValueKind == JsonValueKind.True)) return Problem(HttpStatusCode.BadRequest, "invalid-input");
                    var device = Devices.FirstOrDefault(item => item.Id == Guid.Parse(deviceText) && item.UnitId == unitId); if (device is null) return Problem(HttpStatusCode.NotFound, "not-found");
                    if (device.Status == "revoked") return Problem(HttpStatusCode.Conflict, "conflict");
                    RevokeDevice(device);
                    return Reply(new { device = ToApi(device) });
                }

                // ---- Atendimento ao público ----
                case ["profile"] when method == HttpMethod.Get:
                    return FailProfile ? Problem(HttpStatusCode.InternalServerError, "internal-error") : Reply(ProfileFor(unitId));
                case ["profile"] when method == HttpMethod.Put:
                {
                    if (AdministratorOnly() is { } forbidden) return forbidden;
                    ProfileBodies.Add(body.Clone());
                    var hours = new List<OpeningInterval>();
                    if (!body.TryGetProperty("openingHours", out var list) || list.ValueKind != JsonValueKind.Array) return Problem(HttpStatusCode.BadRequest, "invalid-input");
                    foreach (var item in list.EnumerateArray())
                    {
                        if (!item.TryGetProperty("weekday", out var weekday) || weekday.ValueKind != JsonValueKind.Number || weekday.GetInt32() is < 0 or > 6) return Problem(HttpStatusCode.BadRequest, "invalid-input");
                        if (!OpeningHoursText.TryParseTime(Text(item, "opens"), out var opens) || !OpeningHoursText.TryParseTime(Text(item, "closes"), out var closes) || closes <= opens) return Problem(HttpStatusCode.BadRequest, "invalid-input");
                        hours.Add(new OpeningInterval(weekday.GetInt32(), Text(item, "opens")!, Text(item, "closes")!));
                    }
                    foreach (var day in hours.GroupBy(item => item.Weekday))
                    {
                        var ordered = day.OrderBy(item => item.Opens, StringComparer.Ordinal).ToArray();
                        if (ordered.Length > 2 || (ordered.Length == 2 && string.CompareOrdinal(ordered[1].Opens, ordered[0].Closes) < 0)) return Problem(HttpStatusCode.BadRequest, "invalid-input");
                    }
                    var services = body.TryGetProperty("services", out var servicesValue) && servicesValue.ValueKind == JsonValueKind.String ? servicesValue.GetString()!.Trim() : null;
                    if (services is { Length: > 1000 } || (services?.Any(character => char.IsControl(character) && character != '\n') ?? false)) return Problem(HttpStatusCode.BadRequest, "invalid-input");
                    var before = ProfileFor(unitId);
                    profiles[unitId] = before with { Informed = true, OpeningHours = [.. hours.OrderBy(item => item.Weekday).ThenBy(item => item.Opens, StringComparer.Ordinal)], Services = string.IsNullOrEmpty(services) ? null : services, UpdatedAt = DateTimeOffset.UtcNow };
                    return Reply(profiles[unitId]);
                }
            }
            return null;
        }

        /// <summary>Atendimento da UBS: o salvo ou, sem nada gravado, "não informado"; as especialidades vêm da grade da fixture (ativas e com grade).</summary>
        private UnitProfile ProfileFor(Guid unitId)
        {
            var specialtyIds = Sched(unitId).Schedule.Select(row => row.SpecialtyId).Distinct().ToArray();
            var specialties = Specialties.Where(item => item.IsActive && specialtyIds.Contains(item.Id)).OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase).Select(item => new ProfileSpecialty(item.Id, item.Name)).ToArray();
            var saved = profiles.GetValueOrDefault(unitId) ?? new UnitProfile(unitId, false, [], null, [], null);
            return saved with { Specialties = specialties };
        }
    }
}
