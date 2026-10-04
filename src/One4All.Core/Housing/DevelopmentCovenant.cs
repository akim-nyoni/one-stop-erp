using One4All.Core.Common;

namespace One4All.Core.Housing;

/// <summary>The development milestones a covenant tracks, in the order they must happen (brief §4.10.5).</summary>
public enum CovenantMilestone
{
    /// <summary>Building plans submitted.</summary>
    PlanSubmitted = 1,

    /// <summary>Building plans approved.</summary>
    PlanApproved = 2,

    /// <summary>Construction commenced (inspected).</summary>
    ConstructionStarted = 3,

    /// <summary>Construction completed (completion inspection passed).</summary>
    Completed = 4,
}

/// <summary>Where a development covenant stands.</summary>
public enum CovenantStatus
{
    /// <summary>Running: the holder is still within the deadline.</summary>
    Open = 0,

    /// <summary>Complied with: construction completed.</summary>
    Complied = 1,

    /// <summary>In breach: the deadline passed before completion; the penalty levy raised.</summary>
    Breached = 2,

    /// <summary>A notice of intention to repossess has been issued.</summary>
    NoticeIssued = 3,
}

/// <summary>
/// A development covenant on a stand (<c>hse.development_covenant</c>, brief §4.10.5: build within N years or the stand
/// reverts): the deadline, the milestones reached with their references, extensions granted for a fee, the breach and
/// its penalty levy, and the notice of intention to repossess.
/// </summary>
public sealed class DevelopmentCovenant : AuditableEntity, ICompanyScoped
{
    private readonly List<CovenantStep> _steps = [];

    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private DevelopmentCovenant()
    {
        CovenantNumber = string.Empty;
    }

    /// <summary>Records a covenant.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="covenantNumber">The covenant number.</param>
    /// <param name="standId">The stand.</param>
    /// <param name="holderCustomerId">The holder bound by it.</param>
    /// <param name="startDate">When it starts.</param>
    /// <param name="deadline">The last day to complete construction.</param>
    public DevelopmentCovenant(long companyId, string covenantNumber, long standId, long holderCustomerId, DateOnly startDate, DateOnly deadline)
    {
        CompanyId = companyId;
        CovenantNumber = covenantNumber;
        StandId = standId;
        HolderCustomerId = holderCustomerId;
        StartDate = startDate;
        Deadline = deadline;
        Status = CovenantStatus.Open;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The covenant number.</summary>
    public string CovenantNumber { get; private set; }

    /// <summary>The stand.</summary>
    public long StandId { get; private set; }

    /// <summary>The holder bound by it.</summary>
    public long HolderCustomerId { get; private set; }

    /// <summary>When it starts.</summary>
    public DateOnly StartDate { get; private set; }

    /// <summary>The last day to complete construction (moved by extensions).</summary>
    public DateOnly Deadline { get; private set; }

    /// <summary>Where it stands.</summary>
    public CovenantStatus Status { get; private set; }

    /// <summary>Extensions granted so far.</summary>
    public int Extensions { get; private set; }

    /// <summary>When it was found in breach.</summary>
    public DateOnly? BreachedOn { get; private set; }

    /// <summary>When the notice of intention to repossess was issued.</summary>
    public DateOnly? NoticeIssuedOn { get; private set; }

    /// <summary>The milestones reached, in order.</summary>
    public IReadOnlyList<CovenantStep> Steps => _steps.AsReadOnly();

    /// <summary>The latest milestone reached, if any.</summary>
    public CovenantMilestone? Latest => _steps.Count == 0 ? null : _steps.Max(s => s.Milestone);

    /// <summary>Records a milestone; completion discharges the covenant.</summary>
    /// <param name="step">The milestone.</param>
    public void Reach(CovenantStep step)
    {
        ArgumentNullException.ThrowIfNull(step);
        _steps.Add(step);
        if (step.Milestone == CovenantMilestone.Completed)
        {
            Status = CovenantStatus.Complied;
        }
    }

    /// <summary>Extends the deadline.</summary>
    /// <param name="newDeadline">The new deadline.</param>
    public void Extend(DateOnly newDeadline)
    {
        Deadline = newDeadline;
        Extensions++;
        if (Status == CovenantStatus.Breached)
        {
            Status = CovenantStatus.Open;
            BreachedOn = null;
        }
    }

    /// <summary>Marks it breached.</summary>
    /// <param name="date">When.</param>
    public void Breach(DateOnly date)
    {
        Status = CovenantStatus.Breached;
        BreachedOn = date;
    }

    /// <summary>Records the notice of intention to repossess.</summary>
    /// <param name="date">When.</param>
    public void IssueNotice(DateOnly date)
    {
        Status = CovenantStatus.NoticeIssued;
        NoticeIssuedOn = date;
    }
}

/// <summary>A milestone reached under a development covenant (<c>hse.covenant_step</c>).</summary>
public sealed class CovenantStep : Entity
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private CovenantStep()
    {
    }

    /// <summary>Records a milestone.</summary>
    /// <param name="milestone">The milestone.</param>
    /// <param name="date">When.</param>
    /// <param name="reference">The plan number, licence or inspection reference.</param>
    public CovenantStep(CovenantMilestone milestone, DateOnly date, string? reference)
    {
        Milestone = milestone;
        Date = date;
        Reference = reference;
    }

    /// <summary>The covenant.</summary>
    public long DevelopmentCovenantId { get; private set; }

    /// <summary>The milestone.</summary>
    public CovenantMilestone Milestone { get; private set; }

    /// <summary>When.</summary>
    public DateOnly Date { get; private set; }

    /// <summary>The plan number, licence or inspection reference.</summary>
    public string? Reference { get; private set; }
}
