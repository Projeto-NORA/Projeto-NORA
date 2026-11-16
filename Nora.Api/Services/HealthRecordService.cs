using Nora.Api.DAOs;
using Nora.Api.Models;

namespace Nora.Api.Services;

/// <summary>Leitura da Jornada pelo paciente: sempre o CPF da sessão, vínculo e cadastro ativo conferidos pelo executor da UBS.</summary>
public sealed class HealthRecordService(HealthUnitService units, UnitAccessService access, HealthRecordDao records)
{
    /// <summary>Exames e vacinas do próprio paciente numa UBS. Sem vínculo (ou cadastro inativo na UBS) → 403.</summary>
    public Task<HealthRecords> ListOwnAsync(SessionInfo session, Guid unitId, bool includeVoided, CancellationToken ct)
        => access.ExecutePatientAsync(session, unitId, cpf => records.ListAsync(unitId, cpf, includeVoided, false, false, ct), ct);

    /// <summary>
    /// Jornada agregada: uma volta pelas UBS vinculadas com banco pronto, uma de cada vez (um executor e uma conexão de UBS por vez, sem abrir
    /// conexões em paralelo). UBS sem permissão (cadastro inativo) ou de schema desatualizado ficam de fora, como em <c>GET /api/patient/units</c>;
    /// um banco pronto fora do ar responde 503, sem lista parcial.
    /// </summary>
    public async Task<List<UnitHealthRecords>> ListOwnAllAsync(SessionInfo session, bool includeVoided, CancellationToken ct)
    {
        var result = new List<UnitHealthRecords>();
        foreach (var unit in await units.ListForPatientAsync(session.User.Id, ct))
        {
            if (unit.DatabaseState != "ready") continue;
            try
            {
                var own = await ListOwnAsync(session, unit.Id, includeVoided, ct);
                result.Add(new(unit.Id, unit.Name, own.Exams, own.Vaccines));
            }
            catch (ApiForbiddenException) { }
            catch (ApiCodedException outdated) when (outdated.Code == "unit-database-outdated") { }
        }
        return result;
    }
}
