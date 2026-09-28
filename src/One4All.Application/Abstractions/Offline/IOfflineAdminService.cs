using One4All.Core.Common;
using One4All.Core.Offline;

namespace One4All.Application.Abstractions.Offline;

/// <summary>
/// Administration of the offline outbox: inspecting dead-lettered operations and either requeueing them
/// (after the cause is fixed) or discarding them. Used by operators through the API/admin UI; the routine
/// drain is <see cref="IOfflineSyncService"/>.
/// </summary>
public interface IOfflineAdminService
{
    /// <summary>Lists dead-lettered (failed) operations, oldest first.</summary>
    /// <param name="maxCount">The maximum number to return.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The failed operations.</returns>
    Task<IReadOnlyList<OfflineOperation>> ListFailedAsync(int maxCount = 200, CancellationToken ct = default);

    /// <summary>Returns a dead-lettered operation to the pending queue for another attempt.</summary>
    /// <param name="operationId">The operation id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or NotFound/Rule when the operation is missing or not failed.</returns>
    Task<Result> RequeueAsync(Guid operationId, CancellationToken ct = default);

    /// <summary>Abandons a dead-lettered operation so it is never replayed (kept for audit).</summary>
    /// <param name="operationId">The operation id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or NotFound/Rule when the operation is missing or not failed.</returns>
    Task<Result> DiscardAsync(Guid operationId, CancellationToken ct = default);
}
