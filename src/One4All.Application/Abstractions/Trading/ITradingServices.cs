using One4All.Core.Common;
using One4All.Core.Trading;

namespace One4All.Application.Abstractions.Trading;

/// <summary>The result of an allocation.</summary>
/// <param name="AllocationId">The created allocation id.</param>
/// <param name="TransactionAmount">The amount matched in transaction currency.</param>
/// <param name="RealisedFxBase">The realised FX gain (positive) or loss (negative) in base currency.</param>
/// <param name="FxJournalNumber">The FX journal number, when a realised gain/loss was posted.</param>
public readonly record struct AllocationResult(long AllocationId, decimal TransactionAmount, decimal RealisedFxBase, string? FxJournalNumber);

/// <summary>The result of reversing an allocation (un-allocation).</summary>
/// <param name="AllocationId">The reversed allocation id.</param>
/// <param name="RestoredTransaction">The transaction-currency amount restored to each item.</param>
/// <param name="ReversedFxBase">The realised FX that was reversed (opposite sign to the original), in base currency.</param>
/// <param name="FxReversalJournalNumber">The FX reversal journal number, when the original allocation realised FX.</param>
public readonly record struct UnallocationResult(long AllocationId, decimal RestoredTransaction, decimal ReversedFxBase, string? FxReversalJournalNumber);

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

    /// <summary>Posts a credit note: Dr revenue, Dr output tax, Cr AR control; creates an AR (credit) open item.</summary>
    /// <param name="request">The credit-note request.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The posting result.</returns>
    Task<Result<TradingPostingResult>> PostCreditNoteAsync(SalesCreditNoteRequest request, CancellationToken ct = default);
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

    /// <summary>Posts a supplier credit note: Dr AP control, Cr expense/stock, Cr input tax; creates an AP (debit) open item.</summary>
    /// <param name="request">The credit-note request.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The posting result.</returns>
    Task<Result<TradingPostingResult>> PostCreditNoteAsync(PurchaseCreditNoteRequest request, CancellationToken ct = default);
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

    /// <summary>
    /// Reverses an allocation (un-allocation, brief §RCP-7): restores the outstanding on both items to their
    /// pre-allocation state, reverses any realised FX gain/loss the allocation posted, and marks the
    /// allocation reversed (retained, never deleted). The foundation for receipt/payment reversal.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="allocationId">The allocation to reverse.</param>
    /// <param name="reversalDate">The reversal (accounting) date.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The un-allocation result.</returns>
    Task<Result<UnallocationResult>> UnallocateAsync(long companyId, long allocationId, DateOnly reversalDate, CancellationToken ct = default);
}
