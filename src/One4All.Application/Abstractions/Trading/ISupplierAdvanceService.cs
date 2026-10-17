using One4All.Core.Accounting;
using One4All.Core.Common;
using One4All.Core.Trading;

namespace One4All.Application.Abstractions.Trading;

/// <summary>An advance to a supplier.</summary>
/// <param name="CompanyId">The company.</param>
/// <param name="SupplierId">The supplier paid.</param>
/// <param name="DocumentNumber">The payment document number.</param>
/// <param name="Date">The payment date.</param>
/// <param name="Currency">The currency paid.</param>
/// <param name="Amount">The amount paid.</param>
/// <param name="AdvanceAccountCode">The advances (prepayment) asset account that holds it.</param>
/// <param name="BankAccountCode">The bank account it is paid from.</param>
/// <param name="PurchaseOrderNumber">The purchase order it is for, when any.</param>
/// <param name="Narrative">What for.</param>
/// <param name="ExchangeRate">A keyed rate to base currency; blank takes the table's daily rate.</param>
public sealed record SupplierAdvanceRequest(long CompanyId, long SupplierId, string DocumentNumber, DateOnly Date, CurrencyCode Currency, decimal Amount, string AdvanceAccountCode, string BankAccountCode,
    string? PurchaseOrderNumber = null, string? Narrative = null, decimal? ExchangeRate = null);

/// <summary>What paying an advance posted.</summary>
/// <param name="AdvanceId">The advance.</param>
/// <param name="JournalNumber">The payment journal.</param>
/// <param name="AmountBase">The amount in base currency.</param>
public readonly record struct SupplierAdvanceResult(long AdvanceId, string JournalNumber, decimal AmountBase);

/// <summary>What applying an advance to an invoice posted.</summary>
/// <param name="Applied">The amount applied, in the advance's currency.</param>
/// <param name="PaymentOpenItemId">The purchase-ledger payment item the application raised and allocated to the invoice.</param>
/// <param name="JournalNumber">The transfer journal (advances account to the purchase ledger control).</param>
/// <param name="AdvanceOutstanding">What is left of the advance.</param>
/// <param name="InvoiceOutstanding">What is left of the invoice.</param>
public readonly record struct SupplierAdvanceApplication(decimal Applied, long PaymentOpenItemId, string JournalNumber, decimal AdvanceOutstanding, decimal InvoiceOutstanding);

/// <summary>An advance as listed.</summary>
/// <param name="Id">The advance.</param>
/// <param name="SupplierId">The supplier.</param>
/// <param name="SupplierCode">The supplier's code.</param>
/// <param name="DocumentNumber">The payment document number.</param>
/// <param name="Date">The payment date.</param>
/// <param name="Currency">The currency.</param>
/// <param name="Amount">The amount paid.</param>
/// <param name="Applied">The amount applied to invoices.</param>
/// <param name="Refunded">The amount refunded.</param>
/// <param name="Outstanding">The balance still held.</param>
/// <param name="Status">The state.</param>
/// <param name="PurchaseOrderNumber">The purchase order, when any.</param>
public sealed record SupplierAdvanceRow(long Id, long SupplierId, string SupplierCode, string DocumentNumber, DateOnly Date, string Currency, decimal Amount, decimal Applied, decimal Refunded, decimal Outstanding,
    SupplierAdvanceStatus Status, string? PurchaseOrderNumber);

/// <summary>
/// Advances and prepayments to suppliers (brief §4.4): paid into an advances asset account ahead of the invoice,
/// applied to the invoice when it arrives (moved into the purchase ledger as a payment and allocated), or refunded.
/// </summary>
public interface ISupplierAdvanceService
{
    /// <summary>Pays an advance: Dr the advances account, Cr the bank. A supplier on hold or blacklisted is refused.</summary>
    /// <param name="request">The advance.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The posting, or an error.</returns>
    Task<Result<SupplierAdvanceResult>> PayAsync(SupplierAdvanceRequest request, CancellationToken ct = default);

    /// <summary>
    /// Applies an advance to one of the supplier's invoices: Dr the purchase ledger control, Cr the advances account for the
    /// amount, raising a purchase-ledger payment item that is allocated to the invoice. The amount defaults to the smaller of the
    /// advance's balance and the invoice's; the invoice must be in the advance's currency.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="advanceId">The advance.</param>
    /// <param name="invoiceOpenItemId">The invoice open item.</param>
    /// <param name="date">The posting date.</param>
    /// <param name="amount">The amount to apply, or null for as much as possible.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The application, or an error.</returns>
    Task<Result<SupplierAdvanceApplication>> ApplyAsync(long companyId, long advanceId, long invoiceOpenItemId, DateOnly date, decimal? amount = null, CancellationToken ct = default);

    /// <summary>Records the supplier's refund of the unapplied balance (or part of it): Dr the bank, Cr the advances account.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="advanceId">The advance.</param>
    /// <param name="date">The refund date.</param>
    /// <param name="bankAccountCode">The bank account the refund came into.</param>
    /// <param name="documentNumber">The refund document number.</param>
    /// <param name="amount">The amount refunded, or null for the whole balance.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The refund journal number, or an error.</returns>
    Task<Result<string>> RefundAsync(long companyId, long advanceId, DateOnly date, string bankAccountCode, string documentNumber, decimal? amount = null, CancellationToken ct = default);

    /// <summary>Lists advances, newest first, for one supplier or all.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="supplierId">The supplier, or null for all.</param>
    /// <param name="openOnly">Whether to list only advances with a balance.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The advances.</returns>
    Task<IReadOnlyList<SupplierAdvanceRow>> ListAsync(long companyId, long? supplierId = null, bool openOnly = false, CancellationToken ct = default);
}
