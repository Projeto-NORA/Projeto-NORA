using System.Text.Json.Serialization;

namespace Nora.Api.Models;

// Família (Fase 3). O CPF do dependente só trafega no corpo do pedido e na tela da equipe; as rotas do responsável usam o id do vínculo.
public sealed record FamilyRequest(string? Cpf, Guid? UnitId);
public sealed record FamilyConfirmRequest([property: JsonRequired] bool Confirmed);
public sealed record FamilyRejectRequest([property: JsonRequired] bool Confirmed, string? Reason);

/// <summary>
/// Pedido ou vínculo na visão do responsável. <c>DependentName</c> só vem depois da aprovação (aprovado ou encerrado): antes disso o responsável só
/// conhece o CPF que digitou (<c>DependentCpfMasked</c>), para a rota não servir de consulta de nomes por CPF.
/// </summary>
public sealed record DependentLink(Guid Id, string Status, Guid UnitId, string UnitName, string DependentCpfMasked, string? DependentName, DateTimeOffset RequestedAt,
    DateTimeOffset? DecidedAt, string? DecisionReason, DateTimeOffset? ClosedAt);

/// <summary>Pedido ou vínculo na visão da equipe da UBS, com os dados para conferir os documentos.</summary>
public sealed record FamilyLinkForStaff(Guid Id, string Status, string GuardianName, string GuardianCpf, string DependentCpf, string? DependentName, DateTimeOffset RequestedAt,
    string? DecidedByName, DateTimeOffset? DecidedAt, string? DecisionReason);

/// <summary>Ofertas pendentes de um dependente, na rota agregada de consulta periódica.</summary>
public sealed record DependentOffers(Guid DependentId, Guid UnitId, string UnitName, string DependentName, List<UnitLightningOffer> Offers);

public sealed record DeactivationPreviewRequest(string? Cpf);
public sealed record DeactivationPreview(int FutureAppointments, int ActiveWaitingEntries);

// Sugestão de troca do e-mail da conta.
public sealed record EmailSuggestion(Guid Id, string MaskedEmail, Guid UnitId, string UnitName, DateTimeOffset CreatedAt);
public sealed record EmailSuggestionConfirmRequest(string? Code);
public sealed record EmailSuggestionConfirmResponse(bool CodeValid);
