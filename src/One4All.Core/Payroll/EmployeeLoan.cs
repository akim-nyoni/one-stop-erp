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
