using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Communications;
using One4All.Application.Communications;
using One4All.Core.Common;
using One4All.Core.Communications;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Communications;

/// <summary>Templates, bulk despatch with retry through the channel gateways, and the in-app notification centre.</summary>
public sealed class NotificationService : INotificationService
{
    private const string DefaultLanguage = "en";

    private readonly One4AllDbContext _db;
    private readonly Dictionary<MessageChannel, IMessageGateway> _gateways;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="gateways">The registered gateways (the last registered for a channel wins).</param>
    public NotificationService(One4AllDbContext db, IEnumerable<IMessageGateway> gateways)
    {
        ArgumentNullException.ThrowIfNull(gateways);
        _db = db;
        _gateways = [];
        foreach (IMessageGateway gateway in gateways)
        {
            _gateways[gateway.Channel] = gateway;
        }
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<string>>> SaveTemplateAsync(long companyId, string code, string documentType, string language, MessageChannel channel, string subject, string body, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(documentType) || string.IsNullOrWhiteSpace(language) || !Enum.IsDefined(channel)
            || string.IsNullOrWhiteSpace(body) || (channel == MessageChannel.Email && string.IsNullOrWhiteSpace(subject)))
        {
            return Error.Validation("Msg.Template", "A template needs a code, document type, language, channel and body (and a subject for email).");
        }

        string key = code.Trim().ToUpperInvariant();
        string lang = language.Trim().ToLowerInvariant();
        MessageTemplate? template = await _db.Set<MessageTemplate>()
            .FirstOrDefaultAsync(t => t.CompanyId == companyId && t.Code == key && t.Language == lang && t.Channel == channel, ct).ConfigureAwait(false);
        if (template is null)
        {
            _db.Set<MessageTemplate>().Add(new MessageTemplate(companyId, key, documentType.Trim(), lang, channel, subject?.Trim() ?? string.Empty, body));
        }
        else
        {
            template.Reword(documentType.Trim(), subject?.Trim() ?? string.Empty, body);
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result<IReadOnlyList<string>>.Success([.. TemplateRenderer.Fields(subject ?? string.Empty).Concat(TemplateRenderer.Fields(body)).Distinct(StringComparer.OrdinalIgnoreCase)]);
    }

    /// <inheritdoc />
    public async Task<Result<RenderedMessage>> PreviewAsync(long companyId, string code, string language, MessageChannel channel, IReadOnlyDictionary<string, string> values, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(values);
        Result<MessageTemplate> template = await TemplateAsync(companyId, code, language, channel, ct).ConfigureAwait(false);
        return template.IsFailure ? template.Error : TemplateRenderer.Render(template.Value.Subject, template.Value.Body, values);
    }

    /// <inheritdoc />
    public async Task<Result<long>> QueueAsync(long companyId, string name, string code, string language, MessageChannel channel, IReadOnlyList<DespatchRecipient> recipients, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(recipients);
        if (string.IsNullOrWhiteSpace(name) || recipients.Count == 0)
        {
            return Error.Validation("Msg.Batch", "A despatch needs a name and at least one recipient.");
        }

        Result<MessageTemplate> template = await TemplateAsync(companyId, code, language, channel, ct).ConfigureAwait(false);
        if (template.IsFailure)
        {
            return template.Error;
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        DespatchBatch batch = new(companyId, name.Trim(), template.Value.Code, now);
        _db.Set<DespatchBatch>().Add(batch);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        foreach (DespatchRecipient recipient in recipients)
        {
            RenderedMessage rendered = TemplateRenderer.Render(template.Value.Subject, template.Value.Body, recipient.Fields);
            OutboundMessage message = new(companyId, batch.Id, channel, (recipient.Recipient ?? string.Empty).Trim(), rendered.Subject, rendered.Body, now);
            if (string.IsNullOrWhiteSpace(recipient.Recipient))
            {
                message.Reject("No recipient address.");
            }
            else if (rendered.MissingFields.Count > 0)
            {
                message.Reject($"Missing merge fields: {string.Join(", ", rendered.MissingFields)}.");
            }

            _db.Set<OutboundMessage>().Add(message);
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return batch.Id;
    }

    /// <inheritdoc />
    public async Task<Result<long>> QueueDirectAsync(long companyId, string name, MessageChannel channel, IReadOnlyList<string> recipients, string subject, string body, MessageAttachment? attachment, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(recipients);
        if (string.IsNullOrWhiteSpace(name) || recipients.Count == 0 || !Enum.IsDefined(channel) || string.IsNullOrWhiteSpace(body))
        {
            return Error.Validation("Msg.Batch", "A despatch needs a name, a channel, a body and at least one recipient.");
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        DespatchBatch batch = new(companyId, name.Trim(), "(direct)", now);
        _db.Set<DespatchBatch>().Add(batch);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        foreach (string recipient in recipients)
        {
            OutboundMessage message = new(companyId, batch.Id, channel, (recipient ?? string.Empty).Trim(), subject ?? string.Empty, body, now);
            if (attachment is not null)
            {
                message.Attach(attachment.Name, attachment.ContentType, attachment.Content);
            }

            if (string.IsNullOrWhiteSpace(recipient))
            {
                message.Reject("No recipient address.");
            }

            _db.Set<OutboundMessage>().Add(message);
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return batch.Id;
    }

    /// <inheritdoc />
    public async Task<DespatchRun> DespatchAsync(long companyId, int maxAttempts, CancellationToken ct = default)
    {
        int allowed = Math.Max(1, maxAttempts);
        List<OutboundMessage> queued = await _db.Set<OutboundMessage>().Where(m => m.CompanyId == companyId && m.Status == MessageStatus.Queued)
            .OrderBy(m => m.Id).ToListAsync(ct).ConfigureAwait(false);
        int sent = 0, retrying = 0, failed = 0;
        foreach (OutboundMessage message in queued)
        {
            GatewayResult result;
            if (!_gateways.TryGetValue(message.Channel, out IMessageGateway? gateway))
            {
                result = new GatewayResult(false, $"No {message.Channel} gateway is configured.");
            }
            else
            {
                try
                {
                    result = message.AttachmentContent is byte[] content
                        ? await gateway.SendAsync(companyId, message.Recipient, message.Subject, message.Body, new MessageAttachment(message.AttachmentName!, message.AttachmentContentType!, content), ct).ConfigureAwait(false)
                        : await gateway.SendAsync(companyId, message.Recipient, message.Subject, message.Body, ct).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    result = new GatewayResult(false, ex.Message);
                }
            }

            if (result.Accepted)
            {
                message.MarkSent(result.Reference, DateTimeOffset.UtcNow);
                sent++;
            }
            else
            {
                message.MarkFailedAttempt(result.Reference, allowed);
                if (message.Status == MessageStatus.Failed)
                {
                    failed++;
                }
                else
                {
                    retrying++;
                }
            }

            await _db.SaveChangesAsync(ct).ConfigureAwait(false); // each message's outcome is kept even if a later one throws
        }

        return new DespatchRun(sent, retrying, failed);
    }

    /// <inheritdoc />
    public async Task<Result<BatchStatus>> BatchStatusAsync(long companyId, long batchId, CancellationToken ct = default)
    {
        DespatchBatch? batch = await _db.Set<DespatchBatch>().AsNoTracking().FirstOrDefaultAsync(b => b.Id == batchId && b.CompanyId == companyId, ct).ConfigureAwait(false);
        if (batch is null)
        {
            return Error.NotFound("Msg.BatchNotFound", $"Despatch batch {batchId} not found.");
        }

        var messages = await _db.Set<OutboundMessage>().AsNoTracking().Where(m => m.BatchId == batchId).OrderBy(m => m.Id)
            .Select(m => new { m.Recipient, m.Status, m.LastError }).ToListAsync(ct).ConfigureAwait(false);
        return new BatchStatus(batch.Name, messages.Count(m => m.Status == MessageStatus.Queued), messages.Count(m => m.Status == MessageStatus.Sent),
            messages.Count(m => m.Status == MessageStatus.Failed), [.. messages.Where(m => m.Status == MessageStatus.Failed).Select(m => $"{m.Recipient}: {m.LastError}")]);
    }

    /// <inheritdoc />
    public async Task<Result<long>> NotifyUserAsync(long companyId, long userId, string title, string body, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return Error.Validation("Msg.Notification", "A notification needs a title.");
        }

        if (!await _db.Users.AnyAsync(u => u.Id == userId, ct).ConfigureAwait(false))
        {
            return Error.NotFound("Msg.UserNotFound", $"User {userId} not found.");
        }

        UserNotification notification = new(companyId, userId, title.Trim(), body ?? string.Empty, DateTimeOffset.UtcNow);
        _db.Set<UserNotification>().Add(notification);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return notification.Id;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<NotificationEntry>> InboxAsync(long companyId, long userId, bool unreadOnly, CancellationToken ct = default)
        => await _db.Set<UserNotification>().AsNoTracking()
            .Where(n => n.CompanyId == companyId && n.UserId == userId && (!unreadOnly || n.ReadAt == null))
            .OrderByDescending(n => n.CreatedAt).ThenByDescending(n => n.Id)
            .Select(n => new NotificationEntry(n.Id, n.Title, n.Body, n.CreatedAt, n.ReadAt != null)).ToListAsync(ct).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<Result> MarkReadAsync(long companyId, long userId, long notificationId, CancellationToken ct = default)
    {
        UserNotification? notification = await _db.Set<UserNotification>()
            .FirstOrDefaultAsync(n => n.Id == notificationId && n.CompanyId == companyId && n.UserId == userId, ct).ConfigureAwait(false);
        if (notification is null)
        {
            return Result.Failure(Error.NotFound("Msg.NotificationNotFound", $"Notification {notificationId} not found for the user."));
        }

        notification.MarkRead(DateTimeOffset.UtcNow);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    // The template in the language asked for, else in English.
    private async Task<Result<MessageTemplate>> TemplateAsync(long companyId, string code, string language, MessageChannel channel, CancellationToken ct)
    {
        string key = (code ?? string.Empty).Trim().ToUpperInvariant();
        string lang = (language ?? DefaultLanguage).Trim().ToLowerInvariant();
        List<MessageTemplate> candidates = await _db.Set<MessageTemplate>().AsNoTracking()
            .Where(t => t.CompanyId == companyId && t.Code == key && t.Channel == channel && (t.Language == lang || t.Language == DefaultLanguage)).ToListAsync(ct).ConfigureAwait(false);
        MessageTemplate? template = candidates.FirstOrDefault(t => t.Language == lang) ?? candidates.FirstOrDefault();
        if (template is null)
        {
            return Error.NotFound("Msg.TemplateNotFound", $"No {channel} template {key} in {lang} or {DefaultLanguage}.");
        }

        return template;
    }
}

/// <summary>Delivers in-app messages straight into the recipient user's notification centre (the recipient is the user id).</summary>
public sealed class InAppGateway : IMessageGateway
{
    private readonly One4AllDbContext _db;

    /// <summary>Creates the gateway.</summary>
    /// <param name="db">The database context.</param>
    public InAppGateway(One4AllDbContext db) => _db = db;

    /// <inheritdoc />
    public MessageChannel Channel => MessageChannel.InApp;

    /// <inheritdoc />
    public async Task<GatewayResult> SendAsync(long companyId, string recipient, string subject, string body, CancellationToken ct = default)
    {
        if (!long.TryParse(recipient, out long userId) || !await _db.Users.AnyAsync(u => u.Id == userId, ct).ConfigureAwait(false))
        {
            return new GatewayResult(false, $"\"{recipient}\" is not a user.");
        }

        UserNotification notification = new(companyId, userId, subject, body, DateTimeOffset.UtcNow);
        _db.Set<UserNotification>().Add(notification);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return new GatewayResult(true, $"N{notification.Id}");
    }
}
