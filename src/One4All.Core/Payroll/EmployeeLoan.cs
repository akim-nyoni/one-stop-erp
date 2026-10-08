using One4All.Core.Common;

namespace One4All.Core.Payroll;

/// <summary>The state of an employee loan.</summary>
public enum EmployeeLoanStatus
{
    /// <summary>Outstanding.</summary>
    Active = 1,

    /// <summary>Fully repaid.</summary>
    Settled = 2,
}

/// <summary>
/// A loan advanced to an employee (<c>pay.employee_loan</c>), repaid over time (typically by payroll
/// deduction). The advance debits a loan-receivable asset; repayments credit it back. Tracks the balance.
/// </summary>
public sealed class EmployeeLoan : AuditableEntity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private EmployeeLoan()
    {
        Reference = string.Empty;
        LoanReceivableAccountCode = string.Empty;
    }

    /// <summary>Grants a loan.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="employeeId">The employee.</param>
    /// <param name="reference">The loan reference.</param>
    /// <param name="principal">The amount advanced.</param>
    /// <param name="installment">The scheduled repayment per period (informational).</param>
    /// <param name="grantedOn">The advance date.</param>
    /// <param name="loanReceivableAccountCode">The loan-receivable asset account.</param>
    public EmployeeLoan(long companyId, long employeeId, string reference, decimal principal, decimal installment, DateOnly grantedOn, string loanReceivableAccountCode)
    {
        CompanyId = companyId;
        EmployeeId = employeeId;
        Reference = reference;
        Principal = principal;
        Installment = installment;
        GrantedOn = grantedOn;
        LoanReceivableAccountCode = loanReceivableAccountCode;
        Outstanding = principal;
        Status = EmployeeLoanStatus.Active;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The employee.</summary>
    public long EmployeeId { get; private set; }

    /// <summary>The loan reference.</summary>
    public string Reference { get; private set; }

    /// <summary>The amount advanced.</summary>
    public decimal Principal { get; private set; }

    /// <summary>The scheduled repayment per period (informational).</summary>
    public decimal Installment { get; private set; }

    /// <summary>Gets or sets the annual interest rate the loan charges (0 for an interest-free loan, taxed as a benefit at the deemed rate).</summary>
    public decimal InterestRatePercent { get; set; }

    /// <summary>Gets or sets a value indicating whether each pay run deducts the instalment (brief §4.15.5: automatic deduction each run).</summary>
    public bool DeductFromPay { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether deductions are suspended (unpaid leave, hardship); interest still accrues.</summary>
    public bool Suspended { get; set; }

    /// <summary>Adds a period's interest to the balance.</summary>
    /// <param name="interest">The interest.</param>
    public void ChargeInterest(decimal interest) => Outstanding = decimal.Round(Outstanding + interest, 4, MidpointRounding.ToEven);

    /// <summary>Puts a repayment back (its pay run was reversed), reopening a loan it had settled.</summary>
    /// <param name="amount">The repayment to restore.</param>
    public void Restore(decimal amount)
    {
        Outstanding = decimal.Round(Outstanding + amount, 4, MidpointRounding.ToEven);
        if (Outstanding > 0m)
        {
            Status = EmployeeLoanStatus.Active;
        }
    }

    /// <summary>This period's interest and the most the instalment can take: the instalment, but never more than the balance with its interest.</summary>
    /// <param name="outstanding">The balance before the period.</param>
    /// <param name="installment">The instalment.</param>
    /// <param name="ratePercent">The annual rate.</param>
    /// <param name="periodsPerYear">The employee's pay periods a year.</param>
    /// <returns>The interest and the amount due.</returns>
    public static (decimal Interest, decimal Due) Instalment(decimal outstanding, decimal installment, decimal ratePercent, int periodsPerYear)
    {
        decimal interest = ratePercent <= 0m || outstanding <= 0m ? 0m : decimal.Round(outstanding * ratePercent / 100m / Math.Max(1, periodsPerYear), 2, MidpointRounding.AwayFromZero);
        return (interest, Math.Max(0m, Math.Min(installment, outstanding + interest)));
    }

    /// <summary>The advance date.</summary>
    public DateOnly GrantedOn { get; private set; }

    /// <summary>The loan-receivable asset account.</summary>
    public string LoanReceivableAccountCode { get; private set; }

    /// <summary>The amount still owed.</summary>
    public decimal Outstanding { get; private set; }

    /// <summary>The status.</summary>
    public EmployeeLoanStatus Status { get; private set; }

    /// <summary>Records a repayment, reducing the outstanding balance and settling the loan when it reaches zero.</summary>
    /// <param name="amount">The repayment amount.</param>
    public void Repay(decimal amount)
    {
        Outstanding = decimal.Round(Outstanding - amount, 4, MidpointRounding.ToEven);
        if (Outstanding <= 0m)
        {
            Outstanding = 0m;
            Status = EmployeeLoanStatus.Settled;
        }
    }
}
