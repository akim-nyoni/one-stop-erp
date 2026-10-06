using One4All.Core.Common;

namespace One4All.Core.Communications;

/// <summary>How a message is delivered (brief §8).</summary>
public enum MessageChannel
{
    /// <summary>The in-app notification centre.</summary>
    InApp = 1,

    /// <summary>Email (SMTP / Graph).</summary>
    Email = 2,

    /// <summary>SMS.</summary>
    Sms = 3,

    /// <summary>WhatsApp (Cloud API), with delivery and read receipts.</summary>
    WhatsApp = 4,
}

/// <summary>Where an outbound message stands.</summary>
public enum MessageStatus
{
    /// <summary>Waiting to be sent (or to be retried).</summary>
    Queued = 1,

    /// <summary>Accepted by the gateway.</summary>
    Sent = 2,

    /// <summary>Gave up: it could not be merged, or every attempt failed.</summary>
    Failed = 3,
}

/// <summary>
/// A message template (<c>msg.message_template</c>, brief §8): per document type, language and channel, a subject and body
/// with <c>{{Field}}</c> merge fields. One per code, language and channel.
/// </summary>
public sealed class MessageTemplate : AuditableEntity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private MessageTemplate()
    {
        Code = string.Empty;
        DocumentType = string.Empty;
        Language = string.Empty;
        Subject = string.Empty;
        Body = string.Empty;
    }

    /// <summary>Creates a template.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="code">The template code.</param>
    /// <param name="documentType">The document it is for (statement, bill, dunning letter, fee reminder...).</param>
    /// <param name="language">The language (e.g. en, sn, nd).</param>
    /// <param name="channel">The channel.</param>
    /// <param name="subject">The subject template.</param>
    /// <param name="body">The body template.</param>
    public MessageTemplate(long companyId, string code, string documentType, string language, MessageChannel channel, string subject, string body)
    {
        CompanyId = companyId;
        Code = code;
        DocumentType = documentType;
        Language = language;
        Channel = channel;
        Subject = subject;
        Body = body;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The template code.</summary>
    public string Code { get; private set; }

    /// <summary>The document it is for.</summary>
    public string DocumentType { get; private set; }

    /// <summary>The language.</summary>
    public string Language { get; private set; }

    /// <summary>The channel.</summary>
    public MessageChannel Channel { get; private set; }

    /// <summary>The subject template.</summary>
    public string Subject { get; private set; }

    /// <summary>The body template.</summary>
    public string Body { get; private set; }

    /// <summary>Replaces the wording.</summary>
    /// <param name="documentType">The document it is for.</param>
    /// <param name="subject">The subject template.</param>
    /// <param name="body">The body template.</param>
    public void Reword(string documentType, string subject, string body)
    {
        DocumentType = documentType;
        Subject = subject;
        Body = body;
    }
}

/// <summary>A bulk despatch (<c>msg.despatch_batch</c>): the messages queued together from one template.</summary>
public sealed class DespatchBatch : AuditableEntity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private DespatchBatch()
    {
        Name = string.Empty;
        TemplateCode = string.Empty;
    }

    /// <summary>Creates a batch.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="name">What it is (e.g. "March statements").</param>
    /// <param name="templateCode">The template used.</param>
    /// <param name="queuedOn">When.</param>
    public DespatchBatch(long companyId, string name, string templateCode, DateTimeOffset queuedOn)
    {
        CompanyId = companyId;
        Name = name;
        TemplateCode = templateCode;
        QueuedOn = queuedOn;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>What it is.</summary>
    public string Name { get; private set; }

    /// <summary>The template used.</summary>
    public string TemplateCode { get; private set; }

    /// <summary>When it was queued.</summary>
    public DateTimeOffset QueuedOn { get; private set; }
}

/// <summary>An outbound message (<c>msg.outbound_message</c>) with its delivery status and attempts.</summary>
public sealed class OutboundMessage : Entity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private OutboundMessage()
    {
        Recipient = string.Empty;
        Subject = string.Empty;
        Body = string.Empty;
    }

    /// <summary>Queues a message.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="batchId">The batch, if any.</param>
    /// <param name="channel">The channel.</param>
    /// <param name="recipient">The address, number or user id.</param>
    /// <param name="subject">The subject.</param>
    /// <param name="body">The body.</param>
    /// <param name="queuedAt">When.</param>
    public OutboundMessage(long companyId, long? batchId, MessageChannel channel, string recipient, string subject, string body, DateTimeOffset queuedAt)
    {
        CompanyId = companyId;
        BatchId = batchId;
        Channel = channel;
        Recipient = recipient;
        Subject = subject;
        Body = body;
        QueuedAt = queuedAt;
        Status = MessageStatus.Queued;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The batch, if any.</summary>
    public long? BatchId { get; private set; }

    /// <summary>The channel.</summary>
    public MessageChannel Channel { get; private set; }

    /// <summary>The address, number or user id.</summary>
    public string Recipient { get; private set; }

    /// <summary>The subject.</summary>
    public string Subject { get; private set; }

    /// <summary>The body.</summary>
    public string Body { get; private set; }

    /// <summary>Where it stands.</summary>
    public MessageStatus Status { get; private set; }

    /// <summary>Delivery attempts made.</summary>
    public int Attempts { get; private set; }

    /// <summary>The last failure.</summary>
    public string? LastError { get; private set; }

    /// <summary>The gateway's reference once sent.</summary>
    public string? ProviderReference { get; private set; }

    /// <summary>When it was queued.</summary>
    public DateTimeOffset QueuedAt { get; private set; }

    /// <summary>When it was sent.</summary>
    public DateTimeOffset? SentAt { get; private set; }

    /// <summary>The attached document's file name, if any.</summary>
    public string? AttachmentName { get; private set; }

    /// <summary>The attached document's content type.</summary>
    public string? AttachmentContentType { get; private set; }

    /// <summary>The attached document.</summary>
    public byte[]? AttachmentContent { get; private set; }

    /// <summary>Attaches a document (a report, a statement...).</summary>
    /// <param name="name">Its file name.</param>
    /// <param name="contentType">Its content type.</param>
    /// <param name="content">Its bytes.</param>
    public void Attach(string name, string contentType, byte[] content)
    {
        AttachmentName = name;
        AttachmentContentType = contentType;
        AttachmentContent = content;
    }

    /// <summary>Gets when the provider reported it delivered to the handset.</summary>
    public DateTimeOffset? DeliveredAt { get; private set; }

    /// <summary>Gets when the provider reported it read.</summary>
    public DateTimeOffset? ReadAt { get; private set; }

    /// <summary>
    /// Records a receipt from the provider: delivered, read (which implies delivered), or failed after sending (the handset could not be
    /// reached, the 24-hour window had closed...). Receipts arrive out of order, so a later state is never undone by an earlier one.
    /// </summary>
    /// <param name="status">The provider's status (sent, delivered, read, failed).</param>
    /// <param name="at">When it happened.</param>
    /// <param name="error">The provider's error, for a failure.</param>
    /// <returns>True when it changed anything.</returns>
    public bool RecordReceipt(string status, DateTimeOffset at, string? error = null)
    {
        switch ((status ?? string.Empty).ToLowerInvariant())
        {
            case "delivered" when DeliveredAt is null:
                DeliveredAt = at;
                return true;
            case "read" when ReadAt is null:
                ReadAt = at;
                DeliveredAt ??= at;
                return true;
            case "failed" when DeliveredAt is null && Status != MessageStatus.Failed:
                Status = MessageStatus.Failed;
                LastError = string.IsNullOrWhiteSpace(error) ? "The provider could not deliver it." : (error.Length > 1000 ? error[..1000] : error);
                return true;
            default:
                return false;
        }
    }

    /// <summary>Records a successful attempt.</summary>
    /// <param name="reference">The gateway's reference.</param>
    /// <param name="at">When.</param>
    public void MarkSent(string reference, DateTimeOffset at)
    {
        Attempts++;
        Status = MessageStatus.Sent;
        ProviderReference = reference;
        SentAt = at;
        LastError = null;
    }

    /// <summary>Records a failed attempt; it stays queued for retry until the attempts run out.</summary>
    /// <param name="error">Why.</param>
    /// <param name="maxAttempts">The attempts allowed.</param>
    public void MarkFailedAttempt(string error, int maxAttempts)
    {
        Attempts++;
        LastError = error;
        Status = Attempts >= maxAttempts ? MessageStatus.Failed : MessageStatus.Queued;
    }

    /// <summary>Fails it without an attempt (it could not be prepared).</summary>
    /// <param name="error">Why.</param>
    public void Reject(string error)
    {
        Status = MessageStatus.Failed;
        LastError = error;
    }
}

/// <summary>An in-app notification for a user (<c>msg.notification</c>) — the notification centre.</summary>
public sealed class UserNotification : Entity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private UserNotification()
    {
        Title = string.Empty;
        Body = string.Empty;
    }

    /// <summary>Creates a notification.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="userId">The user.</param>
    /// <param name="title">The title.</param>
    /// <param name="body">The text.</param>
    /// <param name="createdAt">When.</param>
    public UserNotification(long companyId, long userId, string title, string body, DateTimeOffset createdAt)
    {
        CompanyId = companyId;
        UserId = userId;
        Title = title;
        Body = body;
        CreatedAt = createdAt;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The user.</summary>
    public long UserId { get; private set; }

    /// <summary>The title.</summary>
    public string Title { get; private set; }

    /// <summary>The text.</summary>
    public string Body { get; private set; }

    /// <summary>When.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>When the user read it.</summary>
    public DateTimeOffset? ReadAt { get; private set; }

    /// <summary>Marks it read.</summary>
    /// <param name="at">When.</param>
    public void MarkRead(DateTimeOffset at) => ReadAt ??= at;
}

/// <summary>A message received from outside (a WhatsApp reply), matched to the customer or lead whose number it came from (<c>comms.inbound_message</c>).</summary>
public sealed class InboundMessage : Entity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private InboundMessage()
    {
        From = string.Empty;
        Body = string.Empty;
        ProviderReference = string.Empty;
    }

    /// <summary>Records a received message.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="channel">The channel.</param>
    /// <param name="from">The sender's number.</param>
    /// <param name="senderName">The sender's profile name.</param>
    /// <param name="body">The text (or a description of non-text content).</param>
    /// <param name="receivedAt">When it was sent.</param>
    /// <param name="providerReference">The provider's message id (one record per id).</param>
    public InboundMessage(long companyId, MessageChannel channel, string from, string? senderName, string body, DateTimeOffset receivedAt, string providerReference)
    {
        CompanyId = companyId;
        Channel = channel;
        From = from;
        SenderName = senderName;
        Body = body;
        ReceivedAt = receivedAt;
        ProviderReference = providerReference;
    }

    /// <summary>Gets the company.</summary>
    public long CompanyId { get; private set; }

    /// <summary>Gets the channel.</summary>
    public MessageChannel Channel { get; private set; }

    /// <summary>Gets the sender's number.</summary>
    public string From { get; private set; }

    /// <summary>Gets the sender's profile name.</summary>
    public string? SenderName { get; private set; }

    /// <summary>Gets the text.</summary>
    public string Body { get; private set; }

    /// <summary>Gets when it was sent.</summary>
    public DateTimeOffset ReceivedAt { get; private set; }

    /// <summary>Gets the provider's message id.</summary>
    public string ProviderReference { get; private set; }

    /// <summary>Gets or sets the customer whose number it came from.</summary>
    public long? CustomerId { get; set; }

    /// <summary>Gets or sets the CRM lead whose number it came from.</summary>
    public long? LeadId { get; set; }

    /// <summary>Gets or sets when someone dealt with it.</summary>
    public DateTimeOffset? HandledAt { get; set; }

    /// <summary>Gets or sets who dealt with it.</summary>
    public long? HandledByUserId { get; set; }
}
