namespace One4All.Application.Abstractions.Offline;

/// <summary>The outcome of draining the offline outbox.</summary>
/// <param name="Synced">Operations replayed successfully this run.</param>
/// <param name="DeadLettered">Operations that exhausted their retries (or had no handler) and were failed this run.</param>
/// <param name="Retryable">Operations that failed this run but remain pending for a later attempt.</param>
public readonly record struct OfflineSyncReport(int Synced, int DeadLettered, int Retryable)
{
    /// <summary>The total number of operations processed this run.</summary>
    public int Processed => Synced + DeadLettered + Retryable;
}

/// <summary>Drains the offline outbox, replaying captured operations to the central database.</summary>
public interface IOfflineSyncService
{
    /// <summary>Replays pending operations (oldest first) through their handlers and persists the outcome.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A report of what was synced, dead-lettered and left to retry.</returns>
    Task<OfflineSyncReport> SyncPendingAsync(CancellationToken ct = default);
}
