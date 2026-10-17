using One4All.Core.Accounting;
using One4All.Core.Common;

namespace One4All.Application.Abstractions.Accounting;

/// <summary>The result of an imprest reimbursement.</summary>
/// <param name="ReimbursedAmount">The amount paid into petty cash to restore the float.</param>
/// <param name="FloatBalanceAfter">The petty-cash balance after the reimbursement.</param>
/// <param name="JournalNumber">The reimbursement journal number, or null when the float was already full.</param>
public readonly record struct ImprestResult(decimal ReimbursedAmount, decimal FloatBalanceAfter, string? JournalNumber);

/// <summary>A petty cash fund as created or changed.</summary>
/// <param name="Code">The fund code.</param>
/// <param name="Name">The fund name.</param>
/// <param name="AccountCode">The petty cash ledger account.</param>
/// <param name="BankAccountCode">The bank ledger account reimbursements are drawn from.</param>
/// <param name="FloatAmount">The fixed float amount.</param>
/// <param name="OverShortAccountCode">The account that takes count shortages and overages.</param>
/// <param name="CustodianUsername">The custodian's username, when named.</param>
/// <param name="BranchCode">The branch code, when any.</param>
/// <param name="IsActive">Whether the fund is in use.</param>
public sealed record PettyCashFundInput(string Code, string Name, string AccountCode, string BankAccountCode, decimal FloatAmount, string? OverShortAccountCode = null,
    string? CustodianUsername = null, string? BranchCode = null, bool IsActive = true);

/// <summary>A fund with its current book balance.</summary>
/// <param name="Id">The fund id.</param>
/// <param name="Code">The fund code.</param>
/// <param name="Name">The fund name.</param>
/// <param name="AccountCode">The petty cash ledger account.</param>
/// <param name="FloatAmount">The float amount.</param>
/// <param name="Balance">The ledger balance today.</param>
/// <param name="Custodian">The custodian's username, when named.</param>
/// <param name="IsActive">Whether the fund is in use.</param>
/// <param name="LastCountDate">The date of the latest count, when any.</param>
public sealed record PettyCashFundRow(long Id, string Code, string Name, string AccountCode, decimal FloatAmount, decimal Balance, string? Custodian, bool IsActive, DateOnly? LastCountDate);

/// <summary>A disbursement from a fund.</summary>
/// <param name="FundCode">The fund paid from.</param>
/// <param name="Amount">The amount paid out (gross, including any tax).</param>
/// <param name="Date">The posting date.</param>
/// <param name="ExpenseAccountCode">The expense account.</param>
/// <param name="DocumentNumber">The voucher/document number.</param>
/// <param name="TaxCode">The tax code when the receipt carries input tax; the recoverable share is claimed to the input tax account.</param>
/// <param name="Payee">Who was paid.</param>
/// <param name="Narrative">What for.</param>
/// <param name="CostCentre">The cost centre, when any.</param>
/// <param name="Department">The department, when any.</param>
public sealed record PettyCashDisbursement(string FundCode, decimal Amount, DateOnly Date, string ExpenseAccountCode, string DocumentNumber, string? TaxCode = null, string? Payee = null,
    string? Narrative = null, string? CostCentre = null, string? Department = null);

/// <summary>What a disbursement posted.</summary>
/// <param name="JournalNumber">The journal number.</param>
/// <param name="Net">The amount charged to the expense account.</param>
/// <param name="InputTax">The input tax claimed.</param>
/// <param name="FundBalanceAfter">The fund's book balance after the payment.</param>
public readonly record struct PettyCashDisbursementResult(string JournalNumber, decimal Net, decimal InputTax, decimal FundBalanceAfter);

/// <summary>A physical count of a fund.</summary>
/// <param name="FundCode">The fund counted.</param>
/// <param name="Date">The date of the count.</param>
/// <param name="CountedAmount">The cash counted.</param>
/// <param name="CountedByUserId">Who counted.</param>
/// <param name="Denominations">The denomination breakdown as entered (free text, e.g. "100x2, 20x5").</param>
/// <param name="Note">A note.</param>
public sealed record PettyCashCountInput(string FundCode, DateOnly Date, decimal CountedAmount, long CountedByUserId, string? Denominations = null, string? Note = null);

/// <summary>A count against the book.</summary>
/// <param name="CountId">The count id.</param>
/// <param name="BookBalance">The ledger balance at the date.</param>
/// <param name="CountedAmount">The cash counted.</param>
/// <param name="Variance">Counted minus book (positive is an overage).</param>
public readonly record struct PettyCashCountResult(long CountId, decimal BookBalance, decimal CountedAmount, decimal Variance);

/// <summary>What a reconciliation posted.</summary>
/// <param name="Variance">The variance written off (positive overage, negative shortage).</param>
/// <param name="VarianceJournalNumber">The variance journal, when there was a variance.</param>
/// <param name="ReimbursedAmount">The amount drawn from the bank to restore the float.</param>
/// <param name="ReimbursementJournalNumber">The reimbursement journal, when the float was short.</param>
/// <param name="FloatBalanceAfter">The fund's book balance afterwards.</param>
public readonly record struct PettyCashReconciliation(decimal Variance, string? VarianceJournalNumber, decimal ReimbursedAmount, string? ReimbursementJournalNumber, decimal FloatBalanceAfter);

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

    /// <summary>Creates a fund or changes an existing one (matched by code); the float and accounts may change at any time.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="input">The fund.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The fund, or an error.</returns>
    Task<Result<PettyCashFund>> SaveFundAsync(long companyId, PettyCashFundInput input, CancellationToken ct = default);

    /// <summary>Lists the funds with their book balances today.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The funds by code.</returns>
    Task<IReadOnlyList<PettyCashFundRow>> ListFundsAsync(long companyId, CancellationToken ct = default);

    /// <summary>
    /// Pays an expense from a fund: Dr the expense (net of any recoverable input tax), Dr the input tax account for
    /// the recoverable share of the tax on the receipt, Cr the fund's account. The payment is refused when it would
    /// take the fund below zero.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="request">The disbursement.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The posting, or an error.</returns>
    Task<Result<PettyCashDisbursementResult>> DisburseFromFundAsync(long companyId, PettyCashDisbursement request, CancellationToken ct = default);

    /// <summary>Reimburses a fund back to its float from its bank account (a full float is a no-op).</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="fundCode">The fund.</param>
    /// <param name="date">The posting date.</param>
    /// <param name="documentNumber">The reimbursement document number.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The imprest result, or an error.</returns>
    Task<Result<ImprestResult>> ReimburseFundAsync(long companyId, string fundCode, DateOnly date, string documentNumber, CancellationToken ct = default);

    /// <summary>Records a physical count of a fund against its book balance at the date.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="input">The count.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The count with its variance, or an error.</returns>
    Task<Result<PettyCashCountResult>> CountAsync(long companyId, PettyCashCountInput input, CancellationToken ct = default);

    /// <summary>
    /// Reconciles a count: a shortage is written off (Dr over/short, Cr fund) and an overage taken in (Dr fund, Cr
    /// over/short) so the book agrees with the cash, then the float is reimbursed from the bank.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="countId">The count.</param>
    /// <param name="date">The posting date.</param>
    /// <param name="documentNumber">The document number for the postings.</param>
    /// <param name="userId">Who reconciles.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>What was posted, or an error.</returns>
    Task<Result<PettyCashReconciliation>> ReconcileAsync(long companyId, long countId, DateOnly date, string documentNumber, long userId, CancellationToken ct = default);

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
