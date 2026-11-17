using System.Text.Json.Serialization;

namespace Nora.Api.Models;

/// <summary>Credencial do computador enviada no cabeçalho <c>X-Nora-Device</c>: id em hexadecimal (formato N) e segredo de 64 hexadecimais (maiúsculos). O valor nunca vai para log.</summary>
public sealed record DeviceCredential(Guid Id, string Secret);
/// <summary>Computador ativado (resposta do login e de <c>GET /api/obs/devices/current</c>).</summary>
public sealed record ObsDeviceRef(Guid Id, string Name, Guid UnitId, string UnitName);
public sealed record ObsDeviceInfo(Guid Id, string Name, string Status, DateTimeOffset CreatedAt, string CreatedByName, DateTimeOffset? ActivatedAt, DateTimeOffset? LastLoginAt, DateTimeOffset? RevokedAt, string? RevokedByName, DateTimeOffset? CodeExpiresAt, bool IsThisDevice);
public sealed record ObsDeviceList(List<ObsDeviceInfo> Devices);
public sealed record CreateObsDeviceRequest(string? Name, [property: JsonRequired] bool ActivateThisComputer);
public sealed record ActivateObsDeviceRequest(string? Code);
public sealed record RevokeObsDeviceRequest([property: JsonRequired] bool Confirmed);
public sealed record ObsDeviceActivation(Guid DeviceId, string Secret, string Name, Guid UnitId, string UnitName);
public sealed record ObsDeviceCodeResult(string ActivationCode, DateTimeOffset CodeExpiresAt);
public sealed record ObsDeviceCredentialResult(Guid DeviceId, string Secret);
/// <summary>Resposta de criar o computador já ativado: o segredo aparece uma única vez.</summary>
public sealed record ObsDeviceWithCredential(ObsDeviceInfo Device, ObsDeviceCredentialResult Credential);
/// <summary>Resposta de criar o computador aguardando o código de ativação (XXXX-XXXX, uso único, 15 minutos).</summary>
public sealed record ObsDeviceWithCode(ObsDeviceInfo Device, string ActivationCode, DateTimeOffset CodeExpiresAt);
public sealed record ObsDeviceEnvelope(ObsDeviceInfo Device);
