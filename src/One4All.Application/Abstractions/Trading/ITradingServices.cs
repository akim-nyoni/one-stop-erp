using One4All.Core.Accounting;
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

/// <summary>A bad-debt write-off that reclaimed the output VAT on the debt.</summary>
/// <param name="WrittenOffToBadDebt">The net amount charged to bad debt (transaction currency).</param>
/// <param name="VatRelief">The output VAT reclaimed (transaction currency).</param>
/// <param name="BadDebtOpenItemId">The write-off open item allocated to the invoice.</param>
/// <param name="VatReliefOpenItemId">The VAT relief open item allocated to the invoice, or null when there was none.</param>
public sealed record VatReliefWriteOff(decimal WrittenOffToBadDebt, decimal VatRelief, long BadDebtOpenItemId, long? VatReliefOpenItemId);

/// <summary>The Sales Ledger (AR) posting service (brief §4.5). All postings go through the central engine.</summary>
public interface ISalesLedgerService
{
    /// <summary>Posts a sales invoice: Dr AR control, Cr revenue, Cr output tax; creates an AR open item.</summary>
    /// <param name="request">The invoice request.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The posting result.</returns>
    Task<Result<TradingPostingResult>> PostInvoiceAsync(SalesInvoiceRequest request, CancellationToken ct = default);

    /// <summary>
    /// Posts a debit note to a customer (brief §4.4): a further charge against an invoice already raised — an undercharge, a price adjustment,
    /// a re-delivery — posted like an invoice and kept as a debit-note open item referring to the original.
    /// </summary>
    /// <param name="request">The charge (its document number is the debit note's).</param>
    /// <param name="originalInvoiceNumber">The invoice it adds to.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The posting, or the original invoice is not the customer's.</returns>
    Task<Result<TradingPostingResult>> PostDebitNoteAsync(SalesInvoiceRequest request, string originalInvoiceNumber, CancellationToken ct = default);

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

    /// <summary>
    /// Posts a receipt reversal: Dr AR control, Cr bank/cash — the exact opposite of a receipt — and creates
    /// an AR (debit) adjustment open item that restores the debtor balance (brief §RCP-7). Used to reverse a
    /// receipt after its allocations have been un-allocated.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="customerId">The customer.</param>
    /// <param name="documentNumber">The reversal document number.</param>
    /// <param name="date">The reversal date.</param>
    /// <param name="currency">The transaction currency.</param>
    /// <param name="amount">The amount to reverse.</param>
    /// <param name="bankAccountCode">The bank/cash account credited (the money goes back out).</param>
    /// <param name="exchangeRate">Optional explicit exchange rate.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The posting result (the debit adjustment open item).</returns>
    Task<Result<TradingPostingResult>> PostReceiptReversalAsync(long companyId, long customerId, string documentNumber, DateOnly date, CurrencyCode currency, decimal amount, string bankAccountCode, decimal? exchangeRate = null, CancellationToken ct = default);

    /// <summary>
    /// Writes off an uncollectable customer open item as a bad debt: posts the remaining outstanding to a
    /// bad-debt-expense account (Dr bad debt, Cr AR control) and allocates it against the item so it is settled
    /// and no longer ages. Requires a reason; refuses a fully-settled item.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="openItemId">The AR open item (invoice) to write off.</param>
    /// <param name="date">The write-off date.</param>
    /// <param name="badDebtAccountCode">The bad-debt-expense account debited.</param>
    /// <param name="reason">The mandatory reason.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The posting result (the write-off credit open item).</returns>
    Task<Result<TradingPostingResult>> WriteOffAsync(long companyId, long openItemId, DateOnly date, string badDebtAccountCode, string reason, CancellationToken ct = default);

    /// <summary>
    /// Writes off a bad debt with VAT relief (brief §4.5): the output VAT included in what is still owed is reclaimed
    /// (Dr the output VAT account the invoice credited) and only the net goes to bad debt, so the next VAT return pays
    /// back the tax never collected. The share of VAT follows the invoice's own tax lines. Relief is refused before the
    /// debt has been overdue for the qualifying number of days.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="openItemId">The AR invoice to write off.</param>
    /// <param name="date">The write-off date.</param>
    /// <param name="badDebtAccountCode">The bad-debt-expense account debited with the net.</param>
    /// <param name="reason">The mandatory reason.</param>
    /// <param name="minimumDaysOverdue">How long the debt must have been overdue to qualify for relief.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The split between bad debt and VAT relief, or why it was refused.</returns>
    Task<Result<VatReliefWriteOff>> WriteOffWithVatReliefAsync(long companyId, long openItemId, DateOnly date, string badDebtAccountCode, string reason, int minimumDaysOverdue, CancellationToken ct = default);

    /// <summary>
    /// Settles a customer invoice where the customer pays early and takes a settlement (early-payment) discount:
    /// posts the cash receipt (Dr bank / Cr AR) and the discount (Dr discounts-allowed / Cr AR), then allocates
    /// both against the invoice so it fully settles. Refuses non-positive amounts, or a cash+discount total that
    /// exceeds the invoice's outstanding balance.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="invoiceOpenItemId">The AR invoice being settled.</param>
    /// <param name="date">The settlement date.</param>
    /// <param name="cashAmount">The cash received (positive).</param>
    /// <param name="discountAmount">The settlement discount granted (positive).</param>
    /// <param name="bankAccountCode">The bank/cash account the receipt is banked to (debited).</param>
    /// <param name="discountAccountCode">The discounts-allowed account (debited with the discount).</param>
    /// <param name="documentNumber">The receipt document number.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The posting result of the cash receipt.</returns>
    Task<Result<TradingPostingResult>> SettleWithDiscountAsync(long companyId, long invoiceOpenItemId, DateOnly date, decimal cashAmount, decimal discountAmount, string bankAccountCode, string discountAccountCode, string documentNumber, CancellationToken ct = default);
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

    /// <summary>
    /// Writes back an outstanding creditor balance the business will not pay (e.g. a stale supplier invoice
    /// or a trivial residual): posts Dr AP control / Cr the write-back income account for the item's
    /// outstanding amount and allocates it against the invoice so it settles and stops appearing. Refuses a
    /// missing reason/account, or an item that is not an outstanding creditor balance.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="openItemId">The AP open item (a creditor invoice) to write back.</param>
    /// <param name="date">The write-back date.</param>
    /// <param name="writeBackAccountCode">The income account credited with the released liability.</param>
    /// <param name="reason">The reason for the write-back (required for the audit trail).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The posting result of the write-back.</returns>
    Task<Result<TradingPostingResult>> WriteBackAsync(long companyId, long openItemId, DateOnly date, string writeBackAccountCode, string reason, CancellationToken ct = default);

    /// <summary>
    /// Pays a supplier invoice early and takes a settlement discount: posts the cash payment (Dr AP / Cr bank)
    /// and the discount received (Dr AP / Cr discounts-received income), then allocates both against the invoice
    /// so it fully settles. Refuses non-positive amounts, or a cash+discount total that exceeds the invoice's
    /// outstanding balance.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="invoiceOpenItemId">The AP invoice being settled.</param>
    /// <param name="date">The settlement date.</param>
    /// <param name="cashAmount">The cash paid (positive).</param>
    /// <param name="discountAmount">The settlement discount taken (positive).</param>
    /// <param name="bankAccountCode">The bank/cash account the payment is drawn from (credited).</param>
    /// <param name="discountReceivedAccountCode">The discounts-received income account (credited with the discount).</param>
    /// <param name="documentNumber">The payment document number.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The posting result of the cash payment.</returns>
    Task<Result<TradingPostingResult>> PayWithDiscountAsync(long companyId, long invoiceOpenItemId, DateOnly date, decimal cashAmount, decimal discountAmount, string bankAccountCode, string discountReceivedAccountCode, string documentNumber, CancellationToken ct = default);
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

    /// <summary>
    /// Offsets a customer's outstanding invoice against a supplier's outstanding invoice for the same trading
    /// partner (a contra settlement): raises an AR credit and an AP debit for the amount against a contra
    /// clearing account (which nets to zero) and allocates each against the named items. Refuses an amount
    /// greater than either item's outstanding balance.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="customerId">The customer whose debtor balance is reduced.</param>
    /// <param name="customerInvoiceItemId">The customer AR invoice open item to settle.</param>
    /// <param name="supplierId">The supplier whose creditor balance is reduced.</param>
    /// <param name="supplierInvoiceItemId">The supplier AP invoice open item to settle.</param>
    /// <param name="amount">The contra amount (base currency; both items must be base-currency).</param>
    /// <param name="date">The contra (accounting) date.</param>
    /// <param name="contraClearingAccountCode">The clearing account both legs pass through (nets to zero).</param>
    /// <param name="documentNumber">The contra document number.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success or an error.</returns>
    Task<Result> ContraAsync(long companyId, long customerId, long customerInvoiceItemId, long supplierId, long supplierInvoiceItemId, decimal amount, DateOnly date, string contraClearingAccountCode, string documentNumber, CancellationToken ct = default);
}
