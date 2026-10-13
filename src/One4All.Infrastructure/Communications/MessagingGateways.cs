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
    private readonly ICompanyMessagingService? _companies;
    private readonly HttpClient? _http;

    /// <summary>Creates the gateway.</summary>
    /// <param name="options">The messaging settings.</param>
    /// <param name="companies">Each company's own email settings (its SMTP server or Microsoft Graph), over the server's.</param>
    /// <param name="http">The HTTP client Microsoft Graph is called with.</param>
    public SmtpEmailGateway(MessagingOptions options, ICompanyMessagingService? companies = null, HttpClient? http = null)
    {
        _options = options.Smtp;
        _companies = companies;
        _http = http;
    }

    /// <inheritdoc />
    public MessageChannel Channel => MessageChannel.Email;

    /// <inheritdoc />
    public Task<GatewayResult> SendAsync(long companyId, string recipient, string subject, string body, CancellationToken ct = default) => SendCoreAsync(companyId, recipient, subject, body, null, ct);

    /// <inheritdoc />
    public Task<GatewayResult> SendAsync(long companyId, string recipient, string subject, string body, MessageAttachment attachment, CancellationToken ct = default)
        => SendCoreAsync(companyId, recipient, subject, body, attachment, ct);

    private async Task<GatewayResult> SendCoreAsync(long companyId, string recipient, string subject, string body, MessageAttachment? attachment, CancellationToken ct)
    {
        // The company's own delivery, when it has one, over the server's.
        Core.Communications.CompanyMessagingSettings? own = _companies is null ? null : await _companies.ForGatewayAsync(companyId, ct).ConfigureAwait(false);
        if (own?.EmailProvider == Core.Communications.EmailProvider.MicrosoftGraph)
        {
            return _http is null ? new GatewayResult(false, "Microsoft Graph needs an HTTP client on this host.")
                : !MailAddress.TryCreate(recipient?.Trim(), out _) ? new GatewayResult(false, $"\"{recipient}\" is not an email address.")
                : await GraphMailSender.SendAsync(_http, own, recipient!, subject, body, attachment, ct).ConfigureAwait(false);
        }

        SmtpOptions options = own?.EmailProvider == Core.Communications.EmailProvider.Smtp
            ? new SmtpOptions { Host = own.SmtpHost, Port = own.SmtpPort, UseTls = own.SmtpUseTls, Username = own.SmtpUsername, Password = own.SmtpPassword, From = own.FromAddress ?? string.Empty, FromName = own.FromName }
            : _options;
        return await SendSmtpAsync(options, recipient, subject, body, attachment, ct).ConfigureAwait(false);
    }

    private static async Task<GatewayResult> SendSmtpAsync(SmtpOptions options, string recipient, string subject, string body, MessageAttachment? attachment, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(options.Host) || string.IsNullOrWhiteSpace(options.From))
        {
            return new GatewayResult(false, "Email is not configured (Messaging:Smtp).");
        }

        if (!MailAddress.TryCreate(recipient?.Trim(), out MailAddress? to))
        {
            return new GatewayResult(false, $"\"{recipient}\" is not an email address.");
        }

        using MailMessage message = new(new MailAddress(options.From, options.FromName), to) { Subject = subject, Body = body, BodyEncoding = Encoding.UTF8, SubjectEncoding = Encoding.UTF8 };
        string id = $"<{Guid.NewGuid():N}@one4all>";
        message.Headers.Add("Message-ID", id);
        using MemoryStream? content = attachment is null ? null : new MemoryStream(attachment.Content);
        if (attachment is not null)
        {
            message.Attachments.Add(new Attachment(content!, attachment.Name, attachment.ContentType));
        }

        using SmtpClient client = new(options.Host, options.Port) { EnableSsl = options.UseTls, DeliveryMethod = SmtpDeliveryMethod.Network };
        if (!string.IsNullOrWhiteSpace(options.Username))
        {
            client.Credentials = new NetworkCredential(options.Username, options.Password);
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
    private readonly ICompanyMessagingService? _companies;

    /// <summary>Creates the gateway.</summary>
    /// <param name="http">The HTTP client.</param>
    /// <param name="options">The messaging settings.</param>
    /// <param name="companies">Each company's own SMS provider, over the server's.</param>
    public HttpSmsGateway(HttpClient http, MessagingOptions options, ICompanyMessagingService? companies = null)
    {
        _http = http;
        _options = options.Sms;
        _companies = companies;
    }

    /// <inheritdoc />
    public MessageChannel Channel => MessageChannel.Sms;

    /// <inheritdoc />
    public async Task<GatewayResult> SendAsync(long companyId, string recipient, string subject, string body, CancellationToken ct = default)
    {
        Core.Communications.CompanyMessagingSettings? own = _companies is null ? null : await _companies.ForGatewayAsync(companyId, ct).ConfigureAwait(false);
        SmsOptions sms = own?.SmsUrl is string url
            ? new SmsOptions
            {
                Url = url, BodyTemplate = own.SmsBodyTemplate ?? _options.BodyTemplate, ContentType = own.SmsContentType ?? "application/json", AuthHeader = own.SmsAuthHeader,
                AuthValue = own.SmsAuthValue, From = own.SmsFrom,
            }
            : _options;
        if (string.IsNullOrWhiteSpace(sms.Url))
        {
            return new GatewayResult(false, "SMS is not configured (Messaging:Sms).");
        }

        if (SmsRequest.Number(recipient) is not string number)
        {
            return new GatewayResult(false, $"\"{recipient}\" is not a phone number.");
        }

        using HttpRequestMessage request = new(HttpMethod.Post, sms.Url)
        {
            Content = new StringContent(SmsRequest.Fill(sms.BodyTemplate, sms.ContentType, number, body, sms.From), Encoding.UTF8, sms.ContentType),
        };
        if (!string.IsNullOrWhiteSpace(sms.AuthHeader))
        {
            request.Headers.TryAddWithoutValidation(sms.AuthHeader, sms.AuthValue);
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
