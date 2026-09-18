using One4All.Core.Budgeting;
using One4All.Core.Common;

namespace One4All.Application.Abstractions.Budgeting;

/// <summary>A budget line to create (account × period × amount).</summary>
/// <param name="AccountCode">The account.</param>
/// <param name="Period">The period (month 1–12).</param>
/// <param name="Amount">The budgeted amount.</param>
/// <param name="CostCentre">The cost centre, if any.</param>
public readonly record struct NewBudgetLine(string AccountCode, int Period, decimal Amount, string? CostCentre = null);

/// <summary>One row of a budget-vs-actual comparison.</summary>
/// <param name="AccountCode">The account.</param>
/// <param name="Budget">The budget for the account (to the as-at period).</param>
/// <param name="Actual">The actual for the account (to the as-at period).</param>
public readonly record struct BudgetVsActualRow(string AccountCode, decimal Budget, decimal Actual)
{
    /// <summary>The variance (budget − actual); positive is favourable for an expense.</summary>
    public decimal Variance => Budget - Actual;

    /// <summary>The percentage of budget used.</summary>
    public decimal PercentUsed => Budget == 0m ? 0m : decimal.Round(Actual / Budget * 100m, 2, MidpointRounding.ToEven);
}

/// <summary>A budget-vs-actual report.</summary>
/// <param name="FiscalYearId">The fiscal year.</param>
/// <param name="Rows">The rows.</param>
public readonly record struct BudgetVsActualReport(long FiscalYearId, IReadOnlyList<BudgetVsActualRow> Rows)
{
    /// <summary>The total budget.</summary>
    public decimal TotalBudget => Rows.Sum(r => r.Budget);

    /// <summary>The total actual.</summary>
    public decimal TotalActual => Rows.Sum(r => r.Actual);
}

/// <summary>The result of a budget availability check.</summary>
/// <param name="AccountCode">The account.</param>
/// <param name="Budget">The budget for the account.</param>
/// <param name="Actual">The actual to date.</param>
/// <param name="Available">The available balance (budget − actual).</param>
/// <param name="Proposed">The proposed spend tested.</param>
/// <param name="Passes">Whether the proposed spend fits within the available balance.</param>
public readonly record struct AvailabilityResult(string AccountCode, decimal Budget, decimal Actual, decimal Available, decimal Proposed, bool Passes);

/// <summary>
/// Budgeting, Planning &amp; Commitment Control (brief §4.18). Budgets live in their own schema and never
/// post to the GL; they are joined to GL actuals by posting dimensions at report time. Versions are
/// retained permanently — a new active version supersedes the previous one for the same year/type/department.
/// </summary>
public interface IBudgetingService
{
    /// <summary>Creates a new budget version (auto-numbered for the year/type/department).</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="fiscalYearId">The fiscal year.</param>
    /// <param name="label">The label.</param>
    /// <param name="type">The budget type/scenario.</param>
    /// <param name="department">The preparing department (null for company-wide).</param>
    /// <param name="lines">The budget lines.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The created budget id.</returns>
    Task<Result<long>> CreateBudgetAsync(long companyId, long fiscalYearId, string label, BudgetType type, string? department, IReadOnlyList<NewBudgetLine> lines, CancellationToken ct = default);

    /// <summary>Activates a budget version, superseding the prior active one for the same year/type/department.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="budgetId">The budget to activate.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success or an error.</returns>
    Task<Result> ActivateAsync(long companyId, long budgetId, CancellationToken ct = default);

    /// <summary>Compares the active budget to GL actuals, account by account, to an as-at period.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="fiscalYearId">The fiscal year.</param>
    /// <param name="type">The budget type/scenario.</param>
    /// <param name="department">The department (null for company-wide).</param>
    /// <param name="asAtPeriod">The as-at period (month 1–12).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The budget-vs-actual report.</returns>
    Task<Result<BudgetVsActualReport>> BudgetVsActualAsync(long companyId, long fiscalYearId, BudgetType type, string? department, int asAtPeriod, CancellationToken ct = default);

    /// <summary>
    /// Checks whether a proposed spend fits within the available balance on an account (budget − actual),
    /// the check the GL/POP performs before committing spend when fund control is enabled.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="fiscalYearId">The fiscal year.</param>
    /// <param name="type">The budget type/scenario.</param>
    /// <param name="department">The department (null for company-wide).</param>
    /// <param name="accountCode">The account.</param>
    /// <param name="proposedAmount">The proposed spend.</param>
    /// <param name="asAtPeriod">The as-at period (month 1–12).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The availability result.</returns>
    Task<Result<AvailabilityResult>> CheckAvailabilityAsync(long companyId, long fiscalYearId, BudgetType type, string? department, string accountCode, decimal proposedAmount, int asAtPeriod, CancellationToken ct = default);
}
