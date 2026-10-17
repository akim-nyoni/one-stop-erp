using One4All.Core.Common;

namespace One4All.Application.Abstractions.Payroll;

/// <summary>One line of a supplementary run: an amount on an element for an employee (negative recovers an overpayment).</summary>
/// <param name="EmployeeId">The employee.</param>
/// <param name="ElementCode">The pay element.</param>
/// <param name="Amount">The amount.</param>
public readonly record struct SupplementaryLine(long EmployeeId, string ElementCode, decimal Amount);

/// <summary>A supplementary / adjustment run correcting a committed period.</summary>
public sealed record SupplementaryRunRequest
{
    /// <summary>Gets the company.</summary>
    public long CompanyId { get; init; }

    /// <summary>Gets the committed period being corrected.</summary>
    public string PeriodLabel { get; init; } = string.Empty;

    /// <summary>Gets the pay date.</summary>
    public DateOnly PayDate { get; init; }

    /// <summary>Gets the net-pay control account.</summary>
    public string NetPayAccountCode { get; init; } = string.Empty;

    /// <summary>Gets the run's document number.</summary>
    public string DocumentNumber { get; init; } = string.Empty;

    /// <summary>Gets why the correction is made.</summary>
    public string Reason { get; init; } = string.Empty;

    /// <summary>Gets the lines.</summary>
    public IReadOnlyList<SupplementaryLine> Lines { get; init; } = [];
}

/// <summary>One element of a backpay calculation for a period already paid.</summary>
/// <param name="ForPeriod">The period.</param>
/// <param name="ElementCode">The element.</param>
/// <param name="Paid">What was paid (including earlier corrections).</param>
/// <param name="Due">What is due under the change.</param>
public readonly record struct BackpayPreviewLine(string ForPeriod, string ElementCode, decimal Paid, decimal Due)
{
    /// <summary>Gets the difference owed (negative when overpaid).</summary>
    public decimal Difference => Due - Paid;
}

/// <summary>A retrospective basic-salary change paid as backpay in a later period.</summary>
public sealed record BackpayRequest
{
    /// <summary>Gets the company.</summary>
    public long CompanyId { get; init; }

    /// <summary>Gets the employee.</summary>
    public long EmployeeId { get; init; }

    /// <summary>Gets the new basic salary.</summary>
    public decimal NewBasicSalary { get; init; }

    /// <summary>Gets the date the change takes effect from (periods paid on or after it are recalculated).</summary>
    public DateOnly EffectiveFrom { get; init; }

    /// <summary>Gets the period the backpay is paid in.</summary>
    public string PeriodLabel { get; init; } = string.Empty;

    /// <summary>Gets the pay date.</summary>
    public DateOnly PayDate { get; init; }

    /// <summary>Gets the net-pay control account.</summary>
    public string NetPayAccountCode { get; init; } = string.Empty;

    /// <summary>Gets the backpay run's document number.</summary>
    public string DocumentNumber { get; init; } = string.Empty;

    /// <summary>Gets the reason (e.g. the award or agreement).</summary>
    public string Reason { get; init; } = string.Empty;
}

/// <summary>
/// Payroll corrections after commit (brief §4.15.6): a committed run is never edited - it is corrected by a visible supplementary
/// run, settled as backpay in a later period, or undone by a formally approved reversal that writes a reversing journal. Also the
/// go-live parallel run: the legacy system's figures are imported for line-by-line comparison (PR8).
/// </summary>
public interface IPayrollAdjustmentService
{
    /// <summary>
    /// Runs a supplementary payroll for a committed period: one payslip per employee with the given element amounts, posted as
    /// one balanced journal (negative amounts reverse the sides). Refused when the period has no live regular run
    /// (<c>Payroll.PeriodNotRun</c>), the document number is blank or already used (<c>Payroll.DuplicateRun</c>), or an
    /// element or employee is unknown.
    /// </summary>
    /// <param name="request">The run.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The run result.</returns>
    Task<Result<PayrollRunResult>> RunSupplementaryAsync(SupplementaryRunRequest request, CancellationToken ct = default);

    /// <summary>
    /// Reverses a committed run (by its document or journal number): reverses its journal through the engine, writes negative
    /// payslips and marks the run reversed. Needs a reason and an approver who did not make the run (<c>Payroll.SameApprover</c>);
    /// a run is reversed once (<c>Payroll.AlreadyReversed</c>) and a reversal cannot itself be reversed. A reversed regular
    /// period may be run again.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="runNumber">The run's document or journal number.</param>
    /// <param name="date">The reversal date (an open period).</param>
    /// <param name="reason">The mandatory reason.</param>
    /// <param name="approvedByUserId">The approving user.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The reversal run's totals (negative).</returns>
    Task<Result<PayrollRunResult>> ReverseRunAsync(long companyId, string runNumber, DateOnly date, string reason, long approvedByUserId, CancellationToken ct = default);

    /// <summary>
    /// Recalculates every period the employee was paid on or after the effective date at the new basic salary (current element
    /// set-up) and compares it, element by element, with what was paid including earlier corrections and backpay.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="employeeId">The employee.</param>
    /// <param name="newBasicSalary">The new basic salary.</param>
    /// <param name="effectiveFrom">The effective date.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The non-zero differences by period and element.</returns>
    Task<Result<IReadOnlyList<BackpayPreviewLine>>> CalculateBackpayAsync(long companyId, long employeeId, decimal newBasicSalary, DateOnly effectiveFrom, CancellationToken ct = default);

    /// <summary>
    /// Pays the backpay: one backpay-run payslip with the summed differences, posted like any run, the calculation kept for PR10,
    /// and the employee's basic salary set to the new figure. Refused when nothing is owed either way (<c>Payroll.NoBackpay</c>).
    /// </summary>
    /// <param name="request">The backpay.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The run result.</returns>
    Task<Result<PayrollRunResult>> PayBackpayAsync(BackpayRequest request, CancellationToken ct = default);

    /// <summary>
    /// Imports the legacy system's payroll for a period for the parallel run: CSV lines "employee number, element code, amount"
    /// (a header line is skipped; NET compares net pay). Replaces any earlier import of the period.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="periodLabel">The period.</param>
    /// <param name="csv">The lines.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The number of lines imported.</returns>
    Task<Result<int>> ImportLegacyPayrollAsync(long companyId, string periodLabel, string csv, CancellationToken ct = default);
}
