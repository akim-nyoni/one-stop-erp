using One4All.Core.Common;

namespace One4All.Core.Housing;

/// <summary>Why a stand is blocked (brief §4.10.6).</summary>
public enum BlockReason
{
    /// <summary>A court interdict.</summary>
    CourtInterdict = 1,

    /// <summary>A family dispute.</summary>
    FamilyDispute = 2,

    /// <summary>Suspected fraud.</summary>
    SuspectedFraud = 3,

    /// <summary>A double allocation under investigation.</summary>
    DoubleAllocation = 4,

    /// <summary>A boundary dispute.</summary>
    BoundaryDispute = 5,

    /// <summary>Another caveat.</summary>
    Other = 6,
}

/// <summary>
/// A caveat / block on a stand (<c>hse.stand_block</c>, brief §4.10.6): while in force the stand cannot be allocated,
/// sold, leased or transferred. Placing and lifting each need a reason and a supporting document, by different senior
/// officers; the record is kept after it is lifted. One block in force per stand, in the database.
/// </summary>
public sealed class StandBlock : AuditableEntity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private StandBlock()
    {
        Detail = string.Empty;
        DocumentReference = string.Empty;
    }

    /// <summary>Places a block.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="standId">The stand.</param>
    /// <param name="reason">Why.</param>
    /// <param name="detail">The particulars.</param>
    /// <param name="documentReference">The supporting document.</param>
    /// <param name="placedByUserId">Who placed it.</param>
    /// <param name="placedOn">When.</param>
    public StandBlock(long companyId, long standId, BlockReason reason, string detail, string documentReference, long placedByUserId, DateOnly placedOn)
    {
        CompanyId = companyId;
        StandId = standId;
        Reason = reason;
        Detail = detail;
        DocumentReference = documentReference;
        PlacedByUserId = placedByUserId;
        PlacedOn = placedOn;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The stand.</summary>
    public long StandId { get; private set; }

    /// <summary>Why.</summary>
    public BlockReason Reason { get; private set; }

    /// <summary>The particulars.</summary>
    public string Detail { get; private set; }

    /// <summary>The supporting document.</summary>
    public string DocumentReference { get; private set; }

    /// <summary>Who placed it.</summary>
    public long PlacedByUserId { get; private set; }

    /// <summary>When it was placed.</summary>
    public DateOnly PlacedOn { get; private set; }

    /// <summary>Who lifted it.</summary>
    public long? LiftedByUserId { get; private set; }

    /// <summary>When it was lifted; in force while null.</summary>
    public DateOnly? LiftedOn { get; private set; }

    /// <summary>Why it was lifted.</summary>
    public string? LiftReason { get; private set; }

    /// <summary>The document supporting the lifting.</summary>
    public string? LiftDocumentReference { get; private set; }

    /// <summary>Lifts the block.</summary>
    /// <param name="userId">Who.</param>
    /// <param name="date">When.</param>
    /// <param name="reason">Why.</param>
    /// <param name="documentReference">The supporting document.</param>
    public void Lift(long userId, DateOnly date, string reason, string documentReference)
    {
        LiftedByUserId = userId;
        LiftedOn = date;
        LiftReason = reason;
        LiftDocumentReference = documentReference;
    }
}
