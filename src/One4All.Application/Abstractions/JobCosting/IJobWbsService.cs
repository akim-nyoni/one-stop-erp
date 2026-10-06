using One4All.Application.JobCosting;
using One4All.Core.Common;

namespace One4All.Application.Abstractions.JobCosting;

/// <summary>A contract's cost-to-complete breakdown.</summary>
/// <param name="ContractNumber">The contract.</param>
/// <param name="ContractBudget">The contract's own budget, to compare with the WBS total.</param>
/// <param name="Lines">The WBS lines in code order, then any unassigned costs.</param>
/// <param name="Total">The totals.</param>
public sealed record WbsBreakdown(string ContractNumber, decimal ContractBudget, IReadOnlyList<WbsLine> Lines, WbsLine Total);

/// <summary>Work breakdown structure, committed cost and cost-to-complete forecasting on contracts (brief §4.12).</summary>
public interface IJobWbsService
{
    /// <summary>
    /// Adds a WBS element. A dotted code's parent must exist and be a phase (no cost head, no budget); an activity names the cost head its
    /// costs are captured under, unique on the contract.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="contractId">The contract.</param>
    /// <param name="code">The dotted code ("1", "1.2").</param>
    /// <param name="name">The name.</param>
    /// <param name="budgetCost">The budget (activities).</param>
    /// <param name="costHead">The cost head (activities; null for a phase).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The element id.</returns>
    Task<Result<long>> AddElementAsync(long companyId, long contractId, string code, string name, decimal budgetCost, string? costHead, CancellationToken ct = default);

    /// <summary>Records the site's estimate to complete an activity (null clears it, falling back to the budget left).</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="contractId">The contract.</param>
    /// <param name="code">The activity code.</param>
    /// <param name="estimateToComplete">The estimate.</param>
    /// <param name="date">When given.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success or why not.</returns>
    Task<Result> SetEstimateAsync(long companyId, long contractId, string code, decimal? estimateToComplete, DateOnly date, CancellationToken ct = default);

    /// <summary>The breakdown: actual cost per activity from captured costs, committed from open subcontract value, and the forecast.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="contractId">The contract.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The breakdown.</returns>
    Task<Result<WbsBreakdown>> BreakdownAsync(long companyId, long contractId, CancellationToken ct = default);
}
