using One4All.Core.Common;

namespace One4All.Core.Municipal;

/// <summary>Where an indigent registration stands.</summary>
public enum IndigentStatus
{
    /// <summary>Approved and subsidised until its review date.</summary>
    Active = 0,

    /// <summary>Revoked; no longer subsidised.</summary>
    Revoked = 1,

    /// <summary>Requested by one user and awaiting another's approval; not subsidised.</summary>
    Pending = 2,

    /// <summary>Turned down by the approver, with the reason; never subsidised.</summary>
    Rejected = 3,
}

/// <summary>
/// An entry on the indigent / social-tariff register (<c>mun.indigent_registration</c>, brief §4.9): a household found,
/// on a means test, to qualify for a subsidy on its property's bills - a share of what is billed each month - until the
/// date its means test must be reviewed. Past that date no subsidy is given until it is reviewed again. A registration is
/// keyed by one user and subsidised only once another user approves it.
/// </summary>
public sealed class IndigentRegistration : AuditableEntity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private IndigentRegistration()
    {
    }

    /// <summary>Requests a household's registration (pending until another user approves it).</summary>
    /// <param name="companyId">The company (council).</param>
    /// <param name="propertyId">The property.</param>
    /// <param name="householdIncome">The monthly household income found on the means test.</param>
    /// <param name="subsidyPercent">The share of each month's bills subsidised.</param>
    /// <param name="approvedFrom">The first day subsidised.</param>
    /// <param name="reviewDue">The day the means test must be reviewed by.</param>
    /// <param name="requestedByUserId">Who keyed the means test.</param>
    public IndigentRegistration(long companyId, long propertyId, decimal householdIncome, decimal subsidyPercent, DateOnly approvedFrom, DateOnly reviewDue, long requestedByUserId)
    {
        CompanyId = companyId;
        PropertyId = propertyId;
        HouseholdIncome = householdIncome;
        SubsidyPercent = subsidyPercent;
        ApprovedFrom = approvedFrom;
        ReviewDue = reviewDue;
        RequestedByUserId = requestedByUserId;
        Status = IndigentStatus.Pending;
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

    /// <summary>Gets who keyed the means test.</summary>
    public long RequestedByUserId { get; private set; }

    /// <summary>Who approved it (or last reviewed it); null while pending or when rejected.</summary>
    public long? ApprovedByUserId { get; private set; }

    /// <summary>Gets who turned it down, when rejected.</summary>
    public long? RejectedByUserId { get; private set; }

    /// <summary>Gets why it was turned down, when rejected.</summary>
    public string? RejectionReason { get; private set; }

    /// <summary>Where it stands.</summary>
    public IndigentStatus Status { get; private set; }

    /// <summary>Whether a month's bills are subsidised: active, approved by then, and not past review.</summary>
    /// <param name="monthEnd">The month's last day.</param>
    /// <returns>True when subsidised.</returns>
    public bool CoversMonth(DateOnly monthEnd) => Status == IndigentStatus.Active && ApprovedFrom <= monthEnd && ReviewDue >= monthEnd;

    /// <summary>Approves a pending registration (the caller has checked the approver is not who requested it).</summary>
    /// <param name="userId">Who approved it.</param>
    public void Approve(long userId)
    {
        Status = IndigentStatus.Active;
        ApprovedByUserId = userId;
    }

    /// <summary>Turns a pending registration down.</summary>
    /// <param name="userId">Who.</param>
    /// <param name="reason">Why.</param>
    public void Reject(long userId, string reason)
    {
        Status = IndigentStatus.Rejected;
        RejectedByUserId = userId;
        RejectionReason = reason;
    }

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

/// <summary>
/// A council's indigent policy (<c>mun.indigent_policy</c>, one row per company): the maximum monthly household income a
/// household may earn to qualify for the indigent register. It is a council decision held as a setting, so a clerk keying
/// a means test cannot choose the threshold the household is measured against.
/// </summary>
public sealed class IndigentPolicy : AuditableEntity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private IndigentPolicy()
    {
    }

    /// <summary>Creates the policy.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="maximumIncome">The qualifying maximum monthly household income.</param>
    public IndigentPolicy(long companyId, decimal maximumIncome)
    {
        CompanyId = companyId;
        MaximumIncome = maximumIncome;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>Gets the qualifying maximum monthly household income.</summary>
    public decimal MaximumIncome { get; private set; }

    /// <summary>Sets the qualifying maximum monthly household income.</summary>
    /// <param name="maximumIncome">The threshold.</param>
    public void Set(decimal maximumIncome) => MaximumIncome = maximumIncome;
}
