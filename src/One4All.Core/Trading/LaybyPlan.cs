using One4All.Core.Common;

namespace One4All.Core.Trading;

/// <summary>Where a lay-bye stands.</summary>
public enum LaybyStatus
{
    /// <summary>Goods set aside while the customer pays.</summary>
    Open = 0,

    /// <summary>Paid in full: the sale is made and the goods collected.</summary>
    Completed = 1,

    /// <summary>Cancelled or defaulted: the forfeiture kept, the rest refunded.</summary>
    Cancelled = 2,
}

/// <summary>
/// A lay-bye (layaway) plan (<c>ar.layby_plan</c>, brief §4.5): goods set aside for a customer who pays a deposit and
/// instalments by a final date. Payments are held as a liability, not revenue; the sale is made only when the plan is
/// paid in full. If it is cancelled, or the final date passes unpaid, the forfeiture percentage of what was paid is
/// kept as income and the rest refunded.
/// </summary>
public sealed class LaybyPlan : AuditableEntity, ICompanyScoped
{
    private readonly List<LaybyLine> _lines = [];

    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private LaybyPlan()
    {
        PlanNumber = string.Empty;
        DepositAccountCode = string.Empty;
        RevenueAccountCode = string.Empty;
        ForfeitureAccountCode = string.Empty;
    }

    /// <summary>Opens a plan.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="customerId">The customer.</param>
    /// <param name="planNumber">The plan number.</param>
    /// <param name="warehouseId">Where the goods are set aside.</param>
    /// <param name="openedOn">When it was opened.</param>
    /// <param name="finalDate">The date by which it must be paid.</param>
    /// <param name="forfeitPercent">The share of what is paid kept if the plan is cancelled or defaults.</param>
    /// <param name="depositAccountCode">The liability holding the payments.</param>
    /// <param name="revenueAccountCode">The revenue account credited on completion.</param>
    /// <param name="forfeitureAccountCode">The income account credited with forfeitures.</param>
    public LaybyPlan(long companyId, long customerId, string planNumber, long warehouseId, DateOnly openedOn, DateOnly finalDate, decimal forfeitPercent,
        string depositAccountCode, string revenueAccountCode, string forfeitureAccountCode)
    {
        CompanyId = companyId;
        CustomerId = customerId;
        PlanNumber = planNumber;
        WarehouseId = warehouseId;
        OpenedOn = openedOn;
        FinalDate = finalDate;
        ForfeitPercent = forfeitPercent;
        DepositAccountCode = depositAccountCode;
        RevenueAccountCode = revenueAccountCode;
        ForfeitureAccountCode = forfeitureAccountCode;
        Status = LaybyStatus.Open;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The customer.</summary>
    public long CustomerId { get; private set; }

    /// <summary>The plan number.</summary>
    public string PlanNumber { get; private set; }

    /// <summary>Where the goods are set aside.</summary>
    public long WarehouseId { get; private set; }

    /// <summary>When it was opened.</summary>
    public DateOnly OpenedOn { get; private set; }

    /// <summary>The date by which it must be paid.</summary>
    public DateOnly FinalDate { get; private set; }

    /// <summary>The share of what is paid kept on cancellation or default.</summary>
    public decimal ForfeitPercent { get; private set; }

    /// <summary>The liability holding the payments.</summary>
    public string DepositAccountCode { get; private set; }

    /// <summary>The revenue account credited on completion.</summary>
    public string RevenueAccountCode { get; private set; }

    /// <summary>The income account credited with forfeitures.</summary>
    public string ForfeitureAccountCode { get; private set; }

    /// <summary>What has been paid so far.</summary>
    public decimal Paid { get; private set; }

    /// <summary>Where it stands.</summary>
    public LaybyStatus Status { get; private set; }

    /// <summary>When it was completed or cancelled.</summary>
    public DateOnly? ClosedOn { get; private set; }

    /// <summary>What was kept on cancellation.</summary>
    public decimal Forfeited { get; private set; }

    /// <summary>The goods set aside.</summary>
    public IReadOnlyList<LaybyLine> Lines => _lines.AsReadOnly();

    /// <summary>The price of the goods.</summary>
    public decimal Total => _lines.Sum(l => decimal.Round(l.Quantity * l.UnitPrice, 2, MidpointRounding.AwayFromZero));

    /// <summary>Gets or sets the tax code the prices include (null: no tax on the sale).</summary>
    public string? TaxCode { get; set; }

    /// <summary>The tax a tax-inclusive total holds at a rate.</summary>
    /// <param name="total">The tax-inclusive total.</param>
    /// <param name="ratePercent">The rate.</param>
    /// <returns>The tax.</returns>
    public static decimal TaxIn(decimal total, decimal ratePercent)
        => ratePercent <= 0m ? 0m : decimal.Round(total * ratePercent / (100m + ratePercent), 2, MidpointRounding.AwayFromZero);

    /// <summary>What is still to pay.</summary>
    public decimal Balance => Total - Paid;

    /// <summary>Adds goods to the plan.</summary>
    /// <param name="itemId">The item.</param>
    /// <param name="quantity">The quantity.</param>
    /// <param name="unitPrice">The unit price.</param>
    public void AddLine(long itemId, decimal quantity, decimal unitPrice) => _lines.Add(new LaybyLine(itemId, quantity, unitPrice));

    /// <summary>Records a payment; completes the plan when it is paid in full.</summary>
    /// <param name="amount">The amount.</param>
    /// <param name="date">The payment date.</param>
    /// <returns>Whether the payment completed the plan.</returns>
    public bool Pay(decimal amount, DateOnly date)
    {
        Paid += amount;
        if (Balance > 0m)
        {
            return false;
        }

        Status = LaybyStatus.Completed;
        ClosedOn = date;
        return true;
    }

    /// <summary>Cancels the plan, keeping the forfeiture.</summary>
    /// <param name="date">The cancellation date.</param>
    /// <returns>The amount forfeited.</returns>
    public decimal Cancel(DateOnly date)
    {
        Forfeited = decimal.Round(Paid * ForfeitPercent / 100m, 2, MidpointRounding.AwayFromZero);
        Status = LaybyStatus.Cancelled;
        ClosedOn = date;
        return Forfeited;
    }
}

/// <summary>Goods set aside on a lay-bye (<c>ar.layby_line</c>).</summary>
public sealed class LaybyLine : Entity
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private LaybyLine()
    {
    }

    /// <summary>Creates a line.</summary>
    /// <param name="itemId">The item.</param>
    /// <param name="quantity">The quantity.</param>
    /// <param name="unitPrice">The unit price.</param>
    public LaybyLine(long itemId, decimal quantity, decimal unitPrice)
    {
        ItemId = itemId;
        Quantity = quantity;
        UnitPrice = unitPrice;
    }

    /// <summary>The plan.</summary>
    public long LaybyPlanId { get; private set; }

    /// <summary>The item.</summary>
    public long ItemId { get; private set; }

    /// <summary>The quantity set aside.</summary>
    public decimal Quantity { get; private set; }

    /// <summary>The unit price agreed.</summary>
    public decimal UnitPrice { get; private set; }
}
