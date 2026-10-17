using One4All.Core.Accounting;
using One4All.Core.Common;
using One4All.Core.Trading;

namespace One4All.Application.Abstractions.Accounting;

/// <summary>A certificate close to (or past) its expiry.</summary>
/// <param name="CertificateId">The certificate.</param>
/// <param name="Ledger">Customer or supplier.</param>
/// <param name="TraderCode">The trader's code.</param>
/// <param name="TraderName">The trader's name.</param>
/// <param name="CertificateNumber">Its number.</param>
/// <param name="ValidTo">Its last day.</param>
/// <param name="DaysLeft">Days left (negative: expired).</param>
public sealed record ExpiringCertificate(long CertificateId, TradingLedger Ledger, string TraderCode, string TraderName, string CertificateNumber, DateOnly ValidTo, int DaysLeft,
    CertificatePurpose Purpose = CertificatePurpose.TaxExemption);

/// <summary>A VAT return worked out on the company's tax point basis.</summary>
/// <param name="Basis">The basis.</param>
/// <param name="FromDate">First day.</param>
/// <param name="ToDate">Last day.</param>
/// <param name="LedgerOutput">Output tax posted in the period.</param>
/// <param name="LedgerInput">Input tax posted in the period.</param>
/// <param name="OutputTax">Output tax due on the basis.</param>
/// <param name="InputTax">Input tax claimable on the basis.</param>
/// <param name="Adjustments">What moved the figures off the ledger (document, reason, output, input).</param>
public sealed record BasisVatReturn(TaxPointBasis Basis, DateOnly FromDate, DateOnly ToDate, decimal LedgerOutput, decimal LedgerInput, decimal OutputTax, decimal InputTax,
    IReadOnlyList<(string Document, string Reason, decimal Output, decimal Input)> Adjustments)
{
    /// <summary>Gets the tax payable (negative: refundable).</summary>
    public decimal NetPayable => OutputTax - InputTax;
}

/// <summary>
/// Tax compliance (brief §3.5): exemption certificates for customers and suppliers with expiry warnings, and the VAT return on the company's
/// tax point basis - invoice date (the ledger), tax point (documents moved to their tax point), or cash accounting (invoice VAT due as it is paid).
/// </summary>
public interface ITaxComplianceService
{
    /// <summary>Records an exemption certificate.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="ledger">Customer or supplier.</param>
    /// <param name="traderCode">The trader's code.</param>
    /// <param name="certificateNumber">Its number.</param>
    /// <param name="validFrom">First day.</param>
    /// <param name="validTo">Last day.</param>
    /// <param name="issuedBy">Who issued it.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The certificate id, or why not.</returns>
    Task<Result<long>> RecordCertificateAsync(long companyId, TradingLedger ledger, string traderCode, string certificateNumber, DateOnly validFrom, DateOnly validTo, string? issuedBy,
        CancellationToken ct = default) => RecordCertificateAsync(companyId, ledger, traderCode, certificateNumber, validFrom, validTo, issuedBy, CertificatePurpose.TaxExemption, ct);

    /// <summary>Records a certificate for a purpose: an exemption (customer or supplier) or a supplier's tax clearance.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="ledger">Customer or supplier.</param>
    /// <param name="traderCode">The trader's code.</param>
    /// <param name="certificateNumber">The certificate's number.</param>
    /// <param name="validFrom">Valid from.</param>
    /// <param name="validTo">Valid to.</param>
    /// <param name="issuedBy">Who issued it.</param>
    /// <param name="purpose">What it is for (a tax clearance is for suppliers only).</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The certificate id.</returns>
    Task<Result<long>> RecordCertificateAsync(long companyId, TradingLedger ledger, string traderCode, string certificateNumber, DateOnly validFrom, DateOnly validTo, string? issuedBy,
        CertificatePurpose purpose, CancellationToken ct = default);

    /// <summary>Withdraws a certificate from a day.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="certificateId">The certificate.</param>
    /// <param name="date">The day it stops.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success or why not.</returns>
    Task<Result> RevokeCertificateAsync(long companyId, long certificateId, DateOnly date, CancellationToken ct = default);

    /// <summary>Certificates expiring within some days of a date (and those expired in the last 30 days), soonest first.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="asAt">The date.</param>
    /// <param name="withinDays">The window.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The certificates.</returns>
    Task<IReadOnlyList<ExpiringCertificate>> ExpiringAsync(long companyId, DateOnly asAt, int withinDays = 30, CancellationToken ct = default);

    /// <summary>The VAT return for a period on the company's basis (or a given one).</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="fromDate">First day.</param>
    /// <param name="toDate">Last day.</param>
    /// <param name="basis">The basis (null: the company's).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The return.</returns>
    Task<BasisVatReturn> VatReturnAsync(long companyId, DateOnly fromDate, DateOnly toDate, TaxPointBasis? basis = null, CancellationToken ct = default);
}
