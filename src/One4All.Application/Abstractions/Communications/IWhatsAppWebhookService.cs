using One4All.Core.Common;

namespace One4All.Application.Abstractions.Communications;

/// <summary>What a webhook delivery carried.</summary>
/// <param name="Receipts">Delivery / read / failure receipts applied to sent messages.</param>
/// <param name="Received">New inbound messages recorded.</param>
/// <param name="Ignored">Items not applied (unknown message, repeat delivery, unsupported event).</param>
public sealed record WhatsAppWebhookResult(int Receipts, int Received, int Ignored);

/// <summary>A received message awaiting attention.</summary>
/// <param name="Id">The record.</param>
/// <param name="From">The sender's number.</param>
/// <param name="SenderName">Their profile name.</param>
/// <param name="Body">The text.</param>
/// <param name="ReceivedAt">When.</param>
/// <param name="CustomerId">The matched customer.</param>
/// <param name="LeadId">The matched lead.</param>
public sealed record InboundMessageLine(long Id, string From, string? SenderName, string Body, DateTimeOffset ReceivedAt, long? CustomerId, long? LeadId);

/// <summary>
/// The WhatsApp Cloud API webhook (brief §9 messaging): the subscription handshake, and signed deliveries of receipts (sent, delivered,
/// read, failed - applied to the outbound message by its provider id) and of customers' replies (recorded once each, matched to the
/// customer or lead with that number, and listed until handled).
/// </summary>
public interface IWhatsAppWebhookService
{
    /// <summary>Answers Meta's subscription check: the challenge when the mode and verify token match, otherwise null.</summary>
    /// <param name="mode">hub.mode.</param>
    /// <param name="verifyToken">hub.verify_token.</param>
    /// <param name="challenge">hub.challenge.</param>
    /// <returns>The challenge to echo, or null to refuse.</returns>
    string? VerifySubscription(string? mode, string? verifyToken, string? challenge);

    /// <summary>Processes a delivery, after checking its <c>X-Hub-Signature-256</c> against the app secret.</summary>
    /// <param name="body">The raw request body.</param>
    /// <param name="signature">The signature header.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>What it carried, or why it was refused.</returns>
    Task<Result<WhatsAppWebhookResult>> ProcessAsync(byte[] body, string? signature, CancellationToken ct = default);

    /// <summary>Received messages not yet handled, oldest first.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The messages.</returns>
    Task<IReadOnlyList<InboundMessageLine>> UnhandledAsync(long companyId, CancellationToken ct = default);

    /// <summary>Marks a received message handled.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="inboundMessageId">The message.</param>
    /// <param name="userId">Who dealt with it.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success or why not.</returns>
    Task<Result> MarkHandledAsync(long companyId, long inboundMessageId, long userId, CancellationToken ct = default);
}
