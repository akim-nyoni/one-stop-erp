using One4All.Core.Common;

namespace One4All.Application.Abstractions.Accounting;

/// <summary>The result of an imprest reimbursement.</summary>
/// <param name="ReimbursedAmount">The amount paid into petty cash to restore the float.</param>
/// <param name="FloatBalanceAfter">The petty-cash balance after the reimbursement.</param>
/// <param name="JournalNumber">The reimbursement journal number, or null when the float was already full.</param>
public readonly record struct ImprestResult(decimal ReimbursedAmount, decimal FloatBalanceAfter, string? JournalNumber);

/// <summary>
/// Petty cash on the imprest system (brief §4.7): expenses are disbursed from a fixed float, and the float
/// is periodically reimbursed back up to its set amount by transferring the spent total from the bank. All
/// postings go through the central engine.
/// </summary>
public interface IPettyCashService
{
    /// <summary>Disburses a petty-cash expense: Dr the expense account, Cr the petty-cash account.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="amount">The expense amount (positive).</param>
    /// <param name="date">The posting date.</param>
    /// <param name="expenseAccountCode">The expense account debited.</param>
    /// <param name="pettyCashAccountCode">The petty-cash account credited.</param>
    /// <param name="documentNumber">The voucher/document number.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The disbursement journal number, or an error.</returns>
    Task<Result<string>> DisburseAsync(long companyId, decimal amount, DateOnly date, string expenseAccountCode, string pettyCashAccountCode, string documentNumber, CancellationToken ct = default);

    /// <summary>
    /// Reimburses the float back to its set amount: computes the shortfall (float − current balance) and, when
    /// positive, posts Dr petty cash / Cr bank for it, restoring the float. A full float is a no-op.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="date">The posting date.</param>
    /// <param name="pettyCashAccountCode">The petty-cash account.</param>
    /// <param name="bankAccountCode">The bank account the reimbursement is drawn from.</param>
    /// <param name="floatAmount">The set float amount to restore to.</param>
    /// <param name="documentNumber">The reimbursement document number.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The imprest result, or an error.</returns>
    Task<Result<ImprestResult>> ReimburseToFloatAsync(long companyId, DateOnly date, string pettyCashAccountCode, string bankAccountCode, decimal floatAmount, string documentNumber, CancellationToken ct = default);
}
