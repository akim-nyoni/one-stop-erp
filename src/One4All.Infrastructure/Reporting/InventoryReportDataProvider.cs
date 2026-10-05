using System.Globalization;
using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Inventory;
using One4All.Application.Abstractions.Reporting;
using One4All.Application.Inventory;
using One4All.Core.Accounting;
using One4All.Core.Inventory;
using One4All.Core.Pos;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Reporting;

/// <summary>Serves the INV-numbered inventory reports (brief §14 E): every data command whose name starts with <c>Stk</c>.</summary>
public sealed class InventoryReportDataProvider : IReportDataProvider
{
    private readonly One4AllDbContext _db;
    private readonly IStockAvailabilityService _availability;

    /// <summary>Creates the provider.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="availability">Stock availability (commitments and orders).</param>
    public InventoryReportDataProvider(One4AllDbContext db, IStockAvailabilityService availability)
    {
        _db = db;
        _availability = availability;
    }

    /// <inheritdoc />
    public bool CanHandle(string command) => command.StartsWith("Stk", StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc />
    public async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> GetRowsAsync(long companyId, string command, IReadOnlyDictionary<string, object?> parameters, CancellationToken ct = default)
    {
        DateOnly today = DateOnly.FromDateTime(DateTime.Today);
        DateOnly asAt = DateParameter(parameters, "AsAtDate") ?? today;
        DateOnly toDate = DateParameter(parameters, "ToDate") ?? today;
        DateOnly fromDate = DateParameter(parameters, "FromDate") ?? new DateOnly(toDate.Year, 1, 1);
        return command.ToUpperInvariant() switch
        {
            "STKVALUATIONVSGL" => await ValuationVsGlAsync(companyId, asAt, ct).ConfigureAwait(false),
            "STKAVAILABILITY" => await AvailabilityAsync(companyId, ct).ConfigureAwait(false),
            "STKSTOCKTAKE" => await StocktakeAsync(companyId, ct).ConfigureAwait(false),
            "STKREASONS" => await ReasonsAsync(companyId, fromDate, toDate, ct).ConfigureAwait(false),
            "STKBATCHES" => await BatchesAsync(companyId, asAt, ct).ConfigureAwait(false),
            "STKSERIALS" => await SerialsAsync(companyId, asAt, ct).ConfigureAwait(false),
            "STKEXPIRY" => await ExpiryAsync(companyId, asAt, parameters.TryGetValue("Days", out object? days) && days is not null and not "" ? Convert.ToInt32(days, CultureInfo.InvariantCulture) : 30, ct)
                .ConfigureAwait(false),
            "STKINTRANSIT" => await InTransitAsync(companyId, asAt, ct).ConfigureAwait(false),
            "STKCONSIGNMENT" => await ConsignmentAsync(companyId, asAt, ct).ConfigureAwait(false),
            "STKBOM" => await BomAsync(companyId, ct).ConfigureAwait(false),
            "STKYIELD" => await YieldAsync(companyId, fromDate, toDate, ct).ConfigureAwait(false),
            "STKOVERSTOCK" => await OverstockAsync(companyId, asAt, ct).ConfigureAwait(false),
            "STKNEGATIVE" => await NegativeAsync(companyId, ct).ConfigureAwait(false),
            "STKTURNOVER" => await TurnoverAsync(companyId, fromDate, toDate, ct).ConfigureAwait(false),
            "STKMARGIN" => await MarginAsync(companyId, fromDate, toDate, ct).ConfigureAwait(false),
            "STKCOSTCHANGES" => await CostChangesAsync(companyId, fromDate, toDate, ct).ConfigureAwait(false),
            "STKLANDEDCOST" => await LandedCostAsync(companyId, fromDate, toDate, ct).ConfigureAwait(false),
            _ => [],
        };
    }

    // The stock register at a date is the sum of movements to it (each movement carries its signed quantity and value).
    private async Task<List<(long ItemId, long WarehouseId, decimal Quantity, decimal Value)>> RegisterAsync(long companyId, DateOnly asAt, CancellationToken ct)
        => [.. (await _db.StockMovements.AsNoTracking().Where(m => m.CompanyId == companyId && m.Date <= asAt).GroupBy(m => new { m.ItemId, m.WarehouseId })
                .Select(g => new { g.Key.ItemId, g.Key.WarehouseId, Quantity = g.Sum(m => m.Quantity), Value = g.Sum(m => m.Value) }).ToListAsync(ct).ConfigureAwait(false))
            .Select(x => (x.ItemId, x.WarehouseId, x.Quantity, x.Value))];

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> ValuationVsGlAsync(long companyId, DateOnly asAt, CancellationToken ct)
    {
        Dictionary<long, string> warehouses = await _db.Warehouses.AsNoTracking().IgnoreQueryFilters().Where(w => w.CompanyId == companyId).ToDictionaryAsync(w => w.Id, w => w.Code, ct).ConfigureAwait(false);
        var register = await RegisterAsync(companyId, asAt, ct).ConfigureAwait(false);
        List<IReadOnlyDictionary<string, object?>> rows = [.. register.GroupBy(r => r.WarehouseId).OrderBy(g => warehouses.GetValueOrDefault(g.Key), StringComparer.Ordinal)
            .Select(g => Row(("Line", $"Warehouse {warehouses.GetValueOrDefault(g.Key, "?")}"), ("Items", g.Count(r => r.Quantity != 0m)), ("Quantity", Qty(g.Sum(r => r.Quantity))), ("Value", g.Sum(r => r.Value))))];
        decimal registerValue = register.Sum(r => r.Value);
        List<string> controls = await _db.Accounts.AsNoTracking().Where(a => a.CompanyId == companyId && a.ControlAccountType == ControlAccountType.Stock).Select(a => a.AccountCode).ToListAsync(ct).ConfigureAwait(false);
        decimal ledger = await (from l in _db.Set<JournalLine>().AsNoTracking() join j in _db.Journals.AsNoTracking() on l.JournalEntryId equals j.Id
                                where j.CompanyId == companyId && (j.Status == JournalStatus.Posted || j.Status == JournalStatus.Reversed) && j.PostingDate <= asAt && controls.Contains(l.AccountCode)
                                select l.BaseDebit - l.BaseCredit).SumAsync(ct).ConfigureAwait(false);
        // The ledger lines carry their amounts in the text, so the footer (the sum of the warehouse values) stays the register total.
        rows.Add(Row(("Line", $"Stock control account(s) {string.Join(", ", controls)}: {Money(ledger)}"), ("Items", "—"), ("Quantity", "—"), ("Value", 0m)));
        rows.Add(Row(("Line", $"Difference (register − ledger): {Money(registerValue - ledger)}"), ("Items", "—"), ("Quantity", "—"), ("Value", 0m)));
        return rows;
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> AvailabilityAsync(long companyId, CancellationToken ct)
    {
        Dictionary<long, string> items = await ItemsAsync(companyId, ct).ConfigureAwait(false);
        Dictionary<long, string> warehouses = await _db.Warehouses.AsNoTracking().IgnoreQueryFilters().Where(w => w.CompanyId == companyId).ToDictionaryAsync(w => w.Id, w => w.Code, ct).ConfigureAwait(false);
        List<StockBalance> balances = await _db.StockBalances.AsNoTracking().Where(b => b.CompanyId == companyId && (b.QuantityOnHand != 0m || b.TotalValue != 0m)).ToListAsync(ct).ConfigureAwait(false);
        List<IReadOnlyDictionary<string, object?>> rows = [];
        foreach (StockBalance balance in balances.OrderBy(b => items.GetValueOrDefault(b.ItemId), StringComparer.Ordinal).ThenBy(b => warehouses.GetValueOrDefault(b.WarehouseId), StringComparer.Ordinal))
        {
            var availability = await _availability.AvailabilityAsync(companyId, balance.ItemId, balance.WarehouseId, ct).ConfigureAwait(false);
            StockAvailability? a = availability.IsSuccess ? availability.Value : null;
            rows.Add(Row(("Item", items.GetValueOrDefault(balance.ItemId, "?")), ("Warehouse", warehouses.GetValueOrDefault(balance.WarehouseId, "?")), ("OnHand", Qty(balance.QuantityOnHand)),
                ("Committed", Qty(a?.AllocatedToOrders ?? 0m)), ("OnOrder", Qty(a?.OnOrder ?? 0m)), ("Free", Qty(a?.Free ?? balance.QuantityOnHand)), ("Atp", Qty(a?.AvailableToPromise ?? balance.QuantityOnHand)),
                ("Value", balance.TotalValue)));
        }

        return rows;
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> StocktakeAsync(long companyId, CancellationToken ct)
    {
        Dictionary<long, string> items = await ItemsAsync(companyId, ct).ConfigureAwait(false);
        Dictionary<long, string> warehouses = await _db.Warehouses.AsNoTracking().IgnoreQueryFilters().Where(w => w.CompanyId == companyId).ToDictionaryAsync(w => w.Id, w => w.Code, ct).ConfigureAwait(false);
        List<StocktakeSession> sessions = await _db.Set<StocktakeSession>().AsNoTracking().Include(s => s.Lines).Where(s => s.CompanyId == companyId)
            .OrderBy(s => s.StocktakeDate).ThenBy(s => s.Reference).ToListAsync(ct).ConfigureAwait(false);
        return [.. sessions.SelectMany(s => s.Lines.OrderBy(l => items.GetValueOrDefault(l.ItemId), StringComparer.Ordinal).Select(l =>
        {
            decimal? variance = l.CountedQuantity - l.SystemQuantity;
            return Row(("Stocktake", s.Reference), ("Date", Date(s.StocktakeDate)), ("Warehouse", warehouses.GetValueOrDefault(s.WarehouseId, "?")), ("Status", s.Status.ToString()),
                ("Item", items.GetValueOrDefault(l.ItemId, "?")), ("System", Qty(l.SystemQuantity)), ("Counted", l.CountedQuantity is decimal c ? Qty(c) : "not counted"),
                ("Variance", variance is decimal v ? Qty(v) : "—"), ("Value", variance is decimal w ? Math.Round(w * l.AverageCost, 2, MidpointRounding.AwayFromZero) : 0m));
        }))];
    }

    // Ceiling: reorder level plus reorder quantity when set; otherwise 90 days of the last 90 days' issues.
    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> OverstockAsync(long companyId, DateOnly asAt, CancellationToken ct)
    {
        List<Item> items = await _db.Items.AsNoTracking().Where(i => i.CompanyId == companyId && i.IsActive).ToListAsync(ct).ConfigureAwait(false);
        var register = (await RegisterAsync(companyId, asAt, ct).ConfigureAwait(false)).GroupBy(r => r.ItemId).ToDictionary(g => g.Key, g => (Quantity: g.Sum(r => r.Quantity), Value: g.Sum(r => r.Value)));
        DateOnly since = asAt.AddDays(-89);
        Dictionary<long, decimal> issued = await _db.StockMovements.AsNoTracking().Where(m => m.CompanyId == companyId && m.Type == StockMovementType.Issue && m.Date >= since && m.Date <= asAt)
            .GroupBy(m => m.ItemId).Select(g => new { g.Key, Qty = g.Sum(m => -m.Quantity) }).ToDictionaryAsync(x => x.Key, x => x.Qty, ct).ConfigureAwait(false);
        return [.. items.Where(i => register.ContainsKey(i.Id)).Select(i =>
            {
                (decimal quantity, decimal value) = register[i.Id];
                decimal daily = issued.GetValueOrDefault(i.Id) / 90m;
                decimal ceiling = i.ReorderLevel + i.ReorderQuantity > 0m ? i.ReorderLevel + i.ReorderQuantity : Math.Round(daily * 90m, 3);
                return (Item: i, Quantity: quantity, Value: value, Ceiling: ceiling, Daily: daily);
            })
            .Where(x => x.Quantity > x.Ceiling && x.Quantity > 0m).OrderByDescending(x => (x.Quantity - x.Ceiling) * (x.Quantity == 0m ? 0m : x.Value / x.Quantity))
            .Select(x => Row(("Item", $"{x.Item.Code} {x.Item.Description}"), ("OnHand", Qty(x.Quantity)), ("Ceiling", Qty(x.Ceiling)), ("Excess", Qty(x.Quantity - x.Ceiling)),
                ("DaysCover", x.Daily == 0m ? "no issues" : Math.Round(x.Quantity / x.Daily).ToString(CultureInfo.InvariantCulture)),
                ("Value", Math.Round((x.Quantity - x.Ceiling) * x.Value / x.Quantity, 2, MidpointRounding.AwayFromZero))))];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> NegativeAsync(long companyId, CancellationToken ct)
    {
        Dictionary<long, string> items = await ItemsAsync(companyId, ct).ConfigureAwait(false);
        Dictionary<long, string> warehouses = await _db.Warehouses.AsNoTracking().IgnoreQueryFilters().Where(w => w.CompanyId == companyId).ToDictionaryAsync(w => w.Id, w => w.Code, ct).ConfigureAwait(false);
        return [.. (await _db.StockBalances.AsNoTracking().Where(b => b.CompanyId == companyId && b.QuantityOnHand < 0m).ToListAsync(ct).ConfigureAwait(false))
            .OrderBy(b => items.GetValueOrDefault(b.ItemId), StringComparer.Ordinal)
            .Select(b => Row(("Item", items.GetValueOrDefault(b.ItemId, "?")), ("Warehouse", warehouses.GetValueOrDefault(b.WarehouseId, "?")), ("OnHand", Qty(b.QuantityOnHand)), ("AverageCost", Money(b.AverageCost)),
                ("Value", b.TotalValue)))];
    }

    // Turns = cost issued ÷ average of opening and closing stock value; days cover = closing value ÷ daily cost issued.
    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> TurnoverAsync(long companyId, DateOnly fromDate, DateOnly toDate, CancellationToken ct)
    {
        Dictionary<long, string> items = await ItemsAsync(companyId, ct).ConfigureAwait(false);
        Dictionary<long, decimal> opening = (await RegisterAsync(companyId, fromDate.AddDays(-1), ct).ConfigureAwait(false)).GroupBy(r => r.ItemId).ToDictionary(g => g.Key, g => g.Sum(r => r.Value));
        Dictionary<long, decimal> closing = (await RegisterAsync(companyId, toDate, ct).ConfigureAwait(false)).GroupBy(r => r.ItemId).ToDictionary(g => g.Key, g => g.Sum(r => r.Value));
        Dictionary<long, decimal> issued = await _db.StockMovements.AsNoTracking().Where(m => m.CompanyId == companyId && m.Type == StockMovementType.Issue && m.Date >= fromDate && m.Date <= toDate)
            .GroupBy(m => m.ItemId).Select(g => new { g.Key, Cost = g.Sum(m => -m.Value) }).ToDictionaryAsync(x => x.Key, x => x.Cost, ct).ConfigureAwait(false);
        int days = Math.Max(1, toDate.DayNumber - fromDate.DayNumber + 1);
        return [.. closing.Keys.Union(issued.Keys).OrderBy(id => items.GetValueOrDefault(id), StringComparer.Ordinal).Select(id =>
        {
            decimal cost = issued.GetValueOrDefault(id), average = (opening.GetValueOrDefault(id) + closing.GetValueOrDefault(id)) / 2m, close = closing.GetValueOrDefault(id);
            return Row(("Item", items.GetValueOrDefault(id, "?")), ("Issued", Money(cost)), ("AverageStock", Money(average)), ("Turns", average == 0m ? "—" : (cost / average).ToString("0.0", CultureInfo.InvariantCulture)),
                ("DaysCover", cost == 0m ? "no issues" : Math.Round(close / (cost / days)).ToString(CultureInfo.InvariantCulture)), ("Value", close));
        })];
    }

    // Sales are completed POS lines and invoiced sales-order lines (orders dated in the period); cost is the issues in the period.
    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> MarginAsync(long companyId, DateOnly fromDate, DateOnly toDate, CancellationToken ct)
    {
        Dictionary<long, Item> items = await _db.Items.AsNoTracking().IgnoreQueryFilters().Where(i => i.CompanyId == companyId).ToDictionaryAsync(i => i.Id, ct).ConfigureAwait(false);
        var pos = await (from l in _db.Set<SaleLine>().AsNoTracking() join s in _db.Sales.AsNoTracking() on l.SaleId equals s.Id
                         where s.CompanyId == companyId && s.Status == SaleStatus.Completed && s.SaleDate >= fromDate && s.SaleDate <= toDate
                         select new { l.ItemId, l.Quantity, l.LineNet }).ToListAsync(ct).ConfigureAwait(false);
        var orders = await (from l in _db.Set<Core.SalesOrders.SalesOrderLine>().AsNoTracking() join o in _db.Set<Core.SalesOrders.SalesOrder>().AsNoTracking() on l.SalesOrderId equals o.Id
                            where o.CompanyId == companyId && o.OrderDate >= fromDate && o.OrderDate <= toDate && l.InvoicedQuantity > 0m
                            select new { l.ItemId, Quantity = l.InvoicedQuantity, LineNet = l.InvoicedQuantity * l.UnitPrice }).ToListAsync(ct).ConfigureAwait(false);
        Dictionary<long, decimal> cost = await _db.StockMovements.AsNoTracking().Where(m => m.CompanyId == companyId && m.Type == StockMovementType.Issue && m.Date >= fromDate && m.Date <= toDate)
            .GroupBy(m => m.ItemId).Select(g => new { g.Key, Cost = g.Sum(m => -m.Value) }).ToDictionaryAsync(x => x.Key, x => x.Cost, ct).ConfigureAwait(false);
        return [.. pos.Select(p => (p.ItemId, p.Quantity, p.LineNet)).Concat(orders.Select(o => (o.ItemId, o.Quantity, o.LineNet))).GroupBy(x => x.ItemId)
            .Select(g => (Id: g.Key, Quantity: g.Sum(x => x.Quantity), Sales: g.Sum(x => x.LineNet), Cost: cost.GetValueOrDefault(g.Key)))
            .OrderByDescending(x => x.Sales - x.Cost)
            .Select(x => Row(("Item", items.TryGetValue(x.Id, out Item? i) ? $"{i.Code} {i.Description}" : "?"), ("Category", items.GetValueOrDefault(x.Id)?.Category ?? "—"), ("Quantity", Qty(x.Quantity)),
                ("Sales", Money(x.Sales)), ("Cost", Money(x.Cost)), ("MarginPercent", x.Sales == 0m ? "—" : ((x.Sales - x.Cost) * 100m / x.Sales).ToString("0.0", CultureInfo.InvariantCulture) + "%"),
                ("Margin", x.Sales - x.Cost)))];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> CostChangesAsync(long companyId, DateOnly fromDate, DateOnly toDate, CancellationToken ct)
    {
        Dictionary<long, string> items = await ItemsAsync(companyId, ct).ConfigureAwait(false);
        List<StockMovement> receipts = await _db.StockMovements.AsNoTracking().Where(m => m.CompanyId == companyId && m.Type == StockMovementType.Receipt && !m.IsReversed && m.Date <= toDate)
            .OrderBy(m => m.Date).ThenBy(m => m.Id).ToListAsync(ct).ConfigureAwait(false);
        Dictionary<long, decimal> last = [];
        List<IReadOnlyDictionary<string, object?>> rows = [];
        foreach (StockMovement receipt in receipts)
        {
            if (receipt.Date >= fromDate && last.TryGetValue(receipt.ItemId, out decimal previous) && previous != receipt.UnitCost)
            {
                rows.Add(Row(("Item", items.GetValueOrDefault(receipt.ItemId, "?")), ("Date", Date(receipt.Date)), ("Reference", receipt.Reference ?? "—"), ("Previous", Money(previous)), ("Cost", Money(receipt.UnitCost)),
                    ("Change", previous == 0m ? "—" : ((receipt.UnitCost - previous) * 100m / previous).ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture) + "%"), ("Quantity", Qty(receipt.Quantity))));
            }

            last[receipt.ItemId] = receipt.UnitCost;
        }

        return rows;
    }

    // Landed-cost movements carry value only; the goods' value is the receipts under the same reference.
    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> LandedCostAsync(long companyId, DateOnly fromDate, DateOnly toDate, CancellationToken ct)
    {
        Dictionary<long, string> items = await ItemsAsync(companyId, ct).ConfigureAwait(false);
        List<StockMovement> landed = await _db.StockMovements.AsNoTracking().Where(m => m.CompanyId == companyId && m.Type == StockMovementType.LandedCost && m.Date >= fromDate && m.Date <= toDate)
            .OrderBy(m => m.Date).ThenBy(m => m.Reference).ToListAsync(ct).ConfigureAwait(false);
        List<long> itemIds = [.. landed.Select(m => m.ItemId).Distinct()];
        List<StockMovement> receipts = await _db.StockMovements.AsNoTracking().Where(m => m.CompanyId == companyId && m.Type == StockMovementType.Receipt && itemIds.Contains(m.ItemId) && m.Date <= toDate)
            .ToListAsync(ct).ConfigureAwait(false);
        return [.. landed.Select(m =>
        {
            decimal goods = receipts.Where(r => r.ItemId == m.ItemId && r.Date <= m.Date).OrderByDescending(r => r.Date).ThenByDescending(r => r.Id).Select(r => r.Value).FirstOrDefault();
            return Row(("Date", Date(m.Date)), ("Reference", m.Reference ?? "—"), ("Item", items.GetValueOrDefault(m.ItemId, "?")), ("Goods", Money(goods)),
                ("Uplift", goods == 0m ? "—" : (m.Value * 100m / goods).ToString("0.0", CultureInfo.InvariantCulture) + "%"), ("Landed", m.Value));
        })];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> ReasonsAsync(long companyId, DateOnly fromDate, DateOnly toDate, CancellationToken ct)
    {
        List<StockMovement> adjustments = await _db.StockMovements.AsNoTracking()
            .Where(m => m.CompanyId == companyId && m.Type == StockMovementType.Adjustment && m.Date >= fromDate && m.Date <= toDate).ToListAsync(ct).ConfigureAwait(false);
        Dictionary<string, string> names = await _db.StockReasonCodes.AsNoTracking().Where(r => r.CompanyId == companyId).ToDictionaryAsync(r => r.Code, r => r.Name, ct).ConfigureAwait(false);
        return [.. adjustments.GroupBy(m => m.ReasonCode ?? string.Empty).OrderBy(g => g.Key.Length == 0).ThenBy(g => g.Key, StringComparer.Ordinal).Select(g => Row(
            ("Reason", g.Key.Length == 0 ? "—" : g.Key), ("Name", g.Key.Length == 0 ? "No reason (stocktake / unspecified)" : names.GetValueOrDefault(g.Key, "?")), ("Count", g.Count()),
            ("Gained", Qty(g.Where(m => m.Quantity > 0m).Sum(m => m.Quantity))), ("Lost", Qty(-g.Where(m => m.Quantity < 0m).Sum(m => m.Quantity))), ("Value", g.Sum(m => m.Value))))];
    }

    private sealed record LotBalance(long ItemId, long WarehouseId, string Lot, decimal OnHand, DateOnly First, DateOnly Last, DateOnly? Expiry);

    private async Task<List<LotBalance>> LotBalancesAsync(long companyId, DateOnly asAt, ItemTracking tracking, CancellationToken ct)
    {
        List<long> tracked = await _db.Items.AsNoTracking().Where(i => i.CompanyId == companyId && i.Tracking == tracking).Select(i => i.Id).ToListAsync(ct).ConfigureAwait(false);
        return [.. (await _db.StockLotMovements.AsNoTracking().Where(l => l.CompanyId == companyId && tracked.Contains(l.ItemId) && l.Date <= asAt)
                .GroupBy(l => new { l.ItemId, l.WarehouseId, l.LotNumber })
                .Select(g => new { g.Key.ItemId, g.Key.WarehouseId, g.Key.LotNumber, OnHand = g.Sum(l => l.Quantity), First = g.Min(l => l.Date), Last = g.Max(l => l.Date), Expiry = g.Max(l => l.ExpiryDate) })
                .ToListAsync(ct).ConfigureAwait(false))
            .Select(g => new LotBalance(g.ItemId, g.WarehouseId, g.LotNumber, g.OnHand, g.First, g.Last, g.Expiry))];
    }

    private async Task<Dictionary<(long, long), decimal>> AverageCostsAsync(long companyId, CancellationToken ct)
        => await _db.StockBalances.AsNoTracking().Where(b => b.CompanyId == companyId).ToDictionaryAsync(b => (b.ItemId, b.WarehouseId), b => b.AverageCost, ct).ConfigureAwait(false);

    private Task<Dictionary<long, string>> WarehousesAsync(long companyId, CancellationToken ct)
        => _db.Warehouses.AsNoTracking().IgnoreQueryFilters().Where(w => w.CompanyId == companyId).ToDictionaryAsync(w => w.Id, w => w.Code, ct);

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> BatchesAsync(long companyId, DateOnly asAt, CancellationToken ct)
    {
        List<LotBalance> lots = await LotBalancesAsync(companyId, asAt, ItemTracking.Batch, ct).ConfigureAwait(false);
        Dictionary<long, string> items = await ItemsAsync(companyId, ct).ConfigureAwait(false);
        Dictionary<long, string> warehouses = await WarehousesAsync(companyId, ct).ConfigureAwait(false);
        Dictionary<(long, long), decimal> costs = await AverageCostsAsync(companyId, ct).ConfigureAwait(false);
        return [.. lots.Where(l => l.OnHand != 0m).OrderBy(l => items.GetValueOrDefault(l.ItemId), StringComparer.Ordinal).ThenBy(l => warehouses.GetValueOrDefault(l.WarehouseId), StringComparer.Ordinal)
            .ThenBy(l => l.Expiry ?? DateOnly.MaxValue).ThenBy(l => l.Lot, StringComparer.Ordinal)
            .Select(l => Row(("Item", items.GetValueOrDefault(l.ItemId, "?")), ("Warehouse", warehouses.GetValueOrDefault(l.WarehouseId, "?")), ("Batch", l.Lot), ("Received", Date(l.First)),
                ("Expiry", l.Expiry is DateOnly e ? Date(e) : "—"), ("OnHand", Qty(l.OnHand)), ("Value", Math.Round(l.OnHand * costs.GetValueOrDefault((l.ItemId, l.WarehouseId)), 2))))];
    }

    // A serial is in stock where its lot quantity nets to one; otherwise it last left from the warehouse of its latest movement.
    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> SerialsAsync(long companyId, DateOnly asAt, CancellationToken ct)
    {
        List<long> tracked = await _db.Items.AsNoTracking().Where(i => i.CompanyId == companyId && i.Tracking == ItemTracking.Serial).Select(i => i.Id).ToListAsync(ct).ConfigureAwait(false);
        var moves = await (from l in _db.StockLotMovements.AsNoTracking()
                           join m in _db.StockMovements.AsNoTracking() on l.StockMovementId equals m.Id
                           where l.CompanyId == companyId && tracked.Contains(l.ItemId) && l.Date <= asAt
                           select new { l.Id, l.ItemId, l.WarehouseId, l.LotNumber, l.Quantity, l.Date, m.Type, m.Reference }).ToListAsync(ct).ConfigureAwait(false);
        Dictionary<long, string> items = await ItemsAsync(companyId, ct).ConfigureAwait(false);
        Dictionary<long, string> warehouses = await WarehousesAsync(companyId, ct).ConfigureAwait(false);
        return [.. moves.GroupBy(m => (m.ItemId, m.LotNumber)).OrderBy(g => items.GetValueOrDefault(g.Key.ItemId), StringComparer.Ordinal).ThenBy(g => g.Key.LotNumber, StringComparer.Ordinal).Select(g =>
        {
            var inStock = g.GroupBy(m => m.WarehouseId).FirstOrDefault(w => w.Sum(m => m.Quantity) > 0m);
            var last = g.OrderBy(m => m.Date).ThenBy(m => m.Id).Last();
            return Row(("Item", items.GetValueOrDefault(g.Key.ItemId, "?")), ("Serial", g.Key.LotNumber), ("Status", inStock is null ? "Out" : "In stock"),
                ("Warehouse", warehouses.GetValueOrDefault(inStock?.Key ?? last.WarehouseId, "?")), ("Received", Date(g.Where(m => m.Quantity > 0m).Select(m => m.Date).DefaultIfEmpty(last.Date).Min())),
                ("LastMoved", Date(last.Date)), ("LastMovement", $"{last.Type} {last.Reference ?? string.Empty}".TrimEnd()));
        })];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> ExpiryAsync(long companyId, DateOnly asAt, int windowDays, CancellationToken ct)
    {
        List<LotBalance> lots = await LotBalancesAsync(companyId, asAt, ItemTracking.Batch, ct).ConfigureAwait(false);
        Dictionary<long, string> items = await ItemsAsync(companyId, ct).ConfigureAwait(false);
        Dictionary<long, string> warehouses = await WarehousesAsync(companyId, ct).ConfigureAwait(false);
        Dictionary<(long, long), decimal> costs = await AverageCostsAsync(companyId, ct).ConfigureAwait(false);
        return [.. lots.Where(l => l.OnHand > 0m && l.Expiry is DateOnly e && LotRules.ExpiryStatus(e, asAt, windowDays) != "OK").OrderBy(l => l.Expiry).ThenBy(l => items.GetValueOrDefault(l.ItemId), StringComparer.Ordinal)
            .Select(l => Row(("Item", items.GetValueOrDefault(l.ItemId, "?")), ("Warehouse", warehouses.GetValueOrDefault(l.WarehouseId, "?")), ("Batch", l.Lot), ("Expiry", Date(l.Expiry!.Value)),
                ("Days", l.Expiry!.Value.DayNumber - asAt.DayNumber), ("Status", LotRules.ExpiryStatus(l.Expiry!.Value, asAt, windowDays)), ("OnHand", Qty(l.OnHand)),
                ("Value", Math.Round(l.OnHand * costs.GetValueOrDefault((l.ItemId, l.WarehouseId)), 2))))];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> BomAsync(long companyId, CancellationToken ct)
    {
        List<BillOfMaterials> boms = await _db.BillsOfMaterials.AsNoTracking().Include(b => b.Lines).Where(b => b.CompanyId == companyId && b.IsActive).OrderBy(b => b.Code).ToListAsync(ct).ConfigureAwait(false);
        Dictionary<long, string> items = await ItemsAsync(companyId, ct).ConfigureAwait(false);
        Dictionary<long, decimal> costs = (await _db.StockBalances.AsNoTracking().Where(b => b.CompanyId == companyId).GroupBy(b => b.ItemId)
                .Select(g => new { g.Key, Quantity = g.Sum(b => b.QuantityOnHand), Value = g.Sum(b => b.TotalValue) }).ToListAsync(ct).ConfigureAwait(false))
            .Where(g => g.Quantity != 0m).ToDictionary(g => g.Key, g => g.Value / g.Quantity);
        List<IReadOnlyDictionary<string, object?>> rows = [];
        foreach (BillOfMaterials bom in boms)
        {
            foreach (BomLine line in bom.Lines.OrderBy(l => items.GetValueOrDefault(l.ComponentItemId), StringComparer.Ordinal))
            {
                decimal perUnit = BomRules.Requirement(line.Quantity, line.ScrapPercent, bom.OutputQuantity, 1m), unitCost = costs.GetValueOrDefault(line.ComponentItemId);
                rows.Add(Row(("Bom", bom.Code), ("Parent", items.GetValueOrDefault(bom.ParentItemId, "?")), ("Component", items.GetValueOrDefault(line.ComponentItemId, "?")), ("PerBatch", Qty(line.Quantity)),
                    ("Scrap", Qty(line.ScrapPercent)), ("PerUnit", Qty(perUnit)), ("UnitCost", Money(unitCost)), ("Extended", Math.Round(perUnit * unitCost, 4))));
            }

            decimal rolled = BomRules.UnitCost(bom.Lines.Select(l => (l.Quantity, l.ScrapPercent, costs.GetValueOrDefault(l.ComponentItemId))), bom.OutputQuantity);
            rows.Add(Row(("Bom", bom.Code), ("Parent", items.GetValueOrDefault(bom.ParentItemId, "?")), ("Component", $"Rolled-up cost per unit: {Money(rolled)} (batch of {Qty(bom.OutputQuantity)})"),
                ("PerBatch", string.Empty), ("Scrap", string.Empty), ("PerUnit", string.Empty), ("UnitCost", string.Empty), ("Extended", 0m)));
        }

        return rows;
    }

    // Standard cost prices the standard quantity at the cost the component was actually consumed at; the variance is usage only.
    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> YieldAsync(long companyId, DateOnly fromDate, DateOnly toDate, CancellationToken ct)
    {
        List<AssemblyRun> runs = await _db.AssemblyRuns.AsNoTracking().Include(r => r.Lines).Where(r => r.CompanyId == companyId && r.Date >= fromDate && r.Date <= toDate)
            .OrderBy(r => r.Date).ThenBy(r => r.RunNumber).ToListAsync(ct).ConfigureAwait(false);
        Dictionary<long, string> items = await ItemsAsync(companyId, ct).ConfigureAwait(false);
        return [.. runs.SelectMany(r => r.Lines.OrderBy(l => items.GetValueOrDefault(l.ComponentItemId), StringComparer.Ordinal).Select(l =>
        {
            decimal unit = l.ActualQuantity == 0m ? 0m : l.Cost / l.ActualQuantity, standardCost = Math.Round(l.StandardQuantity * unit, 2);
            return Row(("Run", r.RunNumber), ("Date", Date(r.Date)), ("Item", items.GetValueOrDefault(r.ParentItemId, "?")),
                ("Yield", $"{Qty(r.PlannedOutput)} → {Qty(r.ActualOutput)} ({BomRules.Yield(r.PlannedOutput, r.ActualOutput)?.ToString("0.0", CultureInfo.InvariantCulture) ?? "—"}%)"),
                ("Component", items.GetValueOrDefault(l.ComponentItemId, "?")), ("Standard", Qty(l.StandardQuantity)), ("Actual", Qty(l.ActualQuantity)), ("StandardCost", Money(standardCost)),
                ("ActualCost", Money(l.Cost)), ("Variance", Math.Round(l.Cost, 2) - standardCost));
        }))];
    }

    // In transit at the date: dispatched by then and not received by then.
    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> InTransitAsync(long companyId, DateOnly asAt, CancellationToken ct)
    {
        List<StockTransfer> transfers = await _db.StockTransfers.AsNoTracking()
            .Where(t => t.CompanyId == companyId && t.DispatchedOn <= asAt && (t.ReceivedOn == null || t.ReceivedOn > asAt)).OrderBy(t => t.DispatchedOn).ThenBy(t => t.TransferNumber)
            .ToListAsync(ct).ConfigureAwait(false);
        Dictionary<long, string> items = await ItemsAsync(companyId, ct).ConfigureAwait(false);
        Dictionary<long, string> warehouses = await WarehousesAsync(companyId, ct).ConfigureAwait(false);
        return [.. transfers.Select(t => Row(("Transfer", t.TransferNumber), ("Item", items.GetValueOrDefault(t.ItemId, "?")), ("From", warehouses.GetValueOrDefault(t.FromWarehouseId, "?")),
            ("To", warehouses.GetValueOrDefault(t.ToWarehouseId, "?")), ("Dispatched", Date(t.DispatchedOn)), ("Days", asAt.DayNumber - t.DispatchedOn.DayNumber), ("Quantity", Qty(t.Quantity)), ("Value", t.Value)))];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> ConsignmentAsync(long companyId, DateOnly asAt, CancellationToken ct)
    {
        Dictionary<long, (string Code, long CustomerId)> sites = await _db.Warehouses.AsNoTracking().Where(w => w.CompanyId == companyId && w.ConsignmentCustomerId != null)
            .ToDictionaryAsync(w => w.Id, w => (w.Code, w.ConsignmentCustomerId!.Value), ct).ConfigureAwait(false);
        List<long> ids = [.. sites.Keys];
        var held = await _db.StockMovements.AsNoTracking().Where(m => m.CompanyId == companyId && ids.Contains(m.WarehouseId) && m.Date <= asAt)
            .GroupBy(m => new { m.WarehouseId, m.ItemId }).Select(g => new { g.Key.WarehouseId, g.Key.ItemId, OnHand = g.Sum(m => m.Quantity), Value = g.Sum(m => m.Value), Last = g.Max(m => m.Date) })
            .ToListAsync(ct).ConfigureAwait(false);
        List<long> customerIds = [.. sites.Values.Select(s => s.CustomerId).Distinct()];
        Dictionary<long, string> customers = await _db.Customers.AsNoTracking().IgnoreQueryFilters().Where(c => customerIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name, ct).ConfigureAwait(false);
        Dictionary<long, string> items = await ItemsAsync(companyId, ct).ConfigureAwait(false);
        return [.. held.Where(h => h.OnHand != 0m).Select(h => (h, Customer: customers.GetValueOrDefault(sites[h.WarehouseId].CustomerId, "?")))
            .OrderBy(x => x.Customer, StringComparer.Ordinal).ThenBy(x => items.GetValueOrDefault(x.h.ItemId), StringComparer.Ordinal)
            .Select(x => Row(("Customer", x.Customer), ("Warehouse", sites[x.h.WarehouseId].Code), ("Item", items.GetValueOrDefault(x.h.ItemId, "?")), ("OnHand", Qty(x.h.OnHand)),
                ("LastMovement", Date(x.h.Last)), ("Value", Math.Round(x.h.Value, 2))))];
    }

    private Task<Dictionary<long, string>> ItemsAsync(long companyId, CancellationToken ct)
        => _db.Items.AsNoTracking().IgnoreQueryFilters().Where(i => i.CompanyId == companyId).ToDictionaryAsync(i => i.Id, i => i.Code + " " + i.Description, ct);

    private static DateOnly? DateParameter(IReadOnlyDictionary<string, object?> parameters, string name) => parameters.TryGetValue(name, out object? value) && value is DateOnly date ? date : null;

    private static string Qty(decimal quantity) => quantity.ToString("0.###", CultureInfo.InvariantCulture);

    private static string Date(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string Money(decimal amount) => amount.ToString("N2", CultureInfo.InvariantCulture);

    private static Dictionary<string, object?> Row(params (string Key, object? Value)[] cells)
    {
        Dictionary<string, object?> row = new(StringComparer.OrdinalIgnoreCase);
        foreach ((string key, object? value) in cells)
        {
            row[key] = value;
        }

        return row;
    }
}
