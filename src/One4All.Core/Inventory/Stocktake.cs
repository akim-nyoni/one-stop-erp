using One4All.Core.Common;

namespace One4All.Core.Inventory;

/// <summary>The state of a stocktake session.</summary>
public enum StocktakeStatus
{
    /// <summary>Being counted — the on-hand snapshot is frozen and counts are being captured.</summary>
    Draft = 0,

    /// <summary>Posted — variances have been adjusted into stock and the session is closed.</summary>
    Posted = 1,
}

/// <summary>
/// A physical stocktake of one warehouse (brief §4.2): the on-hand quantity of each counted item is frozen
/// when the count starts, the physical counts are captured against it, and on posting the variance of each
/// counted line is adjusted into stock at moving-average cost. Freezing the system quantity up front means a
/// later receipt or issue does not distort the variance.
/// </summary>
public sealed class StocktakeSession : AuditableEntity, ICompanyScoped
{
    private readonly List<StocktakeLine> _lines = [];

    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private StocktakeSession() => Reference = string.Empty;

    /// <summary>Starts a stocktake session.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="warehouseId">The warehouse counted.</param>
    /// <param name="reference">The stocktake reference.</param>
    /// <param name="stocktakeDate">The stocktake date.</param>
    public StocktakeSession(long companyId, long warehouseId, string reference, DateOnly stocktakeDate)
    {
        CompanyId = companyId;
        WarehouseId = warehouseId;
        Reference = reference;
        StocktakeDate = stocktakeDate;
        Status = StocktakeStatus.Draft;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The warehouse counted.</summary>
    public long WarehouseId { get; private set; }

    /// <summary>The stocktake reference.</summary>
    public string Reference { get; private set; }

    /// <summary>The stocktake date.</summary>
    public DateOnly StocktakeDate { get; private set; }

    /// <summary>The status.</summary>
    public StocktakeStatus Status { get; private set; }

    /// <summary>
    /// Gets or sets the variance, as a percent of the frozen quantity, beyond which a count must be recounted by someone else before
    /// posting (null: no recounts).
    /// </summary>
    public decimal? RecountTolerancePercent { get; set; }

    /// <summary>Whether a line's count is outside the tolerance and not yet recounted.</summary>
    /// <param name="line">The line.</param>
    /// <returns>True when a recount is due.</returns>
    public bool NeedsRecount(StocktakeLine line)
        => RecountTolerancePercent is decimal pct && line.IsCounted && !line.IsRecounted && Math.Abs(line.VarianceQuantity) > Math.Abs(line.SystemQuantity) * pct / 100m;

    /// <summary>The count lines.</summary>
    public IReadOnlyList<StocktakeLine> Lines => _lines.AsReadOnly();

    /// <summary>Adds a line with the frozen system quantity for an item.</summary>
    /// <param name="itemId">The item.</param>
    /// <param name="systemQuantity">The on-hand quantity at the start of the count.</param>
    /// <param name="averageCost">The moving-average unit cost (for the indicative variance value).</param>
    /// <returns>The created line.</returns>
    public StocktakeLine AddLine(long itemId, decimal systemQuantity, decimal averageCost)
    {
        StocktakeLine line = new(itemId, systemQuantity, averageCost);
        _lines.Add(line);
        return line;
    }

    /// <summary>Records the physical count for an item.</summary>
    /// <param name="itemId">The item.</param>
    /// <param name="countedQuantity">The counted quantity.</param>
    /// <returns>True when a line for the item existed and was updated.</returns>
    public bool RecordCount(long itemId, decimal countedQuantity)
    {
        StocktakeLine? line = _lines.FirstOrDefault(l => l.ItemId == itemId);
        if (line is null)
        {
            return false;
        }

        line.SetCount(countedQuantity);
        return true;
    }

    /// <summary>Marks the session posted.</summary>
    public void MarkPosted() => Status = StocktakeStatus.Posted;

    /// <summary>Gets the date the variances post on once the approval workflow approves them (null: not awaiting approval).</summary>
    public DateOnly? PendingPostingDate { get; private set; }

    /// <summary>Gets the stock-adjustment account the variances post to once approved.</summary>
    public string? PendingAdjustmentAccountCode { get; private set; }

    /// <summary>Holds the posting for the approval workflow.</summary>
    /// <param name="date">The posting date.</param>
    /// <param name="adjustmentAccountCode">The stock-adjustment account.</param>
    public void HoldForApproval(DateOnly date, string adjustmentAccountCode)
    {
        PendingPostingDate = date;
        PendingAdjustmentAccountCode = adjustmentAccountCode;
    }

    /// <summary>Releases a posting the workflow turned down, so the counts can be corrected and posted again.</summary>
    public void ReleaseHold()
    {
        PendingPostingDate = null;
        PendingAdjustmentAccountCode = null;
    }
}

/// <summary>One counted item on a <see cref="StocktakeSession"/>.</summary>
public sealed class StocktakeLine : Entity
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private StocktakeLine()
    {
    }

    /// <summary>Creates a line.</summary>
    /// <param name="itemId">The item.</param>
    /// <param name="systemQuantity">The frozen on-hand quantity.</param>
    /// <param name="averageCost">The moving-average unit cost.</param>
    public StocktakeLine(long itemId, decimal systemQuantity, decimal averageCost)
    {
        ItemId = itemId;
        SystemQuantity = systemQuantity;
        AverageCost = averageCost;
    }

    /// <summary>The owning stocktake id.</summary>
    public long StocktakeSessionId { get; private set; }

    /// <summary>The item.</summary>
    public long ItemId { get; private set; }

    /// <summary>The frozen on-hand quantity when the count started.</summary>
    public decimal SystemQuantity { get; private set; }

    /// <summary>The moving-average unit cost captured at the start.</summary>
    public decimal AverageCost { get; private set; }

    /// <summary>The physical count, or null until captured.</summary>
    public decimal? CountedQuantity { get; private set; }

    /// <summary>Whether a count has been captured for this line.</summary>
    public bool IsCounted => CountedQuantity.HasValue;

    /// <summary>The variance quantity (counted − system); zero until counted.</summary>
    public decimal VarianceQuantity => CountedQuantity is { } counted ? counted - SystemQuantity : 0m;

    /// <summary>The indicative variance value at the captured average cost.</summary>
    public decimal VarianceValue => decimal.Round(VarianceQuantity * AverageCost, 2, MidpointRounding.ToEven);

    /// <summary>Sets the physical count.</summary>
    /// <param name="countedQuantity">The counted quantity.</param>
    public void SetCount(decimal countedQuantity) => CountedQuantity = countedQuantity;

    /// <summary>Gets or sets who made the first count.</summary>
    public long? CountedByUserId { get; set; }

    /// <summary>Gets the first count, kept when a recount replaces it.</summary>
    public decimal? FirstCountQuantity { get; private set; }

    /// <summary>Gets who recounted.</summary>
    public long? RecountedByUserId { get; private set; }

    /// <summary>Gets a value indicating whether the line was recounted.</summary>
    public bool IsRecounted => RecountedByUserId.HasValue;

    /// <summary>Replaces the count with a recount, keeping the first.</summary>
    /// <param name="quantity">The recount.</param>
    /// <param name="userId">Who recounted.</param>
    public void Recount(decimal quantity, long userId)
    {
        FirstCountQuantity = CountedQuantity;
        CountedQuantity = quantity;
        RecountedByUserId = userId;
    }
}
