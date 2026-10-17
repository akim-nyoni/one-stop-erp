using One4All.Core.Common;

namespace One4All.Application.Abstractions.Accounting;

/// <summary>A request to fiscalise a receipt/invoice with a country's fiscal device or e-invoicing API.</summary>
/// <param name="CompanyId">The company.</param>
/// <param name="DocumentNumber">The source document number.</param>
/// <param name="TotalGross">The gross total.</param>
/// <param name="TotalTax">The tax total.</param>
/// <param name="Currency">The currency code.</param>
/// <param name="IssuedAtUtc">When the document was issued.</param>
/// <summary>What kind of document is being signed.</summary>
public enum FiscalDocumentKind
{
    /// <summary>A till sale receipt.</summary>
    Sale = 0,

    /// <summary>A refund, void or return receipt (references the receipt it reverses).</summary>
    Refund = 1,

    /// <summary>A sales ledger tax invoice (e-invoicing).</summary>
    Invoice = 2,

    /// <summary>A sales ledger credit note (references the invoice it credits when known).</summary>
    CreditNote = 3,
}

/// <summary>A document to sign: a sale, a refund (with the receipt it reverses), a tax invoice or a credit note.</summary>
/// <param name="CompanyId">The company.</param>
/// <param name="DocumentNumber">The receipt, invoice or credit note number.</param>
/// <param name="TotalGross">The total including tax.</param>
/// <param name="TotalTax">The tax.</param>
/// <param name="Currency">The currency.</param>
/// <param name="IssuedAtUtc">When issued.</param>
/// <param name="Kind">What kind of document.</param>
/// <param name="ReferenceDocumentNumber">The document this one reverses or credits, when any.</param>
/// <param name="CustomerTaxNumber">The buyer's tax number (an invoice or credit note).</param>
public readonly record struct FiscalReceiptRequest(long CompanyId, string DocumentNumber, decimal TotalGross, decimal TotalTax, string Currency, DateTimeOffset IssuedAtUtc,
    FiscalDocumentKind Kind = FiscalDocumentKind.Sale, string? ReferenceDocumentNumber = null, string? CustomerTaxNumber = null);

/// <summary>The day's totals submitted with the fiscal day close (the Z report).</summary>
/// <param name="CompanyId">The company.</param>
/// <param name="Date">The trading date.</param>
/// <param name="SaleCount">Receipts signed.</param>
/// <param name="SalesGross">Sales including tax.</param>
/// <param name="SalesTax">Tax on sales.</param>
/// <param name="RefundCount">Refund receipts.</param>
/// <param name="RefundsGross">Refunds including tax.</param>
/// <param name="VoidCount">Receipts voided.</param>
/// <param name="VoidsGross">Voided receipts' value.</param>
/// <param name="Currency">The currency.</param>
public readonly record struct FiscalDayReport(long CompanyId, DateOnly Date, int SaleCount, decimal SalesGross, decimal SalesTax, int RefundCount, decimal RefundsGross, int VoidCount, decimal VoidsGross,
    string Currency);

/// <summary>The signed result returned by a fiscal device.</summary>
/// <param name="FiscalCode">The device/receipt fiscal code.</param>
/// <param name="SignatureOrQr">The receipt signature or QR/verification payload to print.</param>
/// <param name="FiscalDayNumber">The fiscal day number, when the device tracks one.</param>
/// <param name="Queued">True when the device was offline and the receipt was queued for later submission.</param>
public readonly record struct FiscalReceiptResult(string? FiscalCode, string? SignatureOrQr, int? FiscalDayNumber, bool Queued);

/// <summary>
/// Country-specific fiscalisation hook (brief §3.5): fiscal day open/close, receipt signing, QR/verification
/// codes, Z-report submission and an offline queue with retry. A reference country implementation plugs in
/// here; the shipped default is <c>NullFiscalDeviceProvider</c>, which is a no-op.
/// </summary>
public interface IFiscalDeviceProvider
{
    /// <summary>True when a real fiscal device is configured (false for the null provider).</summary>
    bool IsEnabled { get; }

    /// <summary>Opens the fiscal day.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The opened fiscal day number, or an error.</returns>
    Task<Result<int>> OpenDayAsync(long companyId, CancellationToken ct = default);

    /// <summary>Signs/fiscalises a receipt.</summary>
    /// <param name="request">The receipt request.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The signed result, or an error.</returns>
    Task<Result<FiscalReceiptResult>> FiscaliseAsync(FiscalReceiptRequest request, CancellationToken ct = default);

    /// <summary>Closes the fiscal day and submits the Z-report.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success or failure.</returns>
    Task<Result> CloseDayAsync(long companyId, CancellationToken ct = default);

    /// <summary>Submits the day's totals (the Z report) before the day is closed; a device that has no such call accepts it.</summary>
    /// <param name="report">The report.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or why not.</returns>
    Task<Result> SubmitDayReportAsync(FiscalDayReport report, CancellationToken ct = default) => Task.FromResult(Result.Success());
}
