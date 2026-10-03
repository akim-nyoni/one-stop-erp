using One4All.Core.Common;

namespace One4All.Core.Procurement;

/// <summary>
/// A quality inspection of goods received into a quarantine warehouse (<c>pop.grn_inspection</c>, brief §4.3): what passed
/// and was released to stock, what was rejected and returned to the supplier, who inspected and when. Goods on hold are
/// not available to sell until inspected.
/// </summary>
public sealed class GrnInspection : AuditableEntity, ICompanyScoped
{
    private readonly List<GrnInspectionLine> _lines = [];

    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private GrnInspection()
    {
    }

    /// <summary>Records an inspection.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="goodsReceivedNoteId">The goods received note inspected.</param>
    /// <param name="inspectedOn">When.</param>
    /// <param name="inspectorUserId">Who inspected.</param>
    /// <param name="releaseWarehouseId">Where passed goods were released to.</param>
    public GrnInspection(long companyId, long goodsReceivedNoteId, DateOnly inspectedOn, long inspectorUserId, long releaseWarehouseId)
    {
        CompanyId = companyId;
        GoodsReceivedNoteId = goodsReceivedNoteId;
        InspectedOn = inspectedOn;
        InspectorUserId = inspectorUserId;
        ReleaseWarehouseId = releaseWarehouseId;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The goods received note inspected.</summary>
    public long GoodsReceivedNoteId { get; private set; }

    /// <summary>When.</summary>
    public DateOnly InspectedOn { get; private set; }

    /// <summary>Who inspected.</summary>
    public long InspectorUserId { get; private set; }

    /// <summary>Where passed goods were released to.</summary>
    public long ReleaseWarehouseId { get; private set; }

    /// <summary>The results per item.</summary>
    public IReadOnlyList<GrnInspectionLine> Lines => _lines.AsReadOnly();

    /// <summary>Records an item's result.</summary>
    /// <param name="itemId">The item.</param>
    /// <param name="passed">The quantity passed and released.</param>
    /// <param name="rejected">The quantity rejected and returned.</param>
    /// <param name="reason">Why it was rejected.</param>
    public void AddResult(long itemId, decimal passed, decimal rejected, string? reason) => _lines.Add(new GrnInspectionLine(itemId, passed, rejected, reason));
}

/// <summary>One item's inspection result (<c>pop.grn_inspection_line</c>).</summary>
public sealed class GrnInspectionLine : Entity
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private GrnInspectionLine()
    {
    }

    /// <summary>Creates a result.</summary>
    /// <param name="itemId">The item.</param>
    /// <param name="passed">The quantity passed.</param>
    /// <param name="rejected">The quantity rejected.</param>
    /// <param name="reason">Why it was rejected.</param>
    public GrnInspectionLine(long itemId, decimal passed, decimal rejected, string? reason)
    {
        ItemId = itemId;
        Passed = passed;
        Rejected = rejected;
        RejectionReason = reason;
    }

    /// <summary>The inspection.</summary>
    public long GrnInspectionId { get; private set; }

    /// <summary>The item.</summary>
    public long ItemId { get; private set; }

    /// <summary>The quantity passed and released.</summary>
    public decimal Passed { get; private set; }

    /// <summary>The quantity rejected and returned.</summary>
    public decimal Rejected { get; private set; }

    /// <summary>Why it was rejected.</summary>
    public string? RejectionReason { get; private set; }
}
