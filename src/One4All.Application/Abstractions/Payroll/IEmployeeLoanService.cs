using One4All.Core.Common;

namespace One4All.Application.Abstractions.Payroll;

/// <summary>Manages employee loans: advancing them and recording repayments, posting through the engine.</summary>
public interface IEmployeeLoanService
{
    /// <summary>
    /// Grants a loan to an employee: posts Dr loan-receivable / Cr the funding account and opens the loan with
    /// the principal outstanding.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="employeeId">The employee.</param>
    /// <param name="reference">The loan reference.</param>
    /// <param name="principal">The amount advanced (positive).</param>
    /// <param name="installment">The scheduled repayment per period (informational).</param>
    /// <param name="date">The advance date.</param>
    /// <param name="loanReceivableAccountCode">The loan-receivable asset account (debited).</param>
    /// <param name="fundingAccountCode">The bank/cash account the advance is paid from (credited).</param>
    /// <param name="documentNumber">The advance document number.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The created loan id.</returns>
    Task<Result<long>> GrantAsync(long companyId, long employeeId, string reference, decimal principal, decimal installment, DateOnly date, string loanReceivableAccountCode, string fundingAccountCode, string documentNumber, CancellationToken ct = default);

    /// <summary>
    /// Records a repayment against a loan: posts Dr the funding account / Cr loan-receivable and reduces the
    /// outstanding balance, settling the loan when it reaches zero. Refuses more than the outstanding balance
    /// or a repayment against a settled loan.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="loanId">The loan.</param>
    /// <param name="amount">The repayment amount (positive).</param>
    /// <param name="date">The repayment date.</param>
    /// <param name="fundingAccountCode">The account the repayment funds come from (debited — e.g. net-pay control on a payroll deduction, or bank).</param>
    /// <param name="documentNumber">The repayment document number.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The loan's outstanding balance after the repayment.</returns>
    Task<Result<decimal>> RecordRepaymentAsync(long companyId, long loanId, decimal amount, DateOnly date, string fundingAccountCode, string documentNumber, CancellationToken ct = default);
}
