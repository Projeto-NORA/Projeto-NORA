namespace Nora.Api.Models;

// Entrar com Google e push pelo servidor (P-042).
public sealed record GoogleTokenRequest(string? IdToken, string? DeviceName = null);
public sealed record GoogleLinkStatus(bool Enabled, bool Linked, string? Email);
public sealed record PushDeviceRequest(string? Token, string? Platform);
public sealed record PushDeviceRemoveRequest(string? Token);
public sealed record PushStatus(bool Enabled);
