using One4All.Core.Common;

namespace One4All.Core.Payroll;

/// <summary>
/// An employee's figure for the tax year so far from the previous payroll (<c>pay.ytd_opening</c>, brief §10 data migration): taxable pay
/// (<see cref="TaxableCode"/>) or what a tax element deducted, so cumulative tax in the first year on this payroll counts the months paid before it.
/// </summary>
public sealed class PayrollYtdOpening : AuditableEntity, ICompanyScoped
{
    /// <summary>The code for taxable pay so far.</summary>
    public const string TaxableCode = "TAXABLE";

    private PayrollYtdOpening() => Code = string.Empty;

    /// <summary>Records the figure.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="employeeId">The employee.</param>
    /// <param name="taxYear">The tax year.</param>
    /// <param name="code">TAXABLE or a pay element code.</param>
    /// <param name="amount">The amount so far.</param>
    public PayrollYtdOpening(long companyId, long employeeId, int taxYear, string code, decimal amount)
    {
        CompanyId = companyId;
        EmployeeId = employeeId;
        TaxYear = taxYear;
        Code = code;
        Amount = amount;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The employee.</summary>
    public long EmployeeId { get; private set; }

    /// <summary>The tax year.</summary>
    public int TaxYear { get; private set; }

    /// <summary>TAXABLE or a pay element code.</summary>
    public string Code { get; private set; }

    /// <summary>The amount so far.</summary>
    public decimal Amount { get; set; }
}
