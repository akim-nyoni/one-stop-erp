using One4All.Core.Common;

namespace One4All.Core.Housing;

/// <summary>Where a compensation claim stands.</summary>
public enum CompensationStatus
{
    /// <summary>Lodged: not yet probable or measurable, nothing provided.</summary>
    Lodged = 0,

    /// <summary>Provided for: the obligation is probable and measurable.</summary>
    Provided = 1,

    /// <summary>Settled by payment.</summary>
    Settled = 2,

    /// <summary>Withdrawn or dismissed; any provision released.</summary>
    Withdrawn = 3,
}

/// <summary>
/// A claim for compensation on the compulsory acquisition of land (<c>hse.compensation_claim</c>, brief §4.10.9 / IAS
/// 37): provided for once the obligation is probable and measurable — capitalised into the land under development —
/// remeasured as the estimate changes, and settled or withdrawn.
/// </summary>
public sealed class CompensationClaim : AuditableEntity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private CompensationClaim()
    {
        ClaimNumber = string.Empty;
        Claimant = string.Empty;
        LandCostAccountCode = string.Empty;
        ProvisionAccountCode = string.Empty;
    }

    /// <summary>Lodges a claim.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="claimNumber">The claim number.</param>
    /// <param name="landParcelId">The parcel acquired.</param>
    /// <param name="claimant">The claimant.</param>
    /// <param name="lodgedOn">When it was lodged.</param>
    /// <param name="landCostAccountCode">The land (work in progress) account compensation is capitalised to.</param>
    /// <param name="provisionAccountCode">The provision account.</param>
    public CompensationClaim(long companyId, string claimNumber, long landParcelId, string claimant, DateOnly lodgedOn, string landCostAccountCode, string provisionAccountCode)
    {
        CompanyId = companyId;
        ClaimNumber = claimNumber;
        LandParcelId = landParcelId;
        Claimant = claimant;
        LodgedOn = lodgedOn;
        LandCostAccountCode = landCostAccountCode;
        ProvisionAccountCode = provisionAccountCode;
        Status = CompensationStatus.Lodged;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The claim number.</summary>
    public string ClaimNumber { get; private set; }

    /// <summary>The parcel acquired.</summary>
    public long LandParcelId { get; private set; }

    /// <summary>The claimant.</summary>
    public string Claimant { get; private set; }

    /// <summary>When it was lodged.</summary>
    public DateOnly LodgedOn { get; private set; }

    /// <summary>The land (work in progress) account compensation is capitalised to.</summary>
    public string LandCostAccountCode { get; private set; }

    /// <summary>The provision account.</summary>
    public string ProvisionAccountCode { get; private set; }

    /// <summary>Where it stands.</summary>
    public CompensationStatus Status { get; private set; }

    /// <summary>The provision currently held.</summary>
    public decimal Provision { get; private set; }

    /// <summary>What was finally paid.</summary>
    public decimal? SettledAmount { get; private set; }

    /// <summary>Paid so far (part payments and the settlement).</summary>
    public decimal PaidToDate { get; private set; }

    /// <summary>The acquisition notice's reference (compulsory acquisition).</summary>
    public string? NoticeReference { get; private set; }

    /// <summary>When the notice was served.</summary>
    public DateOnly? NoticeServedOn { get; private set; }

    /// <summary>The last day objections may be lodged.</summary>
    public DateOnly? ObjectionDeadline { get; private set; }

    /// <summary>Who assessed the compensation.</summary>
    public string? Assessor { get; private set; }

    /// <summary>When it was assessed.</summary>
    public DateOnly? AssessedOn { get; private set; }

    /// <summary>The assessed compensation.</summary>
    public decimal? AssessedAmount { get; private set; }

    /// <summary>When the council took possession.</summary>
    public DateOnly? PossessionOn { get; private set; }

    /// <summary>Objections, offers and disputes, in the order they arose.</summary>
    public IReadOnlyList<CompensationStep> Steps => _steps.AsReadOnly();

    private readonly List<CompensationStep> _steps = [];

    /// <summary>Serves the acquisition notice, opening the objection window.</summary>
    public void ServeNotice(string reference, DateOnly servedOn, int objectionDays)
    {
        NoticeReference = reference;
        NoticeServedOn = servedOn;
        ObjectionDeadline = servedOn.AddDays(objectionDays);
    }

    /// <summary>Records the compensation assessment.</summary>
    public void Assess(string assessor, DateOnly on, decimal amount)
    {
        Assessor = assessor;
        AssessedOn = on;
        AssessedAmount = amount;
    }

    /// <summary>Adds an objection, offer or dispute.</summary>
    public CompensationStep AddStep(CompensationStep step)
    {
        _steps.Add(step);
        return step;
    }

    /// <summary>Records a part payment out of the provision.</summary>
    public void PayPart(decimal amount)
    {
        PaidToDate += amount;
        Provision -= amount;
    }

    /// <summary>Records taking possession.</summary>
    public void TakePossession(DateOnly on) => PossessionOn = on;

    /// <summary>Sets the provision to a new best estimate.</summary>
    /// <param name="estimate">The estimate.</param>
    public void Provide(decimal estimate)
    {
        Provision = estimate;
        Status = CompensationStatus.Provided;
    }

    /// <summary>Records settlement.</summary>
    /// <param name="paid">What was paid.</param>
    public void Settle(decimal paid)
    {
        PaidToDate += paid;
        SettledAmount = PaidToDate;
        Provision = 0m;
        Status = CompensationStatus.Settled;
    }

    /// <summary>Withdraws it, releasing the provision.</summary>
    public void Withdraw()
    {
        Provision = 0m;
        Status = CompensationStatus.Withdrawn;
    }
}

/// <summary>What kind of step in a compulsory acquisition.</summary>
public enum CompensationStepKind
{
    /// <summary>An objection to the acquisition.</summary>
    Objection = 1,

    /// <summary>A compensation offer to the owner.</summary>
    Offer = 2,

    /// <summary>A compensation dispute referred to arbitration or the court.</summary>
    Dispute = 3,
}

/// <summary>Where a step stands.</summary>
public enum CompensationStepStatus
{
    /// <summary>Awaiting a decision, response or award.</summary>
    Open = 0,

    /// <summary>An objection upheld.</summary>
    Upheld = 1,

    /// <summary>An objection dismissed.</summary>
    Dismissed = 2,

    /// <summary>An offer accepted.</summary>
    Accepted = 3,

    /// <summary>An offer rejected.</summary>
    Rejected = 4,

    /// <summary>A dispute decided by award.</summary>
    Awarded = 5,
}

/// <summary>An objection, offer or dispute in a compulsory acquisition (<c>hse.compensation_step</c>, brief §4.10.1).</summary>
public sealed class CompensationStep : Entity
{
    private CompensationStep() => Party = string.Empty;

    /// <summary>Records the step.</summary>
    /// <param name="kind">Objection, offer or dispute.</param>
    /// <param name="party">The objector, the offeree or the forum (arbitrator / court).</param>
    /// <param name="raisedOn">When.</param>
    /// <param name="amount">The amount offered (offers).</param>
    /// <param name="note">Grounds or detail.</param>
    public CompensationStep(CompensationStepKind kind, string party, DateOnly raisedOn, decimal? amount, string? note)
    {
        Kind = kind;
        Party = party;
        RaisedOn = raisedOn;
        Amount = amount;
        Note = note;
    }

    /// <summary>The claim.</summary>
    public long CompensationClaimId { get; private set; }

    /// <summary>Objection, offer or dispute.</summary>
    public CompensationStepKind Kind { get; private set; }

    /// <summary>The objector, the offeree or the forum.</summary>
    public string Party { get; private set; }

    /// <summary>When it arose.</summary>
    public DateOnly RaisedOn { get; private set; }

    /// <summary>The amount offered or awarded.</summary>
    public decimal? Amount { get; private set; }

    /// <summary>Grounds, detail or the decision's note.</summary>
    public string? Note { get; private set; }

    /// <summary>Where it stands.</summary>
    public CompensationStepStatus Status { get; private set; }

    /// <summary>When it was resolved.</summary>
    public DateOnly? ResolvedOn { get; private set; }

    /// <summary>Resolves it.</summary>
    /// <param name="status">The outcome.</param>
    /// <param name="on">When.</param>
    /// <param name="amount">The award (disputes).</param>
    /// <param name="note">Why.</param>
    public void Resolve(CompensationStepStatus status, DateOnly on, decimal? amount = null, string? note = null)
    {
        Status = status;
        ResolvedOn = on;
        Amount = amount ?? Amount;
        Note = note ?? Note;
    }
}
