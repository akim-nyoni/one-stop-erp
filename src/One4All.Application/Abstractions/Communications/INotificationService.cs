using One4All.Application.Communications;
using One4All.Core.Common;
using One4All.Core.Communications;

namespace One4All.Application.Abstractions.Communications;

/// <summary>What a gateway made of a send.</summary>
/// <param name="Accepted">Whether the gateway accepted it.</param>
/// <param name="Reference">The gateway's reference when accepted, or the reason when not.</param>
public sealed record GatewayResult(bool Accepted, string Reference);

/// <summary>
/// A delivery channel's gateway (brief §8: SMTP/Graph email, an SMS gateway abstraction, optional WhatsApp/Telegram).
/// Register one per channel; a channel without one leaves its messages queued with the reason.
/// </summary>
public interface IMessageGateway
{
    /// <summary>The channel it delivers.</summary>
    MessageChannel Channel { get; }

    /// <summary>Sends a message.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="recipient">The address, number or user id.</param>
    /// <param name="subject">The subject.</param>
    /// <param name="body">The body.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The outcome.</returns>
    Task<GatewayResult> SendAsync(long companyId, string recipient, string subject, string body, CancellationToken ct = default);
}

/// <summary>A recipient of a bulk despatch.</summary>
/// <param name="Recipient">The address, number or user id.</param>
/// <param name="Fields">The merge values for this recipient.</param>
public sealed record DespatchRecipient(string Recipient, IReadOnlyDictionary<string, string> Fields);

/// <summary>A despatch run's outcome.</summary>
/// <param name="Sent">Messages accepted by their gateway.</param>
/// <param name="Retrying">Messages that failed and will be retried.</param>
/// <param name="Failed">Messages that ran out of attempts.</param>
public sealed record DespatchRun(int Sent, int Retrying, int Failed);

/// <summary>A batch's delivery status.</summary>
/// <param name="Name">The batch.</param>
/// <param name="Queued">Still to send.</param>
/// <param name="Sent">Sent.</param>
/// <param name="Failed">Failed.</param>
/// <param name="Failures">Each failed recipient with the reason.</param>
public sealed record BatchStatus(string Name, int Queued, int Sent, int Failed, IReadOnlyList<string> Failures);

/// <summary>An in-app notification.</summary>
/// <param name="Id">The notification.</param>
/// <param name="Title">The title.</param>
/// <param name="Body">The text.</param>
/// <param name="CreatedAt">When.</param>
/// <param name="Read">Whether it has been read.</param>
public sealed record NotificationEntry(long Id, string Title, string Body, DateTimeOffset CreatedAt, bool Read);

/// <summary>
/// Notifications and communications (brief §8): templates with merge fields per document type, language and channel,
/// previewable; bulk despatch batches with per-message delivery status and retry through the channel gateways; and the
/// in-app notification centre.
/// </summary>
public interface INotificationService
{
    /// <summary>Creates or rewords a template.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="code">The template code.</param>
    /// <param name="documentType">The document it is for.</param>
    /// <param name="language">The language.</param>
    /// <param name="channel">The channel.</param>
    /// <param name="subject">The subject template.</param>
    /// <param name="body">The body template.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The merge fields the template uses, or why it was refused.</returns>
    Task<Result<IReadOnlyList<string>>> SaveTemplateAsync(long companyId, string code, string documentType, string language, MessageChannel channel, string subject, string body, CancellationToken ct = default);

    /// <summary>Previews a template merged with sample values (falls back to English when the language has no template).</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="code">The template.</param>
    /// <param name="language">The language.</param>
    /// <param name="channel">The channel.</param>
    /// <param name="values">The sample values.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The rendered message, or why there is none.</returns>
    Task<Result<RenderedMessage>> PreviewAsync(long companyId, string code, string language, MessageChannel channel, IReadOnlyDictionary<string, string> values, CancellationToken ct = default);

    /// <summary>Queues a bulk despatch; recipients whose values leave fields unmerged are failed at once with the reason.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="name">What the batch is.</param>
    /// <param name="code">The template.</param>
    /// <param name="language">The language.</param>
    /// <param name="channel">The channel.</param>
    /// <param name="recipients">The recipients and their values.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The batch id, or why it was refused.</returns>
    Task<Result<long>> QueueAsync(long companyId, string name, string code, string language, MessageChannel channel, IReadOnlyList<DespatchRecipient> recipients, CancellationToken ct = default);

    /// <summary>Sends queued messages through their gateways, retrying failures up to the attempts allowed.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="maxAttempts">Attempts allowed per message.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>What happened.</returns>
    Task<DespatchRun> DespatchAsync(long companyId, int maxAttempts, CancellationToken ct = default);

    /// <summary>A batch's delivery status.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="batchId">The batch.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The status, or why there is none.</returns>
    Task<Result<BatchStatus>> BatchStatusAsync(long companyId, long batchId, CancellationToken ct = default);

    /// <summary>Puts a notification in a user's notification centre.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="userId">The user.</param>
    /// <param name="title">The title.</param>
    /// <param name="body">The text.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The notification id, or why it was refused.</returns>
    Task<Result<long>> NotifyUserAsync(long companyId, long userId, string title, string body, CancellationToken ct = default);

    /// <summary>A user's notifications, newest first.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="userId">The user.</param>
    /// <param name="unreadOnly">Only the unread ones.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The notifications.</returns>
    Task<IReadOnlyList<NotificationEntry>> InboxAsync(long companyId, long userId, bool unreadOnly, CancellationToken ct = default);

    /// <summary>Marks a user's notification read.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="userId">The user.</param>
    /// <param name="notificationId">The notification.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or why it was refused.</returns>
    Task<Result> MarkReadAsync(long companyId, long userId, long notificationId, CancellationToken ct = default);
}
