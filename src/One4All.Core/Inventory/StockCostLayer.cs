using One4All.Core.Common;

namespace One4All.Core.Inventory;

/// <summary>
/// A FIFO cost layer (<c>inv.stock_cost_layer</c>): one receipt of a FIFO-costed item into a warehouse, with what is left
/// of it. Issues consume the oldest layers first, so stock on hand is valued at the most recent costs. Layers carry their
/// remaining value rather than a unit cost, so consuming a whole layer removes exactly its value and capitalised landed
/// charges can be added to it.
/// </summary>
public sealed class StockCostLayer : Entity, ICompanyScoped
{
    private const int ValueScale = 4;

    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private StockCostLayer()
    {
    }

    /// <summary>Creates a layer for a receipt.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="itemId">The item.</param>
    /// <param name="warehouseId">The warehouse.</param>
    /// <param name="layerDate">When the stock was received; layers are consumed oldest first.</param>
    /// <param name="reference">The receipt reference (a goods received note number, say).</param>
    /// <param name="quantity">The quantity received.</param>
    /// <param name="value">Its value.</param>
    public StockCostLayer(long companyId, long itemId, long warehouseId, DateOnly layerDate, string? reference, decimal quantity, decimal value)
    {
        CompanyId = companyId;
        ItemId = itemId;
        WarehouseId = warehouseId;
        LayerDate = layerDate;
        Reference = reference;
        ReceivedQuantity = quantity;
        RemainingQuantity = quantity;
        RemainingValue = value;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The item.</summary>
    public long ItemId { get; private set; }

    /// <summary>The warehouse.</summary>
    public long WarehouseId { get; private set; }

    /// <summary>When the stock was received.</summary>
    public DateOnly LayerDate { get; private set; }

    /// <summary>The receipt reference.</summary>
    public string? Reference { get; private set; }

    /// <summary>The quantity received.</summary>
    public decimal ReceivedQuantity { get; private set; }

    /// <summary>The quantity not yet issued.</summary>
    public decimal RemainingQuantity { get; private set; }

    /// <summary>The value of the quantity not yet issued.</summary>
    public decimal RemainingValue { get; private set; }

    /// <summary>The cost an issue of part of this layer carries: all its value for all of it, else pro rata.</summary>
    /// <param name="quantity">The quantity taken, at most what remains.</param>
    /// <returns>The cost.</returns>
    public decimal CostOf(decimal quantity) => quantity >= RemainingQuantity
        ? RemainingValue
        : decimal.Round(RemainingValue * quantity / RemainingQuantity, ValueScale, MidpointRounding.ToEven);

    /// <summary>Takes stock from the layer.</summary>
    /// <param name="quantity">The quantity taken, at most what remains.</param>
    /// <returns>The cost taken.</returns>
    public decimal Take(decimal quantity)
    {
        decimal cost = CostOf(quantity);
        RemainingQuantity -= Math.Min(quantity, RemainingQuantity);
        RemainingValue = RemainingQuantity == 0m ? 0m : RemainingValue - cost;
        return cost;
    }

    /// <summary>Adds value without quantity, such as landed charges capitalised onto what remains.</summary>
    /// <param name="amount">The value added.</param>
    public void AddValue(decimal amount) => RemainingValue += amount;
}
