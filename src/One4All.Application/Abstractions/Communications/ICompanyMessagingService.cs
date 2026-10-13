using One4All.Core.Common;
using One4All.Core.Communications;

namespace One4All.Application.Abstractions.Communications;

/// <summary>A company's email delivery.</summary>
/// <param name="Provider">Server default, the company's SMTP server, or Microsoft Graph.</param>
/// <param name="FromAddress">The sender address (SMTP) or mailbox (Graph).</param>
/// <param name="FromName">The sender's display name.</param>
/// <param name="SmtpHost">The SMTP host.</param>
/// <param name="SmtpPort">The SMTP port.</param>
/// <param name="SmtpUseTls">Whether SMTP uses TLS.</param>
/// <param name="SmtpUsername">The SMTP login.</param>
/// <param name="SmtpPassword">The SMTP password (null: keep the one stored).</param>
/// <param name="GraphTenantId">The Entra tenant.</param>
/// <param name="GraphClientId">The app registration.</param>
/// <param name="GraphClientSecret">Its client secret (null: keep the one stored).</param>
public sealed record CompanyEmailSettings(EmailProvider Provider, string? FromAddress = null, string? FromName = null, string? SmtpHost = null, int SmtpPort = 587, bool SmtpUseTls = true,
    string? SmtpUsername = null, string? SmtpPassword = null, string? GraphTenantId = null, string? GraphClientId = null, string? GraphClientSecret = null);

/// <summary>A company's SMS provider.</summary>
/// <param name="Url">The endpoint (blank: the server's SMS settings).</param>
/// <param name="BodyTemplate">The request template with {to}, {body} and {from} (blank: the server's).</param>
/// <param name="ContentType">The request content type (blank: application/json).</param>
/// <param name="AuthHeader">The authentication header name.</param>
/// <param name="AuthValue">Its value (null: keep the one stored).</param>
/// <param name="From">The sender id.</param>
public sealed record CompanySmsSettings(string? Url, string? BodyTemplate = null, string? ContentType = null, string? AuthHeader = null, string? AuthValue = null, string? From = null);

/// <summary>
/// A company's own email and SMS delivery (brief §8): set here, used by the email and SMS gateways in place of the server-wide settings, secrets
/// encrypted at rest and never shown back.
/// </summary>
public interface ICompanyMessagingService
{
    /// <summary>Sets how the company sends email.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="settings">The settings.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or what is missing.</returns>
    Task<Result> SaveEmailAsync(long companyId, CompanyEmailSettings settings, CancellationToken ct = default);

    /// <summary>Sets the company's SMS provider.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="settings">The settings.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or what is wrong.</returns>
    Task<Result> SaveSmsAsync(long companyId, CompanySmsSettings settings, CancellationToken ct = default);

    /// <summary>The company's settings with secrets masked.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A description.</returns>
    Task<string> DescribeAsync(long companyId, CancellationToken ct = default);

    /// <summary>The company's settings for the gateways (secrets in clear), or null when it uses the server's.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The settings, or null.</returns>
    Task<CompanyMessagingSettings?> ForGatewayAsync(long companyId, CancellationToken ct = default);
}
