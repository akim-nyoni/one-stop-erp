using One4All.Core.Common;

namespace One4All.Core.Trading;

/// <summary>
/// Withholding tax deducted from a supplier payment (<c>trd.withholding_certificate</c>, brief §3.5): the supplier is paid
/// the gross less the tax, the tax is owed to the revenue authority, and the supplier receives a certificate as proof.
/// Amounts are in base currency. Remitting the period's withholding marks its certificates.
/// </summary>
public sealed class WithholdingCertificate : AuditableEntity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private WithholdingCertificate()
    {
        CertificateNumber = string.Empty;
        PaymentDocumentNumber = string.Empty;
        TaxCode = string.Empty;
    }

    /// <summary>Records withholding on a payment.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="certificateNumber">The certificate number.</param>
    /// <param name="supplierId">The supplier paid.</param>
    /// <param name="paymentOpenItemId">The payment's AP open item.</param>
    /// <param name="paymentDocumentNumber">The payment's document number.</param>
    /// <param name="paymentDate">The payment date.</param>
    /// <param name="taxCode">The withholding tax code.</param>
    /// <param name="ratePercent">The rate applied.</param>
    /// <param name="grossAmount">The gross payment (base currency).</param>
    /// <param name="withheldAmount">The tax withheld (base currency).</param>
    public WithholdingCertificate(long companyId, string certificateNumber, long supplierId, long paymentOpenItemId, string paymentDocumentNumber, DateOnly paymentDate, string taxCode, decimal ratePercent, decimal grossAmount, decimal withheldAmount)
    {
        CompanyId = companyId;
        CertificateNumber = certificateNumber;
        SupplierId = supplierId;
        PaymentOpenItemId = paymentOpenItemId;
        PaymentDocumentNumber = paymentDocumentNumber;
        PaymentDate = paymentDate;
        TaxCode = taxCode;
        RatePercent = ratePercent;
        GrossAmount = grossAmount;
        WithheldAmount = withheldAmount;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The certificate number.</summary>
    public string CertificateNumber { get; private set; }

    /// <summary>The supplier paid.</summary>
    public long SupplierId { get; private set; }

    /// <summary>The payment's AP open item.</summary>
    public long PaymentOpenItemId { get; private set; }

    /// <summary>The payment's document number.</summary>
    public string PaymentDocumentNumber { get; private set; }

    /// <summary>The payment date.</summary>
    public DateOnly PaymentDate { get; private set; }

    /// <summary>The withholding tax code.</summary>
    public string TaxCode { get; private set; }

    /// <summary>The rate applied, as a percent.</summary>
    public decimal RatePercent { get; private set; }

    /// <summary>The gross payment (base currency).</summary>
    public decimal GrossAmount { get; private set; }

    /// <summary>The tax withheld (base currency).</summary>
    public decimal WithheldAmount { get; private set; }

    /// <summary>When the withholding was paid over to the revenue authority, if it has been.</summary>
    public DateOnly? RemittedOn { get; private set; }

    /// <summary>The remittance journal.</summary>
    public string? RemittanceJournalNumber { get; private set; }

    /// <summary>Records that the withholding has been paid over.</summary>
    /// <param name="date">The remittance date.</param>
    /// <param name="journalNumber">The remittance journal.</param>
    public void MarkRemitted(DateOnly date, string journalNumber)
    {
        RemittedOn = date;
        RemittanceJournalNumber = journalNumber;
    }
}
