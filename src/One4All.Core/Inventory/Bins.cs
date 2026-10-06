using One4All.Core.Common;

namespace One4All.Core.Inventory;

/// <summary>A bin location in a warehouse (<c>inv.bin</c>, brief §4.2): where it sits on the pick walk and how many units it holds.</summary>
public sealed class WarehouseBin : Entity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private WarehouseBin() => Code = string.Empty;

    /// <summary>Creates the bin.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="warehouseId">The warehouse.</param>
    /// <param name="code">The bin code (aisle-bay-level).</param>
    /// <param name="pickSequence">Its place on the pick walk.</param>
    /// <param name="capacityUnits">The most units it holds (null: no limit).</param>
    public WarehouseBin(long companyId, long warehouseId, string code, int pickSequence, decimal? capacityUnits)
    {
        CompanyId = companyId;
        WarehouseId = warehouseId;
        Code = code.Trim().ToUpperInvariant();
        PickSequence = pickSequence;
        CapacityUnits = capacityUnits;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The warehouse.</summary>
    public long WarehouseId { get; private set; }

    /// <summary>The bin code.</summary>
    public string Code { get; private set; }

    /// <summary>Gets or sets its place on the pick walk.</summary>
    public int PickSequence { get; set; }

    /// <summary>Gets or sets the most units it holds.</summary>
    public decimal? CapacityUnits { get; set; }
}

/// <summary>How much of an item sits in a bin (<c>inv.bin_stock</c>); never more than the warehouse holds in total.</summary>
public sealed class BinStock : Entity
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private BinStock()
    {
    }

    /// <summary>Creates the record.</summary>
    /// <param name="binId">The bin.</param>
    /// <param name="itemId">The item.</param>
    public BinStock(long binId, long itemId)
    {
        BinId = binId;
        ItemId = itemId;
    }

    /// <summary>The bin.</summary>
    public long BinId { get; private set; }

    /// <summary>The item.</summary>
    public long ItemId { get; private set; }

    /// <summary>The quantity.</summary>
    public decimal Quantity { get; private set; }

    /// <summary>Moves units in (positive) or out (negative).</summary>
    /// <param name="quantity">The change.</param>
    public void Change(decimal quantity) => Quantity += quantity;
}

/// <summary>Reorder level and quantity for an item at one warehouse, overriding the item's company-wide settings there.</summary>
public sealed class ItemWarehouseLevel : Entity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private ItemWarehouseLevel()
    {
    }

    /// <summary>Creates the levels.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="itemId">The item.</param>
    /// <param name="warehouseId">The warehouse.</param>
    public ItemWarehouseLevel(long companyId, long itemId, long warehouseId)
    {
        CompanyId = companyId;
        ItemId = itemId;
        WarehouseId = warehouseId;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The item.</summary>
    public long ItemId { get; private set; }

    /// <summary>The warehouse.</summary>
    public long WarehouseId { get; private set; }

    /// <summary>Gets or sets the reorder level.</summary>
    public decimal ReorderLevel { get; set; }

    /// <summary>Gets or sets the reorder quantity.</summary>
    public decimal ReorderQuantity { get; set; }
}
