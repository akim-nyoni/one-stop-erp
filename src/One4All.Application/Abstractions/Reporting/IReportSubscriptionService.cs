using One4All.Core.Common;

namespace One4All.Application.Abstractions.Reporting;

/// <summary>A subscription to set up.</summary>
public sealed class SubscriptionRequest
{
    /// <summary>The company.</summary>
    public long CompanyId { get; init; }

    /// <summary>The subscription code.</summary>
    public string Code { get; init; } = string.Empty;

    /// <summary>The report.</summary>
    public string ReportCode { get; init; } = string.Empty;

    /// <summary>The output format.</summary>
    public ReportFormat Format { get; init; } = ReportFormat.Pdf;

    /// <summary>The parameters; values may be relative date tokens such as <c>@PrevMonthEnd</c>.</summary>
    public IReadOnlyDictionary<string, string> Parameters { get; init; } = new Dictionary<string, string>();

    /// <summary>The distribution list (email addresses).</summary>
    public IReadOnlyList<string> Recipients { get; init; } = [];
}

/// <summary>A delivery log line.</summary>
/// <param name="RunAt">When it ran.</param>
/// <param name="Recipient">Who it went to.</param>
/// <param name="FileName">The document.</param>
/// <param name="Succeeded">Whether it was handed over for delivery.</param>
/// <param name="Detail">The message reference, or why it failed.</param>
public sealed record DeliveryLogEntry(DateTimeOffset RunAt, string Recipient, string FileName, bool Succeeded, string Detail);

/// <summary>
/// Scheduled report delivery (brief §13.3 scheduling): a report with stored parameters — relative date tokens resolved at
/// each run — rendered in its format and emailed as an attachment to a distribution list, with a delivery log. The
/// <c>reports.subscriptions</c> scheduled task runs every active subscription of a company.
/// </summary>
public interface IReportSubscriptionService
{
    /// <summary>Sets up a subscription.</summary>
    /// <param name="request">The subscription.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or why it was refused.</returns>
    Task<Result> SubscribeAsync(SubscriptionRequest request, CancellationToken ct = default);

    /// <summary>Starts or stops a subscription.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="code">The subscription.</param>
    /// <param name="active">Whether it runs.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or why it was refused.</returns>
    Task<Result> SetActiveAsync(long companyId, string code, bool active, CancellationToken ct = default);

    /// <summary>Runs one subscription: renders the report and queues it to every recipient.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="code">The subscription.</param>
    /// <param name="runAt">The moment it runs for (dates resolve from it).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The recipients it was queued to, or why it failed (also logged).</returns>
    Task<Result<int>> RunAsync(long companyId, string code, DateTimeOffset runAt, CancellationToken ct = default);

    /// <summary>Runs every active subscription of a company.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="runAt">The moment.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>How many ran and how many failed.</returns>
    Task<(int Ran, int Failed)> RunAllAsync(long companyId, DateTimeOffset runAt, CancellationToken ct = default);

    /// <summary>A subscription's delivery log, newest first.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="code">The subscription.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The log, or why there is none.</returns>
    Task<Result<IReadOnlyList<DeliveryLogEntry>>> DeliveryLogAsync(long companyId, string code, CancellationToken ct = default);
}
