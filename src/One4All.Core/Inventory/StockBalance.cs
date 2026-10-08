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
    /// Adds value without quantity — landed charges capitalised onto stock on hand — and re-derives the average cost.
    /// </summary>
    /// <param name="amount">The value added.</param>
    public void AddValue(decimal amount)
    {
        TotalValue = decimal.Round(TotalValue + amount, ValueScale, MidpointRounding.ToEven);
        AverageCost = QuantityOnHand == 0m ? 0m : decimal.Round(TotalValue / QuantityOnHand, CostScale, MidpointRounding.ToEven);
    }

    /// <summary>Receives stock at an exact value (a FIFO layer's, say), recomputing the average.</summary>
    /// <param name="quantity">The quantity received.</param>
    /// <param name="value">The value received.</param>
    public void ReceiveAtValue(decimal quantity, decimal value)
    {
        decimal newValue = TotalValue + value;
        QuantityOnHand += quantity;
        TotalValue = decimal.Round(newValue, ValueScale, MidpointRounding.ToEven);
        AverageCost = QuantityOnHand == 0m ? 0m : decimal.Round(newValue / QuantityOnHand, CostScale, MidpointRounding.ToEven);
    }

    /// <summary>Issues stock at a cost worked out elsewhere (from FIFO layers), recomputing the average of what is left.</summary>
    /// <param name="quantity">The quantity issued.</param>
    /// <param name="cost">The cost of the issue.</param>
    public void IssueAtCost(decimal quantity, decimal cost)
    {
        QuantityOnHand -= quantity;
        TotalValue = decimal.Round(TotalValue - cost, ValueScale, MidpointRounding.ToEven);
        AverageCost = QuantityOnHand == 0m ? 0m : decimal.Round(TotalValue / QuantityOnHand, CostScale, MidpointRounding.ToEven);
    }

    /// <summary>The weight on hand of a catch-weight item (in its weight unit).</summary>
    public decimal WeightOnHand { get; private set; }

    /// <summary>The value of one weight unit on hand (catch-weight items).</summary>
    public decimal CostPerWeight => WeightOnHand == 0m ? 0m : decimal.Round(TotalValue / WeightOnHand, CostScale, MidpointRounding.ToEven);

    /// <summary>Moves the weight on hand.</summary>
    /// <param name="weight">The signed weight (+ in, − out).</param>
    public void AdjustWeight(decimal weight) => WeightOnHand = decimal.Round(WeightOnHand + weight, 4, MidpointRounding.ToEven);

    /// <summary>The cost an issue of <paramref name="quantity"/> would carry at the current average, without issuing it.</summary>
    /// <param name="quantity">The quantity to issue (positive).</param>
    /// <returns>The cost <see cref="Issue"/> would return.</returns>
    public decimal CostOfIssue(decimal quantity) => decimal.Round(quantity * AverageCost, ValueScale, MidpointRounding.ToEven);

    /// <summary>
    /// Issues stock at the current average cost, returning the cost of the issue.
    /// </summary>
    /// <param name="quantity">The quantity issued (positive).</param>
    /// <returns>The cost of the issue (value removed).</returns>
    public decimal Issue(decimal quantity)
    {
        decimal cost = CostOfIssue(quantity);
        QuantityOnHand -= quantity;
        TotalValue = decimal.Round(QuantityOnHand * AverageCost, ValueScale, MidpointRounding.ToEven);
        return cost;
    }
}
