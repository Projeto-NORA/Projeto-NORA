namespace Nora.Desktop.Models;

public sealed record AccountUser(Guid Id, string FullName, string Role, string Scope);
public sealed record LoginSession(string Token, DateTimeOffset ExpiresAt, AccountUser User);
public sealed record AddressInfo(string Cep, string Street, string? Number, string? Complement, string? Neighborhood, string City, string State, string? Ibge = null)
{
    public string Display => $"{Street}{(string.IsNullOrWhiteSpace(Number) ? "" : ", " + Number)} · {Neighborhood ?? "Bairro não informado"} · {City}/{State}";
}
public sealed record PatientAccount(Guid Id, string FullName, string Cpf, string Email, string? Phone = null, bool IsOnline = false, bool IsActive = true, DateTimeOffset? CreatedAt = null, AddressInfo? Address = null)
{
    public string MaskedCpf => Cpf.Length >= 2 ? "***.***.***-" + Cpf[^2..] : "***";
    public string FormattedCpf => Cpf.Length == 11 ? $"{Cpf[..3]}.{Cpf[3..6]}.{Cpf[6..9]}-{Cpf[9..]}" : Cpf;
    public string ActiveDisplay => IsActive ? "Ativo" : "Desativado";
    public string PresenceDisplay => IsOnline ? "Online no APPNORA" : "Offline no APPNORA";
    public string ContactDisplay => $"{Email}  |  CPF: {FormattedCpf}  |  {PhoneDisplay}";
    public string PhoneDisplay => string.IsNullOrWhiteSpace(Phone) ? "Telefone não informado" : Phone;
}
public sealed record QueueEntry(long Id, Guid PatientId, string PatientName, DateTimeOffset EnteredAt, string Status, int Position = 0, string? Specialty = null)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public PatientAccount? Patient { get; init; }
    [System.Text.Json.Serialization.JsonIgnore]
    public string PresenceDisplay => Patient?.PresenceDisplay ?? "Presença não informada";
    public string EnteredAtDisplay => EnteredAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm");
    public string TicketDisplay => $"#{Id:000}";
    public string PositionDisplay => Position > 0 ? $"{Position}º" : "—";
    public string SpecialtyDisplay => string.IsNullOrWhiteSpace(Specialty) ? "Não informada" : Specialty;
    public string WaitTimeDisplay
    {
        get
        {
            var elapsed = DateTimeOffset.Now - EnteredAt.ToLocalTime();
            if (elapsed < TimeSpan.FromMinutes(1)) return "Agora";
            if (elapsed.TotalHours >= 1) return $"{(int)elapsed.TotalHours}h {elapsed.Minutes}min";
            return $"{Math.Max(1, elapsed.Minutes)} min";
        }
    }
    public string StatusDisplay => Status switch { "waiting" => "Aguardando", "offered" => "Oferta em andamento", "completed" => "Concluída", "cancelled" => "Cancelada", _ => Status };
}
public sealed record LightningSlot(Guid Id, string Label, string Status, DateTimeOffset CreatedAt)
{
    public string CreatedAtDisplay => CreatedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm");
    public string StatusDisplay => Status switch { "open" => "Aberta", "offered" => "Oferta em andamento", "accepted" => "Aceita", "closed" => "Encerrada", _ => Status };
}
public sealed record LightningCohort(int Days, long OpenSlots, long AcceptedOffers, long RegisteredSlots, long CreatedOffers, long DeclinedOffers, long ExpiredOffers, long PendingOffers, long CancelledOffers);
public sealed record MunicipalSummary(long Patients, long WaitingPatients, long OpenSlots, long AcceptedOffers, long? RegisteredSlots = null, long? CreatedOffers = null, long? DeclinedOffers = null, long? ExpiredOffers = null, long? PendingOffers = null, long? CancelledOffers = null, LightningCohort[]? LightningCohorts = null);

public sealed record HealthUnit(Guid Id, string Name, string Region, string Address, double Latitude, double Longitude);

public sealed record StaffAccount(Guid Id, string FullName, string Role);

public sealed record UnitPatientRecord(string Cpf, string FullName, string Email, string? Phone = null, AddressInfo? Address = null, bool IsActive = true, bool IsOnline = false, DateTimeOffset? CreatedAt = null)
{
    public string ActiveDisplay => IsActive ? "Ativo nesta UBS" : "Desativado nesta UBS";
    public string FormattedCpf => Cpf.Length == 11 ? $"{Cpf[..3]}.{Cpf[3..6]}.{Cpf[6..9]}-{Cpf[9..]}" : Cpf;
    public string PhoneDisplay => string.IsNullOrWhiteSpace(Phone) ? "Telefone não informado" : Phone;
    public string PresenceDisplay => IsOnline ? "Online no aplicativo" : "Offline no aplicativo";
    public string CreatedAtDisplay => CreatedAt?.ToLocalTime().ToString("dd/MM/yyyy HH:mm") ?? "Data não informada";
}

public sealed record AppointmentInfo(Guid Id, string Cpf, string PatientName, string Specialty, string Professional, DateTimeOffset StartsAt, DateTimeOffset EndsAt, string Status)
{
    public string TimeDisplay => $"{StartsAt.ToLocalTime():dd/MM/yyyy HH:mm} – {EndsAt.ToLocalTime():HH:mm}";
    public string StatusDisplay => Status switch { "scheduled" => "Agendada", "called" => "Chamada", "completed" => "Concluída", "missed" => "Falta", "cancelled" => "Cancelada", _ => Status };
}

public sealed record UnitWaitingEntry(long Id, string Cpf, string PatientName, string Specialty, DateTimeOffset EnteredAt, string Status)
{
    public string EnteredAtDisplay => EnteredAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm");
    public string StatusDisplay => Status switch { "waiting" => "Aguardando", "offered" => "Oferta em andamento", "completed" => "Concluída", "cancelled" => "Cancelada", _ => Status };
}
public sealed record UnitLightningSlot(Guid Id, Guid AppointmentId, string Specialty, string Professional, DateTimeOffset StartsAt, DateTimeOffset EndsAt, string Status, DateTimeOffset CreatedAt)
{
    public string TimeDisplay => $"{StartsAt.ToLocalTime():dd/MM/yyyy HH:mm} – {EndsAt.ToLocalTime():HH:mm}";
    public string StatusDisplay => Status switch { "open" => "Aberta", "offered" => "Ofertada", "accepted" => "Aceita", "closed" => "Encerrada", _ => Status };
}
