using One4All.Core.Common;

namespace One4All.Core.Procurement;

/// <summary>A contract or framework agreement with a supplier (<c>pur.framework_agreement</c>): orders are called off against it up to a ceiling within its term.</summary>
public sealed class FrameworkAgreement : AuditableEntity, ICompanyScoped
{
    private FrameworkAgreement()
    {
        Reference = string.Empty;
        Description = string.Empty;
    }

    /// <summary>Creates an agreement.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="supplierId">The supplier.</param>
    /// <param name="reference">The contract reference.</param>
    /// <param name="description">What it covers.</param>
    /// <param name="startDate">The first day orders may be called off.</param>
    /// <param name="endDate">The last day.</param>
    /// <param name="ceilingValue">The most that may be ordered under it.</param>
    public FrameworkAgreement(long companyId, long supplierId, string reference, string description, DateOnly startDate, DateOnly endDate, decimal ceilingValue)
    {
        CompanyId = companyId;
        SupplierId = supplierId;
        Reference = reference.Trim().ToUpperInvariant();
        Description = description;
        StartDate = startDate;
        EndDate = endDate;
        CeilingValue = ceilingValue;
    }

    /// <summary>Gets the company.</summary>
    public long CompanyId { get; private set; }

    /// <summary>Gets the supplier.</summary>
    public long SupplierId { get; private set; }

    /// <summary>Gets the contract reference.</summary>
    public string Reference { get; private set; }

    /// <summary>Gets what it covers.</summary>
    public string Description { get; private set; }

    /// <summary>Gets the first day of the term.</summary>
    public DateOnly StartDate { get; private set; }

    /// <summary>Gets the last day of the term.</summary>
    public DateOnly EndDate { get; private set; }

    /// <summary>Gets the ceiling.</summary>
    public decimal CeilingValue { get; private set; }
}
