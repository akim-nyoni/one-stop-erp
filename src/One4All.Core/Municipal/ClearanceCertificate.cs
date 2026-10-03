using One4All.Core.Common;

namespace One4All.Core.Municipal;

/// <summary>
/// A rates clearance certificate (<c>mun.clearance_certificate</c>, brief §4.9): issued for a property transfer once the
/// ratepayer's arrears and the required months in advance are paid, valid until a set date. The figures it was issued
/// on are kept.
/// </summary>
public sealed class ClearanceCertificate : AuditableEntity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private ClearanceCertificate()
    {
        CertificateNumber = string.Empty;
    }

    /// <summary>Issues a certificate.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="certificateNumber">The certificate number.</param>
    /// <param name="propertyId">The property.</param>
    /// <param name="issuedOn">When it was issued.</param>
    /// <param name="validUntil">The last day it is valid.</param>
    /// <param name="advanceHeld">The advance charges paid for clearance.</param>
    /// <param name="issuedByUserId">Who issued it.</param>
    public ClearanceCertificate(long companyId, string certificateNumber, long propertyId, DateOnly issuedOn, DateOnly validUntil, decimal advanceHeld, long issuedByUserId)
    {
        CompanyId = companyId;
        CertificateNumber = certificateNumber;
        PropertyId = propertyId;
        IssuedOn = issuedOn;
        ValidUntil = validUntil;
        AdvanceHeld = advanceHeld;
        IssuedByUserId = issuedByUserId;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The certificate number.</summary>
    public string CertificateNumber { get; private set; }

    /// <summary>The property.</summary>
    public long PropertyId { get; private set; }

    /// <summary>When it was issued.</summary>
    public DateOnly IssuedOn { get; private set; }

    /// <summary>The last day it is valid.</summary>
    public DateOnly ValidUntil { get; private set; }

    /// <summary>The advance charges paid for clearance.</summary>
    public decimal AdvanceHeld { get; private set; }

    /// <summary>Who issued it.</summary>
    public long IssuedByUserId { get; private set; }
}
