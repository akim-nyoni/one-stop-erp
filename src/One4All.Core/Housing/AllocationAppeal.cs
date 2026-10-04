using One4All.Core.Common;

namespace One4All.Core.Housing;

/// <summary>Where an allocation appeal stands.</summary>
public enum AppealStatus
{
    /// <summary>Lodged, awaiting a hearing.</summary>
    Lodged = 0,

    /// <summary>A hearing has been set.</summary>
    HearingSet = 1,

    /// <summary>Upheld.</summary>
    Upheld = 2,

    /// <summary>Dismissed.</summary>
    Dismissed = 3,
}

/// <summary>
/// An appeal by an unsuccessful applicant against an allocation run (<c>hse.allocation_appeal</c>, brief §4.10.4
/// appeals register): grounds, hearing, outcome and reasons, and the allocation certificate of any re-allocation that
/// followed.
/// </summary>
public sealed class AllocationAppeal : AuditableEntity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private AllocationAppeal()
    {
        AppealNumber = string.Empty;
        Grounds = string.Empty;
    }

    /// <summary>Lodges an appeal.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="appealNumber">The appeal number.</param>
    /// <param name="applicantId">The appellant.</param>
    /// <param name="allocationRunId">The run appealed against.</param>
    /// <param name="grounds">The grounds.</param>
    /// <param name="lodgedOn">When it was lodged.</param>
    public AllocationAppeal(long companyId, string appealNumber, long applicantId, long allocationRunId, string grounds, DateOnly lodgedOn)
    {
        CompanyId = companyId;
        AppealNumber = appealNumber;
        ApplicantId = applicantId;
        AllocationRunId = allocationRunId;
        Grounds = grounds;
        LodgedOn = lodgedOn;
        Status = AppealStatus.Lodged;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The appeal number.</summary>
    public string AppealNumber { get; private set; }

    /// <summary>The appellant.</summary>
    public long ApplicantId { get; private set; }

    /// <summary>The run appealed against.</summary>
    public long AllocationRunId { get; private set; }

    /// <summary>The grounds.</summary>
    public string Grounds { get; private set; }

    /// <summary>When it was lodged.</summary>
    public DateOnly LodgedOn { get; private set; }

    /// <summary>Where it stands.</summary>
    public AppealStatus Status { get; private set; }

    /// <summary>The hearing date.</summary>
    public DateOnly? HearingDate { get; private set; }

    /// <summary>When it was decided.</summary>
    public DateOnly? DecidedOn { get; private set; }

    /// <summary>The reasons for the outcome.</summary>
    public string? OutcomeReasons { get; private set; }

    /// <summary>The certificate of the allocation made as a result, if any.</summary>
    public string? ReallocationCertificate { get; private set; }

    /// <summary>Sets the hearing.</summary>
    /// <param name="date">The hearing date.</param>
    public void SetHearing(DateOnly date)
    {
        HearingDate = date;
        Status = AppealStatus.HearingSet;
    }

    /// <summary>Records the outcome.</summary>
    /// <param name="upheld">Upheld or dismissed.</param>
    /// <param name="date">When.</param>
    /// <param name="reasons">Why.</param>
    public void Decide(bool upheld, DateOnly date, string reasons)
    {
        Status = upheld ? AppealStatus.Upheld : AppealStatus.Dismissed;
        DecidedOn = date;
        OutcomeReasons = reasons;
    }

    /// <summary>Links the re-allocation an upheld appeal led to.</summary>
    /// <param name="certificateNumber">The allocation certificate.</param>
    public void LinkReallocation(string certificateNumber) => ReallocationCertificate = certificateNumber;
}
