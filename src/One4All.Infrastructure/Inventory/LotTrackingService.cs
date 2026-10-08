using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Inventory;
using One4All.Application.Inventory;
using One4All.Core.Common;
using One4All.Core.Inventory;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Inventory;

/// <summary>Records batches and serials against stock movements and picks lots first-expiry-first-out.</summary>
public sealed class LotTrackingService : ILotTrackingService
{
    private readonly One4AllDbContext _db;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    public LotTrackingService(One4AllDbContext db) => _db = db;

    /// <inheritdoc />
    public async Task<Result> RecordAsync(long companyId, long stockMovementId, IReadOnlyList<LotQuantity> lots, CancellationToken ct = default)
    {
        StockMovement? movement = await _db.StockMovements.AsNoTracking().FirstOrDefaultAsync(m => m.Id == stockMovementId && m.CompanyId == companyId, ct).ConfigureAwait(false);
        if (movement is null)
        {
            return Result.Failure(Error.NotFound("Inv.MovementNotFound", $"Stock movement {stockMovementId} not found."));
        }

        if (movement.Quantity == 0m)
        {
            return Result.Failure(Error.Rule("Inv.NoQuantity", "The movement moved no units."));
        }

        Item? item = await _db.Items.AsNoTracking().FirstOrDefaultAsync(i => i.Id == movement.ItemId, ct).ConfigureAwait(false);
        if (item is null)
        {
            return Result.Failure(Error.NotFound("Inv.ItemNotFound", "The movement's item no longer exists."));
        }

        if (LotRules.Validate(item.Tracking, Math.Abs(movement.Quantity), lots) is { } invalid)
        {
            return Result.Failure(invalid);
        }

        if (await _db.StockLotMovements.AnyAsync(l => l.StockMovementId == stockMovementId, ct).ConfigureAwait(false))
        {
            return Result.Failure(Error.Conflict("Inv.LotsAlreadyRecorded", "Lots are already recorded for this movement."));
        }

        List<string> numbers = [.. lots.Select(l => l.LotNumber.Trim())];
        var onHand = await _db.StockLotMovements.AsNoTracking().Where(l => l.CompanyId == companyId && l.ItemId == movement.ItemId && numbers.Contains(l.LotNumber))
            .GroupBy(l => new { l.LotNumber, l.WarehouseId }).Select(g => new { g.Key.LotNumber, g.Key.WarehouseId, OnHand = g.Sum(l => l.Quantity), Expiry = g.Max(l => l.ExpiryDate) })
            .ToListAsync(ct).ConfigureAwait(false);
        bool inbound = movement.Quantity > 0m;
        foreach (LotQuantity lot in lots)
        {
            string number = lot.LotNumber.Trim();
            decimal here = onHand.Where(o => o.LotNumber == number && o.WarehouseId == movement.WarehouseId).Sum(o => o.OnHand);
            if (inbound && item.Tracking == ItemTracking.Serial && movement.Type != StockMovementType.Transfer && onHand.Any(o => o.LotNumber == number && o.OnHand > 0m))
            {
                return Result.Failure(Error.Conflict("Inv.SerialInStock", $"Serial {number} is already in stock."));
            }

            if (!inbound && here < lot.Quantity)
            {
                return Result.Failure(Error.Rule("Inv.LotShort", $"{(item.Tracking == ItemTracking.Serial ? "Serial" : "Batch")} {number} has {here:0.###} in this warehouse; {lot.Quantity:0.###} cannot leave."));
            }

            DateOnly? known = onHand.Where(o => o.LotNumber == number).Select(o => o.Expiry).Max();
            DateOnly? expiry = inbound ? LotRules.Expiry(lot.ExpiryDate ?? known, movement.Date, item.ShelfLifeDays) : known;
            _db.StockLotMovements.Add(new StockLotMovement(companyId, movement.Id, movement.ItemId, movement.WarehouseId, movement.Date, number, inbound ? lot.Quantity : -lot.Quantity, expiry)
            {
                UnitCost = inbound ? movement.UnitCost : null,
            });
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<(IReadOnlyList<LotQuantity> Picks, decimal Shortfall)> SuggestPicksAsync(long companyId, long itemId, long warehouseId, decimal quantity, DateOnly asAt, CancellationToken ct = default)
    {
        List<LotOnHand> lots = [.. (await _db.StockLotMovements.AsNoTracking().Where(l => l.CompanyId == companyId && l.ItemId == itemId && l.WarehouseId == warehouseId)
                .GroupBy(l => l.LotNumber).Select(g => new { g.Key, OnHand = g.Sum(l => l.Quantity), Expiry = g.Max(l => l.ExpiryDate), First = g.Min(l => l.Date) })
                .ToListAsync(ct).ConfigureAwait(false))
            .Select(g => new LotOnHand(g.Key, g.OnHand, g.Expiry, g.First))];
        return LotRules.Fefo(lots, quantity, asAt);
    }

    /// <inheritdoc />
    public async Task<Result> RecordPartialAsync(long companyId, long stockMovementId, IReadOnlyList<LotQuantity> lots, CancellationToken ct = default)
    {
        StockMovement? movement = await _db.StockMovements.AsNoTracking().FirstOrDefaultAsync(m => m.Id == stockMovementId && m.CompanyId == companyId, ct).ConfigureAwait(false);
        if (movement is null)
        {
            return Result.Failure(Error.NotFound("Inv.MovementNotFound", $"Stock movement {stockMovementId} not found."));
        }

        foreach (LotQuantity lot in lots.Where(l => l.Quantity > 0m))
        {
            DateOnly? expiry = await _db.StockLotMovements.Where(l => l.CompanyId == companyId && l.ItemId == movement.ItemId && l.LotNumber == lot.LotNumber).MaxAsync(l => l.ExpiryDate, ct)
                .ConfigureAwait(false);
            _db.StockLotMovements.Add(new StockLotMovement(companyId, movement.Id, movement.ItemId, movement.WarehouseId, movement.Date, lot.LotNumber.Trim(),
                movement.Quantity > 0m ? lot.Quantity : -lot.Quantity, expiry) { UnitCost = movement.Quantity > 0m ? movement.UnitCost : null });
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result<LotTrace>> TraceAsync(long companyId, long itemId, string lotNumber, CancellationToken ct = default)
    {
        string number = (lotNumber ?? string.Empty).Trim();
        var moves = await (from lot in _db.StockLotMovements.AsNoTracking()
                           join movement in _db.StockMovements.AsNoTracking() on lot.StockMovementId equals movement.Id
                           where lot.CompanyId == companyId && lot.ItemId == itemId && lot.LotNumber == number
                           orderby lot.Date, lot.Id
                           select new { lot.Date, movement.Reference, lot.Quantity, lot.WarehouseId, lot.ExpiryDate }).ToListAsync(ct).ConfigureAwait(false);
        if (moves.Count == 0)
        {
            return Error.NotFound("Inv.LotNotFound", $"No movements of lot {number} for item {itemId}.");
        }

        // Parties: a GRN's supplier for what came in; a till sale's or an invoice's customer for what went out.
        List<string> references = [.. moves.Select(m => m.Reference).OfType<string>().Select(r => r.Split(' ')[0]).Distinct()];
        Dictionary<string, string> suppliers = await (from grn in _db.Set<Core.Procurement.GoodsReceivedNote>().AsNoTracking()
                                                      join supplier in _db.Suppliers.AsNoTracking() on grn.SupplierId equals supplier.Id
                                                      where grn.CompanyId == companyId && references.Contains(grn.GrnNumber)
                                                      select new { grn.GrnNumber, supplier.Name }).ToDictionaryAsync(x => x.GrnNumber, x => x.Name, ct).ConfigureAwait(false);
        Dictionary<string, string> saleCustomers = await (from sale in _db.Set<Core.Pos.Sale>().AsNoTracking()
                                                          join customer in _db.Customers.AsNoTracking() on sale.CustomerId equals customer.Id into joined
                                                          from customer in joined.DefaultIfEmpty()
                                                          where sale.CompanyId == companyId && references.Contains(sale.SaleNumber)
                                                          select new { sale.SaleNumber, Name = customer != null ? customer.Name : "Cash sale" })
            .ToDictionaryAsync(x => x.SaleNumber, x => x.Name, ct).ConfigureAwait(false);
        Dictionary<string, string> invoiceCustomers = (await (from item in _db.OpenItems.AsNoTracking()
                                                              join customer in _db.Customers.AsNoTracking() on item.TraderId equals customer.Id
                                                              where item.CompanyId == companyId && item.Ledger == Core.Trading.TradingLedger.AccountsReceivable && references.Contains(item.DocumentNumber)
                                                              select new { item.DocumentNumber, customer.Name }).ToListAsync(ct).ConfigureAwait(false))
            .GroupBy(x => x.DocumentNumber).ToDictionary(g => g.Key, g => g.First().Name);
        string? Party(string? reference)
        {
            string key = reference?.Split(' ')[0] ?? string.Empty;
            return suppliers.GetValueOrDefault(key) ?? saleCustomers.GetValueOrDefault(key) ?? invoiceCustomers.GetValueOrDefault(key);
        }

        List<LotTraceMovement> all = [.. moves.Select(m => new LotTraceMovement(m.Date, m.Reference, m.Quantity, m.WarehouseId, Party(m.Reference)))];
        return new LotTrace(itemId, number, moves.Max(m => m.ExpiryDate), [.. all.Where(m => m.Quantity > 0m)], [.. all.Where(m => m.Quantity < 0m)],
            moves.GroupBy(m => m.WarehouseId).Where(g => g.Sum(m => m.Quantity) != 0m).ToDictionary(g => g.Key, g => g.Sum(m => m.Quantity)));
    }
}
