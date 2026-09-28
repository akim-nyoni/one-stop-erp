namespace One4All.Application.Abstractions.Offline;

/// <summary>
/// One operation captured in the terminal-local queue while the central database was unreachable. It
/// mirrors the fields needed to later create a central <see cref="One4All.Core.Offline.OfflineOperation"/>.
/// </summary>
/// <param name="OperationId">The client-generated idempotency key.</param>
/// <param name="CompanyId">The owning company.</param>
/// <param name="Kind">The operation kind (selects the replay handler, e.g. <c>PosSale</c>).</param>
/// <param name="PayloadJson">The serialized operation snapshot.</param>
/// <param name="CapturedUtc">When the operation was captured on the terminal.</param>
public sealed record LocalOfflineEntry(Guid OperationId, long CompanyId, string Kind, string PayloadJson, DateTimeOffset CapturedUtc);

/// <summary>
/// A durable, terminal-local queue used when the central database itself is unreachable (so the central
/// <see cref="IOfflineOutbox"/> cannot be written). Operations are captured here and, on reconnect, handed
/// off to the central outbox and then cleared. Unlike the central outbox this must not depend on the
/// database — a file or embedded store on the terminal.
/// </summary>
public interface ILocalOfflineQueue
{
    /// <summary>Captures an operation locally. Enqueuing an operation id already present is a no-op.</summary>
    /// <param name="entry">The entry to capture.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task.</returns>
    Task EnqueueAsync(LocalOfflineEntry entry, CancellationToken ct = default);

    /// <summary>Lists the captured entries, oldest first.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The entries.</returns>
    Task<IReadOnlyList<LocalOfflineEntry>> ListAsync(CancellationToken ct = default);

    /// <summary>Removes a captured entry once it has been handed off to the central outbox.</summary>
    /// <param name="operationId">The entry's operation id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task.</returns>
    Task RemoveAsync(Guid operationId, CancellationToken ct = default);

    /// <summary>Counts the captured entries.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The count.</returns>
    Task<int> CountAsync(CancellationToken ct = default);
}
