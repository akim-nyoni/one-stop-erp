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

/// <summary>
/// A deduction suspended by net-pay protection, or arrears recovered later (<c>pay.deduction_arrear</c>): what the run tried to deduct
/// (this period's amount plus arrears brought forward), what it deducted, and the movement in the arrears carried forward.
/// </summary>
public sealed class DeductionArrear : Entity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private DeductionArrear()
    {
        ElementCode = string.Empty;
        PeriodLabel = string.Empty;
        RunReference = string.Empty;
        Reason = string.Empty;
    }

    /// <summary>Creates the entry.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="employeeId">The employee.</param>
    /// <param name="elementCode">The deduction.</param>
    /// <param name="periodLabel">The period.</param>
    /// <param name="runReference">The run.</param>
    /// <param name="payDate">The pay date.</param>
    /// <param name="attempted">This period's amount plus arrears brought forward.</param>
    /// <param name="deducted">What was deducted.</param>
    /// <param name="movement">The change in the arrears carried forward (positive suspends, negative recovers).</param>
    /// <param name="reason">Why.</param>
    public DeductionArrear(long companyId, long employeeId, string elementCode, string periodLabel, string runReference, DateOnly payDate, decimal attempted, decimal deducted, decimal movement, string reason)
    {
        CompanyId = companyId;
        EmployeeId = employeeId;
        ElementCode = elementCode;
        PeriodLabel = periodLabel;
        RunReference = runReference;
        PayDate = payDate;
        Attempted = attempted;
        Deducted = deducted;
        Movement = movement;
        Reason = reason;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The employee.</summary>
    public long EmployeeId { get; private set; }

    /// <summary>The deduction.</summary>
    public string ElementCode { get; private set; }

    /// <summary>The period.</summary>
    public string PeriodLabel { get; private set; }

    /// <summary>The run.</summary>
    public string RunReference { get; private set; }

    /// <summary>The pay date.</summary>
    public DateOnly PayDate { get; private set; }

    /// <summary>This period's amount plus arrears brought forward.</summary>
    public decimal Attempted { get; private set; }

    /// <summary>What was deducted.</summary>
    public decimal Deducted { get; private set; }

    /// <summary>The change in arrears carried forward.</summary>
    public decimal Movement { get; private set; }

    /// <summary>Why.</summary>
    public string Reason { get; private set; }
}

/// <summary>
/// An AP invoice raised for a payroll third party (<c>pay.remittance_invoice</c>, brief §4.15.5): the tax authority, a pension or
/// medical scheme, or a garnishee creditor, for what a run deducted (and the employer contributed) on its elements.
/// </summary>
public sealed class RemittanceInvoice : Entity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private RemittanceInvoice()
    {
        RunReference = string.Empty;
        PeriodLabel = string.Empty;
        Payee = string.Empty;
        AccountCode = string.Empty;
        DocumentNumber = string.Empty;
    }

    /// <summary>Creates the record.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="runReference">The run.</param>
    /// <param name="periodLabel">The period.</param>
    /// <param name="payee">The scheme code or garnishee order reference.</param>
    /// <param name="supplierId">The AP supplier.</param>
    /// <param name="accountCode">The payroll liability cleared.</param>
    /// <param name="amount">The amount.</param>
    /// <param name="documentNumber">The AP invoice number.</param>
    /// <param name="raisedOn">The invoice date.</param>
    public RemittanceInvoice(long companyId, string runReference, string periodLabel, string payee, long supplierId, string accountCode, decimal amount, string documentNumber, DateOnly raisedOn)
    {
        CompanyId = companyId;
        RunReference = runReference;
        PeriodLabel = periodLabel;
        Payee = payee;
        SupplierId = supplierId;
        AccountCode = accountCode;
        Amount = amount;
        DocumentNumber = documentNumber;
        RaisedOn = raisedOn;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The run.</summary>
    public string RunReference { get; private set; }

    /// <summary>The period.</summary>
    public string PeriodLabel { get; private set; }

    /// <summary>The scheme code or garnishee order reference.</summary>
    public string Payee { get; private set; }

    /// <summary>The AP supplier.</summary>
    public long SupplierId { get; private set; }

    /// <summary>The payroll liability cleared into AP.</summary>
    public string AccountCode { get; private set; }

    /// <summary>The amount.</summary>
    public decimal Amount { get; private set; }

    /// <summary>The AP invoice number.</summary>
    public string DocumentNumber { get; private set; }

    /// <summary>The invoice date.</summary>
    public DateOnly RaisedOn { get; private set; }
}
