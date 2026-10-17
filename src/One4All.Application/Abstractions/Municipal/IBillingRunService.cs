using One4All.Core.Common;
using One4All.Core.Municipal;

namespace One4All.Application.Abstractions.Municipal;

/// <summary>A request to propose a billing run.</summary>
public sealed class BillingRunProposal
{
    /// <summary>The council.</summary>
    public long CompanyId { get; init; }

    /// <summary>The service billed (fixed-charge services: rates, refuse, sewer and so on).</summary>
    public ServiceType ServiceType { get; init; }

    /// <summary>The bill date; the run bills its month.</summary>
    public DateOnly BillDate { get; init; }

    /// <summary>Only this ward, or null for all.</summary>
    public string? Ward { get; init; }

    /// <summary>The council service to bill (null: the category's connections without one).</summary>
    public string? ServiceCode { get; init; }

    /// <summary>Gets the meter-reading route to bill (null: every route): stands with a meter on the route.</summary>
    public string? RouteCode { get; init; }

    /// <summary>Gets the billing cycle to bill (null: every cycle): stands whose tariff for the service is on that cycle.</summary>
    public BillingCycle? Cycle { get; init; }

    /// <summary>The bill-number prefix (bills are numbered prefix-property-yyyyMM).</summary>
    public string InvoicePrefix { get; init; } = string.Empty;

    /// <summary>Flag bills that differ from the last bill for the service by more than this percentage.</summary>
    public decimal VariancePercent { get; init; } = 50m;

    /// <summary>Who proposes it.</summary>
    public long UserId { get; init; }
}

/// <summary>A billing run's totals.</summary>
/// <param name="RunNumber">The run number.</param>
/// <param name="Status">Where it stands.</param>
/// <param name="Lines">The properties considered.</param>
/// <param name="Included">The bills to post.</param>
/// <param name="Total">Their total.</param>
/// <param name="Exceptions">The lines with exceptions.</param>
public sealed record BillingRunSummary(string RunNumber, BillingRunStatus Status, int Lines, int Included, decimal Total, int Exceptions, int Posted = 0, int Failed = 0)
{
    /// <summary>Included bills still to post (the run resumes with them).</summary>
    public int Remaining => Status == BillingRunStatus.Posting ? Included - Posted - Failed : 0;
}

/// <summary>How far posting a billing run has got.</summary>
/// <param name="Posted">Bills posted so far.</param>
/// <param name="Failed">Bills that failed.</param>
/// <param name="Total">Bills to post.</param>
public sealed record BillingRunProgress(int Posted, int Failed, int Total);

/// <summary>How a run is posted.</summary>
public sealed record BillingPostOptions
{
    /// <summary>Bills per chunk (each chunk saved before the next).</summary>
    public int ChunkSize { get; init; } = 500;

    /// <summary>Stop after this many bills (the run stays Posting and resumes later), e.g. to fit a batch window.</summary>
    public int? MaxBills { get; init; }

    /// <summary>Told after every chunk.</summary>
    public IProgress<BillingRunProgress>? Progress { get; init; }

    /// <summary>Bills posted at once, each in its own unit of work (1: one after another); a large run posts several at a time to finish in its window.</summary>
    public int Parallelism { get; init; } = 1;
}

/// <summary>A line of a billing run.</summary>
/// <param name="PropertyId">The property.</param>
/// <param name="StandNumber">The stand.</param>
/// <param name="InvoiceNumber">The bill number.</param>
/// <param name="Charge">The charge.</param>
/// <param name="PreviousCharge">The last bill for the service, if any.</param>
/// <param name="Exceptions">What the review found.</param>
/// <param name="Message">Why it was left out, when it was.</param>
/// <param name="Included">Whether it will be posted on approval.</param>
public sealed record BillingRunLineView(long PropertyId, string StandNumber, string InvoiceNumber, decimal Charge, decimal? PreviousCharge, BillingExceptions Exceptions, string? Message, bool Included,
    decimal Consumption = 0m);

/// <summary>
/// The reviewed billing run (brief §4.9): propose bills for a service, month and ward without posting, with an exception
/// report (already billed, nothing to bill, failed, large change against the last bill); leave lines out; approve — by
/// someone other than the proposer — to post every included bill in one unit; or cancel before posting and run again.
/// </summary>
public interface IBillingRunService
{
    /// <summary>Proposes a run; only one proposed run per service and month at a time.</summary>
    /// <param name="proposal">The run.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The run's totals, or why it was refused.</returns>
    Task<Result<BillingRunSummary>> ProposeAsync(BillingRunProposal proposal, CancellationToken ct = default);

    /// <summary>The run's lines, exceptions first if asked.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="runNumber">The run.</param>
    /// <param name="exceptionsOnly">Only lines with exceptions (the exception report).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The lines, in property order.</returns>
    Task<Result<IReadOnlyList<BillingRunLineView>>> LinesAsync(long companyId, string runNumber, bool exceptionsOnly, CancellationToken ct = default);

    /// <summary>Puts a proposed bill in or leaves it out; bills blocked by an exception cannot be put in.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="runNumber">The run.</param>
    /// <param name="propertyId">The property.</param>
    /// <param name="included">Whether to post it.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The run's totals afterwards, or why it was refused.</returns>
    Task<Result<BillingRunSummary>> SetIncludedAsync(long companyId, string runNumber, long propertyId, bool included, CancellationToken ct = default);

    /// <summary>Approves a proposed run and posts its included bills (in chunks, see the overload with options).</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="runNumber">The run.</param>
    /// <param name="userId">Who approves it (not the proposer).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The run's totals, or why it was refused.</returns>
    Task<Result<BillingRunSummary>> ApproveAsync(long companyId, string runNumber, long userId, CancellationToken ct = default);

    /// <summary>
    /// Approves a proposed run and posts its bills in chunks (brief §15: tens of thousands of accounts): each bill on its own, a failed
    /// bill set aside with its reason while the rest carry on, progress after each chunk; the run stays Posting until every bill is
    /// posted or failed, and <see cref="ResumeAsync"/> carries on after a stop.
    /// </summary>
    /// <param name="companyId">The council.</param>
    /// <param name="runNumber">The run.</param>
    /// <param name="userId">Who approves it (not the proposer).</param>
    /// <param name="options">Chunk size, a cap and progress.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The run's totals.</returns>
    Task<Result<BillingRunSummary>> ApproveAsync(long companyId, string runNumber, long userId, BillingPostOptions options, CancellationToken ct = default);

    /// <summary>Carries on posting a run left Posting (stopped, capped or interrupted); a bill already on the account is not posted twice.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="runNumber">The run.</param>
    /// <param name="options">Chunk size, a cap and progress.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The run's totals.</returns>
    Task<Result<BillingRunSummary>> ResumeAsync(long companyId, string runNumber, BillingPostOptions options, CancellationToken ct = default);

    /// <summary>Cancels a proposed run; nothing was posted, so the month can be run again.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="runNumber">The run.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or why it was refused.</returns>
    Task<Result> CancelAsync(long companyId, string runNumber, CancellationToken ct = default);

    /// <summary>Emails a posted run's bills: each ratepayer billed in the run with an email address is sent their statement (brief §4.9 bill delivery).</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="runNumber">The run.</param>
    /// <param name="templateCode">The email template.</param>
    /// <param name="language">The language.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>What was queued (ratepayers without email are counted for printing), or the run is not posted.</returns>
    Task<Result<Communications.StatementDespatch>> DespatchBillsAsync(long companyId, string runNumber, string templateCode, string language, CancellationToken ct = default);

    /// <summary>
    /// Texts a posted run's bills: each ratepayer billed in the run with a mobile number gets one SMS from the template, with the fields Name, Account, Stands,
    /// Bills, Amount (billed in this run), Balance (owed now), Currency and BillDate (brief §4.9 bulk SMS).
    /// </summary>
    /// <param name="companyId">The council.</param>
    /// <param name="runNumber">The run.</param>
    /// <param name="templateCode">The SMS template.</param>
    /// <param name="language">The language.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>How many were queued and how many ratepayers have no mobile number.</returns>
    Task<Result<BillSmsDespatch>> SmsBillsAsync(long companyId, string runNumber, string templateCode, string language, CancellationToken ct = default);

    /// <summary>
    /// Prints a posted run's bills in one document, one ratepayer statement per page in stand order (brief §4.9 bulk bill print) — all of them, or only the
    /// ratepayers without an email address (the ones emailing did not reach).
    /// </summary>
    /// <param name="companyId">The council.</param>
    /// <param name="runNumber">The run.</param>
    /// <param name="withoutEmailOnly">Only ratepayers without an email address.</param>
    /// <param name="width">The page width in characters.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The bills as text with a form feed between pages, and how many.</returns>
    Task<Result<PrintedBills>> PrintBillsAsync(long companyId, string runNumber, bool withoutEmailOnly, int width = 72, CancellationToken ct = default);
}

/// <summary>A bulk SMS of a run's bills.</summary>
/// <param name="Queued">Ratepayers texted.</param>
/// <param name="NoMobile">Ratepayers without a mobile number.</param>
/// <param name="BatchId">The despatch batch, if any was queued.</param>
public sealed record BillSmsDespatch(int Queued, int NoMobile, long? BatchId);

/// <summary>A run's bills printed in one document.</summary>
/// <param name="Bills">How many bills.</param>
/// <param name="Text">The bills, a form feed between them.</param>
public sealed record PrintedBills(int Bills, string Text);
