using One4All.Core.Common;

namespace One4All.Application.Abstractions.Accounting;

/// <summary>A request to fiscalise a receipt/invoice with a country's fiscal device or e-invoicing API.</summary>
/// <param name="CompanyId">The company.</param>
/// <param name="DocumentNumber">The source document number.</param>
/// <param name="TotalGross">The gross total.</param>
/// <param name="TotalTax">The tax total.</param>
/// <param name="Currency">The currency code.</param>
/// <param name="IssuedAtUtc">When the document was issued.</param>
public readonly record struct FiscalReceiptRequest(long CompanyId, string DocumentNumber, decimal TotalGross, decimal TotalTax, string Currency, DateTimeOffset IssuedAtUtc);

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
}
