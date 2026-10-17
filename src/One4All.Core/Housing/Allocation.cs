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
/// An allocation run (<c>hse.allocation_run</c>, brief §4.10.4 - the highest-fraud-risk process): a proposed list
/// pairing a scheme's available stands with waiting-list applicants by seniority, score or a seeded ballot. The seed,
/// the exact inputs and their hash are kept so the draw can be independently re-verified; approval must come from
/// someone other than the proposer.
/// </summary>
public sealed class AllocationRun : AuditableEntity, ICompanyScoped
{
    private readonly List<StandAllocation> _allocations = [];
    private readonly List<AllocationExclusion> _exclusions = [];

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

    /// <summary>Gets or sets how long offers stay open once approved through the approval workflow.</summary>
    public int OfferDays { get; set; } = 30;

    /// <summary>When it was approved.</summary>
    public DateOnly? ApprovedOn { get; private set; }

    /// <summary>The stand-to-applicant pairings.</summary>
    public IReadOnlyList<StandAllocation> Allocations => _allocations.AsReadOnly();

    /// <summary>The listed applicants in the run's category left out of it, and why (the appeal-defence record).</summary>
    public IReadOnlyList<AllocationExclusion> Exclusions => _exclusions.AsReadOnly();

    /// <summary>Records that a listed applicant was left out of the run.</summary>
    /// <param name="applicantId">The applicant.</param>
    /// <param name="reason">Why.</param>
    public void Exclude(long applicantId, ExclusionReason reason, string? note = null) => _exclusions.Add(new AllocationExclusion(applicantId, reason, note));

    /// <summary>Adds a pairing.</summary>
    /// <param name="allocation">The pairing.</param>
    public void Add(StandAllocation allocation)
    {
        ArgumentNullException.ThrowIfNull(allocation);
        allocation.AssignCompany(CompanyId);
        _allocations.Add(allocation);
    }

    /// <summary>Marks it approved.</summary>
    /// <param name="userId">The approver.</param>
    /// <param name="date">When.</param>
    public void Approve(long userId, DateOnly date)
    {
        Status = AllocationRunStatus.Approved;
        ApprovedByUserId = userId;
        ApprovedOn = date;
    }

    /// <summary>The committee members present at the meeting that decided the run (brief §4.10.4), or null when approved by one officer.</summary>
    public string? CommitteeMembers { get; set; }

    /// <summary>When the committee met, or null.</summary>
    public DateOnly? MeetingDate { get; set; }

    /// <summary>Gets or sets the allocation committee's resolution approving the list (brief §4.10.2).</summary>
    public string? CommitteeResolution { get; set; }

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

    /// <summary>The council (certificate numbers are unique within it).</summary>
    public long CompanyId { get; private set; }

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

    /// <summary>Sets the council from the run it belongs to.</summary>
    /// <param name="companyId">The council.</param>
    internal void AssignCompany(long companyId) => CompanyId = companyId;

    /// <summary>The stand's price frozen at the offer date, when an approved price applied.</summary>
    public decimal? OfferedPrice { get; private set; }

    /// <summary>Issues the offer.</summary>
    /// <param name="certificateNumber">The certificate number.</param>
    /// <param name="expires">The acceptance deadline.</param>
    /// <param name="price">The stand's price on the offer date, if one applies.</param>
    public void Offer(string certificateNumber, DateOnly expires, decimal? price = null)
    {
        CertificateNumber = certificateNumber;
        OfferExpires = expires;
        OfferedPrice = price;
        Status = AllocationStatus.Offered;
    }

    /// <summary>Records the applicant's acceptance.</summary>
    /// <param name="date">When.</param>
    public void Accept(DateOnly date)
    {
        Status = AllocationStatus.Accepted;
        DecidedOn = date;
    }

    /// <summary>Gets how many times the offer's deadline was extended.</summary>
    public int OfferExtensions { get; private set; }

    /// <summary>Gets the reason for the latest extension.</summary>
    public string? ExtensionReason { get; private set; }

    /// <summary>Extends an open offer's deadline (brief §4.10.4: an expired offer is lapsed or extended by an officer, with a reason).</summary>
    /// <param name="newExpiry">The new deadline.</param>
    /// <param name="reason">Why.</param>
    public void ExtendOffer(DateOnly newExpiry, string reason)
    {
        OfferExpires = newExpiry;
        OfferExtensions++;
        ExtensionReason = reason;
    }

    /// <summary>Steps out of the live offers for a moment while a swap moves the other allottee onto this stand (one live offer per stand).</summary>
    public void HoldForSwap() => Status = AllocationStatus.Cancelled;

    /// <summary>Takes a stand (and its frozen price) in a swap, back in the offer's status.</summary>
    /// <param name="standId">The stand.</param>
    /// <param name="price">Its frozen price.</param>
    /// <param name="status">The offer's status.</param>
    public void TakeStand(long standId, decimal? price, AllocationStatus status)
    {
        StandId = standId;
        OfferedPrice = price;
        Status = status;
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

    /// <summary>Puts another applicant on a proposed pairing (the committee's substitution; the skipped applicant is recorded on the run).</summary>
    /// <param name="applicantId">The applicant taking the pairing.</param>
    public void Substitute(long applicantId)
    {
        if (Status != AllocationStatus.Proposed)
        {
            throw new InvalidOperationException("Only a proposed pairing can be substituted.");
        }

        ApplicantId = applicantId;
    }
}

/// <summary>Why a listed applicant in a run's category was left out of it (brief §14 H34).</summary>
public enum ExclusionReason
{
    /// <summary>Suspended for not confirming in time.</summary>
    Suspended = 1,

    /// <summary>Already holds a live allocation (proposed, offered or accepted) from another run.</summary>
    LiveAllocation = 2,

    /// <summary>The spouse already holds a stand or a live allocation (one household, one stand).</summary>
    SpouseHolds = 3,

    /// <summary>Declined or let lapse an offer of the stand a substitution run fills.</summary>
    DeclinedStand = 4,

    /// <summary>The committee rejected the applicant's pairing, with a reason (the stand stays available).</summary>
    CommitteeRejected = 5,

    /// <summary>The committee substituted the next applicant for this one, stating why (brief §4.10.4).</summary>
    CommitteeSkipped = 6,

    /// <summary>Already holds as many stands as one beneficiary may (brief §4.10.4), on their own national ID.</summary>
    HoldsStand = 7,
}

/// <summary>A listed applicant left out of an allocation run, recorded when the run is proposed (<c>hse.allocation_exclusion</c>).</summary>
public sealed class AllocationExclusion : Entity
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private AllocationExclusion()
    {
    }

    /// <summary>Records an exclusion.</summary>
    /// <param name="applicantId">The applicant.</param>
    /// <param name="reason">Why.</param>
    public AllocationExclusion(long applicantId, ExclusionReason reason, string? note = null)
    {
        ApplicantId = applicantId;
        Reason = reason;
        Note = note;
    }

    /// <summary>The committee's stated reason (a rejection or a substitution), or null for an automatic exclusion.</summary>
    public string? Note { get; private set; }

    /// <summary>The run.</summary>
    public long AllocationRunId { get; private set; }

    /// <summary>The applicant.</summary>
    public long ApplicantId { get; private set; }

    /// <summary>Why.</summary>
    public ExclusionReason Reason { get; private set; }
}

/// <summary>A stand swap between two allottees (<c>hse.stand_swap</c>): the document behind the exchange.</summary>
public sealed class StandSwap : AuditableEntity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private StandSwap()
    {
        FirstCertificate = string.Empty;
        SecondCertificate = string.Empty;
        Reference = string.Empty;
        Reason = string.Empty;
    }

    /// <summary>Records a swap.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="firstCertificate">One allottee's certificate.</param>
    /// <param name="secondCertificate">The other's.</param>
    /// <param name="date">When.</param>
    /// <param name="reference">The swap document (the parties' signed request).</param>
    /// <param name="reason">Why.</param>
    /// <param name="approvedByUserId">Who approved it.</param>
    public StandSwap(long companyId, string firstCertificate, string secondCertificate, DateOnly date, string reference, string reason, long approvedByUserId)
    {
        CompanyId = companyId;
        FirstCertificate = firstCertificate;
        SecondCertificate = secondCertificate;
        Date = date;
        Reference = reference;
        Reason = reason;
        ApprovedByUserId = approvedByUserId;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>Gets one allottee's certificate.</summary>
    public string FirstCertificate { get; private set; }

    /// <summary>Gets the other's certificate.</summary>
    public string SecondCertificate { get; private set; }

    /// <summary>Gets when.</summary>
    public DateOnly Date { get; private set; }

    /// <summary>Gets the swap document.</summary>
    public string Reference { get; private set; }

    /// <summary>Gets why.</summary>
    public string Reason { get; private set; }

    /// <summary>Gets who approved it.</summary>
    public long ApprovedByUserId { get; private set; }
}
