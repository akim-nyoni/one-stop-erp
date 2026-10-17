using One4All.Core.Common;

namespace One4All.Application.Abstractions.Payroll;

/// <summary>The result of a single employee's pay calculation (a preview, no posting).</summary>
/// <param name="EmployeeId">The employee.</param>
/// <param name="Gross">The gross earnings.</param>
/// <param name="Deductions">The total deductions.</param>
/// <param name="Net">The net pay.</param>
/// <param name="Lines">The resolved lines (code, amount).</param>
public readonly record struct PayCalculation(long EmployeeId, decimal Gross, decimal Deductions, decimal Net, IReadOnlyList<PayLine> Lines);

/// <summary>One resolved pay line.</summary>
/// <param name="ElementCode">The element code.</param>
/// <param name="Amount">The amount.</param>
public readonly record struct PayLine(string ElementCode, decimal Amount);

/// <summary>The result of a committed payroll run.</summary>
/// <param name="PayslipCount">The number of payslips produced.</param>
/// <param name="TotalGross">The total gross.</param>
/// <param name="TotalDeductions">The total deductions.</param>
/// <param name="TotalNet">The total net pay.</param>
/// <param name="JournalNumber">The GL journal number.</param>
public readonly record struct PayrollRunResult(int PayslipCount, decimal TotalGross, decimal TotalDeductions, decimal TotalNet, string JournalNumber);

/// <summary>
/// Payroll (brief §4.15.6). Calculates pay from configured, formula-driven pay elements (transparent and
/// reproducible) and commits a run - producing immutable payslips and a single balanced GL journal
/// (Dr earning expenses, Cr statutory/deduction liabilities, Cr net pay) through the central engine.
/// </summary>
public interface IPayrollService
{
    /// <summary>Calculates one employee's pay without posting (the "calculate against a sample employee" preview).</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="employeeId">The employee.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The calculation.</returns>
    Task<Result<PayCalculation>> CalculateAsync(long companyId, long employeeId, CancellationToken ct = default);

    /// <summary>
    /// Runs and commits payroll for every active employee: resolves each pay element in sequence, produces
    /// a payslip, and posts one balanced journal crediting net pay to the net-pay control account. A period
    /// is paid once: a second run for the same period label is refused (<c>Payroll.PeriodAlreadyRun</c>),
    /// as is a blank label.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="periodLabel">The pay-period label.</param>
    /// <param name="payDate">The pay date.</param>
    /// <param name="netPayAccountCode">The net-pay control account (credited with net pay).</param>
    /// <param name="documentNumber">The run document number.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The run result.</returns>
    Task<Result<PayrollRunResult>> RunAsync(long companyId, string periodLabel, DateOnly payDate, string netPayAccountCode, string documentNumber, CancellationToken ct = default);

    /// <summary>
    /// Runs and commits one pay group's payroll (brief §4.15.6: monthly staff, weekly wages and casuals run independently): only active
    /// employees in the group are paid, and the period guard applies per group. A blank group is the default payroll - employees with
    /// no pay group - which is what <see cref="RunAsync"/> runs.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="payGroup">The pay group (blank for the default payroll).</param>
    /// <param name="periodLabel">The pay-period label.</param>
    /// <param name="payDate">The pay date.</param>
    /// <param name="netPayAccountCode">The net-pay control account.</param>
    /// <param name="documentNumber">The run document number.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The run result.</returns>
    Task<Result<PayrollRunResult>> RunGroupAsync(long companyId, string? payGroup, string periodLabel, DateOnly payDate, string netPayAccountCode, string documentNumber, CancellationToken ct = default);

    /// <summary>Renders an employee's payslip (earnings, deductions, gross/net) to fixed-width plain text.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="employeeId">The employee.</param>
    /// <param name="periodLabel">The pay-period label shown on the payslip.</param>
    /// <param name="payDate">The pay date shown on the payslip.</param>
    /// <param name="width">The payslip character width.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The payslip text, or NotFound when the employee does not exist.</returns>
    Task<Result<string>> RenderPayslipTextAsync(long companyId, long employeeId, string periodLabel, DateOnly payDate, int width = One4All.Application.Payroll.PayslipFormatter.DefaultWidth, CancellationToken ct = default);
}
