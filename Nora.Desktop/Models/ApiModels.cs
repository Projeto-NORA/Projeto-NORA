using System.Text.Json.Serialization;
using Nora.Desktop.Services;

namespace Nora.Desktop.Models;

public sealed record AccountUser(Guid Id, string FullName, string Role, string Scope);
public sealed record LoginSession(string Token, DateTimeOffset ExpiresAt, AccountUser User);
public sealed record AddressInfo(string Cep, string Street, string? Number, string? Complement, string? Neighborhood, string City, string State, string? Ibge = null)
{
    public string Display => $"{Street}{(string.IsNullOrWhiteSpace(Number) ? "" : ", " + Number)} · {Neighborhood ?? "Bairro não informado"} · {City}/{State}";
}
public sealed record LightningCohort(int Days, long OpenSlots, long AcceptedOffers, long RegisteredSlots, long CreatedOffers, long DeclinedOffers, long ExpiredOffers, long PendingOffers, long CancelledOffers);
public sealed record MunicipalSummary(long Patients, long WaitingPatients, long OpenSlots, long AcceptedOffers, long? RegisteredSlots = null, long? CreatedOffers = null, long? DeclinedOffers = null, long? ExpiredOffers = null, long? PendingOffers = null, long? CancelledOffers = null, LightningCohort[]? LightningCohorts = null);

public sealed record HealthUnit(Guid Id, string Name, string Region, string Address, double Latitude, double Longitude)
{
    /// <summary>Nome e região: unidades com o mesmo nome continuam distinguíveis no seletor.</summary>
    [JsonIgnore] public string SelectorLabel => string.IsNullOrWhiteSpace(Region) ? Name : $"{Name} · {Region}";
}

public sealed record StaffAccount(Guid Id, string FullName, string Role);

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
