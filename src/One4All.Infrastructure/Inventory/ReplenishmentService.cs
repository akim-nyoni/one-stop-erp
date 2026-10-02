using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Inventory;
using One4All.Application.Abstractions.Procurement;
using One4All.Core.Common;
using One4All.Core.Inventory;
using One4All.Core.Procurement;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Inventory;

/// <summary>
/// Replenishment: reorder suggestions that net open purchase orders against on-hand stock, and a purchase
/// order raised from them through the procurement service (so it carries the ordinary PO lifecycle).
/// </summary>
public sealed class ReplenishmentService : IReplenishmentService
{
    private readonly One4AllDbContext _db;
    private readonly IProcurementService _procurement;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="procurement">The procurement service (raises the order).</param>
    public ReplenishmentService(One4AllDbContext db, IProcurementService procurement)
    {
        _db = db;
        _procurement = procurement;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ReplenishmentSuggestion>> SuggestAsync(long companyId, long? warehouseId = null, CancellationToken ct = default)
    {
        List<Item> items = await _db.Items
            .Where(i => i.CompanyId == companyId && i.IsActive && i.ReorderLevel > 0m)
            .OrderBy(i => i.Code)
            .ToListAsync(ct).ConfigureAwait(false);
        items = items.Where(i => i.IsStockTracked).ToList();
        if (items.Count == 0)
        {
            return [];
        }

        List<long> ids = items.Select(i => i.Id).ToList();

        IQueryable<StockBalance> balances = _db.Set<StockBalance>().Where(b => b.CompanyId == companyId && ids.Contains(b.ItemId));
        if (warehouseId is { } wh)
        {
            balances = balances.Where(b => b.WarehouseId == wh);
        }

        Dictionary<long, decimal> onHand = await balances
            .GroupBy(b => b.ItemId)
            .Select(g => new { ItemId = g.Key, Qty = g.Sum(b => b.QuantityOnHand) })
            .ToDictionaryAsync(x => x.ItemId, x => x.Qty, ct).ConfigureAwait(false);

        // On order: the un-received balance of lines on open (not cancelled / closed) purchase orders.
        Dictionary<long, decimal> onOrder = await (
            from po in _db.Set<PurchaseOrder>()
            from line in po.Lines
            where po.CompanyId == companyId
                && po.Status != PurchaseOrderStatus.Cancelled
                && po.Status != PurchaseOrderStatus.Closed
                && ids.Contains(line.ItemId)
                && line.OrderedQuantity > line.ReceivedQuantity
            group line by line.ItemId into g
            select new { ItemId = g.Key, Qty = g.Sum(l => l.OrderedQuantity - l.ReceivedQuantity) })
            .ToDictionaryAsync(x => x.ItemId, x => x.Qty, ct).ConfigureAwait(false);

        Dictionary<long, (decimal Price, long SupplierId)> last = await LastPurchasesAsync(companyId, ids, ct).ConfigureAwait(false);

        List<ReplenishmentSuggestion> suggestions = [];
        foreach (Item item in items)
        {
            decimal hand = onHand.GetValueOrDefault(item.Id);
            decimal order = onOrder.GetValueOrDefault(item.Id);
            decimal available = hand + order;
            if (available > item.ReorderLevel)
            {
                continue;
            }

            decimal suggested = item.ReorderQuantity > 0m ? item.ReorderQuantity : item.ReorderLevel - available;
            last.TryGetValue(item.Id, out (decimal Price, long SupplierId) lastBuy);
            suggestions.Add(new ReplenishmentSuggestion(item.Id, item.Code, item.Description, hand, order, available, item.ReorderLevel, suggested,
                lastBuy.Price, last.ContainsKey(item.Id) ? lastBuy.SupplierId : null));
        }

        return suggestions;
    }

    /// <inheritdoc />
    public async Task<Result<long>> RaiseOrderAsync(long companyId, long supplierId, string orderNumber, DateOnly orderDate, IReadOnlyList<ReplenishmentLine> lines, CancellationToken ct = default)
    {
        if (lines.Count == 0)
        {
            return Error.Validation("Replenish.NoLines", "A replenishment order needs at least one line.");
        }

        if (lines.Any(l => l.Quantity <= 0m))
        {
            return Error.Validation("Replenish.Quantity", "Every replenishment quantity must be positive.");
        }

        Supplier? supplier = await _db.Suppliers.FirstOrDefaultAsync(s => s.Id == supplierId && s.CompanyId == companyId, ct).ConfigureAwait(false);
        if (supplier is null)
        {
            return Error.NotFound("Ap.SupplierNotFound", $"Supplier {supplierId} not found.");
        }

        List<long> ids = lines.Select(l => l.ItemId).Distinct().ToList();
        Dictionary<long, Item> items = await _db.Items
            .Where(i => i.CompanyId == companyId && ids.Contains(i.Id))
            .ToDictionaryAsync(i => i.Id, ct).ConfigureAwait(false);
        Dictionary<long, (decimal Price, long SupplierId)> last = await LastPurchasesAsync(companyId, ids, ct).ConfigureAwait(false);

        List<PurchaseOrderLineRequest> poLines = [];
        foreach (ReplenishmentLine line in lines)
        {
            if (!items.TryGetValue(line.ItemId, out Item? item))
            {
                return Error.NotFound("Inv.ItemNotFound", $"Item {line.ItemId} not found.");
            }

            decimal price = line.UnitPrice ?? (last.TryGetValue(item.Id, out (decimal Price, long SupplierId) lastBuy) ? lastBuy.Price : 0m);
            poLines.Add(new PurchaseOrderLineRequest(item.Id, item.Description, line.Quantity, price));
        }

        Result<PurchaseOrderResult> po = await _procurement.CreatePurchaseOrderAsync(new CreatePurchaseOrderRequest
        {
            CompanyId = companyId,
            SupplierId = supplierId,
            OrderNumber = orderNumber,
            OrderDate = orderDate,
            Currency = supplier.Currency,
            Lines = poLines,
        }, ct).ConfigureAwait(false);

        return po.IsFailure ? po.Error : po.Value.PurchaseOrderId;
    }

    // The unit price and supplier on the most recent (by date, then id) purchase order line for each item.
    private async Task<Dictionary<long, (decimal Price, long SupplierId)>> LastPurchasesAsync(long companyId, List<long> itemIds, CancellationToken ct)
    {
        var recent = await (
            from po in _db.Set<PurchaseOrder>()
            from line in po.Lines
            where po.CompanyId == companyId && po.Status != PurchaseOrderStatus.Cancelled && itemIds.Contains(line.ItemId)
            orderby po.OrderDate descending, po.Id descending
            select new { line.ItemId, line.UnitPrice, po.SupplierId })
            .ToListAsync(ct).ConfigureAwait(false);

        Dictionary<long, (decimal Price, long SupplierId)> last = [];
        foreach (var row in recent)
        {
            last.TryAdd(row.ItemId, (row.UnitPrice, row.SupplierId));
        }

        return last;
    }
}
