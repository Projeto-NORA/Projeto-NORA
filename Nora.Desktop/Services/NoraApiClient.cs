using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Nora.Desktop.Models;

namespace Nora.Desktop.Services;

public sealed class NoraApiException(string message, bool unauthorized = false, string? code = null, int status = 0) : Exception(message)
{
    public bool Unauthorized { get; } = unauthorized;
    /// <summary>Código fixo do problem+json (somente valores conhecidos); permite à tela reagir a casos específicos, como a troca obrigatória de senha.</summary>
    public string? Code { get; } = code;
    /// <summary>Status HTTP da resposta (0 quando o erro não veio de uma resposta, como falha de conexão); a tela usa 409 para avisar no campo.</summary>
    public int Status { get; } = status;
}

/// <summary>HTTP boundary. Session tokens remain in memory and never enter preferences.</summary>
public sealed class NoraApiClient : IDisposable
{
    // Valores fixos do campo "code" do problem+json; qualquer outro texto do servidor é ignorado.
    private static readonly HashSet<string> ErrorCodes = ["invalid-input", "unauthorized", "forbidden", "not-found", "conflict", "too-many-requests", "internal-error", "service-unavailable", "invalid-current-password", "password-change-required", "database-provisioning-failed", "unit-database-outdated", "catalog-required", "slot-unavailable", "patient-overlap", "specialty-limit", "booking-window", "confirmation-window", "family-link-closed", "dependent-inactive", "device-required", "invalid-activation-code", "device-already-active", "device-revoked", "queue-empty", "call-next-too-late", "approval-expired", "other-unit-overlap"];
    private readonly HttpClient http;
    private Uri? endpoint;
    private string? token;
    private bool disposed;
    // Credencial do computador do OBS: só vai a requisições do mesmo endereço de API para o qual foi ativada, nunca a outro servidor.
    private string? deviceHeader;
    private string? deviceEndpoint;
    public NoraApiClient(HttpMessageHandler? handler = null)
    {
        http = new HttpClient(handler ?? new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(20) };
    }
    public static Uri ValidateEndpoint(string address)
    {
        if (!Uri.TryCreate(address.Trim(), UriKind.Absolute, out var uri)
            || (uri.Scheme != "https" && !(uri.Scheme == "http" && uri.IsLoopback))
            || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new NoraApiException("Informe o endereço HTTPS do servidor em \"Servidor (avançado)\" (na tela de entrada) ou em Configurações. HTTP é permitido somente no computador local para desenvolvimento.");
        return new Uri(uri.AbsoluteUri.TrimEnd('/') + "/");
    }
    public void Configure(string address)
    {
        endpoint = ValidateEndpoint(address);
        ClearSession();
    }
    /// <summary>Endereço normalizado da API configurada (com a barra final) ou nulo antes de configurar.</summary>
    public string? EndpointAddress => endpoint?.AbsoluteUri;
    /// <summary>Define (ou retira, com nulo) a credencial do computador e o endereço de API para o qual ela vale. A Secretaria nunca chama isto.</summary>
    public void SetDeviceCredential(string? credentialEndpoint, DeviceCredential? credential)
    {
        if (credential is null || string.IsNullOrWhiteSpace(credentialEndpoint)) { deviceHeader = null; deviceEndpoint = null; return; }
        deviceHeader = credential.DeviceId.ToString("N") + "." + credential.Secret;
        deviceEndpoint = credentialEndpoint;
    }
    /// <summary>A requisição seguinte leva o cabeçalho do computador (há credencial e o endereço configurado é o dela).</summary>
    public bool SendsDeviceCredential => deviceHeader is not null && endpoint is not null && string.Equals(deviceEndpoint, endpoint.AbsoluteUri, StringComparison.Ordinal);
    public async Task<LoginSession> LoginAsync(string scope, string identifier, string password, bool keepSignedIn = false)
    {
        ClearSession();
        // "Manter conectado" só existe no OBS; o corpo dos demais perfis continua exatamente o de antes.
        object body = scope == "obs" && keepSignedIn ? new { scope, identifier, password, keepSignedIn } : new { scope, identifier, password };
        var session = await SendAsync<LoginSession>(HttpMethod.Post, "api/auth/login", body, false);
        if (disposed) throw new NoraApiException("O aplicativo foi fechado. Abra-o para entrar novamente.");
        if (string.IsNullOrWhiteSpace(session.Token) || session.ExpiresAt <= DateTimeOffset.UtcNow || session.User == null || session.User.Scope != scope)
            throw new NoraApiException("A API retornou uma sessão inválida. Tente entrar novamente.");
        token = session.Token;
        return session;
    }
    /// <summary>Retoma uma sessão guardada pelo "Manter conectado"; a API a confere na primeira requisição (GET api/auth/me).</summary>
    public void RestoreSession(string savedToken, DateTimeOffset expiresAt)
    {
        if (string.IsNullOrWhiteSpace(savedToken) || expiresAt <= DateTimeOffset.UtcNow) throw new NoraApiException("A sessão salva venceu. Entre com a senha.");
        token = savedToken;
    }
    public Task<AccountUser> MeAsync() => SendAsync<AccountUser>(HttpMethod.Get, "api/auth/me");

    // Computadores do OBS: a ativação e a conferência são públicas (só o cabeçalho do computador); a gestão é do administrador da UBS.
    public Task<DeviceSummary> CurrentDeviceAsync(CancellationToken cancellation = default) => SendAsync<DeviceSummary>(HttpMethod.Get, "api/obs/devices/current", authenticated: false, cancellation: cancellation);
    public Task<ActivatedDevice> ActivateDeviceAsync(string code) => SendAsync<ActivatedDevice>(HttpMethod.Post, "api/obs/devices/activate", new { code }, authenticated: false);
    public async Task<ObsDevice[]> ObsDevicesAsync(Guid unitId, CancellationToken cancellation = default) => (await SendAsync<ObsDeviceList>(HttpMethod.Get, $"api/obs/units/{unitId}/devices", cancellation: cancellation)).Devices;
    public Task<CreatedObsDevice> CreateObsDeviceAsync(Guid unitId, string name, bool activateThisComputer) => SendAsync<CreatedObsDevice>(HttpMethod.Post, $"api/obs/units/{unitId}/devices", new { name, activateThisComputer });
    public Task<ActivationCodeInfo> IssueDeviceCodeAsync(Guid unitId, Guid id) => SendAsync<ActivationCodeInfo>(HttpMethod.Post, $"api/obs/units/{unitId}/devices/{id}/activation-code", new { });
    public Task<RevokedObsDevice> RevokeDeviceAsync(Guid unitId, Guid id) => SendAsync<RevokedObsDevice>(HttpMethod.Post, $"api/obs/units/{unitId}/devices/{id}/revoke", new { confirmed = true });

    // Atendimento ao público (horário, serviços), aprovação das vagas reaproveitadas e "Chamar próximo".
    public Task<UnitProfile> UnitProfileAsync(Guid unitId, CancellationToken cancellation = default) => SendAsync<UnitProfile>(HttpMethod.Get, $"api/obs/units/{unitId}/profile", cancellation: cancellation);
    public Task<UnitProfile> SaveUnitProfileAsync(Guid unitId, UnitProfileDraft draft) => SendAsync<UnitProfile>(HttpMethod.Put, $"api/obs/units/{unitId}/profile", draft);
    public Task<SlotApprovalState> SetSlotApprovalAsync(Guid unitId, bool enabled) => SendAsync<SlotApprovalState>(HttpMethod.Put, $"api/obs/units/{unitId}/slot-approval", new { enabled, confirmed = true });
    public Task<UnitLightningSlot> ApproveLightningSlotAsync(Guid unitId, Guid slotId) => SendAsync<UnitLightningSlot>(HttpMethod.Post, $"api/obs/units/{unitId}/lightning-slots/{slotId}/approve", new { confirmed = true });
    public Task<UnitLightningSlot> DiscardLightningSlotAsync(Guid unitId, Guid slotId) => SendAsync<UnitLightningSlot>(HttpMethod.Post, $"api/obs/units/{unitId}/lightning-slots/{slotId}/discard", new { confirmed = true });
    /// <summary>1º da fila que pode receber. Com <paramref name="professionalId"/> e <paramref name="startsAt"/> (o horário escolhido) o servidor usa o mesmo critério da chamada e pula quem já tem consulta que cruze esse horário.</summary>
    public async Task<NextInQueue?> NextInQueueAsync(Guid unitId, Guid specialtyId, Guid? professionalId = null, DateTimeOffset? startsAt = null, CancellationToken cancellation = default) =>
        (await SendAsync<NextInQueueResult>(HttpMethod.Get, $"api/obs/units/{unitId}/waiting-list/next?specialtyId={specialtyId}" + (professionalId is { } professional && startsAt is { } start ? $"&professionalId={professional}&startsAt={Uri.EscapeDataString(start.ToUniversalTime().ToString("O"))}" : ""), cancellation: cancellation)).Next;
    public Task<CallNextResult> CallNextAsync(Guid unitId, Guid specialtyId, Guid professionalId, DateTimeOffset startsAt) =>
        SendAsync<CallNextResult>(HttpMethod.Post, $"api/obs/units/{unitId}/waiting-list/call-next", new { specialtyId, professionalId, startsAt, confirmed = true });
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
    public Task<AppointmentInfo> CreateAppointmentAsync(Guid unitId, string cpf, string specialty, string professional, DateTimeOffset startsAt, DateTimeOffset endsAt, Guid? professionalId = null, Guid? specialtyId = null) => SendAsync<AppointmentInfo>(HttpMethod.Post, $"api/obs/units/{unitId}/appointments", new { cpf, specialty, professional, startsAt, endsAt, professionalId, specialtyId });

    // Catálogo da UBS: especialidades da rede (leitura), profissionais, consultórios, grade, bloqueios e horários livres. A escrita é do administrador da UBS.
    private static string Period(DateTimeOffset from, DateTimeOffset to) => $"from={Uri.EscapeDataString(from.ToString("O"))}&to={Uri.EscapeDataString(to.ToString("O"))}";
    public async Task<Specialty[]> ObsSpecialtiesAsync(CancellationToken cancellation = default) => (await SendAsync<SpecialtyList>(HttpMethod.Get, "api/obs/specialties", cancellation: cancellation)).Specialties;
    public async Task<Professional[]> ProfessionalsAsync(Guid unitId, CancellationToken cancellation = default) => (await SendAsync<ProfessionalList>(HttpMethod.Get, $"api/obs/units/{unitId}/professionals", cancellation: cancellation)).Professionals;
    public Task<Professional> CreateProfessionalAsync(Guid unitId, string fullName, Guid[] specialtyIds, string? councilAcronym, string? councilNumber, string? councilState) =>
        SendAsync<Professional>(HttpMethod.Post, $"api/obs/units/{unitId}/professionals", new { fullName, specialtyIds, councilAcronym, councilNumber, councilState });
    public Task<Professional> UpdateProfessionalAsync(Guid unitId, Guid id, string fullName, Guid[] specialtyIds, string? councilAcronym, string? councilNumber, string? councilState) =>
        SendAsync<Professional>(HttpMethod.Put, $"api/obs/units/{unitId}/professionals/{id}", new { fullName, specialtyIds, councilAcronym, councilNumber, councilState });
    public Task<Professional> SetProfessionalActiveAsync(Guid unitId, Guid id, bool isActive) => SendAsync<Professional>(HttpMethod.Patch, $"api/obs/units/{unitId}/professionals/{id}/active", new { isActive });
    public async Task<Room[]> RoomsAsync(Guid unitId, CancellationToken cancellation = default) => (await SendAsync<RoomList>(HttpMethod.Get, $"api/obs/units/{unitId}/rooms", cancellation: cancellation)).Rooms;
    public Task<Room> CreateRoomAsync(Guid unitId, string name) => SendAsync<Room>(HttpMethod.Post, $"api/obs/units/{unitId}/rooms", new { name });
    public Task<Room> RenameRoomAsync(Guid unitId, Guid id, string name) => SendAsync<Room>(HttpMethod.Put, $"api/obs/units/{unitId}/rooms/{id}", new { name });
    public Task<Room> SetRoomActiveAsync(Guid unitId, Guid id, bool isActive) => SendAsync<Room>(HttpMethod.Patch, $"api/obs/units/{unitId}/rooms/{id}/active", new { isActive });
    public async Task<ScheduleTemplate[]> ScheduleAsync(Guid unitId, CancellationToken cancellation = default) => (await SendAsync<ScheduleList>(HttpMethod.Get, $"api/obs/units/{unitId}/schedule", cancellation: cancellation)).Schedule;
    public Task<ScheduleTemplate> CreateScheduleAsync(Guid unitId, ScheduleDraft draft) => SendAsync<ScheduleTemplate>(HttpMethod.Post, $"api/obs/units/{unitId}/schedule", draft);
    public Task<ScheduleTemplate> UpdateScheduleAsync(Guid unitId, Guid id, ScheduleDraft draft) => SendAsync<ScheduleTemplate>(HttpMethod.Put, $"api/obs/units/{unitId}/schedule/{id}", draft);
    public Task<object> DeleteScheduleAsync(Guid unitId, Guid id) => SendAsync<object>(HttpMethod.Delete, $"api/obs/units/{unitId}/schedule/{id}");
    public async Task<ScheduleBlock[]> ScheduleBlocksAsync(Guid unitId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellation = default) =>
        (await SendAsync<ScheduleBlockList>(HttpMethod.Get, $"api/obs/units/{unitId}/schedule-blocks?{Period(from, to)}", cancellation: cancellation)).Blocks;
    public Task<ScheduleBlock> CreateScheduleBlockAsync(Guid unitId, Guid? professionalId, DateTimeOffset startsAt, DateTimeOffset endsAt, string reason) =>
        SendAsync<ScheduleBlock>(HttpMethod.Post, $"api/obs/units/{unitId}/schedule-blocks", new { professionalId, startsAt, endsAt, reason });
    public Task<object> DeleteScheduleBlockAsync(Guid unitId, Guid id) => SendAsync<object>(HttpMethod.Delete, $"api/obs/units/{unitId}/schedule-blocks/{id}");
    public async Task<AvailableSlot[]> AvailabilityAsync(Guid unitId, DateTimeOffset from, DateTimeOffset to, Guid specialtyId, Guid? professionalId, CancellationToken cancellation = default)
    {
        var query = $"{Period(from, to)}&specialtyId={specialtyId}" + (professionalId is { } professional ? $"&professionalId={professional}" : "");
        return (await SendAsync<AvailabilityList>(HttpMethod.Get, $"api/obs/units/{unitId}/availability?{query}", cancellation: cancellation)).Slots;
    }
    public Task<ActiveRooms> ActiveRoomsAsync(Guid unitId, CancellationToken cancellation = default) => SendAsync<ActiveRooms>(HttpMethod.Get, $"api/obs/units/{unitId}/active-rooms", cancellation: cancellation);
    public async Task<AlertItem[]> UnitAlertsAsync(Guid unitId, CancellationToken cancellation = default) => (await SendAsync<AlertList>(HttpMethod.Get, $"api/obs/units/{unitId}/alerts", cancellation: cancellation)).Alerts;

    // Secretaria: lista de especialidades, metas, alertas da rede e leitura do catálogo de uma UBS.
    public async Task<Specialty[]> MunicipalSpecialtiesAsync(CancellationToken cancellation = default) => (await SendAsync<SpecialtyList>(HttpMethod.Get, "api/municipal/specialties", cancellation: cancellation)).Specialties;
    public Task<Specialty> CreateSpecialtyAsync(string name) => SendAsync<Specialty>(HttpMethod.Post, "api/municipal/specialties", new { name });
    public Task<Specialty> RenameSpecialtyAsync(Guid id, string name) => SendAsync<Specialty>(HttpMethod.Put, $"api/municipal/specialties/{id}", new { name });
    public Task<Specialty> SetSpecialtyActiveAsync(Guid id, bool isActive) => SendAsync<Specialty>(HttpMethod.Patch, $"api/municipal/specialties/{id}/active", new { isActive });
    public async Task<Goal[]> GoalsAsync(CancellationToken cancellation = default) => (await SendAsync<GoalList>(HttpMethod.Get, "api/municipal/goals", cancellation: cancellation)).Goals;
    public Task<Goal> SetGoalAsync(string metric, decimal target, Guid? unitId) => SendAsync<Goal>(HttpMethod.Put, $"api/municipal/goals/{Uri.EscapeDataString(metric)}", new { target, unitId });
    public Task<object> RemoveGoalAsync(string metric, Guid? unitId) => SendAsync<object>(HttpMethod.Delete, $"api/municipal/goals/{Uri.EscapeDataString(metric)}" + (unitId is { } id ? $"?unitId={id}" : ""));
    public async Task<AlertItem[]> NetworkAlertsAsync(Guid? unitId, string? region, CancellationToken cancellation = default)
    {
        var query = new List<string>();
        if (unitId is { } id) query.Add($"unitId={id}");
        if (!string.IsNullOrWhiteSpace(region)) query.Add($"region={Uri.EscapeDataString(region)}");
        return (await SendAsync<AlertList>(HttpMethod.Get, "api/municipal/alerts" + (query.Count > 0 ? "?" + string.Join('&', query) : ""), cancellation: cancellation)).Alerts;
    }
    public async Task<Professional[]> UnitProfessionalsAsync(Guid unitId, CancellationToken cancellation = default) => (await SendAsync<ProfessionalList>(HttpMethod.Get, $"api/municipal/units/{unitId}/professionals", cancellation: cancellation)).Professionals;
    public async Task<Room[]> UnitRoomsAsync(Guid unitId, CancellationToken cancellation = default) => (await SendAsync<RoomList>(HttpMethod.Get, $"api/municipal/units/{unitId}/rooms", cancellation: cancellation)).Rooms;
    public async Task<ScheduleTemplate[]> UnitScheduleAsync(Guid unitId, CancellationToken cancellation = default) => (await SendAsync<ScheduleList>(HttpMethod.Get, $"api/municipal/units/{unitId}/schedule", cancellation: cancellation)).Schedule;
    // Comunicados: a equipe lê os das UBS concedidas (cursor incremental) e marca como lido; a Secretaria publica, acompanha leituras e encerra.
    public Task<ReceivedAnnouncementPage> ObsAnnouncementsAsync(long? after, int limit = 100, CancellationToken cancellation = default) =>
        SendAsync<ReceivedAnnouncementPage>(HttpMethod.Get, $"api/obs/announcements?limit={limit}" + (after is { } cursor ? $"&after={cursor}" : ""), cancellation: cancellation);
    public Task<object> MarkObsAnnouncementReadAsync(Guid id) => SendAsync<object>(HttpMethod.Post, $"api/obs/announcements/{id}/read");
    public Task<AnnouncementSummaryPage> MunicipalAnnouncementsAsync(long? before, int limit = 50, CancellationToken cancellation = default) =>
        SendAsync<AnnouncementSummaryPage>(HttpMethod.Get, $"api/municipal/announcements?limit={limit}" + (before is { } cursor ? $"&before={cursor}" : ""), cancellation: cancellation);
    public Task<AnnouncementSummary> CreateAnnouncementAsync(AnnouncementDraft draft) => SendAsync<AnnouncementSummary>(HttpMethod.Post, "api/municipal/announcements", draft);
    public Task<object> CloseAnnouncementAsync(Guid id) => SendAsync<object>(HttpMethod.Post, $"api/municipal/announcements/{id}/close", new { confirmed = true });
    public Task<AnnouncementReads> AnnouncementReadsAsync(Guid id, CancellationToken cancellation = default) => SendAsync<AnnouncementReads>(HttpMethod.Get, $"api/municipal/announcements/{id}/reads", cancellation: cancellation);

    // Jornada do paciente (exames e vacinas): o CPF vai sempre no corpo, nunca na URL. A anulação é a correção; nada é apagado.
    public Task<HealthRecords> HealthRecordsAsync(Guid unitId, string cpf, bool includeVoided, CancellationToken cancellation = default) =>
        SendAsync<HealthRecords>(HttpMethod.Post, $"api/obs/units/{unitId}/health-records/query", new { cpf, includeVoided }, cancellation: cancellation);
    public Task<ExamInfo> CreateExamAsync(Guid unitId, string cpf, string name, string? note) => SendAsync<ExamInfo>(HttpMethod.Post, $"api/obs/units/{unitId}/health-records/exams", new { cpf, name, note });
    /// <summary><paramref name="performedOn"/> é "yyyy-MM-dd" e vai só com <c>done</c>.</summary>
    public Task<ExamInfo> SetExamStatusAsync(Guid unitId, Guid id, string status, string? performedOn) => SendAsync<ExamInfo>(HttpMethod.Patch, $"api/obs/units/{unitId}/health-records/exams/{id}/status", new { status, performedOn });
    public Task<ExamInfo> VoidExamAsync(Guid unitId, Guid id, string reason) => SendAsync<ExamInfo>(HttpMethod.Post, $"api/obs/units/{unitId}/health-records/exams/{id}/void", new { reason, confirmed = true });
    public Task<VaccineInfo> CreateVaccineAsync(Guid unitId, string cpf, string name, string dose, string appliedOn, string? note) =>
        SendAsync<VaccineInfo>(HttpMethod.Post, $"api/obs/units/{unitId}/health-records/vaccines", new { cpf, name, dose, appliedOn, note });
    public Task<VaccineInfo> VoidVaccineAsync(Guid unitId, Guid id, string reason) => SendAsync<VaccineInfo>(HttpMethod.Post, $"api/obs/units/{unitId}/health-records/vaccines/{id}/void", new { reason, confirmed = true });

    // Família: pedidos de vínculo do responsável com um dependente, decididos pela equipe da UBS do dependente.
    /// <summary>Teto de páginas de uma leitura da Família: protege a tela de uma lista sem fim; acima dele a leitura volta cortada e avisa.</summary>
    public const int FamilyPageMax = 50;
    /// <summary>Segue as páginas da lista da equipe pelo cursor (ordem requested_at, id) até a última ou até o teto de páginas.</summary>
    public async Task<FamilyLinkPages> FamilyLinksAsync(Guid unitId, string status, CancellationToken cancellation = default)
    {
        var links = new List<FamilyLink>();
        var path = $"api/obs/units/{unitId}/family-links?status={Uri.EscapeDataString(status)}&limit=200";
        string? after = null;
        for (var page = 0; page < FamilyPageMax; page++)
        {
            var result = await SendAsync<FamilyLinkList>(HttpMethod.Get, after is null ? path : $"{path}&after={Uri.EscapeDataString(after)}", cancellation: cancellation);
            links.AddRange(result.Links);
            if (!result.HasMore) return new([.. links], false);
            if (string.IsNullOrEmpty(result.Cursor)) break;
            after = result.Cursor;
        }
        return new([.. links], true);
    }
    public Task<FamilyLink> ApproveFamilyLinkAsync(Guid unitId, Guid id) => SendAsync<FamilyLink>(HttpMethod.Post, $"api/obs/units/{unitId}/family-links/{id}/approve", new { confirmed = true });
    public Task<FamilyLink> RejectFamilyLinkAsync(Guid unitId, Guid id, string? reason) => SendAsync<FamilyLink>(HttpMethod.Post, $"api/obs/units/{unitId}/family-links/{id}/reject", new { confirmed = true, reason });
    public Task<FamilyLink> EndFamilyLinkAsync(Guid unitId, Guid id) => SendAsync<FamilyLink>(HttpMethod.Post, $"api/obs/units/{unitId}/family-links/{id}/end", new { confirmed = true });

    /// <summary>Prévia da desativação do cadastro: quantas consultas futuras e entradas de fila seriam canceladas. O CPF vai no corpo; nada é gravado.</summary>
    public Task<DeactivationPreview> DeactivationPreviewAsync(Guid unitId, string cpf) => SendAsync<DeactivationPreview>(HttpMethod.Post, $"api/obs/units/{unitId}/registry/deactivation-preview", new { cpf });

    public Task<AppointmentInfo> ChangeAppointmentStatusAsync(Guid unitId, Guid id, string status, bool confirmed) => SendAsync<AppointmentInfo>(HttpMethod.Patch, $"api/obs/units/{unitId}/appointments/{id}/status", new { status, confirmed });
    /// <summary>Primeira página (todas as entradas ativas e as encerradas mais recentes) ou, com <paramref name="historyBefore"/>, só as encerradas mais antigas que o cursor.</summary>
    public Task<WaitingListPage> UnitWaitingListAsync(Guid unitId, string? historyBefore = null) =>
        SendAsync<WaitingListPage>(HttpMethod.Get, $"api/obs/units/{unitId}/waiting-list{HistoryQuery(historyBefore)}");
    public Task<UnitWaitingEntry> AddUnitWaitingEntryAsync(Guid unitId, string cpf, string specialty) => SendAsync<UnitWaitingEntry>(HttpMethod.Post, $"api/obs/units/{unitId}/waiting-list", new { cpf, specialty });
    public Task<object> CancelUnitWaitingEntryAsync(Guid unitId, long id) => SendAsync<object>(HttpMethod.Post, $"api/obs/units/{unitId}/waiting-list/{id}/cancel", new { confirmed = true });
    /// <summary>Mesmo formato da fila: vagas ativas mais as encerradas mais recentes, e <paramref name="historyBefore"/> para as mais antigas.</summary>
    public Task<LightningSlotPage> UnitLightningSlotsAsync(Guid unitId, string? historyBefore = null) =>
        SendAsync<LightningSlotPage>(HttpMethod.Get, $"api/obs/units/{unitId}/lightning-slots{HistoryQuery(historyBefore)}");
    private static string HistoryQuery(string? historyBefore) => string.IsNullOrEmpty(historyBefore) ? "" : "?historyBefore=" + Uri.EscapeDataString(historyBefore);
    public Task<UnitLightningSlot> CreateUnitLightningSlotAsync(Guid unitId, Guid appointmentId) => SendAsync<UnitLightningSlot>(HttpMethod.Post, $"api/obs/units/{unitId}/lightning-slots", new { appointmentId, confirmed = true });
    public Task<UnitSlotOffer[]> UnitSlotOffersAsync(Guid unitId, Guid slotId) => SendAsync<UnitSlotOffer[]>(HttpMethod.Get, $"api/obs/units/{unitId}/lightning-slots/{slotId}/offers");
    public Task<UnitMetrics> UnitMetricsAsync(Guid unitId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellation = default) =>
        SendAsync<UnitMetrics>(HttpMethod.Get, $"api/obs/units/{unitId}/metrics?from={Uri.EscapeDataString(from.ToString("O"))}&to={Uri.EscapeDataString(to.ToString("O"))}", cancellation: cancellation);
    public Task<UnitPatientRecord[]> UnitRegistryAsync(Guid unitId) => SendAsync<UnitPatientRecord[]>(HttpMethod.Get, $"api/municipal/units/{unitId}/registry");
    public Task<AppointmentInfo[]> UnitAppointmentsAsync(Guid unitId, DateTimeOffset from, DateTimeOffset to) => SendAsync<AppointmentInfo[]>(HttpMethod.Get, $"api/municipal/units/{unitId}/appointments?from={Uri.EscapeDataString(from.ToString("O"))}&to={Uri.EscapeDataString(to.ToString("O"))}");
    public Task<StaffAccount[]> StaffAsync() => SendAsync<StaffAccount[]>(HttpMethod.Get, "api/municipal/staff");
    public Task<StaffAccount> CreateStaffAsync(string fullName, string email, string role, string password) => SendAsync<StaffAccount>(HttpMethod.Post, "api/municipal/staff", new { fullName, email, password, role });
    public Task<object> SetStaffActiveAsync(Guid id, bool isActive) => SendAsync<object>(HttpMethod.Patch, $"api/municipal/staff/{id}/active", new { isActive, confirmed = true });
    public Task<object> ResetStaffPasswordAsync(Guid id, string password) => SendAsync<object>(HttpMethod.Post, $"api/municipal/staff/{id}/password", new { password, confirmed = true });
    public Task<HealthUnit> ProvisionUnitDatabaseAsync(Guid id) => SendAsync<HealthUnit>(HttpMethod.Post, $"api/municipal/units/{id}/database");
    public Task<object> ChangePasswordAsync(string currentPassword, string newPassword) => SendAsync<object>(HttpMethod.Post, "api/auth/password", new { currentPassword, newPassword });
    public Task<NetworkMetrics> MetricsAsync(DateTimeOffset from, DateTimeOffset to, Guid? unitId, string? region, string? specialty, CancellationToken cancellation = default)
    {
        var query = $"from={Uri.EscapeDataString(from.ToString("O"))}&to={Uri.EscapeDataString(to.ToString("O"))}";
        if (unitId is { } id) query += $"&unitId={id}";
        if (!string.IsNullOrWhiteSpace(region)) query += $"&region={Uri.EscapeDataString(region)}";
        if (!string.IsNullOrWhiteSpace(specialty)) query += $"&specialty={Uri.EscapeDataString(specialty)}";
        return SendAsync<NetworkMetrics>(HttpMethod.Get, "api/municipal/metrics?" + query, cancellation: cancellation);
    }
    /// <summary><paramref name="before"/> é o <c>cursor</c> opaco do último item da página anterior (a API o devolve em cada evento); ele não é interpretado aqui.</summary>
    public Task<HistoryEvent[]> HistoryAsync(DateTimeOffset? from, DateTimeOffset? to, Guid? unitId, int limit, string? before, CancellationToken cancellation = default)
    {
        var query = $"limit={limit}";
        if (from is { } start) query += $"&from={Uri.EscapeDataString(start.ToString("O"))}";
        if (to is { } end) query += $"&to={Uri.EscapeDataString(end.ToString("O"))}";
        if (unitId is { } id) query += $"&unitId={id}";
        if (!string.IsNullOrEmpty(before)) query += $"&before={Uri.EscapeDataString(before)}";
        return SendAsync<HistoryEvent[]>(HttpMethod.Get, "api/municipal/history?" + query, cancellation: cancellation);
    }
    public Task<HealthUnit[]> StaffUnitsAsync(Guid id) => SendAsync<HealthUnit[]>(HttpMethod.Get, $"api/municipal/staff/{id}/units");
    public Task<object> SetStaffUnitAsync(Guid staffId, Guid unitId, bool grant) => SendAsync<object>(grant ? HttpMethod.Put : HttpMethod.Delete, $"api/municipal/staff/{staffId}/units/{unitId}");
    public Task<MunicipalSummary> SummaryAsync(Guid? unitId = null, CancellationToken cancellation = default) => SendAsync<MunicipalSummary>(HttpMethod.Get, unitId is { } id ? $"api/municipal/summary?unitId={id}" : "api/municipal/summary", cancellation: cancellation);

    private async Task<T> SendAsync<T>(HttpMethod method, string path, object? body = null, bool authenticated = true, CancellationToken cancellation = default)
    {
        if (disposed) throw new NoraApiException("O aplicativo foi fechado. Abra-o para conectar novamente.");
        if (endpoint == null) throw new NoraApiException("Configure o endereço do servidor antes de conectar.");
        if (authenticated && token == null) throw new NoraApiException("Entre na sua conta para continuar.", true);
        try
        {
            using var request = new HttpRequestMessage(method, new Uri(endpoint, path));
            if (authenticated) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            if (SendsDeviceCredential) request.Headers.TryAddWithoutValidation("X-Nora-Device", deviceHeader);
            if (body != null) request.Content = JsonContent.Create(body);
            using var response = await http.SendAsync(request, cancellation);
            if (!response.IsSuccessStatusCode)
            {
                var unauthorized = response.StatusCode == HttpStatusCode.Unauthorized;
                // Só o 401 de uma requisição com sessão diz que a sessão acabou; o de uma requisição sem sessão (conferência do computador, senha errada no login) não pode derrubar a que acabou de abrir.
                if (unauthorized && authenticated) ClearSession();
                // Nunca exibe corpo arbitrário do servidor: a mensagem sai do status, do code conhecido e do contexto da operação.
                var code = await ReadErrorCodeAsync(response);
                throw new NoraApiException(ErrorMessage(method, path, response.StatusCode, code, authenticated), unauthorized, code, (int)response.StatusCode);
            }
            if (response.StatusCode == HttpStatusCode.NoContent && typeof(T) == typeof(object)) return (T)new object();
            // Só JSON é aceito como sucesso: HTML de proxy ou texto simples com status 200 vira erro tratado.
            if (!IsJson(response.Content.Headers.ContentType?.MediaType)) throw new NoraApiException("A API retornou dados em um formato inesperado.");
            return await response.Content.ReadFromJsonAsync<T>(cancellation) ?? throw new NoraApiException("A API retornou uma resposta incompleta.");
        }
        catch (HttpRequestException) { throw new NoraApiException("Não foi possível conectar ao servidor. Confira o endereço em \"Servidor (avançado)\" e a conexão."); }
        catch (IOException) { throw new NoraApiException("A conexão com a API foi interrompida. Tente novamente."); }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { throw; }
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
            "unauthorized" => authenticated ? "Sua sessão terminou. Entre novamente." : "E-mail ou senha não conferem.",
            "device-required" => "Este computador não está ativado para a sua UBS. Peça ao administrador da UBS para ativá-lo.",
            "invalid-activation-code" => "Código inválido, vencido ou já usado. Peça um novo código ao administrador da UBS.",
            "device-already-active" => "Este computador já está ativado. Revogue-o antes de ativá-lo para outra UBS.",
            "device-revoked" => "Este computador foi desativado pelo administrador da UBS ou a ativação não vale mais.",
            "queue-empty" => "Ninguém desta fila pode receber a oferta agora (sem conta no aplicativo, cadastro inativo, oferta pendente ou consulta no mesmo horário).",
            "call-next-too-late" => "Faltam menos de 10 minutos para esse horário. Escolha um horário mais tarde.",
            "approval-expired" => "Falta menos de 1 hora para a consulta; a vaga foi fechada sem ser ofertada.",
            "other-unit-overlap" => "O paciente já tem consulta neste horário em outra UBS. Escolha outro horário.",
            "forbidden" when IsAdministratorWrite(method, path) => "Somente o administrador da UBS altera o atendimento ao público, a aprovação das vagas e os computadores. Peça a um administrador da UBS.",
            "forbidden" => IsCatalogWrite(method, path)
                ? "Somente o administrador da UBS altera profissionais, consultórios, grade e bloqueios. Peça a um administrador da UBS."
                : path.StartsWith("api/obs/units/", StringComparison.Ordinal)
                ? "Sua conta não tem acesso a esta UBS. Peça a concessão à Secretaria."
                : "Esta conta não tem acesso a este aplicativo ou operação.",
            "password-change-required" => "Defina uma nova senha para continuar usando o sistema.",
            "invalid-current-password" => "A senha atual não confere.",
            "unit-database-outdated" => UnitBaseMessage,
            "catalog-required" => "Esta UBS marca consultas pelo catálogo: escolha a especialidade, o profissional e um horário livre da lista.",
            "slot-unavailable" when path.EndsWith("/approve", StringComparison.Ordinal) => "O horário já foi ocupado; a vaga foi fechada.",
            "slot-unavailable" => "Esse horário não está mais livre. Escolha outro horário da lista.",
            "patient-overlap" => "O paciente já tem uma consulta que cruza este horário. Escolha outro horário ou cancele a outra consulta.",
            "specialty-limit" => "O paciente já tem uma consulta futura marcada pelo aplicativo nesta especialidade.",
            "booking-window" => "Esse horário já começou ou passou. Escolha outro horário.",
            "confirmation-window" => "A presença é confirmada de 48 horas até 2 horas antes da consulta.",
            "family-link-closed" => "Este pedido já foi decidido, cancelado ou encerrado por outra pessoa. Atualize a lista.",
            "dependent-inactive" => "O cadastro do dependente está desativado nesta UBS. Reative o cadastro dele antes de aprovar o vínculo.",
            "database-provisioning-failed" => "Não foi possível criar a base de dados da unidade. Confirme se o usuário PostgreSQL da API tem permissão para criar bancos e tente novamente.",
            "conflict" => ConflictMessage(method, path),
            "not-found" => NotFoundMessage(path),
            "invalid-input" => InvalidInputMessage(path),
            "too-many-requests" => "Muitas tentativas. Aguarde e tente novamente.",
            "service-unavailable" => UnavailableMessage(path),
            _ => "Não foi possível concluir a operação. Verifique a API e tente novamente."
        };
    }

    /// <summary>Escrita do catálogo da UBS (profissionais, consultórios, grade e bloqueios): só o administrador da UBS pode.</summary>
    private static bool IsCatalogWrite(HttpMethod method, string path) => method != HttpMethod.Get && path.StartsWith("api/obs/units/", StringComparison.Ordinal)
        && (path.Contains("/professionals", StringComparison.Ordinal) || path.Contains("/rooms", StringComparison.Ordinal) || path.Contains("/schedule", StringComparison.Ordinal));

    /// <summary>Escrita que só o administrador da UBS faz e que não é do catálogo: atendimento ao público, aprovação das vagas e computadores do OBS.</summary>
    private static bool IsAdministratorWrite(HttpMethod method, string path) => method != HttpMethod.Get && path.StartsWith("api/obs/units/", StringComparison.Ordinal)
        && (path.EndsWith("/profile", StringComparison.Ordinal) || path.EndsWith("/slot-approval", StringComparison.Ordinal) || path.Contains("/devices", StringComparison.Ordinal));

    private static string NotFoundMessage(string path)
    {
        if (path.Contains("/devices/", StringComparison.Ordinal) || path.EndsWith("/devices", StringComparison.Ordinal)) return "O computador não foi encontrado nesta UBS. Atualize a lista de computadores.";
        if (path.Contains("/lightning-slots/", StringComparison.Ordinal)) return "A vaga não foi encontrada nesta UBS. Atualize as vagas.";
        if (path.StartsWith("api/address/cep/", StringComparison.Ordinal)) return "CEP não encontrado. Confira os oito dígitos.";
        if (path.StartsWith("api/municipal/staff/", StringComparison.Ordinal)) return "A conta não foi encontrada ou não é uma conta OBS. Atualize a lista.";
        if (path.StartsWith("api/municipal/specialties/", StringComparison.Ordinal)) return "A especialidade não foi encontrada. Atualize a lista.";
        if (path.StartsWith("api/municipal/goals/", StringComparison.Ordinal)) return "Essa meta não existe mais. Atualize a lista de metas.";
        if (path.StartsWith("api/municipal/announcements", StringComparison.Ordinal)) return "O comunicado não foi encontrado. Atualize a lista.";
        if (path.StartsWith("api/obs/announcements", StringComparison.Ordinal)) return "Este comunicado não está mais disponível. Atualize a lista.";
        if (path.Contains("/family-links", StringComparison.Ordinal)) return "O pedido não foi encontrado nesta UBS. Atualize a lista.";
        if (path.EndsWith("/health-records/query", StringComparison.Ordinal) || path.EndsWith("/health-records/exams", StringComparison.Ordinal) || path.EndsWith("/health-records/vaccines", StringComparison.Ordinal))
            return "O paciente não tem cadastro ativo nesta UBS. Atualize a lista de pacientes.";
        if (path.Contains("/health-records/", StringComparison.Ordinal)) return "O registro não foi encontrado. Atualize a lista de exames e vacinas.";
        if (path.EndsWith("/registry/deactivation-preview", StringComparison.Ordinal)) return "Não há cadastro deste paciente nesta UBS. Atualize a lista de pacientes.";
        if (path.Contains("/schedule", StringComparison.Ordinal) || path.Contains("/professionals/", StringComparison.Ordinal) || path.Contains("/rooms/", StringComparison.Ordinal))
            return "O item não foi encontrado: outra pessoa pode tê-lo removido. Atualize a tela.";
        return "Registro não encontrado. Atualize os dados e tente novamente.";
    }

    private static string InvalidInputMessage(string path)
    {
        if (path.Contains("/devices", StringComparison.Ordinal)) return "Confira o nome do computador: de 1 a 60 caracteres, sem caracteres de controle.";
        if (path.Contains("historyBefore=", StringComparison.Ordinal)) return "O ponto de partida do histórico não vale mais. Atualize a lista e peça o histórico de novo.";
        if (path.EndsWith("/profile", StringComparison.Ordinal)) return "Confira o atendimento ao público: até dois intervalos por dia, com o fim depois do início e sem sobreposição, e serviços de até 1.000 caracteres.";
        if (path.Contains("/waiting-list/call-next", StringComparison.Ordinal) || path.Contains("/waiting-list/next", StringComparison.Ordinal)) return "Confira a especialidade e o horário escolhidos para o Chamar próximo.";
        if (path == "api/auth/password") return "Confira a nova senha: use de 8 a 256 caracteres, diferente da atual.";
        if (path == "api/municipal/staff") return "Confira nome, e-mail, perfil e senha provisória (8 a 256 caracteres).";
        if (path.StartsWith("api/municipal/staff/", StringComparison.Ordinal) && path.EndsWith("/password", StringComparison.Ordinal)) return "A senha provisória precisa ter de 8 a 256 caracteres.";
        if (path.StartsWith("api/municipal/metrics", StringComparison.Ordinal) || path.StartsWith("api/municipal/history", StringComparison.Ordinal))
            return "Confira o período: o fim deve ser posterior ao início e o intervalo pode ter no máximo 366 dias.";
        if (path.StartsWith("api/municipal/specialties", StringComparison.Ordinal)) return "Confira o nome da especialidade: de 2 a 120 caracteres.";
        if (path.StartsWith("api/municipal/announcements", StringComparison.Ordinal)) return "Confira o comunicado: título de até 120 caracteres, texto de até 2.000, público, alcance e uma validade que ainda esteja no futuro.";
        if (path.StartsWith("api/obs/announcements", StringComparison.Ordinal)) return "Não foi possível ler os comunicados. Tente novamente.";
        if (path.Contains("/family-links", StringComparison.Ordinal)) return "Confira o motivo da recusa: de 1 a 280 caracteres, ou deixe em branco.";
        if (path.EndsWith("/void", StringComparison.Ordinal)) return "Informe o motivo da anulação, de 3 a 280 caracteres.";
        if (path.EndsWith("/status", StringComparison.Ordinal) && path.Contains("/health-records/", StringComparison.Ordinal)) return "Para marcar como realizado informe a data (de 01/01/1900 até hoje); para cancelar não informe data.";
        if (path.Contains("/health-records/", StringComparison.Ordinal)) return "Confira o nome (até 160 caracteres), a observação (até 280), a dose e a data (de 01/01/1900 até hoje).";
        if (path.EndsWith("/registry/deactivation-preview", StringComparison.Ordinal)) return "CPF inválido para a prévia da desativação.";
        if (path.StartsWith("api/municipal/goals/", StringComparison.Ordinal)) return "Confira o valor da meta: comparecimento e faltas de 0 a 100% e espera maior que zero.";
        if (path.Contains("/availability", StringComparison.Ordinal)) return "Não foi possível buscar horários: confira a especialidade e o período (até 92 dias).";
        if (path.EndsWith("/professionals", StringComparison.Ordinal) || path.Contains("/professionals/", StringComparison.Ordinal))
            return "Confira o profissional: nome de 2 a 160 caracteres, ao menos uma especialidade ativa e, se informar o conselho, sigla, número e UF juntos.";
        if (path.EndsWith("/rooms", StringComparison.Ordinal) || path.Contains("/rooms/", StringComparison.Ordinal)) return "Confira o nome do consultório: de 1 a 80 caracteres.";
        if (path.Contains("/schedule-blocks", StringComparison.Ordinal)) return "Confira o bloqueio: período válido e motivo de 1 a 120 caracteres.";
        if (path.EndsWith("/schedule", StringComparison.Ordinal) || path.Contains("/schedule/", StringComparison.Ordinal))
            return "Confira a grade: profissional ativo, especialidade dele, consultório ativo, horários que comportem a duração e vigência válida.";
        if (path.EndsWith("/appointments", StringComparison.Ordinal)) return "Confira o paciente, a especialidade, o profissional e o horário escolhido; o início precisa ser no futuro.";
        return "Confira os dados informados e tente novamente.";
    }

    private const string UnitBaseMessage = "A base de dados desta UBS está indisponível, não foi criada ou está desatualizada. Procure a Secretaria ou o suporte para prepará-la e tente novamente.";

    private static string UnavailableMessage(string path)
    {
        if (path.StartsWith("api/obs/units/", StringComparison.Ordinal)) return UnitBaseMessage;
        if (path.StartsWith("api/municipal/summary", StringComparison.Ordinal) || path.StartsWith("api/municipal/metrics", StringComparison.Ordinal))
            return "Uma base de unidade está indisponível no momento, por isso a rede não pôde ser consolidada. Os números exibidos podem estar desatualizados.";
        if (path.StartsWith("api/municipal/units/", StringComparison.Ordinal))
            return "A base desta unidade está indisponível no momento. Tente novamente em instantes.";
        return "O serviço ou a base de dados da unidade está indisponível no momento. Tente novamente em instantes.";
    }

    private static string ConflictMessage(HttpMethod method, string path)
    {
        var create = method == HttpMethod.Post;
        if (path.Contains("/devices/", StringComparison.Ordinal)) return "Este computador já foi revogado ou mudou de situação. Atualize a lista de computadores.";
        if (path.Contains("/lightning-slots/", StringComparison.Ordinal)) return "A vaga já foi decidida por outra pessoa. Atualize as vagas.";
        if (create && path == "api/municipal/staff") return "Já existe uma conta OBS com este e-mail.";
        if (path == "api/auth/password") return "Não foi possível trocar a senha agora. Entre novamente e tente de novo.";
        if (create && path.EndsWith("/registry", StringComparison.Ordinal)) return "Já existe um cadastro com este CPF nesta UBS.";
        if (path.StartsWith("api/municipal/specialties", StringComparison.Ordinal)) return "Já existe uma especialidade com este nome (maiúsculas, acentos e espaços não diferenciam).";
        if (path.StartsWith("api/municipal/announcements/", StringComparison.Ordinal) && path.EndsWith("/close", StringComparison.Ordinal)) return "Este comunicado já foi encerrado. Atualize a lista.";
        if (path.Contains("/health-records/exams/", StringComparison.Ordinal) && path.EndsWith("/status", StringComparison.Ordinal)) return "Este exame já foi alterado por outra pessoa: só um exame solicitado pode virar realizado ou cancelado. Atualize a lista.";
        if (path.Contains("/health-records/", StringComparison.Ordinal) && path.EndsWith("/void", StringComparison.Ordinal)) return "Este registro já foi anulado. Atualize a lista.";
        if (path.EndsWith("/professionals", StringComparison.Ordinal)) return "Já existe um profissional com este nome nesta UBS.";
        if (path.Contains("/professionals/", StringComparison.Ordinal)) return method == HttpMethod.Patch ? "A situação do profissional mudou. Atualize a lista." : "Não foi possível salvar: já existe um profissional com este nome, ou uma especialidade desmarcada ainda tem horários deste profissional na grade. Deixe essa especialidade marcada; se ela foi desativada pela Secretaria, use Editar profissional selecionado para recuperá-la.";
        if (path.EndsWith("/rooms", StringComparison.Ordinal) || path.Contains("/rooms/", StringComparison.Ordinal)) return "Já existe um consultório com este nome nesta UBS.";
        if (path.EndsWith("/schedule", StringComparison.Ordinal) || path.Contains("/schedule/", StringComparison.Ordinal))
            return "Sobreposição: o profissional ou o consultório já tem horário neste dia da semana que cruza o intervalo informado (encostar não é sobrepor). Ajuste o horário, o dia ou a vigência. Se nada cruza, o profissional ou o consultório pode ter sido desativado por outra pessoa: atualize as configurações.";
        if (create && path.EndsWith("/appointments", StringComparison.Ordinal)) return "Horário em conflito: o paciente ou o profissional já tem consulta ou vaga neste intervalo.";
        if (path.Contains("/appointments/", StringComparison.Ordinal) && path.EndsWith("/status", StringComparison.Ordinal)) return "A consulta já foi alterada por outra pessoa. Atualize a agenda.";
        if (create && path.EndsWith("/waiting-list", StringComparison.Ordinal)) return "Este paciente já está na fila desta especialidade.";
        if (create && path.EndsWith("/lightning-slots", StringComparison.Ordinal)) return "Já existe uma vaga para esta consulta, talvez aguardando aprovação. Atualize as vagas.";
        if (path.Contains("/waiting-list/", StringComparison.Ordinal) && path.EndsWith("/cancel", StringComparison.Ordinal)) return "A entrada já foi encerrada. Atualize a fila.";
        return "O registro já existe ou sua situação mudou. Atualize a lista.";
    }
    public void Dispose() { disposed = true; ClearSession(); http.Dispose(); }
}
