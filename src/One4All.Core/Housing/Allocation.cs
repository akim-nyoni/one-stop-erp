using One4All.Core.Common;

namespace One4All.Core.Housing;

/// <summary>How an allocation run orders the waiting list (brief §4.10.4).</summary>
public enum AllocationMethod
{
    /// <summary>By registration date, earliest first.</summary>
    Seniority = 1,

    /// <summary>By priority score, highest first.</summary>
    Score = 2,

    /// <summary>By a seeded, reproducible random draw.</summary>
    Ballot = 3,
}

/// <summary>Where an allocation run stands.</summary>
public enum AllocationRunStatus
{
    /// <summary>A proposed list awaiting the committee's approval.</summary>
    Proposed = 0,

    /// <summary>Approved: offers issued.</summary>
    Approved = 1,

    /// <summary>Cancelled before approval.</summary>
    Cancelled = 2,
}

/// <summary>Where a stand allocation stands.</summary>
public enum AllocationStatus
{
    /// <summary>On a proposed list.</summary>
    Proposed = 0,

    /// <summary>Approved and offered to the applicant.</summary>
    Offered = 1,

    /// <summary>Accepted by the applicant.</summary>
    Accepted = 2,

    /// <summary>Declined by the applicant.</summary>
    Declined = 3,

    /// <summary>The offer expired without acceptance.</summary>
    Lapsed = 4,

    /// <summary>Cancelled with its run before approval.</summary>
    Cancelled = 5,
}

/// <summary>
/// An allocation run (<c>hse.allocation_run</c>, brief §4.10.4 — the highest-fraud-risk process): a proposed list
/// pairing a scheme's available stands with waiting-list applicants by seniority, score or a seeded ballot. The seed,
/// the exact inputs and their hash are kept so the draw can be independently re-verified; approval must come from
/// someone other than the proposer.
/// </summary>
public sealed class AllocationRun : AuditableEntity, ICompanyScoped
{
    private readonly List<StandAllocation> _allocations = [];

    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private AllocationRun()
    {
        RunNumber = string.Empty;
        InputCandidates = string.Empty;
        InputStands = string.Empty;
        InputHash = string.Empty;
    }

    /// <summary>Proposes a run.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="runNumber">The run number.</param>
    /// <param name="schemeId">The scheme whose stands are allocated.</param>
    /// <param name="category">The waiting-list category drawn from.</param>
    /// <param name="method">How the list was ordered.</param>
    /// <param name="seed">The ballot seed, for a ballot.</param>
    /// <param name="inputCandidates">The candidate waiting-list numbers in canonical order, joined by '|'.</param>
    /// <param name="inputStands">The stand numbers in order, joined by '|'.</param>
    /// <param name="inputHash">The hash of the inputs.</param>
    /// <param name="proposedByUserId">Who proposed it.</param>
    public AllocationRun(long companyId, string runNumber, long schemeId, HousingCategory category, AllocationMethod method, long? seed,
        string inputCandidates, string inputStands, string inputHash, long proposedByUserId)
    {
        CompanyId = companyId;
        RunNumber = runNumber;
        SchemeId = schemeId;
        Category = category;
        Method = method;
        Seed = seed;
        InputCandidates = inputCandidates;
        InputStands = inputStands;
        InputHash = inputHash;
        ProposedByUserId = proposedByUserId;
        Status = AllocationRunStatus.Proposed;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The run number.</summary>
    public string RunNumber { get; private set; }

    /// <summary>The scheme whose stands are allocated.</summary>
    public long SchemeId { get; private set; }

    /// <summary>The waiting-list category drawn from.</summary>
    public HousingCategory Category { get; private set; }

    /// <summary>How the list was ordered.</summary>
    public AllocationMethod Method { get; private set; }

    /// <summary>The ballot seed, for a ballot.</summary>
    public long? Seed { get; private set; }

    /// <summary>The candidate waiting-list numbers in canonical order, joined by '|'.</summary>
    public string InputCandidates { get; private set; }

    /// <summary>The stand numbers in order, joined by '|'.</summary>
    public string InputStands { get; private set; }

    /// <summary>The hash of the inputs, for independent re-verification.</summary>
    public string InputHash { get; private set; }

    /// <summary>Where it stands.</summary>
    public AllocationRunStatus Status { get; private set; }

    /// <summary>Who proposed it.</summary>
    public long ProposedByUserId { get; private set; }

    /// <summary>Who approved it.</summary>
    public long? ApprovedByUserId { get; private set; }

    /// <summary>When it was approved.</summary>
    public DateOnly? ApprovedOn { get; private set; }

    /// <summary>The stand-to-applicant pairings.</summary>
    public IReadOnlyList<StandAllocation> Allocations => _allocations.AsReadOnly();

    /// <summary>Adds a pairing.</summary>
    /// <param name="allocation">The pairing.</param>
    public void Add(StandAllocation allocation) => _allocations.Add(allocation);

    /// <summary>Marks it approved.</summary>
    /// <param name="userId">The approver.</param>
    /// <param name="date">When.</param>
    public void Approve(long userId, DateOnly date)
    {
        Status = AllocationRunStatus.Approved;
        ApprovedByUserId = userId;
        ApprovedOn = date;
    }

    /// <summary>Cancels it and its pairings.</summary>
    public void Cancel()
    {
        Status = AllocationRunStatus.Cancelled;
        foreach (StandAllocation allocation in _allocations)
        {
            allocation.Close(AllocationStatus.Cancelled, null, null);
        }
    }
}

/// <summary>
/// A stand allocated to a waiting-list applicant (<c>hse.stand_allocation</c>): proposed, then offered with an
/// immutable certificate number and an acceptance deadline, then accepted, declined or lapsed. A stand and an applicant
/// each have at most one live allocation, enforced in the database.
/// </summary>
public sealed class StandAllocation : Entity
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private StandAllocation()
    {
    }

    /// <summary>Proposes a pairing.</summary>
    /// <param name="sequence">Its place on the list.</param>
    /// <param name="standId">The stand.</param>
    /// <param name="applicantId">The applicant.</param>
    public StandAllocation(int sequence, long standId, long applicantId)
    {
        Sequence = sequence;
        StandId = standId;
        ApplicantId = applicantId;
        Status = AllocationStatus.Proposed;
    }

    /// <summary>The run.</summary>
    public long AllocationRunId { get; private set; }

    /// <summary>Its place on the list.</summary>
    public int Sequence { get; private set; }

    /// <summary>The stand.</summary>
    public long StandId { get; private set; }

    /// <summary>The applicant.</summary>
    public long ApplicantId { get; private set; }

    /// <summary>Where it stands.</summary>
    public AllocationStatus Status { get; private set; }

    /// <summary>The allocation certificate number, issued on approval and never changed.</summary>
    public string? CertificateNumber { get; private set; }

    /// <summary>The last day the offer can be accepted.</summary>
    public DateOnly? OfferExpires { get; private set; }

    /// <summary>When it was accepted, declined or lapsed.</summary>
    public DateOnly? DecidedOn { get; private set; }

    /// <summary>Why it was declined or lapsed.</summary>
    public string? Reason { get; private set; }

    /// <summary>Whether it still holds the stand and the applicant.</summary>
    public bool IsLive => Status is AllocationStatus.Proposed or AllocationStatus.Offered or AllocationStatus.Accepted;

    /// <summary>Issues the offer.</summary>
    /// <param name="certificateNumber">The certificate number.</param>
    /// <param name="expires">The acceptance deadline.</param>
    public void Offer(string certificateNumber, DateOnly expires)
    {
        CertificateNumber = certificateNumber;
        OfferExpires = expires;
        Status = AllocationStatus.Offered;
    }

    /// <summary>Records the applicant's acceptance.</summary>
    /// <param name="date">When.</param>
    public void Accept(DateOnly date)
    {
        Status = AllocationStatus.Accepted;
        DecidedOn = date;
    }

    /// <summary>Closes it.</summary>
    /// <param name="status">Declined, lapsed or cancelled.</param>
    /// <param name="date">When.</param>
    /// <param name="reason">Why.</param>
    public void Close(AllocationStatus status, DateOnly? date, string? reason)
    {
        Status = status;
        DecidedOn = date;
        Reason = reason;
    }
}
