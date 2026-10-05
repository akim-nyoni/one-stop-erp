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
            _db.StockLotMovements.Add(new StockLotMovement(companyId, movement.Id, movement.ItemId, movement.WarehouseId, movement.Date, number, inbound ? lot.Quantity : -lot.Quantity, expiry));
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
}
