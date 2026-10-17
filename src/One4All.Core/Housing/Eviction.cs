using One4All.Core.Common;

namespace One4All.Core.Housing;

/// <summary>The stages of the eviction workflow, in the order they must happen (brief §4.10.7).</summary>
public enum EvictionStage
{
    /// <summary>A reminder of the arrears.</summary>
    Reminder = 1,

    /// <summary>A formal letter of demand.</summary>
    Demand = 2,

    /// <summary>Notice to vacate.</summary>
    NoticeToVacate = 3,

    /// <summary>Handed to the council's attorneys.</summary>
    LegalHandover = 4,

    /// <summary>A court order obtained.</summary>
    CourtOrder = 5,

    /// <summary>The tenant evicted; the unit returned.</summary>
    Evicted = 6,
}

/// <summary>Where an eviction case stands.</summary>
public enum EvictionCaseStatus
{
    /// <summary>In progress.</summary>
    Open = 0,

    /// <summary>Ended by eviction.</summary>
    Evicted = 1,

    /// <summary>Withdrawn (for example the arrears were cleared).</summary>
    Withdrawn = 2,
}

/// <summary>
/// An eviction case against a council tenancy in arrears (<c>hse.eviction_case</c>, brief §4.10.7): its stages, each
/// recorded with its date and reference, the notice period honoured, ending in eviction - which ends the tenancy - or
/// withdrawal. A tenancy has one open case at a time.
/// </summary>
public sealed class EvictionCase : AuditableEntity, ICompanyScoped
{
    private readonly List<EvictionStep> _steps = [];

    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private EvictionCase()
    {
        CaseNumber = string.Empty;
    }

    /// <summary>Opens a case with its reminder.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="caseNumber">The case number.</param>
    /// <param name="tenancyId">The tenancy.</param>
    /// <param name="arrears">What the tenant owed when the case opened.</param>
    /// <param name="noticePeriodDays">Days between notice to vacate and legal handover.</param>
    /// <param name="openedOn">When the reminder was sent.</param>
    /// <param name="userId">Who opened it.</param>
    public EvictionCase(long companyId, string caseNumber, long tenancyId, decimal arrears, int noticePeriodDays, DateOnly openedOn, long userId)
    {
        CompanyId = companyId;
        CaseNumber = caseNumber;
        TenancyId = tenancyId;
        ArrearsAtOpening = arrears;
        NoticePeriodDays = noticePeriodDays;
        Status = EvictionCaseStatus.Open;
        _steps.Add(new EvictionStep(EvictionStage.Reminder, openedOn, null, userId));
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The case number.</summary>
    public string CaseNumber { get; private set; }

    /// <summary>The tenancy.</summary>
    public long TenancyId { get; private set; }

    /// <summary>What the tenant owed when the case opened.</summary>
    public decimal ArrearsAtOpening { get; private set; }

    /// <summary>Days between notice to vacate and legal handover.</summary>
    public int NoticePeriodDays { get; private set; }

    /// <summary>Where it stands.</summary>
    public EvictionCaseStatus Status { get; private set; }

    /// <summary>Why it was withdrawn.</summary>
    public string? WithdrawnReason { get; private set; }

    /// <summary>The stages reached, in order.</summary>
    public IReadOnlyList<EvictionStep> Steps => _steps.AsReadOnly();

    /// <summary>The latest stage reached.</summary>
    public EvictionStep Current => _steps.OrderBy(s => s.Stage).Last();

    /// <summary>Records the next stage.</summary>
    /// <param name="step">The step.</param>
    public void Advance(EvictionStep step)
    {
        ArgumentNullException.ThrowIfNull(step);
        _steps.Add(step);
        if (step.Stage == EvictionStage.Evicted)
        {
            Status = EvictionCaseStatus.Evicted;
        }
    }

    /// <summary>Withdraws it.</summary>
    /// <param name="reason">Why.</param>
    public void Withdraw(string reason)
    {
        Status = EvictionCaseStatus.Withdrawn;
        WithdrawnReason = reason;
    }
}

/// <summary>A stage reached in an eviction case (<c>hse.eviction_step</c>).</summary>
public sealed class EvictionStep : Entity
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private EvictionStep()
    {
    }

    /// <summary>Records a stage.</summary>
    /// <param name="stage">The stage.</param>
    /// <param name="date">When.</param>
    /// <param name="reference">The letter, notice, attorney or court-order reference.</param>
    /// <param name="userId">Who recorded it.</param>
    public EvictionStep(EvictionStage stage, DateOnly date, string? reference, long userId)
    {
        Stage = stage;
        Date = date;
        Reference = reference;
        UserId = userId;
    }

    /// <summary>The case.</summary>
    public long EvictionCaseId { get; private set; }

    /// <summary>The stage.</summary>
    public EvictionStage Stage { get; private set; }

    /// <summary>When.</summary>
    public DateOnly Date { get; private set; }

    /// <summary>The letter, notice, attorney or court-order reference.</summary>
    public string? Reference { get; private set; }

    /// <summary>Who recorded it.</summary>
    public long UserId { get; private set; }
}
