using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Communications;
using One4All.Core.Common;
using One4All.Core.Communications;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Communications;

/// <summary>Keeps each company's email and SMS delivery settings.</summary>
public sealed class CompanyMessagingService : ICompanyMessagingService
{
    private readonly One4AllDbContext _db;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    public CompanyMessagingService(One4AllDbContext db) => _db = db;

    /// <inheritdoc />
    public async Task<Result> SaveEmailAsync(long companyId, CompanyEmailSettings settings, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        CompanyMessagingSettings row = await RowAsync(companyId, ct).ConfigureAwait(false);
        string? secret = Clean(settings.GraphClientSecret) ?? row.GraphClientSecret;
        string? password = Clean(settings.SmtpPassword) ?? row.SmtpPassword;
        switch (settings.Provider)
        {
            case EmailProvider.Smtp when Clean(settings.SmtpHost) is null || Clean(settings.FromAddress) is null || settings.SmtpPort is < 1 or > 65535:
                return Result.Failure(Error.Validation("Msg.Smtp", "The company's SMTP server needs a host, a port and a sender address."));
            case EmailProvider.MicrosoftGraph when Clean(settings.GraphTenantId) is null || Clean(settings.GraphClientId) is null || secret is null || Clean(settings.FromAddress) is null:
                return Result.Failure(Error.Validation("Msg.Graph", "Microsoft Graph needs the tenant, the app's client id and secret, and the mailbox to send from."));
        }

        row.EmailProvider = settings.Provider;
        row.FromAddress = Clean(settings.FromAddress);
        row.FromName = Clean(settings.FromName);
        row.SmtpHost = Clean(settings.SmtpHost);
        row.SmtpPort = settings.SmtpPort;
        row.SmtpUseTls = settings.SmtpUseTls;
        row.SmtpUsername = Clean(settings.SmtpUsername);
        row.SmtpPassword = password;
        row.GraphTenantId = Clean(settings.GraphTenantId);
        row.GraphClientId = Clean(settings.GraphClientId);
        row.GraphClientSecret = secret;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result> SaveSmsAsync(long companyId, CompanySmsSettings settings, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (Clean(settings.Url) is string url && !Uri.TryCreate(url, UriKind.Absolute, out _))
        {
            return Result.Failure(Error.Validation("Msg.SmsUrl", $"{url} is not a web address."));
        }

        CompanyMessagingSettings row = await RowAsync(companyId, ct).ConfigureAwait(false);
        row.SmsUrl = Clean(settings.Url);
        row.SmsBodyTemplate = Clean(settings.BodyTemplate);
        row.SmsContentType = Clean(settings.ContentType);
        row.SmsAuthHeader = Clean(settings.AuthHeader);
        row.SmsAuthValue = Clean(settings.AuthValue) ?? row.SmsAuthValue;
        row.SmsFrom = Clean(settings.From);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<string> DescribeAsync(long companyId, CancellationToken ct = default)
    {
        CompanyMessagingSettings? row = await ForGatewayAsync(companyId, ct).ConfigureAwait(false);
        if (row is null)
        {
            return "Email and SMS go through the server's settings.";
        }

        string email = row.EmailProvider switch
        {
            EmailProvider.Smtp => $"Email: SMTP {row.SmtpHost}:{row.SmtpPort}{(row.SmtpUseTls ? " (TLS)" : string.Empty)} as {row.FromAddress}{(row.SmtpPassword is null ? string.Empty : ", password set")}",
            EmailProvider.MicrosoftGraph => $"Email: Microsoft Graph, mailbox {row.FromAddress}, tenant {row.GraphTenantId}, app {row.GraphClientId}{(row.GraphClientSecret is null ? string.Empty : ", secret set")}",
            _ => "Email: the server's settings",
        };
        string sms = row.SmsUrl is null ? "SMS: the server's settings" : $"SMS: {row.SmsUrl} as {row.SmsFrom ?? "(no sender id)"}{(row.SmsAuthValue is null ? string.Empty : ", key set")}";
        return email + Environment.NewLine + sms;
    }

    /// <inheritdoc />
    public Task<CompanyMessagingSettings?> ForGatewayAsync(long companyId, CancellationToken ct = default)
        => _db.Set<CompanyMessagingSettings>().AsNoTracking().FirstOrDefaultAsync(s => s.CompanyId == companyId, ct);

    private async Task<CompanyMessagingSettings> RowAsync(long companyId, CancellationToken ct)
    {
        CompanyMessagingSettings? row = await _db.Set<CompanyMessagingSettings>().FirstOrDefaultAsync(s => s.CompanyId == companyId, ct).ConfigureAwait(false);
        if (row is null)
        {
            row = new CompanyMessagingSettings(companyId);
            _db.Add(row);
        }

        return row;
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
