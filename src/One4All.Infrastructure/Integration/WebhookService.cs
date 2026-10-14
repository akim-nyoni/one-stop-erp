using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Integration;
using One4All.Application.Integration;
using One4All.Core.Common;
using One4All.Core.Integration;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Integration;

/// <summary>Webhook subscriptions, the delivery outbox and its dispatcher (brief §10).</summary>
public sealed class WebhookService : IWebhookService
{
    private readonly One4AllDbContext _db;
    private readonly HttpClient _http;

    // The company's active subscriptions, read once for a short while: a run of postings publishes on every one, and they change rarely.
    private readonly Dictionary<long, (DateTimeOffset ReadAt, List<WebhookSubscription> Active)> _subscriptions = [];

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="http">Posts deliveries.</param>
    public WebhookService(One4AllDbContext db, HttpClient http)
    {
        _db = db;
        _http = http;
    }

    /// <inheritdoc />
    public async Task<Result<long>> SubscribeAsync(long companyId, string eventType, string url, string secret, CancellationToken ct = default)
    {
        _subscriptions.Remove(companyId);
        string evt = (eventType ?? string.Empty).Trim().ToLowerInvariant();
        if ((evt != "*" && !WebhookEvents.All.Contains(evt)) || !Uri.TryCreate(url?.Trim(), UriKind.Absolute, out Uri? endpoint)
            || endpoint.Scheme is not ("https" or "http") || (secret ?? string.Empty).Length < 16)
        {
            return Error.Validation("Webhook.Invalid", $"Subscribe to one of {string.Join(", ", WebhookEvents.All)} (or *), an http(s) URL and a secret of at least 16 characters.");
        }

        WebhookSubscription subscription = new(companyId, evt, endpoint.ToString(), secret!);
        _db.Add(subscription);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return subscription.Id;
    }

    /// <inheritdoc />
    public async Task<Result> UnsubscribeAsync(long companyId, long subscriptionId, CancellationToken ct = default)
    {
        _subscriptions.Remove(companyId);
        WebhookSubscription? subscription = await _db.Set<WebhookSubscription>().FirstOrDefaultAsync(s => s.Id == subscriptionId && s.CompanyId == companyId, ct).ConfigureAwait(false);
        if (subscription is null)
        {
            return Result.Failure(Error.NotFound("Webhook.NotFound", $"Subscription {subscriptionId} not found."));
        }

        subscription.IsActive = false;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<int> PublishAsync(long companyId, string eventType, object data, CancellationToken ct = default)
    {
        DateTimeOffset readAt = DateTimeOffset.UtcNow;
        if (!_subscriptions.TryGetValue(companyId, out var cached) || readAt - cached.ReadAt > TimeSpan.FromSeconds(30))
        {
            cached = (readAt, await _db.Set<WebhookSubscription>().AsNoTracking().Where(s => s.CompanyId == companyId && s.IsActive).ToListAsync(ct).ConfigureAwait(false));
            _subscriptions[companyId] = cached;
        }

        List<WebhookSubscription> subscribers = [.. cached.Active.Where(s => s.Takes(eventType))];
        if (subscribers.Count == 0)
        {
            return 0;
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        string payload = JsonSerializer.Serialize(new { @event = eventType, id = Guid.NewGuid().ToString("N"), occurredAt = now, companyId, data }, JsonOptions);
        foreach (WebhookSubscription subscriber in subscribers)
        {
            _db.Add(new WebhookDelivery(companyId, subscriber.Id, eventType, payload, now));
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return subscribers.Count;
    }

    /// <inheritdoc />
    public async Task<WebhookDispatchResult> DispatchDueAsync(DateTimeOffset now, CancellationToken ct = default)
    {
        List<WebhookDelivery> due = await _db.Set<WebhookDelivery>().Where(d => d.Status == WebhookDeliveryStatus.Pending && d.NextAttemptOnUtc <= now)
            .OrderBy(d => d.Id).Take(200).ToListAsync(ct).ConfigureAwait(false);
        List<long> subscriptionIds = [.. due.Select(d => d.SubscriptionId).Distinct()];
        Dictionary<long, WebhookSubscription> subscriptions = await _db.Set<WebhookSubscription>().AsNoTracking().Where(s => subscriptionIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, ct).ConfigureAwait(false);
        int delivered = 0, retrying = 0, failed = 0;
        foreach (WebhookDelivery delivery in due)
        {
            if (!subscriptions.TryGetValue(delivery.SubscriptionId, out WebhookSubscription? subscription) || !subscription.IsActive)
            {
                delivery.RecordAttempt(false, null, "The subscription was stopped.", null);
                failed++;
                continue;
            }

            (bool ok, int? status, string? error) = await SendAsync(subscription, delivery, ct).ConfigureAwait(false);
            DateTimeOffset? next = ok ? null : WebhookRules.NextAttempt(delivery.Attempts + 1, now);
            delivery.RecordAttempt(ok, status, error, next);
            if (ok)
            {
                delivered++;
            }
            else if (next is null)
            {
                failed++;
            }
            else
            {
                retrying++;
            }
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return new WebhookDispatchResult(delivered, retrying, failed);
    }

    /// <inheritdoc />
    public async Task<Result> RetryAsync(long companyId, long deliveryId, CancellationToken ct = default)
    {
        WebhookDelivery? delivery = await _db.Set<WebhookDelivery>().FirstOrDefaultAsync(d => d.Id == deliveryId && d.CompanyId == companyId, ct).ConfigureAwait(false);
        if (delivery is null || delivery.Status != WebhookDeliveryStatus.Failed)
        {
            return Result.Failure(Error.Rule("Webhook.NotFailed", "Only a failed delivery is retried."));
        }

        delivery.Requeue(DateTimeOffset.UtcNow);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private async Task<(bool Ok, int? Status, string? Error)> SendAsync(WebhookSubscription subscription, WebhookDelivery delivery, CancellationToken ct)
    {
        using HttpRequestMessage request = new(HttpMethod.Post, subscription.Url) { Content = new StringContent(delivery.Payload, Encoding.UTF8, "application/json") };
        request.Headers.TryAddWithoutValidation("X-One4All-Event", delivery.EventType);
        request.Headers.TryAddWithoutValidation("X-One4All-Delivery", delivery.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
        request.Headers.TryAddWithoutValidation("X-One4All-Signature", WebhookRules.Signature(subscription.Secret, delivery.Payload));
        try
        {
            using HttpResponseMessage response = await _http.SendAsync(request, ct).ConfigureAwait(false);
            return response.IsSuccessStatusCode ? (true, (int)response.StatusCode, null) : (false, (int)response.StatusCode, $"Receiver answered {(int)response.StatusCode}.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return (false, null, ex.Message);
        }
    }
}
