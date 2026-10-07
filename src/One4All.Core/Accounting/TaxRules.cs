using One4All.Core.Common;
using One4All.Core.Trading;

namespace One4All.Core.Accounting;

/// <summary>When VAT becomes due (brief §3.5 tax point rules).</summary>
public enum TaxPointBasis
{
    /// <summary>On the invoice date: the return follows the ledger.</summary>
    InvoiceDate = 1,

    /// <summary>On the tax point: delivery, brought forward by an earlier invoice or payment, or moved to an invoice issued soon after delivery.</summary>
    TaxPoint = 2,

    /// <summary>Cash accounting: VAT on invoices is due as they are paid (allocated), and on cash sales when made.</summary>
    Cash = 3,
}

/// <summary>The tax point of a supply.</summary>
public static class TaxPoints
{
    /// <summary>The usual window after delivery within which an invoice moves the tax point to its own date.</summary>
    public const int DefaultInvoiceWindowDays = 14;

    /// <summary>
    /// The tax point: the basic tax point is delivery (the invoice date when nothing was delivered); an invoice or payment before it brings
    /// it forward to the earlier of them; an invoice issued within the window after delivery moves it to the invoice date.
    /// </summary>
    /// <param name="invoiceDate">The invoice date.</param>
    /// <param name="deliveryDate">When the goods or service were supplied.</param>
    /// <param name="paymentDate">When payment was received, if before.</param>
    /// <param name="invoiceWindowDays">The window after delivery.</param>
    /// <returns>The tax point.</returns>
    public static DateOnly Determine(DateOnly invoiceDate, DateOnly? deliveryDate, DateOnly? paymentDate = null, int invoiceWindowDays = DefaultInvoiceWindowDays)
    {
        DateOnly basic = deliveryDate ?? invoiceDate;
        DateOnly earliest = new[] { basic, invoiceDate, paymentDate ?? DateOnly.MaxValue }.Min();
        if (earliest < basic)
        {
            return earliest;
        }

        return deliveryDate is DateOnly delivered && invoiceDate > delivered && invoiceDate.DayNumber - delivered.DayNumber <= invoiceWindowDays ? invoiceDate : basic;
    }
}

/// <summary>What a certificate a trader holds is for.</summary>
public enum CertificatePurpose
{
    /// <summary>Supplies to or from the trader are exempt from tax (an exempt tax code needs one; a certified trader is not charged tax).</summary>
    TaxExemption = 1,

    /// <summary>A supplier's tax clearance: while valid, no withholding tax is deducted from payments to it.</summary>
    TaxClearance = 2,
}

/// <summary>
/// A tax exemption certificate held for a customer or supplier (<c>gl.tax_exemption_certificate</c>): while valid, the trader is not
/// charged tax; without one, an exempt supply cannot be invoiced as exempt.
/// </summary>
public sealed class TaxExemptionCertificate : AuditableEntity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private TaxExemptionCertificate() => CertificateNumber = string.Empty;

    /// <summary>Records a certificate.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="ledger">Customer or supplier.</param>
    /// <param name="traderId">The trader.</param>
    /// <param name="certificateNumber">Its number.</param>
    /// <param name="validFrom">First day.</param>
    /// <param name="validTo">Last day.</param>
    public TaxExemptionCertificate(long companyId, TradingLedger ledger, long traderId, string certificateNumber, DateOnly validFrom, DateOnly validTo,
        CertificatePurpose purpose = CertificatePurpose.TaxExemption)
    {
        Purpose = purpose;
        CompanyId = companyId;
        Ledger = ledger;
        TraderId = traderId;
        CertificateNumber = certificateNumber;
        ValidFrom = validFrom;
        ValidTo = validTo;
    }

    /// <summary>Gets the company.</summary>
    public long CompanyId { get; private set; }

    /// <summary>Gets customer or supplier.</summary>
    public TradingLedger Ledger { get; private set; }

    /// <summary>Gets the trader.</summary>
    public long TraderId { get; private set; }

    /// <summary>Gets the certificate number.</summary>
    public string CertificateNumber { get; private set; }

    /// <summary>Gets the first valid day.</summary>
    public DateOnly ValidFrom { get; private set; }

    /// <summary>Gets the last valid day.</summary>
    public DateOnly ValidTo { get; private set; }

    /// <summary>Gets or sets who issued it (the revenue authority, a ministry).</summary>
    public string? IssuedBy { get; set; }

    /// <summary>Gets when it was withdrawn, if it was.</summary>
    public DateOnly? RevokedOn { get; private set; }

    /// <summary>What the certificate is for.</summary>
    public CertificatePurpose Purpose { get; private set; } = CertificatePurpose.TaxExemption;

    /// <summary>Whether it covers a day.</summary>
    /// <param name="date">The day.</param>
    /// <returns>True when valid.</returns>
    public bool CoversDate(DateOnly date) => date >= ValidFrom && date <= ValidTo && (RevokedOn is null || date < RevokedOn);

    /// <summary>Withdraws it from a day.</summary>
    /// <param name="date">The day it stops.</param>
    public void Revoke(DateOnly date) => RevokedOn = date;
}
