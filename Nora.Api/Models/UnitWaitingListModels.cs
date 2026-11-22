using System.Text.Json.Serialization;

namespace Nora.Api.Models;

public sealed record UnitWaitingEntry(long Id, string Cpf, string PatientName, string Specialty, DateTimeOffset EnteredAt, string Status);
public sealed record AddUnitWaitingEntryRequest(string Cpf, string Specialty);
public sealed record CancelUnitWaitingEntryRequest([property: JsonRequired] bool Confirmed);
/// <summary>
/// Entrada da fila como o OBS a vê: além dos campos de <see cref="UnitWaitingEntry"/>, o score em pontos do CPF na UBS (<c>Points</c> 0 a 1000, <c>Band</c> low/regular/good/excellent) e <c>OfferOrder</c>, a posição (1, 2, 3...)
/// na ordem de oferta da Vaga Relâmpago da especialidade (mais pontos primeiro, depois a ordem de entrada); nulo nas entradas encerradas.
/// </summary>
public sealed record ObsWaitingEntry(long Id, string Cpf, string PatientName, string Specialty, DateTimeOffset EnteredAt, string Status, int Points, string Band, int? OfferOrder);
/// <summary>Resposta de <c>GET /api/obs/units/{unitId}/waiting-list</c>: todas as entradas ativas mais uma página do histórico; <c>HistoryCursor</c> nulo = não há mais encerradas.</summary>
public sealed record ObsWaitingPage(List<ObsWaitingEntry> Items, string? HistoryCursor);
