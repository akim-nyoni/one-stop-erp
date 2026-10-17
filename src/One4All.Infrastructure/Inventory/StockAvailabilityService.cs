using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Inventory;
using One4All.Core.Common;
using One4All.Core.Inventory;
using One4All.Core.Procurement;
using One4All.Core.SalesOrders;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Inventory;

/// <summary>
/// Available-to-promise and the back-order book, read from stock balances, open sales-order lines and open
/// purchase-order lines. Pure read model - nothing here moves stock.
/// </summary>
public sealed class StockAvailabilityService : IStockAvailabilityService
{
    private readonly One4AllDbContext _db;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    public StockAvailabilityService(One4AllDbContext db) => _db = db;

    /// <inheritdoc />
    public async Task<Result<StockAvailability>> AvailabilityAsync(long companyId, long itemId, long? warehouseId = null, CancellationToken ct = default)
    {
        Item? item = await _db.Items.FirstOrDefaultAsync(i => i.Id == itemId && i.CompanyId == companyId, ct).ConfigureAwait(false);
        if (item is null)
        {
            return Error.NotFound("Inv.ItemNotFound", $"Item {itemId} not found.");
        }

        IQueryable<StockBalance> balances = _db.Set<StockBalance>().Where(b => b.CompanyId == companyId && b.ItemId == itemId);
        if (warehouseId is { } wh)
        {
            balances = balances.Where(b => b.WarehouseId == wh);
        }
        else
        {
            // Goods held for quality inspection are not available until released.
            balances = balances.Where(b => !_db.Set<Warehouse>().Any(w => w.Id == b.WarehouseId && w.IsQuarantine));
        }

        decimal onHand = await balances.SumAsync(b => (decimal?)b.QuantityOnHand, ct).ConfigureAwait(false) ?? 0m;

        decimal allocated = await (
            from so in _db.Set<SalesOrder>()
            from line in so.Lines
            where so.CompanyId == companyId && so.Status != SalesOrderStatus.Cancelled
                && line.ItemId == itemId && line.OrderedQuantity > line.DespatchedQuantity
            select line.OrderedQuantity - line.DespatchedQuantity)
            .SumAsync(q => (decimal?)q, ct).ConfigureAwait(false) ?? 0m;

        // Goods set aside on open lay-byes are as good as sold.
        allocated += await (
            from plan in _db.Set<LaybyPlan>()
            from line in plan.Lines
            where plan.CompanyId == companyId && plan.Status == LaybyStatus.Open && line.ItemId == itemId && (warehouseId == null || plan.WarehouseId == warehouseId)
            select line.Quantity)
            .SumAsync(q => (decimal?)q, ct).ConfigureAwait(false) ?? 0m;

        decimal onOrder = await (
            from po in _db.Set<PurchaseOrder>()
            from line in po.Lines
            where po.CompanyId == companyId
                && po.Status != PurchaseOrderStatus.Cancelled
                && po.Status != PurchaseOrderStatus.Closed
                && line.ItemId == itemId && line.OrderedQuantity > line.ReceivedQuantity
            select line.OrderedQuantity - line.ReceivedQuantity)
            .SumAsync(q => (decimal?)q, ct).ConfigureAwait(false) ?? 0m;

        decimal free = onHand - allocated;
        return new StockAvailability(item.Id, item.Code, onHand, allocated, onOrder, free, free + onOrder);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<BackOrderLine>> BackOrdersAsync(long companyId, CancellationToken ct = default)
    {
        var outstanding = await (
            from so in _db.Set<SalesOrder>()
            from line in so.Lines
            where so.CompanyId == companyId && so.Status != SalesOrderStatus.Cancelled
                && line.OrderedQuantity > line.DespatchedQuantity
            orderby so.OrderDate, so.Id, line.Id
            select new
            {
                so.Id,
                so.OrderNumber,
                so.OrderDate,
                so.CustomerId,
                LineId = line.Id,
                line.ItemId,
                Outstanding = line.OrderedQuantity - line.DespatchedQuantity,
            })
            .ToListAsync(ct).ConfigureAwait(false);

        if (outstanding.Count == 0)
        {
            return [];
        }

        List<long> itemIds = outstanding.Select(o => o.ItemId).Distinct().ToList();
        Dictionary<long, string> codes = await _db.Items
            .Where(i => i.CompanyId == companyId && itemIds.Contains(i.Id))
            .ToDictionaryAsync(i => i.Id, i => i.Code, ct).ConfigureAwait(false);
        Dictionary<long, decimal> onHand = await _db.Set<StockBalance>()
            .Where(b => b.CompanyId == companyId && itemIds.Contains(b.ItemId))
            .GroupBy(b => b.ItemId)
            .Select(g => new { ItemId = g.Key, Qty = g.Sum(b => b.QuantityOnHand) })
            .ToDictionaryAsync(x => x.ItemId, x => x.Qty, ct).ConfigureAwait(false);

        // Allocate on-hand to back-orders oldest-first, so a later order is only "fulfillable" from what is left.
        Dictionary<long, decimal> remaining = new(onHand);
        List<BackOrderLine> lines = [];
        foreach (var o in outstanding)
        {
            decimal left = remaining.GetValueOrDefault(o.ItemId);
            decimal covered = Math.Min(left, o.Outstanding);
            decimal shortfall = o.Outstanding - covered;
            remaining[o.ItemId] = left - covered;

            lines.Add(new BackOrderLine(o.Id, o.OrderNumber, o.OrderDate, o.CustomerId, o.LineId, o.ItemId,
                codes.GetValueOrDefault(o.ItemId, string.Empty), o.Outstanding, onHand.GetValueOrDefault(o.ItemId), shortfall == 0m, shortfall));
        }

        return lines;
    }
}
