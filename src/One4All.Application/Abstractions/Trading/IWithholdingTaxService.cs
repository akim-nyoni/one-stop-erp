using One4All.Core.Common;

namespace One4All.Application.Abstractions.Trading;

/// <summary>One certificate on a withholding tax return.</summary>
/// <param name="CertificateId">The certificate.</param>
/// <param name="CertificateNumber">Its number.</param>
/// <param name="SupplierCode">The supplier's code.</param>
/// <param name="SupplierName">The supplier's name.</param>
/// <param name="PaymentDate">The payment date.</param>
/// <param name="TaxCode">The withholding tax code.</param>
/// <param name="Gross">The gross payment (base currency).</param>
/// <param name="Withheld">The tax withheld (base currency).</param>
/// <param name="RemittedOn">When it was paid over, if it has been.</param>
public readonly record struct WithholdingReturnLine(long CertificateId, string CertificateNumber, string SupplierCode, string SupplierName, DateOnly PaymentDate, string TaxCode, decimal Gross, decimal Withheld, DateOnly? RemittedOn);

/// <summary>The withholding tax return for a period.</summary>
/// <param name="FromDate">The first payment date covered.</param>
/// <param name="ToDate">The last payment date covered.</param>
/// <param name="Lines">The certificates.</param>
public sealed record WithholdingReturn(DateOnly FromDate, DateOnly ToDate, IReadOnlyList<WithholdingReturnLine> Lines)
{
    /// <summary>Total withheld in the period.</summary>
    public decimal TotalWithheld => Lines.Sum(l => l.Withheld);

    /// <summary>Withheld in the period and not yet paid over.</summary>
    public decimal Unremitted => Lines.Where(l => l.RemittedOn is null).Sum(l => l.Withheld);
}

/// <summary>The result of paying a period's withholding over.</summary>
/// <param name="Certificates">How many certificates were remitted.</param>
/// <param name="Amount">The amount paid over.</param>
/// <param name="JournalNumber">The remittance journal, or null when nothing was outstanding.</param>
public readonly record struct WithholdingRemittance(int Certificates, decimal Amount, string? JournalNumber);

/// <summary>
/// Withholding tax on supplier payments (brief §3.5): the return of what was withheld in a period, paying it over to the
/// revenue authority (Dr WHT payable / Cr bank, marking the certificates), and the certificate for the supplier.
/// The withholding itself happens on the payment (<c>PaymentRequest.WithholdingTaxCode</c>).
/// </summary>
public interface IWithholdingTaxService
{
    /// <summary>The certificates for payments dated in a period.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="fromDate">The first payment date.</param>
    /// <param name="toDate">The last payment date.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The return.</returns>
    Task<Result<WithholdingReturn>> ReturnAsync(long companyId, DateOnly fromDate, DateOnly toDate, CancellationToken ct = default);

    /// <summary>Pays over everything withheld in a period and not yet remitted.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="fromDate">The first payment date.</param>
    /// <param name="toDate">The last payment date.</param>
    /// <param name="remittanceDate">The date of the remittance.</param>
    /// <param name="bankAccountCode">The bank account paying it over.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The remittance.</returns>
    Task<Result<WithholdingRemittance>> RemitAsync(long companyId, DateOnly fromDate, DateOnly toDate, DateOnly remittanceDate, string bankAccountCode, CancellationToken ct = default);

    /// <summary>Renders a certificate for the supplier.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="certificateId">The certificate.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The certificate text.</returns>
    Task<Result<string>> CertificateTextAsync(long companyId, long certificateId, CancellationToken ct = default);
}
