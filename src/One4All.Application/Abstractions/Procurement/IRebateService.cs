using One4All.Application.Procurement;
using One4All.Core.Common;

namespace One4All.Application.Abstractions.Procurement;

/// <summary>A new rebate agreement.</summary>
/// <param name="SupplierId">The supplier.</param>
/// <param name="Code">The agreement code.</param>
/// <param name="From">The first day of the period.</param>
/// <param name="To">The last day of the period.</param>
/// <param name="Tiers">The tiers.</param>
public sealed record RebateAgreementRequest(long SupplierId, string Code, DateOnly From, DateOnly To, IReadOnlyList<RebateTier> Tiers);

/// <summary>Where an agreement stands after an accrual run.</summary>
/// <param name="Code">The agreement.</param>
/// <param name="Spend">Purchases from the supplier in the period so far.</param>
/// <param name="RatePercent">The rate reached.</param>
/// <param name="Earned">The rebate earned so far.</param>
/// <param name="Booked">What this run booked (the change since the last run).</param>
public sealed record RebatePosition(string Code, decimal Spend, decimal RatePercent, decimal Earned, decimal Booked);

/// <summary>
/// Supplier volume rebates (brief §4.3): agreements with tiered, retrospective rates; an accrual run books each
/// agreement's rebate earned to date on purchases (Dr rebate receivable, Cr purchase rebates), only the change since the
/// last run.
/// </summary>
public interface IRebateService
{
    /// <summary>Creates an agreement.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="request">The agreement.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The agreement id, or why it was refused.</returns>
    Task<Result<long>> CreateAsync(long companyId, RebateAgreementRequest request, CancellationToken ct = default);

    /// <summary>Accrues every agreement in force at a date, in one journal and one unit.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="asAtDate">The date.</param>
    /// <param name="receivableAccountCode">The rebate receivable.</param>
    /// <param name="incomeAccountCode">Where rebates are credited (purchase rebates, or cost of sales).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Each agreement's position, or why the run failed.</returns>
    Task<Result<IReadOnlyList<RebatePosition>>> AccrueAsync(long companyId, DateOnly asAtDate, string receivableAccountCode, string incomeAccountCode, CancellationToken ct = default);
}
