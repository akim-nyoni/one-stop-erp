using One4All.Application.Abstractions.Offline;
using One4All.Core.Common;
using One4All.Core.Offline;

namespace One4All.Application.Offline;

/// <summary>
/// Administers the offline outbox over an <see cref="IOfflineOutbox"/>: lists dead-lettered operations and
/// requeues or discards them. Depends only on the abstraction, so it is unit-tested with an in-memory fake.
/// </summary>
public sealed class OfflineAdminService : IOfflineAdminService
{
    private readonly IOfflineOutbox _outbox;

    /// <summary>Creates the admin service.</summary>
    /// <param name="outbox">The outbox to administer.</param>
    public OfflineAdminService(IOfflineOutbox outbox) => _outbox = outbox;

    /// <inheritdoc />
    public Task<IReadOnlyList<OfflineOperation>> ListFailedAsync(int maxCount = 200, CancellationToken ct = default) =>
        _outbox.GetByStatusAsync(OfflineOperationStatus.Failed, maxCount, ct);

    /// <inheritdoc />
    public Task<Result> RequeueAsync(Guid operationId, CancellationToken ct = default) =>
        MutateAsync(operationId, op => op.Requeue(), "requeued", ct);

    /// <inheritdoc />
    public Task<Result> DiscardAsync(Guid operationId, CancellationToken ct = default) =>
        MutateAsync(operationId, op => op.Discard(), "discarded", ct);

    private async Task<Result> MutateAsync(Guid operationId, Func<OfflineOperation, bool> mutate, string verb, CancellationToken ct)
    {
        OfflineOperation? operation = await _outbox.FindAsync(operationId, ct).ConfigureAwait(false);
        if (operation is null)
        {
            return Result.Failure(Error.NotFound("Offline.NotFound", $"No offline operation {operationId}."));
        }

        if (!mutate(operation))
        {
            return Result.Failure(Error.Rule("Offline.NotFailed", $"Only a dead-lettered operation can be {verb} (it is {operation.Status})."));
        }

        await _outbox.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }
}
