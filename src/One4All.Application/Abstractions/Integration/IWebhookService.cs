using One4All.Core.Common;

namespace One4All.Application.Abstractions.Integration;

/// <summary>What a dispatch pass did.</summary>
/// <param name="Delivered">Deliveries accepted.</param>
/// <param name="Retrying">Deliveries that failed and will be retried.</param>
/// <param name="Failed">Deliveries given up.</param>
public sealed record WebhookDispatchResult(int Delivered, int Retrying, int Failed);

/// <summary>
/// The webhook publisher (brief §10): subscriptions per event; <see cref="PublishAsync"/> queues a delivery for each matching subscriber in the
/// caller's unit of work (an outbox, so an event is never lost or sent for a change that rolled back); the dispatcher posts them signed
/// (<c>X-One4All-Signature: sha256=…</c>) and retries with back-off.
/// </summary>
public interface IWebhookService
{
    /// <summary>Subscribes an endpoint to an event (or * for all); refused for unknown events or URLs that are not http(s).</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="eventType">The event.</param>
    /// <param name="url">The endpoint.</param>
    /// <param name="secret">The signing secret (at least 16 characters).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The subscription id.</returns>
    Task<Result<long>> SubscribeAsync(long companyId, string eventType, string url, string secret, CancellationToken ct = default);

    /// <summary>Stops a subscription.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="subscriptionId">The subscription.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success or why not.</returns>
    Task<Result> UnsubscribeAsync(long companyId, long subscriptionId, CancellationToken ct = default);

    /// <summary>Queues an event for every active subscriber (saved with the caller's unit of work).</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="eventType">The event.</param>
    /// <param name="data">The event data (serialised to JSON with the event name, id and time).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>How many deliveries were queued.</returns>
    Task<int> PublishAsync(long companyId, string eventType, object data, CancellationToken ct = default);

    /// <summary>Sends every delivery that is due, recording each attempt.</summary>
    /// <param name="now">Now.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>What happened.</returns>
    Task<WebhookDispatchResult> DispatchDueAsync(DateTimeOffset now, CancellationToken ct = default);

    /// <summary>Puts a failed delivery back in the queue.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="deliveryId">The delivery.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success or why not.</returns>
    Task<Result> RetryAsync(long companyId, long deliveryId, CancellationToken ct = default);
}
