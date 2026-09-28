using One4All.Core.Offline;

namespace One4All.Application.Abstractions.Offline;

/// <summary>
/// The store of operations captured while a terminal was offline. Terminals enqueue operations here; the
/// sync service drains the pending ones and persists their updated status.
/// </summary>
public interface IOfflineOutbox
{
    /// <summary>Enqueues a captured operation. Enqueuing an operation id that is already present is a no-op.</summary>
    /// <param name="operation">The operation to enqueue.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True when the operation was newly enqueued; false when its id was already present.</returns>
    Task<bool> EnqueueAsync(OfflineOperation operation, CancellationToken ct = default);

    /// <summary>Returns pending operations in capture order (oldest first), up to a limit.</summary>
    /// <param name="maxCount">The maximum number to return.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The pending operations.</returns>
    Task<IReadOnlyList<OfflineOperation>> GetPendingAsync(int maxCount, CancellationToken ct = default);

    /// <summary>Persists status changes made to operations returned by <see cref="GetPendingAsync"/>.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task.</returns>
    Task SaveChangesAsync(CancellationToken ct = default);
}
