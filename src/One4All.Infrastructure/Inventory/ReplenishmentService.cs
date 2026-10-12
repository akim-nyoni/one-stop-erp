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
        // A warehouse's own reorder levels override the item's company-wide ones there.
        Dictionary<long, ItemWarehouseLevel> levels = warehouseId is long wid
            ? await _db.ItemWarehouseLevels.Where(l => l.CompanyId == companyId && l.WarehouseId == wid).ToDictionaryAsync(l => l.ItemId, ct).ConfigureAwait(false)
            : [];
        List<long> levelled = [.. levels.Keys];
        List<Item> items = await _db.Items
            .Where(i => i.CompanyId == companyId && i.IsActive && (i.ReorderLevel > 0m || i.ForecastWindowDays > 0 || levelled.Contains(i.Id)))
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
        Dictionary<long, decimal> usage = await DailyUsageAsync(companyId, warehouseId, items.Where(i => i.ForecastWindowDays > 0).ToList(), ct).ConfigureAwait(false);

        List<ReplenishmentSuggestion> suggestions = [];
        // The preferred supplier on the item's supplier links wins over the last one bought from.
        Dictionary<long, ItemSupplier> preferred = await _db.ItemSuppliers.Where(l => l.CompanyId == companyId && ids.Contains(l.ItemId) && l.IsPreferred).ToDictionaryAsync(l => l.ItemId, ct)
            .ConfigureAwait(false);
        foreach (Item item in items)
        {
            decimal level = levels.TryGetValue(item.Id, out ItemWarehouseLevel? own) ? own.ReorderLevel : item.ReorderLevel;
            decimal reorderQuantity = own?.ReorderQuantity ?? item.ReorderQuantity;
            // The moving average, scaled by the season of the month the lead time ends in.
            decimal daily = usage.GetValueOrDefault(item.Id) * SeasonalFactors.For(item.SeasonalFactors, DateOnly.FromDateTime(DateTime.Today).AddDays(item.LeadTimeDays));

            // The forecast reorder point: the moving-average usage over the lead time (never below the set minimum).
            level = Math.Max(level, Math.Ceiling(daily * item.LeadTimeDays));
            decimal hand = onHand.GetValueOrDefault(item.Id);
            decimal order = onOrder.GetValueOrDefault(item.Id);
            decimal available = hand + order;
            if (level <= 0m || available > level)
            {
                continue;
            }

            decimal suggested = item.MaximumStock > available ? item.MaximumStock - available : reorderQuantity > 0m ? reorderQuantity : level - available;
            last.TryGetValue(item.Id, out (decimal Price, long SupplierId) lastBuy);
            long? supplier = preferred.TryGetValue(item.Id, out ItemSupplier? link) ? link.SupplierId : last.ContainsKey(item.Id) ? lastBuy.SupplierId : null;
            decimal price = link?.LastCost ?? lastBuy.Price;
            suggestions.Add(new ReplenishmentSuggestion(item.Id, item.Code, item.Description, hand, order, available, level, suggested, price, supplier, item.MaximumStock, Math.Round(daily, 4)));
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

    /// <inheritdoc />
    public async Task<Result> SetPolicyAsync(long companyId, long itemId, ReplenishmentPolicy policy, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(policy);
        if (policy.ReorderLevel < 0m || policy.ReorderQuantity < 0m || policy.MaximumStock < 0m || policy.LeadTimeDays < 0 || policy.ForecastWindowDays < 0)
        {
            return Result.Failure(Error.Validation("Replenish.PolicyNegative", "Replenishment levels, lead time and forecast window cannot be negative."));
        }

        if (policy.MaximumStock > 0m && policy.MaximumStock <= policy.ReorderLevel)
        {
            return Result.Failure(Error.Validation("Replenish.MaxBelowMin", $"The maximum ({policy.MaximumStock:0.###}) must be above the reorder level ({policy.ReorderLevel:0.###})."));
        }

        if (policy.ForecastWindowDays > 0 && policy.LeadTimeDays == 0)
        {
            return Result.Failure(Error.Validation("Replenish.LeadTime", "A usage forecast needs the lead time it has to cover."));
        }

        if (!SeasonalFactors.TryParse(policy.SeasonalFactors, out decimal[]? factors))
        {
            return Result.Failure(Error.Validation("Replenish.Seasonal", "Seasonal factors are twelve numbers from 0 to 99, January first, separated by commas."));
        }

        Item? item = await _db.Items.FirstOrDefaultAsync(i => i.Id == itemId && i.CompanyId == companyId, ct).ConfigureAwait(false);
        if (item is null)
        {
            return Result.Failure(Error.NotFound("Inv.ItemNotFound", $"Item {itemId} not found."));
        }

        item.ReorderLevel = policy.ReorderLevel;
        item.ReorderQuantity = policy.ReorderQuantity;
        item.MaximumStock = policy.MaximumStock;
        item.LeadTimeDays = policy.LeadTimeDays;
        item.ForecastWindowDays = policy.ForecastWindowDays;
        item.SeasonalFactors = factors is null ? null : SeasonalFactors.Format(factors);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    // Moving-average daily usage: the quantity issued (reversed issues excluded) over each item's window up to yesterday, per day.
    private async Task<Dictionary<long, decimal>> DailyUsageAsync(long companyId, long? warehouseId, List<Item> forecast, CancellationToken ct)
    {
        if (forecast.Count == 0)
        {
            return [];
        }

        DateOnly today = DateOnly.FromDateTime(DateTime.Today);
        DateOnly earliest = today.AddDays(-forecast.Max(i => i.ForecastWindowDays));
        List<long> ids = [.. forecast.Select(i => i.Id)];
        IQueryable<StockMovement> issues = _db.StockMovements.Where(m => m.CompanyId == companyId && ids.Contains(m.ItemId) && m.Type == StockMovementType.Issue && !m.IsReversed
            && m.Date >= earliest && m.Date < today);
        if (warehouseId is long wh)
        {
            issues = issues.Where(m => m.WarehouseId == wh);
        }

        var rows = await issues.Select(m => new { m.ItemId, m.Date, m.Quantity }).ToListAsync(ct).ConfigureAwait(false);
        Dictionary<long, decimal> usage = [];
        foreach (Item item in forecast)
        {
            DateOnly from = today.AddDays(-item.ForecastWindowDays);
            decimal issued = -rows.Where(r => r.ItemId == item.Id && r.Date >= from).Sum(r => r.Quantity);
            usage[item.Id] = Math.Max(issued, 0m) / item.ForecastWindowDays;
        }

        return usage;
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
