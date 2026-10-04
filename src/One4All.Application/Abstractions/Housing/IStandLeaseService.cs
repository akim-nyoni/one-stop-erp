using One4All.Application.Housing;
using One4All.Core.Common;
using One4All.Core.Housing;

namespace One4All.Application.Abstractions.Housing;

/// <summary>A request to lease a stand.</summary>
public sealed class StandLeaseRequest
{
    /// <summary>The council.</summary>
    public long CompanyId { get; init; }

    /// <summary>The stand.</summary>
    public long StandId { get; init; }

    /// <summary>The lessee's customer account.</summary>
    public long LesseeCustomerId { get; init; }

    /// <summary>What the stand is used for.</summary>
    public LeaseUse Use { get; init; }

    /// <summary>The first day (the first of a month).</summary>
    public DateOnly StartDate { get; init; }

    /// <summary>The term in years (up to 99).</summary>
    public int TermYears { get; init; }

    /// <summary>The first year's rent.</summary>
    public decimal AnnualRent { get; init; }

    /// <summary>Years between rent reviews.</summary>
    public int ReviewEveryYears { get; init; } = 1;

    /// <summary>The fixed escalation at each review.</summary>
    public decimal EscalationPercent { get; init; }

    /// <summary>The lease income account.</summary>
    public string IncomeAccountCode { get; init; } = string.Empty;

    /// <summary>The accrued (straight-lining) lease income account.</summary>
    public string AccruedIncomeAccountCode { get; init; } = string.Empty;
}

/// <summary>A lease billing run's result.</summary>
/// <param name="LeasesBilled">Leases billed for the month.</param>
/// <param name="RentBilled">The contractual rent invoiced.</param>
/// <param name="IncomeRecognised">The straight-line income recognised.</param>
public sealed record LeaseRunResult(int LeasesBilled, decimal RentBilled, decimal IncomeRecognised);

/// <summary>
/// Leases of stands (brief §4.10.5, accounted per §4.10.9 / IFRS 16 lessor): long leases with fixed escalation at each
/// review; each month the contractual rent is invoiced and the income straight-lined, the difference carried on an
/// accrued lease income account that unwinds to nothing over the term.
/// </summary>
public interface IStandLeaseService
{
    /// <summary>Records a lease of a stand; a stand has one lease at a time.</summary>
    /// <param name="request">The lease.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The lease number, or why it was refused.</returns>
    Task<Result<string>> CreateAsync(StandLeaseRequest request, CancellationToken ct = default);

    /// <summary>The lease's contractual rent by year.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="leaseNumber">The lease.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The years, or why there are none.</returns>
    Task<Result<IReadOnlyList<LeaseYear>>> ScheduleAsync(long companyId, string leaseNumber, CancellationToken ct = default);

    /// <summary>Bills a month on every lease covering it: rent invoiced, income straight-lined; never twice. One unit.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="month">Any day in the month.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>What was billed and recognised.</returns>
    Task<Result<LeaseRunResult>> RunMonthAsync(long companyId, DateOnly month, CancellationToken ct = default);
}
