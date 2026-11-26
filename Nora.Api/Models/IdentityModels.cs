namespace Nora.Api.Models;

// Entrar com Google e push pelo servidor (P-042).
/// <summary><c>KeepSignedIn</c> só vale em <c>POST /api/auth/google</c> (manter conectado por 30 dias, como no login com senha); o vínculo da conta Google o ignora.</summary>
public sealed record GoogleTokenRequest(string? IdToken, string? DeviceName = null, bool? KeepSignedIn = null);
public sealed record GoogleLinkStatus(bool Enabled, bool Linked, string? Email);
public sealed record PushDeviceRequest(string? Token, string? Platform);
public sealed record PushDeviceRemoveRequest(string? Token);
public sealed record PushStatus(bool Enabled);
