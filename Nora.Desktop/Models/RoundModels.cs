using System.Globalization;
using System.Text.Json.Serialization;
using Nora.Desktop.Services;

namespace Nora.Desktop.Models;

// Contratos da rodada final de 2026-11-16 (docs/api.md): computadores do OBS e atendimento ao público.

/// <summary>Computador ativado como a API o descreve (GET /api/obs/devices/current e o campo "device" do login).</summary>
public sealed record DeviceSummary(Guid Id, string Name, Guid UnitId, string UnitName)
{
    [JsonIgnore] public string Display => $"{Name} · {UnitName}";
}

/// <summary>Resposta da ativação por código: o segredo aparece uma única vez e só é guardado no próprio computador.</summary>
public sealed record ActivatedDevice(Guid DeviceId, string Secret, string Name, Guid UnitId, string UnitName);

/// <summary>Segredo do computador, devolvido ao ativar "este computador" pela tela do administrador.</summary>
public sealed record DeviceCredential(Guid DeviceId, string Secret);

/// <summary>Um computador da lista da UBS. Situação: <c>active</c>, <c>pending</c> (aguardando o código) ou <c>revoked</c>.</summary>
public sealed record ObsDevice(Guid Id, string Name, string Status, DateTimeOffset CreatedAt, string? CreatedByName = null, DateTimeOffset? ActivatedAt = null, DateTimeOffset? LastLoginAt = null,
    DateTimeOffset? RevokedAt = null, string? RevokedByName = null, DateTimeOffset? CodeExpiresAt = null, bool IsThisDevice = false)
{
    [JsonIgnore] public bool IsActive => Status == "active";
    [JsonIgnore] public bool IsRevoked => Status == "revoked";
    [JsonIgnore] public string StatusDisplay => Status switch { "active" => "Ativo", "pending" => "Aguardando código", "revoked" => "Revogado", _ => Status };
    [JsonIgnore] public string ActivatedDisplay => ActivatedAt is { } at ? at.ToLocalTime().ToString("dd/MM/yyyy HH:mm", DisplayFormat.Culture) : "—";
    [JsonIgnore] public string LastLoginDisplay => LastLoginAt is { } at ? at.ToLocalTime().ToString("dd/MM/yyyy HH:mm", DisplayFormat.Culture) : "Nunca";
    [JsonIgnore] public string ThisDeviceDisplay => IsThisDevice ? "Este computador" : "";
    [JsonIgnore] public string NameDisplay => IsThisDevice ? Name + " (este computador)" : Name;
}
public sealed record ObsDeviceList(ObsDevice[] Devices);

/// <summary>Resposta de POST .../devices: com "ativar este computador" vem a credencial; sem, vem o código de ativação (uma única vez).</summary>
public sealed record CreatedObsDevice(ObsDevice Device, DeviceCredential? Credential = null, string? ActivationCode = null, DateTimeOffset? CodeExpiresAt = null);
public sealed record ActivationCodeInfo(string ActivationCode, DateTimeOffset CodeExpiresAt);
public sealed record RevokedObsDevice(ObsDevice Device);

/// <summary>Um passo de uma operação demorada: o que está sendo feito agora, o que já terminou e o que falhou.</summary>
public enum StepState { Running, Done, Failed }

public sealed class ProgressStep(string text) : Generics.ObservableObject
{
    private string label = text;
    private StepState state = StepState.Running;
    public string Text { get => label; set { label = value; Notify(); Notify(nameof(AutomationText)); } }
    public StepState State { get => state; set { state = value; Notify(); Notify(nameof(IsRunning)); Notify(nameof(IsDone)); Notify(nameof(IsFailed)); Notify(nameof(AutomationText)); } }
    public bool IsRunning => state == StepState.Running;
    public bool IsDone => state == StepState.Done;
    public bool IsFailed => state == StepState.Failed;
    /// <summary>O estado também é dito em palavras, nunca só por cor ou ícone.</summary>
    public string AutomationText => $"{Text}: {state switch { StepState.Done => "concluído", StepState.Failed => "falhou", _ => "em andamento" }}";
}

public sealed record OpeningInterval(int Weekday, string Opens, string Closes);
public sealed record ProfileSpecialty(Guid Id, string Name);

/// <summary>Atendimento ao público da UBS (horário de funcionamento por dia, texto de serviços e especialidades da grade).</summary>
public sealed record UnitProfile(Guid UnitId, bool Informed, OpeningInterval[] OpeningHours, string? Services, ProfileSpecialty[] Specialties, DateTimeOffset? UpdatedAt);
public sealed record UnitProfileDraft(OpeningInterval[] OpeningHours, string? Services);

/// <summary>Consulta cancelada de onde a vaga veio.</summary>
public sealed record SlotSource(Guid AppointmentId, string PatientName, DateTimeOffset StartsAt, DateTimeOffset EndsAt, DateTimeOffset? CancelledAt = null, string? CancelledBy = null)
{
    [JsonIgnore] public string Display
    {
        get
        {
            var by = CancelledBy switch { "patient" => " pelo paciente", "staff" => " pela equipe", "system" => " pelo sistema", _ => "" };
            var at = CancelledAt is { } time ? $", cancelada em {time.ToLocalTime():dd/MM HH:mm}{by}" : ", cancelada";
            return $"Veio da consulta de {PatientName}{at}";
        }
    }
}

/// <summary>Dia da semana de <see cref="OpeningInterval"/> (0 = domingo) em português.</summary>
public static class OpeningHoursText
{
    /// <summary>"07:00–11:30 e 13:00–17:00" ou "Fechado".</summary>
    public static string ForDay(IEnumerable<OpeningInterval> hours, int weekday)
    {
        var parts = hours.Where(item => item.Weekday == weekday).OrderBy(item => item.Opens, StringComparer.Ordinal).Select(item => $"{item.Opens}–{item.Closes}").ToArray();
        return parts.Length == 0 ? "Fechado" : string.Join(" e ", parts);
    }
    public static bool TryParseTime(string? text, out TimeOnly time) => TimeOnly.TryParseExact(text?.Trim(), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out time);
}
