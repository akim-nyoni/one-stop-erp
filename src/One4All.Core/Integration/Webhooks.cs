using One4All.Core.Common;

namespace One4All.Core.Integration;

/// <summary>A third party's subscription to an event (<c>sys.webhook_subscription</c>, brief §10 webhook publisher).</summary>
public sealed class WebhookSubscription : Entity, ICompanyScoped
{
    private WebhookSubscription()
    {
        EventType = string.Empty;
        Url = string.Empty;
        Secret = string.Empty;
    }

    /// <summary>Creates the subscription.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="eventType">The event (e.g. sale.completed), or * for all.</param>
    /// <param name="url">Where deliveries are posted.</param>
    /// <param name="secret">The shared secret deliveries are signed with.</param>
    public WebhookSubscription(long companyId, string eventType, string url, string secret)
    {
        CompanyId = companyId;
        EventType = eventType;
        Url = url;
        Secret = secret;
        IsActive = true;
    }

    /// <summary>Gets the company.</summary>
    public long CompanyId { get; private set; }

    /// <summary>Gets the event, or * for all.</summary>
    public string EventType { get; private set; }

    /// <summary>Gets the endpoint.</summary>
    public string Url { get; private set; }

    /// <summary>Gets the signing secret.</summary>
    public string Secret { get; private set; }

    /// <summary>Gets or sets a value indicating whether deliveries are made.</summary>
    public bool IsActive { get; set; }

    /// <summary>Whether the subscription takes an event.</summary>
    /// <param name="eventType">The event.</param>
    /// <returns>True when it does.</returns>
    public bool Takes(string eventType) => IsActive && (EventType == "*" || string.Equals(EventType, eventType, StringComparison.OrdinalIgnoreCase));
}

/// <summary>Where a delivery stands.</summary>
public enum WebhookDeliveryStatus
{
    /// <summary>Waiting to be sent (or retried).</summary>
    Pending = 0,

    /// <summary>Accepted by the receiver.</summary>
    Delivered = 1,

    /// <summary>Given up after the last attempt.</summary>
    Failed = 2,
}

/// <summary>One event for one subscriber (<c>sys.webhook_delivery</c>): an outbox row written with the business change, sent and retried by the dispatcher.</summary>
public sealed class WebhookDelivery : Entity, ICompanyScoped
{
    private WebhookDelivery()
    {
        EventType = string.Empty;
        Payload = string.Empty;
    }

    /// <summary>Queues the delivery.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="subscriptionId">The subscription.</param>
    /// <param name="eventType">The event.</param>
    /// <param name="payload">The JSON body.</param>
    /// <param name="createdOnUtc">When the event happened.</param>
    public WebhookDelivery(long companyId, long subscriptionId, string eventType, string payload, DateTimeOffset createdOnUtc)
    {
        CompanyId = companyId;
        SubscriptionId = subscriptionId;
        EventType = eventType;
        Payload = payload;
        CreatedOnUtc = createdOnUtc;
        NextAttemptOnUtc = createdOnUtc;
    }

    /// <summary>Gets the company.</summary>
    public long CompanyId { get; private set; }

    /// <summary>Gets the subscription.</summary>
    public long SubscriptionId { get; private set; }

    /// <summary>Gets the event.</summary>
    public string EventType { get; private set; }

    /// <summary>Gets the JSON body.</summary>
    public string Payload { get; private set; }

    /// <summary>Gets when the event happened.</summary>
    public DateTimeOffset CreatedOnUtc { get; private set; }

    /// <summary>Gets the status.</summary>
    public WebhookDeliveryStatus Status { get; private set; }

    /// <summary>Gets how many attempts were made.</summary>
    public int Attempts { get; private set; }

    /// <summary>Gets when the next attempt is due.</summary>
    public DateTimeOffset NextAttemptOnUtc { get; private set; }

    /// <summary>Gets the last HTTP status received.</summary>
    public int? LastStatusCode { get; private set; }

    /// <summary>Gets the last error.</summary>
    public string? LastError { get; private set; }

    /// <summary>Records an attempt.</summary>
    /// <param name="delivered">Whether the receiver accepted it.</param>
    /// <param name="statusCode">The HTTP status, if any.</param>
    /// <param name="error">The error, if any.</param>
    /// <param name="nextAttempt">When to retry, or null to give up.</param>
    public void RecordAttempt(bool delivered, int? statusCode, string? error, DateTimeOffset? nextAttempt)
    {
        Attempts++;
        LastStatusCode = statusCode;
        LastError = error;
        if (delivered)
        {
            Status = WebhookDeliveryStatus.Delivered;
        }
        else if (nextAttempt is DateTimeOffset next)
        {
            NextAttemptOnUtc = next;
        }
        else
        {
            Status = WebhookDeliveryStatus.Failed;
        }
    }

    /// <summary>Puts a failed delivery back in the queue.</summary>
    /// <param name="at">When to try.</param>
    public void Requeue(DateTimeOffset at)
    {
        Status = WebhookDeliveryStatus.Pending;
        NextAttemptOnUtc = at;
    }
}
