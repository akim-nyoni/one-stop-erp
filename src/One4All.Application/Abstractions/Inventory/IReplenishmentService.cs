using One4All.Core.Common;

namespace One4All.Application.Abstractions.Inventory;

/// <summary>A replenishment suggestion for one item.</summary>
/// <param name="ItemId">The item.</param>
/// <param name="ItemCode">The item code.</param>
/// <param name="Description">The description.</param>
/// <param name="OnHand">Quantity on hand (all warehouses, or the one asked for).</param>
/// <param name="OnOrder">Quantity on open purchase orders not yet received.</param>
/// <param name="Available">On hand plus on order.</param>
/// <param name="ReorderLevel">The item's reorder level.</param>
/// <param name="SuggestedQuantity">The quantity to order: the item's reorder quantity, or enough to reach the reorder level.</param>
/// <param name="LastUnitPrice">The unit price on the most recent purchase order for the item (0 when never ordered).</param>
/// <param name="LastSupplierId">The supplier on that order (null when never ordered).</param>
/// <param name="MaximumStock">The order-up-to maximum (0: none).</param>
/// <param name="AverageDailyUsage">The moving-average daily usage over the forecast window (0 when not forecast).</param>
public sealed record ReplenishmentSuggestion(long ItemId, string ItemCode, string Description, decimal OnHand, decimal OnOrder, decimal Available, decimal ReorderLevel, decimal SuggestedQuantity, decimal LastUnitPrice, long? LastSupplierId,
    decimal MaximumStock = 0m, decimal AverageDailyUsage = 0m);

/// <summary>An item's replenishment policy.</summary>
/// <param name="ReorderLevel">The minimum: at or below it (on hand plus on order) a reorder is suggested.</param>
/// <param name="ReorderQuantity">A fixed quantity to order (used when there is no maximum).</param>
/// <param name="MaximumStock">Order up to this (0: none).</param>
/// <param name="LeadTimeDays">Days from order to receipt.</param>
/// <param name="ForecastWindowDays">Days of issues the moving-average forecast looks back over (0: none); the reorder point rises to cover the lead time's usage.</param>
public sealed record ReplenishmentPolicy(decimal ReorderLevel, decimal ReorderQuantity, decimal MaximumStock, int LeadTimeDays, int ForecastWindowDays);

/// <summary>An item and quantity to include on a replenishment purchase order.</summary>
/// <param name="ItemId">The item.</param>
/// <param name="Quantity">The quantity to order (positive).</param>
/// <param name="UnitPrice">The unit price; null uses the item's last purchase price.</param>
public readonly record struct ReplenishmentLine(long ItemId, decimal Quantity, decimal? UnitPrice = null);

/// <summary>
/// Stock replenishment (brief §4.2/§4.3): suggests what to reorder — netting open purchase orders against
/// stock on hand so goods already on their way are not ordered twice — and raises a purchase order from the
/// chosen suggestions at each item's last purchase price.
/// </summary>
public interface IReplenishmentService
{
    /// <summary>
    /// Lists the active, stock-tracked items with a reorder level (or a usage forecast) whose available quantity (on hand plus on
    /// open purchase orders) is at or below that level. With a forecast window the level rises to the moving-average daily usage over the
    /// lead time; with a maximum the suggestion orders up to it.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="warehouseId">Optional warehouse to measure on-hand in (on-order is company-wide).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The suggestions, by item code.</returns>
    Task<IReadOnlyList<ReplenishmentSuggestion>> SuggestAsync(long companyId, long? warehouseId = null, CancellationToken ct = default);

    /// <summary>Sets an item's min/max, lead time and forecast window (brief §4.3 replenishment).</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="itemId">The item.</param>
    /// <param name="policy">The policy.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or why the policy is invalid.</returns>
    Task<Result> SetPolicyAsync(long companyId, long itemId, ReplenishmentPolicy policy, CancellationToken ct = default);

    /// <summary>
    /// Raises one purchase order on a supplier for the given items, pricing each line at the given price or,
    /// when none is given, the item's last purchase price. Refuses an empty request, a non-positive quantity,
    /// or an unknown item.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="supplierId">The supplier.</param>
    /// <param name="orderNumber">The order number.</param>
    /// <param name="orderDate">The order date.</param>
    /// <param name="lines">The items and quantities.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The created purchase order id.</returns>
    Task<Result<long>> RaiseOrderAsync(long companyId, long supplierId, string orderNumber, DateOnly orderDate, IReadOnlyList<ReplenishmentLine> lines, CancellationToken ct = default);
}
