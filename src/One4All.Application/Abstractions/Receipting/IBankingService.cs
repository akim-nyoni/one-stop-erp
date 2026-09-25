using One4All.Core.Common;

namespace One4All.Application.Abstractions.Receipting;

/// <summary>The result of moving takings into cash-in-transit.</summary>
/// <param name="DepositId">The created deposit id.</param>
/// <param name="Reference">The deposit-slip reference.</param>
/// <param name="JournalNumber">The GL journal number.</param>
public readonly record struct BankingResult(long DepositId, string Reference, string JournalNumber);

/// <summary>
/// Banking &amp; treasury (brief §RCP-10). Moves counted takings from the till/safe into a cash-in-transit
/// control account (a deposit slip), then clears cash-in-transit to the bank when the statement confirms.
/// Cash-in-transit is its own control account that must clear; both movements post through the engine.
/// </summary>
public interface IBankingService
{
    /// <summary>
    /// Moves takings into cash-in-transit (raising a deposit slip): Dr cash-in-transit, Cr cash on hand.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="amount">The amount banked.</param>
    /// <param name="date">The deposit date.</param>
    /// <param name="reference">The deposit-slip reference.</param>
    /// <param name="cashAccountCode">The cash-on-hand / safe account (credited).</param>
    /// <param name="cashInTransitAccountCode">The cash-in-transit account (debited).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The banking result.</returns>
    Task<Result<BankingResult>> BankCashAsync(long companyId, decimal amount, DateOnly date, string reference, string cashAccountCode, string cashInTransitAccountCode, CancellationToken ct = default);

    /// <summary>
    /// Confirms a deposit on the bank statement, clearing cash-in-transit to the bank: Dr bank, Cr
    /// cash-in-transit. The deposit becomes immutable.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="depositId">The deposit.</param>
    /// <param name="date">The confirmation date.</param>
    /// <param name="cashInTransitAccountCode">The cash-in-transit account (credited).</param>
    /// <param name="bankAccountCode">The bank account (debited).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The GL journal number.</returns>
    Task<Result<string>> ConfirmBankedAsync(long companyId, long depositId, DateOnly date, string cashInTransitAccountCode, string bankAccountCode, CancellationToken ct = default);

    /// <summary>
    /// Transfers funds between two of the company's own cash/bank accounts (e.g. bank to petty cash): Dr the
    /// destination account, Cr the source account. Refuses a non-positive amount or the same account both sides.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="amount">The amount transferred (positive).</param>
    /// <param name="date">The transfer date.</param>
    /// <param name="reference">The transfer reference.</param>
    /// <param name="fromAccountCode">The source cash/bank account (credited).</param>
    /// <param name="toAccountCode">The destination cash/bank account (debited).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The GL journal number.</returns>
    Task<Result<string>> TransferAsync(long companyId, decimal amount, DateOnly date, string reference, string fromAccountCode, string toAccountCode, CancellationToken ct = default);
}
