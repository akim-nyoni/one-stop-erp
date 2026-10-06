using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Inventory;
using One4All.Core.Common;
using One4All.Core.Inventory;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Inventory;

/// <summary>Bins, put-away, moves, picks and per-warehouse reorder levels (brief §4.2).</summary>
public sealed class BinService : IBinService
{
    private const string Unbinned = "UNBINNED";
    private readonly One4AllDbContext _db;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    public BinService(One4AllDbContext db) => _db = db;

    /// <inheritdoc />
    public async Task<Result<long>> AddBinAsync(long companyId, long warehouseId, string code, int pickSequence, decimal? capacityUnits, CancellationToken ct = default)
    {
        string key = (code ?? string.Empty).Trim().ToUpperInvariant();
        if (key.Length == 0 || key == Unbinned || capacityUnits <= 0m)
        {
            return Error.Validation("Inv.BinInvalid", "A bin needs a code and, if it has one, a positive capacity.");
        }

        if (!await _db.Warehouses.AnyAsync(w => w.Id == warehouseId && w.CompanyId == companyId, ct).ConfigureAwait(false))
        {
            return Error.NotFound("Inv.WarehouseNotFound", $"Warehouse {warehouseId} not found.");
        }

        if (await _db.WarehouseBins.AnyAsync(b => b.WarehouseId == warehouseId && b.Code == key, ct).ConfigureAwait(false))
        {
            return Error.Conflict("Inv.BinExists", $"Bin {key} already exists in the warehouse.");
        }

        WarehouseBin bin = new(companyId, warehouseId, key, pickSequence, capacityUnits);
        _db.WarehouseBins.Add(bin);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return bin.Id;
    }

    /// <inheritdoc />
    public async Task<Result> PutAwayAsync(long companyId, long warehouseId, string binCode, long itemId, decimal quantity, CancellationToken ct = default)
    {
        Result<WarehouseBin> bin = await BinAsync(companyId, warehouseId, binCode, ct).ConfigureAwait(false);
        if (bin.IsFailure || quantity <= 0m)
        {
            return Result.Failure(bin.IsFailure ? bin.Error : Error.Validation("Inv.QuantityInvalid", "The quantity must be positive."));
        }

        decimal onHand = await _db.StockBalances.Where(b => b.WarehouseId == warehouseId && b.ItemId == itemId).SumAsync(b => (decimal?)b.QuantityOnHand, ct).ConfigureAwait(false) ?? 0m;
        decimal binned = await (from s in _db.BinStocks join b in _db.WarehouseBins on s.BinId equals b.Id where b.WarehouseId == warehouseId && s.ItemId == itemId select (decimal?)s.Quantity)
            .SumAsync(ct).ConfigureAwait(false) ?? 0m;
        if (quantity > onHand - binned)
        {
            return Result.Failure(Error.Rule("Inv.NotEnoughUnbinned", $"Only {onHand - binned:0.##} of the item are in the warehouse and not yet in a bin."));
        }

        Result fits = await FitsAsync(bin.Value, quantity, ct).ConfigureAwait(false);
        if (fits.IsFailure)
        {
            return fits;
        }

        (await StockAsync(bin.Value.Id, itemId, ct).ConfigureAwait(false)).Change(quantity);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result> MoveAsync(long companyId, long warehouseId, string fromBin, string toBin, long itemId, decimal quantity, CancellationToken ct = default)
    {
        Result<WarehouseBin> from = await BinAsync(companyId, warehouseId, fromBin, ct).ConfigureAwait(false);
        Result<WarehouseBin> to = await BinAsync(companyId, warehouseId, toBin, ct).ConfigureAwait(false);
        if (from.IsFailure || to.IsFailure)
        {
            return Result.Failure(from.IsFailure ? from.Error : to.Error);
        }

        BinStock source = await StockAsync(from.Value.Id, itemId, ct).ConfigureAwait(false);
        if (quantity <= 0m || quantity > source.Quantity)
        {
            return Result.Failure(Error.Rule("Inv.NotInBin", $"Bin {from.Value.Code} holds {source.Quantity:0.##} of the item."));
        }

        Result fits = await FitsAsync(to.Value, quantity, ct).ConfigureAwait(false);
        if (fits.IsFailure)
        {
            return fits;
        }

        source.Change(-quantity);
        (await StockAsync(to.Value.Id, itemId, ct).ConfigureAwait(false)).Change(quantity);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result> TakeAsync(long companyId, long warehouseId, string binCode, long itemId, decimal quantity, CancellationToken ct = default)
    {
        Result<WarehouseBin> bin = await BinAsync(companyId, warehouseId, binCode, ct).ConfigureAwait(false);
        if (bin.IsFailure)
        {
            return Result.Failure(bin.Error);
        }

        BinStock stock = await StockAsync(bin.Value.Id, itemId, ct).ConfigureAwait(false);
        if (quantity <= 0m || quantity > stock.Quantity)
        {
            return Result.Failure(Error.Rule("Inv.NotInBin", $"Bin {bin.Value.Code} holds {stock.Quantity:0.##} of the item."));
        }

        stock.Change(-quantity);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PickStop>> PickRouteAsync(long companyId, long warehouseId, IReadOnlyList<(long ItemId, decimal Quantity)> wanted, CancellationToken ct = default)
    {
        List<long> ids = [.. wanted.Select(w => w.ItemId)];
        var stock = await (from s in _db.BinStocks
                           join b in _db.WarehouseBins on s.BinId equals b.Id
                           where b.CompanyId == companyId && b.WarehouseId == warehouseId && ids.Contains(s.ItemId) && s.Quantity > 0m
                           select new { b.Code, b.PickSequence, s.ItemId, s.Quantity }).ToListAsync(ct).ConfigureAwait(false);
        List<PickStop> stops = [];
        foreach ((long itemId, decimal quantity) in wanted)
        {
            decimal left = quantity;
            foreach (var bin in stock.Where(s => s.ItemId == itemId).OrderBy(s => s.PickSequence).ThenBy(s => s.Code, StringComparer.Ordinal))
            {
                if (left <= 0m)
                {
                    break;
                }

                decimal take = Math.Min(left, bin.Quantity);
                stops.Add(new PickStop(bin.Code, bin.PickSequence, itemId, take));
                left -= take;
            }

            if (left > 0m)
            {
                stops.Add(new PickStop(Unbinned, int.MaxValue, itemId, left));
            }
        }

        return [.. stops.OrderBy(s => s.PickSequence).ThenBy(s => s.BinCode, StringComparer.Ordinal)];
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<BinContent>> ContentsAsync(long companyId, long warehouseId, CancellationToken ct = default)
        => [.. (await (from s in _db.BinStocks
                       join b in _db.WarehouseBins on s.BinId equals b.Id
                       join i in _db.Items on s.ItemId equals i.Id
                       where b.CompanyId == companyId && b.WarehouseId == warehouseId && s.Quantity != 0m
                       select new { b.Code, b.PickSequence, ItemCode = i.Code, s.Quantity }).ToListAsync(ct).ConfigureAwait(false))
            .OrderBy(x => x.PickSequence).ThenBy(x => x.Code, StringComparer.Ordinal).ThenBy(x => x.ItemCode, StringComparer.Ordinal)
            .Select(x => new BinContent(x.Code, x.PickSequence, x.ItemCode, x.Quantity))];

    /// <inheritdoc />
    public async Task<Result> SetWarehouseLevelAsync(long companyId, long itemId, long warehouseId, decimal reorderLevel, decimal reorderQuantity, CancellationToken ct = default)
    {
        if (reorderLevel < 0m || reorderQuantity < 0m)
        {
            return Result.Failure(Error.Validation("Inv.LevelInvalid", "Reorder level and quantity cannot be negative."));
        }

        ItemWarehouseLevel? level = await _db.ItemWarehouseLevels.FirstOrDefaultAsync(l => l.ItemId == itemId && l.WarehouseId == warehouseId, ct).ConfigureAwait(false);
        if (level is null)
        {
            level = new ItemWarehouseLevel(companyId, itemId, warehouseId);
            _db.ItemWarehouseLevels.Add(level);
        }

        level.ReorderLevel = reorderLevel;
        level.ReorderQuantity = reorderQuantity;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    private async Task<Result<WarehouseBin>> BinAsync(long companyId, long warehouseId, string code, CancellationToken ct)
    {
        string key = (code ?? string.Empty).Trim().ToUpperInvariant();
        WarehouseBin? bin = await _db.WarehouseBins.FirstOrDefaultAsync(b => b.CompanyId == companyId && b.WarehouseId == warehouseId && b.Code == key, ct).ConfigureAwait(false);
        return bin is null ? Error.NotFound("Inv.BinNotFound", $"No bin '{key}' in the warehouse.") : bin;
    }

    private async Task<Result> FitsAsync(WarehouseBin bin, decimal quantity, CancellationToken ct)
    {
        if (bin.CapacityUnits is not decimal capacity)
        {
            return Result.Success();
        }

        decimal held = await _db.BinStocks.Where(s => s.BinId == bin.Id).SumAsync(s => (decimal?)s.Quantity, ct).ConfigureAwait(false) ?? 0m;
        return held + quantity <= capacity ? Result.Success() : Result.Failure(Error.Rule("Inv.BinFull", $"Bin {bin.Code} has room for {capacity - held:0.##} more units."));
    }

    private async Task<BinStock> StockAsync(long binId, long itemId, CancellationToken ct)
    {
        BinStock? stock = _db.BinStocks.Local.FirstOrDefault(s => s.BinId == binId && s.ItemId == itemId)
            ?? await _db.BinStocks.FirstOrDefaultAsync(s => s.BinId == binId && s.ItemId == itemId, ct).ConfigureAwait(false);
        if (stock is null)
        {
            stock = new BinStock(binId, itemId);
            _db.BinStocks.Add(stock);
        }

        return stock;
    }
}
