using System.Text.Json.Serialization;
using Nora.Desktop.Services;

namespace Nora.Desktop.Models;

public sealed record AccountUser(Guid Id, string FullName, string Role, string Scope, bool MustChangePassword = false);
public sealed record LoginSession(string Token, DateTimeOffset ExpiresAt, AccountUser User);
public sealed record AddressInfo(string Cep, string Street, string? Number, string? Complement, string? Neighborhood, string City, string State, string? Ibge = null)
{
    public string Display => $"{Street}{(string.IsNullOrWhiteSpace(Number) ? "" : ", " + Number)} · {Neighborhood ?? "Bairro não informado"} · {City}/{State}";
}
public sealed record LightningCohort(int Days, long OpenSlots, long AcceptedOffers, long RegisteredSlots, long CreatedOffers, long DeclinedOffers, long ExpiredOffers, long PendingOffers, long CancelledOffers);
public sealed record MunicipalSummary(long Patients, long WaitingPatients, long OpenSlots, long AcceptedOffers, long? RegisteredSlots = null, long? CreatedOffers = null, long? DeclinedOffers = null, long? ExpiredOffers = null, long? PendingOffers = null, long? CancelledOffers = null, LightningCohort[]? LightningCohorts = null, long? InactivePatients = null, long? WaitingEntries = null, long? UnitsWithoutDatabase = null);

public sealed record HealthUnit(Guid Id, string Name, string Region, string Address, double Latitude, double Longitude, string? DatabaseState = null)
{
    /// <summary>A base da UBS ainda não foi criada (só a API nova informa o estado; sem ele nada é afirmado).</summary>
    [JsonIgnore] public bool DatabasePending => DatabaseState == "pending";
    [JsonIgnore] public string DatabaseLabel => DatabaseState switch { "pending" => "Base não criada", "ready" => "Base pronta", _ => "" };
    /// <summary>Nome e região: unidades com o mesmo nome continuam distinguíveis no seletor.</summary>
    [JsonIgnore] public string SelectorLabel => string.IsNullOrWhiteSpace(Region) ? Name : $"{Name} · {Region}";
}

public sealed record StaffAccount(Guid Id, string FullName, string Role, string? Email = null, bool IsActive = true, bool MustChangePassword = false)
{
    [JsonIgnore] public string RoleDisplay => Role switch { "administrator" => "Administrador", "attendant" => "Atendente", var other => other };
    [JsonIgnore] public string StatusDisplay => IsActive ? "Ativa" : "Desativada";
    [JsonIgnore] public string PendingDisplay => MustChangePassword ? "Pendente" : "Não";
    [JsonIgnore] public string EmailDisplay => string.IsNullOrWhiteSpace(Email) ? "—" : Email;
    [JsonIgnore] public string SelectorLabel => IsActive ? FullName : FullName + " (desativada)";
}

/// <summary>Ação registrada no histórico da gestão; sem CPF nem e-mail.</summary>
public sealed record HistoryEvent(DateTimeOffset OccurredAt, string Kind, string? ActorName = null, Guid? UnitId = null, string? UnitName = null, string? AccountName = null)
{
    [JsonIgnore] public string OccurredAtDisplay => OccurredAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss");
    [JsonIgnore] public string ActionDisplay => Kind switch
    {
        "staff-granted" => "Concedeu acesso a uma UBS",
        "staff-revoked" => "Revogou acesso a uma UBS",
        "registry-read" => "Consultou os cadastros da UBS",
        "appointment-read" => "Consultou a agenda da UBS",
        "staff-created" => "Criou uma conta OBS",
        "staff-deactivated" => "Desativou uma conta OBS",
        "staff-reactivated" => "Reativou uma conta OBS",
        "staff-password-reset" => "Redefiniu a senha de uma conta OBS",
        "unit-database-created" => "Criou a base de uma UBS",
        _ => "Ação registrada"
    };
    [JsonIgnore] public string ActorDisplay => string.IsNullOrWhiteSpace(ActorName) ? "Sistema" : ActorName;
    [JsonIgnore] public string AccountDisplay => string.IsNullOrWhiteSpace(AccountName) ? "—" : AccountName;
    [JsonIgnore] public string UnitDisplay => string.IsNullOrWhiteSpace(UnitName) ? "—" : UnitName;
}

public sealed record UnitPatientRecord(string Cpf, string FullName, string Email, string? Phone = null, AddressInfo? Address = null, bool IsActive = true, bool IsOnline = false, DateTimeOffset? CreatedAt = null)
{
    public string ActiveDisplay => IsActive ? "Ativo nesta UBS" : "Desativado nesta UBS";
    public string FormattedCpf => BrazilianDataValidator.FormatCpf(Cpf);
    public string PhoneDisplay => string.IsNullOrWhiteSpace(Phone) ? "Telefone não informado" : Phone;
    public string PresenceDisplay => IsOnline ? "Online no aplicativo" : "Offline no aplicativo";
    public string CreatedAtDisplay => CreatedAt?.ToLocalTime().ToString("dd/MM/yyyy HH:mm") ?? "Data não informada";
}

public sealed record AppointmentInfo(Guid Id, string Cpf, string PatientName, string Specialty, string Professional, DateTimeOffset StartsAt, DateTimeOffset EndsAt, string Status)
{
    public string TimeDisplay => $"{StartsAt.ToLocalTime():dd/MM/yyyy HH:mm} – {EndsAt.ToLocalTime():HH:mm}";
    public string FormattedCpf => BrazilianDataValidator.FormatCpf(Cpf);
    public string PatientDisplay => $"{PatientName} · CPF {FormattedCpf}";
    public string StatusDisplay => Status switch { "scheduled" => "Agendada", "called" => "Chamada", "completed" => "Concluída", "missed" => "Falta", "cancelled" => "Cancelada", _ => Status };
}

public sealed record UnitWaitingEntry(long Id, string Cpf, string PatientName, string Specialty, DateTimeOffset EnteredAt, string Status)
{
    /// <summary>Posição entre as entradas ativas da mesma especialidade; calculada pelo cliente a cada leitura.</summary>
    [JsonIgnore] public int Position { get; init; }
    /// <summary>Tempo desde a entrada, calculado na leitura (sem temporizador próprio).</summary>
    [JsonIgnore] public string WaitDisplay { get; init; } = "—";
    [JsonIgnore] public bool IsActive => Status is "waiting" or "offered";
    [JsonIgnore] public string PositionDisplay => Position > 0 ? $"{Position}º" : "—";
    [JsonIgnore] public string FormattedCpf => BrazilianDataValidator.FormatCpf(Cpf);
    public string EnteredAtDisplay => EnteredAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm");
    public string StatusDisplay => Status switch { "waiting" => "Aguardando", "offered" => "Oferta em andamento", "completed" => "Concluída", "cancelled" => "Cancelada", _ => Status };
}
public sealed record UnitLightningSlot(Guid Id, Guid AppointmentId, string Specialty, string Professional, DateTimeOffset StartsAt, DateTimeOffset EndsAt, string Status, DateTimeOffset CreatedAt)
{
    public string TimeDisplay => $"{StartsAt.ToLocalTime():dd/MM/yyyy HH:mm} – {EndsAt.ToLocalTime():HH:mm}";
    public string CreatedAtDisplay => CreatedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm");
    public string StatusDisplay => Status switch { "open" => "Aberta", "offered" => "Ofertada", "accepted" => "Aceita", "closed" => "Encerrada", _ => Status };
}
