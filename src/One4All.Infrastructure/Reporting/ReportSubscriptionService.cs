using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Communications;
using One4All.Application.Abstractions.Reporting;
using One4All.Application.Abstractions.Scheduling;
using One4All.Application.Reporting;
using One4All.Core.Common;
using One4All.Core.Communications;
using One4All.Core.Reporting;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Reporting;

/// <summary>Report subscriptions: render on a run and email to the distribution list, logging each delivery.</summary>
public sealed class ReportSubscriptionService : IReportSubscriptionService
{
    private readonly One4AllDbContext _db;
    private readonly IReportRunner _runner;
    private readonly INotificationService _messages;
    private readonly IReportBurstService? _burst;
    private readonly ReportDeliveryOptions _delivery;
    private readonly IReportPrinter? _printer;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="runner">The report runner.</param>
    /// <param name="messages">The notification service.</param>
    /// <param name="delivery">Where file deliveries may go.</param>
    /// <param name="printer">The printer subscriptions with a printer print on.</param>
    public ReportSubscriptionService(One4AllDbContext db, IReportRunner runner, INotificationService messages, IReportBurstService? burst = null, ReportDeliveryOptions? delivery = null,
        IReportPrinter? printer = null)
    {
        _printer = printer;
        _delivery = delivery ?? new ReportDeliveryOptions();
        _burst = burst;
        _db = db;
        _runner = runner;
        _messages = messages;
    }

    /// <inheritdoc />
    public async Task<Result> SubscribeAsync(SubscriptionRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        List<string> recipients = [.. request.Recipients.Where(r => !string.IsNullOrWhiteSpace(r)).Select(r => r.Trim()).Distinct(StringComparer.OrdinalIgnoreCase)];
        bool bursting = !string.IsNullOrWhiteSpace(request.BurstKeyField);
        string? folder = string.IsNullOrWhiteSpace(request.DeliveryFolder) ? null : request.DeliveryFolder.Trim();
        if (folder is not null && (bursting || _delivery.Resolve(folder) is null))
        {
            return Result.Failure(Error.Validation("Rpt.DeliveryFolder", bursting ? "A burst subscription is delivered by email, not to a folder."
                : $"Folder '{folder}' must be a relative folder inside the delivery root."));
        }

        string? printer = string.IsNullOrWhiteSpace(request.DeliveryPrinter) ? null : request.DeliveryPrinter.Trim();
        if (printer is not null && bursting)
        {
            return Result.Failure(Error.Validation("Rpt.DeliveryPrinter", "A burst subscription is delivered by email, not to a printer."));
        }

        if (string.IsNullOrWhiteSpace(request.Code) || string.IsNullOrWhiteSpace(request.ReportCode) || (!bursting && recipients.Count == 0 && folder is null && printer is null) || !Enum.IsDefined(request.Format)
            || recipients.Any(r => !r.Contains('@', StringComparison.Ordinal)))
        {
            return Result.Failure(Error.Validation("Rpt.Subscription", "A subscription needs a code, a report, a format and at least one email address (or a burst key, a folder or a printer)."));
        }

        if (bursting && string.IsNullOrWhiteSpace(request.BurstRecipientField) && request.BurstLedger is null)
        {
            return Result.Failure(Error.Validation("Rpt.BurstRecipients", "A burst subscription needs the field holding each part's email, or the ledger whose contacts receive it."));
        }

        string? badToken = request.Parameters.Values.FirstOrDefault(v => !ReportParameterTokens.IsValid(v));
        if (badToken is not null)
        {
            return Result.Failure(Error.Validation("Rpt.Token", $"Unknown date token {badToken}."));
        }

        string reportCode = request.ReportCode.Trim();
        if (!await _db.ReportDefinitions.AnyAsync(r => r.Code == reportCode && (r.CompanyId == null || r.CompanyId == request.CompanyId), ct).ConfigureAwait(false))
        {
            return Result.Failure(Error.NotFound("Rpt.ReportNotFound", $"No report {reportCode}."));
        }

        string code = request.Code.Trim().ToUpperInvariant();
        if (await _db.Set<ReportSubscription>().AnyAsync(s => s.CompanyId == request.CompanyId && s.Code == code, ct).ConfigureAwait(false))
        {
            return Result.Failure(Error.Conflict("Rpt.SubscriptionExists", $"Subscription {code} already exists."));
        }

        _db.Set<ReportSubscription>().Add(new ReportSubscription(request.CompanyId, code, reportCode, (int)request.Format, JsonSerializer.Serialize(request.Parameters), recipients)
        {
            BurstKeyField = bursting ? request.BurstKeyField!.Trim() : null, BurstRecipientField = bursting ? request.BurstRecipientField?.Trim() : null,
            BurstLedger = bursting ? (int?)request.BurstLedger : null, DeliveryFolder = folder, DeliveryPrinter = printer,
        });
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result> SetActiveAsync(long companyId, string code, bool active, CancellationToken ct = default)
    {
        Result<ReportSubscription> found = await FindAsync(companyId, code, ct).ConfigureAwait(false);
        if (found.IsFailure)
        {
            return Result.Failure(found.Error);
        }

        found.Value.SetActive(active);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result<int>> RunAsync(long companyId, string code, DateTimeOffset runAt, CancellationToken ct = default)
    {
        Result<ReportSubscription> found = await FindAsync(companyId, code, ct).ConfigureAwait(false);
        return found.IsFailure ? found.Error : await RunOneAsync(found.Value, runAt, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<(int Ran, int Failed)> RunAllAsync(long companyId, DateTimeOffset runAt, CancellationToken ct = default)
    {
        List<ReportSubscription> active = await _db.Set<ReportSubscription>().Where(s => s.CompanyId == companyId && s.IsActive).OrderBy(s => s.Code).ToListAsync(ct).ConfigureAwait(false);
        int failed = 0;
        foreach (ReportSubscription subscription in active)
        {
            failed += (await RunOneAsync(subscription, runAt, ct).ConfigureAwait(false)).IsFailure ? 1 : 0;
        }

        return (active.Count, failed);
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<DeliveryLogEntry>>> DeliveryLogAsync(long companyId, string code, CancellationToken ct = default)
    {
        Result<ReportSubscription> found = await FindAsync(companyId, code, ct).ConfigureAwait(false);
        if (found.IsFailure)
        {
            return found.Error;
        }

        long id = found.Value.Id;
        return Result<IReadOnlyList<DeliveryLogEntry>>.Success(await _db.Set<ReportDelivery>().AsNoTracking().Where(d => d.SubscriptionId == id)
            .OrderByDescending(d => d.RunAt).ThenBy(d => d.Id).Select(d => new DeliveryLogEntry(d.RunAt, d.Recipient, d.FileName, d.Succeeded, d.Detail)).ToListAsync(ct).ConfigureAwait(false));
    }

    // Renders the report and queues it to each recipient; either way the outcome goes in the delivery log.
    private async Task<Result<int>> RunOneAsync(ReportSubscription subscription, DateTimeOffset runAt, CancellationToken ct)
    {
        Dictionary<string, string> stored = JsonSerializer.Deserialize<Dictionary<string, string>>(subscription.ParametersJson) ?? [];
        IReadOnlyDictionary<string, object?> parameters = ReportParameterTokens.Resolve(stored, DateOnly.FromDateTime(runAt.UtcDateTime));
        if (subscription.BurstKeyField is string keyField && _burst is not null)
        {
            Result<IReadOnlyList<BurstDelivery>> burst = await _burst.BurstAsync(new BurstRequest(subscription.CompanyId, subscription.ReportCode, keyField)
            {
                Parameters = parameters, RecipientField = subscription.BurstRecipientField, Ledger = (One4All.Core.Trading.TradingLedger?)subscription.BurstLedger, Format = (ReportFormat)subscription.Format,
            }, ct).ConfigureAwait(false);
            if (burst.IsFailure)
            {
                _db.Set<ReportDelivery>().Add(new ReportDelivery(subscription.CompanyId, subscription.Id, runAt, "(burst)", string.Empty, false, burst.Error.Message));
                await _db.SaveChangesAsync(ct).ConfigureAwait(false);
                return burst.Error;
            }

            foreach (BurstDelivery delivery in burst.Value)
            {
                if (delivery.Recipients.Count == 0)
                {
                    _db.Set<ReportDelivery>().Add(new ReportDelivery(subscription.CompanyId, subscription.Id, runAt, $"({delivery.Key})", delivery.FileName, false, $"No email address for {delivery.Key}."));
                }

                foreach (string recipient in delivery.Recipients)
                {
                    _db.Set<ReportDelivery>().Add(new ReportDelivery(subscription.CompanyId, subscription.Id, runAt, recipient, delivery.FileName, true, $"{delivery.Key}: queued in despatch batch {delivery.BatchId}"));
                }
            }

            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            return burst.Value.Sum(d => d.Recipients.Count);
        }

        Result<RenderedDocument> rendered = await _runner.RenderAsync(subscription.CompanyId, subscription.ReportCode, parameters, (ReportFormat)subscription.Format, ct).ConfigureAwait(false);
        if (rendered.IsFailure)
        {
            foreach (string recipient in subscription.Recipients)
            {
                _db.Set<ReportDelivery>().Add(new ReportDelivery(subscription.CompanyId, subscription.Id, runAt, recipient, string.Empty, false, rendered.Error.Message));
            }

            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            return rendered.Error;
        }

        RenderedDocument document = rendered.Value;
        int delivered = 0;
        bool folderFailed = false;
        if (subscription.DeliveryFolder is string folder)
        {
            // Saved under the delivery root, stamped with the run so earlier runs are kept.
            string? path = _delivery.Resolve(folder);
            string fileName = $"{runAt:yyyyMMdd-HHmm}-{document.FileName}";
            try
            {
                if (path is null)
                {
                    throw new IOException($"Folder '{folder}' is outside the delivery root.");
                }

                Directory.CreateDirectory(path);
                await File.WriteAllBytesAsync(Path.Combine(path, fileName), document.Content, ct).ConfigureAwait(false);
                _db.Set<ReportDelivery>().Add(new ReportDelivery(subscription.CompanyId, subscription.Id, runAt, $"folder:{folder}", fileName, true, $"Saved to {Path.Combine(path, fileName)}"));
                delivered++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _db.Set<ReportDelivery>().Add(new ReportDelivery(subscription.CompanyId, subscription.Id, runAt, $"folder:{folder}", fileName, false, ex.Message));
                folderFailed = true;
            }
        }

        if (subscription.DeliveryPrinter is string printerName)
        {
            Result printed = _printer is null
                ? Result.Failure(Error.Rule("Rpt.PrinterUnavailable", "No printer service is registered on this host."))
                : await _printer.PrintAsync(printerName, document, ct).ConfigureAwait(false);
            _db.Set<ReportDelivery>().Add(new ReportDelivery(subscription.CompanyId, subscription.Id, runAt, $"printer:{printerName}", document.FileName, printed.IsSuccess,
                printed.IsSuccess ? $"Sent to printer {printerName}" : printed.Error.Message));
            if (printed.IsSuccess)
            {
                delivered++;
            }
            else
            {
                folderFailed = true;
            }
        }

        if (subscription.Recipients.Count == 0)
        {
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            return folderFailed ? Error.Failure("Rpt.FolderDelivery", $"Subscription {subscription.Code} could not be saved to its folder or printed.") : delivered;
        }

        string summary = string.Join(", ", parameters.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => $"{p.Key} = {Format(p.Value)}"));
        Result<long> batch = await _messages.QueueDirectAsync(subscription.CompanyId, $"Report {subscription.Code} {runAt:yyyy-MM-dd}", MessageChannel.Email, subscription.Recipients,
            $"{subscription.ReportCode} - {runAt:d MMMM yyyy}", $"Please find the {subscription.ReportCode} report attached ({summary}).",
            new MessageAttachment(document.FileName, document.ContentType, document.Content), ct).ConfigureAwait(false);
        foreach (string recipient in subscription.Recipients)
        {
            _db.Set<ReportDelivery>().Add(new ReportDelivery(subscription.CompanyId, subscription.Id, runAt, recipient, document.FileName, batch.IsSuccess,
                batch.IsSuccess ? $"Queued in despatch batch {batch.Value}" : batch.Error.Message));
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return batch.IsFailure ? batch.Error : subscription.Recipients.Count + delivered;
    }

    private static string Format(object? value) => value switch
    {
        DateOnly date => date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
        null => "(none)",
        _ => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
    };

    private async Task<Result<ReportSubscription>> FindAsync(long companyId, string code, CancellationToken ct)
    {
        string key = (code ?? string.Empty).Trim().ToUpperInvariant();
        ReportSubscription? subscription = await _db.Set<ReportSubscription>().FirstOrDefaultAsync(s => s.CompanyId == companyId && s.Code == key, ct).ConfigureAwait(false);
        if (subscription is null)
        {
            return Error.NotFound("Rpt.SubscriptionNotFound", $"Subscription {key} not found.");
        }

        return subscription;
    }
}

/// <summary>Built-in task: run every active report subscription of the company.</summary>
public sealed class ReportSubscriptionsTask : IScheduledTask
{
    private readonly IReportSubscriptionService _subscriptions;

    /// <summary>Creates the task.</summary>
    /// <param name="subscriptions">The subscriptions.</param>
    public ReportSubscriptionsTask(IReportSubscriptionService subscriptions) => _subscriptions = subscriptions;

    /// <inheritdoc />
    public string Key => "reports.subscriptions";

    /// <inheritdoc />
    public async Task<Result<string>> RunAsync(long companyId, DateTimeOffset runAt, CancellationToken ct = default)
    {
        (int ran, int failed) = await _subscriptions.RunAllAsync(companyId, runAt, ct).ConfigureAwait(false);
        string message = $"{ran} report subscriptions run, {failed} failed.";
        return failed == 0 ? message : Error.Rule("Rpt.SubscriptionsFailed", message);
    }
}
