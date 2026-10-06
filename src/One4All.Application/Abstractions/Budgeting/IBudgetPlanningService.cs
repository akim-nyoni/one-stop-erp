using One4All.Application.Budgeting;
using One4All.Core.Common;

namespace One4All.Application.Abstractions.Budgeting;

/// <summary>How a changed assumption moves one budget line.</summary>
/// <param name="BudgetId">The budget.</param>
/// <param name="AccountCode">The account.</param>
/// <param name="Period">The period.</param>
/// <param name="Before">The amount before.</param>
/// <param name="After">The amount after.</param>
public sealed record DriverImpact(long BudgetId, string AccountCode, int Period, decimal Before, decimal After);

/// <summary>The staff budget built from the establishment.</summary>
/// <param name="Posts">Approved posts counted.</param>
/// <param name="Salaries">The annual salary budget (after increment and vacancy factor).</param>
/// <param name="Contributions">The annual employer contributions.</param>
public sealed record StaffBudget(int Posts, decimal Salaries, decimal Contributions);

/// <summary>
/// Budget planning tools (brief §4.18.2, §4.18.6): the driver/assumption register that re-computes dependent lines, spreading profiles,
/// the establishment-driven staff budget, and re-phasing of an approved budget between periods (approved, never by the requester).
/// </summary>
public interface IBudgetPlanningService
{
    /// <summary>
    /// Sets an assumption for a year (creating it when new). Every driver-based line on a budget still in preparation (not sealed) is
    /// re-computed as quantity × value; <paramref name="commit"/> false only previews the impact.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="fiscalYearId">The year.</param>
    /// <param name="code">The assumption code.</param>
    /// <param name="value">Its value.</param>
    /// <param name="commit">Apply, or only preview.</param>
    /// <param name="name">Its name (when new).</param>
    /// <param name="unit">Its unit (when new).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The lines that change.</returns>
    Task<Result<IReadOnlyList<DriverImpact>>> SetDriverAsync(long companyId, long fiscalYearId, string code, decimal value, bool commit, string? name = null, string? unit = null, CancellationToken ct = default);

    /// <summary>Adds or replaces a driver-based line: quantity × the assumption's value (e.g. 40 learners × FEE).</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="budgetId">The budget.</param>
    /// <param name="accountCode">The account.</param>
    /// <param name="period">The period.</param>
    /// <param name="driverCode">The assumption.</param>
    /// <param name="quantity">The volume.</param>
    /// <param name="costCentre">The cost centre.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The line's amount.</returns>
    Task<Result<decimal>> SetDriverLineAsync(long companyId, long budgetId, string accountCode, int period, string driverCode, decimal quantity, string? costCentre = null, CancellationToken ct = default);

    /// <summary>
    /// Spreads an annual amount on an account over the year's periods by a profile, replacing the account's preparation lines. Seasonal
    /// uses the prior year's actuals on the account; custom takes one weight per period.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="budgetId">The budget (not sealed).</param>
    /// <param name="accountCode">The account.</param>
    /// <param name="annual">The annual amount.</param>
    /// <param name="profile">The profile.</param>
    /// <param name="weights">Per-period weights for the custom profile.</param>
    /// <param name="costCentre">The cost centre.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The amount per period.</returns>
    Task<Result<IReadOnlyList<decimal>>> SpreadAsync(long companyId, long budgetId, string accountCode, decimal annual, SpreadProfile profile, IReadOnlyList<decimal>? weights = null,
        string? costCentre = null, CancellationToken ct = default);

    /// <summary>
    /// Builds the staff budget from the approved establishment of the budget's department (all departments when it has none): posts ×
    /// annual cost per post, plus the increment, less the vacancy factor, spread evenly; employer contributions on top to their own account.
    /// Replaces those accounts' preparation lines, so re-running after a salary change re-computes it.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="budgetId">The budget (not sealed).</param>
    /// <param name="salaryAccountCode">The salaries account.</param>
    /// <param name="incrementPercent">The salary increment.</param>
    /// <param name="vacancyPercent">The vacancy factor.</param>
    /// <param name="contributionPercent">Employer contributions as a share of salaries.</param>
    /// <param name="contributionAccountCode">The contributions account.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The staff budget.</returns>
    Task<Result<StaffBudget>> BuildStaffBudgetAsync(long companyId, long budgetId, string salaryAccountCode, decimal incrementPercent, decimal vacancyPercent, decimal contributionPercent,
        string? contributionAccountCode, CancellationToken ct = default);

    /// <summary>Requests moving an approved amount on an account from one period to another (never more than the period holds).</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="budgetId">The budget (approved).</param>
    /// <param name="accountCode">The account.</param>
    /// <param name="fromPeriod">The giving period.</param>
    /// <param name="toPeriod">The receiving period.</param>
    /// <param name="amount">The amount.</param>
    /// <param name="reason">Why.</param>
    /// <param name="userId">Who asks.</param>
    /// <param name="costCentre">The cost centre.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The request id.</returns>
    Task<Result<long>> RequestRephaseAsync(long companyId, long budgetId, string accountCode, int fromPeriod, int toPeriod, decimal amount, string reason, long userId, string? costCentre = null,
        CancellationToken ct = default);

    /// <summary>Approves (applying it as a pair of re-phasing lines, the annual total unchanged) or rejects a re-phasing; not by the requester.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="rephaseId">The request.</param>
    /// <param name="approve">Approve or reject.</param>
    /// <param name="userId">Who decides.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success or why not.</returns>
    Task<Result> DecideRephaseAsync(long companyId, long rephaseId, bool approve, long userId, CancellationToken ct = default);
}
