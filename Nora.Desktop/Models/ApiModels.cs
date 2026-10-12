namespace Nora.Desktop.Models;

public sealed record AccountUser(Guid Id, string FullName, string Role, string Scope);
public sealed record LoginSession(string Token, DateTimeOffset ExpiresAt, AccountUser User);
public sealed record PatientAccount(Guid Id, string FullName, string Cpf, string Email, string? Phone = null, bool IsOnline = false)
{
    public string MaskedCpf => Cpf.Length >= 2 ? "***.***.***-" + Cpf[^2..] : "***";
    public string PresenceDisplay => IsOnline ? "Online no APPNORA" : "Offline no APPNORA";
    public string ContactDisplay => $"{Email}  |  CPF: {MaskedCpf}  |  {PhoneDisplay}";
    public string PhoneDisplay => string.IsNullOrWhiteSpace(Phone) ? "Telefone não informado" : Phone;
}
public sealed record QueueEntry(long Id, Guid PatientId, string PatientName, DateTimeOffset EnteredAt, string Status)
{
    public string EnteredAtDisplay => EnteredAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm");
    public string StatusDisplay => Status switch { "waiting" => "Aguardando", "offered" => "Oferta em andamento", "completed" => "Concluída", "cancelled" => "Cancelada", _ => Status };
}
public sealed record LightningSlot(Guid Id, string Label, string Status, DateTimeOffset CreatedAt)
{
    public string StatusDisplay => Status switch { "open" => "Aberta", "offered" => "Oferta em andamento", "accepted" => "Aceita", "closed" => "Encerrada", _ => Status };
}
public sealed record MunicipalSummary(long Patients, long WaitingPatients, long OpenSlots, long AcceptedOffers);
