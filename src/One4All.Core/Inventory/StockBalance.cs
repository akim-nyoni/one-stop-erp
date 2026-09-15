using One4All.Core.Common;

namespace One4All.Core.Inventory;

/// <summary>
/// The on-hand quantity and moving weighted-average cost of one item in one warehouse (<c>inv.stock_balance</c>,
/// brief §3.6). Receiving stock recomputes the average; issuing draws down at the current average so cost of
/// sales is the weighted-average cost (IAS 2). Value is held to <c>numeric(19,4)</c> and unit cost to
/// <c>numeric(19,6)</c>.
/// </summary>
public sealed class StockBalance : Entity, ICompanyScoped
{
    private const int ValueScale = 4;
    private const int CostScale = 6;

    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private StockBalance()
    {
    }

    /// <summary>Creates a zeroed stock balance.</summary>
    /// <param name="companyId">The owning company.</param>
    /// <param name="itemId">The item.</param>
    /// <param name="warehouseId">The warehouse.</param>
    public StockBalance(long companyId, long itemId, long warehouseId)
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

    /// <summary>The quantity on hand.</summary>
    public decimal QuantityOnHand { get; private set; }

    /// <summary>The moving weighted-average unit cost.</summary>
    public decimal AverageCost { get; private set; }

    /// <summary>The total value on hand (quantity × average cost).</summary>
    public decimal TotalValue { get; private set; }

    /// <summary>
    /// Receives stock at a unit cost, recomputing the moving weighted average.
    /// </summary>
    /// <param name="quantity">The quantity received (positive).</param>
    /// <param name="unitCost">The unit cost of the receipt.</param>
    /// <returns>The value added.</returns>
    public decimal Receive(decimal quantity, decimal unitCost)
    {
        decimal valueIn = decimal.Round(quantity * unitCost, ValueScale, MidpointRounding.ToEven);
        decimal newQty = QuantityOnHand + quantity;
        decimal newValue = TotalValue + valueIn;

        QuantityOnHand = newQty;
        TotalValue = decimal.Round(newValue, ValueScale, MidpointRounding.ToEven);
        AverageCost = newQty == 0m ? 0m : decimal.Round(newValue / newQty, CostScale, MidpointRounding.ToEven);
        return valueIn;
    }

    /// <summary>
    /// Issues stock at the current average cost, returning the cost of the issue.
    /// </summary>
    /// <param name="quantity">The quantity issued (positive).</param>
    /// <returns>The cost of the issue (value removed).</returns>
    public decimal Issue(decimal quantity)
    {
        decimal cost = decimal.Round(quantity * AverageCost, ValueScale, MidpointRounding.ToEven);
        QuantityOnHand -= quantity;
        TotalValue = decimal.Round(QuantityOnHand * AverageCost, ValueScale, MidpointRounding.ToEven);
        return cost;
    }
}
