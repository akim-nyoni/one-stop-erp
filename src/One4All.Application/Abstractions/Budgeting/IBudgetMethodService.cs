using One4All.Application.Budgeting;
using One4All.Core.Common;

namespace One4All.Application.Abstractions.Budgeting;

/// <summary>Where an incremental budget starts from.</summary>
public enum IncrementalBasis
{
    /// <summary>The prior year's actual movements, month by month.</summary>
    PriorYearActual = 0,

    /// <summary>The prior year's active budget of the same type and department.</summary>
    PriorYearBudget = 1,
}

/// <summary>Builds a draft budget incrementally from a prior year.</summary>
public sealed class IncrementalBudgetRequest
{
    /// <summary>Gets the company.</summary>
    public long CompanyId { get; init; }

    /// <summary>Gets the draft budget filled.</summary>
    public long BudgetId { get; init; }

    /// <summary>Gets what it starts from.</summary>
    public IncrementalBasis Basis { get; init; }

    /// <summary>Gets the prior fiscal year.</summary>
    public long SourceFiscalYearId { get; init; }

    /// <summary>Gets the uplift applied (5 = 5% more; negative cuts).</summary>
    public decimal UpliftPercent { get; init; }

    /// <summary>Gets the accounts covered (null: every income and expense account).</summary>
    public string? AccountPrefix { get; init; }
}

/// <summary>Budget methods beyond line entry and drivers (brief §4.6): incremental budgets from a prior year, the mid-year reforecast and the cash budget.</summary>
public interface IBudgetMethodService
{
    /// <summary>Fills a draft budget's lines from the prior year's actuals or budget with an uplift; accounts the budget already has lines for are left alone.</summary>
    /// <param name="request">The request.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The number of lines added, or why not.</returns>
    Task<Result<int>> BuildIncrementalAsync(IncrementalBudgetRequest request, CancellationToken ct = default);

    /// <summary>
    /// Reforecasts the year from a budget: a new Reforecast budget whose months up to the as-at period are the actuals and whose later months
    /// are the budget's (the year-end outturn).
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="budgetId">The budget reforecast.</param>
    /// <param name="asAtPeriod">The last period with actuals (1–11).</param>
    /// <param name="label">The reforecast's label.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The reforecast budget's id.</returns>
    Task<Result<long>> ReforecastAsync(long companyId, long budgetId, int asAtPeriod, string label, CancellationToken ct = default);

    /// <summary>The cash budget of a budget: its lines turned into cash by their collection and payment timings, from an opening cash balance.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="budgetId">The budget.</param>
    /// <param name="timings">The timings by account prefix.</param>
    /// <param name="openingCash">The cash at the start of the year.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Twelve months and the cash falling after the year end.</returns>
    Task<Result<(IReadOnlyList<CashBudgetRow> Months, decimal CarryOut)>> CashBudgetAsync(long companyId, long budgetId, IReadOnlyList<CashTiming> timings, decimal openingCash, CancellationToken ct = default);
}
