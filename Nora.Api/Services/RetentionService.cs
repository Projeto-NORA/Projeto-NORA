using Nora.Api.Database;
using Nora.Api.DAOs;
using Nora.Api.Models;

namespace Nora.Api.Services;

public sealed class RetentionService(DatabaseHelper database, RetentionDao retention, UnitAccessService unitAccess, SchedulingAccessService scheduling)
{
    public Task<RetentionOverview> ReadAsync(SessionInfo session, Guid? unitId, CancellationToken ct) =>
        AuthorizedAsync(session, unitId, () => retention.ReadAsync(unitId, ct), ct);

    public Task<RetentionPolicy> ChangePolicyAsync(SessionInfo session, Guid? unitId, RetentionChange change, CancellationToken ct)
    {
        Validate(change);
        return AuthorizedAsync(session, unitId, () => retention.ChangePolicyAsync(unitId, change.Days, ct), ct);
    }

    public async Task ChangeScheduleAsync(SessionInfo session, Guid? unitId, Guid id, RetentionChange change, CancellationToken ct)
    {
        Validate(change);
        if (!await AuthorizedAsync(session, unitId, () => retention.ChangeScheduleAsync(unitId, id, change.Days, ct), ct))
            throw new ApiCodedException(StatusCodes.Status409Conflict, "retention-item-unavailable");
    }

    private async Task<T> AuthorizedAsync<T>(SessionInfo session, Guid? unitId, Func<Task<T>> action, CancellationToken ct)
    {
        if (unitId is { } id) return await scheduling.ExecuteAdministratorAsync(session, id, action, ct);
        if (session.User is not { Scope: "municipal", Role: "manager" }) throw new ApiForbiddenException();
        await using var connection = await database.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await unitAccess.RevalidateAsync(connection, session, ct);
        var result = await action();
        await transaction.CommitAsync(ct);
        return result;
    }

    private static void Validate(RetentionChange request)
    {
        if (!request.Confirmed || request.Days is < 1 or > 36500) throw new ApiInputException();
    }
}

public sealed class RetentionWorker(DatabaseHelper database, RetentionDao retention, HealthUnitService units, ILogger<RetentionWorker> log)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!database.IsConfigured) return;
        await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        do
        {
            try
            {
                await retention.PurgeDueAsync(null, stoppingToken);
                foreach (var unit in await units.ListReadyAsync(stoppingToken))
                {
                    try { await retention.PurgeDueAsync(unit.Id, stoppingToken); }
                    catch (Exception error) when (!stoppingToken.IsCancellationRequested)
                    {
                        log.LogWarning("NORA: exclusão programada da unidade {UnitId} adiada ({Type}).", unit.Id, error.GetType().Name);
                    }
                }
            }
            catch (Exception error) when (!stoppingToken.IsCancellationRequested)
            {
                log.LogWarning("NORA: exclusão programada adiada ({Type}).", error.GetType().Name);
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
