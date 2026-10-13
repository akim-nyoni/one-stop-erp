using One4All.Core.Common;

namespace One4All.Core.Communications;

/// <summary>How a company sends email.</summary>
public enum EmailProvider
{
    /// <summary>The server's configured SMTP settings.</summary>
    Default = 0,

    /// <summary>The company's own SMTP server.</summary>
    Smtp = 1,

    /// <summary>Microsoft 365 through the Microsoft Graph API (an app registration with Mail.Send).</summary>
    MicrosoftGraph = 2,
}

/// <summary>
/// A company's own email and SMS delivery (<c>sys.company_messaging</c>, brief §8 "per company SMTP/Graph and SMS gateway"): its SMTP server or
/// Microsoft Graph mailbox, and its SMS provider, in place of the server-wide settings. Passwords, client secrets and API keys are stored encrypted.
/// </summary>
public sealed class CompanyMessagingSettings : AuditableEntity, ICompanyScoped
{
    private CompanyMessagingSettings()
    {
    }

    /// <summary>Creates a company's settings.</summary>
    /// <param name="companyId">The company.</param>
    public CompanyMessagingSettings(long companyId) => CompanyId = companyId;

    /// <summary>Gets the company.</summary>
    public long CompanyId { get; private set; }

    /// <summary>Gets or sets how the company sends email.</summary>
    public EmailProvider EmailProvider { get; set; }

    /// <summary>Gets or sets the SMTP host.</summary>
    public string? SmtpHost { get; set; }

    /// <summary>Gets or sets the SMTP port.</summary>
    public int SmtpPort { get; set; } = 587;

    /// <summary>Gets or sets a value indicating whether SMTP uses TLS.</summary>
    public bool SmtpUseTls { get; set; } = true;

    /// <summary>Gets or sets the SMTP login.</summary>
    public string? SmtpUsername { get; set; }

    /// <summary>Gets or sets the SMTP password (encrypted at rest).</summary>
    public string? SmtpPassword { get; set; }

    /// <summary>Gets or sets the sender address (SMTP), or the mailbox sent from (Graph).</summary>
    public string? FromAddress { get; set; }

    /// <summary>Gets or sets the sender's display name.</summary>
    public string? FromName { get; set; }

    /// <summary>Gets or sets the Microsoft Entra tenant.</summary>
    public string? GraphTenantId { get; set; }

    /// <summary>Gets or sets the app registration's client id.</summary>
    public string? GraphClientId { get; set; }

    /// <summary>Gets or sets the app registration's client secret (encrypted at rest).</summary>
    public string? GraphClientSecret { get; set; }

    /// <summary>Gets or sets the SMS provider's endpoint (blank: the server's SMS settings).</summary>
    public string? SmsUrl { get; set; }

    /// <summary>Gets or sets the SMS request template with {to}, {body} and {from}.</summary>
    public string? SmsBodyTemplate { get; set; }

    /// <summary>Gets or sets the SMS request content type.</summary>
    public string? SmsContentType { get; set; }

    /// <summary>Gets or sets the SMS authentication header name.</summary>
    public string? SmsAuthHeader { get; set; }

    /// <summary>Gets or sets the SMS authentication header value (encrypted at rest).</summary>
    public string? SmsAuthValue { get; set; }

    /// <summary>Gets or sets the SMS sender id.</summary>
    public string? SmsFrom { get; set; }
}
