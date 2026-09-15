using One4All.Core.Common;
using One4All.Core.Trading;

namespace One4All.Application.Abstractions.Trading;

/// <summary>The result of an allocation.</summary>
/// <param name="AllocationId">The created allocation id.</param>
/// <param name="TransactionAmount">The amount matched in transaction currency.</param>
/// <param name="RealisedFxBase">The realised FX gain (positive) or loss (negative) in base currency.</param>
/// <param name="FxJournalNumber">The FX journal number, when a realised gain/loss was posted.</param>
public readonly record struct AllocationResult(long AllocationId, decimal TransactionAmount, decimal RealisedFxBase, string? FxJournalNumber);

/// <summary>The Sales Ledger (AR) posting service (brief §4.5). All postings go through the central engine.</summary>
public interface ISalesLedgerService
{
    /// <summary>Posts a sales invoice: Dr AR control, Cr revenue, Cr output tax; creates an AR open item.</summary>
    /// <param name="request">The invoice request.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The posting result.</returns>
    Task<Result<TradingPostingResult>> PostInvoiceAsync(SalesInvoiceRequest request, CancellationToken ct = default);

    /// <summary>Posts a receipt: Dr bank, Cr AR control; creates an AR (credit) open item.</summary>
    /// <param name="request">The receipt request.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The posting result.</returns>
    Task<Result<TradingPostingResult>> PostReceiptAsync(ReceiptRequest request, CancellationToken ct = default);
}

/// <summary>The Purchase Ledger (AP) posting service (brief §4.6).</summary>
public interface IPurchaseLedgerService
{
    /// <summary>Posts a supplier invoice: Dr expense/stock, Dr input tax, Cr AP control; creates an AP open item.</summary>
    /// <param name="request">The invoice request.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The posting result.</returns>
    Task<Result<TradingPostingResult>> PostInvoiceAsync(PurchaseInvoiceRequest request, CancellationToken ct = default);

    /// <summary>Posts a payment: Dr AP control, Cr bank; creates an AP (debit) open item.</summary>
    /// <param name="request">The payment request.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The posting result.</returns>
    Task<Result<TradingPostingResult>> PostPaymentAsync(PaymentRequest request, CancellationToken ct = default);
}

/// <summary>Matches credit items against debit items and realises FX (brief §4.5 allocation/matching).</summary>
public interface IAllocationService
{
    /// <summary>
    /// Allocates a transaction-currency amount of a credit item against a debit item, reducing both
    /// outstanding balances and posting any realised FX gain/loss to the FX control accounts.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="ledger">The sub-ledger.</param>
    /// <param name="debitItemId">The debit item (invoice) id.</param>
    /// <param name="creditItemId">The credit item (receipt/payment/credit note) id.</param>
    /// <param name="transactionAmount">The amount to match in transaction currency.</param>
    /// <param name="allocationDate">The allocation (accounting) date.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The allocation result.</returns>
    Task<Result<AllocationResult>> AllocateAsync(long companyId, TradingLedger ledger, long debitItemId, long creditItemId, decimal transactionAmount, DateOnly allocationDate, CancellationToken ct = default);
}
