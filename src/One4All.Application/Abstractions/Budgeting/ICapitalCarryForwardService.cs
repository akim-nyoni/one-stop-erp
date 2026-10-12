using One4All.Core.Common;

namespace One4All.Application.Abstractions.Budgeting;

/// <summary>
/// Year-end carry-forward of unspent approved capital (brief §5): proposed per capital vote — the year's approved capital budget less what
/// was spent and what was already carried — and approved as its own act into the next year's capital budget, leaving an audit document.
/// </summary>
public interface ICapitalCarryForwardService
{
    /// <summary>The unspent capital per vote for a year, as at a date.</summary>
    Task<Result<IReadOnlyList<CapitalUnspent>>> ProposeAsync(long companyId, long fiscalYearId, DateOnly asAt, CancellationToken ct = default);

    /// <summary>
    /// Approves carrying the given amounts into a later year's capital budget (each no more than the vote's carryable balance), adding them to the
    /// budget's first period as carried lines and recording the act.
    /// </summary>
    /// <returns>The total carried.</returns>
    Task<Result<decimal>> ApproveAsync(long companyId, long fiscalYearId, long toBudgetId, IReadOnlyList<CarryForwardAmount> amounts, string reference, long approvedByUserId, DateOnly asAt,
        CancellationToken ct = default);
}

/// <summary>A capital vote's unspent balance.</summary>
/// <param name="AccountCode">The vote.</param>
/// <param name="Budget">The approved capital budget for the year.</param>
/// <param name="Spent">Spent to the date.</param>
/// <param name="AlreadyCarried">Already carried forward.</param>
public sealed record CapitalUnspent(string AccountCode, decimal Budget, decimal Spent, decimal AlreadyCarried)
{
    /// <summary>Gets what may still be carried.</summary>
    public decimal Carryable => Math.Max(0m, Budget - Spent - AlreadyCarried);
}

/// <summary>An amount to carry for a vote.</summary>
/// <param name="AccountCode">The vote.</param>
/// <param name="Amount">The amount.</param>
public sealed record CarryForwardAmount(string AccountCode, decimal Amount);
