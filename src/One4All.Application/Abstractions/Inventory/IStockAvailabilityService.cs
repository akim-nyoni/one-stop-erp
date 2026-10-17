using One4All.Core.Common;

namespace One4All.Application.Abstractions.Inventory;

/// <summary>Stock availability of one item.</summary>
/// <param name="ItemId">The item.</param>
/// <param name="ItemCode">The item code.</param>
/// <param name="OnHand">Quantity on hand (all warehouses, or the one asked for).</param>
/// <param name="AllocatedToOrders">Quantity still to despatch on open sales orders (company-wide).</param>
/// <param name="OnOrder">Quantity on open purchase orders not yet received (company-wide).</param>
/// <param name="Free">On hand less allocated: what can be promised from stock now (may be negative when over-committed).</param>
/// <param name="AvailableToPromise">Free plus on order: what can be promised once inbound stock arrives.</param>
public sealed record StockAvailability(long ItemId, string ItemCode, decimal OnHand, decimal AllocatedToOrders, decimal OnOrder, decimal Free, decimal AvailableToPromise);

/// <summary>A sales-order line still awaiting despatch.</summary>
/// <param name="SalesOrderId">The order.</param>
/// <param name="OrderNumber">The order number.</param>
/// <param name="OrderDate">The order date.</param>
/// <param name="CustomerId">The customer.</param>
/// <param name="SalesOrderLineId">The line.</param>
/// <param name="ItemId">The item.</param>
/// <param name="ItemCode">The item code.</param>
/// <param name="Outstanding">Ordered less despatched.</param>
/// <param name="OnHand">The item's quantity on hand.</param>
/// <param name="CanFulfil">True when on-hand (after earlier back-orders for the same item, oldest first) covers the outstanding quantity.</param>
/// <param name="Shortfall">The quantity on-hand cannot cover (0 when fulfillable).</param>
public sealed record BackOrderLine(long SalesOrderId, string OrderNumber, DateOnly OrderDate, long CustomerId, long SalesOrderLineId, long ItemId, string ItemCode, decimal Outstanding, decimal OnHand, bool CanFulfil, decimal Shortfall);

/// <summary>
/// Stock availability (brief §4.2/§4.13): what is free to promise after open sales-order commitments, and
/// the back-order book - every undespatched order line with whether stock on hand can fulfil it, allocating
/// on-hand to back-orders oldest-first so the view is honest about who gets served.
/// </summary>
public interface IStockAvailabilityService
{
    /// <summary>Computes the availability of one item.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="itemId">The item.</param>
    /// <param name="warehouseId">Optional warehouse to measure on-hand in.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The availability, or not-found.</returns>
    Task<Result<StockAvailability>> AvailabilityAsync(long companyId, long itemId, long? warehouseId = null, CancellationToken ct = default);

    /// <summary>Lists every undespatched line on non-cancelled sales orders, oldest order first.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The back-order lines.</returns>
    Task<IReadOnlyList<BackOrderLine>> BackOrdersAsync(long companyId, CancellationToken ct = default);
}
