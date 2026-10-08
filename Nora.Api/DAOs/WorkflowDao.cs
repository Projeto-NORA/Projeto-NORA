using Npgsql;
using Nora.Api.Database;
using Nora.Api.Models;
using Nora.Api.Services;

namespace Nora.Api.DAOs;

public sealed class WorkflowDao(DatabaseHelper database, TimeProvider time)
{
    public Task<List<QueueEntry>> QueueAsync(CancellationToken ct) => InTransactionAsync(c => ReadQueueAsync(c, null, ct), ct);
    public Task<List<SlotInfo>> SlotsAsync(CancellationToken ct) => InTransactionAsync(c => ReadSlotsAsync(c, ct), ct);
    public Task<PatientOverview> OverviewAsync(UserInfo user, CancellationToken ct) => InTransactionAsync(async c =>
        new PatientOverview(user, await ReadQueueAsync(c,user.Id,ct), await ReadOffersAsync(c,user.Id,ct)), ct);
    public Task<MunicipalSummary> SummaryAsync(CancellationToken ct) => InTransactionAsync(async c =>
    {
        await using var command = database.Command(c, "SELECT (SELECT count(*) FROM nora.accounts WHERE scope='patient'),(SELECT count(*) FROM nora.queue_entries WHERE status IN ('waiting','offered')),(SELECT count(*) FROM nora.slots WHERE status IN ('open','offered')),(SELECT count(*) FROM nora.offers WHERE status='accepted')");
        await using var reader = await command.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        return new MunicipalSummary(reader.GetInt64(0),reader.GetInt64(1),reader.GetInt64(2),reader.GetInt64(3));
    }, ct);
    public Task<QueueEntry> AddQueueAsync(Guid patientId, CancellationToken ct) => InTransactionAsync(async c =>
    {
        await using (var check = database.Command(c,"SELECT EXISTS(SELECT 1 FROM nora.accounts WHERE id=$1 AND scope='patient')", patientId))
            if (!(bool)(await check.ExecuteScalarAsync(ct))!) throw new ApiNotFoundException();
        await using var insert = database.Command(c,"INSERT INTO nora.queue_entries(patient_id,entered_at,status) VALUES($1,$2,'waiting') RETURNING id",patientId,time.GetUtcNow());
        var id = (long)(await insert.ExecuteScalarAsync(ct))!;
        await AdvanceAsync(c,ct);
        return (await ReadQueueAsync(c,null,ct)).Single(q => q.Id==id);
    },ct);
    public Task<bool> CancelQueueAsync(long id, CancellationToken ct) => InTransactionAsync(async c =>
    {
        var count = await database.ExecuteAsync(c,"UPDATE nora.queue_entries SET status='cancelled' WHERE id=$1 AND status IN ('waiting','offered')",ct,id);
        if (count==0) throw new ApiConflictException();
        await database.ExecuteAsync(c,"UPDATE nora.slots SET status='open' WHERE id IN (SELECT slot_id FROM nora.offers WHERE queue_id=$1 AND status='pending')",ct,id);
        await database.ExecuteAsync(c,"UPDATE nora.offers SET status='cancelled' WHERE queue_id=$1 AND status='pending'",ct,id);
        await AdvanceAsync(c,ct);
        return true;
    },ct);
    public Task<SlotInfo> AddSlotAsync(string label, CancellationToken ct) => InTransactionAsync(async c =>
    {
        var id=Guid.NewGuid();
        await database.ExecuteAsync(c,"INSERT INTO nora.slots(id,label,status,created_at) VALUES($1,$2,'open',$3)",ct,id,label,time.GetUtcNow());
        await AdvanceAsync(c,ct);
        return (await ReadSlotsAsync(c,ct)).Single(s=>s.Id==id);
    },ct);
    public Task<OfferInfo> RespondAsync(Guid id,Guid patientId,bool accept,CancellationToken ct) => InTransactionAsync(async c =>
    {
        var offers=await ReadOffersAsync(c,patientId,ct);
        var offer=offers.SingleOrDefault(o=>o.Id==id) ?? throw new ApiNotFoundException();
        if(offer.Status!="pending") throw new ApiConflictException();
        await database.ExecuteAsync(c,"UPDATE nora.offers SET status=$2 WHERE id=$1",ct,id,accept?"accepted":"declined");
        await database.ExecuteAsync(c,"UPDATE nora.queue_entries SET status='completed' WHERE id=(SELECT queue_id FROM nora.offers WHERE id=$1)",ct,id);
        await database.ExecuteAsync(c,"UPDATE nora.slots SET status=$2 WHERE id=$1",ct,offer.SlotId,accept?"accepted":"open");
        await AdvanceAsync(c,ct);
        return offer with { Status=accept?"accepted":"declined" };
    },ct);
    public Task<bool> AdvanceAsync(CancellationToken ct) => InTransactionAsync(_=>Task.FromResult(true),ct);

    private async Task<T> InTransactionAsync<T>(Func<NpgsqlConnection,Task<T>> action,CancellationToken ct)
    {
        await using var c=await database.OpenAsync(ct);
        await using var transaction=await c.BeginTransactionAsync(ct);
        // Uma única fila: serialize criação/resposta/expiração para evitar dupla oferta.
        await database.ExecuteAsync(c,"SELECT pg_advisory_xact_lock(62419803)",ct);
        await AdvanceAsync(c,ct);
        try
        {
            var result=await action(c);
            await transaction.CommitAsync(ct);
            return result;
        }
        catch(ApiConflictException)
        {
            // A expiração feita antes da resposta deve permanecer mesmo ao responder tarde.
            await transaction.CommitAsync(ct);
            throw;
        }
    }
    private async Task AdvanceAsync(NpgsqlConnection c,CancellationToken ct)
    {
        var now=time.GetUtcNow();
        await database.ExecuteAsync(c,"UPDATE nora.queue_entries SET status='completed' WHERE id IN (SELECT queue_id FROM nora.offers WHERE status='pending' AND expires_at<=$1)",ct,now);
        await database.ExecuteAsync(c,"UPDATE nora.slots SET status='open' WHERE id IN (SELECT slot_id FROM nora.offers WHERE status='pending' AND expires_at<=$1)",ct,now);
        await database.ExecuteAsync(c,"UPDATE nora.offers SET status='expired' WHERE status='pending' AND expires_at<=$1",ct,now);
        var open=new List<Guid>();
        await using(var command=database.Command(c,"SELECT id FROM nora.slots WHERE status='open' ORDER BY created_at,id"))
        await using(var reader=await command.ExecuteReaderAsync(ct))
            while(await reader.ReadAsync(ct)) open.Add(reader.GetGuid(0));
        foreach(var slotId in open)
        {
            long queueId;
            Guid patientId;
            await using(var command=database.Command(c,"SELECT id,patient_id FROM nora.queue_entries WHERE status='waiting' ORDER BY entered_at,id LIMIT 1"))
            await using(var reader=await command.ExecuteReaderAsync(ct))
            {
                if(!await reader.ReadAsync(ct)) break;
                queueId=reader.GetInt64(0); patientId=reader.GetGuid(1);
            }
            await database.ExecuteAsync(c,"INSERT INTO nora.offers(id,slot_id,queue_id,patient_id,expires_at,status) VALUES($1,$2,$3,$4,$5,'pending')",ct,Guid.NewGuid(),slotId,queueId,patientId,now.AddMinutes(10));
            await database.ExecuteAsync(c,"UPDATE nora.queue_entries SET status='offered' WHERE id=$1",ct,queueId);
            await database.ExecuteAsync(c,"UPDATE nora.slots SET status='offered' WHERE id=$1",ct,slotId);
        }
    }
    private async Task<List<QueueEntry>> ReadQueueAsync(NpgsqlConnection c,Guid? patientId,CancellationToken ct)
    {
        await using var command=patientId is null
            ? database.Command(c,"SELECT q.id,q.patient_id,a.full_name,q.entered_at,q.status FROM nora.queue_entries q JOIN nora.accounts a ON a.id=q.patient_id ORDER BY q.entered_at,q.id")
            : database.Command(c,"SELECT q.id,q.patient_id,a.full_name,q.entered_at,q.status FROM nora.queue_entries q JOIN nora.accounts a ON a.id=q.patient_id WHERE q.patient_id=$1 ORDER BY q.entered_at,q.id",patientId.Value);
        await using var reader=await command.ExecuteReaderAsync(ct);
        var result=new List<QueueEntry>();
        while(await reader.ReadAsync(ct)) result.Add(new(reader.GetInt64(0),reader.GetGuid(1),reader.GetString(2),reader.GetFieldValue<DateTimeOffset>(3),reader.GetString(4)));
        return result;
    }
    private async Task<List<SlotInfo>> ReadSlotsAsync(NpgsqlConnection c,CancellationToken ct)
    {
        await using var command=database.Command(c,"SELECT id,label,status,created_at FROM nora.slots ORDER BY created_at,id");
        await using var reader=await command.ExecuteReaderAsync(ct);
        var result=new List<SlotInfo>();
        while(await reader.ReadAsync(ct)) result.Add(new(reader.GetGuid(0),reader.GetString(1),reader.GetString(2),reader.GetFieldValue<DateTimeOffset>(3)));
        return result;
    }
    private async Task<List<OfferInfo>> ReadOffersAsync(NpgsqlConnection c,Guid patientId,CancellationToken ct)
    {
        await using var command=database.Command(c,"SELECT o.id,o.slot_id,o.patient_id,s.label,o.expires_at,o.status FROM nora.offers o JOIN nora.slots s ON s.id=o.slot_id WHERE o.patient_id=$1 ORDER BY o.expires_at,o.id",patientId);
        await using var reader=await command.ExecuteReaderAsync(ct);
        var result=new List<OfferInfo>();
        while(await reader.ReadAsync(ct)) result.Add(new(reader.GetGuid(0),reader.GetGuid(1),reader.GetGuid(2),reader.GetString(3),reader.GetFieldValue<DateTimeOffset>(4),reader.GetString(5)));
        return result;
    }
}
