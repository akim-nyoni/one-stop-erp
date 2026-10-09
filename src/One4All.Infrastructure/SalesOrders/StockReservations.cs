using Microsoft.EntityFrameworkCore;
using One4All.Core.Inventory;
using One4All.Core.SalesOrders;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.SalesOrders;

/// <summary>Stock free to issue or reserve in a warehouse: on hand less what open sales orders have reserved there.</summary>
internal static class StockReservations
{
    /// <summary>The free quantity of an item in a warehouse.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="companyId">The company.</param>
    /// <param name="itemId">The item.</param>
    /// <param name="warehouseId">The warehouse.</param>
    /// <param name="exceptLineId">A sales-order line whose own reservation counts as free (its despatch), or null.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <param name="onHand">The on-hand quantity when already loaded.</param>
    /// <returns>The free quantity (negative when reservations exceed stock).</returns>
    public static async Task<decimal> FreeAsync(One4AllDbContext db, long companyId, long itemId, long warehouseId, long? exceptLineId, CancellationToken ct, decimal? onHand = null)
    {
        decimal stock = onHand ?? await db.Set<StockBalance>().Where(b => b.CompanyId == companyId && b.ItemId == itemId && b.WarehouseId == warehouseId)
            .SumAsync(b => (decimal?)b.QuantityOnHand, ct).ConfigureAwait(false) ?? 0m;
        decimal reserved = await (
            from order in db.Set<SalesOrder>()
            from line in order.Lines
            where order.CompanyId == companyId && order.Status != SalesOrderStatus.Cancelled && line.ItemId == itemId && line.ReservedWarehouseId == warehouseId
                && line.ReservedQuantity > 0m && (exceptLineId == null || line.Id != exceptLineId)
            select line.ReservedQuantity).SumAsync(q => (decimal?)q, ct).ConfigureAwait(false) ?? 0m;
        return stock - reserved;
    }
}
