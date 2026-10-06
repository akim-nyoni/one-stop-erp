using System.Net;
using System.Net.Mail;
using System.Text;
using One4All.Application.Abstractions.Communications;
using One4All.Application.Communications;
using One4All.Core.Communications;

namespace One4All.Infrastructure.Communications;

/// <summary>SMTP settings (configuration section <c>Messaging:Smtp</c>; keep the password in a secret store or environment variable).</summary>
public sealed class SmtpOptions
{
    /// <summary>Gets or sets the server host (blank: email is not configured).</summary>
    public string? Host { get; set; }

    /// <summary>Gets or sets the port.</summary>
    public int Port { get; set; } = 587;

    /// <summary>Gets or sets a value indicating whether to use TLS.</summary>
    public bool UseTls { get; set; } = true;

    /// <summary>Gets or sets the login name, if the server needs one.</summary>
    public string? Username { get; set; }

    /// <summary>Gets or sets the password.</summary>
    public string? Password { get; set; }

    /// <summary>Gets or sets the sender address.</summary>
    public string From { get; set; } = string.Empty;

    /// <summary>Gets or sets the sender's display name.</summary>
    public string? FromName { get; set; }
}

/// <summary>An HTTP SMS provider (configuration section <c>Messaging:Sms</c>).</summary>
public sealed class SmsOptions
{
    /// <summary>Gets or sets the provider's endpoint (blank: SMS is not configured).</summary>
    public string? Url { get; set; }

    /// <summary>Gets or sets the request body template with {to}, {body} and {from}.</summary>
    public string BodyTemplate { get; set; } = "{\"to\":\"{to}\",\"from\":\"{from}\",\"text\":\"{body}\"}";

    /// <summary>Gets or sets the request content type.</summary>
    public string ContentType { get; set; } = "application/json";

    /// <summary>Gets or sets the authentication header name (e.g. Authorization), if any.</summary>
    public string? AuthHeader { get; set; }

    /// <summary>Gets or sets the authentication header value (keep it in a secret store).</summary>
    public string? AuthValue { get; set; }

    /// <summary>Gets or sets the sender id.</summary>
    public string? From { get; set; }
}

/// <summary>Messaging gateway settings (configuration section <c>Messaging</c>).</summary>
public sealed class MessagingOptions
{
    /// <summary>The configuration section name.</summary>
    public const string SectionName = "Messaging";

    /// <summary>Gets or sets the email settings.</summary>
    public SmtpOptions Smtp { get; set; } = new();

    /// <summary>Gets or sets the SMS settings.</summary>
    public SmsOptions Sms { get; set; } = new();

    /// <summary>Gets or sets the WhatsApp Cloud API settings.</summary>
    public WhatsAppOptions WhatsApp { get; set; } = new();
}

/// <summary>Sends email through SMTP (brief §8), with an attachment when given.</summary>
public sealed class SmtpEmailGateway : IMessageGateway
{
    private readonly SmtpOptions _options;

    /// <summary>Creates the gateway.</summary>
    /// <param name="options">The messaging settings.</param>
    public SmtpEmailGateway(MessagingOptions options) => _options = options.Smtp;

    /// <inheritdoc />
    public MessageChannel Channel => MessageChannel.Email;

    /// <inheritdoc />
    public Task<GatewayResult> SendAsync(long companyId, string recipient, string subject, string body, CancellationToken ct = default) => SendCoreAsync(recipient, subject, body, null, ct);

    /// <inheritdoc />
    public Task<GatewayResult> SendAsync(long companyId, string recipient, string subject, string body, MessageAttachment attachment, CancellationToken ct = default)
        => SendCoreAsync(recipient, subject, body, attachment, ct);

    private async Task<GatewayResult> SendCoreAsync(string recipient, string subject, string body, MessageAttachment? attachment, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_options.Host) || string.IsNullOrWhiteSpace(_options.From))
        {
            return new GatewayResult(false, "Email is not configured (Messaging:Smtp).");
        }

        if (!MailAddress.TryCreate(recipient?.Trim(), out MailAddress? to))
        {
            return new GatewayResult(false, $"\"{recipient}\" is not an email address.");
        }

        using MailMessage message = new(new MailAddress(_options.From, _options.FromName), to) { Subject = subject, Body = body, BodyEncoding = Encoding.UTF8, SubjectEncoding = Encoding.UTF8 };
        string id = $"<{Guid.NewGuid():N}@one4all>";
        message.Headers.Add("Message-ID", id);
        using MemoryStream? content = attachment is null ? null : new MemoryStream(attachment.Content);
        if (attachment is not null)
        {
            message.Attachments.Add(new Attachment(content!, attachment.Name, attachment.ContentType));
        }

        using SmtpClient client = new(_options.Host, _options.Port) { EnableSsl = _options.UseTls, DeliveryMethod = SmtpDeliveryMethod.Network };
        if (!string.IsNullOrWhiteSpace(_options.Username))
        {
            client.Credentials = new NetworkCredential(_options.Username, _options.Password);
        }

        try
        {
            await client.SendMailAsync(message, ct).ConfigureAwait(false);
            return new GatewayResult(true, id);
        }
        catch (SmtpException ex)
        {
            return new GatewayResult(false, $"SMTP refused: {ex.Message}");
        }
    }
}

/// <summary>Sends SMS through a provider's HTTP API (brief §8 gateway abstraction): the request is the configured template filled in.</summary>
public sealed class HttpSmsGateway : IMessageGateway
{
    private readonly HttpClient _http;
    private readonly SmsOptions _options;

    /// <summary>Creates the gateway.</summary>
    /// <param name="http">The HTTP client.</param>
    /// <param name="options">The messaging settings.</param>
    public HttpSmsGateway(HttpClient http, MessagingOptions options)
    {
        _http = http;
        _options = options.Sms;
    }

    /// <inheritdoc />
    public MessageChannel Channel => MessageChannel.Sms;

    /// <inheritdoc />
    public async Task<GatewayResult> SendAsync(long companyId, string recipient, string subject, string body, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_options.Url))
        {
            return new GatewayResult(false, "SMS is not configured (Messaging:Sms).");
        }

        if (SmsRequest.Number(recipient) is not string number)
        {
            return new GatewayResult(false, $"\"{recipient}\" is not a phone number.");
        }

        using HttpRequestMessage request = new(HttpMethod.Post, _options.Url)
        {
            Content = new StringContent(SmsRequest.Fill(_options.BodyTemplate, _options.ContentType, number, body, _options.From), Encoding.UTF8, _options.ContentType),
        };
        if (!string.IsNullOrWhiteSpace(_options.AuthHeader))
        {
            request.Headers.TryAddWithoutValidation(_options.AuthHeader, _options.AuthValue);
        }

        try
        {
            using HttpResponseMessage response = await _http.SendAsync(request, ct).ConfigureAwait(false);
            string reply = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            string reference = reply.Length > 200 ? reply[..200] : reply;
            return response.IsSuccessStatusCode ? new GatewayResult(true, reference) : new GatewayResult(false, $"Provider refused ({(int)response.StatusCode}): {reference}");
        }
        catch (HttpRequestException ex)
        {
            return new GatewayResult(false, $"Provider unreachable: {ex.Message}");
        }
    }
}
