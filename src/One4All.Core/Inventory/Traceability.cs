using One4All.Core.Common;

namespace One4All.Core.Inventory;

/// <summary>How an item's units are traced (brief §14 E 88–90).</summary>
public enum ItemTracking
{
    /// <summary>Not traced below item level.</summary>
    None = 0,

    /// <summary>Traced by batch / lot number, with an optional expiry date.</summary>
    Batch = 1,

    /// <summary>Each unit traced by its own serial number.</summary>
    Serial = 2,
}

/// <summary>A stock adjustment reason (damaged, expired, theft, count correction…) that adjustments are analysed by (<c>inv.reason_code</c>).</summary>
public sealed class StockReasonCode : Entity, ICompanyScoped
{
    private StockReasonCode()
    {
        Code = string.Empty;
        Name = string.Empty;
    }

    /// <summary>Creates a reason code.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="code">The code.</param>
    /// <param name="name">What it means.</param>
    public StockReasonCode(long companyId, string code, string name)
    {
        CompanyId = companyId;
        Code = code;
        Name = name;
        IsActive = true;
    }

    /// <summary>Gets the company.</summary>
    public long CompanyId { get; private set; }

    /// <summary>Gets the code.</summary>
    public string Code { get; private set; }

    /// <summary>Gets or sets what it means.</summary>
    public string Name { get; set; }

    /// <summary>Gets or sets a value indicating whether the code can still be used.</summary>
    public bool IsActive { get; set; }
}

/// <summary>
/// The batch or serial numbers behind one stock movement (<c>inv.lot_movement</c>): signed like the movement (+ in, − out),
/// so the on-hand per lot is the sum per item, warehouse and lot.
/// </summary>
public sealed class StockLotMovement : Entity, ICompanyScoped
{
    private StockLotMovement()
    {
        LotNumber = string.Empty;
    }

    /// <summary>Records lot quantities against a movement.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="stockMovementId">The costing movement.</param>
    /// <param name="itemId">The item.</param>
    /// <param name="warehouseId">The warehouse.</param>
    /// <param name="date">The movement date.</param>
    /// <param name="lotNumber">The batch or serial number.</param>
    /// <param name="quantity">The signed quantity.</param>
    /// <param name="expiryDate">The batch's expiry, when it has one.</param>
    public StockLotMovement(long companyId, long stockMovementId, long itemId, long warehouseId, DateOnly date, string lotNumber, decimal quantity, DateOnly? expiryDate)
    {
        CompanyId = companyId;
        StockMovementId = stockMovementId;
        ItemId = itemId;
        WarehouseId = warehouseId;
        Date = date;
        LotNumber = lotNumber;
        Quantity = quantity;
        ExpiryDate = expiryDate;
    }

    /// <summary>Gets the company.</summary>
    public long CompanyId { get; private set; }

    /// <summary>Gets the costing movement.</summary>
    public long StockMovementId { get; private set; }

    /// <summary>Gets the item.</summary>
    public long ItemId { get; private set; }

    /// <summary>Gets the warehouse.</summary>
    public long WarehouseId { get; private set; }

    /// <summary>Gets the movement date.</summary>
    public DateOnly Date { get; private set; }

    /// <summary>Gets the batch or serial number.</summary>
    public string LotNumber { get; private set; }

    /// <summary>Gets the signed quantity.</summary>
    public decimal Quantity { get; private set; }

    /// <summary>Gets the batch's expiry date.</summary>
    public DateOnly? ExpiryDate { get; private set; }

    /// <summary>The unit cost the batch or serial came in at (on receipts): what a specifically identified issue of it costs.</summary>
    public decimal? UnitCost { get; set; }
}
