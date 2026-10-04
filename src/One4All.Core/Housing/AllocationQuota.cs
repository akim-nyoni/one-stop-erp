using One4All.Core.Common;

namespace One4All.Core.Housing;

/// <summary>Beneficiary groups that allocation quotas target (brief §14 H37); an applicant can belong to several.</summary>
[Flags]
public enum BeneficiaryGroups
{
    /// <summary>No group.</summary>
    None = 0,

    /// <summary>Women.</summary>
    Women = 1,

    /// <summary>Youth (18–35; also derived from the date of birth).</summary>
    Youth = 2,

    /// <summary>People with disabilities (also derived from the disability flag).</summary>
    Disabled = 4,

    /// <summary>The elderly (60 and over; also derived from the date of birth).</summary>
    Elderly = 8,

    /// <summary>Civil servants.</summary>
    CivilServants = 16,

    /// <summary>War veterans.</summary>
    WarVeterans = 32,
}

/// <summary>
/// A council's allocation quota for a beneficiary group (<c>hse.allocation_quota</c>, brief §14 H37): the share of a
/// scheme's allocations (or every scheme's, when no scheme is named) policy reserves for the group. A scheme's own quota
/// for a group overrides the council-wide one.
/// </summary>
public sealed class AllocationQuota : AuditableEntity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private AllocationQuota()
    {
    }

    /// <summary>Sets a quota.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="schemeId">The scheme, or null for all schemes.</param>
    /// <param name="group">The group (a single flag).</param>
    /// <param name="targetPercent">The share of allocations reserved.</param>
    public AllocationQuota(long companyId, long? schemeId, BeneficiaryGroups group, decimal targetPercent)
    {
        CompanyId = companyId;
        SchemeId = schemeId;
        Group = group;
        TargetPercent = targetPercent;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The scheme, or null for all schemes.</summary>
    public long? SchemeId { get; private set; }

    /// <summary>The group.</summary>
    public BeneficiaryGroups Group { get; private set; }

    /// <summary>The share of allocations reserved, in percent.</summary>
    public decimal TargetPercent { get; set; }
}
