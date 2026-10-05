using One4All.Core.Common;

namespace One4All.Core.Payroll;

/// <summary>
/// One line of the legacy system's payroll for a period, imported for the go-live parallel run (brief §4.15.6, PR8):
/// what the old system paid an employee on an element, compared line by line with One4All's payslips.
/// </summary>
public sealed class LegacyPayslipLine : Entity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private LegacyPayslipLine()
    {
        PeriodLabel = string.Empty;
        EmployeeNumber = string.Empty;
        ElementCode = string.Empty;
    }

    /// <summary>Creates the line.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="periodLabel">The pay period.</param>
    /// <param name="employeeNumber">The employee number.</param>
    /// <param name="elementCode">The element code (mapped to One4All's codes; NET for net pay).</param>
    /// <param name="amount">The amount the legacy system paid.</param>
    public LegacyPayslipLine(long companyId, string periodLabel, string employeeNumber, string elementCode, decimal amount)
    {
        CompanyId = companyId;
        PeriodLabel = periodLabel;
        EmployeeNumber = employeeNumber.Trim().ToUpperInvariant();
        ElementCode = elementCode.Trim().ToUpperInvariant();
        Amount = amount;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The pay period.</summary>
    public string PeriodLabel { get; private set; }

    /// <summary>The employee number.</summary>
    public string EmployeeNumber { get; private set; }

    /// <summary>The element code.</summary>
    public string ElementCode { get; private set; }

    /// <summary>The amount.</summary>
    public decimal Amount { get; private set; }
}

/// <summary>
/// One element of a backpay calculation (PR10): for a period already paid, what was paid on the element, what is now due under the
/// retrospective change, and the difference settled in the backpay run.
/// </summary>
public sealed class BackpayLine : Entity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private BackpayLine()
    {
        ForPeriod = string.Empty;
        ElementCode = string.Empty;
        RunReference = string.Empty;
        Reason = string.Empty;
    }

    /// <summary>Creates the line.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="employeeId">The employee.</param>
    /// <param name="forPeriod">The period being corrected.</param>
    /// <param name="elementCode">The element.</param>
    /// <param name="paid">What was paid.</param>
    /// <param name="due">What is due under the change.</param>
    /// <param name="runReference">The backpay run that settled it.</param>
    /// <param name="paidOn">The backpay run's pay date.</param>
    /// <param name="reason">The retrospective change.</param>
    public BackpayLine(long companyId, long employeeId, string forPeriod, string elementCode, decimal paid, decimal due, string runReference, DateOnly paidOn, string reason)
    {
        CompanyId = companyId;
        EmployeeId = employeeId;
        ForPeriod = forPeriod;
        ElementCode = elementCode;
        Paid = paid;
        Due = due;
        RunReference = runReference;
        PaidOn = paidOn;
        Reason = reason;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The employee.</summary>
    public long EmployeeId { get; private set; }

    /// <summary>The period being corrected.</summary>
    public string ForPeriod { get; private set; }

    /// <summary>The element.</summary>
    public string ElementCode { get; private set; }

    /// <summary>What was paid.</summary>
    public decimal Paid { get; private set; }

    /// <summary>What is due under the change.</summary>
    public decimal Due { get; private set; }

    /// <summary>Gets the difference settled.</summary>
    public decimal Difference => Due - Paid;

    /// <summary>The backpay run's document number.</summary>
    public string RunReference { get; private set; }

    /// <summary>The backpay run's pay date.</summary>
    public DateOnly PaidOn { get; private set; }

    /// <summary>The retrospective change.</summary>
    public string Reason { get; private set; }
}
