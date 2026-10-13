namespace Nora.Desktop.Models;

public sealed record AccountUser(Guid Id, string FullName, string Role, string Scope);
public sealed record LoginSession(string Token, DateTimeOffset ExpiresAt, AccountUser User);
public sealed record PatientAccount(Guid Id, string FullName, string Cpf, string Email, string? Phone = null, bool IsOnline = false, bool IsActive = true, DateTimeOffset? CreatedAt = null)
{
    public string MaskedCpf => Cpf.Length >= 2 ? "***.***.***-" + Cpf[^2..] : "***";
    public string FormattedCpf => Cpf.Length == 11 ? $"{Cpf[..3]}.{Cpf[3..6]}.{Cpf[6..9]}-{Cpf[9..]}" : Cpf;
    public string ActiveDisplay => IsActive ? "Ativo" : "Desativado";
    public string PresenceDisplay => IsOnline ? "Online no APPNORA" : "Offline no APPNORA";
    public string ContactDisplay => $"{Email}  |  CPF: {FormattedCpf}  |  {PhoneDisplay}";
    public string PhoneDisplay => string.IsNullOrWhiteSpace(Phone) ? "Telefone não informado" : Phone;
}
public sealed record QueueEntry(long Id, Guid PatientId, string PatientName, DateTimeOffset EnteredAt, string Status)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public PatientAccount? Patient { get; init; }
    [System.Text.Json.Serialization.JsonIgnore]
    public string PresenceDisplay => Patient?.PresenceDisplay ?? "Presença não informada";
    public string EnteredAtDisplay => EnteredAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm");
    public string StatusDisplay => Status switch { "waiting" => "Aguardando", "offered" => "Oferta em andamento", "completed" => "Concluída", "cancelled" => "Cancelada", _ => Status };
}
public sealed record LightningSlot(Guid Id, string Label, string Status, DateTimeOffset CreatedAt)
{
    public string CreatedAtDisplay => CreatedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm");
    public string StatusDisplay => Status switch { "open" => "Aberta", "offered" => "Oferta em andamento", "accepted" => "Aceita", "closed" => "Encerrada", _ => Status };
}
public sealed record MunicipalSummary(long Patients, long WaitingPatients, long OpenSlots, long AcceptedOffers);
