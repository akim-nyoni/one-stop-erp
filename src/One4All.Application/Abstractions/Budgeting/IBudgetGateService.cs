using One4All.Core.Budgeting;
using One4All.Core.Common;

namespace One4All.Application.Abstractions.Budgeting;

/// <summary>Spend to check against the budget.</summary>
/// <param name="AccountCode">The expense account.</param>
/// <param name="Department">The department, if analysed.</param>
/// <param name="Amount">The amount.</param>
public sealed record BudgetDemand(string AccountCode, string? Department, decimal Amount);

/// <summary>One line's check.</summary>
/// <param name="AccountCode">The account.</param>
/// <param name="Department">The department.</param>
/// <param name="Amount">The spend.</param>
/// <param name="Available">What was available (approved budget − actuals − open commitments).</param>
/// <param name="Shortfall">How far the spend overruns (0 when it fits).</param>
/// <param name="Action">The rule's action should it overrun.</param>
public sealed record BudgetGateLine(string AccountCode, string? Department, decimal Amount, decimal Available, decimal Shortfall, BudgetAction Action);

/// <summary>A document's check.</summary>
/// <param name="Outcome">The strictest action among overrunning lines (Allow when all fit).</param>
/// <param name="Lines">Each line.</param>
public sealed record BudgetGateResult(BudgetAction Outcome, IReadOnlyList<BudgetGateLine> Lines)
{
    /// <summary>Gets a warning to show, or null.</summary>
    public string? Warning => Outcome == BudgetAction.Allow ? null
        : $"Over budget: {string.Join("; ", Lines.Where(l => l.Shortfall > 0m).Select(l => $"{l.AccountCode} short by {l.Shortfall:0.00}"))}.";
}

/// <summary>A commitment that does not agree with its purchase order.</summary>
/// <param name="Reference">The order (or commitment reference).</param>
/// <param name="AccountCode">The account.</param>
/// <param name="Committed">The open commitment.</param>
/// <param name="OnOrder">The order's uninvoiced value on the account.</param>
/// <param name="Problem">What is wrong.</param>
public sealed record CommitmentException(string Reference, string AccountCode, decimal Committed, decimal OnOrder, string Problem);

/// <summary>
/// The budget check at every spending gate (brief §4.18.5): available balance per line (full-year approved budget less actuals and open
/// commitments), the enforcement rule for overruns, commitments raised for documents and released when they move on, and reservations
/// (earmarks) for known future spend.
/// </summary>
public interface IBudgetGateService
{
    /// <summary>Adds or replaces the rule for a department / account prefix / value band.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="department">The department, or null for any.</param>
    /// <param name="accountPrefix">The account prefix, or null for any.</param>
    /// <param name="minimumValue">The smallest spend it applies to.</param>
    /// <param name="action">The action.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success or why not.</returns>
    Task<Result> SetRuleAsync(long companyId, string? department, string? accountPrefix, decimal minimumValue, BudgetAction action, CancellationToken ct = default);

    /// <summary>Checks spend dated on a day against the budget of the fiscal year it falls in.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="date">The document date.</param>
    /// <param name="demands">The spend by line.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The check.</returns>
    Task<Result<BudgetGateResult>> CheckAsync(long companyId, DateOnly date, IReadOnlyList<BudgetDemand> demands, CancellationToken ct = default);

    /// <summary>Raises commitments for a document's spend under its reference (lines on the same account and department combine).</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="reference">The document reference.</param>
    /// <param name="date">The document date.</param>
    /// <param name="demands">The spend.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>How many commitments were raised.</returns>
    Task<Result<int>> CommitAsync(long companyId, string reference, DateOnly date, IReadOnlyList<BudgetDemand> demands, CancellationToken ct = default);

    /// <summary>Releases every open commitment under a reference (the document moved on or was cancelled).</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="reference">The reference.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The amount released.</returns>
    Task<decimal> ReleaseAsync(long companyId, string reference, CancellationToken ct = default);

    /// <summary>Reduces a document's open commitment on an account (spend invoiced), releasing it when nothing is left.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="reference">The document reference.</param>
    /// <param name="accountCode">The account.</param>
    /// <param name="amount">The amount now spent.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>How much of the commitment was used up.</returns>
    Task<decimal> ReduceAsync(long companyId, string reference, string accountCode, decimal amount, CancellationToken ct = default);

    /// <summary>
    /// Reconciles the commitment ledger to purchase orders (brief §4.18.7, nightly): open PO commitments whose order is cancelled, closed
    /// or fully invoiced, orders budgeted against an account with no open commitment, and commitments differing from the uninvoiced value.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The exceptions.</returns>
    Task<IReadOnlyList<CommitmentException>> ReconcileAsync(long companyId, CancellationToken ct = default);

    /// <summary>Earmarks budget for known future spend with no order yet; refused when it would overrun a blocking rule.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="reference">The reservation reference (unique while open).</param>
    /// <param name="date">The date.</param>
    /// <param name="demand">The spend reserved.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The check made.</returns>
    Task<Result<BudgetGateResult>> ReserveAsync(long companyId, string reference, DateOnly date, BudgetDemand demand, CancellationToken ct = default);
}
