using One4All.Core.Accounting;
using One4All.Core.Budgeting;
using One4All.Core.Common;

namespace One4All.Application.Abstractions.Budgeting;

/// <summary>A budget line to create (account × period × amount).</summary>
/// <param name="AccountCode">The account.</param>
/// <param name="Period">The period (month 1–12).</param>
/// <param name="Amount">The budgeted amount.</param>
/// <param name="CostCentre">The cost centre, if any.</param>
/// <param name="Quantity">The budgeted volume behind the amount, for the volume / price split.</param>
/// <param name="Currency">The currency the line was planned in, if foreign.</param>
/// <param name="PlannedRate">The planned rate (base per unit of the currency).</param>
/// <param name="Project">The project the line budgets for.</param>
/// <param name="Fund">The fund (vote) the line budgets for.</param>
/// <param name="Branch">The branch the line budgets for.</param>
public readonly record struct NewBudgetLine(string AccountCode, int Period, decimal Amount, string? CostCentre = null, decimal? Quantity = null, CurrencyCode? Currency = null, decimal? PlannedRate = null,
    string? Project = null, string? Fund = null, string? Branch = null);

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
/// <param name="Committed">Open commitments (encumbrances) reserved against the account.</param>
/// <param name="Available">The available balance (budget − actual − committed).</param>
/// <param name="Proposed">The proposed spend tested.</param>
/// <param name="Passes">Whether the proposed spend fits within the available balance.</param>
public readonly record struct AvailabilityResult(string AccountCode, decimal Budget, decimal Actual, decimal Committed, decimal Available, decimal Proposed, bool Passes);

/// <summary>An analysis dimension budgets and actuals can be compared on.</summary>
public enum BudgetDimension
{
    /// <summary>The fund (vote).</summary>
    Fund = 1,

    /// <summary>The project.</summary>
    Project = 2,

    /// <summary>The branch.</summary>
    Branch = 3,
}

/// <summary>Budget against actual for one dimension value and account.</summary>
/// <param name="Value">The fund, project or branch ("" for lines without one).</param>
/// <param name="AccountCode">The account.</param>
/// <param name="Budget">Budgeted to the as-at period.</param>
/// <param name="Actual">Posted to the as-at period (debit-positive).</param>
public sealed record DimensionBudgetRow(string Value, string AccountCode, decimal Budget, decimal Actual)
{
    /// <summary>Gets what is left (budget less actual).</summary>
    public decimal Remaining => Budget - Actual;
}

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

    /// <summary>
    /// Prepares an unsealed budget in a currency at a budget rate held on the header (brief §4.16): its lines are planned in that currency and their
    /// base equivalents kept at the rate, so a new rate re-states them; the FX variance then measures against the budget rate. Null clears it.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="budgetId">The budget.</param>
    /// <param name="currency">The currency, or null for the base currency.</param>
    /// <param name="rate">The budget rate (base per unit of the currency).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or why it was refused.</returns>
    Task<Result> SetBudgetCurrencyAsync(long companyId, long budgetId, CurrencyCode? currency, decimal? rate, CancellationToken ct = default);

    /// <summary>
    /// Records an actual volume against a budgeted account (units sold, tonnes, hours) for the volume variance. Refused for an unknown
    /// account (<c>Budget.AccountNotFound</c>) or a zero quantity (<c>Budget.VolumeZero</c>).
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="accountCode">The account.</param>
    /// <param name="department">The department, for a departmental budget.</param>
    /// <param name="date">The date.</param>
    /// <param name="quantity">The volume.</param>
    /// <param name="reference">Where the figure came from.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The record id.</returns>
    Task<Result<long>> RecordActualVolumeAsync(long companyId, string accountCode, string? department, DateOnly date, decimal quantity, string reference, CancellationToken ct = default);

    /// <summary>
    /// Activates a budget version, superseding the prior active one for the same year/type/department. Where the company requires approval the version must be approved,
    /// and the activating user (when given) may be neither its submitter nor its approver (brief §4.18.4 segregation).
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="budgetId">The budget to activate.</param>
    /// <param name="activatedByUserId">Who activates it (recorded on the version); null for a system activation.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success or an error.</returns>
    Task<Result> ActivateAsync(long companyId, long budgetId, long? activatedByUserId = null, CancellationToken ct = default);

    /// <summary>
    /// Compares the active budget to actuals by fund, project or branch and account, to an as-at period (brief §4.18.1: budget lines key on the
    /// full dimension set). Actuals come from the posted journal lines carrying the dimension.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="fiscalYearId">The year.</param>
    /// <param name="type">The budget type.</param>
    /// <param name="department">The budget's department (null: company-wide).</param>
    /// <param name="dimension">Fund, project or branch.</param>
    /// <param name="value">One value only (null: all).</param>
    /// <param name="asAtPeriod">Up to this period.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The rows by value then account, or why not.</returns>
    Task<Result<IReadOnlyList<DimensionBudgetRow>>> BudgetVsActualByDimensionAsync(long companyId, long fiscalYearId, BudgetType type, string? department, BudgetDimension dimension, string? value,
        int asAtPeriod, CancellationToken ct = default);

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

    /// <summary>
    /// Raises a budget commitment (encumbrance) reserving an amount against a budget line — the reservation
    /// a purchase order makes so its funds cannot be committed twice. Open commitments reduce the available
    /// balance in <see cref="CheckAvailabilityAsync"/>.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="fiscalYearId">The fiscal year.</param>
    /// <param name="type">The budget type/scenario.</param>
    /// <param name="department">The department (null for company-wide).</param>
    /// <param name="accountCode">The account committed against.</param>
    /// <param name="period">The period (month 1–12).</param>
    /// <param name="amount">The committed amount (positive).</param>
    /// <param name="reference">The source reference (e.g. the purchase-order number).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The created commitment id, or an error.</returns>
    Task<Result<long>> CommitAsync(long companyId, long fiscalYearId, BudgetType type, string? department, string accountCode, int period, decimal amount, string reference, CancellationToken ct = default);

    /// <summary>Releases an open commitment, freeing the reserved budget (e.g. when it is invoiced or cancelled).</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="commitmentId">The commitment to release.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or NotFound/Rule when it is missing or already released.</returns>
    Task<Result> ReleaseCommitmentAsync(long companyId, long commitmentId, CancellationToken ct = default);
}
