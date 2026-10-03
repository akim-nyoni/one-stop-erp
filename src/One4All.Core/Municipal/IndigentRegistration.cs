using One4All.Core.Common;

namespace One4All.Core.Municipal;

/// <summary>Where an indigent registration stands.</summary>
public enum IndigentStatus
{
    /// <summary>Approved and subsidised until its review date.</summary>
    Active = 0,

    /// <summary>Revoked; no longer subsidised.</summary>
    Revoked = 1,
}

/// <summary>
/// An entry on the indigent / social-tariff register (<c>mun.indigent_registration</c>, brief §4.9): a household found,
/// on a means test, to qualify for a subsidy on its property's bills — a share of what is billed each month — until the
/// date its means test must be reviewed. Past that date no subsidy is given until it is reviewed again.
/// </summary>
public sealed class IndigentRegistration : AuditableEntity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private IndigentRegistration()
    {
    }

    /// <summary>Registers a household.</summary>
    /// <param name="companyId">The company (council).</param>
    /// <param name="propertyId">The property.</param>
    /// <param name="householdIncome">The monthly household income found on the means test.</param>
    /// <param name="subsidyPercent">The share of each month's bills subsidised.</param>
    /// <param name="approvedFrom">The first day subsidised.</param>
    /// <param name="reviewDue">The day the means test must be reviewed by.</param>
    /// <param name="approvedByUserId">Who approved it.</param>
    public IndigentRegistration(long companyId, long propertyId, decimal householdIncome, decimal subsidyPercent, DateOnly approvedFrom, DateOnly reviewDue, long approvedByUserId)
    {
        CompanyId = companyId;
        PropertyId = propertyId;
        HouseholdIncome = householdIncome;
        SubsidyPercent = subsidyPercent;
        ApprovedFrom = approvedFrom;
        ReviewDue = reviewDue;
        ApprovedByUserId = approvedByUserId;
        Status = IndigentStatus.Active;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The property.</summary>
    public long PropertyId { get; private set; }

    /// <summary>The monthly household income found on the means test.</summary>
    public decimal HouseholdIncome { get; private set; }

    /// <summary>The share of each month's bills subsidised.</summary>
    public decimal SubsidyPercent { get; private set; }

    /// <summary>The first day subsidised.</summary>
    public DateOnly ApprovedFrom { get; private set; }

    /// <summary>The day the means test must be reviewed by.</summary>
    public DateOnly ReviewDue { get; private set; }

    /// <summary>Who approved it.</summary>
    public long ApprovedByUserId { get; private set; }

    /// <summary>Where it stands.</summary>
    public IndigentStatus Status { get; private set; }

    /// <summary>Whether a month's bills are subsidised: active, approved by then, and not past review.</summary>
    /// <param name="monthEnd">The month's last day.</param>
    /// <returns>True when subsidised.</returns>
    public bool CoversMonth(DateOnly monthEnd) => Status == IndigentStatus.Active && ApprovedFrom <= monthEnd && ReviewDue >= monthEnd;

    /// <summary>Records a fresh means test, setting a new income, subsidy and review date.</summary>
    /// <param name="householdIncome">The income found.</param>
    /// <param name="subsidyPercent">The subsidy share.</param>
    /// <param name="reviewDue">The next review date.</param>
    /// <param name="userId">Who reviewed it.</param>
    public void Review(decimal householdIncome, decimal subsidyPercent, DateOnly reviewDue, long userId)
    {
        HouseholdIncome = householdIncome;
        SubsidyPercent = subsidyPercent;
        ReviewDue = reviewDue;
        ApprovedByUserId = userId;
    }

    /// <summary>Revokes the registration.</summary>
    public void Revoke() => Status = IndigentStatus.Revoked;
}
