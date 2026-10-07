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
public sealed record BillingRunLineView(long PropertyId, string StandNumber, string InvoiceNumber, decimal Charge, decimal? PreviousCharge, BillingExceptions Exceptions, string? Message, bool Included);

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
}
