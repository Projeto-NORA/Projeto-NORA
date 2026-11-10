namespace Nora.Api.Models;

public sealed record RegistrationStartRequest(string? FullName, string? Cpf, string? Password, Guid[]? UnitIds);
public sealed record RegistrationEmailOption(string Id, string MaskedEmail);
public sealed record RegistrationStartResponse(Guid? ChallengeId, List<RegistrationEmailOption> Emails);
public sealed record RegistrationSendRequest(Guid ChallengeId, string? EmailOptionId);
public sealed record RegistrationConfirmRequest(Guid ChallengeId, string? Code);
public sealed record RegistrationConfirmResponse(bool CodeValid, List<Guid> LinkedUnits, List<Guid> UnmatchedUnits);
public sealed record PatientUnitLinkRequest(Guid[]? UnitIds);
public sealed record PatientUnitLinkResponse(List<Guid> LinkedUnits, List<Guid> UnmatchedUnits);
