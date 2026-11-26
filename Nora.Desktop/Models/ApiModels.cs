using System.Text.Json.Serialization;
using Nora.Desktop.Services;

namespace Nora.Desktop.Models;

public sealed record AccountUser(Guid Id, string FullName, string Role, string Scope, bool MustChangePassword = false);
/// <summary>Resposta do login. <c>KeepSignedIn</c> e <c>Device</c> só vêm da API nova (computador do OBS); sem eles a sessão é a de sempre.</summary>
public sealed record LoginSession(string Token, DateTimeOffset ExpiresAt, AccountUser User, bool KeepSignedIn = false, DeviceSummary? Device = null);
public sealed record StaffUnitOption(Guid Id, string Name);
public sealed record StaffSelfProfile(string FullName, string Email, string Role, string Scope, string? MunicipalityName, StaffUnitOption[] Units);
public sealed record AddressInfo(string Cep, string Street, string? Number, string? Complement, string? Neighborhood, string City, string State, string? Ibge = null)
{
    public string Display => $"{Street}{(string.IsNullOrWhiteSpace(Number) ? "" : ", " + Number)} · {Neighborhood ?? "Bairro não informado"} · {City}/{State}";
}
public sealed record LightningCohort(int Days, long OpenSlots, long AcceptedOffers, long RegisteredSlots, long CreatedOffers, long DeclinedOffers, long ExpiredOffers, long PendingOffers, long CancelledOffers);
public sealed record MunicipalSummary(long Patients, long OpenSlots, long AcceptedOffers, long? RegisteredSlots = null, long? CreatedOffers = null, long? DeclinedOffers = null, long? ExpiredOffers = null, long? PendingOffers = null, long? CancelledOffers = null, LightningCohort[]? LightningCohorts = null, long? InactivePatients = null, long? UnitsWithoutDatabase = null);

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
public sealed record HistoryEvent(DateTimeOffset OccurredAt, string Kind, string? ActorName = null, Guid? UnitId = null, string? UnitName = null, string? AccountName = null, string? Cursor = null)
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

/// <summary>
/// Participação do paciente na UBS nos últimos 12 meses (só vem na listagem do OBS): concluídas, faltas e cancelamentos feitos por ele, com o peso somado dos cancelamentos.
/// <c>Points</c> (0 a 1000), <c>Band</c> e <c>HasHistory</c> são o score em pontos; sem eles (API antiga) saem do <c>Score</c> pelo mesmo cálculo.
/// </summary>
public sealed record UnitAttendance(long Completed, long Missed, long CancelledByPatient, double CancellationWeight, double? Score, int? Points = null, string? Band = null, bool? HasHistory = null)
{
    [JsonIgnore] public bool HasHistoryValue => HasHistory ?? Score is not null;
    [JsonIgnore] public int PointsValue => Points ?? DisplayFormat.PointsFromScore(Score);
    [JsonIgnore] public string BandValue => Band ?? DisplayFormat.BandFor(PointsValue);
    [JsonIgnore] public string BandDisplay => DisplayFormat.BandLabel(BandValue);
    /// <summary>"742 · Bom", ou "1000 · sem consultas encerradas ainda".</summary>
    [JsonIgnore] public string ScoreDisplay => DisplayFormat.ScoreText(PointsValue, BandValue, HasHistoryValue);
    [JsonIgnore] public string CancelledDisplay => CancelledByPatient == 0 ? "Nenhum feito pelo paciente" : $"{(CancelledByPatient == 1 ? "1 feito" : CancelledByPatient + " feitos")} pelo paciente (peso somado {CancellationWeight.ToString("0.##", DisplayFormat.Culture)})";
}

public sealed record UnitPatientRecord(string Cpf, string FullName, string Email, string? Phone = null, AddressInfo? Address = null, bool IsActive = true, bool IsOnline = false, DateTimeOffset? CreatedAt = null, UnitAttendance? Attendance = null)
{
    public string ActiveDisplay => IsActive ? "Ativo nesta UBS" : "Desativado nesta UBS";
    public string FormattedCpf => BrazilianDataValidator.FormatCpf(Cpf);
    public string PhoneDisplay => string.IsNullOrWhiteSpace(Phone) ? "Telefone não informado" : Phone;
    /// <summary>Linha de dados do item da listagem: "CPF | e-mail | telefone", só com o que foi informado.</summary>
    public string MetaLine => string.Join(" | ", new[] { FormattedCpf, Email, Phone }.Where(value => !string.IsNullOrWhiteSpace(value)));
    /// <summary>Texto da etiqueta de situação do item: só o cadastro desativado leva etiqueta.</summary>
    public string StatusText => IsActive ? "" : "Desativado";
    public string PresenceDisplay => IsOnline ? "Online no aplicativo" : "Offline no aplicativo";
    public string CreatedAtDisplay => CreatedAt?.ToLocalTime().ToString("dd/MM/yyyy HH:mm") ?? "Data não informada";
    /// <summary>Score em pontos e faixa ("742 · Bom"); "—" quando a API não trouxe o dado.</summary>
    public string ScoreDisplay => Attendance?.ScoreDisplay ?? "—";
}

public sealed record AppointmentInfo(Guid Id, string Cpf, string PatientName, string Specialty, string Professional, DateTimeOffset StartsAt, DateTimeOffset EndsAt, string Status,
    DateTimeOffset? ConfirmedAt = null, string BookedBy = "staff", Guid? ProfessionalId = null, Guid? SpecialtyId = null)
{
    /// <summary>A presença pode ser confirmada de 48 h até 2 h antes; só nas próximas 48 h falta de confirmação vale como informação.</summary>
    private static readonly TimeSpan ConfirmationHorizon = TimeSpan.FromHours(48);
    public string TimeDisplay => $"{StartsAt.ToLocalTime():dd/MM/yyyy HH:mm} – {EndsAt.ToLocalTime():HH:mm}";
    public string AgendaDayDisplay => StartsAt.ToLocalTime().ToString("dddd, dd/MM/yyyy");
    public string FormattedCpf => BrazilianDataValidator.FormatCpf(Cpf);
    public string PatientDisplay => $"{PatientName} · CPF {FormattedCpf}";
    public string StatusDisplay => Status switch { "scheduled" => "Agendada", "called" => "Chamada", "completed" => "Concluída", "missed" => "Falta", "cancelled" => "Cancelada", _ => Status };
    /// <summary>Quem marcou: a equipe da UBS, o paciente pelo aplicativo ou o aceite de uma Vaga Relâmpago.</summary>
    public string BookedByDisplay => BookedBy switch { "patient" => "Paciente pelo aplicativo", "lightning" => "Vaga Relâmpago", _ => "Equipe da UBS" };
    /// <summary>"Presença confirmada às HH:mm" ou "Sem confirmação" (consulta agendada nas próximas 48 h); vazio quando não há o que informar.</summary>
    public string ConfirmationDisplay => ConfirmationAt(DateTimeOffset.Now);
    public bool HasConfirmationInfo => ConfirmationDisplay.Length > 0;
    public bool IsConfirmed => Status == "scheduled" && ConfirmedAt is not null;
    public string ConfirmationAt(DateTimeOffset now)
    {
        if (Status != "scheduled") return "";
        if (ConfirmedAt is { } confirmed)
        {
            var local = confirmed.ToLocalTime();
            return local.Date == now.ToLocalTime().Date ? $"Presença confirmada às {local:HH:mm}" : $"Presença confirmada em {local:dd/MM} às {local:HH:mm}";
        }
        return StartsAt > now && StartsAt - now <= ConfirmationHorizon ? "Sem confirmação" : "";
    }
}

public sealed record UnitLightningSlot(Guid Id, Guid? AppointmentId, string Specialty, string Professional, DateTimeOffset StartsAt, DateTimeOffset EndsAt, string Status, DateTimeOffset CreatedAt,
    string Origin = "cancellation", string? ClosedReason = null, SlotSource? Source = null)
{
    public string TimeDisplay => $"{StartsAt.ToLocalTime():dd/MM/yyyy HH:mm} – {EndsAt.ToLocalTime():HH:mm}";
    public string CreatedAtDisplay => CreatedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm");
    /// <summary>Vaga ainda em andamento (aberta ou ofertada); as demais são histórico.</summary>
    [JsonIgnore] public bool IsActive => Status is "open" or "offered";
    public string OriginDisplay => Origin switch { "cancellation" => "Cancelamento", _ => Origin };
    public string StatusDisplay => Status switch
    {
        "open" => "Aberta",
        "offered" => "Ofertada",
        "accepted" => "Aceita",
        "closed" => ClosedReason switch
        {
            "conflict" => "Fechada: horário ocupado",
            "too-late" => "Fechada: faltavam menos de 10 min",
            _ => "Encerrada"
        },
        _ => Status
    };
    /// <summary>"Veio da consulta de Maria, cancelada em 14/11 09:10 pelo paciente".</summary>
    [JsonIgnore] public string SourceDisplay => Source?.Display ?? "";
}
/// <summary>Uma oferta de uma Vaga Relâmpago (GET .../lightning-slots/{id}/offers): sem CPF, na ordem em que foram feitas.</summary>
public sealed record UnitSlotOffer(Guid Id, string PatientName, string Status, DateTimeOffset ExpiresAt)
{
    public string StatusDisplay => Status switch { "pending" => "Aguardando resposta", "accepted" => "Aceita", "declined" => "Recusada", "expired" => "Expirada", "cancelled" => "Cancelada", _ => Status };
    public string ExpiresAtDisplay => ExpiresAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm");
}

/// <summary>Resposta de GET .../lightning-slots: vagas ativas mais as encerradas recentes; <c>HistoryCursor</c> não nulo = há mais antigas.</summary>
public sealed record LightningSlotPage(UnitLightningSlot[] Items, string? HistoryCursor = null);
