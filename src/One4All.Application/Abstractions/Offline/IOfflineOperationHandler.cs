using One4All.Core.Common;
using One4All.Core.Offline;

namespace One4All.Application.Abstractions.Offline;

/// <summary>
/// Replays one kind of captured offline operation against the central database. A handler <b>must be
/// idempotent</b>: replaying the same operation (same natural key, e.g. a sale number) more than once must
/// not apply the effect twice - the outbox guarantees at-least-once delivery, not exactly-once, so a
/// handler that already sees its effect present should return success without repeating it.
/// </summary>
public interface IOfflineOperationHandler
{
    /// <summary>The operation kind this handler replays (matched against <see cref="OfflineOperation.Kind"/>).</summary>
    string Kind { get; }

    /// <summary>Replays the operation. Idempotent: a no-op success when the effect is already present.</summary>
    /// <param name="operation">The operation to replay.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success when applied (or already present); a failure to retry or dead-letter.</returns>
    Task<Result> ReplayAsync(OfflineOperation operation, CancellationToken ct = default);
}
