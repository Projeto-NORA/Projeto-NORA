using System.Text.Json.Serialization;

namespace Nora.Api.Models;

public sealed record UserInfo(Guid Id, string FullName, string Role, string Scope, bool MustChangePassword = false);
public sealed record Account(UserInfo User, string Cpf, string Email, string PasswordHash);
public sealed record LoginRequest(string Scope, string Identifier, string Password, bool? KeepSignedIn = null,
    bool? DesktopSession = null, string? DeviceName = null, Guid? UnitId = null);
/// <summary>KeepSignedIn indica a duração realmente aplicada. Device confirma a identidade do computador autenticado pelo OBS.</summary>
public sealed record LoginResponse(string Token, DateTimeOffset ExpiresAt, UserInfo User, bool KeepSignedIn = false, ObsDeviceRef? Device = null);
public sealed record LoginUnitOption(Guid Id, string Name);
public sealed record DesktopLoginResult(LoginResponse? Session, IReadOnlyList<LoginUnitOption>? Units = null);
public sealed record StaffSelfProfile(string FullName, string Email, string Role, string Scope, string? MunicipalityName, IReadOnlyList<LoginUnitOption> Units);
public sealed record ProfileNameRequest(string? FullName);
public sealed record AddressInfo(string Cep, string Street, string? Number, string? Complement, string? Neighborhood, string City, string State, string? Ibge = null);
public sealed record CreatePatientRequest(string FullName, string Cpf, string Email, string Password, string? Phone = null, AddressInfo? Address = null);
public sealed record PatientInfo(Guid Id, string FullName, string Cpf, string Email, string? Phone = null, bool IsOnline = false, bool IsActive = true, DateTimeOffset? CreatedAt = null, AddressInfo? Address = null);
public sealed record SetPatientActiveRequest([property: JsonRequired] bool IsActive);
/// <summary>DeviceId e DeviceUnitId: computador ativado a que a sessão do OBS está ligada (nulos sem computador).</summary>
public sealed record SessionInfo(UserInfo User, string TokenHash, Guid? DeviceId = null, Guid? DeviceUnitId = null);
public sealed record PasswordResetStartRequest(string Cpf);
public sealed record PasswordResetChallengeResponse(Guid ChallengeId, int ExpiresInSeconds);
public sealed record PasswordResetFinishRequest(Guid ChallengeId, string Code, string NewPassword);
public sealed record PasswordResetFinishResponse(bool CodeValid);
public sealed record PasswordResetTarget(Guid Id, string FullName, string Email);
public sealed record ChangePasswordRequest(string? CurrentPassword, string? NewPassword);
/// <summary>Conta de equipe do OBS: sem CPF (P-053). Um <c>cpf</c> enviado pelo cliente é ignorado.</summary>
public sealed record CreateStaffRequest(string? FullName, string? Email, string? Password, string? Role);
public sealed record SetStaffActiveRequest([property: JsonRequired] bool IsActive, [property: JsonRequired] bool Confirmed);
public sealed record ResetStaffPasswordRequest(string? Password, [property: JsonRequired] bool Confirmed);
public sealed record UnlinkUnitRequest([property: JsonRequired] bool Confirmed);
public sealed record UnlinkUnitResult(int CancelledAppointments, int CancelledEntries);
/// <summary>Prévia da remoção do vínculo com a UBS: o que a remoção cancelaria agora (consultas futuras scheduled/called e entradas de fila waiting/offered do CPF da conta, sem limite de data).</summary>
public sealed record UnlinkPreview(int FutureAppointments, int ActiveWaitingEntries);
/// <summary>Cursor é opaco: devolva-o em before para a próxima página (o instante ISO de occurredAt ainda é aceito, mas perde eventos empatados).</summary>
public sealed record HistoryEvent(DateTimeOffset OccurredAt, string Kind, string? ActorName, Guid? UnitId, string? UnitName, string? AccountName, string Cursor);
