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
        SettledAmount = paid;
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
