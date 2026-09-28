using One4All.Application.Abstractions.Offline;
using One4All.Core.Common;
using One4All.Core.Offline;
using One4All.Licensing.Core;

namespace One4All.Application.Offline;

/// <summary>Tuning for the offline sync engine.</summary>
public sealed class OfflineSyncOptions
{
    /// <summary>The number of failed attempts after which an operation is dead-lettered. Default 5.</summary>
    public int MaxAttempts { get; init; } = 5;

    /// <summary>The maximum number of operations drained per run. Default 500.</summary>
    public int BatchSize { get; init; } = 500;
}

/// <summary>
/// Drains the offline outbox: it takes the pending operations in capture order, replays each through the
/// handler registered for its <see cref="OfflineOperation.Kind"/>, and records the outcome. A successful
/// replay marks the operation synced; a failure is retried on later runs until the attempt ceiling, after
/// which it is dead-lettered; an operation whose kind has no handler is dead-lettered immediately. A
/// handler that throws is treated as a (retryable) failure rather than aborting the whole run, so one bad
/// operation cannot block the queue. The engine depends only on abstractions, so it is unit-tested with
/// in-memory fakes; the database-backed outbox and the concrete handlers live in the infrastructure.
/// </summary>
public sealed class OfflineSyncService : IOfflineSyncService
{
    private readonly IOfflineOutbox _outbox;
    private readonly IReadOnlyDictionary<string, IOfflineOperationHandler> _handlers;
    private readonly IClock _clock;
    private readonly OfflineSyncOptions _options;

    /// <summary>Creates the sync engine.</summary>
    /// <param name="outbox">The outbox to drain.</param>
    /// <param name="handlers">The registered replay handlers (one per kind).</param>
    /// <param name="clock">The clock.</param>
    /// <param name="options">Optional tuning (attempt ceiling, batch size).</param>
    public OfflineSyncService(
        IOfflineOutbox outbox,
        IEnumerable<IOfflineOperationHandler> handlers,
        IClock clock,
        OfflineSyncOptions? options = null)
    {
        _outbox = outbox;
        _clock = clock;
        _options = options ?? new OfflineSyncOptions();
        _handlers = handlers.ToDictionary(h => h.Kind, StringComparer.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public async Task<OfflineSyncReport> SyncPendingAsync(CancellationToken ct = default)
    {
        IReadOnlyList<OfflineOperation> pending = await _outbox.GetPendingAsync(_options.BatchSize, ct).ConfigureAwait(false);

        int synced = 0;
        int deadLettered = 0;
        int retryable = 0;

        foreach (OfflineOperation operation in pending)
        {
            if (!_handlers.TryGetValue(operation.Kind, out IOfflineOperationHandler? handler))
            {
                operation.Fail($"No handler is registered for operation kind '{operation.Kind}'.");
                deadLettered++;
                continue;
            }

            Result result = await ReplaySafelyAsync(handler, operation, ct).ConfigureAwait(false);
            if (result.IsSuccess)
            {
                operation.MarkSynced(_clock.UtcNow);
                synced++;
                continue;
            }

            operation.RecordFailure(result.Error.Message, _options.MaxAttempts);
            if (operation.Status == OfflineOperationStatus.Failed)
            {
                deadLettered++;
            }
            else
            {
                retryable++;
            }
        }

        if (pending.Count > 0)
        {
            await _outbox.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        return new OfflineSyncReport(synced, deadLettered, retryable);
    }

    private static async Task<Result> ReplaySafelyAsync(IOfflineOperationHandler handler, OfflineOperation operation, CancellationToken ct)
    {
        try
        {
            return await handler.ReplayAsync(operation, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Result.Failure(Error.Failure("Offline.ReplayThrew", ex.Message));
        }
    }
}
