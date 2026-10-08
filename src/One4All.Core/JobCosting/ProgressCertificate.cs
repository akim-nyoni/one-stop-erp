using One4All.Core.Common;

namespace One4All.Core.JobCosting;

/// <summary>
/// An interim payment certificate / progress valuation (brief §4.12, <c>job.progress_certificate</c>).
/// Values work done and materials on site, deducts previous certificates and retention, and yields the
/// net payable that posts to AR.
/// </summary>
public sealed class ProgressCertificate : AuditableEntity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private ProgressCertificate() => CertificateNumber = string.Empty;

    /// <summary>Creates a progress certificate and computes its derived amounts.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="jobContractId">The contract.</param>
    /// <param name="certificateNumber">The certificate number.</param>
    /// <param name="date">The valuation date.</param>
    /// <param name="workDoneToDate">The cumulative measured work to date.</param>
    /// <param name="materialsOnSite">Materials on site this valuation.</param>
    /// <param name="previouslyCertified">The gross previously certified.</param>
    /// <param name="retentionPercent">The retention percentage.</param>
    /// <param name="variationsToDate">The cumulative value of approved variation work done to date.</param>
    public ProgressCertificate(long companyId, long jobContractId, string certificateNumber, DateOnly date, decimal workDoneToDate, decimal materialsOnSite, decimal previouslyCertified, decimal retentionPercent,
        decimal variationsToDate = 0m)
    {
        VariationsToDate = variationsToDate;
        CompanyId = companyId;
        JobContractId = jobContractId;
        CertificateNumber = certificateNumber.Trim().ToUpperInvariant();
        Date = date;
        WorkDoneToDate = workDoneToDate;
        MaterialsOnSite = materialsOnSite;
        PreviouslyCertified = previouslyCertified;
        GrossThisCertificate = workDoneToDate + variationsToDate + materialsOnSite - previouslyCertified;
        RetentionThisCertificate = decimal.Round(GrossThisCertificate * retentionPercent / 100m, 4, MidpointRounding.ToEven);
        NetPayable = GrossThisCertificate - RetentionThisCertificate;
    }

    /// <summary>Gets the cumulative value of approved variation work done to date.</summary>
    public decimal VariationsToDate { get; private set; }

    /// <summary>Gets the client's advance recovered on this certificate.</summary>
    public decimal AdvanceRecovered { get; private set; }

    /// <summary>Deducts advance recovery from what the certificate pays.</summary>
    /// <param name="amount">The recovery.</param>
    public void RecoverAdvance(decimal amount)
    {
        AdvanceRecovered = amount;
        NetPayable -= amount;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The contract.</summary>
    public long JobContractId { get; private set; }

    /// <summary>The certificate number.</summary>
    public string CertificateNumber { get; private set; }

    /// <summary>The valuation date.</summary>
    public DateOnly Date { get; private set; }

    /// <summary>The cumulative measured work to date.</summary>
    public decimal WorkDoneToDate { get; private set; }

    /// <summary>Materials on site this valuation.</summary>
    public decimal MaterialsOnSite { get; private set; }

    /// <summary>The gross previously certified.</summary>
    public decimal PreviouslyCertified { get; private set; }

    /// <summary>The gross value certified this time (work + materials − previous).</summary>
    public decimal GrossThisCertificate { get; private set; }

    /// <summary>The retention held this time.</summary>
    public decimal RetentionThisCertificate { get; private set; }

    /// <summary>The net payable this certificate (gross − retention).</summary>
    public decimal NetPayable { get; private set; }
}
