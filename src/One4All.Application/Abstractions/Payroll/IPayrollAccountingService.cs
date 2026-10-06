using One4All.Core.Common;

namespace One4All.Application.Abstractions.Payroll;

/// <summary>A period-end salary accrual.</summary>
/// <param name="Employees">Employees accrued for.</param>
/// <param name="Amount">The amount accrued.</param>
/// <param name="JournalNumber">The accrual journal.</param>
/// <param name="ReversalJournalNumber">Its reversal on the first day of the next month.</param>
public sealed record SalaryAccrual(int Employees, decimal Amount, string JournalNumber, string ReversalJournalNumber);

/// <summary>Labour capitalised into a contract's work in progress.</summary>
/// <param name="Timesheets">Timesheet lines capitalised.</param>
/// <param name="Hours">Hours.</param>
/// <param name="Amount">The cost capitalised.</param>
/// <param name="JournalNumber">The journal.</param>
public sealed record LabourCapitalised(int Timesheets, decimal Hours, decimal Amount, string JournalNumber);

/// <summary>
/// Payroll accounting at period end (brief §4.15.9): accrue the salaries of employees not yet paid for the month when the pay date falls
/// after the cut-off (reversed on the first of the next month), and capitalise the labour booked on contract timesheets into the
/// contract's WIP (Dr WIP, Cr the payroll cost recovered), each timesheet once.
/// </summary>
public interface IPayrollAccountingService
{
    /// <summary>
    /// Accrues basic salary for every employee employed at the date who has no payslip dated in its month (the PR63 list), posted and
    /// auto-reversed through the accrual service. Refused when nobody is owed (<c>Payroll.NothingToAccrue</c>).
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="asAt">The period end.</param>
    /// <param name="expenseAccountCode">The salary expense account.</param>
    /// <param name="accrualAccountCode">The accrued salaries liability.</param>
    /// <param name="documentNumber">The document number.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The accrual.</returns>
    Task<Result<SalaryAccrual>> AccrueSalariesAsync(long companyId, DateOnly asAt, string expenseAccountCode, string accrualAccountCode, string documentNumber, CancellationToken ct = default);

    /// <summary>
    /// Capitalises a contract's timesheet labour up to a date that has not been capitalised before: Dr the contract's WIP account, Cr
    /// the payroll cost account the labour is recovered from. Refused when nothing is left (<c>Payroll.NothingToCapitalise</c>).
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="contractId">The contract.</param>
    /// <param name="upTo">Timesheets dated up to and including this date.</param>
    /// <param name="wipAccountCode">The contract's WIP account.</param>
    /// <param name="labourRecoveryAccountCode">The payroll cost account credited.</param>
    /// <param name="documentNumber">The document number.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>What was capitalised.</returns>
    Task<Result<LabourCapitalised>> CapitaliseTimesheetsAsync(long companyId, long contractId, DateOnly upTo, string wipAccountCode, string labourRecoveryAccountCode, string documentNumber,
        CancellationToken ct = default);
}
