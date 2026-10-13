using Nora.Api.DAOs;
using Nora.Api.Models;
namespace Nora.Api.Services;
public sealed class WorkflowService(WorkflowDao dao)
{
    public Task<List<QueueEntry>> QueueAsync(CancellationToken ct) => dao.QueueAsync(ct);
    public Task<List<SlotInfo>> SlotsAsync(CancellationToken ct) => dao.SlotsAsync(ct);
    public Task<PatientOverview> OverviewAsync(SessionInfo session,CancellationToken ct) => dao.OverviewAsync(session,ct);
    public Task<MunicipalSummary> SummaryAsync(CancellationToken ct) => dao.SummaryAsync(ct);
    public Task<QueueEntry> AddQueueAsync(AddQueueRequest request,CancellationToken ct) => request.PatientId==Guid.Empty ? throw new ApiInputException() : dao.AddQueueAsync(request.PatientId,ct);
    public Task<bool> CancelQueueAsync(long id,CancellationToken ct) => dao.CancelQueueAsync(id,ct);
    public Task<SlotInfo> AddSlotAsync(CreateSlotRequest request,CancellationToken ct)
    {
        var label=request.Label?.Trim()??"";
        return label.Length is <1 or >160 ? throw new ApiInputException() : dao.AddSlotAsync(label,ct);
    }
    public Task<OfferInfo> RespondAsync(Guid id,SessionInfo session,RespondOfferRequest request,CancellationToken ct) => dao.RespondAsync(id,session,request.Accept,ct);
    public Task<bool> SetPatientActiveAsync(Guid id,SetPatientActiveRequest request,CancellationToken ct) => id==Guid.Empty ? throw new ApiInputException() : dao.SetPatientActiveAsync(id,request.IsActive,ct);
}
