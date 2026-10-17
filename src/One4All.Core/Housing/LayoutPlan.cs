using One4All.Core.Common;

namespace One4All.Core.Housing;

/// <summary>The approved density of a layout (brief §4.10.2).</summary>
public enum PlanDensity
{
    /// <summary>Low density.</summary>
    Low = 1,

    /// <summary>Medium density.</summary>
    Medium = 2,

    /// <summary>High density.</summary>
    High = 3,
}

/// <summary>What a layout sets land aside for (brief §4.10.2).</summary>
public enum StandUse
{
    /// <summary>Residential stands.</summary>
    Residential = 1,

    /// <summary>Commercial stands.</summary>
    Commercial = 2,

    /// <summary>Industrial stands.</summary>
    Industrial = 3,

    /// <summary>Institutional stands.</summary>
    Institutional = 4,

    /// <summary>Open space.</summary>
    OpenSpace = 5,

    /// <summary>Roads (not stands).</summary>
    Roads = 6,

    /// <summary>School sites.</summary>
    Schools = 7,

    /// <summary>Clinic sites.</summary>
    Clinics = 8,
}

/// <summary>
/// A layout plan for a parcel (<c>hse.layout_plan</c>, brief §4.10.2 layout plan register): each submission and
/// amendment is a version with its planner, density and stands by use, approved by the planning authority; earlier
/// versions are kept, so the plan's history can always be shown. One plan per parcel.
/// </summary>
public sealed class LayoutPlan : AuditableEntity, ICompanyScoped
{
    private readonly List<LayoutPlanVersion> _versions = [];

    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private LayoutPlan()
    {
        PlanNumber = string.Empty;
    }

    /// <summary>Registers a plan.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="planNumber">The layout number.</param>
    /// <param name="landParcelId">The parcel.</param>
    public LayoutPlan(long companyId, string planNumber, long landParcelId)
    {
        CompanyId = companyId;
        PlanNumber = planNumber;
        LandParcelId = landParcelId;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The layout number.</summary>
    public string PlanNumber { get; private set; }

    /// <summary>The parcel.</summary>
    public long LandParcelId { get; private set; }

    /// <summary>Every version, oldest first.</summary>
    public IReadOnlyList<LayoutPlanVersion> Versions => [.. _versions.OrderBy(v => v.Version)];

    /// <summary>The newest version.</summary>
    public LayoutPlanVersion Latest => _versions.MaxBy(v => v.Version)!;

    /// <summary>The newest approved version - the plan in force - if any.</summary>
    public LayoutPlanVersion? InForce => _versions.Where(v => v.ApprovedOn is not null).MaxBy(v => v.Version);

    /// <summary>Adds a version.</summary>
    /// <param name="version">The version.</param>
    public void AddVersion(LayoutPlanVersion version) => _versions.Add(version);
}

/// <summary>A version of a layout plan (<c>hse.layout_plan_version</c>).</summary>
public sealed class LayoutPlanVersion : Entity
{
    private readonly List<LayoutUseCount> _uses = [];

    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private LayoutPlanVersion()
    {
        Planner = string.Empty;
    }

    /// <summary>Records a submitted version.</summary>
    /// <param name="version">The version number.</param>
    /// <param name="planner">The planner.</param>
    /// <param name="density">The density.</param>
    /// <param name="submittedOn">When it was submitted.</param>
    /// <param name="amendmentReason">Why it amends the previous version.</param>
    public LayoutPlanVersion(int version, string planner, PlanDensity density, DateOnly submittedOn, string? amendmentReason)
    {
        Version = version;
        Planner = planner;
        Density = density;
        SubmittedOn = submittedOn;
        AmendmentReason = amendmentReason;
    }

    /// <summary>The plan.</summary>
    public long LayoutPlanId { get; private set; }

    /// <summary>The version number.</summary>
    public int Version { get; private set; }

    /// <summary>The planner.</summary>
    public string Planner { get; private set; }

    /// <summary>The density.</summary>
    public PlanDensity Density { get; private set; }

    /// <summary>When it was submitted.</summary>
    public DateOnly SubmittedOn { get; private set; }

    /// <summary>Why it amends the previous version.</summary>
    public string? AmendmentReason { get; private set; }

    /// <summary>The approving authority.</summary>
    public string? ApprovalAuthority { get; private set; }

    /// <summary>When it was approved.</summary>
    public DateOnly? ApprovedOn { get; private set; }

    /// <summary>The stands by use.</summary>
    public IReadOnlyList<LayoutUseCount> Uses => _uses.AsReadOnly();

    /// <summary>Sets the stands for a use.</summary>
    /// <param name="use">The use.</param>
    /// <param name="stands">The number of stands (or road reserves).</param>
    public void SetUse(StandUse use, int stands) => _uses.Add(new LayoutUseCount(use, stands));

    /// <summary>Records the approval.</summary>
    /// <param name="authority">The approving authority.</param>
    /// <param name="date">When.</param>
    public void Approve(string authority, DateOnly date)
    {
        ApprovalAuthority = authority;
        ApprovedOn = date;
    }
}

/// <summary>The stands a layout version sets aside for one use (<c>hse.layout_use_count</c>).</summary>
public sealed class LayoutUseCount : Entity
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private LayoutUseCount()
    {
    }

    /// <summary>Records a count.</summary>
    /// <param name="use">The use.</param>
    /// <param name="stands">The number.</param>
    public LayoutUseCount(StandUse use, int stands)
    {
        Use = use;
        Stands = stands;
    }

    /// <summary>The version.</summary>
    public long LayoutPlanVersionId { get; private set; }

    /// <summary>The use.</summary>
    public StandUse Use { get; private set; }

    /// <summary>The number.</summary>
    public int Stands { get; private set; }
}
