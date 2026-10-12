using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Inventory;
using One4All.Application.Abstractions.Pos;
using One4All.Core.Common;
using One4All.Core.Inventory;
using One4All.Core.Pos;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Pos;

/// <summary>The till's tiles from the item master and the default price list: all of them, a server search, the favourites, or a category and its sub-categories.</summary>
public sealed class PosCatalogueService : IPosCatalogueService
{
    private const int MaxCostPlusLookups = 1_000;

    private readonly One4AllDbContext _db;
    private readonly IPricingService _pricing;
    private readonly IItemCatalogueService? _items;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="pricing">Prices items a cost-plus list does not list explicitly.</param>
    /// <param name="items">Searches the item master (aliases, pack barcodes, synonyms).</param>
    public PosCatalogueService(One4AllDbContext db, IPricingService pricing, IItemCatalogueService? items = null)
    {
        _db = db;
        _pricing = pricing;
        _items = items;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PosTile>> TilesAsync(long companyId, CancellationToken ct = default)
        => await PriceAsync(companyId, await Sellable(companyId).OrderBy(i => i.Category).ThenBy(i => i.Description).ToListAsync(ct).ConfigureAwait(false), ct).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<IReadOnlyList<PosTile>> SearchAsync(long companyId, string term, int take = 30, CancellationToken ct = default)
    {
        if (_items is null || string.IsNullOrWhiteSpace(term))
        {
            return [];
        }

        List<long> ids = [.. (await _items.SearchAsync(companyId, term, Math.Clamp(take, 1, 200), ct).ConfigureAwait(false)).Select(h => h.ItemId)];
        Dictionary<long, Item> found = await Sellable(companyId).Where(i => ids.Contains(i.Id)).ToDictionaryAsync(i => i.Id, ct).ConfigureAwait(false);
        return await PriceAsync(companyId, [.. ids.Where(found.ContainsKey).Select(id => found[id])], ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PosTile>> FindAsync(long companyId, PosFinderFilter filter, DateOnly asAt, int take = 60, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        IQueryable<Item> items = Sellable(companyId);
        if (!string.IsNullOrWhiteSpace(filter.Category))
        {
            // The category and everything under it.
            List<(string Code, string? Parent)> tree = [.. (await _db.ItemCategories.AsNoTracking().Where(c => c.CompanyId == companyId).Select(c => new { c.Code, c.ParentCode })
                .ToListAsync(ct).ConfigureAwait(false)).Select(c => (c.Code, c.ParentCode))];
            HashSet<string> under = new(StringComparer.OrdinalIgnoreCase) { filter.Category.Trim() };
            for (bool grew = true; grew;)
            {
                grew = false;
                foreach ((string code, string? parent) in tree.Where(c => c.Parent is not null && under.Contains(c.Parent) && !under.Contains(c.Code)))
                {
                    grew |= under.Add(code);
                }
            }

            List<string> categories = [.. under.Select(c => c.ToUpperInvariant())];
            items = items.Where(i => i.Category != null && categories.Contains(i.Category.ToUpper()));
        }

        if (!string.IsNullOrWhiteSpace(filter.Brand))
        {
            string brand = filter.Brand.Trim().ToUpperInvariant();
            items = items.Where(i => i.Brand != null && i.Brand.ToUpper() == brand);
        }

        if (!string.IsNullOrWhiteSpace(filter.SupplierCode))
        {
            string supplier = filter.SupplierCode.Trim().ToUpperInvariant();
            IQueryable<long> supplied = from s in _db.Set<ItemSupplier>()
                                        join v in _db.Suppliers on s.SupplierId equals v.Id
                                        where s.CompanyId == companyId && v.Code == supplier
                                        select s.ItemId;
            items = items.Where(i => supplied.Contains(i.Id));
        }

        if (filter.InStockOnly)
        {
            items = items.Where(i => _db.StockBalances.Any(b => b.ItemId == i.Id && b.QuantityOnHand > 0m));
        }

        if (filter.SoldWithinDays is int days and > 0)
        {
            DateOnly since = asAt.AddDays(-days);
            IQueryable<long> sold = _db.Sales.Where(s => s.CompanyId == companyId && s.Status == SaleStatus.Completed && s.SaleDate >= since && s.SaleDate <= asAt)
                .SelectMany(s => s.Lines).Select(l => l.ItemId);
            items = items.Where(i => sold.Contains(i.Id));
        }

        if (filter.PromotionOnly)
        {
            var running = await _db.Promotions.AsNoTracking().Include(p => p.Items)
                .Where(p => p.CompanyId == companyId && p.IsActive && p.StartsOn <= asAt && (p.EndsOn == null || p.EndsOn >= asAt)).ToListAsync(ct).ConfigureAwait(false);
            List<long> promoted = [.. running.SelectMany(p => p.Items).Select(i => i.ItemId).Distinct()];
            List<string> promotedCategories = [.. running.Where(p => p.Category != null).Select(p => p.Category!.ToUpperInvariant()).Distinct()];
            items = items.Where(i => promoted.Contains(i.Id) || (i.Category != null && promotedCategories.Contains(i.Category.ToUpper())));
        }

        List<Item> found = await items.OrderBy(i => i.Description).Take(Math.Clamp(take, 1, 500) * 4).ToListAsync(ct).ConfigureAwait(false);
        return [.. (await PriceAsync(companyId, found, ct).ConfigureAwait(false))
            .Where(t => (filter.MinPrice is not decimal min || t.UnitPrice >= min) && (filter.MaxPrice is not decimal max || t.UnitPrice <= max))
            .Take(Math.Clamp(take, 1, 500))];
    }

    /// <inheritdoc />
    public async Task<(IReadOnlyList<string> Brands, IReadOnlyList<string> Suppliers)> FinderChoicesAsync(long companyId, CancellationToken ct = default)
    {
        List<string> brands = await Sellable(companyId).Where(i => i.Brand != null && i.Brand != string.Empty).Select(i => i.Brand!).Distinct().OrderBy(b => b).ToListAsync(ct).ConfigureAwait(false);
        List<string> suppliers = await (from s in _db.Set<ItemSupplier>().AsNoTracking()
                                        join v in _db.Suppliers.AsNoTracking() on s.SupplierId equals v.Id
                                        where s.CompanyId == companyId
                                        select v.Code).Distinct().OrderBy(c => c).ToListAsync(ct).ConfigureAwait(false);
        return (brands, suppliers);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PosTile>> FavouritesAsync(long companyId, long? terminalId, CancellationToken ct = default)
    {
        // The till's own favourites first, then the ones for every till; an item pinned twice shows once.
        List<long> ids = [.. (await _db.PosFavourites.AsNoTracking().Where(f => f.CompanyId == companyId && f.UserId == null && (f.TerminalId == null || f.TerminalId == terminalId))
                .ToListAsync(ct).ConfigureAwait(false))
            .OrderBy(f => f.TerminalId is null ? 1 : 0).ThenBy(f => f.SortOrder).Select(f => f.ItemId).Distinct()];
        Dictionary<long, Item> found = await Sellable(companyId).Where(i => ids.Contains(i.Id)).ToDictionaryAsync(i => i.Id, ct).ConfigureAwait(false);
        return await PriceAsync(companyId, [.. ids.Where(found.ContainsKey).Select(id => found[id])], ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<Result> SetFavouriteAsync(long companyId, long? terminalId, string itemCode, int sortOrder, bool remove, CancellationToken ct = default)
    {
        string code = (itemCode ?? string.Empty).Trim().ToUpperInvariant();
        long? itemId = await _db.Items.Where(i => i.CompanyId == companyId && i.Code == code).Select(i => (long?)i.Id).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (itemId is null)
        {
            return Result.Failure(Error.NotFound("Pos.ItemNotFound", $"Item {code} not found."));
        }

        if (terminalId is long terminal && !await _db.PosTerminals.AnyAsync(t => t.Id == terminal && t.CompanyId == companyId, ct).ConfigureAwait(false))
        {
            return Result.Failure(Error.NotFound("Pos.TerminalNotFound", $"Terminal {terminal} not found."));
        }

        return await SaveFavouriteAsync(companyId, terminalId, null, itemId.Value, sortOrder, 1, null, remove, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<Result> SetFavouriteAsync(long companyId, long? terminalId, long? userId, string itemCode, int sortOrder, int page, string? colour, bool remove,
        CancellationToken ct = default)
    {
        string code = (itemCode ?? string.Empty).Trim().ToUpperInvariant();
        long? itemId = await _db.Items.Where(i => i.CompanyId == companyId && i.Code == code).Select(i => (long?)i.Id).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (itemId is null)
        {
            return Result.Failure(Error.NotFound("Pos.ItemNotFound", $"Item {code} not found."));
        }

        if (page < 1)
        {
            return Result.Failure(Error.Validation("Pos.FavouritePage", "Favourite pages start at 1."));
        }

        if (terminalId is long terminal && !await _db.PosTerminals.AnyAsync(t => t.Id == terminal && t.CompanyId == companyId, ct).ConfigureAwait(false))
        {
            return Result.Failure(Error.NotFound("Pos.TerminalNotFound", $"Terminal {terminal} not found."));
        }

        return await SaveFavouriteAsync(companyId, terminalId, userId, itemId.Value, sortOrder, page, string.IsNullOrWhiteSpace(colour) ? null : colour.Trim(), remove, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<FavouriteTile>> FavouriteTilesAsync(long companyId, long? terminalId, long? userId, CancellationToken ct = default)
    {
        // The cashier's own first, then the till's, then every till's; an item pinned twice shows once, as the nearest pin has it.
        List<PosFavourite> pins = [.. (await _db.PosFavourites.AsNoTracking()
                .Where(f => f.CompanyId == companyId && (f.TerminalId == null || f.TerminalId == terminalId) && (f.UserId == null || f.UserId == userId)).ToListAsync(ct).ConfigureAwait(false))
            .OrderBy(f => f.UserId is null ? 1 : 0).ThenBy(f => f.TerminalId is null ? 1 : 0).DistinctBy(f => f.ItemId)
            .OrderBy(f => f.Page).ThenBy(f => f.UserId is null ? 1 : 0).ThenBy(f => f.SortOrder)];
        List<long> ids = [.. pins.Select(f => f.ItemId)];
        Dictionary<long, Item> found = await Sellable(companyId).Where(i => ids.Contains(i.Id)).ToDictionaryAsync(i => i.Id, ct).ConfigureAwait(false);
        Dictionary<long, PosTile> tiles = (await PriceAsync(companyId, [.. ids.Where(found.ContainsKey).Select(id => found[id])], ct).ConfigureAwait(false)).ToDictionary(t => t.ItemId);
        return [.. pins.Where(f => tiles.ContainsKey(f.ItemId)).Select(f => new FavouriteTile(tiles[f.ItemId], f.Colour, f.Page))];
    }

    private async Task<Result> SaveFavouriteAsync(long companyId, long? terminalId, long? userId, long itemId, int sortOrder, int page, string? colour, bool remove, CancellationToken ct)
    {
        PosFavourite? existing = await _db.PosFavourites.FirstOrDefaultAsync(f => f.CompanyId == companyId && f.TerminalId == terminalId && f.UserId == userId && f.ItemId == itemId, ct)
            .ConfigureAwait(false);
        if (remove)
        {
            if (existing is not null)
            {
                _db.PosFavourites.Remove(existing);
            }
        }
        else if (existing is null)
        {
            _db.PosFavourites.Add(new PosFavourite(companyId, terminalId, itemId, sortOrder) { UserId = userId, Page = page, Colour = colour });
        }
        else
        {
            existing.SortOrder = sortOrder;
            existing.Page = page;
            existing.Colour = colour;
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PosCategory>> CategoriesAsync(long companyId, string? parentCode, CancellationToken ct = default)
    {
        string? parent = string.IsNullOrWhiteSpace(parentCode) ? null : parentCode.Trim().ToUpperInvariant();
        List<ItemCategory> all = await _db.ItemCategories.AsNoTracking().Where(c => c.CompanyId == companyId && c.IsActive).ToListAsync(ct).ConfigureAwait(false);
        return [.. all.Where(c => c.ParentCode == parent).OrderBy(c => c.Name)
            .Select(c => new PosCategory(c.Code, c.Name, all.Any(child => child.ParentCode == c.Code)))];
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PosTile>> CategoryTilesAsync(long companyId, string categoryCode, CancellationToken ct = default)
    {
        string root = (categoryCode ?? string.Empty).Trim().ToUpperInvariant();
        List<ItemCategory> all = await _db.ItemCategories.AsNoTracking().Where(c => c.CompanyId == companyId).ToListAsync(ct).ConfigureAwait(false);

        // The category and everything under it; items name their category by code or by name.
        HashSet<string> codes = [root];
        for (bool grew = true; grew;)
        {
            grew = false;
            foreach (ItemCategory child in all.Where(c => c.ParentCode is string p && codes.Contains(p) && !codes.Contains(c.Code)))
            {
                grew |= codes.Add(child.Code);
            }
        }

        List<string> keys = [.. codes, .. all.Where(c => codes.Contains(c.Code)).Select(c => c.Name.ToUpperInvariant())];
        return await PriceAsync(companyId, await Sellable(companyId).Where(i => i.Category != null && keys.Contains(i.Category.ToUpper())).OrderBy(i => i.Description).ToListAsync(ct)
            .ConfigureAwait(false), ct).ConfigureAwait(false);
    }

    private IQueryable<Item> Sellable(long companyId)
        => _db.Items.AsNoTracking().Where(i => i.CompanyId == companyId && i.IsActive && i.RevenueAccountCode != null && i.RevenueAccountCode != string.Empty);

    // Tiles for the items that have a price of one on the default list (explicit, or cost-plus).
    private async Task<IReadOnlyList<PosTile>> PriceAsync(long companyId, List<Item> items, CancellationToken ct)
    {
        PriceList? list = await _db.PriceLists.AsNoTracking().Include(p => p.Lines)
            .FirstOrDefaultAsync(p => p.CompanyId == companyId && p.IsDefault && p.IsActive, ct).ConfigureAwait(false);
        if (list is null || items.Count == 0)
        {
            return [];
        }

        // The price of one: the explicit line with the largest break not above one.
        Dictionary<long, decimal> explicitPrices = list.Lines.Where(l => l.MinimumQuantity <= 1m).GroupBy(l => l.ItemId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(l => l.MinimumQuantity).First().UnitPrice);
        List<PosTile> tiles = [];
        int lookups = 0;
        foreach (Item item in items)
        {
            decimal? price = explicitPrices.TryGetValue(item.Id, out decimal listed) ? listed : null;
            if (price is null && list.CostPlusBasis != CostPlusBasis.None && lookups++ < MaxCostPlusLookups)
            {
                Result<ResolvedPrice> resolved = await _pricing.PriceAsync(companyId, null, item.Id, 1m, ct).ConfigureAwait(false);
                price = resolved.IsSuccess ? resolved.Value.UnitPrice : null;
            }

            if (price is decimal unitPrice)
            {
                tiles.Add(new PosTile(item.Id, item.Code, item.ShortDescription ?? item.Description, string.IsNullOrWhiteSpace(item.Category) ? "General" : item.Category!, unitPrice,
                    item.RevenueAccountCode!, item.TaxCode, item.Barcode, item.Plu));
            }
        }

        return tiles;
    }
}
