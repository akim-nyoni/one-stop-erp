using Microsoft.EntityFrameworkCore;
using One4All.Core.Inventory;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Inventory;

/// <summary>
/// An issue costed but not yet applied: its cost is known before the journal posts, and <see cref="Apply"/> takes the
/// stock from the balance (and, for a FIFO item, from its cost layers) only once it has.
/// </summary>
/// <param name="Balance">The stock balance issued from.</param>
/// <param name="Quantity">The quantity issued.</param>
/// <param name="Cost">The cost of the issue.</param>
/// <param name="Fifo">The FIFO layers drawn on; null for an average-cost item.</param>
internal sealed record StockIssue(StockBalance Balance, decimal Quantity, decimal Cost, FifoIssuePlan? Fifo)
{
    /// <summary>The issue's unit cost.</summary>
    public decimal UnitCost => Quantity == 0m ? 0m : decimal.Round(Cost / Quantity, 6, MidpointRounding.ToEven);

    /// <summary>Takes the stock.</summary>
    public void Apply()
    {
        if (Fifo is null)
        {
            Balance.Issue(Quantity);
            return;
        }

        Fifo.Apply();
        Balance.IssueAtCost(Quantity, Cost);
    }
}

/// <summary>
/// Values stock movements by the item's costing method (brief §3.6): moving weighted average, or FIFO, where each receipt
/// opens a cost layer and issues consume the oldest layers first. Every path that receives or issues stock goes through
/// here, so a FIFO item's layers always add up to its stock balance.
/// </summary>
internal static class StockCosting
{
    /// <summary>Whether the item is costed FIFO.</summary>
    /// <param name="item">The item.</param>
    /// <returns>True for FIFO.</returns>
    public static bool IsFifo(Item item) => item.CostingMethod == CostingMethod.Fifo;

    /// <summary>Costs an issue without changing anything.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="item">The item.</param>
    /// <param name="balance">The balance issued from.</param>
    /// <param name="quantity">The quantity.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The costed issue.</returns>
    public static async Task<StockIssue> PlanIssueAsync(One4AllDbContext db, Item item, StockBalance balance, decimal quantity, CancellationToken ct)
    {
        if (!IsFifo(item))
        {
            return new StockIssue(balance, quantity, balance.CostOfIssue(quantity), null);
        }

        List<StockCostLayer> layers = await OpenLayersAsync(db, balance.CompanyId, item.Id, balance.WarehouseId, ct).ConfigureAwait(false);
        FifoIssuePlan plan = FifoIssuePlan.For(layers, quantity, balance.AverageCost);
        return new StockIssue(balance, quantity, plan.Cost, plan);
    }

    /// <summary>Receives stock at a value, opening a cost layer for a FIFO item.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="item">The item.</param>
    /// <param name="balance">The balance received into.</param>
    /// <param name="date">The layer date: when the stock was received.</param>
    /// <param name="reference">The receipt reference.</param>
    /// <param name="quantity">The quantity.</param>
    /// <param name="value">The value received.</param>
    public static void Receive(One4AllDbContext db, Item item, StockBalance balance, DateOnly date, string? reference, decimal quantity, decimal value)
    {
        if (!IsFifo(item))
        {
            balance.ReceiveAtValue(quantity, value);
            return;
        }

        // Stock taken below zero was issued at the last known cost; the first units received settle it, and the
        // difference stays in the new layer so the layers still add up to the balance.
        bool wasNegative = balance.QuantityOnHand < 0m;
        balance.ReceiveAtValue(quantity, value);
        decimal layerQuantity = wasNegative ? balance.QuantityOnHand : quantity;
        if (layerQuantity > 0m)
        {
            db.Set<StockCostLayer>().Add(new StockCostLayer(balance.CompanyId, item.Id, balance.WarehouseId, date, reference, layerQuantity, wasNegative ? balance.TotalValue : value));
        }
    }

    /// <summary>The open layers of an item in a warehouse, oldest first.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="companyId">The company.</param>
    /// <param name="itemId">The item.</param>
    /// <param name="warehouseId">The warehouse.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The layers with stock left.</returns>
    public static Task<List<StockCostLayer>> OpenLayersAsync(One4AllDbContext db, long companyId, long itemId, long warehouseId, CancellationToken ct)
        => db.Set<StockCostLayer>()
            .Where(l => l.CompanyId == companyId && l.ItemId == itemId && l.WarehouseId == warehouseId && l.RemainingQuantity > 0m)
            .OrderBy(l => l.LayerDate).ThenBy(l => l.Id)
            .ToListAsync(ct);
}
